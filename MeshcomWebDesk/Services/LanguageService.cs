using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services;

/// <summary>A selectable UI language (code, display name, optional flag emoji).</summary>
public sealed record LanguageInfo(string Code, string Name, string Flag)
{
    public string Label => string.IsNullOrEmpty(Flag) ? Name : $"{Flag} {Name}";
}

/// <summary>
/// Provides UI-language switching between German ("de"), English ("en") and any number of
/// file-backed languages.
/// German and English live in the source (<c>T(de, en)</c>). All other languages are JSON files
/// keyed on the English source text: the built-in ones are embedded from <c>Languages/*.json</c>,
/// additional or overriding ones are read from <c>&lt;DataPath&gt;/languages/*.json</c> at startup
/// (see <see cref="Reload"/>). A file in the data folder overrides single strings of a built-in
/// language or adds a new language; missing strings fall back to the English text.
/// File format: <c>{ "code": "fr", "name": "Français", "flag": "🇫🇷", "strings": { "English": "Français" } }</c>.
/// Inject this singleton into Blazor components and call <see cref="T"/> for inline translations.
/// Subscribe to <see cref="OnChange"/> and call StateHasChanged() to re-render on language switch.
/// </summary>
public class LanguageService
{
    private static readonly Regex CodePattern = new("^[a-z]{2,3}(-[a-z0-9]{2,8})?$", RegexOptions.Compiled);

    private sealed class Loaded
    {
        public string Code = "";
        public string Name = "";
        public string Flag = "";
        public Dictionary<string, string> Strings = new(StringComparer.Ordinal);
    }

    private readonly ILogger<LanguageService> _logger;
    private readonly string _externalDir;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;

    /// <summary>English source texts requested so far (EN → DE); basis for <see cref="ExportTemplate"/>.</summary>
    private readonly ConcurrentDictionary<string, string> _seen = new(StringComparer.Ordinal);

    private volatile Dictionary<string, Loaded> _languages = new();
    private string _lang;

    public event Action? OnChange;

    public LanguageService(IOptionsMonitor<MeshcomSettings> settings, SettingsService settingsService,
                           ILogger<LanguageService> logger)
    {
        _settings = settings;
        _logger = logger;
        _externalDir = Path.Combine(settingsService.EffectiveDataPath, "languages");
        _languages = LoadAll();
        _lang = Normalize(settings.CurrentValue.Language);
        settings.OnChange(s =>
        {
            var next = Normalize(s.Language);
            if (next == _lang) return;
            _lang = next;
            OnChange?.Invoke();
        });
    }

    /// <summary>Currently active language code.</summary>
    public string Current => _lang;

    /// <summary>Language of the linked docs (docs/node-connection-xx.md exist for de, en, es, it; everything else → en).</summary>
    public string DocsLanguage => _lang is "de" or "es" or "it" ? _lang : "en";

    /// <summary>Folder scanned for additional / overriding language files.</summary>
    public string ExternalDirectory => _externalDir;

    /// <summary>All selectable languages: de, en and every loaded file-backed language.</summary>
    public IReadOnlyList<LanguageInfo> AvailableLanguages =>
        new[] { new LanguageInfo("de", "Deutsch", "🇩🇪"), new LanguageInfo("en", "English", "🇬🇧") }
            .Concat(_languages.Values.OrderBy(l => l.Code, StringComparer.Ordinal)
                .Select(l => new LanguageInfo(l.Code, l.Name, l.Flag)))
            .ToList();

    /// <summary>
    /// Returns the string for the active language.
    /// "de" → <paramref name="de"/>; "en" → <paramref name="en"/>;
    /// all other languages → lookup on <paramref name="en"/>, fallback to <paramref name="en"/>.
    /// </summary>
    public string T(string de, string en)
    {
        _seen.TryAdd(en, de);
        return _lang switch
        {
            "de" => de,
            "en" => en,
            _    => _languages.TryGetValue(_lang, out var l) && l.Strings.TryGetValue(en, out var t) ? t : en
        };
    }

