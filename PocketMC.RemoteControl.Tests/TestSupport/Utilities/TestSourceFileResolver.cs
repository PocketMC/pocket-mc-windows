namespace PocketMC.RemoteControl.Tests.TestSupport.Utilities;

internal static class TestSourceFileResolver
{
    public static string Resolve(params string[] relativeSegments)
    {
        foreach (string root in EnumerateCandidateRoots())
        {
            string path = Path.Combine(new[] { root }.Concat(relativeSegments).ToArray());
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new DirectoryNotFoundException($"Could not locate source file: {Path.Combine(relativeSegments)}");
    }

    private static IEnumerable<string> EnumerateCandidateRoots()
    {
        string? explicitRoot = Environment.GetEnvironmentVariable("POCKETMC_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            yield return explicitRoot;
        }

        yield return Directory.GetCurrentDirectory();

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}