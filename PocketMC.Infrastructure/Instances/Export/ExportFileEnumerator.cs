using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Infrastructure.Instances;

public record struct ExportFile(string FullPath, string RelativePath, long SizeBytes);

public class ExportFileEnumerator
{
    private static readonly string[] JavaRootFiles =
    [
        "server.properties",
        "eula.txt",
        "whitelist.json",
        "ops.json",
        "banned-players.json",
        "banned-ips.json",
        "bukkit.yml",
        "spigot.yml",
        "paper.yml",
        "paper-global.yml",
        "paper-world-defaults.yml",
        "commands.yml",
        "permissions.yml",
        "addon_manifest.json"
    ];

    private static readonly string[] BedrockRootFiles =
    [
        "server.properties",
        "allowlist.json",
        "permissions.json",
        "valid_known_packs.json",
        "addon_manifest.json"
    ];

    private static readonly string[] JavaDirectories =
    [
        "config",
        "mods",
        "plugins",
        "world",
        "world_nether",
        "world_the_end"
    ];

    private static readonly string[] BedrockDirectories =
    [
        "behavior_packs",
        "resource_packs",
        "worlds"
    ];

    private static readonly HashSet<string> SkippedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "session.lock"
    };

    private static readonly HashSet<string> BedrockBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".dll",
        ".pdb"
    };

    public async IAsyncEnumerable<ExportFile> EnumerateExportFilesAsync(
        string instanceRoot,
        bool isJava,
        bool includeWorlds,
        List<string> skippedFiles,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (ExportFile file in EnumerateExportFiles(instanceRoot, isJava, includeWorlds, skippedFiles, cancellationToken))
        {
            yield return file;
            await Task.Yield();
        }
    }

    public IEnumerable<ExportFile> EnumerateExportFiles(
        string instanceRoot,
        bool isJava,
        bool includeWorlds,
        List<string> skippedFiles,
        CancellationToken cancellationToken)
    {
        string[] rootFiles = isJava ? JavaRootFiles : BedrockRootFiles;
        foreach (string fileName in rootFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(instanceRoot, fileName);
            if (File.Exists(path) && ShouldIncludeFile(path, isJava))
            {
                yield return CreateExportFile(instanceRoot, path);
            }
        }

        string[] directories = isJava ? JavaDirectories : BedrockDirectories;
        foreach (string directoryName in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!includeWorlds && IsWorldDirectory(directoryName, isJava))
            {
                continue;
            }

            string directory = Path.Combine(instanceRoot, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(instanceRoot, file);

                if (ShouldSkipFile(file, isJava))
                {
                    skippedFiles.Add(ToPortableRelativePath(relativePath));
                    continue;
                }

                yield return CreateExportFile(instanceRoot, file);
            }
        }
    }

    public long EstimateExportBytes(
        string instanceRoot,
        bool isJava,
        bool includeWorlds,
        List<string> skippedFiles,
        CancellationToken cancellationToken)
    {
        long totalBytes = 0;

        foreach (ExportFile file in EnumerateExportFiles(instanceRoot, isJava, includeWorlds, skippedFiles, cancellationToken))
        {
            totalBytes += file.SizeBytes;
        }

        return totalBytes;
    }

    private static bool ShouldIncludeFile(string fullPath, bool isJava) => !ShouldSkipFile(fullPath, isJava);

    private static bool ShouldSkipFile(string fullPath, bool isJava)
    {
        string fileName = Path.GetFileName(fullPath);
        if (SkippedFileNames.Contains(fileName))
        {
            return true;
        }

        if (!isJava)
        {
            string extension = Path.GetExtension(fileName);
            return BedrockBinaryExtensions.Contains(extension);
        }

        return false;
    }

    private static bool IsWorldDirectory(string directoryName, bool isJava)
    {
        if (!isJava)
        {
            return directoryName.Equals("worlds", StringComparison.OrdinalIgnoreCase);
        }

        return directoryName.Equals("world", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("world_nether", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("world_the_end", StringComparison.OrdinalIgnoreCase);
    }

    private static ExportFile CreateExportFile(string instanceRoot, string fullPath)
    {
        var info = new FileInfo(fullPath);
        return new ExportFile(fullPath, ToPortableRelativePath(instanceRoot, fullPath), info.Length);
    }

    private static string ToPortableRelativePath(string root, string fullPath) =>
        ToPortableRelativePath(Path.GetRelativePath(root, fullPath));

    private static string ToPortableRelativePath(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');
}
