namespace MeshcomWebDesk.Models;

/// <summary>Online status of a MeshCom node based on the last received UDP packet.</summary>
public enum NodeOnlineStatus
{
    /// <summary>No packet has been received yet since the app started.</summary>
    Unknown,

    /// <summary>A packet was received within the last 5 minutes.</summary>
    Online,

    /// <summary>Last packet was received between 5 and 30 minutes ago.</summary>
    Stale,

    /// <summary>No packet received for more than 30 minutes.</summary>
    Offline
}
