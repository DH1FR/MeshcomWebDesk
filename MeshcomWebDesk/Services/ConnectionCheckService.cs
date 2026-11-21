using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MeshcomWebDesk.Helpers;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Checks the node connection settings for typical mistakes and explains what to change.
/// <para>
/// The check only <b>reports</b>: it never changes a WebDesk setting and never sends a command to the
/// node. Node console commands are returned as text the user can copy.
/// </para>
/// <para>
/// It knows two kinds of counterparts: a WiFi/Ethernet MeshCom node (ext-udp, fixed port 1799) and the
/// MeshCom UDP Bluetooth hub (emulates ext-udp and the NET console; on the same PC it listens on
/// port 1798 next to WebDesk's 1799).
/// </para>
/// </summary>
public sealed partial class ConnectionCheckService
{
    private readonly MeshcomUdpService _udp;
    private readonly NodeManager _nodes;
    private readonly ConsoleCommandHelperService _console;
    private readonly LanguageService _loc;
    private readonly NodeDiscoveryService _discovery;
    private readonly NodeConsoleFixService _fix;
    private readonly MeshcomWebDesk.Services.Kiss.KissClientService _kiss;

    public ConnectionCheckService(
        MeshcomUdpService udp,
        NodeManager nodes,
        ConsoleCommandHelperService console,
        LanguageService loc,
        NodeDiscoveryService discovery,
        NodeConsoleFixService fix,
        MeshcomWebDesk.Services.Kiss.KissClientService kiss)
    {
        _kiss      = kiss;
        _fix       = fix;
        _udp       = udp;
        _nodes     = nodes;
        _console   = console;
        _loc       = loc;
        _discovery = discovery;
    }

    private string T(string de, string en) => _loc.T(de, en);
    private string TF(string de, string en, params object?[] args) => _loc.TF(de, en, args);

    /// <summary>Base callsign: letters/digits, at least one of each (e.g. OE5HOO, DH1FR).</summary>
    [GeneratedRegex(@"^(?=.*\d)(?=.*[A-Za-z])[A-Za-z0-9]{3,7}$")]
    private static partial Regex BaseCallsignPattern();

    /// <summary>Number of own-node packets without a callsign match before the callsign is called doubtful.</summary>
    private const int DoubtAfterPackets = 5;

    /// <summary>
    /// True when the counterpart is most probably the Bluetooth hub: it runs on this PC (loopback or
    /// one of the PC's own addresses) or uses the hub's default port 1798. A real node is always on
    /// another machine and fixed to port 1799.
    /// </summary>
    public static bool LooksLikeHub(string deviceIp, int devicePort)
        => (IPAddress.TryParse(deviceIp, out var ip) && NetworkHelper.IsLocalAddress(ip)) || devicePort == 1798;

    /// <summary>Local PC addresses to enter as the node's ext-udp target, best match for the node's subnet first.</summary>
    public static IReadOnlyList<string> SuggestPcIps(string deviceIp)
    {
        var nets = NetworkHelper.GetLocalIpv4Networks();
        if (IPAddress.TryParse(deviceIp, out var dev))
        {
            var same = nets.Where(n => NetworkHelper.IsInSubnet(dev, n.Address, n.Mask))
                           .Select(n => n.Address.ToString()).ToList();
            if (same.Count > 0) return same;
        }

        // No address in the node's subnet: list real LAN adapters first, VPN/virtual ones (Tailscale, WSL, Docker …)
        // last, so the first suggestion is the most plausible one.
        var lan = NetworkHelper.GetScanNetworks();
        bool IsLan(System.Net.IPAddress a)
        {
            var b = a.GetAddressBytes();
            return lan.Any(l => l.GetAddressBytes().Take(3).SequenceEqual(b.Take(3)));
        }
        return nets.OrderBy(n => IsLan(n.Address) ? 0 : 1).Select(n => n.Address.ToString()).ToList();
    }

