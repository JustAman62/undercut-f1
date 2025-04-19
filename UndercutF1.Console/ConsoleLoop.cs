using System.Buffers;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Advanced;
using Spectre.Console.Rendering;

namespace UndercutF1.Console;

public class ConsoleLoop(
    State state,
    IEnumerable<IDisplay> displays,
    IEnumerable<IInputHandler> inputHandlers,
    IHostApplicationLifetime hostApplicationLifetime,
    TerminalInfoProvider terminalInfo,
    ILogger<ConsoleLoop> logger
) : BackgroundService
{
    private const long TargetFrameTimeMs = 100;
    private const byte ESC = 27; //0x1B
    private const byte CSI = 91; //0x5B [
    private const byte ARG_SEP = 59; //0x3B ;
    private const byte FE_START = 79; //0x4F

    private string _previousDraw = string.Empty;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Immediately yield to ensure all the other hosted services start as expected
        await Task.Yield();

        await SetupTerminalAsync(cancellationToken);

        var contentPanel = new Panel("Undercut F1").Expand().RoundedBorder() as IRenderable;
        var layout = new Layout("Root").SplitRows(
            new Layout("Content", contentPanel),
            new Layout("Footer")
        );
        layout["Footer"].Size = 1;

        var stopwatch = Stopwatch.StartNew();
        while (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Restart();

            logger.LogDebug("Buffer setup");

            await SetupBufferAsync(cancellationToken);
            logger.LogDebug("handle inputs");
            await HandleInputsAsync(cancellationToken);

            if (state.CurrentScreen == Screen.Shutdown)
            {
                await StopAsync(cancellationToken);
                return;
            }

            var display = displays.SingleOrDefault(x => x.Screen == state.CurrentScreen);

            try
            {
                if (terminalInfo.IsSynchronizedOutputSupported.Value)
                {
                    await Terminal.OutAsync(
                        TerminalGraphics.BeginSynchronizedUpdate(),
                        cancellationToken
                    );
                }

                contentPanel = display is not null
                    ? await display.GetContentAsync()
                    : new Panel($"Unknown Display Selected: {state.CurrentScreen}").Expand();
                layout["Content"].Update(contentPanel);

                UpdateInputFooter(layout);

                var output = AnsiConsole.Console.ToAnsi(layout).Replace("\n", "");
                // For some reason ToAnsi doesn't use Environment.NewLine correctly.
                // On Windows, it outputs only CR's and no LF, so only a single line ends up output in the terminal
                // So now we manually deal with this
                if (OperatingSystem.IsWindows())
                {
                    output = output.Replace("\r", Environment.NewLine);
                }

                if (_previousDraw != output)
                {
                    await Terminal.OutAsync(output, cancellationToken);
                    _previousDraw = output;
                }

                if (display is not null)
                {
                    await display.PostContentDrawAsync();
                }
            }
            catch (Exception ex)
            {
                await Terminal.ErrorLineAsync(
                    $"Exception whilst rendering screen {state.CurrentScreen}",
                    cancellationToken
                );
                await Terminal.ErrorAsync(ex, cancellationToken);
                logger.LogError(ex, "Error rendering screen: {CurrentScreen}", state.CurrentScreen);
            }

            if (terminalInfo.IsSynchronizedOutputSupported.Value)
            {
                await Terminal.OutAsync(
                    TerminalGraphics.EndSynchronizedUpdate(),
                    cancellationToken
                );
            }

            stopwatch.Stop();
            var timeToDelay = TargetFrameTimeMs - stopwatch.ElapsedMilliseconds;
            if (timeToDelay > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(timeToDelay), cancellationToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!ExecuteTask?.IsCompleted ?? false)
        {
            // Don't log if the token has already been cancelled, as that means we've already stopped.
            await Terminal.OutLineAsync("Exiting undercutf1...", CancellationToken.None);
            logger.LogInformation("ConsoleLoop Stopping.");
        }
        await Terminal.OutAsync(
            ControlSequences.ClearScreen(ClearMode.Full),
            CancellationToken.None
        );
        await Terminal.OutAsync(ControlSequences.SetCursorVisibility(true), CancellationToken.None);
        await Terminal.OutAsync(
            ControlSequences.SetScreenBuffer(ScreenBuffer.Main),
            CancellationToken.None
        );
        Terminal.DisableRawMode();
        await base.StopAsync(cancellationToken);
        hostApplicationLifetime.StopApplication();
    }

    private static async Task SetupTerminalAsync(CancellationToken cancellationToken)
    {
        Terminal.EnableRawMode();
        await Terminal.OutAsync(
            ControlSequences.SetScreenBuffer(ScreenBuffer.Alternate),
            cancellationToken
        );
        await Terminal.OutAsync(ControlSequences.SetCursorVisibility(false), cancellationToken);
        await Terminal.OutAsync(ControlSequences.MoveCursorTo(0, 0), cancellationToken);
        await Terminal.OutAsync(ControlSequences.ClearScreen(ClearMode.Full), cancellationToken);
    }

    private static async Task SetupBufferAsync(CancellationToken cancellationToken) =>
        await Terminal.OutAsync(ControlSequences.MoveCursorTo(0, 0), cancellationToken);

    private void UpdateInputFooter(Layout layout)
    {
        var commandDescriptions = inputHandlers
            .Where(x => x.IsEnabled && x.ApplicableScreens.Contains(state.CurrentScreen))
            .OrderBy(x => x.Sort)
            .Select(x => $"[{x.DisplayKeys.ToDisplayCharacters()}] {x.Description}");

        var columns = new Columns(commandDescriptions.Select(x => new Text(x)));
        columns.Collapse();
        layout["Footer"].Update(columns);
    }

    private async Task HandleInputsAsync(CancellationToken cancellationToken = default)
    {
        var inputBuffer = ArrayPool<byte>.Shared.Rent(8);
        Array.Fill<byte>(inputBuffer, 0);
        try
        {
            // wait for a very short amount of time to read input
            var cts = new CancellationTokenSource(millisecondsDelay: 50);

            await Terminal.ReadAsync(inputBuffer, cts.Token);
            logger.LogDebug("Read in input: {Input}", string.Join(',', inputBuffer));

            if (TryParseRawInput(inputBuffer, out var keyChar, out var consoleKey))
            {
                var tasks = inputHandlers
                    .Where(x =>
                        x.IsEnabled
                        && x.Keys.Contains(consoleKey)
                        && (
                            x.ApplicableScreens is null
                            || x.ApplicableScreens.Contains(state.CurrentScreen)
                        )
                    )
                    .Select(x =>
                        x.ExecuteAsync(
                            new ConsoleKeyInfo(
                                keyChar,
                                consoleKey,
                                shift: char.IsUpper(keyChar),
                                alt: false,
                                control: false
                            ),
                            cancellationToken
                        )
                    );
                await Task.WhenAll(tasks);
            }
        }
        catch (OperationCanceledException)
        {
            // No input to read, so skip
            return;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(inputBuffer);
        }
    }

    /// <summary>
    /// Parses raw input from the console in to the appropriate console character.
    /// Intended to parse control sequences (like those for arrow keys) in to the relevant console character.
    /// </summary>
    /// <remarks>
    /// See https://gist.github.com/fnky/458719343aabd01cfb17a3a4f7296797 for escape code reference.
    /// </remarks>
    /// <param name="bytes">The bytes from the input to parse</param>
    /// <returns>
    /// A tuple of (keyChar, consoleKey) if the input can be parsed.
    /// <c>null</c> if the input is not a simple character,
    /// and should be treated as an actual escape sequence.
    /// </returns>
    private bool TryParseRawInput(byte[] bytes, out char keyChar, out ConsoleKey consoleKey)
    {
        switch (bytes)
        {
            case [ESC, CSI, ..]: // An ANSI escape sequence starting with a CSI (Control Sequence Introducer)
                switch (bytes[2..])
                {
                    // Keyboard strings
                    // these are mappings from keyboard presses (like shift+arrow_)
                    case [49, ARG_SEP, 50, var key, ..]:
                        keyChar = (char)key;
                        consoleKey = key switch
                        {
                            68 => ConsoleKey.LeftArrow,
                            65 => ConsoleKey.UpArrow,
                            66 => ConsoleKey.DownArrow,
                            67 => ConsoleKey.RightArrow,
                            _ => default,
                        };
                        if (consoleKey == default)
                        {
                            logger.LogInformation(
                                "Unknown CSI keyboard string: {Seq}",
                                string.Join('|', bytes[2..])
                            );
                            return false;
                        }
                        return true;
                }
                logger.LogInformation("Unknown CSI sequence: {Seq}", string.Join('|', bytes[2..]));
                break;
            case [ESC, FE_START, var key, ..]: // An escape sequence for terminal cursor control via Fe escape codes
                keyChar = default;
                consoleKey = key switch
                {
                    68 => ConsoleKey.LeftArrow,
                    65 => ConsoleKey.UpArrow,
                    66 => ConsoleKey.DownArrow,
                    67 => ConsoleKey.RightArrow,
                    _ => default,
                };
                if (consoleKey == default)
                {
                    logger.LogInformation(
                        "Unknown FE escape sequence: {Seq}",
                        string.Join('|', bytes[2..])
                    );
                    return false;
                }
                return true;
            case [ESC, 0, ..]: // Just the escape key
                keyChar = (char)ESC;
                consoleKey = ConsoleKey.Escape;
                return true;
            case [ESC, ..]:
                logger.LogInformation("Unknown esc sequence: {Seq}", string.Join('|', bytes[1..]));
                break;
            case [var key, ..]: // Just a normal key press
                keyChar = (char)key;
                consoleKey = (ConsoleKey)char.ToUpperInvariant(keyChar);
                return true;
            default:
                logger.LogInformation("Unknown input: {Input}", string.Join('|', bytes));
                break;
        }
        keyChar = default;
        consoleKey = default;
        return false;
    }
}
