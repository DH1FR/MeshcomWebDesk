using System.Text;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services;

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
    private readonly LanguageService _lang;
    private MeshcomSettings _settings;

    public BotCommandService(IEnumerable<IBotCommand> builtinCommands, IOptionsMonitor<MeshcomSettings> settings, LanguageService lang)
    {
        _builtinCommands = builtinCommands.ToList();
        _lang            = lang;
        _settings        = settings.CurrentValue;
        settings.OnChange(s => _settings = s);
    }

    /// <summary>
    /// Returns true when <paramref name="text"/> is a bot command.
    /// Accepts both <c>--</c> (two hyphens) and <c>\u2014</c> (em dash, typed automatically
    /// by some MeshCom clients / mobile keyboards instead of --).
    /// Also accepts a bare <c>ping</c> keyword (case-insensitive) for compatibility with
    /// clients that send plain "ping" without a command prefix.
    /// </summary>
    public static bool IsCommand(string? text) =>
        text != null &&
        (text.StartsWith("--", StringComparison.Ordinal) ||
         text.StartsWith('\u2014') ||
         text.Trim().Equals("ping", StringComparison.OrdinalIgnoreCase));

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
    public async Task<string> ExecuteAsync(string text, string senderCallsign, MeshcomMessage? context = null)
    {
        // Normalize bare "ping" (case-insensitive) to "--ping" so it is dispatched like any other command
        if (text.Trim().Equals("ping", StringComparison.OrdinalIgnoreCase))
            text = "--ping";

        // Strip the leading "--" or "—" (em dash U+2014, sent by some MeshCom clients / mobile keyboards)
        string body;
        if (text.StartsWith("--", StringComparison.Ordinal))
            body = text.Length > 2 ? text[2..] : string.Empty;
        else if (text.Length > 0 && text[0] == '\u2014')
            body = text.Length > 1 ? text[1..] : string.Empty;
        else
            body = text;

        var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name  = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        var args  = parts.Length > 1 ? parts[1..] : [];

        if (string.IsNullOrEmpty(name) || name == "help")
            return BuildHelp();

        var cmd = AllCommands.FirstOrDefault(
            c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        return cmd is null
            ? $"{_lang.T("Unbekannter Befehl", "Unknown command", "Comando sconosciuto", "Comando desconocido")}: --{name}. {_lang.T("Mit --help erhältst Du alle Befehle.", "Use --help to see all commands.", "Usa --help per vedere tutti i comandi.", "Usa --help para ver todos los comandos.")}"
            : await cmd.ExecuteAsync(args, senderCallsign, context);
    }

    private string BuildHelp()
    {
        var sb = new StringBuilder($"{_lang.T("Befehle", "Commands", "Comandi", "Comandos")}: --help");
        foreach (var cmd in AllCommands)
            sb.Append($", --{cmd.Name}");
        return sb.ToString();
    }
}
