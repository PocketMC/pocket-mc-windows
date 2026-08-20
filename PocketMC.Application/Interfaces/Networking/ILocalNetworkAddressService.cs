using System.Collections.Generic;

namespace PocketMC.Application.Interfaces.Networking;

/// <summary>
/// Service contract for discovering and ranking local area network (LAN) IPv4 addresses and URLs.
/// </summary>
public interface ILocalNetworkAddressService
{
    /// <summary>
    /// Returns the primary active IPv4 LAN address of the host machine (e.g., 192.168.1.50).
    /// Returns 127.0.0.1 if no active network interface is available.
    /// </summary>
    string GetPrimaryLanIpAddress();

    /// <summary>
    /// Returns a ranked list of available local IPv4 addresses, prioritized by physical/gateway status,
    /// ending with loopback (127.0.0.1).
    /// </summary>
    IReadOnlyList<string> GetLocalIpAddresses();

    /// <summary>
    /// Returns local URLs for the specified port and scheme (e.g., http://192.168.1.50:25580).
    /// </summary>
    IReadOnlyList<string> GetLocalUrls(int port, string scheme = "http");

    /// <summary>
    /// Returns the single preferred local URL for the specified port and scheme.
    /// </summary>
    string GetPreferredLocalUrl(int port, string scheme = "http");
}
