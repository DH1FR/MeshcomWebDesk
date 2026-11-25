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
    /// Returns true when <paramref name="text"/> is a bot command: it starts with <c>&gt;</c>, <c>--</c> (two hyphens)
    /// or <c>—</c> (em dash, typed by some mobile keyboards instead of <c>--</c>) and a letter follows
    /// immediately (<c>&gt;ping</c>, <c>--ping</c>, <c>&gt;version</c>). <c>--</c> is needed because some devices (e.g. the
    /// LilyGo T-Deck) have no <c>&gt;</c> key.
    /// The letter requirement keeps decoration strings like <c>---===</c> from being mistaken for commands.
    /// A bare <c>ping</c> is ordinary chat text and is NOT answered.
    /// </summary>
    public static bool IsCommand(string? text) => PrefixLength(text) > 0;

    /// <summary>Length of the command prefix (<c>&gt;</c> = 1, <c>--</c> = 2, em dash = 1), 0 when it is no command.</summary>
    private static int PrefixLength(string? text)
    {
        if (text is null) return 0;
        if (text.Length > 2 && text.StartsWith("--", StringComparison.Ordinal) && char.IsLetter(text[2])) return 2;
        if (text.Length > 1 && (text[0] == '>' || text[0] == '—') && char.IsLetter(text[1])) return 1;
        return 0;
    }

    /// <summary>The text without its command prefix (<c>&gt;ping x</c>, <c>--ping x</c> and <c>—ping x</c> all give <c>ping x</c>).</summary>
    public static string StripPrefix(string text) => text[PrefixLength(text)..];

    /// <summary>True for the bare ping command in any accepted form (<c>&gt;ping</c>, <c>--ping</c>, em dash).</summary>
    public static bool IsPing(string? text) =>
        text != null && IsCommand(text.Trim()) && StripPrefix(text.Trim()).Equals("ping", StringComparison.OrdinalIgnoreCase);

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
        // Strip the leading ">", "--" or em dash (the Settings test box also accepts a bare command name,
        // and a lone prefix without a letter is answered with the help)
        var body = IsCommand(text) ? StripPrefix(text) : text.TrimStart('>', '—', '-');

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
