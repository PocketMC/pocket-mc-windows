using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceExportViewModelTests
{
    [Fact]
    public async Task ExportCommand_Success_UpdatesCompletionStateAndPassesRequest()
    {
        var metadata = new InstanceMetadata
        {
            Name = "Export Me",
            ServerType = "Paper",
            MinecraftVersion = "1.20.4"
        };
        var service = new FakeExportService
        {
            ExportHandler = (request, progress, cancellationToken) =>
            {
                progress?.Report(new InstanceTransferProgress
                {
                    CurrentStep = "Exporting world...",
                    OverallProgress = 64,
                    CurrentItem = @"world\region\r.0.0.mca"
                });

                return Task.FromResult(new InstanceExportResult
                {
                    ZipPath = request.DestinationZipPath,
                    SizeBytes = 1234,
                    Manifest = new InstanceExportManifest(),
                    SkippedFiles = new[] { "server.jar", "plugins\\Example.jar" }
                });
            }
        };
        var viewModel = new InstanceExportViewModel(service, metadata, @"C:\PocketMC\servers\export-me")
        {
            DestinationZipPath = @" C:\exports\export-me.zip ",
            IncludeWorlds = false
        };

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\PocketMC\servers\export-me", service.LastRequest!.InstancePath);
        Assert.Equal(@"C:\exports\export-me.zip", service.LastRequest.DestinationZipPath);
        Assert.False(service.LastRequest.IncludeWorlds);
        Assert.False(viewModel.IsExporting);
        Assert.True(viewModel.IsComplete);
        Assert.False(viewModel.HasError);
        Assert.Equal("Export complete.", viewModel.CurrentStep);
        Assert.Equal(100, viewModel.OverallProgress);
        Assert.True(viewModel.HasSkippedFiles);
        Assert.Contains("Skipped 2", viewModel.SkippedFilesSummary);
    }

    [Fact]
    public void UseConfigOnlyDefaultForRunningServer_DisablesWorldsAndExplainsState()
    {
        var viewModel = new InstanceExportViewModel(
            new FakeExportService(),
            new InstanceMetadata { Name = "Running", ServerType = "Bedrock (BDS)" },
            @"C:\PocketMC\servers\running");

        viewModel.UseConfigOnlyDefaultForRunningServer();

        Assert.False(viewModel.IncludeWorlds);
        Assert.Contains("running", viewModel.CurrentStep, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeExportService : IInstanceExportService
    {
        public Func<InstanceExportRequest, IProgress<InstanceTransferProgress>?, CancellationToken, Task<InstanceExportResult>>? ExportHandler { get; set; }
        public InstanceExportRequest? LastRequest { get; private set; }
        public bool IsActive { get; set; }
        public void Cancel() { }

        public Task<InstanceExportResult> ExportAsync(
            InstanceExportRequest request,
            IProgress<InstanceTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            if (ExportHandler is not null)
            {
                return ExportHandler(request, progress, cancellationToken);
            }

            return Task.FromResult(new InstanceExportResult
            {
                ZipPath = request.DestinationZipPath,
                Manifest = new InstanceExportManifest()
            });
        }
    }
}
