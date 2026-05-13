using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services.Translations;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Provides UI-language switching between German ("de"), English ("en") and any number of
/// dictionary-backed languages (currently "it", "es", "fr").
/// Inject this singleton into Blazor components and call <see cref="T"/> for inline translations.
/// Subscribe to <see cref="OnChange"/> and call StateHasChanged() to re-render on language switch.
/// Adding a new language: create Translations/Xx.cs implementing a string dictionary keyed on the
/// English source text, then register it in <see cref="_translations"/> below.
/// </summary>
public class LanguageService
{
    private string _lang;

    /// <summary>
    /// Registry of all dictionary-backed languages (all except "de" / "en").
    /// To add a new language create Translations/Xx.cs and add one line here.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> _translations = new()
    {
        { "it", It.Strings },
        { "es", Es.Strings },
        { "fr", Fr.Strings },
    };

    public event Action? OnChange;

    public LanguageService(IOptionsMonitor<MeshcomSettings> settings)
    {
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

    /// <summary>
    /// Returns the string for the active language.
    /// "de" → <paramref name="de"/>; "en" → <paramref name="en"/>;
    /// all other languages → dictionary lookup on <paramref name="en"/>, fallback to <paramref name="en"/>.
    /// </summary>
    public string T(string de, string en) =>
        _lang switch
        {
            "de" => de,
            "en" => en,
            _    => _translations.TryGetValue(_lang, out var dict) && dict.TryGetValue(en, out var t) ? t : en
        };

    private static string Normalize(string? lang)
    {
        var l = lang?.ToLowerInvariant();
        if (l == "de" || l == "en") return l;
        if (_translations.ContainsKey(l ?? "")) return l!;
        return "de";
    }
}
