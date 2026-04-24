namespace MeshcomWebDesk.Services;

/// <summary>
/// Expands MeshCom template variables (e.g. {mycall}, {date}, {callsign}) in a text string.
/// </summary>
public interface IMeshcomVariableExpander
{
    string ExpandVariables(string template, string? callsign = null);
}
