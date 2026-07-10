using PocketMC.Desktop.Features.Instances.ImportExport;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceImportViewModelTests
{
    [Fact]
    public void ZipPath_ControlsImportAvailability()
    {
        var viewModel = new InstanceImportViewModel(new FakeImportService());

        Assert.False(viewModel.CanImport);
        Assert.False(viewModel.ImportCommand.CanExecute(null));

        viewModel.ZipPath = @"C:\imports\server.zip";

        Assert.True(viewModel.CanImport);
        Assert.True(viewModel.ImportCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportCommand_Success_UpdatesCompletionStateAndPassesRequest()
    {
        var service = new FakeImportService
        {
            ImportHandler = (request, progress, cancellationToken) =>
            {
                progress?.Report(new InstanceTransferProgress
                {
                    CurrentStep = "Downloading server files...",
                    OverallProgress = 42,
                    DownloadProgress = 15,
                    CurrentItem = "server.jar"
                });

                return Task.FromResult(new InstanceImportResult
                {
                    InstanceId = Guid.NewGuid(),
                    InstancePath = @"C:\PocketMC\servers\imported",
                    Metadata = new InstanceMetadata { Name = request.RequestedName ?? "Imported" },
                    Manifest = new InstanceExportManifest
                    {
                        ServerMeta = new InstanceExportServerMeta { Name = "Imported" },
                        Software = new JavaServerSoftwareManifest
                        {
                            Type = "Paper",
                            MinecraftVersion = "1.20.4"
                        },
                        Runtime = new InstanceRuntimeManifest
                        {
                            Type = InstanceRuntimeType.Java,
                            TargetVersion = "21"
                        }
                    }
                });
            }
        };
        var viewModel = new InstanceImportViewModel(service)
        {
            ZipPath = @" C:\imports\server.zip ",
            RequestedName = " Imported Copy "
        };

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\imports\server.zip", service.LastRequest!.ZipPath);
        Assert.Equal("Imported Copy", service.LastRequest.RequestedName);
        Assert.False(viewModel.IsImporting);
        Assert.True(viewModel.IsComplete);
        Assert.False(viewModel.HasError);
        Assert.NotNull(viewModel.ImportResult);
        Assert.Equal("Import complete.", viewModel.CurrentStep);
        Assert.Equal(100, viewModel.OverallProgress);
        Assert.Equal(100, viewModel.DownloadProgress);
    }

    [Fact]
    public async Task ImportCommand_AddonUnavailable_SurfacesUiRecoverableError()
    {
        var expected = new AddonUnavailableException(
            "Essentials",
            "Modrinth",
            @"plugins\Essentials.jar",
            "The requested add-on could not be downloaded.");
        var service = new FakeImportService
        {
            ImportHandler = (_, _, _) => Task.FromException<InstanceImportResult>(expected)
        };
        var viewModel = new InstanceImportViewModel(service)
        {
            ZipPath = @"C:\imports\server.zip"
        };
        AddonUnavailableException? eventException = null;
        viewModel.AddonUnavailable += (_, ex) => eventException = ex;

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsImporting);
        Assert.False(viewModel.IsComplete);
        Assert.True(viewModel.HasError);
        Assert.True(viewModel.HasUnavailableAddon);
        Assert.Same(expected, viewModel.UnavailableAddon);
        Assert.Same(expected, eventException);
        Assert.Equal("Add-on unavailable.", viewModel.CurrentStep);
        Assert.Contains("Provider: Modrinth", viewModel.UnavailableAddonDetails);
        Assert.Contains("Add-on: Essentials", viewModel.UnavailableAddonDetails);
        Assert.Contains("Essentials", viewModel.ErrorMessage);
        Assert.Contains("Modrinth", viewModel.ErrorMessage);
    }

    [Fact]
    public void FolderImport_GatingAndCommands()
    {
        var service = new FakeImportService();
        var viewModel = new InstanceImportViewModel(service);

        // Initially package mode
        Assert.True(viewModel.IsPackageImport);
        Assert.False(viewModel.IsFolderImport);

        // Switch mode to Folder
        viewModel.Mode = ImportMode.Folder;
        Assert.False(viewModel.IsPackageImport);
        Assert.True(viewModel.IsFolderImport);

        // CanImport gating constraints
        Assert.False(viewModel.CanImport); // Empty folder path, version, eula

        viewModel.FolderPath = @"C:\TestServer";
        viewModel.MinecraftVersion = "1.20.4";
        viewModel.AcceptEula = true;
        Assert.True(viewModel.CanImport);
    }

    [Fact]
    public async Task ImportCommand_FolderImport_ForwardsAllFieldsCorrectly()
    {
        var service = new FakeImportService();
        var viewModel = new InstanceImportViewModel(service)
        {
            Mode = ImportMode.Folder,
            FolderPath = @"C:\TestServer",
            RequestedName = "My Custom Server",
            SelectedServerType = "Fabric",
            MinecraftVersion = "1.21.1",
            ShouldCopyFiles = false,
            Description = "A clean test server instance",
            AcceptEula = true
        };

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.NotNull(service.LastFolderRequest);
        Assert.Equal(@"C:\TestServer", service.LastFolderRequest.SourceFolderPath);
        Assert.Equal("My Custom Server", service.LastFolderRequest.RequestedName);
        Assert.Equal("Fabric", service.LastFolderRequest.ServerType);
        Assert.Equal("1.21.1", service.LastFolderRequest.MinecraftVersion);
        Assert.False(service.LastFolderRequest.CopyFiles);
        Assert.Equal("A clean test server instance", service.LastFolderRequest.Description);
    }

    private sealed class FakeImportService : IInstanceImportService
    {
        public Func<InstanceImportRequest, IProgress<InstanceTransferProgress>?, CancellationToken, Task<InstanceImportResult>>? ImportHandler { get; set; }
        public InstanceImportRequest? LastRequest { get; private set; }
        public bool IsActive { get; set; }
        public void Cancel() { }

        public Task<InstanceExportManifest> ReadManifestAsync(
            string zipPath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InstanceImportStagingResult> StageImportAsync(
            InstanceImportRequest request,
            IProgress<InstanceTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ReconstructStagedImportAsync(
            InstanceImportStagingResult stagingResult,
            IProgress<InstanceTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
        public Task<InstanceImportResult> ImportAsync(
            InstanceImportRequest request,
            IProgress<InstanceTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            if (ImportHandler is not null)
            {
                return ImportHandler(request, progress, cancellationToken);
            }

            return Task.FromResult(new InstanceImportResult());
        }

        public LocalFolderImportRequest? LastFolderRequest { get; private set; }

        public Task<InstanceImportResult> ImportLocalFolderAsync(
            LocalFolderImportRequest request,
            IProgress<InstanceTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastFolderRequest = request;
            return Task.FromResult(new InstanceImportResult());
        }
    }
}
