using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PocketMC.RemoteControl.Services;

public sealed class LocalNetworkAddressService
{
    public IReadOnlyList<string> GetLocalUrls(int port)
    {
        var urls = new List<string>();
        foreach (IPAddress address in GetLocalIPv4Addresses())
        {
            urls.Add($"http://{address}:{port}");
        }

        urls.Add($"http://127.0.0.1:{port}");
        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string GetPreferredLocalUrl(int port) =>
        GetLocalUrls(port).FirstOrDefault(url => !url.Contains("127.0.0.1", StringComparison.Ordinal))
        ?? $"http://127.0.0.1:{port}";

    public IReadOnlyList<string> GetLocalIpAddresses()
    {
        var ips = GetLocalIPv4Addresses().Select(ip => ip.ToString()).ToList();
        ips.Add("127.0.0.1");
        return ips;
    }

    private static IEnumerable<IPAddress> GetLocalIPv4Addresses()
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address))
                {
                    yield return address.Address;
                }
            }
        }
    }
}