    /// <summary>Runs all checks against <paramref name="input"/> (the current, possibly unsaved form values).</summary>
    public async Task<IReadOnlyList<CheckResult>> RunAsync(ConnectionCheckInput input, CancellationToken ct = default)
    {
        var results = new List<CheckResult>();

        var listenOk = IPAddress.TryParse(input.ListenIp?.Trim(), out var listenIp) && listenIp.AddressFamily == AddressFamily.InterNetwork;
        var deviceOk = IPAddress.TryParse(input.DeviceIp?.Trim(), out var deviceIp) && deviceIp.AddressFamily == AddressFamily.InterNetwork;
        var listenPortOk = input.ListenPort is >= 1 and <= 65535;
        var devicePortOk = input.DevicePort is >= 1 and <= 65535;

        // ── 1. Formats ────────────────────────────────────────────────────
        if (input.IsPrimary && !listenOk)
            results.Add(new(CheckSeverity.Error,
                T("Listen IP ist keine gültige IPv4-Adresse", "Listen IP is not a valid IPv4 address"),
                T("Verwenden Sie 0.0.0.0 (alle Netzwerkkarten) oder die IP-Adresse dieses PCs.",
                  "Use 0.0.0.0 (all network adapters) or the IP address of this PC.")));
        if (!deviceOk)
            results.Add(new(CheckSeverity.Error,
                T("Device IP ist keine gültige IPv4-Adresse", "Device IP is not a valid IPv4 address"),
                T("Tragen Sie die IP-Adresse des Nodes ein (z. B. im Router oder am Node-Display zu finden).",
                  "Enter the IP address of the node (shown in your router or on the node display).")));
        if (input.IsPrimary && !listenPortOk)
            results.Add(new(CheckSeverity.Error,
                T("Listen Port ist ungültig", "Listen Port is invalid"),
                T("Erlaubt sind Werte von 1 bis 65535, üblich ist 1799.", "Allowed values are 1 to 65535, usually 1799.")));
        if (!devicePortOk)
            results.Add(new(CheckSeverity.Error,
                T("Device Port ist ungültig", "Device Port is invalid"),
                T("Erlaubt sind Werte von 1 bis 65535, üblich ist 1799.", "Allowed values are 1 to 65535, usually 1799.")));

        var deviceIsLocal = deviceOk && NetworkHelper.IsLocalAddress(deviceIp!);
        var hubLikely = deviceOk && devicePortOk && LooksLikeHub(input.DeviceIp.Trim(), input.DevicePort);
        var pcIps = NetworkHelper.GetLocalIpv4Addresses();

        // ── 2. Counterpart type ──────────────────────────────────────────
        if (deviceOk && devicePortOk)
        {
            if (deviceIsLocal)
                results.Add(new(CheckSeverity.Info,
                    T("Bluetooth-Hub auf diesem PC erkannt", "Bluetooth hub on this PC detected"),
                    T("Die Device IP zeigt auf diesen PC – das ist nur mit dem MeshCom UDP Bluetooth Hub sinnvoll. Der Hub nutzt neben WebDesk den Port 1798.",
                      "The Device IP points to this PC – that only makes sense with the MeshCom UDP Bluetooth hub. Next to WebDesk the hub uses port 1798.")));
            else if (input.DevicePort == 1798)
                results.Add(new(CheckSeverity.Info,
                    T("Device Port 1798: Bluetooth-Hub auf einem anderen Rechner?", "Device Port 1798: Bluetooth hub on another machine?"),
                    T("Port 1798 ist der Standard des MeshCom UDP Bluetooth Hubs. Ein WLAN-/LAN-Node ist fest auf 1799 eingestellt.",
                      "Port 1798 is the default of the MeshCom UDP Bluetooth hub. A WiFi/LAN node is fixed to 1799.")));
            else if (input.DevicePort != 1799)
                results.Add(new(CheckSeverity.Warning,
                    T("Device Port weicht von 1799 ab", "Device Port differs from 1799"),
                    T("Ein MeshCom-Node lauscht fest auf UDP 1799 und kann das nicht ändern. Nur der Bluetooth-Hub (1798) oder eine Port-Weiterleitung rechtfertigen einen anderen Port.",
                      "A MeshCom node listens on UDP 1799 only and cannot change it. Only the Bluetooth hub (1798) or a port forward justify another port.")));
        }

        // ── 2b. Clashes with the other nodes ─────────────────────────────
        // All nodes share one UDP socket; an incoming packet is assigned to a node by its source IP
        // only (NodeManager.ResolveNodeByIp). Two nodes with the same Device IP – e.g. two Bluetooth
        // hubs on 127.0.0.1 with different ports – cannot be told apart.
        if (input.Others is { Count: > 0 })
        {
            foreach (var o in input.Others)
            {
                if (deviceOk && IPAddress.TryParse(o.DeviceIp?.Trim(), out var oip) && oip.Equals(deviceIp))
                    results.Add(new(CheckSeverity.Error,
                        TF("Device IP gleich wie bei Node \"{0}\"", "Device IP is the same as on node \"{0}\"", o.Name),
                        T("WebDesk ordnet eingehende Pakete einem Node über dessen IP-Adresse zu (nicht über den Port). Zwei Nodes mit derselben IP – auch zwei Bluetooth-Hubs auf demselben PC – lassen sich nicht unterscheiden. Verwenden Sie für jeden Node eine eigene IP (ein Hub auf einem anderen Rechner oder eine zweite PC-Adresse).",
                          "WebDesk assigns incoming packets to a node by its IP address (not by port). Two nodes with the same IP – also two Bluetooth hubs on the same PC – cannot be told apart. Give every node its own IP (a hub on another machine or a second PC address).")));
                if (!string.IsNullOrWhiteSpace(input.Callsign) &&
                    string.Equals(input.Callsign.Trim(), o.Callsign?.Trim(), StringComparison.OrdinalIgnoreCase))
                    results.Add(new(CheckSeverity.Warning,
                        TF("Rufzeichen gleich wie bei Node \"{0}\"", "Callsign is the same as on node \"{0}\"", o.Name),
                        T("Jeder Node hat ein eigenes Rufzeichen mit eigener SSID. Gleiche Rufzeichen verwechseln eigene Nachrichten.",
                          "Every node has its own callsign with its own SSID. Identical callsigns mix up own messages.")));
            }
        }

        // Shared socket: only the primary node owns the Listen IP / Listen Port fields.
        if (!input.IsPrimary)
            results.Add(_udp.Status.IsListening
                ? new(CheckSeverity.Ok,
                    TF("Gemeinsamer UDP-Socket ist offen auf {0}", "Shared UDP socket is open on {0}", _udp.Status.BoundListenEndpoint))
                : new(CheckSeverity.Error,
                    T("Gemeinsamer UDP-Socket ist nicht offen (roter UDP-Status)", "Shared UDP socket is not open (red UDP status)"),
                    T("Alle Nodes nutzen den Socket des Primär-Node. Prüfen Sie Listen IP / Listen Port im Abschnitt Verbindung.",
                      "All nodes use the primary node's socket. Check Listen IP / Listen Port in the Connection section.")));

        // ── 3. Listen IP ─────────────────────────────────────────────────
        if (input.IsPrimary && listenOk)
        {
            var listenAny = listenIp!.Equals(IPAddress.Any);
            if (listenAny)
            {
                results.Add(new(CheckSeverity.Ok,
                    T("Listen IP 0.0.0.0: WebDesk lauscht auf allen Netzwerkkarten", "Listen IP 0.0.0.0: WebDesk listens on all network adapters")));
            }
            else if (!NetworkHelper.IsLocalAddress(listenIp))
            {
                var isNodeIp = deviceOk && listenIp.Equals(deviceIp);
                results.Add(new(CheckSeverity.Error,
                    isNodeIp
                        ? T("Listen IP ist die IP des Nodes – nicht die dieses PCs", "Listen IP is the node's IP, not this PC's")
                        : T("Listen IP gehört nicht zu diesem PC", "Listen IP does not belong to this PC"),
                    TF("WebDesk kann keinen Socket auf {0} öffnen, deshalb kommen keine Daten an (roter UDP-Status). Tragen Sie 0.0.0.0 ein oder eine IP dieses PCs: {1}",
                       "WebDesk cannot open a socket on {0}, so no data arrives (red UDP status). Enter 0.0.0.0 or an IP of this PC: {1}",
                       input.ListenIp.Trim(), pcIps.Count > 0 ? string.Join(", ", pcIps) : "–")));
            }
            else if (NetworkHelper.IsLoopback(listenIp) && deviceOk && !deviceIsLocal)
            {
                results.Add(new(CheckSeverity.Error,
                    T("Listen IP 127.0.0.1 empfängt keine Pakete aus dem Netzwerk", "Listen IP 127.0.0.1 does not receive packets from the network"),
                    TF("Der Node ({0}) liegt nicht auf diesem PC. Tragen Sie 0.0.0.0 oder eine IP dieses PCs ein: {1}",
                       "The node ({0}) is not on this PC. Enter 0.0.0.0 or an IP of this PC: {1}",
                       input.DeviceIp.Trim(), pcIps.Count > 0 ? string.Join(", ", pcIps) : "–")));
            }
            else if (NetworkHelper.IsLoopback(listenIp))
            {
                results.Add(new(CheckSeverity.Ok,
                    T("Listen IP 127.0.0.1: nur lokale Programme (Hub auf diesem PC) erreichen WebDesk", "Listen IP 127.0.0.1: only local programs (hub on this PC) can reach WebDesk")));
            }
            else
            {
                results.Add(new(CheckSeverity.Ok,
                    TF("Listen IP {0} gehört zu diesem PC", "Listen IP {0} belongs to this PC", input.ListenIp.Trim())));
                if (deviceOk && !deviceIsLocal)
                {
                    var net = NetworkHelper.GetLocalIpv4Networks().FirstOrDefault(n => n.Address.Equals(listenIp));
                    if (net.Address is not null && !NetworkHelper.IsInSubnet(deviceIp!, net.Address, net.Mask))
                        results.Add(new(CheckSeverity.Warning,
                            T("Listen IP liegt nicht im Netz des Nodes", "Listen IP is not in the node's network"),
                            TF("Die Netzwerkkarte {0} liegt nicht im selben Subnetz wie der Node {1}. Der Node erreicht diese Adresse eventuell nicht. Im Zweifel 0.0.0.0 verwenden.",
                               "The adapter {0} is not in the same subnet as the node {1}. The node may not reach this address. If in doubt use 0.0.0.0.",
                               input.ListenIp.Trim(), input.DeviceIp.Trim())));
                }
            }
        }

        // ── 4. Device IP ─────────────────────────────────────────────────
        var pingOk = false;
        if (deviceOk)
        {
            if (deviceIp!.Equals(IPAddress.Any) || NetworkHelper.IsBroadcastOrMulticast(deviceIp))
            {
                results.Add(new(CheckSeverity.Error,
                    T("Device IP ist keine Node-Adresse", "Device IP is not a node address"),
                    T("0.0.0.0, Broadcast- und Multicast-Adressen sind nicht erlaubt. Tragen Sie die konkrete IP des Nodes ein.",
                      "0.0.0.0, broadcast and multicast addresses are not allowed. Enter the node's own IP address.")));
            }
            else if (!deviceIsLocal)
            {
                var sameNet = NetworkHelper.GetLocalIpv4Networks().Any(n => NetworkHelper.IsInSubnet(deviceIp, n.Address, n.Mask));
                if (sameNet)
                    results.Add(new(CheckSeverity.Ok,
                        TF("Device IP {0} liegt im selben Netz wie dieser PC", "Device IP {0} is in the same network as this PC", input.DeviceIp.Trim())));
                else
                    results.Add(new(CheckSeverity.Warning,
                        TF("Device IP {0} liegt in einem anderen Netz als dieser PC", "Device IP {0} is in a different network than this PC", input.DeviceIp.Trim()),
                        TF("Diesen PC erreichen Sie über {0}. Funktioniert nur, wenn Ihr Router UDP zwischen beiden Netzen weiterleitet (VLAN, VPN, Docker-Netz).",
                           "This PC is reachable at {0}. This only works if your router forwards UDP between both networks (VLAN, VPN, Docker network).",
                           pcIps.Count > 0 ? string.Join(", ", pcIps) : "–")));

                var reachable = await PingAsync(deviceIp, ct);
                pingOk = reachable;
                results.Add(reachable
                    ? new(CheckSeverity.Ok, T("Node antwortet auf Ping", "Node answers ping"))
                    : new(CheckSeverity.Info,
                        T("Node antwortet nicht auf Ping", "Node does not answer ping"),
                        T("Das ist nicht zwingend ein Fehler (viele Geräte blockieren Ping). Stimmt die IP wirklich? Prüfen Sie Router oder Node-Display; WLAN-Nodes wechseln die IP, wenn sie nicht fest eingestellt ist.",
                          "This is not necessarily an error (many devices block ping). Is the IP really correct? Check your router or the node display; WiFi nodes change IP unless it is fixed.")));

                // The node answers to its callsign as hostname (DHCP name / mDNS): suggest the IP it really has.
                if (!reachable)
                {
                    var byName = await _discovery.LookupByCallsignAsync(input.Callsign, ct);
                    var other = byName.FirstOrDefault(n => n.Ip != deviceIp.ToString());
                    if (other is not null)
                        results.Add(new(CheckSeverity.Warning,
                            TF("Ein Gerät namens {0} wurde unter {1} gefunden", "A device named {0} was found at {1}", other.Callsign, other.Ip),
                            TF("Die Device IP ist {0}. Falls das Ihr Node ist, tragen Sie {1} als Device IP ein.",
                               "The Device IP is {0}. If that is your node, enter {1} as Device IP.",
                               input.DeviceIp.Trim(), other.Ip)));
                }
            }
        }

        // ── 5. Ports ─────────────────────────────────────────────────────
        if (listenPortOk && devicePortOk)
        {
            if (deviceIsLocal && input.ListenPort == input.DevicePort)
                results.Add(new(CheckSeverity.Error,
                    TF("Port-Konflikt: Hub und WebDesk nutzen beide UDP {0}", "Port conflict: hub and WebDesk both use UDP {0}", input.ListenPort),
                    T("Auf einem PC kann nur ein Programm einen UDP-Port öffnen. Stellen Sie den Hub auf Port 1798 (Device Port 1798 in WebDesk, Listen Port 1799).",
                      "Only one program per PC can open a UDP port. Set the hub to port 1798 (Device Port 1798 in WebDesk, Listen Port 1799).")));
            else if (input.IsPrimary && !deviceIsLocal && deviceOk && input.ListenPort != 1799 && input.DevicePort != 1798)
                results.Add(new(CheckSeverity.Warning,
                    T("Listen Port weicht von 1799 ab", "Listen Port differs from 1799"),
                    T("Der Node sendet immer an den Port, der in seiner ext-UDP-Einstellung steht (1799). Ohne Port-Weiterleitung kommt bei einem anderen Listen Port nichts an.",
                      "The node always sends to port 1799. With a different Listen Port nothing arrives unless you forward the port.")));
        }

        // ── 6. Socket state ──────────────────────────────────────────────
        if (input.IsPrimary)
            await CheckSocketAsync(input, listenOk, listenPortOk, listenIp, results);

        // ── 7. Hub on this PC: is something listening on the Device Port? ─
        if (deviceIsLocal && devicePortOk && input.ListenPort != input.DevicePort)
        {
            var inUse = IsUdpPortInUse(input.DevicePort, IPAddress.Any, deviceIp!);
            results.Add(inUse
                ? new(CheckSeverity.Ok,
                    TF("Auf diesem PC lauscht ein Programm auf UDP {0} (Hub läuft)", "A program on this PC listens on UDP {0} (hub is running)", input.DevicePort))
                : new(CheckSeverity.Warning,
                    TF("Auf diesem PC lauscht nichts auf UDP {0}", "Nothing on this PC listens on UDP {0}", input.DevicePort),
                    T("Ist der Bluetooth-Hub gestartet? Der Hub-Port (\"UDP port\" im Hub-Fenster) muss dem Device Port entsprechen.",
                      "Is the Bluetooth hub started? The hub's \"UDP port\" must equal the Device Port.")));
        }

        // ── 8. KISS reachability ─────────────────────────────────────────
        if (input.Kiss && deviceOk && !deviceIsLocal)
        {
            // The node serves ONE KISS client: if WebDesk itself holds the slot, a second probe connection is
            // refused although KISS is on - so ask WebDesk's own KISS connection first.
            var nodeKey = input.NodeId ?? _nodes.PrimaryNode?.Id ?? Guid.Empty;
            var kissState = _kiss.GetStatus(nodeKey).State;
            var ownKiss = nodeKey != Guid.Empty && _kiss.IsConnected(nodeKey);
            var kissBusy = kissState == MeshcomWebDesk.Services.Kiss.KissConnectionState.SlotBusy;
            var kissOk = ownKiss || kissBusy || await TcpProbeAsync(deviceIp!, 8001, ct);
            results.Add(ownKiss
                ? new(CheckSeverity.Ok, T("KISS/TCP: WebDesk ist mit dem Node über KISS verbunden", "KISS/TCP: WebDesk is connected to the node via KISS"))
                : kissBusy
                ? new(CheckSeverity.Warning,
                    T("KISS/TCP: Port 8001 ist belegt – ein anderer KISS-Client hängt am Node", "KISS/TCP: port 8001 is busy – another KISS client is connected to the node"),
                    T("Der Node bedient nur einen KISS-Client. Beenden Sie die andere Verbindung (z. B. eine andere WebDesk-Instanz oder ein KISS-Programm), dann verbindet WebDesk sich selbst.",
                      "The node serves only one KISS client. Close the other connection (e.g. another WebDesk instance or a KISS program); WebDesk then connects by itself."))
                : kissOk
                ? new(CheckSeverity.Ok, T("KISS/TCP: Node nimmt Verbindungen auf Port 8001 an", "KISS/TCP: node accepts connections on port 8001"))
                : new(CheckSeverity.Warning,
                    T("KISS/TCP: Node nimmt auf Port 8001 keine Verbindung an", "KISS/TCP: node does not accept connections on port 8001"),
                    T("Am Node muss KISS/TCP eingeschaltet sein (--kiss on). Nur ESP32-Nodes mit passender Firmware bieten KISS.",
                      "KISS/TCP must be switched on at the node (--kiss on). Only ESP32 nodes with suitable firmware offer KISS."),
                    ["--kiss on"]));
        }

        // ── 8b. NET console: reachable? Read callsign + ext UDP right away (read-only) ──
        var consoleReachable = false;
        NodeConsoleSnapshot? snap = null;
        if (deviceOk && devicePortOk)
        {
            consoleReachable = await _fix.IsReachableAsync(input.DeviceIp.Trim(), ct);
            if (consoleReachable)
            {
                var password = input.NodePassword ?? _fix.SavedPassword(input.NodeId);
                snap = await _fix.ReadAsync(input.DeviceIp.Trim(), password, ct);
            }
        }

        // ── 9. Callsign ──────────────────────────────────────────────────
        AddCallsignResults(input, results, snap);

        // ── 10. Data flow ────────────────────────────────────────────────
        AddReceiveResults(input, hubLikely, deviceIp, snap, results);

        // ── 10b. NET console result ──────────────────────────────────────
        if (deviceOk && devicePortOk)
        {
            var nodeAlive = pingOk || (deviceIp is not null && _udp.RawRxBySource.GetValueOrDefault(deviceIp.ToString()) > 0);
            AddNetConsoleResult(hubLikely, nodeAlive, consoleReachable, snap, results);
        }

        // ── 11. Node settings read via the NET console (read-only) ───────
        if (snap is { Error: NodeConsoleError.None }) AddConsoleResults(input, snap, results);

        return results;
    }

