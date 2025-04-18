using Microsoft.AspNet.SignalR.Client;

namespace UndercutF1.Data;

/// <summary>
/// A client which interacts with the SignalR data stream provided by F1.
/// </summary>
public interface ILiveTimingClient
{
    HubConnection? Connection { get; }

    /// <summary>
    /// Starts the timing client, which establishes a connection to the real F1 live timing data source.
    /// </summary>
    Task StartAsync();
}
