namespace MeshcomWebDesk.Services.Bot;

/// <summary>Returns the list of recently heard stations from the MH list.</summary>
public class MhCommand(ChatService chatService) : IBotCommand
{
    public string Name        => "mh";
    public string Description => "Gehörte Stationen";

    public Task<string> ExecuteAsync(string[] args, string senderCallsign)
    {
        var list = chatService.MhList;
        if (list.Count == 0)
            return Task.FromResult("MH: Keine Stationen gehört");

        var names  = string.Join(", ", list.Take(5).Select(s => s.Callsign));
        var suffix = list.Count > 5 ? $", ... (+{list.Count - 5})" : string.Empty;
        return Task.FromResult($"MH({list.Count}): {names}{suffix}");
    }
}
