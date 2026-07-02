using PocketMC.Desktop.Features.RemoteControl.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Application.Instances.Services;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemotePlayerActionServiceTests : IDisposable
{
    private readonly ApplicationState _applicationState;
    private readonly InstanceRegistry _registry;
    private readonly Mock<IServerLifecycleService> _lifecycleMock;
    private readonly RemoteAuditLogService _auditLogService;
    private readonly RemotePlayerActionService _service;
    private readonly Guid _javaInstanceId = Guid.NewGuid();
    private readonly Guid _bedrockInstanceId = Guid.NewGuid();
    private readonly string _tempDirectory;

    public RemotePlayerActionServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _applicationState = new ApplicationState();
        _applicationState.Settings.RemoteControl.AllowRemotePlayerActions = true;
        _applicationState.ApplySettings(new AppSettings { AppRootPath = _tempDirectory });

        _registry = new InstanceRegistry(new InstancePathService(_applicationState), NullLogger<InstanceRegistry>.Instance);

        string javaPath = Path.Combine(_tempDirectory, "servers", "Java Server");
        Directory.CreateDirectory(javaPath);
        _registry.Register(new InstanceMetadata { Id = _javaInstanceId, ServerType = "vanilla", Name = "Java Server" }, javaPath);

        string bedrockPath = Path.Combine(_tempDirectory, "servers", "Bedrock Server");
        Directory.CreateDirectory(bedrockPath);
        _registry.Register(new InstanceMetadata { Id = _bedrockInstanceId, ServerType = "bedrock", Name = "Bedrock Server" }, bedrockPath);

        _lifecycleMock = new Mock<IServerLifecycleService>();
        _lifecycleMock.Setup(l => l.GetProcess(It.IsAny<Guid>())).Returns((PocketMC.Domain.Models.ServerProcess?)null);
        _lifecycleMock.Setup(l => l.IsRunning(It.IsAny<Guid>())).Returns(false);

        _auditLogService = new RemoteAuditLogService();
        _service = new RemotePlayerActionService(_applicationState, _registry, _lifecycleMock.Object, _auditLogService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("valid_name")]
    [InlineData("ValidName123")]
    public async Task ExecuteAsync_Java_AllowsValidNames(string name)
    {
        var result = await _service.ExecuteAsync(_javaInstanceId, name, "kick", null, "test-device");
        Assert.False(result.Success);
        Assert.Equal(RemoteControlActionFailure.NotRunning, result.Failure);
    }

    [Theory]
    [InlineData("invalid name with space")]
    [InlineData("name;stop")]
    [InlineData("invalid$name")]
    [InlineData("")]
    [InlineData("toolongname1234567890")]
    public async Task ExecuteAsync_Java_RejectsInvalidNames(string name)
    {
        var result = await _service.ExecuteAsync(_javaInstanceId, name, "kick", null, "test-device");
        Assert.False(result.Success);
        Assert.Equal(RemoteControlActionFailure.Failed, result.Failure);
        Assert.Equal("Invalid player name.", result.Message);
    }

    [Theory]
    [InlineData("Valid Name with spaces")]
    [InlineData("Bedrock_123")]
    public async Task ExecuteAsync_Bedrock_AllowsSpaces(string name)
    {
        var result = await _service.ExecuteAsync(_bedrockInstanceId, name, "kick", null, "test-device");
        Assert.False(result.Success);
        Assert.Equal(RemoteControlActionFailure.NotRunning, result.Failure);
    }

    [Theory]
    [InlineData("name\nstop")]
    [InlineData("name\"")]
    [InlineData("name\'")]
    public async Task ExecuteAsync_Bedrock_RejectsControlCharactersAndQuotes(string name)
    {
        var result = await _service.ExecuteAsync(_bedrockInstanceId, name, "kick", null, "test-device");
        Assert.False(result.Success);
        Assert.Equal(RemoteControlActionFailure.Failed, result.Failure);
        Assert.Equal("Invalid player name.", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidReason()
    {
        var request = new RemotePlayerActionRequest { Reason = "Invalid \n reason" };
        var result = await _service.ExecuteAsync(_javaInstanceId, "valid_name", "kick", request, "test-device");

        Assert.False(result.Success);
        Assert.Equal("Invalid reason format.", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ObeysAllowRemotePlayerActionsSetting()
    {
        _applicationState.Settings.RemoteControl.AllowRemotePlayerActions = false;

        var result = await _service.ExecuteAsync(_javaInstanceId, "valid_name", "kick", null, "test-device");

        Assert.False(result.Success);
        Assert.Equal(RemoteControlActionFailure.Disabled, result.Failure);
    }
}



