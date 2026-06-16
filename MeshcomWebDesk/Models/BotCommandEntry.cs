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
    /// Ignored when <see cref="IsExternal"/> is true.
    /// </summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>Short description shown in --help output.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// When true, the command is executed by launching an external process from
    /// <see cref="MeshcomSettings.BotExternalCommandsPath"/> instead of returning
    /// the static <see cref="Response"/> text.
    /// </summary>
    public bool IsExternal { get; set; } = false;

    /// <summary>
    /// File name (without path) of the external process to launch, e.g. "weather.ps1".
    /// The file must reside in <see cref="MeshcomSettings.BotExternalCommandsPath"/>.
    /// Only evaluated when <see cref="IsExternal"/> is true.
    /// </summary>
    public string ExternalFileName { get; set; } = string.Empty;

    /// <summary>
    /// Maximum time in seconds to wait for the external process to complete.
    /// Defaults to 10 seconds. Only evaluated when <see cref="IsExternal"/> is true.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;
}
