using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using MeshcomWebDesk.Services.Update;

namespace MeshcomWebDesk.Services.Access;

/// <summary>When WebDesk asks for the access password.</summary>
public enum AccessMode
{
    /// <summary>No protection (default).</summary>
    Off,
    /// <summary>Password for everybody except requests from this PC (localhost).</summary>
    OutsideLocalhost,
    /// <summary>Password for every request, localhost included.</summary>
    Always,
}

/// <summary>Content of <c>&lt;DataPath&gt;/access.json</c>.</summary>
public sealed class AccessConfig
{
    public AccessMode Mode { get; set; } = AccessMode.Off;
    /// <summary>PBKDF2 hash of the password, empty = no password set.</summary>
    public string Hash { get; set; } = string.Empty;
    /// <summary>Raised to sign out every device (all cookies carry the version they were issued with).</summary>
    public int SessionVersion { get; set; } = 1;
}

/// <summary>
/// Reads and writes the access configuration. A separate file next to the settings (not part of
/// <c>appsettings.override.json</c>): saving the settings form cannot overwrite it, it is not part of a settings
/// backup, and <c>--reset-password</c> can change it without starting the web server.
/// </summary>
public static class AccessStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string FilePath(string dataPath) => Path.Combine(dataPath, "access.json");

    public static AccessConfig Load(string dataPath)
    {
        try
        {
            var file = FilePath(dataPath);
            if (File.Exists(file))
                return JsonSerializer.Deserialize<AccessConfig>(File.ReadAllText(file), Json) ?? new AccessConfig();
        }
        catch { /* unreadable file: treated like "not configured" – the caller logs nothing sensitive */ }
        return new AccessConfig();
    }

    /// <summary>
    /// Emergency exit for a forgotten password: protection off and every device signed out; with
    /// <paramref name="newPassword"/> the protection stays on (default: everybody except localhost) under the new password.
    /// </summary>
    public static void Reset(string dataPath, string? newPassword)
    {
        var old  = Load(dataPath);
        var next = new AccessConfig { SessionVersion = old.SessionVersion + 1 };
        if (!string.IsNullOrEmpty(newPassword))
        {
            next.Hash = AccessPassword.Hash(newPassword);
            next.Mode = old.Mode == AccessMode.Off ? AccessMode.OutsideLocalhost : old.Mode;
        }
        Save(dataPath, next);
    }

    /// <summary>
    /// Where <c>access.json</c> lives, worked out like <c>Program.cs</c> does (appsettings, environment variables,
    /// command line) – for <c>--reset-password</c>, which runs before the web host exists.
    /// </summary>
    public static string ResolveDataPath(string[] args)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
               ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        var cfg = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{env}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        return cfg.GetSection("Meshcom").GetValue<string>("DataPath") ?? @"C:\Temp\MeshcomData";
    }

    public static void Save(string dataPath, AccessConfig config)
    {
        Directory.CreateDirectory(dataPath);
        var file = FilePath(dataPath);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Json) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(tmp, file, overwrite: true);
    }
}

/// <summary>PBKDF2-SHA256 password hashing (salted, constant-time comparison).</summary>
public static class AccessPassword
{
    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 210_000;

    public const int MinLength = 6;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        try
        {
            var p = stored.Split('$');
            if (p.Length != 4 || p[0] != Prefix || !int.TryParse(p[1], out var iterations) || iterations < 1) return false;
            var salt     = Convert.FromBase64String(p[2]);
            var expected = Convert.FromBase64String(p[3]);
            var actual   = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }
}

/// <summary>Result of a login attempt.</summary>
public readonly record struct LoginResult(bool Ok, int RetryAfterSeconds = 0);

/// <summary>
/// Optional password protection of the whole web UI (see <see cref="AccessMode"/>). One shared password, kept as a
/// PBKDF2 hash in <c>access.json</c>; a signed cookie remembers the device. Wrong passwords are throttled per
/// client address.
/// </summary>
public sealed class AccessService
{
    public const string CookieName = "meshcom-access";
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(90);
    private static readonly TimeSpan CookieRenewBelow = TimeSpan.FromDays(60);

