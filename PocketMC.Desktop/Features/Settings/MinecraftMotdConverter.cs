using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PocketMC.Desktop.Features.Settings
{
    /// <summary>
    /// Converts Minecraft MOTD strings (legacy §-codes, \u00A7 escaped codes, &amp; codes,
    /// and native JSON format with hex RGB colors) into rich WPF TextBlock elements.
    /// </summary>
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

        /// <summary>
        /// Maps standard Minecraft color names (used in JSON MOTDs) to brushes.
        /// </summary>
        private static readonly Dictionary<string, Brush> NamedColorMap = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase)
        {
            { "black",       ColorMap['0'] },
            { "dark_blue",   ColorMap['1'] },
            { "dark_green",  ColorMap['2'] },
            { "dark_aqua",   ColorMap['3'] },
            { "dark_red",    ColorMap['4'] },
            { "dark_purple", ColorMap['5'] },
            { "gold",        ColorMap['6'] },
            { "gray",        ColorMap['7'] },
            { "dark_gray",   ColorMap['8'] },
            { "blue",        ColorMap['9'] },
            { "green",       ColorMap['a'] },
            { "aqua",        ColorMap['b'] },
            { "red",         ColorMap['c'] },
            { "light_purple",ColorMap['d'] },
            { "yellow",      ColorMap['e'] },
            { "white",       ColorMap['f'] }
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };

            if (string.IsNullOrEmpty(text))
            {
                textBlock.Inlines.Add(new Run("A Minecraft Server") { Foreground = Brushes.Gray });
                return textBlock;
            }

            // Detect JSON MOTD format (starts with '{')
            var trimmed = text.Trim();
            if (trimmed.StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    ParseJsonMotd(doc.RootElement, textBlock, Brushes.Silver, false, false, false, false);
                    return textBlock;
                }
                catch (JsonException)
                {
                    // Not valid JSON, fall through to legacy parsing
                }
            }

            // Legacy parsing: normalise all formatting prefixes to §
            // Handle the escaped \u00A7 form (literal backslash-u-00A7 in the properties file)
            text = text.Replace("\\u00A7", "§");
            text = text.Replace("&", "§");

            // Handle literal \n as line breaks
            text = text.Replace("\\n", "\n");

            ParseLegacyMotd(text, textBlock);
            return textBlock;
        }

        /// <summary>
        /// Parses a legacy §-code MOTD string into styled Runs and adds them to the TextBlock.
        /// </summary>
        private static void ParseLegacyMotd(string text, TextBlock textBlock)
        {
            Brush currentBrush = Brushes.Silver;
            bool isBold = false;
            bool isStrike = false;
            bool isUnderline = false;
            bool isItalic = false;

            var parts = text.Split('§');

            // First part has no preceding color formatting
            if (!string.IsNullOrEmpty(parts[0]))
            {
                AddTextRuns(textBlock, parts[0], currentBrush, isBold, isItalic, isStrike, isUnderline);
            }

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
                    // Color codes reset all styles per Minecraft spec
                    isBold = false; isStrike = false; isUnderline = false; isItalic = false;
                }
                else if (code == 'l') isBold = true;
                else if (code == 'm') isStrike = true;
                else if (code == 'n') isUnderline = true;
                else if (code == 'o') isItalic = true;
                else if (code == 'k')
                {
                    // Obfuscated: we can't animate random chars in WPF, just show the text normally
                }
                else if (code == 'r')
                {
                    currentBrush = Brushes.Silver;
                    isBold = false; isStrike = false; isUnderline = false; isItalic = false;
                }
                else
                {
                    // Not a valid code, treat as standard text including the section sign
                    content = "§" + part;
                }

                if (!string.IsNullOrEmpty(content))
                {
                    AddTextRuns(textBlock, content, currentBrush, isBold, isItalic, isStrike, isUnderline);
                }
            }
        }

        /// <summary>
        /// Adds text to the TextBlock, splitting on newlines to insert LineBreaks.
        /// </summary>
        private static void AddTextRuns(TextBlock textBlock, string content, Brush brush,
            bool bold, bool italic, bool strike, bool underline)
        {
            var lines = content.Split('\n');
            for (int j = 0; j < lines.Length; j++)
            {
                if (j > 0)
                {
                    textBlock.Inlines.Add(new LineBreak());
                }

                if (!string.IsNullOrEmpty(lines[j]))
                {
                    var run = new Run(lines[j]) { Foreground = brush };
                    if (bold) run.FontWeight = FontWeights.Bold;
                    if (italic) run.FontStyle = FontStyles.Italic;

                    // WPF Run can only hold one TextDecorationCollection
                    var decorations = new TextDecorationCollection();
                    if (strike) decorations.Add(TextDecorations.Strikethrough);
                    if (underline) decorations.Add(TextDecorations.Underline);
                    if (decorations.Count > 0) run.TextDecorations = decorations;

                    textBlock.Inlines.Add(run);
                }
            }
        }

        /// <summary>
        /// Recursively parses a JSON MOTD element (the native Minecraft JSON format)
        /// and adds styled Runs to the TextBlock.
        /// Supports: text, color (named + hex #RRGGBB), bold, italic, underlined, strikethrough, extra[].
        /// </summary>
        private static void ParseJsonMotd(JsonElement element, TextBlock textBlock,
            Brush inheritedBrush, bool inheritedBold, bool inheritedItalic,
            bool inheritedStrike, bool inheritedUnderline)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                // Plain string shorthand
                var plainText = element.GetString();
                if (!string.IsNullOrEmpty(plainText))
                {
                    AddTextRuns(textBlock, plainText, inheritedBrush, inheritedBold, inheritedItalic, inheritedStrike, inheritedUnderline);
                }
                return;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    ParseJsonMotd(child, textBlock, inheritedBrush, inheritedBold, inheritedItalic, inheritedStrike, inheritedUnderline);
                }
                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            // Resolve color
            Brush currentBrush = inheritedBrush;
            if (element.TryGetProperty("color", out var colorProp) && colorProp.ValueKind == JsonValueKind.String)
            {
                var colorStr = colorProp.GetString();
                if (!string.IsNullOrEmpty(colorStr))
                {
                    currentBrush = ResolveColor(colorStr) ?? inheritedBrush;
                }
            }

            // Resolve style flags (inherit from parent if not explicitly set)
            bool bold = ResolveBool(element, "bold", inheritedBold);
            bool italic = ResolveBool(element, "italic", inheritedItalic);
            bool strikethrough = ResolveBool(element, "strikethrough", inheritedStrike);
            bool underlined = ResolveBool(element, "underlined", inheritedUnderline);

            // Render "text" property
            if (element.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                var txt = textProp.GetString();
                if (!string.IsNullOrEmpty(txt))
                {
                    // Handle \n inside JSON text values
                    AddTextRuns(textBlock, txt, currentBrush, bold, italic, strikethrough, underlined);
                }
            }

            // Recurse into "extra" array
            if (element.TryGetProperty("extra", out var extraProp) && extraProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in extraProp.EnumerateArray())
                {
                    ParseJsonMotd(child, textBlock, currentBrush, bold, italic, strikethrough, underlined);
                }
            }
        }

        /// <summary>
        /// Resolves a color string to a WPF Brush. Supports standard Minecraft
        /// named colors (e.g., "red", "dark_aqua") and hex codes (e.g., "#ff0000").
        /// </summary>
        private static Brush? ResolveColor(string color)
        {
            if (NamedColorMap.TryGetValue(color, out var namedBrush))
                return namedBrush;

            if (color.StartsWith("#") && color.Length == 7)
            {
                try
                {
                    byte r = System.Convert.ToByte(color.Substring(1, 2), 16);
                    byte g = System.Convert.ToByte(color.Substring(3, 2), 16);
                    byte b = System.Convert.ToByte(color.Substring(5, 2), 16);
                    return new SolidColorBrush(Color.FromRgb(r, g, b));
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads a boolean JSON property, falling back to the inherited value if absent.
        /// </summary>
        private static bool ResolveBool(JsonElement element, string property, bool fallback)
        {
            if (element.TryGetProperty(property, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
            return fallback;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
