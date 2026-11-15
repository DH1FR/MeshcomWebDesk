using System.Text.Json;

namespace MeshcomWebDesk.Services.Update;

/// <summary>
/// Persistence of the update state plus the startup check that rolls a failed update back automatically.
///
/// After files were replaced, <c>update-state.json</c> exists in the data directory. Every start of the new
/// version counts an attempt; the marker is removed by <see cref="UpdateHealthService"/> once the app has run
/// stably for a minute. If the new version keeps dying before that (3rd start without a healthy minute), the
/// next start restores the backup and restarts with the old version.
/// </summary>
internal static class UpdateRecovery
{
    private const int MaxAttempts = 2;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string StatePath(string dataPath)  => Path.Combine(dataPath, "update", "update-state.json");
    public static string ResultPath(string dataPath) => Path.Combine(dataPath, "update", "update-result.json");

    public static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static UpdateState? ReadState(string dataPath) => Read<UpdateState>(StatePath(dataPath));
    public static UpdateResult? ReadResult(string dataPath) => Read<UpdateResult>(ResultPath(dataPath));

    public static void WriteState(string dataPath, UpdateState state) => Write(StatePath(dataPath), state);

    public static void WriteResult(string dataPath, UpdateResult result) => Write(ResultPath(dataPath), result);

    public static void ClearState(string dataPath)
    {
        try { File.Delete(StatePath(dataPath)); } catch { /* ignore */ }
    }

    private static T? Read<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    }

    /// <summary>
    /// Called very early from Program.cs (before the host is built, so a crash later in startup is still counted).
    /// May restore the previous version and restart the process (does not return in that case).
    /// </summary>
    public static void OnStartup(string dataPath)
    {
        try
        {
            var info = UpdateRestart.Detect();
            var state = ReadState(dataPath);

            if (state is null)
            {
                FileSwap.CleanupTemp(info.AppDir);
                return;
            }

            // The marker belongs to another version (apply failed and the old binary came back, or a manual
            // rollback happened): nothing to supervise.
            if (!state.To.Equals(CurrentVersion, StringComparison.OrdinalIgnoreCase))
            {
                ClearState(dataPath);
                return;
            }

            state.Attempts++;
            WriteState(dataPath, state);

            if (state.Attempts <= MaxAttempts) return;

            Console.WriteLine($"[Update] v{state.To} did not become healthy after {MaxAttempts} starts – rolling back to v{state.From}.");
            FileSwap.RestoreBackup(info.AppDir, dataPath, state.Backup);
            ClearState(dataPath);
            WriteResult(dataPath, new UpdateResult
            {
                Kind = "rolledback", From = state.From, To = state.To, WhenUtc = DateTime.UtcNow
            });
            UpdateRestart.RestartNow(info);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Update] Recovery check failed: {ex.Message}");
        }
    }
}
