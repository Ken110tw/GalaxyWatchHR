using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WatchHROverlay.Services;

public static class NetworkInfoHelper
{
    private static readonly string[] VirtualAdapterKeywords =
    {
        "radmin", "hamachi", "tailscale", "zerotier", "virtual", "vmware",
        "vbox", "hyper-v", "wsl", "loopback", "tap", "tun", "vpn", "nordlynx", "wireguard"
    };

    public static IReadOnlyList<string> GetLocalLanIPv4Addresses()
    {
        var candidates = new List<(int priority, string ip)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.Any.Equals(g.Address));

            var desc = $"{nic.Name} {nic.Description}".ToLowerInvariant();
            var isVirtual = VirtualAdapterKeywords.Any(k => desc.Contains(k, StringComparison.Ordinal));

            foreach (var unicast in props.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var ip = unicast.Address.ToString();
                if (ip.StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                var priority = ScoreAddress(ip, hasGateway, isVirtual, nic.NetworkInterfaceType);
                candidates.Add((priority, ip));
            }
        }

        if (candidates.Count == 0)
        {
            return new[] { IPAddress.Loopback.ToString() };
        }

        return candidates
            .OrderByDescending(c => c.priority)
            .Select(c => c.ip)
            .Distinct()
            .ToList();
    }

    public static string GetPrimaryLanIp() => GetLocalLanIPv4Addresses()[0];

    private static int ScoreAddress(string ip, bool hasGateway, bool isVirtual, NetworkInterfaceType type)
    {
        var score = 0;
        if (!isVirtual) score += 100;
        if (hasGateway) score += 80;
        if (type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211) score += 40;
        if (ip.StartsWith("192.168.", StringComparison.Ordinal)) score += 30;
        else if (ip.StartsWith("10.", StringComparison.Ordinal)) score += 20;
        else if (IsPrivate172(ip)) score += 10;
        return score;
    }

    private static bool IsPrivate172(string ip)
    {
        if (!ip.StartsWith("172.", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = ip.Split('.');
        return parts.Length == 4 && int.TryParse(parts[1], out var second) && second is >= 16 and <= 31;
    }
}
