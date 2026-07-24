namespace MeshcomWebDesk.Models;

/// <summary>
/// Appearance / theming configuration.
/// <see cref="ActiveTheme"/> is either a built-in preset id ("dark", "midnight",
/// "light", "contrast") or the name of an entry in <see cref="CustomThemes"/>.
/// </summary>
public class AppearanceSettings
{
    /// <summary>Preset id or custom theme name. Default: "dark" (the classic look).</summary>
    public string ActiveTheme { get; set; } = "dark";

    /// <summary>User-defined themes. Each stores a full token → colour map.</summary>
    public List<CustomTheme> CustomThemes { get; set; } = [];
}

/// <summary>
/// A user-defined colour theme. <see cref="Colors"/> maps CSS token names
/// (without the leading "--", e.g. "bg-base") to hex colour values ("#1a1a2e").
/// Unknown tokens are ignored, missing tokens fall back to the dark defaults,
/// so exported themes stay forward/backward compatible.
/// </summary>
public class CustomTheme
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional preset id this theme was derived from (informational).</summary>
    public string BasedOn { get; set; } = "dark";

    public Dictionary<string, string> Colors { get; set; } = [];
}
