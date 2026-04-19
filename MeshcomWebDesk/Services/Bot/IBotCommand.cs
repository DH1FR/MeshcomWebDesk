using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>
/// Represents a single bot command that can be executed via an incoming direct message
/// starting with <c>--</c>.
/// </summary>
public interface IBotCommand
{
    /// <summary>Command name without the -- prefix, e.g. "version".</summary>
    string Name { get; }

    /// <summary>Short description shown in --help output.</summary>
    string Description { get; }

    /// <summary>
    /// Executes the command and returns the reply text.
    /// The return value may contain {variable} placeholders which will be expanded
    /// by <see cref="MeshcomUdpService"/> before sending.
    /// </summary>
    Task<string> ExecuteAsync(string[] args, string senderCallsign);

    /// <summary>
    /// Executes the command with optional message context (RSSI, relay path, timestamp).
    /// Default implementation delegates to <see cref="ExecuteAsync(string[], string)"/>.
    /// Override to make use of the full incoming message metadata.
    /// </summary>
    Task<string> ExecuteAsync(string[] args, string senderCallsign, MeshcomMessage? context)
        => ExecuteAsync(args, senderCallsign);
}
