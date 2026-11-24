using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PocketMC.Desktop.Localization;

/// <summary>
/// XAML helper: <c>Text="{loc:Tr Dashboard}"</c>. The English source text doubles as the catalog key.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string source)
    {
        Source = source;
    }

    [ConstructorArgument("source")]
    public string Source { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(nameof(LocalizationService.Version))
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
            Converter = TranslateConverter.Instance,
            ConverterParameter = Source,
        };

        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>Resolves the English source (ConverterParameter) through the active catalog.</summary>
public sealed class TranslateConverter : IValueConverter
{
    public static TranslateConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => LocalizationService.Instance.Get(parameter as string ?? value as string ?? string.Empty);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