    // ── Socket ────────────────────────────────────────────────────────────

    private async Task CheckSocketAsync(ConnectionCheckInput input, bool listenOk, bool portOk, IPAddress? listenIp, List<CheckResult> results)
    {
        var st = _udp.Status;
        var formEndpoint = $"{input.ListenIp?.Trim()}:{input.ListenPort}";

        if (st.IsListening)
        {
            if (string.Equals(st.BoundListenEndpoint, formEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new(CheckSeverity.Ok,
                    TF("UDP-Socket ist offen auf {0}", "UDP socket is open on {0}", st.BoundListenEndpoint)));
                return;
            }
            results.Add(new(CheckSeverity.Info,
                TF("WebDesk läuft noch mit {0}", "WebDesk is still running with {0}", st.BoundListenEndpoint),
                T("Änderungen an Listen IP / Listen Port werden erst nach dem Speichern und einem Neustart von WebDesk wirksam.",
                  "Changes to Listen IP / Listen Port only take effect after saving and restarting WebDesk.")));
            return;
        }

        if (!string.IsNullOrEmpty(st.LastBindError))
        {
            results.Add(new(CheckSeverity.Error,
                T("UDP-Socket konnte nicht geöffnet werden (roter UDP-Status)", "UDP socket could not be opened (red UDP status)"),
                TF("Fehler: {0}. Prüfen Sie Listen IP und Listen Port; ein anderes Programm (z. B. ein zweites WebDesk oder der Bluetooth-Hub) darf den Port nicht belegen. Nach der Korrektur WebDesk neu starten.",
                   "Error: {0}. Check Listen IP and Listen Port; no other program (e.g. a second WebDesk or the Bluetooth hub) may use the port. Restart WebDesk after the fix.",
                   st.LastBindError.TrimEnd('.', ' '))));
            return;
        }

        if (!listenOk || !portOk || listenIp is null) return;

        // Not listening and no recorded error (e.g. service not started yet): try a throw-away bind.
        var error = TryBind(listenIp, input.ListenPort);
        results.Add(error is null
            ? new(CheckSeverity.Ok, TF("UDP-Port {0} ist frei", "UDP port {0} is free", input.ListenPort))
            : new(CheckSeverity.Error,
                TF("UDP-Port {0} kann nicht geöffnet werden", "UDP port {0} cannot be opened", input.ListenPort),
                error));
        await Task.CompletedTask;
    }

