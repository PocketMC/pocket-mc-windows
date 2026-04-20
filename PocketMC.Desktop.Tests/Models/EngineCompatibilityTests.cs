using Xunit;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests.Models
{
    public class EngineCompatibilityTests
    {
        [Theory]
        [InlineData("Fabric 1.20.1", EngineFamily.Fabric, true, false, "mods", "fabric")]
        [InlineData("Paper 1.21", EngineFamily.Spigot, false, true, "plugins", "spigot")]
        [InlineData("Forge 1.12.2", EngineFamily.Forge, true, false, "mods", "forge")]
        [InlineData("NeoForge 1.20.4", EngineFamily.NeoForge, true, false, "mods", "neoforge")]
        [InlineData("Pocketmine-MP", EngineFamily.Pocketmine, false, true, "plugins", "pocketmine")]
        [InlineData("Bedrock Dedicated Server", EngineFamily.Bedrock, false, false, "behavior_packs", "bedrock")]
        [InlineData("Vanilla 1.20", EngineFamily.Vanilla, false, false, "mods", "vanilla")]
        public void Compatibility_ShouldMapCorrectly(
            string serverType, 
            EngineFamily expectedFamily, 
            bool supportsMods, 
            bool supportsPlugins, 
            string expectedSubDir,
            string expectedLoader)
        {
            var compat = new EngineCompatibility(serverType);

            Assert.Equal(expectedFamily, compat.Family);
            Assert.Equal(supportsMods, compat.SupportsMods);
            Assert.Equal(supportsPlugins, compat.SupportsPlugins);
            Assert.Equal(expectedSubDir, compat.PrimaryAddonSubDir);
            Assert.Equal(expectedLoader, compat.LoaderName);
        }

        [Fact]
        public void InstanceMetadata_ShouldExposeCompatibility()
        {
            var metadata = new InstanceMetadata { ServerType = "Fabric 1.20.1" };
            Assert.NotNull(metadata.Compatibility);
            Assert.Equal(EngineFamily.Fabric, metadata.Compatibility.Family);
        }

        [Fact]
        public void Bedrock_ShouldSupportAddons()
        {
            var compat = new EngineCompatibility("Bedrock Dedicated Server");
            Assert.True(compat.SupportsBedrockAddons);
            Assert.False(compat.SupportsMods);
            Assert.False(compat.SupportsPlugins);
        }

        [Fact]
        public void Pocketmine_ShouldSupportPoggit()
        {
            var compat = new EngineCompatibility("Pocketmine-MP");
            Assert.True(compat.SupportsPoggit);
            Assert.False(compat.SupportsModrinth);
        }
    }
}