    /// <summary>
    /// Like <see cref="T"/> for texts with values: <paramref name="de"/> and <paramref name="en"/> are
    /// <see cref="string.Format(string, object[])"/> templates ("{0} min ago"), so the translation key
    /// stays constant and translators can move the placeholders.
    /// </summary>
    public string TF(string de, string en, params object?[] args)
    {
        var template = T(de, en);
        try { return string.Format(template, args); }
        catch (FormatException) { return string.Format(en, args); }
    }

    /// <summary>
    /// Returns the TTS announcement string for the active language.
    /// Used for dynamic strings (with interpolated callsigns) that cannot be in the static dictionary.
    /// Languages without a dedicated variant fall back to English.
    /// </summary>
    public string TtsT(string de, string en, string fr, string it, string es) =>
        _lang switch
        {
            "de" => de,
            "en" => en,
            "fr" => fr,
            "it" => it,
            "es" => es,
            _    => en
        };

    /// <summary>Re-reads all language files (embedded + data folder) and re-renders subscribers.</summary>
    public void Reload()
    {
        _languages = LoadAll();
        _lang = Normalize(_settings.CurrentValue.Language);
        OnChange?.Invoke();
    }

    /// <summary>
    /// Builds a translation file for <paramref name="code"/> containing every English text seen so far
    /// plus all keys already present in that language; untranslated values are empty (= English fallback).
    /// </summary>
    public string ExportTemplate(string code)
    {
        _languages.TryGetValue(code, out var existing);
        var keys = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in _seen.Keys) keys[k] = existing is not null && existing.Strings.TryGetValue(k, out var v) ? v : "";
        if (existing is not null)
            foreach (var kv in existing.Strings) keys.TryAdd(kv.Key, kv.Value);

        var obj = new Dictionary<string, object>
        {
            ["code"]    = code,
            ["name"]    = existing?.Name ?? code,
            ["flag"]    = existing?.Flag ?? "",
            ["strings"] = keys,
        };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private Dictionary<string, Loaded> LoadAll()
    {
        var result = new Dictionary<string, Loaded>(StringComparer.Ordinal);

        var asm = typeof(LanguageService).Assembly;
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.Contains(".Languages.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is not null) Merge(result, stream, name);
        }

        try
        {
            if (Directory.Exists(_externalDir))
                foreach (var file in Directory.EnumerateFiles(_externalDir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                {
                    using var stream = File.OpenRead(file);
                    Merge(result, stream, file);
                }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read language files from {Dir}", _externalDir);
        }

        return result;
    }

    /// <summary>Parses one language file and merges it into <paramref name="target"/> (later files win per string).</summary>
    private void Merge(Dictionary<string, Loaded> target, Stream stream, string source)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            var code = (root.TryGetProperty("code", out var c) ? c.GetString() : null)?.Trim().ToLowerInvariant();
            if (code is null || !CodePattern.IsMatch(code) || code is "de" or "en")
            {
                _logger.LogWarning("Language file {Source} skipped: missing or invalid 'code' ('de' and 'en' are built in)", source);
                return;
            }
            if (!target.TryGetValue(code, out var lang))
                target[code] = lang = new Loaded { Code = code, Name = code };

            if (root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nm) lang.Name = nm;
            if (root.TryGetProperty("flag", out var f) && f.GetString() is { } fl) lang.Flag = fl;
            if (root.TryGetProperty("strings", out var strings) && strings.ValueKind == JsonValueKind.Object)
                foreach (var p in strings.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(p.Value.GetString()))
                        lang.Strings[p.Name] = p.Value.GetString()!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Language file {Source} skipped: invalid JSON", source);
        }
    }

    private string Normalize(string? lang)
    {
        var l = lang?.ToLowerInvariant();
        if (l == "de" || l == "en") return l;
        if (l is not null && _languages.ContainsKey(l)) return l;
        return "de";
    }
}
