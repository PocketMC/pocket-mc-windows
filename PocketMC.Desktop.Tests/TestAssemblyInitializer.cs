using System.Runtime.CompilerServices;
using PocketMC.Desktop.Localization;

namespace PocketMC.Desktop.Tests;

internal static class TestAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Desktop tests assert English source strings; pin the shared catalog before any view model runs.
        LocalizationService.Instance.SetLanguage("en");
    }
}
