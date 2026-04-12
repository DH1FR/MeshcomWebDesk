namespace MeshcomWebDesk.Services.Bot;

/// <summary>Returns the current date and time.</summary>
public class TimeCommand : IBotCommand
{
    public string Name        => "time";
    public string Description => "Aktuelle Uhrzeit";

    public Task<string> ExecuteAsync(string[] args, string senderCallsign) =>
        Task.FromResult($"Zeit: {DateTime.Now:dd.MM.yyyy HH:mm}");
}
