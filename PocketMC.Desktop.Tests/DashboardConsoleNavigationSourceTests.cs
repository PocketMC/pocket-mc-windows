namespace PocketMC.Desktop.Tests;

public sealed class DashboardConsoleNavigationSourceTests
{
    [Fact]
    public void OpenConsole_ResolvesInstancePathAndDoesNotRequireLiveProcess()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Dashboard",
            "DashboardActionsViewModel.cs"));

        string method = ExtractMethod(source, "public void OpenConsole");

        Assert.Contains("_registry.GetPath(vm.Id)", method);
        Assert.DoesNotContain("if (process == null) return;", method);
        Assert.Contains("process != null", method);
        Assert.Contains("vm.Metadata, process, instancePath", method);
        Assert.Contains("vm.Metadata, instancePath", method);
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature '{signature}'.");

        int brace = source.IndexOf('{', start);
        Assert.True(brace > start, "Could not find method body start.");

        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException("Could not find method body end.");
    }
}
