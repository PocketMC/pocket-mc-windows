using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PocketMC.Domain.Storage;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Instances;

public static class ServerPropertiesParser
{
    private static readonly Regex InlineCommentRegex = new(
        @"(?<value>.*?)(?<comment>\s+#.*)?$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public static Dictionary<string, string> Read(string filePath)
    {
        var properties = new Dictionary<string, string>();
        if (!File.Exists(filePath))
        {
            return properties;
        }

        var lines = ReadAllLinesShared(filePath, Encoding.UTF8);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex > 0)
            {
                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = StripInlineComment(trimmed.Substring(separatorIndex + 1)).Trim();
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
            existingLines.AddRange(ReadAllLinesShared(filePath, Encoding.UTF8));
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
                    string inlineComment = ExtractInlineComment(line);
                    newLines.Add($"{key}={newValue}{inlineComment}");
                    keysUpdated.Add(key);
                }
                else
                {
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

        string contents = string.Join(Environment.NewLine, newLines) + Environment.NewLine;
        FileUtils.AtomicWriteAllText(filePath, contents, new UTF8Encoding(false));
    }

    private static string StripInlineComment(string value)
    {
        var match = InlineCommentRegex.Match(value);
        return match.Success ? match.Groups["value"].Value : value;
    }

    private static string ExtractInlineComment(string line)
    {
        int separatorIndex = line.IndexOf('=');
        if (separatorIndex < 0 || separatorIndex == line.Length - 1)
        {
            return string.Empty;
        }

        string valuePortion = line.Substring(separatorIndex + 1);
        var match = InlineCommentRegex.Match(valuePortion);
        return match.Success ? match.Groups["comment"].Value : string.Empty;
    }

    private static string[] ReadAllLinesShared(string filePath, Encoding encoding)
    {
        var lines = new List<string>();
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, encoding);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }
        return lines.ToArray();
    }
}
