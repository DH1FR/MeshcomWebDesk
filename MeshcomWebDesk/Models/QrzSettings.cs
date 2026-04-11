namespace MeshcomWebDesk.Models;

public class QrzSettings
{
    public const string SectionName = "Qrz";

    /// <summary>When true, QRZ.com callsign lookups are performed in the MH list.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>QRZ.com login username.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>QRZ.com login password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>When true, every QRZ.com lookup (API call and cache hit) is written to the log file.</summary>
    public bool LogRequests { get; set; } = false;

    /// <summary>
    /// Maximum age of a cached callsign entry in days before it is automatically re-fetched from QRZ.com.
    /// Set to 0 to disable expiry (entries are kept indefinitely).
    /// Default is 30 days.
    /// </summary>
    public int CacheMaxAgeDays { get; set; } = 30;
}
