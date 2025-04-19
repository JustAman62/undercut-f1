using InMemLogger;
using Serilog;
using Serilog.Events;
using TextCopy;
using UndercutF1.Data;

namespace UndercutF1.Console;

public static partial class CommandHandler
{
    private static WebApplicationBuilder GetBuilder(
        bool isApiEnabled = false,
        DirectoryInfo? dataDirectory = null,
        bool isVerbose = false,
        bool? notifyEnabled = null,
        bool useConsoleLogging = false
    )
    {
        var builder = WebApplication.CreateEmptyBuilder(new() { ApplicationName = "undercutf1" });

        var commandLineOpts = new Dictionary<string, string?>();
        if (isVerbose)
        {
            commandLineOpts.Add(nameof(LiveTimingOptions.Verbose), isVerbose.ToString());
        }
        if (isApiEnabled)
        {
            commandLineOpts.Add(nameof(LiveTimingOptions.ApiEnabled), isApiEnabled.ToString());
        }
        if (dataDirectory is not null)
        {
            commandLineOpts.Add(nameof(LiveTimingOptions.DataDirectory), dataDirectory?.FullName);
        }
        if (notifyEnabled is not null)
        {
            commandLineOpts.Add(nameof(LiveTimingOptions.Notify), notifyEnabled.ToString());
        }

        builder
            .Configuration.AddJsonFile(
                Path.Join(LiveTimingOptions.BaseDirectory, "config.json"),
                optional: true
            )
            .AddEnvironmentVariables("UNDERCUTF1_")
            .AddInMemoryCollection(commandLineOpts);

        var options = builder.Configuration.Get<LiveTimingOptions>() ?? new();

        var (inMemoryLogLevel, fileLogLevel) = options.Verbose
            ? (LogLevel.Trace, LogEventLevel.Verbose)
            : (LogLevel.Information, LogEventLevel.Information);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(fileLogLevel)
            .WriteTo.File(
                path: Path.Join(LiveTimingOptions.BaseDirectory, "logs/undercutf1.log"),
                rollOnFileSizeLimit: true,
                rollingInterval: RollingInterval.Hour,
                retainedFileCountLimit: 5
            )
            .CreateLogger();

        builder
            .Services.AddOptions()
            .AddLogging(configure =>
            {
                if (useConsoleLogging)
                {
                    configure
                        .ClearProviders()
                        .SetMinimumLevel(inMemoryLogLevel)
                        .AddSerilog()
                        .AddTerminal(opt =>
                        {
                            opt.SingleLine = true;
                            opt.UseColors = true;
                            opt.UseUtcTimestamp = true;
                        });
                }
                else
                {
                    configure
                        .ClearProviders()
                        .SetMinimumLevel(inMemoryLogLevel)
                        .AddInMemory()
                        .AddSerilog();
                }
            })
            .AddLiveTiming(builder.Configuration)
            .InjectClipboard();

        builder.WebHost.UseServer(new NullServer());

        return builder;
    }
}
