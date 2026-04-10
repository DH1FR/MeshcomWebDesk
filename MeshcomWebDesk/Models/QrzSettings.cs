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
}
