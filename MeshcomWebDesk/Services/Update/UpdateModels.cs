using System.Text.Json.Serialization;

namespace MeshcomWebDesk.Services.Update;

/// <summary>How this WebDesk instance is started – decides how it can be updated and restarted.</summary>
public enum InstallMode
{
    /// <summary>Windows service (Inno installer). Restart: service recovery + "sc start" safety net.</summary>
    WindowsService,
    /// <summary>systemd unit (deploy/install.sh). Restart: exit non-zero, systemd (Restart=on-failure) restarts.</summary>
    Systemd,
    /// <summary>Started by hand / autostart without a supervisor. Restart: delayed relaunch of the executable.</summary>
    Manual,
    /// <summary>Docker container – not supported by the self-updater (image is the unit of deployment).</summary>
    Docker,
    /// <summary>Development run (dotnet run / debugger) – never updated.</summary>
    Development
}

/// <summary>Result of the install-type detection.</summary>
public sealed record InstallInfo(
    InstallMode Mode,
    string AppDir,
    string ExePath,
    string Rid,
    bool SelfContained,
    bool Supported,
    string? UnsupportedReason);

public enum UpdatePhase
{
    Idle,
    Checking,
    Downloading,
    Verifying,
    Extracting,
    BackingUp,
    Applying,
    Restarting,
    Failed
}

/// <summary>Live progress of a running update (read by the UI).</summary>
public sealed class UpdateStatus
{
    public UpdatePhase Phase { get; set; } = UpdatePhase.Idle;
    public int Percent { get; set; }
    public string? TargetVersion { get; set; }
    public string? Error { get; set; }
    public bool Busy => Phase is not (UpdatePhase.Idle or UpdatePhase.Failed);
}

/// <summary>Persisted while an update is "on probation" (written before restart, removed once healthy).</summary>
public sealed class UpdateState
{
    [JsonPropertyName("from")]     public string From { get; set; } = "";
    [JsonPropertyName("to")]       public string To { get; set; } = "";
    [JsonPropertyName("backup")]   public string Backup { get; set; } = "";
    [JsonPropertyName("attempts")] public int Attempts { get; set; }
    [JsonPropertyName("started")]  public DateTime StartedUtc { get; set; }
}

/// <summary>Outcome of the last update, shown on the update page.</summary>
public sealed class UpdateResult
{
    /// <summary>"ok" | "rolledback" | "rollback-manual"</summary>
    [JsonPropertyName("kind")]   public string Kind { get; set; } = "";
    [JsonPropertyName("from")]   public string From { get; set; } = "";
    [JsonPropertyName("to")]     public string To { get; set; } = "";
    [JsonPropertyName("whenUtc")] public DateTime WhenUtc { get; set; }
}

/// <summary>One backup directory below <c>&lt;DataPath&gt;/backups</c>.</summary>
public sealed record BackupInfo(string Name, string Version, DateTime CreatedUtc);