    private readonly string _dataPath;
    private readonly IDataProtector _protector;
    private readonly ILogger<AccessService> _logger;
    private readonly object _lock = new();

    private AccessConfig _config = new();
    private DateTime _fileStamp = DateTime.MinValue;
    private DateTime _lastCheck = DateTime.MinValue;

    private sealed class Throttle { public int Fails; public DateTime LockedUntil; }
    private readonly ConcurrentDictionary<string, Throttle> _throttle = new();
    private readonly ConcurrentDictionary<string, DateTime> _tickets = new();

    public AccessService(string dataPath, IDataProtectionProvider dataProtection, ILogger<AccessService> logger)
    {
        _dataPath  = dataPath;
        _protector = dataProtection.CreateProtector("MeshcomWebDesk.Access.v1");
        _logger    = logger;
    }

    // ── Configuration ───────────────────────────────────────────────────────

    /// <summary>
    /// Current configuration. The file is looked at every couple of seconds so a change made from outside
    /// (<c>--reset-password</c> in a running container) takes effect without a restart.
    /// </summary>
    private AccessConfig Config
    {
        get
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastCheck).TotalSeconds < 2) return _config;
                _lastCheck = now;
                var file  = AccessStore.FilePath(_dataPath);
                var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
                if (stamp != _fileStamp)
                {
                    _fileStamp = stamp;
                    _config    = AccessStore.Load(_dataPath);
                }
                return _config;
            }
        }
    }

    private void Persist(AccessConfig config)
    {
        lock (_lock)
        {
            AccessStore.Save(_dataPath, config);
            _config    = config;
            var file   = AccessStore.FilePath(_dataPath);
            _fileStamp = File.GetLastWriteTimeUtc(file);
            _lastCheck = DateTime.UtcNow;
        }
    }

    public AccessMode Mode => Config.Mode;

    /// <summary>True when a password is set and the mode is not <see cref="AccessMode.Off"/>.</summary>
    public bool Enabled => Config.Mode != AccessMode.Off && !string.IsNullOrEmpty(Config.Hash);

    public bool HasPassword => !string.IsNullOrEmpty(Config.Hash);

    /// <summary>Does this request have to prove that it knows the password?</summary>
    public bool IsProtected(HttpContext ctx)
    {
        var c = Config;
        if (c.Mode == AccessMode.Off || string.IsNullOrEmpty(c.Hash)) return false;
        return c.Mode == AccessMode.Always || !UpdateEndpoints.IsLocalRequest(ctx);
    }

    // ── Changing the configuration (Settings page, CLI) ─────────────────────

    /// <summary>
    /// Sets the mode and, when <paramref name="newPassword"/> is given, a new password (this signs every other
    /// device out). Returns an error key or null. <paramref name="currentPassword"/> is only checked when a
    /// password already exists and <paramref name="skipCurrentCheck"/> is false.
    /// </summary>
    public string? Apply(AccessMode mode, string? newPassword, string? currentPassword, bool skipCurrentCheck)
    {
        var cfg = Config;
        if (!string.IsNullOrEmpty(cfg.Hash) && !skipCurrentCheck
            && !AccessPassword.Verify(currentPassword ?? string.Empty, cfg.Hash))
            return "wrong-current";

        var next = new AccessConfig { Mode = mode, Hash = cfg.Hash, SessionVersion = cfg.SessionVersion };
        if (!string.IsNullOrEmpty(newPassword))
        {
            if (newPassword.Length < AccessPassword.MinLength) return "too-short";
            next.Hash = AccessPassword.Hash(newPassword);
            next.SessionVersion = cfg.SessionVersion + 1;
        }
        if (mode != AccessMode.Off && string.IsNullOrEmpty(next.Hash)) return "password-required";
        if (mode == AccessMode.Off) { next.Hash = string.Empty; next.SessionVersion = cfg.SessionVersion + 1; }

        Persist(next);
        _logger.LogInformation("Access protection set to {Mode}{Changed}", mode,
            string.IsNullOrEmpty(newPassword) ? "" : " (new password)");
        return null;
    }

    /// <summary>Signs every device out (all cookies become invalid).</summary>
    public void SignOutEverywhere(string? currentPassword, bool skipCurrentCheck, out string? error)
    {
        var cfg = Config;
        error = null;
        if (!string.IsNullOrEmpty(cfg.Hash) && !skipCurrentCheck
            && !AccessPassword.Verify(currentPassword ?? string.Empty, cfg.Hash))
        {
            error = "wrong-current";
            return;
        }
        Persist(new AccessConfig { Mode = cfg.Mode, Hash = cfg.Hash, SessionVersion = cfg.SessionVersion + 1 });
        _logger.LogInformation("All devices signed out of the access protection");
    }

    // ── Login / cookie ──────────────────────────────────────────────────────

    public async Task<LoginResult> TryLoginAsync(string clientKey, string password)
    {
        var cfg = Config;
        if (string.IsNullOrEmpty(cfg.Hash)) return new LoginResult(true);

        var t = _throttle.GetOrAdd(clientKey, _ => new Throttle());
        lock (t)
        {
            var wait = (int)Math.Ceiling((t.LockedUntil - DateTime.UtcNow).TotalSeconds);
            if (wait > 0) return new LoginResult(false, wait);
        }

        if (AccessPassword.Verify(password ?? string.Empty, cfg.Hash))
        {
            _throttle.TryRemove(clientKey, out _);
            return new LoginResult(true);
        }

        int retry = 0;
        lock (t)
        {
            t.Fails++;
            // 5 free attempts, then 30 s, doubling per further failure, at most 5 minutes
            if (t.Fails >= 5)
            {
                retry = (int)Math.Min(300, 30 * Math.Pow(2, t.Fails - 5));
                t.LockedUntil = DateTime.UtcNow.AddSeconds(retry);
            }
        }
        _logger.LogWarning("Wrong access password from {Client}{Locked}", clientKey,
            retry > 0 ? $" – locked for {retry} s" : "");
        await Task.Delay(400);
        return new LoginResult(false, retry);
    }

    public bool HasValidCookie(HttpContext ctx) => TryReadCookie(ctx, out _);

    private bool TryReadCookie(HttpContext ctx, out DateTime expires)
    {
        expires = default;
        if (!ctx.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value)) return false;
        try
        {
            var parts = _protector.Unprotect(value).Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var version)
                || !long.TryParse(parts[1], out var unix)) return false;
            expires = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            return version == Config.SessionVersion && expires > DateTime.UtcNow;
        }
        catch { return false; }
    }

    /// <summary>Remembers this device: sets the (HttpOnly) access cookie.</summary>
    public void IssueCookie(HttpContext ctx)
    {
        var expires = DateTime.UtcNow.Add(CookieLifetime);
        var value   = _protector.Protect($"{Config.SessionVersion}|{new DateTimeOffset(expires).ToUnixTimeSeconds()}");
        ctx.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure   = IsHttps(ctx),
            Expires  = expires,
            Path     = "/",
        });
    }

    /// <summary>Extends the cookie of a device that is still in use, so it never has to log in again.</summary>
    public void RenewCookieIfNeeded(HttpContext ctx)
    {
        if (TryReadCookie(ctx, out var expires) && expires - DateTime.UtcNow < CookieRenewBelow)
            IssueCookie(ctx);
    }

    public void ClearCookie(HttpContext ctx) =>
        ctx.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/", Secure = IsHttps(ctx), SameSite = SameSiteMode.Lax });

    public static bool IsHttps(HttpContext ctx) =>
        ctx.Request.IsHttps
        || string.Equals(ctx.Request.Headers["X-Forwarded-Proto"].ToString().Split(',')[0].Trim(), "https", StringComparison.OrdinalIgnoreCase);

    // ── One-time ticket: lets the Settings page (a SignalR circuit cannot set cookies) sign this browser in ──

    public string CreateTicket()
    {
        var now = DateTime.UtcNow;
        foreach (var old in _tickets.Where(t => t.Value < now).Select(t => t.Key).ToList()) _tickets.TryRemove(old, out _);
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tickets[ticket] = now.AddSeconds(60);
        return ticket;
    }

    public bool ConsumeTicket(string? ticket) =>
        !string.IsNullOrEmpty(ticket) && _tickets.TryRemove(ticket, out var until) && until > DateTime.UtcNow;
}
