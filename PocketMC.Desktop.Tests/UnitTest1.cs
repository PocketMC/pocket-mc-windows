using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Tests;

public class SlugHelperTests
{
    [Theory]
    [InlineData(" My Cool Server! ", "my-cool-server")]
    [InlineData("  spaces  ", "spaces")]
    [InlineData("", "unnamed-server")]
    [InlineData("---", "unnamed-server")]
    public void GenerateSlug_HandlesEdgeCases(string input, string expected)
    {
        Assert.Equal(expected, SlugHelper.GenerateSlug(input));
    }
}
