using MeshcomWebDesk.Services;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>Returns the current date and time.</summary>
public class TimeCommand(LanguageService lang) : IBotCommand
{
    public string Name        => "time";
    public string Description => lang.T("Aktuelle Uhrzeit", "Current time");

    public Task<string> ExecuteAsync(string[] args, string senderCallsign)
    {
        var label  = lang.T("Zeit",       "Time");
        var format = lang.T("dd.MM.yyyy", "dd/MM/yyyy");
        return Task.FromResult($"{label}: {DateTime.Now.ToString(format)} {DateTime.Now:HH:mm}");
    }
}