    private string? TryBind(IPAddress ip, int port)
    {
        try
        {
            using var c = new UdpClient(new IPEndPoint(ip, port));
            return null;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return T("Der Port ist bereits belegt (zweites WebDesk? Bluetooth-Hub? anderes Programm?).",
                     "The port is already in use (second WebDesk? Bluetooth hub? another program?).");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressNotAvailable)
        {
            return T("Diese IP-Adresse gehört nicht zu diesem PC.", "This IP address does not belong to this PC.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            return T("Zugriff verweigert (Berechtigung oder Firewall).", "Access denied (permission or firewall).");
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>True when a program on this PC (the Bluetooth hub) already holds the UDP <paramref name="port"/>.</summary>
    public static bool IsHubPortInUse(int port) => IsUdpPortInUse(port, IPAddress.Any, IPAddress.Loopback);

    /// <summary>
    /// True when something already holds the UDP port on this PC. Tried on the wildcard address and on the
    /// concrete address – Windows lets a wildcard bind succeed next to a specific one.
    /// </summary>
    private static bool IsUdpPortInUse(int port, params IPAddress[] addresses)
    {
        foreach (var address in addresses)
        {
            try
            {
                using var c = new UdpClient(new IPEndPoint(address, port));
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return true;
            }
            catch
            {
                // other errors (e.g. address not available): ignore this address
            }
        }
        return false;
    }

    // ── Callsign ──────────────────────────────────────────────────────────

    private void AddCallsignResults(ConnectionCheckInput input, List<CheckResult> results, NodeConsoleSnapshot? snap)
    {
        var raw = input.Callsign?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            results.Add(new(CheckSeverity.Error,
                T("Eigenes Rufzeichen fehlt", "Own callsign is missing"),
                T("Tragen Sie das Rufzeichen Ihres Nodes mit SSID ein, genau wie am Node eingestellt (z. B. OE1XYZ-1).",
                  "Enter your node's callsign with SSID exactly as set on the node (e.g. OE1XYZ-1).")));
            return;
        }

        var dash = raw.IndexOf('-');
        var baseCall = dash >= 0 ? raw[..dash] : raw;
        var ssid = dash >= 0 ? raw[(dash + 1)..] : null;
        var formatOk = true;

        if (raw.Any(char.IsWhiteSpace) || !BaseCallsignPattern().IsMatch(baseCall))
        {
            formatOk = false;
            results.Add(new(CheckSeverity.Error,
                TF("Rufzeichen \"{0}\" hat kein gültiges Format", "Callsign \"{0}\" does not have a valid format", raw),
                T("Erwartet wird Rufzeichen und SSID, z. B. OE1XYZ-1: 3–7 Buchstaben/Ziffern (mit mindestens einer Ziffer), keine Leerzeichen oder Sonderzeichen.",
                  "Expected is callsign and SSID, e.g. OE1XYZ-1: 3–7 letters/digits (at least one digit), no spaces or special characters.")));
        }
        else if (string.Equals(baseCall, "NOCALL", StringComparison.OrdinalIgnoreCase))
        {
            formatOk = false;
            results.Add(new(CheckSeverity.Warning,
                T("Rufzeichen steht noch auf dem Standardwert NOCALL", "Callsign is still the default NOCALL"),
                T("Tragen Sie Ihr eigenes Rufzeichen ein – dasselbe wie am Node.", "Enter your own callsign – the same as on the node.")));
        }

        if (ssid is null)
        {
            if (formatOk)
                results.Add(new(CheckSeverity.Warning,
                    T("Rufzeichen ohne SSID", "Callsign without SSID"),
                    T("MeshCom-Nodes nutzen ein Rufzeichen mit SSID (z. B. OE1XYZ-1). Die SSID muss in WebDesk exakt wie am Node stehen, sonst erkennt WebDesk eigene Nachrichten nicht.",
                      "MeshCom nodes use a callsign with SSID (e.g. OE1XYZ-1). The SSID must be identical in WebDesk and on the node, otherwise WebDesk does not recognise its own messages.")));
        }
        else if (!Regex.IsMatch(ssid, @"^\d{1,2}$"))
        {
            formatOk = false;
            results.Add(new(CheckSeverity.Error,
                TF("SSID \"{0}\" ist ungültig", "SSID \"{0}\" is invalid", ssid),
                T("Die SSID ist eine Zahl mit ein oder zwei Ziffern, z. B. -1 oder -11.", "The SSID is a number with one or two digits, e.g. -1 or -11.")));
        }
        else if (input.Kiss && int.Parse(ssid) > 15)
        {
            results.Add(new(CheckSeverity.Info,
                T("SSID über 15 mit KISS: Senden läuft über ext UDP", "SSID above 15 with KISS: sending goes via ext UDP"),
                T("AX.25 (KISS) kennt nur SSID 0–15. WebDesk empfängt über KISS und sendet bei dieser SSID automatisch über ext UDP – ext UDP muss am Node deshalb eingeschaltet sein.",
                  "AX.25 (KISS) only has SSID 0–15. WebDesk receives via KISS and sends with this SSID automatically via ext UDP – so ext UDP must be switched on at the node.")));
        }

        if (raw != raw.ToUpperInvariant() && formatOk)
            results.Add(new(CheckSeverity.Info,
                T("Rufzeichen enthält Kleinbuchstaben", "Callsign contains lower-case letters"),
                T("Schreiben Sie das Rufzeichen in Großbuchstaben, so wie es am Node steht.", "Write the callsign in capital letters as it is set on the node.")));

        if (!formatOk) return;

        // Compare with what the node itself reports.
        var nodeId = input.NodeId ?? _nodes.PrimaryNode?.Id ?? Guid.Empty;
        var obs = _udp.CallsignObserver.Get(nodeId);

        // Authoritative, read-only: the callsign the node reports on its console (read just now, or – when the
        // user has a console open – its last known value).
        var consoleCall = snap?.Callsign;
        if (consoleCall is null && _console.IsConnected && _console.CurrentValues.TryGetValue("setcall", out var cv))
            consoleCall = cv;
        if (!string.IsNullOrWhiteSpace(consoleCall))
        {
            consoleCall = consoleCall.Trim();
            if (string.Equals(consoleCall, raw, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new(CheckSeverity.Ok,
                    TF("Rufzeichen {0} stimmt mit dem Node überein (Konsole)", "Callsign {0} matches the node (console)", raw)));
                return;
            }
            results.Add(new(CheckSeverity.Error,
                T("Rufzeichen weicht vom Node ab", "Callsign differs from the node"),
                TF("Der Node meldet {0}, in WebDesk steht {1}. Ändern Sie in WebDesk unter Einstellungen → Verbindung das \"Eigene Rufzeichen\" auf {0} (oder ändern Sie das Rufzeichen am Node).",
                   "The node reports {0}, WebDesk has {1}. In WebDesk under Settings → Connection change \"Own Callsign\" to {0} (or change the callsign on the node).",
                   consoleCall, raw),
                Commands: [$"--setcall {raw.ToUpperInvariant()}"],
                SuggestCallsign: consoleCall));
            return;
        }

        // Certain proof from the traffic: the node returned a message WebDesk sent, but under another callsign.
        if (obs.EchoCallsign is { } echo && !string.Equals(echo, raw, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new(CheckSeverity.Error,
                T("Rufzeichen weicht vom Node ab", "Callsign differs from the node"),
                TF("Der Node hat eine soeben gesendete Nachricht als {0} zurückgemeldet, in WebDesk steht {1}. Ändern Sie in WebDesk unter Einstellungen → Verbindung das \"Eigene Rufzeichen\" auf {0} (oder ändern Sie das Rufzeichen am Node). Sonst erkennt WebDesk eigene Nachrichten nicht.",
                   "The node returned a message that was just sent as {0}, WebDesk has {1}. In WebDesk under Settings → Connection change \"Own Callsign\" to {0} (or change the callsign on the node). Otherwise WebDesk does not recognise its own messages.",
                   echo, raw),
                Commands: [$"--setcall {raw.ToUpperInvariant()}"],
                SuggestCallsign: echo));
            return;
        }

        if (obs.Counts.ContainsKey(raw))
        {
            results.Add(new(CheckSeverity.Ok,
                TF("Rufzeichen {0} vom Node bestätigt", "Callsign {0} confirmed by the node", raw)));
            return;
        }

        // Same base callsign, other SSID: most likely the same node with a different SSID.
        var sameBase = obs.Counts
            .Where(kv => string.Equals(kv.Key.Split('-')[0], baseCall, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault();
        if (sameBase is not null)
        {
            results.Add(new(CheckSeverity.Warning,
                T("SSID passt vermutlich nicht zum Node", "SSID probably does not match the node"),
                TF("Der Node sendet als {0}, in WebDesk steht {1}. Ist {0} Ihr Node, ändern Sie in WebDesk unter Einstellungen → Verbindung das \"Eigene Rufzeichen\" auf {0} (oder die SSID am Node). Sonst erkennt WebDesk eigene Nachrichten nicht.",
                   "The node sends as {0}, WebDesk has {1}. If {0} is your node, change \"Own Callsign\" in WebDesk under Settings → Connection to {0} (or the SSID on the node). Otherwise WebDesk does not recognise its own messages.",
                   sameBase, raw)));
            return;
        }

        if (obs.Total >= DoubtAfterPackets)
        {
            var top = obs.Counts.OrderByDescending(kv => kv.Value).First().Key;
            results.Add(new(CheckSeverity.Warning,
                T("Rufzeichen vom Node nicht bestätigt", "Callsign not confirmed by the node"),
                TF("Der Node hat {0} Pakete gesendet, aber keines mit {1} (häufigster Absender: {2}). Prüfen Sie, ob das Rufzeichen samt SSID exakt dem am Node entspricht (am Node-Display oder mit --info).",
                   "The node sent {0} packets, none with {1} (most frequent sender: {2}). Check that callsign and SSID are exactly those set on the node (node display or --info).",
                   obs.Total, raw, top),
                ["--info"]));
            return;
        }

        if (snap is { Error: NodeConsoleError.AuthFailed })
        {
            results.Add(new(CheckSeverity.Warning,
                T("Rufzeichen noch nicht geprüft – Abweichung möglich", "Callsign not verified yet – a mismatch is possible"),
                T("WebDesk konnte das Rufzeichen nicht am Node lesen, weil die Anmeldung an der Net-Console fehlschlug. Tragen Sie das Node-Passwort ein (Einstellungen oder Feld unten) und prüfen Sie erneut – oder senden Sie eine Nachricht; der Node meldet sie mit seinem Rufzeichen zurück, und WebDesk warnt bei einer Abweichung.",
                  "WebDesk could not read the callsign from the node because the net console login failed. Enter the node password (settings or the field below) and check again – or send a message; the node returns it with its callsign and WebDesk warns about a mismatch.")));
            return;
        }

        results.Add(new(CheckSeverity.Warning,
            T("Rufzeichen noch nicht geprüft – Abweichung möglich", "Callsign not verified yet – a mismatch is possible"),
            T("WebDesk konnte noch nicht feststellen, ob Rufzeichen und SSID zum Node passen. Am schnellsten geht es so: Net-Console am Node einschalten (--netconsole on) und neu prüfen – oder eine Nachricht senden; der Node meldet sie mit seinem Rufzeichen zurück, und WebDesk warnt bei einer Abweichung. Eine falsche SSID zeigt sich sonst nur daran, dass eigene Nachrichten nicht bestätigt werden.",
              "WebDesk could not yet tell whether callsign and SSID match the node. The quickest way: switch the net console on at the node (--netconsole on) and check again – or send a message; the node returns it with its callsign and WebDesk warns about a mismatch. A wrong SSID otherwise only shows as own messages not being acknowledged.")));
    }

    // ── Data flow ─────────────────────────────────────────────────────────

    private void AddReceiveResults(ConnectionCheckInput input, bool hubLikely, IPAddress? deviceIp, NodeConsoleSnapshot? snap, List<CheckResult> results)
    {
        var st = _udp.Status;
        if (!st.IsListening) return;   // the socket result above already explains it

        // Per node when a node profile exists (NodeManager.MarkNodeSeen, UTC); legacy single-node: global status.
        DateTime? lastRx = input.NodeId is { } nid && nid != Guid.Empty
            ? _nodes.GetLastSeen(nid)?.ToLocalTime()
            : st.LastRxTime;

        if (lastRx.HasValue)
        {
            var age = DateTime.Now - lastRx.Value;
            if (age <= TimeSpan.FromMinutes(15))
            {
                results.Add(new(CheckSeverity.Ok,
                    TF("Pakete vom Node kommen an (zuletzt vor {0} min)", "Packets from the node arrive (last {0} min ago)", (int)age.TotalMinutes)));
                return;
            }
            results.Add(new(CheckSeverity.Warning,
                TF("Seit {0} min kein Paket mehr vom Node", "No packet from the node for {0} min", (int)age.TotalMinutes),
                T("Früher kamen Daten an. Prüfen Sie, ob der Node/Hub noch läuft und im Netz ist.",
                  "Data arrived earlier. Check whether the node/hub is still running and on the network.")));
            return;
        }

        // Datagrams do arrive, yet none was accepted as data of this node: either they come from the
        // Device IP but are not recognised (sender callsign / SSID differs), or they come from another IP.
        var rawBySource = _udp.RawRxBySource;
        var fromDevice = deviceIp is not null ? rawBySource.GetValueOrDefault(deviceIp.ToString()) : 0;
        if (fromDevice > 0)
        {
            // Packets DO arrive from the node, but none was counted as radio traffic yet. Say what happened to them.
            var stats = _udp.GetSourceStats(deviceIp!.ToString());
            var configured = input.Callsign.Trim();

            if (stats.Skipped > 0)
            {
                results.Add(new(stats.Skipped >= 5 ? CheckSeverity.Warning : CheckSeverity.Info,
                    TF("{0} UDP-Paket(e) vom Node angekommen, aber noch kein Funkverkehr gezählt", "{0} UDP packet(s) arrived from the node, but no radio traffic has been counted yet", fromDevice),
                    TF("Bisher kamen nur Weiterleitungen (src_type node), zuletzt von {0}. Der Node meldet jedes empfangene Funkpaket erst als Weiterleitung und danach als eigentliches Paket – WebDesk wertet nur das eigentliche und bleibt bis dahin auf „Warte auf Signal“. Das ist anfangs normal. Bleibt es dauerhaft dabei, passt vermutlich das Rufzeichen nicht (in WebDesk: {1}).",
                       "So far only relay echoes (src_type node) arrived, the last one from {0}. The node reports every received radio packet first as a relay echo and then as the actual packet – WebDesk only counts the actual one and stays on \"waiting for signal\" until then. That is normal at the beginning. If it stays like this for long, the callsign probably does not match (in WebDesk: {1}).",
                       stats.LastSkippedFrom ?? "?", configured)));
            }
            else
            {
                results.Add(new(CheckSeverity.Info,
                    TF("{0} UDP-Paket(e) vom Node angekommen, aber noch kein Funkverkehr gezählt", "{0} UDP packet(s) arrived from the node, but no radio traffic has been counted yet", fromDevice),
                    T("Es kamen bisher nur Quittungen oder Statusmeldungen an – noch keine Nachrichten, Positionen oder Telemetrie. Der Status wird grün, sobald der Node Mesh-Verkehr meldet (siehe Monitor).",
                      "So far only acknowledgements or status messages arrived – no messages, positions or telemetry yet. The status turns green as soon as the node reports mesh traffic (see the monitor).")));
            }
            return;
        }
        var others = rawBySource.Where(kv => kv.Value > 0).Select(kv => kv.Key).Take(4).ToList();
        if (input.IsPrimary && others.Count > 0)
        {
            results.Add(new(CheckSeverity.Warning,
                TF("Pakete kommen von {0} an, aber nicht von der Device IP {1}", "Packets arrive from {0}, but not from the Device IP {1}", string.Join(", ", others), input.DeviceIp.Trim()),
                T("WebDesk erkennt den Node an der Absender-IP der Pakete. Ist die Absender-IP der Node, tragen Sie sie als Device IP ein. Bei Docker/NAT kann die Absender-IP von der Node-IP abweichen.",
                  "WebDesk recognises the node by the sender IP of its packets. If the sender IP is your node, enter it as Device IP. With Docker/NAT the sender IP can differ from the node's IP.")));
            return;
        }

        if (hubLikely)
        {
            results.Add(new(CheckSeverity.Warning,
                T("Noch kein Paket vom Bluetooth-Hub empfangen", "No packet received from the Bluetooth hub yet"),
                T("Prüfen Sie im Hub: Ist der Node per Bluetooth verbunden (Handy-App getrennt)? Steht im Hub-Feld \"WebDesk\" die Adresse von WebDesk (Standard 127.0.0.1:1799)? Sendet WebDesk das Registrierungspaket an den Hub-Port?",
                  "Check in the hub: is the node connected via Bluetooth (phone app disconnected)? Does the hub's \"WebDesk\" field hold WebDesk's address (default 127.0.0.1:1799)? Does WebDesk send its registration packet to the hub's port?")));
            return;
        }

        var ips = SuggestPcIps(input.DeviceIp.Trim());

        // The console already told how the node is set up: do not repeat the standard advice then.
        if (snap is { Error: NodeConsoleError.None })
        {
            var rows = CompareNodeSettings(input, snap);
            var extOk = rows.Where(r => r.Key is "extudp" or "extudpip").All(r => r.Severity == CheckSeverity.Ok)
                        && rows.Any(r => r.Key == "extudp");
            if (extOk)
            {
                results.Add(new(CheckSeverity.Warning,
                    T("Noch kein Paket vom Node empfangen", "No packet received from the node yet"),
                    TF("Laut Net-Console ist der Node richtig eingestellt (ext UDP an, Ziel-IP {0}). Prüfen Sie die Firewall dieses PCs (eingehendes UDP auf Port {1}). Der Node sendet erst, wenn Mesh-Verkehr oder sein nächstes Beacon anfällt – eine Weile warten oder eine Nachricht senden.",
                       "According to the net console the node is set up correctly (ext UDP on, target IP {0}). Check this PC's firewall (inbound UDP on port {1}). The node only sends once there is mesh traffic or its next beacon – wait a while or send a message.",
                       snap.ExtUdpIp ?? "–", input.ListenPort)));
            }
            // otherwise the console findings above name the wrong setting and offer to correct it
            return;
        }

        // IP first: the node only accepts "--extudp on" once the target IP is set.
        var cmds = ips.Select(ip => $"--extudpip {ip}").ToList();
        cmds.Add("--extudp on");
        results.Add(new(CheckSeverity.Warning,
            T("Noch kein Paket vom Node empfangen", "No packet received from the node yet"),
            TF("Fast immer liegt es an den Node-Einstellungen: ext UDP muss eingeschaltet sein und die ext-UDP-Ziel-IP muss die IP dieses PCs sein ({0}), Port 1799. Zusätzlich darf die Firewall dieses PCs eingehendes UDP auf Port {1} nicht blockieren. Firmware ab 4.40a.10 verweigert Broadcast-Adressen und die Node-IP selbst als Ziel.",
               "It is almost always the node settings: ext UDP must be on and the ext UDP target IP must be this PC's IP ({0}), port 1799. Also this PC's firewall must allow inbound UDP on port {1}. Firmware from 4.40a.10 refuses broadcast addresses and the node's own IP as target.",
               ips.Count > 0 ? string.Join(", ", ips) : "–", input.ListenPort),
            cmds));
    }

    // ── NET console reachability ─────────────────────────────────────────

    /// <summary>
    /// Tells whether the node's NET console answers. Reachable: the panel offers "read node settings" (and, on
    /// confirmation, "correct on the node"). Not reachable although the node is alive: explain how to switch it on.
    /// </summary>
    private void AddNetConsoleResult(bool hubLikely, bool nodeAlive, bool reachable, NodeConsoleSnapshot? snap, List<CheckResult> results)
    {
        if (reachable)
        {
            if (snap is { Error: NodeConsoleError.AuthFailed })
            {
                results.Add(new(CheckSeverity.Warning,
                    T("Net-Console am Node erreichbar, aber die Anmeldung schlug fehl", "Net console on the node is reachable, but the login failed"),
                    T("Das Node-Passwort stimmt nicht (am Node mit --passwd gesetzt). Tragen Sie es in den Einstellungen ein oder unten im Feld, dann kann WebDesk Rufzeichen und ext UDP automatisch abgleichen.",
                      "The node password is wrong (set on the node with --passwd). Enter it in the settings or in the field below, then WebDesk can compare callsign and ext UDP automatically."),
                    Code: "netconsole-ok"));
                return;
            }
            if (snap is { Error: NodeConsoleError.Busy or NodeConsoleError.Other or NodeConsoleError.Unreachable })
            {
                results.Add(new(CheckSeverity.Warning,
                    T("Net-Console erreichbar, die Einstellungen konnten aber nicht gelesen werden", "Net console is reachable, but the settings could not be read"),
                    snap.Error == NodeConsoleError.Busy
                        ? TF("Die Konsole von WebDesk ist gerade mit {0} verbunden, daher kann das Rufzeichen nicht abgeglichen werden. Trennen Sie sie zuerst (Einstellungen → Console) und prüfen Sie erneut.",
                             "WebDesk's console is currently connected to {0}, so the callsign cannot be compared. Disconnect it first (Settings → Console) and check again.", snap.BusyHost ?? "?")
                        : T("Die Verbindung kam zustande, der Node hat aber nicht (vollständig) geantwortet. Prüfen Sie erneut; hilft das nicht, öffnen Sie unten „Vom Node lesen“ und sehen Sie sich die Antwort an.",
                            "The connection was made, but the node did not answer (completely). Check again; if that does not help, use \"Read from node\" below and look at the reply."),
                    Code: "netconsole-ok"));
                return;
            }
            results.Add(new(CheckSeverity.Ok,
                T("Net-Console am Node erreichbar", "Net console on the node is reachable"),
                T("Sie können die Einstellungen am Node auslesen und auf Wunsch korrigieren (Bereich unten).",
                  "You can read the node's settings and correct them on request (section below)."),
                Code: "netconsole-ok"));
            return;
        }

        // The Bluetooth hub only emulates the console optionally – no hint for it.
        // For a real node the box is offered anyway ("try"): the user may start the attempt on request, e.g. when
        // ext UDP is not configured yet and the node is silent, so nothing else tells that it is alive.
        if (hubLikely) return;

        results.Add(new(CheckSeverity.Info,
            T("Net-Console am Node nicht erreichbar", "Net console on the node is not reachable"),
            TF("Damit der Assistent Einstellungen am Node auslesen und korrigieren kann, muss die Net-Console am Node eingeschaltet sein (Port {0}, HMAC). Schalten Sie sie in der Weboberfläche des Nodes ein (IP Network Settings → net console) oder mit dem Befehl unten, und setzen Sie ein Passwort mit --passwd <Passwort>. Danach erneut prüfen.",
               "For the wizard to read and correct settings on the node, the net console must be switched on at the node (port {0}, HMAC). Switch it on in the node's web interface (IP Network Settings → net console) or with the command below, and set a password with --passwd <password>. Then check again.",
               _fix.Port),
            ["--netconsole on"],
            Code: "netconsole-try"));
    }

    // ── NET console (read-only) ───────────────────────────────────────────

    // ── Comparison of the node's settings with the WebDesk configuration ──

    /// <summary>
    /// Compares what the node reports on its console with what WebDesk is configured for. Only settings that WebDesk
    /// has an opinion about are compared; rows with a <see cref="SettingRow.FixCommand"/> can be corrected on the
    /// node after the user confirms.
    /// </summary>
    public IReadOnlyList<SettingRow> CompareNodeSettings(ConnectionCheckInput input, NodeConsoleSnapshot snap)
    {
        var rows = new List<SettingRow>();
        var values = snap.Values ?? new Dictionary<string, string>();
        string? V(string key) => values.TryGetValue(key, out var x) && !string.IsNullOrWhiteSpace(x) ? x.Trim() : null;
        static bool On(string? x) => string.Equals(x, "on", StringComparison.OrdinalIgnoreCase);

        var deviceIp = input.DeviceIp.Trim();
        var hub = LooksLikeHub(deviceIp, input.DevicePort);
        var ips = SuggestPcIps(deviceIp);
        var configured = input.Callsign.Trim();

        // Callsign
        if (snap.Callsign is { } call)
        {
            var same = string.Equals(call.Trim(), configured, StringComparison.OrdinalIgnoreCase);
            rows.Add(new("callsign", T("Rufzeichen", "Callsign"), call.Trim(), configured,
                same ? CheckSeverity.Ok : CheckSeverity.Error,
                same ? null : TF("WebDesk hat {0}, der Node {1}.", "WebDesk has {0}, the node has {1}.", configured, call.Trim()),
                same ? null : $"--setcall {configured.ToUpperInvariant()}"));
        }

        var extIp = V("extudpip");

        // WebDesk only receives what is sent to an address it listens on: 0.0.0.0 covers every local address, a specific
        // Listen IP only itself (a hub sending to 127.0.0.1 is lost when WebDesk listens only on the LAN address).
        var listenSpecific = IPAddress.TryParse(input.ListenIp?.Trim(), out var listenAddr) && !listenAddr.Equals(IPAddress.Any);
        IReadOnlyList<string> targetIps = listenSpecific && !IPAddress.IsLoopback(listenAddr!) ? new List<string> { listenAddr!.ToString() } : ips;
        var extIpLocal = IPAddress.TryParse(extIp, out var extTarget) && (ips.Contains(extTarget.ToString()) || NetworkHelper.IsLocalAddress(extTarget));
        var extIpCovered = !listenSpecific || (extIpLocal && extTarget!.Equals(listenAddr));
        var extIpOk = extIpLocal && extIpCovered;

        // ext UDP and its target IP belong together (the firmware prints them on one line). A real node always reports
        // them; the Bluetooth hub only if its console happens to – then both rows are shown, never just one.
        var extudp = V("extudp");
        var showExt = !hub || extudp is not null || extIp is not null;

        // ext UDP – the way WebDesk receives (and with KISS still the node's own data)
        if (showExt)
        {
            var ok = On(extudp);
            var unknown = extudp is null;   // the node's answer did not contain it – not the same as "off"
            rows.Add(new("extudp", "ext UDP", extudp ?? "–", "on",
                ok ? CheckSeverity.Ok : (unknown || input.Kiss ? CheckSeverity.Warning : CheckSeverity.Error),
                ok ? null
                   : unknown ? T("Der Node hat diese Angabe in seiner --info-Antwort nicht gemeldet, WebDesk kann sie nicht beurteilen. Die Antwort des Nodes steht unten.",
                                 "The node did not report this in its --info answer, so WebDesk cannot judge it. The node's answer is shown below.")
                   : extIpOk ? T("Ohne ext UDP sendet der Node nichts an WebDesk.", "Without ext UDP the node sends nothing to WebDesk.")
                             : T("Ohne ext UDP sendet der Node nichts an WebDesk. Der Node schaltet ext UDP nur ein, wenn die Ziel-IP schon gesetzt ist – deshalb wird sie zuerst mitgesendet.",
                                 "Without ext UDP the node sends nothing to WebDesk. The node only switches ext UDP on once the target IP is set – so it is sent first."),
                ok || hub || unknown ? null : "--extudp on"));
        }

        if (showExt)
        {
            var ok = extIpOk;
            rows.Add(new("extudpip", T("ext UDP Ziel-IP", "ext UDP target IP"), extIp ?? "–",
                listenSpecific && !IPAddress.IsLoopback(listenAddr!) || (listenSpecific && hub) ? listenAddr!.ToString()
                    : ips.Count > 0 ? string.Join(" / ", ips) : T("IP dieses PCs", "this PC's IP"),
                ok ? CheckSeverity.Ok : (extIp is null ? CheckSeverity.Warning : CheckSeverity.Error),
                ok ? null
                   : extIp is null ? T("Der Node hat diese Angabe in seiner --info-Antwort nicht gemeldet, WebDesk kann sie nicht beurteilen. Die Antwort des Nodes steht unten.",
                                       "The node did not report this in its --info answer, so WebDesk cannot judge it. The node's answer is shown below.")
                   : extIpLocal && !extIpCovered
                       ? TF("WebDesk lauscht nur auf {0}, der Absender sendet aber an {1}. Setzen Sie die Listen IP auf 0.0.0.0 oder stellen Sie den Absender auf {0} ein.",
                            "WebDesk only listens on {0}, but the sender uses {1}. Set the Listen IP to 0.0.0.0 or let it send to {0}.",
                            listenAddr, extIp)
                   : hub ? T("Im Hub muss im Feld „WebDesk“ die Adresse dieses PCs stehen.", "In the hub the field \"WebDesk\" must hold this PC's address.")
                         : T("Der Node sendet an eine andere Adresse als die dieses PCs.", "The node sends to an address other than this PC's."),
                ok || hub || extIp is null || targetIps.Count == 0 ? null : $"--extudpip {targetIps[0]}"));
        }

        // Bluetooth hub: its two UDP ports (reported by the current hub --info) must match WebDesk's settings
        if (hub)
        {
            if (V("hubudpport") is { } hubUdp && int.TryParse(hubUdp, out var hubUdpPort))
            {
                var ok = hubUdpPort == input.DevicePort;
                rows.Add(new("hubudpport", T("Hub UDP-Port", "Hub UDP port"), hubUdp, input.DevicePort.ToString(),
                    ok ? CheckSeverity.Ok : CheckSeverity.Error,
                    ok ? null : TF("Der Hub lauscht auf UDP {0}, in WebDesk steht Device Port {1}. Tragen Sie in WebDesk den Device Port {0} ein (oder ändern Sie den UDP-Port im Hub).",
                                   "The hub listens on UDP {0}, WebDesk has Device Port {1}. Enter Device Port {0} in WebDesk (or change the UDP port in the hub).",
                                   hubUdpPort, input.DevicePort)));
            }
            if (V("hubextport") is { } hubPeer && int.TryParse(hubPeer, out var hubPeerPort))
            {
                var ok = hubPeerPort == input.ListenPort;
                rows.Add(new("hubextport", T("Hub sendet an Port", "Hub sends to port"), hubPeer, input.ListenPort.ToString(),
                    ok ? CheckSeverity.Ok : CheckSeverity.Error,
                    ok ? null : TF("Der Hub sendet an Port {0}, WebDesk lauscht auf {1}. Im Hub-Feld „WebDesk“ muss {2}:{1} stehen (oder in WebDesk den Listen Port ändern).",
                                   "The hub sends to port {0}, WebDesk listens on {1}. The hub's \"WebDesk\" field must hold {2}:{1} (or change the Listen Port in WebDesk).",
                                   hubPeerPort, input.ListenPort, extIp ?? "127.0.0.1")));
            }
        }

        // The node's own IP must be what WebDesk uses as Device IP
        var nodeIp = V("ip");
        if (nodeIp is not null && !hub)
        {
            var same = string.Equals(nodeIp, deviceIp, StringComparison.OrdinalIgnoreCase);
            rows.Add(new("ip", T("IP-Adresse des Nodes", "Node IP address"), nodeIp, deviceIp,
                same ? CheckSeverity.Ok : CheckSeverity.Error,
                same ? null : TF("Der Node meldet {0}. Tragen Sie in WebDesk {0} als Device IP ein.", "The node reports {0}. Enter {0} as Device IP in WebDesk.", nodeIp)));
        }

        // KISS/TCP – only when WebDesk uses it for this node
        if (input.Kiss)
        {
            var kiss = V("kiss");
            if (kiss is not null)
            {
                var ok = On(kiss);
                rows.Add(new("kiss", "KISS/TCP", kiss, "on", ok ? CheckSeverity.Ok : CheckSeverity.Error,
                    ok ? null : T("WebDesk nutzt KISS für diesen Node, der Node hat KISS aber aus.", "WebDesk uses KISS for this node, but KISS is off on the node."),
                    ok ? null : "--kiss on"));
            }
            var tx = V("kiss tx");
            if (tx is not null)
            {
                var ok = On(tx);
                rows.Add(new("kisstx", "KISS TX", tx, "on", ok ? CheckSeverity.Ok : CheckSeverity.Warning,
                    ok ? null : T("Ohne KISS TX kann WebDesk über KISS nicht senden.", "Without KISS TX WebDesk cannot send via KISS."),
                    ok ? null : "--kiss tx on"));
            }
            var meta = V("kiss meta");
            if (meta is not null)
            {
                var ok = On(meta);
                rows.Add(new("kissmeta", "KISS RxMeta", meta, "on", ok ? CheckSeverity.Ok : CheckSeverity.Info,
                    ok ? null : T("Ohne RxMeta zeigt WebDesk kein RSSI/SNR pro Frame.", "Without RxMeta WebDesk shows no RSSI/SNR per frame."),
                    ok ? null : "--kiss meta on"));
            }
            var auth = V("kiss auth");
            if (On(auth))
            {
                var hasPassword = !string.IsNullOrEmpty(input.NodePassword ?? _fix.SavedPassword(input.NodeId));
                rows.Add(new("kissauth", "KISS Auth", auth!, T("Node-Passwort gesetzt", "node password set"),
                    hasPassword ? CheckSeverity.Ok : CheckSeverity.Warning,
                    hasPassword ? null : T("Der Node verlangt KISS-Authentifizierung, in WebDesk ist aber kein Node-Passwort eingetragen.", "The node requires KISS authentication, but no node password is set in WebDesk.")));
            }
        }

        // Foreign direct messages
        if (On(V("nopmother")))
            rows.Add(new("nopmother", "nopmother", "on", "off",
                CheckSeverity.Info,
                T("Am Node ist --nopmother on gesetzt: Direktnachrichten zwischen anderen Stationen erscheinen nicht in WebDesk. Mit --nopmother off am Node werden sie wieder geliefert.",
                  "--nopmother on is set on the node: direct messages between other stations do not show up in WebDesk. With --nopmother off on the node they are delivered again.")));

        return rows;
    }

    /// <summary>Turns the comparison into check findings (the callsign has its own finding in the callsign section).</summary>
    private void AddConsoleResults(ConnectionCheckInput input, NodeConsoleSnapshot snap, List<CheckResult> results)
    {
        var all = CompareNodeSettings(input, snap);
        var rows = all.Where(r => r.Key != "callsign").ToList();
        var ipFix = rows.FirstOrDefault(r => r.Key == "extudpip")?.FixCommand;
        foreach (var r in rows.Where(r => r.Severity >= CheckSeverity.Info && r.Severity != CheckSeverity.Ok))
        {
            // The firmware refuses "--extudp on" while no target IP is set: send the IP first.
            var commands = r.FixCommand is null ? null
                : r.Key == "extudp" && ipFix is not null ? new List<string> { ipFix, r.FixCommand }
                : new List<string> { r.FixCommand };
            var hint = r.Hint;
            if (r.NodeValue is null && !string.IsNullOrWhiteSpace(snap.RawInfo))
                hint = (hint is null ? "" : hint + " ") + T("Antwort des Nodes: ", "Node's answer: ") + snap.RawInfo;
            results.Add(new(r.Severity,
                TF("Am Node: {0} = {1} (erwartet: {2})", "On the node: {0} = {1} (expected: {2})", r.Label, r.NodeValue ?? "–", r.Expected),
                hint,
                commands));
        }
        // Only claim "all match" when really everything does – the callsign included.
        if (all.Count > 0 && all.All(r => r.Severity is CheckSeverity.Ok))
            results.Add(new(CheckSeverity.Ok,
                TF("Alle {0} geprüften Node-Einstellungen passen zur WebDesk-Konfiguration", "All {0} checked node settings match the WebDesk configuration", all.Count)));
    }

    // ── Probes ────────────────────────────────────────────────────────────

    private static async Task<bool> PingAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, TimeSpan.FromMilliseconds(1500), cancellationToken: ct);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TcpProbeAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var client = new TcpClient();
            await client.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
