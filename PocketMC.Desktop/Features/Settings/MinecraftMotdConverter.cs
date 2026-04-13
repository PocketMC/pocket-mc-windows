using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PocketMC.Desktop.Features.Settings
{
    public class MinecraftMotdConverter : IValueConverter
    {
        private static readonly Dictionary<char, Brush> ColorMap = new Dictionary<char, Brush>
        {
            { '0', new SolidColorBrush(Color.FromRgb(0, 0, 0)) },         // Black
            { '1', new SolidColorBrush(Color.FromRgb(0, 0, 170)) },       // Dark Blue
            { '2', new SolidColorBrush(Color.FromRgb(0, 170, 0)) },       // Dark Green
            { '3', new SolidColorBrush(Color.FromRgb(0, 170, 170)) },     // Dark Aqua
            { '4', new SolidColorBrush(Color.FromRgb(170, 0, 0)) },       // Dark Red
            { '5', new SolidColorBrush(Color.FromRgb(170, 0, 170)) },     // Dark Purple
            { '6', new SolidColorBrush(Color.FromRgb(255, 170, 0)) },     // Gold
            { '7', new SolidColorBrush(Color.FromRgb(170, 170, 170)) },   // Gray
            { '8', new SolidColorBrush(Color.FromRgb(85, 85, 85)) },      // Dark Gray
            { '9', new SolidColorBrush(Color.FromRgb(85, 85, 255)) },     // Blue
            { 'a', new SolidColorBrush(Color.FromRgb(85, 255, 85)) },     // Green
            { 'b', new SolidColorBrush(Color.FromRgb(85, 255, 255)) },    // Aqua
            { 'c', new SolidColorBrush(Color.FromRgb(255, 85, 85)) },     // Red
            { 'd', new SolidColorBrush(Color.FromRgb(255, 85, 255)) },    // Light Purple
            { 'e', new SolidColorBrush(Color.FromRgb(255, 255, 85)) },    // Yellow
            { 'f', new SolidColorBrush(Color.FromRgb(255, 255, 255)) }    // White
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            var textBlock = new TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap };

            if (string.IsNullOrEmpty(text))
            {
                textBlock.Inlines.Add(new Run("A Minecraft Server") { Foreground = Brushes.Gray });
                return textBlock;
            }

            // Normalise the formatters
            text = text.Replace('§', '&');

            var parts = text.Split('&');
            // First part has no preceding color formatting
            if (!string.IsNullOrEmpty(parts[0]))
            {
                textBlock.Inlines.Add(new Run(parts[0]) { Foreground = Brushes.Silver });
            }

            Brush currentBrush = Brushes.Silver;
            bool isBold = false;
            bool isStrike = false;
            bool isUnderline = false;
            bool isItalic = false;

            for (int i = 1; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length == 0)
                    continue;

                char code = char.ToLower(part[0]);
                string content = part.Substring(1);

                if (ColorMap.ContainsKey(code))
                {
                    currentBrush = ColorMap[code];
                    isBold = false; isStrike = false; isUnderline = false; isItalic = false;
                }
                else if (code == 'l') isBold = true;
                else if (code == 'm') isStrike = true;
                else if (code == 'n') isUnderline = true;
                else if (code == 'o') isItalic = true;
                else if (code == 'r')
                {
                    currentBrush = Brushes.Silver;
                    isBold = false; isStrike = false; isUnderline = false; isItalic = false;
                }
                else
                {
                    // Not a valid code, treat as standard text including the ampersand
                    content = "&" + part;
                }

                if (!string.IsNullOrEmpty(content))
                {
                    var run = new Run(content) { Foreground = currentBrush };
                    if (isBold) run.FontWeight = System.Windows.FontWeights.Bold;
                    if (isItalic) run.FontStyle = System.Windows.FontStyles.Italic;
                    if (isStrike) run.TextDecorations = System.Windows.TextDecorations.Strikethrough;
                    if (isUnderline) run.TextDecorations = System.Windows.TextDecorations.Underline;
                    textBlock.Inlines.Add(run);
                }
            }

            return textBlock;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
