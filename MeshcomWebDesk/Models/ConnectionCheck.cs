namespace MeshcomWebDesk.Models;

/// <summary>Severity of a single connection-check finding.</summary>
public enum CheckSeverity
{
    /// <summary>Everything fine.</summary>
    Ok,
    /// <summary>Informational – no action needed.</summary>
    Info,
    /// <summary>Probably wrong or unconfirmed – worth a look.</summary>
    Warning,
    /// <summary>Will not work as configured.</summary>
    Error,
}

/// <summary>
/// One finding of the connection check. <see cref="Title"/> and <see cref="Detail"/> are already
/// localised; <see cref="Commands"/> are node console commands the user can copy (never sent automatically).
/// </summary>
public sealed record CheckResult(
    CheckSeverity Severity,
    string Title,
    string? Detail = null,
    IReadOnlyList<string>? Commands = null,
    string? Code = null,
    string? SuggestCallsign = null);

/// <summary>A further configured node, used to detect clashes (same IP / same callsign).</summary>
public sealed record OtherNodeInfo(string Name, string Callsign, string DeviceIp, int DevicePort);

/// <summary>
/// The values the check runs against – the current (possibly unsaved) form state of ONE node.
/// </summary>
/// <param name="NodeId">Node profile id; null = legacy single-node mode (no profile yet).</param>
/// <param name="IsPrimary">True for the primary node (the one managed in the "Connection" section).</param>
/// <param name="Others">All other enabled nodes of the form, for the clash checks.</param>
public sealed record ConnectionCheckInput(
    string ListenIp,
    int ListenPort,
    string DeviceIp,
    int DevicePort,
    string Callsign,
    bool Kiss,
    Guid? NodeId,
    bool IsPrimary = true,
    IReadOnlyList<OtherNodeInfo>? Others = null,
    string? NodePassword = null);

/// <summary>Connection values entered in the setup wizard; applied to the form only on the user's request.</summary>
public sealed record WizardValues(
    string Callsign,
    string DeviceIp,
    int DevicePort,
    string ListenIp,
    int ListenPort);

/// <summary>
/// One setting of the node, read over the net console, compared with the WebDesk configuration.
/// <see cref="FixCommand"/> is the node command that would correct it (only ever sent after the user confirms).
/// </summary>
public sealed record SettingRow(
    string Key,
    string Label,
    string? NodeValue,
    string Expected,
    CheckSeverity Severity,
    string? Hint = null,
    string? FixCommand = null);
