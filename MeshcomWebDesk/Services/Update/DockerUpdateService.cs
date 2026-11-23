using System.Net;
using System.Net.Http.Headers;

namespace MeshcomWebDesk.Services.Update;

/// <summary>What the Docker update page and banner need to know.</summary>
/// <param name="Channel"><c>latest</c> (releases) or <c>dev</c> (every push to the dev branch).</param>
/// <param name="Available">A newer image/build than the running one is known.</param>
/// <param name="Latest">Version tag (release channel) or short commit id (dev channel) of the newest build.</param>
/// <param name="CheckFailed">The newest build could not be determined (offline, rate limit).</param>
/// <param name="ImageBuilding">
/// A newer commit exists, but the published image still carries an older one – the CI is still building it. Updating
/// now would only pull the old image again.
/// </param>
public sealed record DockerUpdateInfo(
    string Channel,
    string Version,
    string? RunningCommit,
    bool WatchtowerConfigured,
    bool Available,
    string? Latest,
    bool CheckFailed,
    bool ImageBuilding = false);

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

        // The commit is on GitHub as soon as it is pushed, but the image only about three minutes later (CI build).
        var building = false;
        if (available && head is not null)
        {
            var published = await GetPublishedRevisionAsync(head, ct, force);
            building = published is not null && !SameCommit(head, published);   // unknown (null): do not block
        }
        return new(channel, Version, running, WatchtowerConfigured, available, head is null ? null : Short(head), head is null && _devHeadFailed, building);
    }

    // ── Is the image for the newest commit published yet? ─────────────────
    private readonly SemaphoreSlim _imageGate = new(1, 1);
    private string? _publishedRevision;
    private DateTime _publishedAtUtc;
    private static readonly TimeSpan PublishedRecheck = TimeSpan.FromSeconds(45);

    /// <summary>
    /// The commit the published dev image was built from (<c>null</c> = could not be determined, e.g. offline or a
    /// registry other than ghcr.io). Once the image carries the head commit nothing changes until the head moves on;
    /// while it still lags behind it is looked at again after 45 seconds.
    /// </summary>
    private async Task<string?> GetPublishedRevisionAsync(string head, CancellationToken ct, bool force)
    {
        await _imageGate.WaitAsync(ct);
        try
        {
            if (!force && _publishedRevision is not null && SameCommit(head, _publishedRevision)) return _publishedRevision;
            if (!force && _publishedAtUtc != default && DateTime.UtcNow - _publishedAtUtc < PublishedRecheck) return _publishedRevision;

            _publishedRevision = await FetchPublishedRevisionAsync(Env("WEBDESK_IMAGE") ?? DefaultImage, "dev", ct, _logger);
            _publishedAtUtc = DateTime.UtcNow;
            return _publishedRevision;
        }
        finally
        {
            _imageGate.Release();
        }
    }

    /// <summary>
    /// Reads the build commit of <c>ghcr.io/&lt;repo&gt;:&lt;tag&gt;</c> from the registry (anonymous pull token → manifest →
    /// image config: <c>WEBDESK_COMMIT</c> or the OCI revision label). Returns <c>null</c> on any problem.
    /// </summary>
    public static async Task<string?> FetchPublishedRevisionAsync(string image, string tag, CancellationToken ct, ILogger? logger = null)
    {
        const string host = "ghcr.io/";
        if (!image.StartsWith(host, StringComparison.OrdinalIgnoreCase)) return null;   // only GHCR is known
        var repo = image[host.Length..];
        var colon = repo.IndexOf(':');
        if (colon > 0) repo = repo[..colon];
        var at = repo.IndexOf('@');
        if (at > 0) repo = repo[..at];

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MeshcomWebDesk/" + Version);

            using (var tokenDoc = System.Text.Json.JsonDocument.Parse(
                       await http.GetStringAsync($"https://ghcr.io/token?service=ghcr.io&scope=repository:{repo}:pull", ct)))
            {
                var token = tokenDoc.RootElement.GetProperty("token").GetString();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            async Task<System.Text.Json.JsonDocument> GetJson(string path, string accept)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://ghcr.io/v2/{repo}/{path}");
                req.Headers.TryAddWithoutValidation("Accept", accept);
                using var resp = await http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                return System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            }

            const string manifestTypes =
                "application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, " +
                "application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json";

            var doc = await GetJson($"manifests/{tag}", manifestTypes);
            try
            {
                // Multi-arch: take the first real platform (attestation manifests report architecture "unknown").
                if (doc.RootElement.TryGetProperty("manifests", out var list))
                {
                    string? digest = null;
                    foreach (var m in list.EnumerateArray())
                    {
                        var arch = m.TryGetProperty("platform", out var p) && p.TryGetProperty("architecture", out var a) ? a.GetString() : null;
                        if (arch is not null && arch != "unknown") { digest = m.GetProperty("digest").GetString(); break; }
                    }
                    if (digest is null) return null;
                    doc.Dispose();
                    doc = await GetJson($"manifests/{digest}", manifestTypes);
                }

                var configDigest = doc.RootElement.GetProperty("config").GetProperty("digest").GetString();
                using var cfg = await GetJson($"blobs/{configDigest}", "application/json");
                if (!cfg.RootElement.TryGetProperty("config", out var c)) return null;

                if (c.TryGetProperty("Env", out var env))
                    foreach (var e in env.EnumerateArray())
                        if (e.GetString() is { } s && s.StartsWith("WEBDESK_COMMIT=", StringComparison.Ordinal))
                            return s["WEBDESK_COMMIT=".Length..].Trim() is { Length: >= 7 } v ? v : null;
                if (c.TryGetProperty("Labels", out var labels) && labels.ValueKind == System.Text.Json.JsonValueKind.Object
                    && labels.TryGetProperty("org.opencontainers.image.revision", out var rev) && rev.GetString() is { Length: >= 7 } r)
                    return r;
                return null;
            }
            finally { doc.Dispose(); }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not read the published image revision from GHCR.");
            return null;
        }
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
