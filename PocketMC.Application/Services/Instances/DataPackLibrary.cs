using PocketMC.Domain.Security;

namespace PocketMC.Application.Services.Instances;

public sealed record DataPackEntry(string Name, bool IsEnabled, bool IsFolder, string Path);

/// <summary>
/// Lists, enables, disables and removes Java data packs in a world.
/// Disabled packs are parked in a sibling folder because Minecraft loads every folder under <c>datapacks</c> that has a pack.mcmeta.
/// </summary>
public static class DataPackLibrary
{
    public const string ActiveFolder = "datapacks";
    public const string DisabledFolder = "datapacks.disabled";

    public static IReadOnlyList<DataPackEntry> List(string worldDir)
    {
        var entries = new List<DataPackEntry>();
        Scan(System.IO.Path.Combine(worldDir, ActiveFolder), true, entries);
        Scan(System.IO.Path.Combine(worldDir, DisabledFolder), false, entries);
        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Scan(string dir, bool enabled, List<DataPackEntry> into)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string path in Directory.EnumerateFileSystemEntries(dir))
        {
            string name = System.IO.Path.GetFileName(path);
            if (name.StartsWith('.')) continue;
            bool isFolder = Directory.Exists(path);
            if (isFolder ? !File.Exists(System.IO.Path.Combine(path, "pack.mcmeta"))
                         : !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            into.Add(new DataPackEntry(name, enabled, isFolder, path));
        }
    }

    public static void SetEnabled(string worldDir, string name, bool enabled)
    {
        string from = Resolve(worldDir, enabled ? DisabledFolder : ActiveFolder, name);
        string toDir = System.IO.Path.Combine(worldDir, enabled ? ActiveFolder : DisabledFolder);
        string to = Resolve(worldDir, enabled ? ActiveFolder : DisabledFolder, name);
        Directory.CreateDirectory(toDir);
        if (Directory.Exists(from)) Directory.Move(from, to);
        else if (File.Exists(from)) File.Move(from, to, overwrite: true);
        else throw new FileNotFoundException("Data pack not found.", name);
    }

    public static void Remove(string worldDir, string name)
    {
        foreach (string folder in new[] { ActiveFolder, DisabledFolder })
        {
            string path = Resolve(worldDir, folder, name);
            if (Directory.Exists(path)) { Directory.Delete(path, true); return; }
            if (File.Exists(path)) { File.Delete(path); return; }
        }
    }

    private static string Resolve(string worldDir, string folder, string name)
    {
        if (name != System.IO.Path.GetFileName(name)) throw new InvalidDataException("Unsafe data pack name.");
        return PathSafety.ValidateContainedPath(System.IO.Path.Combine(worldDir, folder), name)
            ?? throw new InvalidDataException("Unsafe data pack name.");
    }
}
