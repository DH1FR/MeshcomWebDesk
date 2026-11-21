using System.Net;
using System.Net.Http.Headers;

namespace MeshcomWebDesk.Services.Update;

/// <summary>What the Docker update page and banner need to know.</summary>
/// <param name="Channel"><c>latest</c> (releases) or <c>dev</c> (every push to the dev branch).</param>
/// <param name="Available">A newer image/build than the running one is known.</param>
/// <param name="Latest">Version tag (release channel) or short commit id (dev channel) of the newest build.</param>
/// <param name="CheckFailed">The newest build could not be determined (offline, rate limit).</param>
public sealed record DockerUpdateInfo(
    string Channel,
    string Version,
    string? RunningCommit,
    bool WatchtowerConfigured,
    bool Available,
    string? Latest,
    bool CheckFailed);

/// <summary>
/// Updating a Docker installation: the container cannot replace itself, so a Watchtower container (which owns the
/// access to Docker) pulls the new image and recreates WebDesk. WebDesk only asks it to, over its HTTP API
/// (<c>POST /v1/update</c>, bearer token) – WebDesk itself never gets access to the Docker socket.
/// <para>
/// Two channels: <c>latest</c> follows the GitHub releases (image <c>:latest</c>); <c>dev</c> follows every push to the
/// dev branch (image <c>:dev</c>, tagged with the commit it was built from, <c>WEBDESK_COMMIT</c>).
/// </para>
/// Settings come from environment variables set in <c>docker-compose.image.yml</c>:
/// <c>WEBDESK_CHANNEL</c>, <c>WEBDESK_COMMIT</c> (baked into the image), <c>WEBDESK_WATCHTOWER_URL</c>,
/// <c>WEBDESK_WATCHTOWER_TOKEN</c> and <c>WEBDESK_IMAGE</c>.
/// </summary>
public sealed class DockerUpdateService
{
    private const string DevHeadUrl = "https://api.github.com/repos/DH1FR/MeshcomWebDesk/commits/dev";
    private const string DefaultImage = "ghcr.io/dh1fr/meshcomwebdesk";
    private static readonly TimeSpan DevCacheTime = TimeSpan.FromMinutes(10);

    private readonly UpdateCheckService _releases;
    private readonly ILogger<DockerUpdateService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _devHead;
    private DateTime _devHeadAtUtc;
    private bool _devHeadFailed;

    public DockerUpdateService(UpdateCheckService releases, ILogger<DockerUpdateService> logger)
    {
        _releases = releases;
        _logger   = logger;
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>True when WebDesk runs in a container.</summary>
    public static bool InContainer => string.Equals(Env("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>dev</c> or <c>latest</c> (every other value counts as <c>latest</c>).</summary>
    public static string Channel => string.Equals(Env("WEBDESK_CHANNEL"), "dev", StringComparison.OrdinalIgnoreCase) ? "dev" : "latest";

    /// <summary>The commit this image was built from (only the dev workflow sets it).</summary>
    public static string? RunningCommit => Env("WEBDESK_COMMIT");

    public static bool WatchtowerConfigured => Env("WEBDESK_WATCHTOWER_URL") is not null;

    private static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    private static string Short(string? commit) => commit is { Length: > 7 } c ? c[..7] : commit ?? "";

    /// <summary>The current state of the update check. The dev head is cached for ten minutes (GitHub rate limit).</summary>
    public async Task<DockerUpdateInfo> GetInfoAsync(CancellationToken ct = default, bool force = false)
    {
        var channel = Channel;
        if (channel == "latest")
        {
            if (force) await _releases.CheckNowAsync(ct);
            // UpdateCheckService already asks GitHub for the latest release and only reports a newer one.
            return new(channel, Version, RunningCommit, WatchtowerConfigured,
                _releases.LatestVersion is not null, _releases.LatestVersion, force && _releases.LastCheckFailed);
        }

        var head = await GetDevHeadAsync(ct, force);
        var running = RunningCommit;
        var available = head is not null && running is not null && !SameCommit(head, running);
        return new(channel, Version, running, WatchtowerConfigured, available, head is null ? null : Short(head), head is null && _devHeadFailed);
    }

    private static bool SameCommit(string a, string b)
    {
        var n = Math.Min(Math.Min(a.Length, b.Length), 40);
        return n >= 7 && string.Equals(a[..n], b[..n], StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> GetDevHeadAsync(CancellationToken ct, bool force = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!force && _devHead is not null && DateTime.UtcNow - _devHeadAtUtc < DevCacheTime) return _devHead;
            if (!force && _devHeadFailed && DateTime.UtcNow - _devHeadAtUtc < TimeSpan.FromMinutes(2)) return _devHead;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MeshcomWebDesk/" + Version);
                // This media type makes the API answer with just the commit id.
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.sha"));
                var sha = (await http.GetStringAsync(DevHeadUrl, ct)).Trim();
                _devHead = sha.Length >= 7 ? sha : null;
                _devHeadFailed = _devHead is null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read the dev branch head from GitHub.");
                _devHeadFailed = true;
            }
            _devHeadAtUtc = DateTime.UtcNow;
            return _devHead;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Asks Watchtower to pull the newest image and recreate WebDesk. Watchtower replaces this very container, so the
    /// connection usually breaks while the answer is on its way – that counts as "started".
    /// </summary>
    public async Task<(bool Ok, string Message)> TriggerAsync(CancellationToken ct = default)
    {
        var url = Env("WEBDESK_WATCHTOWER_URL");
        if (url is null) return (false, "not-configured");
        var token = Env("WEBDESK_WATCHTOWER_TOKEN");
        var image = Env("WEBDESK_IMAGE") ?? DefaultImage;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{url.TrimEnd('/')}/v1/update?image={Uri.EscapeDataString(image)}");
            if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return (false, "unauthorized");
            if (!resp.IsSuccessStatusCode) return (false, $"http-{(int)resp.StatusCode}");
            return (true, "started");
        }
        catch (TaskCanceledException)
        {
            return (true, "started");          // Watchtower is busy replacing us
        }
        catch (HttpRequestException ex) when (ex.InnerException is IOException)
        {
            return (true, "started");          // connection dropped because this container is being replaced
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watchtower request failed ({Url})", url);
            return (false, "unreachable");
        }
    }
}
