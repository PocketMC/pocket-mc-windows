using System;
using System.Collections.Generic;
using PocketMC.Application.Interfaces.Networking;

namespace PocketMC.RemoteControl.Services;

/// <summary>
/// RemoteControl wrapper around the enterprise ILocalNetworkAddressService.
/// </summary>
public sealed class LocalNetworkAddressService : ILocalNetworkAddressService
{
    private readonly ILocalNetworkAddressService _inner;

    public LocalNetworkAddressService()
        : this(new PocketMC.Infrastructure.Networking.LocalNetworkAddressService())
    {
    }

    public LocalNetworkAddressService(ILocalNetworkAddressService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string GetPrimaryLanIpAddress() => _inner.GetPrimaryLanIpAddress();

    public IReadOnlyList<string> GetLocalIpAddresses() => _inner.GetLocalIpAddresses();

    public IReadOnlyList<string> GetLocalUrls(int port, string scheme = "http") => _inner.GetLocalUrls(port, scheme);

    public string GetPreferredLocalUrl(int port, string scheme = "http") => _inner.GetPreferredLocalUrl(port, scheme);
}
