namespace MeshcomWebDesk.Services;

/// <summary>
/// Abstraction over the UDP send path so that <see cref="MqttService"/> can
/// forward incoming MQTT send-commands without a circular dependency on
/// <see cref="MeshcomUdpService"/>.
/// </summary>
public interface IMeshcomSender
{
    Task SendMessageAsync(string destination, string text, string? tabKey = null);
}
