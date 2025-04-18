using UndercutF1.Data;

namespace UndercutF1.Console;

public class IncreaseDelayInputHandler(IDateTimeProvider dateTimeProvider) : IInputHandler
{
    public bool IsEnabled => true;

    public Screen[] ApplicableScreens =>
        [
            Screen.ManageSession,
            Screen.RaceControl,
            Screen.DriverTracker,
            Screen.TimingTower,
            Screen.TimingHistory,
            Screen.TyreStints,
        ];

    public ConsoleKey[] Keys => [ConsoleKey.N, ConsoleKey.M];

    public string Description => "Delay";

    public int Sort => 22;

    public Task ExecuteAsync(
        ConsoleKeyInfo consoleKeyInfo,
        CancellationToken cancellationToken = default
    )
    {
        var changeBy = consoleKeyInfo.Key == ConsoleKey.M ? 5 : -5;
        if (consoleKeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            changeBy *= 6;
        }

        dateTimeProvider.Delay += TimeSpan.FromSeconds(changeBy);

        if (dateTimeProvider.Delay < TimeSpan.Zero)
            dateTimeProvider.Delay = TimeSpan.Zero;

        return Task.CompletedTask;
    }
}
