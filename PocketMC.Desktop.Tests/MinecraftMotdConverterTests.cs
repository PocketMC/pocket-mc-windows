using System.Globalization;
using System.Windows.Data;
using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Desktop.Tests;

public class MinecraftMotdConverterTests
{
    [Fact]
    public void ConvertBack_ReturnsBindingDoNothing()
    {
        var converter = new MinecraftMotdConverter();

        var result = converter.ConvertBack("ignored", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Same(Binding.DoNothing, result);
    }
}
