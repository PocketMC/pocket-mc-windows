using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PocketMC.Desktop.Services
{
    public static class ServerPropertiesParser
    {
        public static Dictionary<string, string> Read(string filePath)
        {
            var properties = new Dictionary<string, string>();
            if (!File.Exists(filePath))
            {
                return properties;
            }

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex > 0)
                {
                    var key = trimmed.Substring(0, separatorIndex).Trim();
                    var value = trimmed.Substring(separatorIndex + 1).Trim();
                    properties[key] = value;
                }
            }

            return properties;
        }

        public static void Write(string filePath, Dictionary<string, string> properties)
        {
            var existingLines = new List<string>();
            var keysUpdated = new HashSet<string>();

            if (File.Exists(filePath))
            {
                existingLines.AddRange(File.ReadAllLines(filePath, Encoding.UTF8));
            }

            var newLines = new List<string>();

            foreach (var line in existingLines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                {
                    newLines.Add(line);
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex > 0)
                {
                    var key = trimmed.Substring(0, separatorIndex).Trim();
                    if (properties.TryGetValue(key, out var newValue))
                    {
                        newLines.Add($"{key}={newValue}");
                        keysUpdated.Add(key);
                    }
                }
                else
                {
                    newLines.Add(line);
                }
            }

            // Append new keys that weren't in the file
            foreach (var kvp in properties)
            {
                if (!keysUpdated.Contains(kvp.Key))
                {
                    newLines.Add($"{kvp.Key}={kvp.Value}");
                }
            }

            File.WriteAllLines(filePath, newLines, new UTF8Encoding(false)); // Write without BOM
        }
    }
}
