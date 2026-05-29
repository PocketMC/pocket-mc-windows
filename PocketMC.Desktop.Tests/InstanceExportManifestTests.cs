using System.Text.Json;
using PocketMC.Desktop.Features.Instances.ImportExport;

namespace PocketMC.Desktop.Tests;

public class InstanceExportManifestTests
{
    [Fact]
    public void SerializeBedrockManifest_UsesRequestedSchemaShape()
    {
        var manifest = new InstanceExportManifest
        {
            Origin = new InstanceExportOrigin
            {
                PocketMcVersion = "2.4.0",
                Timestamp = DateTimeOffset.Parse("2026-05-29T00:00:00Z")
            },
            ServerMeta = new InstanceExportServerMeta
            {
                Name = "Server Name",
                Description = "Desc",
                Icon = "meta/icon.png"
            },
            Software = new BedrockServerSoftwareManifest
            {
                Type = "BDS",
                MinecraftVersion = "1.20.80"
            },
            Runtime = new InstanceRuntimeManifest
            {
                Type = InstanceRuntimeType.Native
            },
            Addons =
            {
                new BedrockAddonManifest
                {
                    Name = "SparkPortals",
                    Type = InstanceAddonTypes.BehaviorPack,
                    Provider = "Local",
                    Uuid = "xxxxx-xxxx-xxxx",
                    Version = "1.0.0"
                }
            }
        };

        string json = JsonSerializer.Serialize(manifest, InstanceExportManifest.CreateJsonOptions(writeIndented: false));
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement root = document.RootElement;
        Assert.Equal("1.0", root.GetProperty("exportVersion").GetString());
        Assert.Equal("Bedrock", root.GetProperty("software").GetProperty("platform").GetString());
        Assert.True(root.GetProperty("software").GetProperty("loaderVersion").ValueKind == JsonValueKind.Null);
        Assert.Equal("Native", root.GetProperty("runtime").GetProperty("type").GetString());
        Assert.Equal("behavior_pack", root.GetProperty("addons")[0].GetProperty("type").GetString());
        Assert.Equal("xxxxx-xxxx-xxxx", root.GetProperty("addons")[0].GetProperty("uuid").GetString());
    }

    [Fact]
    public void DeserializeManifest_CreatesJavaAndBedrockSpecificBlocks()
    {
        const string json = """
            {
              "exportVersion": "1.0",
              "origin": { "pocketMcVersion": "2.4.0", "timestamp": "2026-05-29T00:00:00Z" },
              "serverMeta": { "name": "Server Name", "description": "Desc", "icon": "meta/icon.png" },
              "software": {
                "platform": "Java",
                "type": "Paper",
                "minecraftVersion": "1.20.4",
                "loaderVersion": null
              },
              "runtime": { "type": "Java", "targetVersion": "21" },
              "addons": [
                {
                  "name": "Essentials",
                  "type": "plugin",
                  "provider": "Modrinth",
                  "projectId": "essentials",
                  "versionId": "1.3",
                  "hash": "sha512-abc"
                },
                {
                  "name": "SparkPortals",
                  "type": "behavior_pack",
                  "provider": "Local",
                  "uuid": "xxxxx-xxxx-xxxx",
                  "version": "1.0.0"
                }
              ]
            }
            """;

        InstanceExportManifest manifest = JsonSerializer.Deserialize<InstanceExportManifest>(
            json,
            InstanceExportManifest.CreateJsonOptions(writeIndented: false))!;

        Assert.IsType<JavaServerSoftwareManifest>(manifest.Software);
        Assert.Equal(InstanceServerPlatform.Java, manifest.Software.Platform);
        Assert.Equal("Paper", manifest.Software.Type);
        Assert.Equal(InstanceRuntimeType.Java, manifest.Runtime.Type);
        Assert.Equal("21", manifest.Runtime.TargetVersion);

        var javaAddon = Assert.IsType<JavaAddonManifest>(manifest.Addons[0]);
        Assert.Equal("essentials", javaAddon.ProjectId);
        Assert.Equal("1.3", javaAddon.VersionId);

        var bedrockAddon = Assert.IsType<BedrockAddonManifest>(manifest.Addons[1]);
        Assert.Equal("xxxxx-xxxx-xxxx", bedrockAddon.Uuid);
        Assert.Equal("1.0.0", bedrockAddon.Version);
    }
}
