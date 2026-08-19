using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Tests.TestSupport.Utilities;
using PocketMC.Infrastructure.Security;
using PocketMC.Domain.Models;
using System.Reflection;
using System.Text.RegularExpressions;
using PocketMC.Infrastructure.Backups;

namespace PocketMC.Desktop.Tests.Infrastructure.Architecture;

public sealed class RegexTimeoutSecurityArchitectureTests
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
            "PocketMC.Infrastructure",
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