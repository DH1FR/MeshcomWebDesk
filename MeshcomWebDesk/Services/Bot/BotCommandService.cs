using System.Text;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>
/// Dispatches incoming bot commands (messages starting with <c>&gt;</c>) to the correct
/// <see cref="IBotCommand"/> implementation.
/// Built-in commands are injected via DI; user-defined commands are loaded live from
/// <see cref="MeshcomSettings.BotCommands"/> and support hot-reload via
/// <see cref="IOptionsMonitor{TOptions}"/>.
/// </summary>
public class BotCommandService
{
    private readonly IReadOnlyList<IBotCommand> _builtinCommands;
    private readonly ExternalProcessRunner _runner;
    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly LanguageService _lang;
    private readonly AppLicenseService _license;
    private MeshcomSettings _settings;

    public BotCommandService(
        IEnumerable<IBotCommand> builtinCommands,
        IOptionsMonitor<MeshcomSettings> settings,
        LanguageService lang,
        ExternalProcessRunner runner,
        AppLicenseService license)
    {
        _builtinCommands = builtinCommands.ToList();
        _runner          = runner;
        _settingsMonitor = settings;
        _lang            = lang;
        _license         = license;
        _settings        = settings.CurrentValue;
        settings.OnChange(s => _settings = s);
    }

    /// <summary>
    /// Returns true when <paramref name="text"/> is a bot command: it starts with <c>&gt;</c> and a letter
    /// follows immediately (<c>&gt;ping</c>, <c>&gt;version</c>).
    /// <c>--cmd</c> (the node firmware's own console commands), the em dash variant and a bare <c>ping</c>
    /// are ordinary chat text and are NOT answered: the node firmware owns the <c>--</c> namespace.
    /// </summary>
    public static bool IsCommand(string? text) =>
        text != null &&
        text.Length > 1 &&
        text[0] == '>' &&
        char.IsLetter(text[1]);

    /// <summary>
    /// All currently active commands: built-in (DI-registered) plus user-defined (from config).
    /// Config-based commands are re-read on every call, so hot-reload is automatic.
    /// </summary>
    public IEnumerable<IBotCommand> AllCommands =>
        _builtinCommands.Concat(
            _settings.BotCommands
                .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Name))
                .Select(e => e.IsExternal
                    ? (IBotCommand)new ExternalProcessCommand(e, _runner, _settingsMonitor, _license)
                    : new ConfiguredBotCommand(e)));

    /// <summary>
    /// Parses and executes a bot command. Returns the reply text.
    /// The reply may contain {variable} placeholders; callers are responsible for expanding them.
    /// </summary>
    public async Task<string> ExecuteAsync(string text, string senderCallsign, MeshcomMessage? context = null)
    {
        // Strip the leading ">" (the Settings test box also accepts a bare command name)
        var body = text.Length > 0 && text[0] == '>'
            ? (text.Length > 1 ? text[1..] : string.Empty)
            : text;

        var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name  = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        var args  = parts.Length > 1 ? parts[1..] : [];

        if (string.IsNullOrEmpty(name) || name == "help")
            return BuildHelp();

        var cmd = AllCommands.FirstOrDefault(
            c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        return cmd is null
            ? $"{_lang.T("Unbekannter Befehl", "Unknown command")}: >{name}. {_lang.T("Mit >help erhältst Du alle Befehle.", "Use >help to see all commands.")}"
            : await cmd.ExecuteAsync(args, senderCallsign, context);
    }

    private string BuildHelp()
    {
        var sb = new StringBuilder("Cmds: >help");
        foreach (var cmd in AllCommands)
            sb.Append($", >{cmd.Name}");
        return sb.ToString();
    }
}
