using System;
using System.Collections.Generic;
using System.Linq;

namespace PocketMC.Desktop.Features.WhatsNew
{
    /// <summary>
    /// Represents a single section (e.g. Features, Fixes, Improvements) in a changelog.
    /// </summary>
    public sealed class ChangelogSection
    {
        public string Name { get; }
        public IReadOnlyList<string> Items { get; }

        public ChangelogSection(string name, IReadOnlyList<string> items)
        {
            Name = name;
            Items = items;
        }
    }

    /// <summary>
    /// Represents a parsed changelog entry with a version and categorized sections.
    /// </summary>
    public sealed class ChangelogEntry
    {
        public string Version { get; }
        public IReadOnlyList<ChangelogSection> Sections { get; }

        public ChangelogEntry(string version, IReadOnlyList<ChangelogSection> sections)
        {
            Version = version;
            Sections = sections;
        }
    }

    /// <summary>
    /// Parses the WhatsNew.txt format into a structured <see cref="ChangelogEntry"/>.
    /// </summary>
    public static class ChangelogParser
    {
        private const string VersionPrefix = "VERSION=";

        /// <summary>
        /// Parses changelog text content into a <see cref="ChangelogEntry"/>.
        /// Returns null if the content is empty or missing the VERSION= header.
        /// </summary>
        public static ChangelogEntry? Parse(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var lines = content
                .Split(new[] { '\r', '\n' }, StringSplitOptions.None)
                .Select(l => l.Trim())
                .ToList();

            // Extract version from VERSION= header
            string? version = null;
            foreach (var line in lines)
            {
                if (line.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    version = line.Substring(VersionPrefix.Length).Trim();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            // Parse sections: [FEATURES], [FIXES], [IMPROVEMENTS], etc.
            var sections = new List<ChangelogSection>();
            string? currentSectionName = null;
            var currentItems = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("[", StringComparison.Ordinal) &&
                    line.EndsWith("]", StringComparison.Ordinal) &&
                    line.Length > 2)
                {
                    // Flush the previous section
                    if (currentSectionName != null && currentItems.Count > 0)
                    {
                        sections.Add(new ChangelogSection(currentSectionName, currentItems.ToList()));
                    }

                    currentSectionName = line.Substring(1, line.Length - 2).Trim();
                    currentItems = new List<string>();
                    continue;
                }

                // Collect non-empty lines under the current section
                if (currentSectionName != null && !string.IsNullOrWhiteSpace(line) &&
                    !line.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    currentItems.Add(line);
                }
            }

            // Flush the last section
            if (currentSectionName != null && currentItems.Count > 0)
            {
                sections.Add(new ChangelogSection(currentSectionName, currentItems.ToList()));
            }

            return new ChangelogEntry(version, sections);
        }
    }
}
