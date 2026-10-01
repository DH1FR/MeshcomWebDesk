namespace MeshcomWebDesk.Models;

/// <summary>
/// A user-defined web page that can be shown in the embedded "Web" view.
/// </summary>
public class WebLinkEntry
{
    /// <summary>Short display name shown on the selector (e.g. "MeshMap").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute http(s) URL of the page to embed.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>When false, the entry is kept but not offered in the Web view.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True when <paramref name="url"/> is an absolute http/https URL.</summary>
    public static bool IsValidUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
