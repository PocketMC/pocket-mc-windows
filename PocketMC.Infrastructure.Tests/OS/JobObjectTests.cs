using PocketMC.Infrastructure.OS;

namespace PocketMC.Infrastructure.Tests.OS;

public sealed class JobObjectTests
{
    [Fact]
    public void AddProcess_ThrowsObjectDisposedException_AfterDispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var jobObject = new JobObject();
        jobObject.Dispose();

        Assert.Throws<ObjectDisposedException>(() => jobObject.AddProcess(IntPtr.Zero));
    }
}

