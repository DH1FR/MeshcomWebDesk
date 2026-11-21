using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using MeshcomWebDesk.Helpers;

namespace MeshcomWebDesk.Services;

/// <summary>How a node was found.</summary>
public enum DiscoverySource
{
    /// <summary>Hostname / mDNS lookup of the node's callsign.</summary>
    Name,
    /// <summary>It already sent UDP packets to this PC.</summary>
    Passive,
    /// <summary>Found by scanning the local network for the node's web server.</summary>
    Scan,
}

/// <summary>A device that is probably a MeshCom node. <see cref="Callsign"/> is known only when the node told it.</summary>
public sealed record DiscoveredNode(string Ip, string? Callsign, DiscoverySource Source);

/// <summary>
/// Finds MeshCom nodes in the local network so the setup wizard can suggest an IP address.
/// <para>
/// A node on ext-UDP never announces itself, so there is no single reliable method. Three are combined:
/// </para>
/// <list type="number">
///   <item><b>Name</b> – the firmware uses the callsign as DHCP hostname and mDNS name
///   (<c>OE1XYZ-1.local</c>), so resolving it yields the node's IP. Cheap and exact, but needs the callsign.</item>
///   <item><b>Passive</b> – sender IPs of UDP packets this PC already received.</item>
///   <item><b>Scan</b> – on demand only: probes the PC's own private /24 networks on TCP port 80 and
///   recognises a node by its page title <c>&lt;CALL&gt; - Meshcom</c>. Finds nodes with the web server on.</item>
/// </list>
/// Discovery only reads; it never changes a setting and never sends anything but a plain HTTP GET.
/// </summary>
public sealed partial class NodeDiscoveryService
{
    private readonly MeshcomUdpService _udp;

    public NodeDiscoveryService(MeshcomUdpService udp) => _udp = udp;

    /// <summary>The firmware's page title: "&lt;node_call&gt; - Meshcom" (web_functions.cpp).</summary>
    [GeneratedRegex(@"<title>\s*([A-Za-z0-9\-]{3,16})\s*-\s*Meshcom\s*</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitlePattern();

    /// <summary>Valid DNS label: callsign with optional SSID.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-]{1,14}$")]
    private static partial Regex HostnamePattern();

    private const int ScanConcurrency = 48;

    // ── 1. Name lookup ────────────────────────────────────────────────────

    /// <summary>Resolves <c>&lt;callsign&gt;.local</c> (mDNS) and the plain hostname (router DNS / DHCP name).</summary>
    public async Task<IReadOnlyList<DiscoveredNode>> LookupByCallsignAsync(string? callsign, CancellationToken ct = default)
    {
        var cs = callsign?.Trim();
        if (string.IsNullOrEmpty(cs) || !HostnamePattern().IsMatch(cs)) return [];

        var lookups = await Task.WhenAll(new[] { $"{cs}.local", cs }.Select(n => ResolveAsync(n, ct)));
        return lookups
            .SelectMany(a => a)
            .Distinct()
            .Select(ip => new DiscoveredNode(ip, cs.ToUpperInvariant(), DiscoverySource.Name))
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> ResolveAsync(string name, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2.5));
            var addresses = await Dns.GetHostAddressesAsync(name, AddressFamily.InterNetwork, cts.Token);
            return addresses
                .Where(a => !IPAddress.IsLoopback(a) && !a.Equals(IPAddress.Any) && !NetworkHelper.IsLocalAddress(a))
                .Select(a => a.ToString())
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // ── 2. Passive ────────────────────────────────────────────────────────

    /// <summary>IPs that already sent UDP packets to this PC (loopback excluded).</summary>
    public IReadOnlyList<DiscoveredNode> PassiveSenders() =>
        _udp.RawRxBySource
            .Where(kv => kv.Value > 0 && kv.Key != IPAddress.Loopback.ToString())
            .Select(kv => new DiscoveredNode(kv.Key, null, DiscoverySource.Passive))
            .ToList();

    // ── 3. Scan (on demand) ───────────────────────────────────────────────

    /// <summary>Number of addresses a scan would probe (for the confirmation text).</summary>
    public int ScanHostCount() => ScanHosts().Count;

    /// <summary>
    /// Probes every host of the PC's private /24 networks on TCP port 80 and keeps those that identify
    /// themselves as MeshCom node. <paramref name="progress"/> gets (done, total).
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredNode>> ScanAsync(Action<int, int>? progress = null, CancellationToken ct = default)
    {
        var hosts = ScanHosts();
        var found = new ConcurrentBag<DiscoveredNode>();
        var done = 0;
        using var gate = new SemaphoreSlim(ScanConcurrency);

        var tasks = hosts.Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (await ProbeAsync(ip, ct) is { } node) found.Add(node);
            }
            finally
            {
                gate.Release();
                progress?.Invoke(Interlocked.Increment(ref done), hosts.Count);
            }
        }).ToList();

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { /* cancelled: return what was found so far */ }

        return found.OrderBy(n => IpSortKey(n.Ip)).ToList();
    }

    private static List<IPAddress> ScanHosts()
    {
        var own = NetworkHelper.GetLocalIpv4Networks().Select(n => n.Address).ToHashSet();
        var hosts = new List<IPAddress>();
        foreach (var net in NetworkHelper.GetScanNetworks())
        {
            var b = net.GetAddressBytes();
            for (var i = 1; i <= 254; i++)
            {
                var ip = new IPAddress([b[0], b[1], b[2], (byte)i]);
                if (!own.Contains(ip) && !hosts.Contains(ip)) hosts.Add(ip);
            }
        }
        return hosts;
    }

    private static async Task<DiscoveredNode?> ProbeAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromMilliseconds(500));
            using var client = new TcpClient();
            await client.ConnectAsync(ip, 80, connectCts.Token);

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromMilliseconds(1800));
            var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes($"GET / HTTP/1.0\r\nHost: {ip}\r\nUser-Agent: MeshcomWebDesk-discovery\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(request, readCts.Token);

            var buffer = new byte[4096];
            var text = new StringBuilder();
            int n;
            while (text.Length < 16384 && (n = await stream.ReadAsync(buffer, readCts.Token)) > 0)
            {
                text.Append(Encoding.UTF8.GetString(buffer, 0, n));
                var m = TitlePattern().Match(text.ToString());
                if (m.Success)
                    return new DiscoveredNode(ip.ToString(), m.Groups[1].Value.ToUpperInvariant(), DiscoverySource.Scan);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static long IpSortKey(string ip) =>
        IPAddress.TryParse(ip, out var a) ? a.GetAddressBytes().Aggregate(0L, (acc, b) => acc * 256 + b) : 0;
}
