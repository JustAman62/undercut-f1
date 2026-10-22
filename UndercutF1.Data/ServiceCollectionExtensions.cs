using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UndercutF1.Data;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services required to support processing live timing data.
    /// A <see cref="ILiveTimingClient"/> is registered to support ingesting live timing data from the live feed.
    /// A <see cref="IJsonTimingClient"/> is registered to support ingesting previously recorded timing data from JSON files.
    /// A <see cref="INotifyService"/> is registered to support sending notifications for when interesting data changes happen.
    /// </summary>
    /// <param name="collection">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configuration">The <see cref="IConfiguration"/> to bind <see cref="LiveTimingOptions"/></param>
    /// <returns>The same <paramref name="collection"/> to support chaining.</returns>
    public static IServiceCollection AddLiveTiming(
        this IServiceCollection collection,
        IConfiguration configuration
    )
    {
        collection
            .Configure<LiveTimingOptions>(configuration)
            .AddLiveTimingClient()
            .AddLiveTimingProcessors()
            .AddSingleton<INotifyService, NotifyService>()
            .AddSingleton<ITranscriptionProvider, TranscriptionProvider>()
            .AddSingleton<Formula1Account>()
            .AddSingleton<IDataImporter, DataImporter>();

        collection.AddHttpClient(
            "Default",
            (serviceProvider, options) =>
            {
                options.DefaultRequestHeaders.Add(
                    "User-Agent",
                    $"undercut-f1/{ThisAssembly.AssemblyInformationalVersion}"
                );
                var formula1Account = serviceProvider.GetRequiredService<Formula1Account>();
                options.DefaultRequestHeaders.Add(
                    "Authorization",
                    $"Bearer {formula1Account.AccessToken}"
                );
            }
        );

        collection.AddHttpClient(
            "Proxy",
            options =>
            {
                options.DefaultRequestHeaders.Add(
                    "User-Agent",
                    $"undercut-f1/{ThisAssembly.AssemblyInformationalVersion}"
                );
                options.DefaultRequestHeaders.ConnectionClose = true;
                options.BaseAddress = new Uri("https://undercutf1.amandhoot.com");
            }
        );

        return collection;
    }
}
