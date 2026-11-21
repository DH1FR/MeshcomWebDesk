using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Checks GitHub Releases at startup and every 24 hours, exposes the latest version string
/// if it is newer than the running assembly version.
/// Uses the public GitHub REST API (no auth required for public repos).
/// </summary>
public sealed class UpdateCheckService : IHostedService, IDisposable
{
    private const string ReleaseUrl = "https://api.github.com/repos/DH1FR/MeshcomWebDesk/releases/latest";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly ILogger<UpdateCheckService> _logger;
    private CancellationToken _stoppingToken;
    private Timer? _timer;

    private static readonly Version _current =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Latest release tag from GitHub, e.g. "v1.7.7". Null if check failed or already up-to-date.</summary>
    public string? LatestVersion { get; private set; }

    /// <summary>URL to the GitHub release page.</summary>
    public string? ReleasePageUrl { get; private set; }

    /// <summary>True after the user dismissed the banner – survives layout re-initialization.</summary>
    public bool Dismissed { get; private set; }

    /// <summary>Raised once when a newer version is found.</summary>
    public event Action? OnUpdateAvailable;

    /// <summary>Marks the banner as dismissed.</summary>
    public void Dismiss() => Dismissed = true;

    /// <summary>When the last check finished (UTC) and whether it failed (offline, GitHub rate limit).</summary>
    public DateTime? LastCheckUtc { get; private set; }
    public bool LastCheckFailed { get; private set; }

    /// <summary>Asks GitHub right now (the "Check now" button), without waiting for the timer.</summary>
    public Task CheckNowAsync(CancellationToken ct = default) => CheckAsync(ct);

    public UpdateCheckService(ILogger<UpdateCheckService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingToken = cancellationToken;
        _timer = new Timer(
            _ => _ = CheckAsync(_stoppingToken),
            null,
            TimeSpan.Zero,
            CheckInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task CheckAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MeshcomWebDesk/" + _current.ToString(3));
            http.Timeout = TimeSpan.FromSeconds(10);

            var release = await http.GetFromJsonAsync<GitHubRelease>(ReleaseUrl, ct);
            if (release is null || string.IsNullOrEmpty(release.TagName)) { LastCheckFailed = true; LastCheckUtc = DateTime.UtcNow; return; }

            // Parse tag, strip leading 'v'
            var tag = release.TagName.TrimStart('v');
            if (!Version.TryParse(tag, out var latest)) { LastCheckFailed = true; LastCheckUtc = DateTime.UtcNow; return; }
            LastCheckFailed = false; LastCheckUtc = DateTime.UtcNow;

            if (latest > _current)
            {
                LatestVersion  = release.TagName;
                ReleasePageUrl = release.HtmlUrl;
                _logger.LogInformation("Update available: {Latest} (running {Current})", release.TagName, _current.ToString(3));
                OnUpdateAvailable?.Invoke();
            }
            else
            {
                _logger.LogDebug("Up to date: {Current}", _current.ToString(3));
            }
        }
        catch (Exception ex)
        {
            LastCheckFailed = true; LastCheckUtc = DateTime.UtcNow;
            _logger.LogDebug(ex, "Update check failed (offline or rate-limited).");
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]  public string? TagName  { get; set; }
        [JsonPropertyName("html_url")]  public string? HtmlUrl  { get; set; }
    }
}
