using System;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Domain.Models;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Php;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Php
{
    public sealed class PhpProvisioningServiceTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly ApplicationState _appState;
        private readonly PhpProvisioningService _service;

        public PhpProvisioningServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);

            _appState = new ApplicationState();
            _appState.ApplySettings(new AppSettings { AppRootPath = _tempDirectory });

            var mockFactory = new Moq.Mock<IHttpClientFactory>();
            mockFactory.Setup(f => f.CreateClient(Moq.It.IsAny<string>())).Returns(new HttpClient());

            var downloader = new DownloaderService(mockFactory.Object, NullLogger<DownloaderService>.Instance);
            _service = new PhpProvisioningService(new HttpClient(), downloader, _appState, NullLogger<PhpProvisioningService>.Instance);
        }

        [Fact]
        public void IsPhpVersionPresent_ReturnsFalse_WhenNotInstalled()
        {
            Assert.False(_service.IsPhpVersionPresent("8.2"));
            Assert.Null(_service.GetPhpExecutablePath("8.2"));
        }

        [Fact]
        public void IsPhpVersionPresent_ReturnsTrue_WhenExecutableExists()
        {
            string phpDir = Path.Combine(_tempDirectory, "runtime", "php8.2", "bin", "php");
            Directory.CreateDirectory(phpDir);
            string exe = Path.Combine(phpDir, "php.exe");
            File.WriteAllText(exe, "dummy");

            Assert.True(_service.IsPhpVersionPresent("8.2"));
            Assert.Equal(exe, _service.GetPhpExecutablePath("8.2"));
        }

        [Fact]
        public void GetStatuses_ReturnsEntriesForAllBundledVersions()
        {
            var statuses = _service.GetStatuses();
            Assert.Equal(4, statuses.Count);
            Assert.Contains(statuses, s => s.Version == "8.0");
            Assert.Contains(statuses, s => s.Version == "8.1");
            Assert.Contains(statuses, s => s.Version == "8.2");
            Assert.Contains(statuses, s => s.Version == "8.3");
        }

        [Fact]
        public void AutoMigrates_LegacyRuntimesPhpFolder()
        {
            string legacyDir = Path.Combine(_tempDirectory, "runtimes", "php", "bin", "php");
            Directory.CreateDirectory(legacyDir);
            string legacyExe = Path.Combine(legacyDir, "php.exe");
            File.WriteAllText(legacyExe, "dummy");

            // Resolving 8.2 should migrate legacy runtimes/php to runtime/php8.2
            string? found = _service.GetPhpExecutablePath("8.2");
            Assert.NotNull(found);
            Assert.Contains(Path.Combine("runtime", "php8.2"), found);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch { }
        }
    }
}
