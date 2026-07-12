using System.Text;
using System.Text.RegularExpressions;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>One themeable CSS custom property (without the leading "--").</summary>
/// <param name="Key">Token name, e.g. "bg-base" → CSS variable "--bg-base".</param>
/// <param name="Group">Editor group id (see <see cref="ThemeCatalog.Groups"/>).</param>
/// <param name="De">German editor label.</param>
/// <param name="En">English editor label.</param>
/// <param name="Default">Dark-theme default value (the classic WebDesk look).</param>
public sealed record ThemeToken(string Key, string Group, string De, string En, string Default);

/// <summary>Editor group of theme tokens.</summary>
public sealed record ThemeTokenGroup(string Id, string De, string En);

/// <summary>Built-in theme preset. Colors contains overrides on top of the dark defaults.</summary>
public sealed record ThemePreset(string Id, string De, string En, Dictionary<string, string> Colors);

/// <summary>
/// Central registry of all themeable CSS tokens, the built-in presets and the
/// logic to resolve a theme selection into a ":root { … }" CSS block.
/// The dark defaults reproduce the classic hard-coded WebDesk colours exactly,
/// so preset "dark" renders pixel-identical to previous releases.
/// </summary>
public static class ThemeCatalog
{
    private static readonly Regex HexColor =
        new(@"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    public static readonly IReadOnlyList<ThemeTokenGroup> Groups =
    [
        new("bg",     "Grundflächen",           "Backgrounds"),
        new("accent", "Akzente & Struktur",     "Accents & structure"),
        new("btn",    "Buttons",                "Buttons"),
        new("text",   "Text",                   "Text"),
        new("link",   "Links & Rufzeichen",     "Links & callsigns"),
        new("status", "Statusfarben",           "Status colours"),
        new("msg",    "Nachrichten & Hinweise", "Messages & notices"),
        new("mon",    "Monitor & Listen",       "Monitor & lists"),
    ];

    public static readonly IReadOnlyList<ThemeToken> Tokens =
    [
        // ── Backgrounds ──────────────────────────────────────────────────
        new("bg-base",      "bg", "Grundfläche / Chat",        "Base / chat background",   "#1a1a2e"),
        new("bg-panel",     "bg", "Panels & Tabs",             "Panels & tabs",            "#16213e"),
        new("bg-deep",      "bg", "Monitor & Statusleiste",    "Monitor & status bar",     "#0d1117"),
        new("bg-elevated",  "bg", "Karten & Kopfzeilen",       "Cards & headers",          "#161b22"),
        new("bg-hover",     "bg", "Hover-Fläche",              "Hover surface",            "#1c2128"),
        new("bg-input",     "bg", "Eingabefelder",             "Input fields",             "#1a1a2e"),
        new("surface-alt",  "bg", "Sekundärflächen & Linien",  "Secondary surfaces & lines", "#21262d"),
        new("border-strong","bg", "Rahmen (stark)",            "Borders (strong)",         "#30363d"),

        // ── Accents / structure ──────────────────────────────────────────
        new("accent",           "accent", "Akzent (Struktur)",       "Accent (structure)",       "#0f3460"),
        new("accent-hover",     "accent", "Akzent Hover",            "Accent hover",             "#1a4a8a"),
        new("accent-strong",    "accent", "Akzent kräftig",          "Accent strong",            "#1a5a9a"),
        new("accent-active",    "accent", "Akzent gedrückt",         "Accent pressed",           "#154080"),
        new("accent-soft",      "accent", "Akzent (Sekundärbutton)", "Accent (secondary button)","#1a4a6e"),
        new("accent-soft-hover","accent", "Sekundärbutton Hover",    "Secondary button hover",   "#1e6091"),

        // ── Buttons ──────────────────────────────────────────────────────
        new("btn-bg",         "btn", "Button-Hintergrund",   "Button background",   "#0f3460"),
        new("btn-text",       "btn", "Button-Text",          "Button text",         "#a0c4ff"),
        new("btn-border",     "btn", "Button-Rahmen",        "Button border",       "#1a4a8a"),
        new("btn-hover-bg",   "btn", "Button Hover",         "Button hover",        "#1a5a9a"),
        new("btn-hover-text", "btn", "Button Hover-Text",    "Button hover text",   "#ffffff"),
        new("primary",        "btn", "Primärbutton (Senden)","Primary button (send)","#2d6a4f"),
        new("primary-hover",  "btn", "Primärbutton Hover",   "Primary button hover", "#40916c"),
        new("btn-warn-bg",    "btn", "Warn-Button",          "Warning button",       "#3a2800"),
        new("btn-warn-hover", "btn", "Warn-Button Hover",    "Warning button hover", "#5a3e00"),
        new("danger-bg",      "btn", "Gefahr-Button",        "Danger button",        "#3a0d0d"),

        // ── Text ─────────────────────────────────────────────────────────
        new("text",           "text", "Text",                "Text",                "#e0e0e0"),
        new("text-soft",      "text", "Text (weich)",        "Text (soft)",         "#c9d1d9"),
        new("text-muted",     "text", "Text (gedämpft)",     "Text (muted)",        "#8b949e"),
        new("text-faint",     "text", "Text (schwach)",      "Text (faint)",        "#6c757d"),
        new("text-dim",       "text", "Text (sehr schwach)", "Text (dim)",          "#6e7681"),
        new("text-on-accent", "text", "Text auf Akzent",     "Text on accent",      "#ffffff"),
        new("text-emphasis",  "text", "Text (hervorgehoben)","Text (emphasis)",     "#ffffff"),

        // ── Links ────────────────────────────────────────────────────────
        new("link",       "link", "Link",             "Link",              "#58a6ff"),
        new("link-soft",  "link", "Rufzeichen/Titel", "Callsigns/titles",  "#a0c4ff"),
        new("link-hover", "link", "Link Hover",       "Link hover",        "#79c0ff"),

        // ── Status ───────────────────────────────────────────────────────
        new("ok",           "status", "OK / Grün",         "OK / green",        "#3fb950"),
        new("ok-bright",    "status", "OK hell",           "OK bright",         "#56d364"),
        new("ok-soft",      "status", "OK weich",          "OK soft",           "#7ee787"),
        new("ok-strong",    "status", "OK kräftig",        "OK strong",         "#238636"),
        new("warn",         "status", "Warnung / Gelb",    "Warning / yellow",  "#e3b341"),
        new("warn-strong",  "status", "Warnung kräftig",   "Warning strong",    "#d29922"),
        new("error",        "status", "Fehler / Rot",      "Error / red",       "#f85149"),
        new("error-soft",   "status", "Fehler weich",      "Error soft",        "#ff7b72"),
        new("error-strong", "status", "Fehler kräftig",    "Error strong",      "#da3633"),
        new("error-bright", "status", "Fehler hell",       "Error bright",      "#ff6b6b"),
        new("error-hover",  "status", "Fehler Hover",      "Error hover",       "#ff3333"),

        // ── Messages / notices ───────────────────────────────────────────
        new("msg-out-bg",        "msg", "Eigene Nachrichten",     "Own messages",          "#1b4332"),
        new("panel-ok-bg",       "msg", "Erfolgs-Hinweis",        "Success notice",        "#0d2b1e"),
        new("panel-err-bg",      "msg", "Fehler-Hinweis",         "Error notice",          "#2b0d0d"),
        new("panel-err-border",  "msg", "Fehler-Hinweis Rahmen",  "Error notice border",   "#6e2020"),
        new("panel-warn-bg",     "msg", "Warn-Hinweis",           "Warning notice",        "#2b2400"),
        new("panel-warn-border", "msg", "Warn-Hinweis Rahmen",    "Warning notice border", "#6e5f00"),

        // ── Monitor / lists ──────────────────────────────────────────────
        new("mon-text",           "mon", "Monitor-Text",           "Monitor text",           "#e6edf3"),
        new("mon-text-soft",      "mon", "Monitor-Text (weich)",   "Monitor text (soft)",    "#cdd9e5"),
        new("mon-row-tx-bg",      "mon", "TX-Zeile",               "TX row",                 "#0d2018"),
        new("mon-row-tx-hover",   "mon", "TX-Zeile Hover",         "TX row hover",           "#142b1f"),
        new("mon-row-pos-bg",     "mon", "Positions-Zeile",        "Position row",           "#0d1c22"),
        new("mon-row-pos-hover",  "mon", "Positions-Zeile Hover",  "Position row hover",     "#0f252e"),
        new("mon-row-tele-bg",    "mon", "Telemetrie-Zeile",       "Telemetry row",          "#1a0d2e"),
        new("mon-row-tele-hover", "mon", "Telemetrie-Zeile Hover", "Telemetry row hover",    "#220f38"),
        new("mh-gateway-bg",      "mon", "Gateway-Zeile (MH)",     "Gateway row (MH)",       "#0d2b1a"),
        new("mh-gateway-hover",   "mon", "Gateway-Zeile Hover",    "Gateway row hover",      "#143622"),
    ];

    /// <summary>
    /// Built-in presets. Colors contains only the overrides relative to the dark
    /// defaults; "dark" therefore has an empty override map.
    /// </summary>
    public static readonly IReadOnlyList<ThemePreset> Presets =
    [
        new("dark", "MeshCom Dark (Standard)", "MeshCom Dark (default)", new()),

        new("midnight", "Mitternacht (OLED)", "Midnight (OLED)", new()
        {
            ["bg-base"]      = "#000000",
            ["bg-panel"]     = "#0b0e14",
            ["bg-deep"]      = "#000000",
            ["bg-elevated"]  = "#0a0d12",
            ["bg-hover"]     = "#11151c",
            ["bg-input"]     = "#05070a",
            ["surface-alt"]  = "#161b22",
            ["border-strong"]= "#262c36",
            ["accent"]       = "#0d2b50",
            ["accent-hover"] = "#17416f",
            ["btn-bg"]       = "#0d2b50",
            ["msg-out-bg"]   = "#12301f",
            ["mon-row-tx-bg"]   = "#081710",
            ["mon-row-pos-bg"]  = "#081319",
            ["mon-row-tele-bg"] = "#120920",
            ["mh-gateway-bg"]   = "#081f12",
        }),

        new("light", "Hell", "Light", new()
        {
            ["bg-base"]      = "#eef2f6",
            ["bg-panel"]     = "#ffffff",
            ["bg-deep"]      = "#e8ecf1",
            ["bg-elevated"]  = "#f4f6f9",
            ["bg-hover"]     = "#e2e8ee",
            ["bg-input"]     = "#ffffff",
            ["surface-alt"]  = "#d8dee5",
            ["border-strong"]= "#c8d1da",
            ["accent"]           = "#c9d6e6",
            ["accent-hover"]     = "#a9c4e2",
            ["accent-strong"]    = "#3b7dd8",
            ["accent-active"]    = "#2d6bb8",
            ["accent-soft"]      = "#3576c4",
            ["accent-soft-hover"]= "#2d6bb8",
            ["btn-bg"]        = "#e3ecf7",
            ["btn-text"]      = "#1a4f8a",
            ["btn-border"]    = "#b9cde6",
            ["btn-hover-bg"]  = "#3b7dd8",
            ["btn-hover-text"]= "#ffffff",
            ["primary"]       = "#1f883d",
            ["primary-hover"] = "#1a7f37",
            ["btn-warn-bg"]   = "#fff0c2",
            ["btn-warn-hover"]= "#f5d980",
            ["danger-bg"]     = "#ffe5e2",
            ["text"]           = "#1f2328",
            ["text-soft"]      = "#333b43",
            ["text-muted"]     = "#57606a",
            ["text-faint"]     = "#6e7781",
            ["text-dim"]       = "#768390",
            ["text-on-accent"] = "#ffffff",
            ["text-emphasis"]  = "#0d1117",
            ["link"]       = "#0969da",
            ["link-soft"]  = "#1a4f8a",
            ["link-hover"] = "#0550ae",
            ["ok"]           = "#1a7f37",
            ["ok-bright"]    = "#1f883d",
            ["ok-soft"]      = "#2da44e",
            ["ok-strong"]    = "#1f883d",
            ["warn"]         = "#9a6700",
            ["warn-strong"]  = "#7d4e00",
            ["error"]        = "#cf222e",
            ["error-soft"]   = "#d1242f",
            ["error-strong"] = "#a40e26",
            ["error-bright"] = "#d1242f",
            ["error-hover"]  = "#86061d",
            ["msg-out-bg"]        = "#d9f2e0",
            ["panel-ok-bg"]       = "#d9f2e0",
            ["panel-err-bg"]      = "#ffe5e2",
            ["panel-err-border"]  = "#f1a8a2",
            ["panel-warn-bg"]     = "#fff3c9",
            ["panel-warn-border"] = "#d9b64e",
            ["mon-text"]           = "#24292f",
            ["mon-text-soft"]      = "#57606a",
            ["mon-row-tx-bg"]      = "#e3f5e8",
            ["mon-row-tx-hover"]   = "#d4eedd",
            ["mon-row-pos-bg"]     = "#dff0f7",
            ["mon-row-pos-hover"]  = "#cfe7f2",
            ["mon-row-tele-bg"]    = "#f3e8fb",
            ["mon-row-tele-hover"] = "#ebdbf7",
            ["mh-gateway-bg"]      = "#dcf2e2",
            ["mh-gateway-hover"]   = "#c8ead3",
        }),

        new("contrast", "Hoher Kontrast", "High contrast", new()
        {
            ["bg-base"]      = "#000000",
            ["bg-panel"]     = "#000000",
            ["bg-deep"]      = "#000000",
            ["bg-elevated"]  = "#0a0a0a",
            ["bg-hover"]     = "#1a1a1a",
            ["bg-input"]     = "#000000",
            ["surface-alt"]  = "#2a2a2a",
            ["border-strong"]= "#888888",
            ["accent"]           = "#003b8e",
            ["accent-hover"]     = "#0057d2",
            ["accent-strong"]    = "#0057d2",
            ["accent-active"]    = "#0048ac",
            ["accent-soft"]      = "#003b8e",
            ["accent-soft-hover"]= "#0057d2",
            ["btn-bg"]        = "#003b8e",
            ["btn-text"]      = "#ffffff",
            ["btn-border"]    = "#66aaff",
            ["btn-hover-bg"]  = "#0057d2",
            ["btn-hover-text"]= "#ffffff",
            ["primary"]       = "#00701a",
            ["primary-hover"] = "#009a24",
            ["danger-bg"]     = "#5c0000",
            ["text"]           = "#ffffff",
            ["text-soft"]      = "#ffffff",
            ["text-muted"]     = "#c8c8c8",
            ["text-faint"]     = "#b0b0b0",
            ["text-dim"]       = "#b0b0b0",
            ["text-on-accent"] = "#ffffff",
            ["text-emphasis"]  = "#ffffff",
            ["link"]       = "#66b2ff",
            ["link-soft"]  = "#99ccff",
            ["link-hover"] = "#cce4ff",
            ["ok"]           = "#33ff66",
            ["ok-bright"]    = "#33ff66",
            ["ok-soft"]      = "#88ffaa",
            ["ok-strong"]    = "#00c840",
            ["warn"]         = "#ffd700",
            ["warn-strong"]  = "#ffc400",
            ["error"]        = "#ff5555",
            ["error-soft"]   = "#ff8080",
            ["error-strong"] = "#ff2222",
            ["error-bright"] = "#ff6666",
            ["error-hover"]  = "#ff0000",
            ["msg-out-bg"]   = "#003820",
            ["mon-text"]      = "#ffffff",
            ["mon-text-soft"] = "#e8e8e8",
        }),
    ];

    public static bool IsValidColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexColor.IsMatch(value.Trim());

    public static bool IsPreset(string id) => Presets.Any(p => p.Id == id);

    /// <summary>
    /// Resolves the active theme selection into a complete token → colour map
    /// (dark defaults + preset/custom overrides). Invalid colour values are dropped.
    /// </summary>
    public static Dictionary<string, string> Resolve(AppearanceSettings appearance)
    {
        var result = Tokens.ToDictionary(t => t.Key, t => t.Default);

        Dictionary<string, string>? overrides =
            Presets.FirstOrDefault(p => p.Id == appearance.ActiveTheme)?.Colors
            ?? appearance.CustomThemes
                .FirstOrDefault(t => string.Equals(t.Name, appearance.ActiveTheme, StringComparison.OrdinalIgnoreCase))
                ?.Colors;

        if (overrides != null)
            foreach (var (key, value) in overrides)
                if (result.ContainsKey(key) && IsValidColor(value))
                    result[key] = value.Trim();

        return result;
    }

    /// <summary>Full token map for a preset (dark defaults + preset overrides).</summary>
    public static Dictionary<string, string> ResolvePreset(string presetId)
    {
        var result = Tokens.ToDictionary(t => t.Key, t => t.Default);
        var preset = Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset != null)
            foreach (var (key, value) in preset.Colors)
                if (result.ContainsKey(key))
                    result[key] = value;
        return result;
    }

    /// <summary>Builds the ":root { … }" CSS block for the active theme.</summary>
    public static string BuildRootCss(AppearanceSettings appearance)
    {
        var colors = Resolve(appearance);
        var sb = new StringBuilder(":root{");
        foreach (var (key, value) in colors)
            sb.Append("--").Append(key).Append(':').Append(value).Append(';');
        sb.Append('}');
        return sb.ToString();
    }
}
