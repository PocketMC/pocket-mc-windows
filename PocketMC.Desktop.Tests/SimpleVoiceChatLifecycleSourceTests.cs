using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Tests;

public sealed class SimpleVoiceChatLifecycleSourceTests
{
    [Fact]
    public void StopServer_DoesNotRunSimpleVoiceChatPreStartFlow()
    {
        string source = ReadDesktopSource("Features", "Dashboard", "DashboardActionsViewModel.cs");
        string stopBody = ExtractMethodBody(source, "StopServer");

        Assert.DoesNotContain("EnsureSimpleVoiceChatBeforeStartAsync", stopBody);
    }

    [Fact]
    public void DashboardRestart_RunsSimpleVoiceChatPreStartFlowBeforeRestartAsync()
    {
        string source = ReadDesktopSource("Features", "Dashboard", "DashboardActionsViewModel.cs");
        string restartBody = ExtractMethodBody(source, "RestartServer");

        AssertOrder(restartBody, "EnsureSimpleVoiceChatBeforeStartAsync", "RestartAsync");
    }

    [Fact]
    public void ConsoleRestart_RunsSimpleVoiceChatPreStartFlowBeforeRestartAsync()
    {
        string source = ReadDesktopSource("Features", "Console", "ServerConsolePage.xaml.cs");
        string restartBody = ExtractMethodBody(source, "BtnRestart_Click");

        AssertOrder(restartBody, "EnsureSimpleVoiceChatBeforeStartAsync", "RestartAsync");
    }

    [Fact]
    public void AppDialogWindowCloseDefaultsToDismiss()
    {
        string source = ReadDesktopSource("Infrastructure", "AppDialogWindow.xaml.cs");

        Assert.Contains("DialogResult.Dismiss", source);
    }

    private static void AssertOrder(string text, string before, string after)
    {
        int beforeIndex = text.IndexOf(before, StringComparison.Ordinal);
        int afterIndex = text.IndexOf(after, StringComparison.Ordinal);

        Assert.True(beforeIndex >= 0, $"Expected to find {before}.");
        Assert.True(afterIndex >= 0, $"Expected to find {after}.");
        Assert.True(beforeIndex < afterIndex, $"Expected {before} before {after}.");
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        Match match = Regex.Match(source, $@"\b{Regex.Escape(methodName)}\s*\(");
        Assert.True(match.Success, $"Could not find method {methodName}.");

        int braceStart = source.IndexOf('{', match.Index);
        Assert.True(braceStart >= 0, $"Could not find body for {methodName}.");

        int depth = 0;
        for (int i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[braceStart..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method body for {methodName}.");
    }

    private static string ReadDesktopSource(params string[] relativeParts)
    {
        string path = Path.Combine(FindRepositoryRoot(), "PocketMC.Desktop", Path.Combine(relativeParts));

        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        string? explicitRoot = Environment.GetEnvironmentVariable("POCKETMC_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot) &&
            File.Exists(Path.Combine(explicitRoot, "PocketMC.Desktop.sln")))
        {
            return explicitRoot;
        }

        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (string candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PocketMC.Desktop.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate PocketMC repository root.");
    }
}
