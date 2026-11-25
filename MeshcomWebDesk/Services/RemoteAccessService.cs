using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Helpers;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>Where an address offered for a phone/tablet comes from.</summary>
public enum RemoteUrlKind
{
    /// <summary>Entered by the user in Settings (<see cref="MeshcomSettings.PublicBaseUrl"/>).</summary>
    Configured,
    /// <summary>The address the browser is using right now (works behind Docker / a reverse proxy).</summary>
    Current,
    /// <summary>A LAN address of this PC combined with a port WebDesk listens on.</summary>
    Lan,
    /// <summary>The computer name instead of an IP address.</summary>
    Host,
}

/// <summary>An address a phone or tablet can use to open WebDesk.</summary>
public sealed record RemoteUrl(string Url, RemoteUrlKind Kind, bool Secure);

/// <summary>
/// Works out the addresses under which this WebDesk can be reached from another device in the same network, so the
/// "open on phone/tablet" dialog can show them as a QR code. https addresses come first.
/// <para>
/// Sources, best first: an address configured in Settings, the address of the browser that asks (unless it is
/// localhost), and the addresses WebDesk listens on expanded to the PC's LAN IPs. In a container the PC's own
/// addresses are the container's, so only the configured/current address is offered there.
/// </para>
/// </summary>
public sealed class RemoteAccessService
{
    private readonly IServer _server;
    private readonly IConfiguration _config;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;

    public RemoteAccessService(IServer server, IConfiguration config, IOptionsMonitor<MeshcomSettings> settings)
    {
        _server   = server;
        _config   = config;
        _settings = settings;
    }

    /// <summary>True when WebDesk runs in a container: the container's IPs are not reachable from the LAN.</summary>
    public static bool InContainer =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

    private int? _httpsPort;
    private bool _httpsPortKnown;

    /// <summary>
    /// The port of the https address WebDesk itself listens on (profile <c>lan-https</c> / the docker https overlay),
    /// or null when it only speaks http. Used to send remote browsers there once a password is set.
    /// </summary>
    public int? FindHttpsPort()
    {
        if (_httpsPortKnown) return _httpsPort;
        var addresses = ListeningAddresses().ToList();
        // The server fills its address list once it has started; before that nothing can be told.
        if (addresses.Count == 0) return null;
        _httpsPort = addresses.Where(a => a.Scheme == Uri.UriSchemeHttps).Select(a => (int?)a.Port).FirstOrDefault();
        _httpsPortKnown = true;
        return _httpsPort;
    }

    /// <summary>All addresses worth offering, https first, without duplicates.</summary>
    /// <param name="currentBaseUri">The base URI of the browser asking (<c>NavigationManager.BaseUri</c>).</param>
    public IReadOnlyList<RemoteUrl> GetCandidates(string currentBaseUri)
    {
        var found = new List<RemoteUrl>();

        // 1. Entered by the user
        var configured = _settings.CurrentValue.PublicBaseUrl?.Trim();
        if (!string.IsNullOrEmpty(configured) && TryHttp(configured, out var cu))
            found.Add(new(Origin(cu), RemoteUrlKind.Configured, IsSecure(cu)));

        // 2. The address this browser used, if a phone could use it too
        if (TryHttp(currentBaseUri, out var current) && !IsLoopbackHost(current.Host))
            found.Add(new(Origin(current), RemoteUrlKind.Current, IsSecure(current)));

        // 3. Where WebDesk listens, expanded to this PC's LAN addresses (not inside a container)
        if (!InContainer)
        {
            var lanIps = NetworkHelper.GetReachableAddresses();   // real LAN adapters first, no virtual ones
            foreach (var address in ListeningAddresses())
            {
                var secure = IsSecure(address);
                if (IsWildcardHost(address.Host))
                {
                    foreach (var ip in lanIps)
                        found.Add(new($"{address.Scheme}://{ip}:{address.Port}/", RemoteUrlKind.Lan, secure));
                    found.Add(new($"{address.Scheme}://{Dns.GetHostName()}:{address.Port}/", RemoteUrlKind.Host, secure));
                }
                else if (!IsLoopbackHost(address.Host))
                {
                    found.Add(new(Origin(address), RemoteUrlKind.Lan, secure));
                }
            }
        }

        return found
            .GroupBy(f => f.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(f => f.Kind).First())
            .OrderBy(f => f.Kind == RemoteUrlKind.Configured ? 0 : 1)
            .ThenBy(f => f.Secure ? 0 : 1)
            .ThenBy(f => f.Kind)
            .ToList();
    }

    /// <summary>
    /// Everything WebDesk listens on: the server's own list plus the configured <c>Urls</c> and
    /// <c>Kestrel:Endpoints:*:Url</c> (Kestrel leaves the server list empty for endpoints that come from configuration,
    /// which is where an https LAN port is defined).
    /// </summary>
    private IEnumerable<Uri> ListeningAddresses()
    {
        var raw = new List<string>();
        var feature = _server.Features.Get<IServerAddressesFeature>();
        if (feature is not null) raw.AddRange(feature.Addresses);

        // Endpoints defined in Kestrel's configuration replace "Urls" (the server logs "Overriding address(es)").
        var endpoints = _config.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(e => e["Url"]).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!).ToList();
        if (endpoints.Count > 0)
        {
            raw.AddRange(endpoints);
        }
        else
        {
            var urls = _config["Urls"] ?? _config["urls"];
            if (!string.IsNullOrWhiteSpace(urls))
                raw.AddRange(urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in raw)
        {
            // Kestrel allows "+" and "*" as host; Uri cannot parse them.
            var text = address.Replace("://+:", "://0.0.0.0:").Replace("://*:", "://0.0.0.0:");
            if (seen.Add(text) && TryHttp(text, out var uri)) yield return uri;
        }
    }

    private static bool TryHttp(string text, out Uri uri)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            uri = u;
            return true;
        }
        uri = null!;
        return false;
    }

    private static string Origin(Uri u) => $"{u.Scheme}://{u.Authority}/";

    private static bool IsSecure(Uri u) => u.Scheme == Uri.UriSchemeHttps;

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));

    private static bool IsWildcardHost(string host) =>
        IPAddress.TryParse(host.Trim('[', ']'), out var ip) && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));
}
