namespace MeshcomWebDesk.Models;

/// <summary>
/// A user-defined bot command loaded from <c>appsettings.json</c>.
/// </summary>
public class BotCommandEntry
{
    /// <summary>Command name without the -- prefix, e.g. "info".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Response text. Supports the same {variable} placeholders as AutoReplyText
    /// (e.g. {version}, {mycall}, {callsign}, {rssi}, {time}, {date}).
    /// </summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>Short description shown in --help output.</summary>
    public string Description { get; set; } = string.Empty;
}
