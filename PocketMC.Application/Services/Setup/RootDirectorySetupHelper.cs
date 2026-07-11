using System;
using System.IO;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Setup;

public static class RootDirectorySetupHelper
{
    public const string SuggestedFolderName = "PocketMC";

    public static string? GetDefaultParentDirectory()
    {
        string documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documentsDirectory))
        {
            return documentsDirectory;
        }

        string userProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfileDirectory))
        {
            return userProfileDirectory;
        }

        return null;
    }

    public static string ResolveRootPath(string selectedFolderPath)
    {
        if (string.IsNullOrWhiteSpace(selectedFolderPath))
        {
            throw new ArgumentException("A folder path is required.", nameof(selectedFolderPath));
        }

        string fullPath = Path.GetFullPath(selectedFolderPath);
        string normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leafName = Path.GetFileName(normalizedPath);

        if (string.Equals(leafName, SuggestedFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return Path.Combine(fullPath, SuggestedFolderName);
    }
}
