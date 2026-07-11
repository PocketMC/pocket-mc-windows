using PocketMC.Domain.Models;
using System;
using System.Text.RegularExpressions;

namespace PocketMC.Application.Services.Instances;

public static class SlugHelper
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex InvalidSlugCharacterRegex = new(
        @"[^a-z0-9\-_]",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex RepeatedDashRegex = new(
        @"-+",
        RegexOptions.Compiled,
        RegexTimeout);

    public static string GenerateSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unnamed-server";

        // Convert to lowercase
        string slug = input.ToLowerInvariant();

        // Replace spaces and invalid filename characters with hyphens
        slug = InvalidSlugCharacterRegex.Replace(slug, "-");

        // Remove multiple consecutive hyphens
        slug = RepeatedDashRegex.Replace(slug, "-");

        // Trim hyphens from start and end
        slug = slug.Trim('-');

        if (string.IsNullOrEmpty(slug))
            return "unnamed-server";

        return slug;
    }
}

