using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace MeshcomWebDesk.Helpers;

/// <summary>Local network interface helpers.</summary>
public static class NetworkHelper
{
    /// <summary>All non-loopback IPv4 addresses of interfaces that are currently up.</summary>
    public static List<string> GetLocalIpv4Addresses()
        => GetLocalIpv4Networks().Select(n => n.Address.ToString()).ToList();

    /// <summary>All non-loopback IPv4 addresses (with subnet mask) of interfaces that are currently up.</summary>
    public static List<(IPAddress Address, IPAddress Mask)> GetLocalIpv4Networks()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => (a.Address, a.IPv4Mask ?? IPAddress.Parse("255.255.255.0")))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Adapter names/descriptions that never carry a MeshCom node (virtual, tunnel, VM networks).</summary>
    private static readonly Regex VirtualAdapterPattern = new(
        "vEthernet|Hyper-V|WSL|VMware|VirtualBox|Docker|Tailscale|ZeroTier|TAP-|TUN|Npcap|Bluetooth|Loopback|Pseudo|VPN",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The /24 networks of this PC's physical interfaces that lie in private address space (10/8, 172.16/12,
    /// 192.168/16). Each entry is the network address (x.y.z.0). Used by the on-demand node scan.
    /// </summary>
    public static List<IPAddress> GetScanNetworks()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                if (VirtualAdapterPattern.IsMatch(nic.Name) || VirtualAdapterPattern.IsMatch(nic.Description))
                    continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes();
                    var isPrivate = b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
                    if (!isPrivate) continue;
                    var net = new IPAddress([b[0], b[1], b[2], 0]);
                    if (!result.Contains(net)) result.Add(net);
                }
            }
        }
        catch
        {
            // no network information available
        }
        return result;
    }

    /// <summary>True when <paramref name="ip"/> is a loopback address (127.x.x.x).</summary>
    public static bool IsLoopback(IPAddress ip) => IPAddress.IsLoopback(ip);

    /// <summary>True when <paramref name="ip"/> belongs to one of this PC's network interfaces (loopback included).</summary>
    public static bool IsLocalAddress(IPAddress ip)
        => IPAddress.IsLoopback(ip) || GetLocalIpv4Networks().Any(n => n.Address.Equals(ip));

    /// <summary>True when <paramref name="ip"/> lies in the same IPv4 subnet as <paramref name="network"/>.</summary>
    public static bool IsInSubnet(IPAddress ip, IPAddress network, IPAddress mask)
    {
        var a = ip.GetAddressBytes();
        var b = network.GetAddressBytes();
        var m = mask.GetAddressBytes();
        if (a.Length != 4 || b.Length != 4 || m.Length != 4) return false;
        for (var i = 0; i < 4; i++)
            if ((a[i] & m[i]) != (b[i] & m[i])) return false;
        return true;
    }

    /// <summary>True when <paramref name="ip"/> is a limited/directed broadcast or a multicast address.</summary>
    public static bool IsBroadcastOrMulticast(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        if (b[0] >= 224 && b[0] <= 239) return true;            // multicast
        if (ip.Equals(IPAddress.Broadcast)) return true;        // 255.255.255.255
        return GetLocalIpv4Networks().Any(n =>
        {
            var addr = n.Address.GetAddressBytes();
            var mask = n.Mask.GetAddressBytes();
            var bcast = new byte[4];
            for (var i = 0; i < 4; i++) bcast[i] = (byte)(addr[i] | ~mask[i]);
            return bcast.SequenceEqual(b);
        });
    }
}
