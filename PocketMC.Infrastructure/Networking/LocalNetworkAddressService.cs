using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PocketMC.Application.Interfaces.Networking;

namespace PocketMC.Infrastructure.Networking;

/// <summary>
/// Production-grade local network address resolution service.
/// Safely discovers and ranks LAN IPv4 addresses prioritizing physical router-connected interfaces
/// while filtering out virtual/VPN adapters without opening ports or creating firewall issues.
/// </summary>
public sealed class LocalNetworkAddressService : ILocalNetworkAddressService
{
    private const string LoopbackIp = "127.0.0.1";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    private readonly object _syncLock = new();
    private string? _cachedPrimaryIp;
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    private static readonly string[] ExcludedAdapterKeywords = new[]
    {
        "tailscale",
        "wsl",
        "hyper-v",
        "virtualbox",
        "vmware",
        "docker",
        "radmin",
        "hamachi",
        "tap",
        "tun",
        "vethernet",
        "pseudo",
        "bluetooth",
        "wi-fi direct",
        "p2p",
        "kernel debug",
        "loopback",
        "teredo",
        "6to4",
        "isatap"
    };

    public string GetPrimaryLanIpAddress()
    {
        lock (_syncLock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (_cachedPrimaryIp != null && now < _cacheExpiresAt)
            {
                return _cachedPrimaryIp;
            }

            string resolvedIp = ResolvePrimaryLanIp();
            _cachedPrimaryIp = resolvedIp;
            _cacheExpiresAt = now.Add(CacheDuration);
            return resolvedIp;
        }
    }

    public IReadOnlyList<string> GetLocalIpAddresses()
    {
        var rankedIps = new List<string>();
        string primary = GetPrimaryLanIpAddress();

        if (!string.Equals(primary, LoopbackIp, StringComparison.OrdinalIgnoreCase))
        {
            rankedIps.Add(primary);
        }

        foreach (var ip in GetRankedCandidateIps())
        {
            string ipStr = ip.ToString();
            if (!rankedIps.Contains(ipStr, StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(ipStr, LoopbackIp, StringComparison.OrdinalIgnoreCase))
            {
                rankedIps.Add(ipStr);
            }
        }

        rankedIps.Add(LoopbackIp);
        return rankedIps.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> GetLocalUrls(int port, string scheme = "http")
    {
        var urls = new List<string>();
        string normalizedScheme = string.IsNullOrWhiteSpace(scheme) ? "http" : scheme.TrimEnd(':', '/');

        foreach (string ip in GetLocalIpAddresses())
        {
            urls.Add($"{normalizedScheme}://{ip}:{port}");
        }

        return urls;
    }

    public string GetPreferredLocalUrl(int port, string scheme = "http")
    {
        string normalizedScheme = string.IsNullOrWhiteSpace(scheme) ? "http" : scheme.TrimEnd(':', '/');
        string primaryIp = GetPrimaryLanIpAddress();
        return $"{normalizedScheme}://{primaryIp}:{port}";
    }

    private string ResolvePrimaryLanIp()
    {
        // Strategy 1: OS Kernel UDP Socket Route Probe (Zero network packets, queries Windows routing table)
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            // 8.8.8.8 on high port: OS evaluates default outbound route for IPv4 internet/gateway traffic
            socket.Connect(new IPAddress(new byte[] { 8, 8, 8, 8 }), 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint &&
                IsValidLanIPv4(endPoint.Address))
            {
                // Ensure this IP is not from an excluded virtual adapter
                if (!IsFromExcludedAdapter(endPoint.Address))
                {
                    return endPoint.Address.ToString();
                }
            }
        }
        catch
        {
            // Fall back to interface heuristic if socket routing probe cannot resolve
        }

        // Strategy 2: Ranked Interface Evaluation
        var candidateIps = GetRankedCandidateIps();
        if (candidateIps.Count > 0)
        {
            return candidateIps[0].ToString();
        }

        return LoopbackIp;
    }

    private static List<IPAddress> GetRankedCandidateIps()
    {
        var scoredList = new List<(IPAddress Address, int Score)>();

        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                bool isVirtualOrVpn = IsVirtualOrVpnAdapter(ni);
                IPInterfaceProperties properties = ni.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(g.Address) &&
                    !g.Address.Equals(IPAddress.Any));

                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    IPAddress addr = unicast.Address;
                    if (addr.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(addr) ||
                        !IsValidLanIPv4(addr))
                    {
                        continue;
                    }

                    int score = 0;

                    // Physical network adapter types
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        score += 50;
                    }

                    // Has active default gateway (connected to a router)
                    if (hasGateway)
                    {
                        score += 100;
                    }

                    // Private RFC 1918 subnets
                    byte[] bytes = addr.GetAddressBytes();
                    if (bytes[0] == 192 && bytes[1] == 168)
                    {
                        score += 40; // Most common home router subnet (192.168.x.x)
                    }
                    else if (bytes[0] == 10)
                    {
                        score += 30; // 10.0.0.0/8
                    }
                    else if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    {
                        score += 20; // 172.16.0.0/12
                    }

                    // Penalize virtual / VPN adapters heavily
                    if (isVirtualOrVpn)
                    {
                        score -= 200;
                    }

                    // Penalize CGNAT range (100.64.0.0/10 used by Tailscale/carrier-grade NAT)
                    if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                    {
                        score -= 150;
                    }

                    scoredList.Add((addr, score));
                }
            }
        }
        catch
        {
            // Ignore interface enumeration exceptions
        }

        return scoredList
            .OrderByDescending(x => x.Score)
            .Select(x => x.Address)
            .Distinct()
            .ToList();
    }

    private static bool IsValidLanIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();

        // 0.0.0.0 or broadcast
        if (bytes[0] == 0 || (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255))
        {
            return false;
        }

        // Link-local / APIPA (169.254.x.x) - not usable on LAN
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return false;
        }

        return true;
    }

    private static bool IsVirtualOrVpnAdapter(NetworkInterface ni)
    {
        string name = (ni.Name ?? string.Empty).ToLowerInvariant();
        string desc = (ni.Description ?? string.Empty).ToLowerInvariant();

        return ExcludedAdapterKeywords.Any(k => name.Contains(k) || desc.Contains(k));
    }

    private static bool IsFromExcludedAdapter(IPAddress address)
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (IsVirtualOrVpnAdapter(ni))
                {
                    IPInterfaceProperties props = ni.GetIPProperties();
                    if (props.UnicastAddresses.Any(u => u.Address.Equals(address)))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Non-critical check
        }

        return false;
    }
}
