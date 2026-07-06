using System;
using System.Collections.Generic;

namespace PocketMC.Domain.Models
{
    public class VersionStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var xParts = x.Split('.');
            var yParts = y.Split('.');

            int maxLength = Math.Max(xParts.Length, yParts.Length);

            for (int i = 0; i < maxLength; i++)
            {
                var xPart = i < xParts.Length ? xParts[i] : string.Empty;
                var yPart = i < yParts.Length ? yParts[i] : string.Empty;

                var (xNum, xSuffix) = ParsePart(xPart);
                var (yNum, ySuffix) = ParsePart(yPart);

                if (xNum != yNum)
                {
                    return xNum.CompareTo(yNum);
                }

                // If numeric parts are equal, compare suffixes.
                // An empty suffix means stable/release, which is newer than a non-empty suffix (e.g., "-beta").
                if (string.IsNullOrEmpty(xSuffix) && !string.IsNullOrEmpty(ySuffix))
                {
                    return 1;
                }
                if (!string.IsNullOrEmpty(xSuffix) && string.IsNullOrEmpty(ySuffix))
                {
                    return -1;
                }

                int suffixCmp = string.Compare(xSuffix, ySuffix, StringComparison.OrdinalIgnoreCase);
                if (suffixCmp != 0)
                {
                    return suffixCmp;
                }
            }

            return 0;
        }

        private static (long Number, string Suffix) ParsePart(string part)
        {
            if (string.IsNullOrEmpty(part))
            {
                return (0, string.Empty);
            }

            int i = 0;
            while (i < part.Length && char.IsDigit(part[i]))
            {
                i++;
            }

            if (i == 0)
            {
                return (0, part);
            }

            long.TryParse(part.Substring(0, i), out var num);
            string suffix = part.Substring(i);
            return (num, suffix);
        }
    }
}
