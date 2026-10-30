using System.Linq;
using System.Xml.Linq;
using PocketMC.Desktop.Tests.TestSupport.Utilities;
using Xunit;

namespace PocketMC.Desktop.Tests.Accessibility;

public sealed class PumpkinCreationCalloutTests
{
    [Fact]
    public void PumpkinNxsRequirement_IsPresentedAsWarningCallout()
    {
        var document = XDocument.Load(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop", "Features", "InstanceCreation", "NewInstancePage.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var callout = document.Descendants().SingleOrDefault(element =>
            (string?)element.Attribute(x + "Name") == "PumpkinPremiumCallout");

        Assert.NotNull(callout);
        Assert.Contains(callout.Descendants(), element => element.Name.LocalName == "SymbolIcon");
        Assert.Contains(callout.Descendants(), element =>
            (string?)element.Attribute("Text") == "Free Playit NetherNet requires NXS support." &&
            (string?)element.Attribute("FontWeight") == "SemiBold");
    }
}
