using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Provides UI-language switching between German ("de"), English ("en"), Italian ("it") and Spanish ("es").
/// Inject this singleton into Blazor components and call T(de, en, it, es) for inline translations.
/// Subscribe to <see cref="OnChange"/> and call StateHasChanged() to re-render on language switch.
/// </summary>
public class LanguageService
{
    private string _lang;

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

    /// <summary>Currently active language code ("de" or "en").</summary>
    public string Current => _lang;

    /// <summary>Returns the string matching the active language; falls back to <paramref name="de"/> if no translation is provided.</summary>
    public string T(string de, string en, string? it = null, string? es = null) =>
        _lang switch
        {
            "en" => en,
            "it" => it ?? de,
            "es" => es ?? de,
            _    => de
        };

    private static string Normalize(string? lang) =>
        lang?.ToLowerInvariant() switch
        {
            "en" => "en",
            "it" => "it",
            "es" => "es",
            _    => "de"
        };
}
