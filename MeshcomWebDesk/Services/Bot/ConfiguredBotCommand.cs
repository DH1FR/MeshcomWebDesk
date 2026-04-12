using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>
/// A user-defined bot command loaded from <c>appsettings.json</c>.
/// The response text may contain {variable} placeholders expanded by <see cref="MeshcomUdpService"/>.
/// </summary>
public class ConfiguredBotCommand(BotCommandEntry entry) : IBotCommand
{
    public string Name        => entry.Name;
    public string Description => !string.IsNullOrWhiteSpace(entry.Description)
        ? entry.Description
        : entry.Name;

    public Task<string> ExecuteAsync(string[] args, string senderCallsign) =>
        Task.FromResult(entry.Response);
}
