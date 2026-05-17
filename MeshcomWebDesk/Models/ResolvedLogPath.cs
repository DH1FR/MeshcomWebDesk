namespace MeshcomWebDesk.Models;

/// <summary>
/// Holds the effective log directory path as resolved by Program.cs at startup
/// (appsettings.json → environment → default). This value is immune to later
/// overrides from appsettings.override.json which may write an empty string.
/// </summary>
public sealed class ResolvedLogPath(string path)
{
    public string Path { get; } = path;
}
