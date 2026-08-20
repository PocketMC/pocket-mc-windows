using System;
using System.Net;
using PocketMC.Infrastructure.Networking;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Networking;

public sealed class LocalNetworkAddressServiceTests
{
    [Fact]
    public void GetPrimaryLanIpAddress_ReturnsValidIPv4Address()
    {
        var service = new LocalNetworkAddressService();
        string ip = service.GetPrimaryLanIpAddress();

        Assert.False(string.IsNullOrWhiteSpace(ip));
        Assert.True(IPAddress.TryParse(ip, out var parsedAddress));
        Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, parsedAddress.AddressFamily);
    }

    [Fact]
    public void GetLocalIpAddresses_ContainsPrimaryAndLoopbackAtEnd()
    {
        var service = new LocalNetworkAddressService();
        var ips = service.GetLocalIpAddresses();

        Assert.NotEmpty(ips);
        Assert.Equal("127.0.0.1", ips[^1]);
        Assert.Equal(ips.Count, ips.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GetLocalUrls_FormatsWithSpecifiedPortAndScheme()
    {
        var service = new LocalNetworkAddressService();
        var urls = service.GetLocalUrls(25580, "https");

        Assert.NotEmpty(urls);
        foreach (var url in urls)
        {
            Assert.StartsWith("https://", url);
            Assert.EndsWith(":25580", url);
        }
    }

    [Fact]
    public void GetPreferredLocalUrl_ContainsPrimaryLanIpAndPort()
    {
        var service = new LocalNetworkAddressService();
        string primaryIp = service.GetPrimaryLanIpAddress();
        string preferredUrl = service.GetPreferredLocalUrl(8080);

        Assert.Equal($"http://{primaryIp}:8080", preferredUrl);
    }

    [Fact]
    public void GetPrimaryLanIpAddress_IsCachedConsistently()
    {
        var service = new LocalNetworkAddressService();
        string first = service.GetPrimaryLanIpAddress();
        string second = service.GetPrimaryLanIpAddress();

        Assert.Equal(first, second);
    }
}
