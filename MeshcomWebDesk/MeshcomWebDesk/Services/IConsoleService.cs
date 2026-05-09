namespace MeshcomWebDesk.Services;

/// <summary>
/// Common abstraction for TLS console and serial console.
/// </summary>
public interface IConsoleService
{
    bool IsConnected { get; }
    List<string> Lines { get; }
    event Action OnChange;

    Task ConnectAsync();
    Task DisconnectAsync();
    Task SendLineAsync(string line);
}
