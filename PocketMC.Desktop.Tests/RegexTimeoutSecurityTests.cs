using System.Reflection;
using System.Text.RegularExpressions;
using PocketMC.Desktop.Features.CloudBackups;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Tests;

public sealed class RegexTimeoutSecurityTests
{
    [Theory]
    [InlineData(typeof(CloudPathSanitizer))]
    [InlineData(typeof(BackupService))]
    [InlineData(typeof(PlayitAgentService))]
    [InlineData(typeof(SessionLogPreprocessor))]
    [InlineData(typeof(SimpleVoiceChatDetector))]
    public void ParserRegexes_UseFiniteMatchTimeouts(Type parserType)
    {
        foreach (Regex regex in GetRegexFields(parserType))
        {
            Assert.NotEqual(Regex.InfiniteMatchTimeout, regex.MatchTimeout);
        }
    }

    [Fact]
    public void PlayitAgentService_UsesBoundedRegexForLegacyTomlImport()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Tunnel",
            "PlayitAgentService.cs"));

        Assert.DoesNotContain("Match match = Regex.Match(content", source);
        Assert.Contains("LegacyTomlSecretRegex.Match(content)", source);
    }

    private static IEnumerable<Regex> GetRegexFields(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            object? value = field.GetValue(null);
            if (value is Regex regex)
            {
                yield return regex;
            }
            else if (value is IEnumerable<Regex> regexes)
            {
                foreach (Regex item in regexes)
                {
                    yield return item;
                }
            }
        }
    }
}
