using System.Text.Json;

namespace MeshcomWebDesk.Services.Update;

/// <summary>
/// File-level building blocks of the self-updater: backup, in-place file replacement and restore.
///
/// Replacing files of a *running* application works without a helper process:
///  • Linux/macOS – a new file is moved over the old one (the running binary keeps its old inode);
///  • Windows – a running exe / loaded DLL cannot be overwritten, but it can be renamed, so the old
///    file is renamed to <c>*.old-update</c> and the new one is written under the original name.
/// The process is restarted afterwards (see <see cref="UpdateRestart"/>).
/// </summary>
public static class FileSwap
{
    /// <summary>Top-level directories of the app directory that belong to the user and are never touched.</summary>
    private static readonly string[] UserDirs = { "data", "logs", "keys", "bot-scripts", "certs", "backups", "update" };

    /// <summary>Top-level files of the release archive that are installer artefacts, not part of the app.</summary>
    private static readonly string[] SkipFiles = { "install.sh", "meshcom-webdesk.service" };

    /// <summary>
    /// Additional directories (full paths) that hold user data although they have another name than the
    /// defaults – i.e. the configured DataPath/LogPath when they are located inside the program directory.
    /// </summary>
    private static string[] _extraExcludes = Array.Empty<string>();

    public static void ConfigureExcludes(params string?[] dirs) =>
        _extraExcludes = dirs.Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => Path.GetFullPath(d!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToArray();

    private static bool IsExcludedPath(string fullPath) =>
        _extraExcludes.Any(e => fullPath.StartsWith(e, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    private const string OldSuffix = ".old-update";
    private const string NewSuffix = ".new-update";

    public static string BackupRoot(string dataPath) => Path.Combine(dataPath, "backups");

    // ── Classification ───────────────────────────────────────────────────────────────────

    private static bool IsUserDir(string rel)
    {
        var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return UserDirs.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>appsettings*.json in the app root hold the user's configuration – never overwritten.</summary>
    private static bool IsUserOwnedFile(string rel) =>
        !rel.Contains(Path.DirectorySeparatorChar) && !rel.Contains(Path.AltDirectorySeparatorChar) &&
        rel.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
        rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkipFile(string rel) =>
        SkipFiles.Contains(rel, StringComparer.OrdinalIgnoreCase);

    private static bool IsTemp(string rel) =>
        rel.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase) ||
        rel.EndsWith(NewSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Files that may be removed when they no longer exist in the new version.</summary>
    private static bool IsPrunable(string rel)
    {
        var ext = Path.GetExtension(rel).ToLowerInvariant();
        return ext is ".dll" or ".pdb" or ".so" or ".dylib"
            || rel.StartsWith("wwwroot" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// All files below <paramref name="root"/> (relative paths) except user directories and update temp files.
    /// <paramref name="isAppDir"/> additionally applies the configured data/log directory exclusions – only
    /// correct for the real program directory (the staging area lives inside the data directory).
    /// </summary>
    public static List<string> EnumerateAppFiles(string root, bool isAppDir = false)
    {
        var result = new List<string>();
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file);
            if (IsUserDir(rel) || IsTemp(rel) || (isAppDir && IsExcludedPath(Path.GetFullPath(file)))) continue;
            result.Add(rel);
        }
        return result;
    }

    // ── Copy / replace ───────────────────────────────────────────────────────────────────

    /// <summary>Copies a file even when it is locked for writing (running exe/DLL).</summary>
    private static void CopyShared(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        using var input  = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static void CopyMode(string src, string dst)
    {
        if (OperatingSystem.IsWindows()) return;
        try { File.SetUnixFileMode(dst, File.GetUnixFileMode(src)); } catch { /* best effort */ }
    }

    private static void MoveAside(string target)
    {
        var old = target + OldSuffix;
        try { if (File.Exists(old)) File.Delete(old); }
        catch { old = target + "." + Guid.NewGuid().ToString("N")[..6] + OldSuffix; }
        File.Move(target, old);
    }

    private static void ReplaceFile(string src, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + NewSuffix;
        CopyShared(src, tmp);
        CopyMode(src, tmp);

        if (!File.Exists(target)) { File.Move(tmp, target); return; }

        if (OperatingSystem.IsWindows())
        {
            MoveAside(target);          // running exe / DLL: rename is allowed, overwrite is not
            File.Move(tmp, target);
        }
        else
        {
            File.Move(tmp, target, overwrite: true);   // atomic rename, the running process keeps its inode
        }
    }

    /// <summary>
    /// Makes <paramref name="appDir"/> look like <paramref name="srcDir"/>: copies/replaces every file,
    /// keeps user-owned files and removes outdated binaries/assets that are no longer part of the version.
    /// </summary>
    public static void ApplyTree(string srcDir, string appDir)
    {
        var before = EnumerateAppFiles(appDir, isAppDir: true);
        var incoming = EnumerateAppFiles(srcDir);
        var incomingSet = new HashSet<string>(incoming, StringComparer.OrdinalIgnoreCase);

        foreach (var rel in incoming)
        {
            if (IsSkipFile(rel)) continue;
            var target = Path.Combine(appDir, rel);
            if (IsUserOwnedFile(rel) && File.Exists(target)) continue;
            ReplaceFile(Path.Combine(srcDir, rel), target);
        }

        foreach (var rel in before)
        {
            if (incomingSet.Contains(rel) || IsUserOwnedFile(rel) || IsSkipFile(rel) || !IsPrunable(rel)) continue;
            var target = Path.Combine(appDir, rel);
            try
            {
                if (OperatingSystem.IsWindows()) MoveAside(target);
                else File.Delete(target);
            }
            catch { /* stale file stays – harmless */ }
        }
    }

    // ── Backup ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies the current program files (<c>app/</c>) and the data directory (<c>data/</c>) to a new backup
    /// directory. Returns the directory name below <c>&lt;DataPath&gt;/backups</c>.
    /// </summary>
    public static string CreateBackup(string appDir, string dataPath, string version, int keep = 3)
    {
        var name = $"v{version}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var dir  = Path.Combine(BackupRoot(dataPath), name);
        Directory.CreateDirectory(dir);

        foreach (var rel in EnumerateAppFiles(appDir, isAppDir: true))
            CopyShared(Path.Combine(appDir, rel), Path.Combine(dir, "app", rel));

        if (Directory.Exists(dataPath))
        {
            foreach (var file in Directory.EnumerateFiles(dataPath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(dataPath, file);
                var first = rel.Split(Path.DirectorySeparatorChar)[0];
                if (first is "backups" or "update" or "logs") continue;
                CopyShared(file, Path.Combine(dir, "data", rel));
            }
        }

        File.WriteAllText(Path.Combine(dir, "backup.json"),
            JsonSerializer.Serialize(new { version, createdUtc = DateTime.UtcNow }));

        PruneBackups(dataPath, keep);
        return name;
    }

    private static void PruneBackups(string dataPath, int keep)
    {
        foreach (var old in ListBackups(dataPath).Skip(keep))
        {
            try { Directory.Delete(Path.Combine(BackupRoot(dataPath), old.Name), recursive: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>Backups, newest first.</summary>
    public static List<BackupInfo> ListBackups(string dataPath)
    {
        var root = BackupRoot(dataPath);
        var list = new List<BackupInfo>();
        if (!Directory.Exists(root)) return list;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (!Directory.Exists(Path.Combine(dir, "app"))) continue;   // incomplete backup
            var name = Path.GetFileName(dir);
            var version = name.StartsWith('v') ? name[1..].Split('-')[0] : name;
            list.Add(new BackupInfo(name, version, Directory.GetCreationTimeUtc(dir)));
        }
        return list.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    // ── Restore ──────────────────────────────────────────────────────────────────────────

    /// <summary>Restores program files and data directory from a backup (the running process must restart afterwards).</summary>
    public static void RestoreBackup(string appDir, string dataPath, string backupName)
    {
        if (backupName.Contains("..") || backupName.IndexOfAny(new[] { '/', '\\' }) >= 0)
            throw new ArgumentException("Invalid backup name.", nameof(backupName));

        var dir = Path.Combine(BackupRoot(dataPath), backupName);
        var appBackup = Path.Combine(dir, "app");
        if (!Directory.Exists(appBackup))
            throw new DirectoryNotFoundException($"Backup '{backupName}' not found or incomplete.");

        ApplyTree(appBackup, appDir);

        var dataBackup = Path.Combine(dir, "data");
        if (Directory.Exists(dataBackup))
        {
            foreach (var file in Directory.EnumerateFiles(dataBackup, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dataPath, Path.GetRelativePath(dataBackup, file));
                CopyShared(file, target);
            }
        }
    }

    /// <summary>Removes leftovers (<c>*.old-update</c> / <c>*.new-update</c>) of a finished update; best effort. Returns the number removed.</summary>
    public static int CleanupTemp(string appDir)
    {
        var removed = 0;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(appDir, "*", options).ToList())
            {
                var name = Path.GetFileName(f);
                if (!name.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(NewSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); removed++; } catch { /* still locked */ }
            }
        }
        catch { /* ignore */ }
        return removed;
    }
}
