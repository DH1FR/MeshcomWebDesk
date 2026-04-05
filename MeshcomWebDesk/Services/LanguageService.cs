using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Provides UI-language switching between German ("de") and English ("en").
/// Inject this singleton into Blazor components and call T(de, en) for inline translations.
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

    /// <summary>Returns <paramref name="de"/> or <paramref name="en"/> depending on the active language.</summary>
    public string T(string de, string en) => _lang == "en" ? en : de;

    private static string Normalize(string? lang) =>
        lang?.ToLowerInvariant() == "en" ? "en" : "de";
}
