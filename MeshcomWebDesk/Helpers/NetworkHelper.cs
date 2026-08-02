using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MeshcomWebDesk.Helpers;

/// <summary>Local network interface helpers.</summary>
public static class NetworkHelper
{
    /// <summary>All non-loopback IPv4 addresses of interfaces that are currently up.</summary>
    public static List<string> GetLocalIpv4Addresses()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .ToList();
}
