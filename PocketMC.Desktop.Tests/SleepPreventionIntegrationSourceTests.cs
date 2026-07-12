namespace PocketMC.Desktop.Tests;

public sealed class SleepPreventionIntegrationSourceTests
{
    [Fact]
    public void ServiceCollection_RegistersSleepPreventionServicesInCoreInfrastructure()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Composition",
            "ServiceCollectionExtensions.cs"));

        Assert.Contains("AddSingleton<IExecutionStateApi, Kernel32ExecutionStateApi>()", source);
        Assert.Contains("AddSingleton<SleepPreventionService>()", source);
        Assert.Contains("AddSingleton<ServerSleepPreventionCoordinator>()", source);
    }

    [Fact]
    public void AppStartup_ConstructsSleepPreventionCoordinatorAfterHostStarts()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "App.xaml.cs"));

        int hostStartIndex = source.IndexOf("await _host.StartAsync();", StringComparison.Ordinal);
        int coordinatorIndex = source.IndexOf("GetRequiredService<ServerSleepPreventionCoordinator>().Refresh()", StringComparison.Ordinal);

        Assert.True(hostStartIndex >= 0);
        Assert.True(coordinatorIndex > hostStartIndex);
    }

    [Fact]
    public void ApplicationLifecycle_AlwaysReleasesSleepPreventionDuringGracefulShutdown()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Infrastructure",
            "ApplicationLifecycleService.cs"));

        Assert.Contains("SleepPreventionService", source);
        Assert.Contains("finally", source);
        Assert.Contains("_sleepPreventionService.AllowSleep()", source);
    }

    [Fact]
    public void Coordinator_DefersRefreshFromProcessManagerStateChange()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Infrastructure",
            "Power",
            "ServerSleepPreventionCoordinator.cs"));

        Assert.Contains("_serverProcessManager.OnInstanceStateChanged += OnInstanceStateChanged", source);
        Assert.Contains("_serverProcessManager.OnInstanceStateChanged -= OnInstanceStateChanged", source);
        Assert.Contains("Task.Run(Refresh)", source);
    }
}
