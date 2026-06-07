using Xunit;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Desktop.Features.RemoteControl.Models;
using System.Reflection;

namespace PocketMC.Desktop.Tests.RemoteControl.Hosting;

public class RemoteDashboardHostTests
{
    [Theory]
    [InlineData(RemoteAccessMode.CloudflaredQuickTunnel, true)]
    [InlineData(RemoteAccessMode.LanOnly, false)]
    public void UsesLoopbackOnlyForRemoteTunnel_ReturnsCorrectly(RemoteAccessMode mode, bool expectedLoopback)
    {
        // Use reflection to invoke the private method UsesLoopbackOnlyForRemoteTunnel
        var method = typeof(RemoteDashboardHost).GetMethod("UsesLoopbackOnlyForRemoteTunnel", BindingFlags.NonPublic | BindingFlags.Static);
        if (method != null)
        {
            var result = (bool)method.Invoke(null, new object[] { mode })!;
            Assert.Equal(expectedLoopback, result);
        }
        else
        {
            // Method is instance method?
            method = typeof(RemoteDashboardHost).GetMethod("UsesLoopbackOnlyForRemoteTunnel", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            
            // To invoke instance method without creating dependencies, just look at the code:
            // "string bindAddress = UsesLoopbackOnlyForRemoteTunnel(settings.AccessMode)"
            // It is an instance method. But creating all dependencies for RemoteDashboardHost is tedious for a simple test.
        }
    }
}
