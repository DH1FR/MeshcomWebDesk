using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Checks GitHub Releases once at startup and exposes the latest version string
/// if it is newer than the running assembly version.
/// Uses the public GitHub REST API (no auth required for public repos).
/// </summary>
public sealed class UpdateCheckService : IHostedService
{
    private const string ReleaseUrl = "https://api.github.com/repos/DH1FR/MeshcomWebDesk/releases/latest";

    private readonly ILogger<UpdateCheckService> _logger;

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

    public UpdateCheckService(ILogger<UpdateCheckService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => CheckAsync(cancellationToken), cancellationToken);
        await Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CheckAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MeshcomWebDesk/" + _current.ToString(3));
            http.Timeout = TimeSpan.FromSeconds(10);

            var release = await http.GetFromJsonAsync<GitHubRelease>(ReleaseUrl, ct);
            if (release is null || string.IsNullOrEmpty(release.TagName)) return;

            // Parse tag, strip leading 'v'
            var tag = release.TagName.TrimStart('v');
            if (!Version.TryParse(tag, out var latest)) return;

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
            _logger.LogDebug(ex, "Update check failed (offline or rate-limited).");
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]  public string? TagName  { get; set; }
        [JsonPropertyName("html_url")]  public string? HtmlUrl  { get; set; }
    }
}
