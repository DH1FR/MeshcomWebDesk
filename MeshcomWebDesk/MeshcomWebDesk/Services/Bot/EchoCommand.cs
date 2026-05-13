using MeshcomWebDesk.Services;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>Echoes the supplied arguments back to the sender.</summary>
public class EchoCommand(LanguageService lang) : IBotCommand
{
    public string Name        => "echo";
    public string Description => lang.T("Text zurücksenden", "Echo text back");

    public Task<string> ExecuteAsync(string[] args, string senderCallsign)
    {
        if (args.Length == 0)
        {
            var hint = lang.T(
                "Verwendung: --echo <Text>",
                "Usage: --echo <text>");
            return Task.FromResult(hint);
        }

        return Task.FromResult(string.Join(' ', args));
    }
}
