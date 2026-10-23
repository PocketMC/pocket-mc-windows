using PocketMC.Domain.Models;
using PocketMC.Domain.Security;

namespace PocketMC.Application.Services.Instances;

public sealed record WorldRoot(string DirectoryPath, string RelativePath);

public static class WorldInventory
{
    public static IReadOnlyList<WorldRoot> Resolve(string serverDir, InstanceMetadata metadata)
    {
        var properties = ServerPropertiesParser.Read(Path.Combine(serverDir, "server.properties"));
        string name = properties.GetValueOrDefault("level-name")?.Trim() ?? "";
        var roots = new List<WorldRoot>();
        void Add(string relative)
        {
            string path = PathSafety.ValidateContainedPath(serverDir, relative)
                ?? throw new InvalidDataException("The configured world path is unsafe.");
            if (!roots.Any(r => string.Equals(r.DirectoryPath, path, StringComparison.OrdinalIgnoreCase)))
                roots.Add(new(path, relative.Replace('\\', '/')));
        }
        if (metadata.Compatibility.Family == EngineFamily.Pocketmine) Add("worlds");
        else if (metadata.Compatibility.Family == EngineFamily.Bedrock)
            Add(Path.Combine("worlds", name.Length == 0 ? "Bedrock level" : name));
        else
        {
            name = name.Length == 0 ? "world" : name;
            Add(name);
            foreach (string suffix in new[] { "_nether", "_the_end" })
                if (Directory.Exists(Path.Combine(serverDir, name + suffix))) Add(name + suffix);
            // Include independently loaded Java worlds, without scanning runtime/plugin data.
            if (Directory.Exists(serverDir))
                foreach (string directory in Directory.EnumerateDirectories(serverDir))
                    if (!Path.GetFileName(directory).StartsWith('.') && File.Exists(Path.Combine(directory, "level.dat")))
                        Add(Path.GetFileName(directory));
        }
        return roots;
    }
}
