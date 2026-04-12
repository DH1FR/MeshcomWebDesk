using System.Text;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>
/// Dispatches incoming bot commands (messages starting with <c>--</c>) to the correct
/// <see cref="IBotCommand"/> implementation.
/// Built-in commands are injected via DI; user-defined commands are loaded live from
/// <see cref="MeshcomSettings.BotCommands"/> and support hot-reload via
/// <see cref="IOptionsMonitor{TOptions}"/>.
/// </summary>
public class BotCommandService
{
    private readonly IReadOnlyList<IBotCommand> _builtinCommands;
    private MeshcomSettings _settings;

    public BotCommandService(IEnumerable<IBotCommand> builtinCommands, IOptionsMonitor<MeshcomSettings> settings)
    {
        _builtinCommands = builtinCommands.ToList();
        _settings        = settings.CurrentValue;
        settings.OnChange(s => _settings = s);
    }

    /// <summary>Returns true when <paramref name="text"/> is a bot command (starts with --).</summary>
    public static bool IsCommand(string? text) =>
        text != null && text.StartsWith("--", StringComparison.Ordinal);

    /// <summary>
    /// All currently active commands: built-in (DI-registered) plus user-defined (from config).
    /// Config-based commands are re-read on every call, so hot-reload is automatic.
    /// </summary>
    public IEnumerable<IBotCommand> AllCommands =>
        _builtinCommands.Concat(
            _settings.BotCommands
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Select(e => new ConfiguredBotCommand(e)));

    /// <summary>
    /// Parses and executes a bot command. Returns the reply text.
    /// The reply may contain {variable} placeholders; callers are responsible for expanding them.
    /// </summary>
    public async Task<string> ExecuteAsync(string text, string senderCallsign)
    {
        // Strip the leading "--" and split into name + optional arguments
        var body  = text.Length > 2 ? text[2..] : string.Empty;
        var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name  = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        var args  = parts.Length > 1 ? parts[1..] : [];

        if (string.IsNullOrEmpty(name) || name == "help")
            return BuildHelp();

        var cmd = AllCommands.FirstOrDefault(
            c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        return cmd is null
            ? $"Unbekannter Befehl: --{name}. Mit --help erhältst Du alle Befehle."
            : await cmd.ExecuteAsync(args, senderCallsign);
    }

    private string BuildHelp()
    {
        var sb = new StringBuilder("Befehle: --help");
        foreach (var cmd in AllCommands)
            sb.Append($", --{cmd.Name}");

        // Truncate to MeshCom 149-char wire limit
        const int MaxLength = 149;
        return sb.Length <= MaxLength ? sb.ToString() : sb.ToString()[..MaxLength];
    }
}
