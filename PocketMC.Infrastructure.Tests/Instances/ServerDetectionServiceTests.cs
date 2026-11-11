using PocketMC.Infrastructure.Instances;
using PocketMC.Domain.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Instances
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

        [Fact]
        public async Task DetectVersion_ShouldNotBeHijackedByDependencyJars()
        {
            var detector = new ServerDetectionService(NullLogger<ServerDetectionService>.Instance);
            string serverPath = Path.Combine(_tempDirectory, "HijackedDetection");
            Directory.CreateDirectory(serverPath);
            
            // Create a dependency jar with alphabetical precedence (starts with 'l')
            File.WriteAllText(Path.Combine(serverPath, "log4j-2.14.1.jar"), "");
            // Create a valid server jar
            File.WriteAllText(Path.Combine(serverPath, "paper-1.18.2.jar"), "");

            var (_, version) = await detector.DetectServerTypeAndVersionAsync(serverPath);

            Assert.Equal("1.18.2", version);
        }

        [Theory]
        [InlineData("mohist-1.20.1-101-server.jar", "Mohist", "1.20.1")]
        [InlineData("arclight-forge-1.20.1-1.0.2.jar", "Arclight", "1.20.1")]
        [InlineData("magma-1.12.2.jar", "Magma", "")]
        [InlineData("catserver-1.16.5.jar", "CatServer", "1.16.5")]
        [InlineData("purpur-1.20.4.jar", "Purpur", "1.20.4")]
        [InlineData("spigot-1.21.1.jar", "Spigot", "1.21.1")]
        [InlineData("craftbukkit-1.8.8.jar", "Spigot", "1.8.8")]
        public async Task DetectServerType_JarBrand_ReturnsBrandAndVersion(string jarName, string expectedType, string expectedVersionPrefix)
        {
            var detector = new ServerDetectionService(NullLogger<ServerDetectionService>.Instance);
            string serverPath = Path.Combine(_tempDirectory, "BrandServer");
            Directory.CreateDirectory(serverPath);
            File.WriteAllText(Path.Combine(serverPath, jarName), "fake_content");

            var (serverType, version) = await detector.DetectServerTypeAndVersionAsync(serverPath);

            Assert.Equal(expectedType, serverType);
            if (expectedVersionPrefix.Length > 0)
            {
                Assert.StartsWith(expectedVersionPrefix, version);
            }
        }

        [Fact]
        public async Task DetectServerType_MohistWithForgeLibraries_StillReturnsMohist()
        {
            var detector = new ServerDetectionService(NullLogger<ServerDetectionService>.Instance);
            string serverPath = Path.Combine(_tempDirectory, "MohistServer");
            Directory.CreateDirectory(Path.Combine(serverPath, "libraries", "net", "minecraftforge"));
            File.WriteAllText(Path.Combine(serverPath, "mohist-1.20.1-101-server.jar"), "fake_content");

            var (serverType, _) = await detector.DetectServerTypeAndVersionAsync(serverPath);

            Assert.Equal("Mohist", serverType);
        }
    }
}
