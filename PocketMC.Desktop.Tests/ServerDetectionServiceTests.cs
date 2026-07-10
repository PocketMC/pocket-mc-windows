using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Instances.ImportExport;
using Xunit;

namespace PocketMC.Desktop.Tests
{
    public class ServerDetectionServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public ServerDetectionServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC_ServerDetectionServiceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
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

        [Fact]
        public async Task DetectServerType_Pocketmine_ReturnsPocketmine()
        {
            var detector = new ServerDetectionService(NullLogger<ServerDetectionService>.Instance);
            string serverPath = Path.Combine(_tempDirectory, "PocketmineServer");
            Directory.CreateDirectory(serverPath);
            File.WriteAllText(Path.Combine(serverPath, "PocketMine-MP.phar"), "fake_content");

            var (serverType, _) = await detector.DetectServerTypeAndVersionAsync(serverPath);

            Assert.Equal("Pocketmine", serverType);
        }

        [Fact]
        public async Task DetectServerType_Bedrock_ReturnsBedrock()
        {
            var detector = new ServerDetectionService(NullLogger<ServerDetectionService>.Instance);
            string serverPath = Path.Combine(_tempDirectory, "BedrockServer");
            Directory.CreateDirectory(serverPath);
            File.WriteAllText(Path.Combine(serverPath, "bedrock_server.exe"), "fake_content");

            var (serverType, _) = await detector.DetectServerTypeAndVersionAsync(serverPath);

            Assert.Equal("Bedrock", serverType);
        }

        [Fact]
        public async Task DetectServerType_Fabric_ReturnsFabric()
        {
            var detector = new ServerDetectionService(NullLogger<ServerDetectionService>.Instance);
            string serverPath = Path.Combine(_tempDirectory, "FabricServer");
            Directory.CreateDirectory(serverPath);
            Directory.CreateDirectory(Path.Combine(serverPath, ".fabric"));

            var (serverType, _) = await detector.DetectServerTypeAndVersionAsync(serverPath);

            Assert.Equal("Fabric", serverType);
        }

        [Fact]
        public async Task DetectVersion_FromBrandMatch_ReturnsVersion()
        {
            var detector = new ServerDetectionService(NullLogger<ServerDetectionService>.Instance);
            string serverPath = Path.Combine(_tempDirectory, "PaperServer");
            Directory.CreateDirectory(serverPath);
            File.WriteAllText(Path.Combine(serverPath, "paper-1.20.4-381.jar"), "");

            var (_, version) = await detector.DetectServerTypeAndVersionAsync(serverPath);

            Assert.Equal("1.20.4", version);
        }

        [Fact]
        public async Task DetectVersion_FromMcMatch_ReturnsVersion()
        {
            var detector = new ServerDetectionService(NullLogger<ServerDetectionService>.Instance);
            string serverPath = Path.Combine(_tempDirectory, "FabricServerVersion");
            Directory.CreateDirectory(serverPath);
            File.WriteAllText(Path.Combine(serverPath, "fabric-server-mc.1.20.1-loader.0.14.22.jar"), "");

            var (_, version) = await detector.DetectServerTypeAndVersionAsync(serverPath);

            Assert.Equal("1.20.1", version);
        }
    }
}
