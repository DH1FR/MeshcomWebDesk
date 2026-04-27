namespace MeshcomWebDesk.Services.Bot;

/// <summary>Returns the current MeshComWebDesk version.</summary>
public class VersionCommand : IBotCommand
{
    private static readonly string AppVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    public string Name        => "version";
    public string Description => "MeshComWebDesk Version";

    public Task<string> ExecuteAsync(string[] args, string senderCallsign) =>
        Task.FromResult($"MeshComWebDesk v{AppVersion}");
}
