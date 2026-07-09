using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Helpers
{
    public static class SemanticVersionHelper
    {
        public static bool IsCompatible(string? range, string? version)
        {
            if (string.IsNullOrWhiteSpace(range) || string.IsNullOrWhiteSpace(version))
                return true;

            range = range.Trim();
            if (range == "*" || range.Equals("any", StringComparison.OrdinalIgnoreCase))
                return true;

            range = ConvertMavenRangeToNpm(range);

            if (!TryParseVersion(version, out var vParts))
                return false; // Cannot parse installed version

            // Handle OR conditions
            if (range.Contains("||"))
            {
                var orParts = range.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);
                return orParts.Any(p => SatisfiesAndConditions(p, vParts));
            }

            return SatisfiesAndConditions(range, vParts);
        }

        private static string ConvertMavenRangeToNpm(string range)
        {
            range = range.Trim();
            
            // Check if it's a Maven range. They always start with [ or ( and end with ] or )
            if ((range.StartsWith("[") || range.StartsWith("(")) && (range.EndsWith("]") || range.EndsWith(")")))
            {
                var isLowerInclusive = range.StartsWith("[");
                var isUpperInclusive = range.EndsWith("]");
                
                var inner = range.Substring(1, range.Length - 2).Trim();
                
                // Exact version match e.g. [1.20]
                if (!inner.Contains(","))
                {
                    return "=" + inner;
                }
                
                var parts = inner.Split(',');
                var lower = parts[0].Trim();
                var upper = parts.Length > 1 ? parts[1].Trim() : "";
                
                var npmRange = "";
                if (!string.IsNullOrEmpty(lower))
                {
                    npmRange += (isLowerInclusive ? ">=" : ">") + lower;
                }
                
                if (!string.IsNullOrEmpty(upper))
                {
                    if (npmRange.Length > 0) npmRange += " ";
                    npmRange += (isUpperInclusive ? "<=" : "<") + upper;
                }
                
                return string.IsNullOrEmpty(npmRange) ? "*" : npmRange;
            }
            
            return range;
        }

        private static bool SatisfiesAndConditions(string andRange, Version v)
        {
            // Split by space for AND conditions (e.g., ">=1.20.1 <1.21.0")
            var conditions = andRange.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var condition in conditions)
            {
                if (!SatisfiesCondition(condition, v))
                    return false;
            }
            return true;
        }

        private static bool SatisfiesCondition(string condition, Version v)
        {
            condition = condition.Trim();

            // Handle caret ^ ranges
            if (condition.StartsWith("^"))
            {
                var caretTargetStr = condition.Substring(1);
                if (!TryParseVersion(caretTargetStr, out var caretTarget)) return false;

                if (caretTarget.Major == 0)
                {
                    // ^0.15.1 -> >=0.15.1 <0.16.0
                    if (v.Major != 0 || v.Minor != caretTarget.Minor || v < caretTarget) return false;
                }
                else
                {
                    // ^1.20.1 -> >=1.20.1 <2.0.0
                    if (v.Major != caretTarget.Major || v < caretTarget) return false;
                }
                return true;
            }

            // Handle tilde ~ ranges
            if (condition.StartsWith("~"))
            {
                var tildeTargetStr = condition.Substring(1);
                if (!TryParseVersion(tildeTargetStr, out var tildeTarget)) return false;
                
                // ~1.20.1 -> >=1.20.1 <1.21.0
                if (v.Major != tildeTarget.Major || v.Minor != tildeTarget.Minor || v < tildeTarget) return false;
                return true;
            }

            // Handle operators
            var match = Regex.Match(condition, @"^(>=|<=|>|<|=)?\s*(.*)$");
            if (!match.Success) return false;

            var op = match.Groups[1].Value;
            var targetStr = match.Groups[2].Value;

            if (string.IsNullOrEmpty(op)) op = "=";

            // Handle wildcard target e.g., "1.20.x"
            if (targetStr.EndsWith(".x") || targetStr.EndsWith(".*"))
            {
                targetStr = targetStr.Substring(0, targetStr.Length - 2);
                if (!TryParseVersion(targetStr, out var targetWildcard)) return false;

                if (op == "=")
                {
                    // 1.20.x means anything matching 1.20
                    int partsCount = targetStr.Split('.').Length;
                    if (partsCount == 1) return v.Major == targetWildcard.Major;
                    if (partsCount == 2) return v.Major == targetWildcard.Major && v.Minor == targetWildcard.Minor;
                    return true;
                }
            }

            if (!TryParseVersion(targetStr, out var target)) return false;

            return op switch
            {
                ">=" => v >= target,
                ">"  => v > target,
                "<=" => v <= target,
                "<"  => v < target,
                "="  => v.Major == target.Major && v.Minor == target.Minor && v.Build == target.Build,
                _    => false
            };
        }

        private static bool TryParseVersion(string versionStr, out Version version)
        {
            version = new Version();
            
            // Strip any pre-release or build metadata (e.g. 1.20.1-rc1)
            var clean = Regex.Replace(versionStr, @"[-+].*$", "");
            
            // Ensure x.y.z format
            var parts = clean.Split('.').ToList();
            if (parts.Count == 0) return false;
            
            // Pad to 3 parts if needed (e.g. "1.20" -> "1.20.0")
            while (parts.Count < 3) parts.Add("0");
            
            // If more than 4 parts, truncate
            if (parts.Count > 4) parts = parts.Take(4).ToList();

            try
            {
                var stringToParse = string.Join(".", parts);
                return Version.TryParse(stringToParse, out var parsedVersion) ? (version = parsedVersion) != null : false;
            }
            catch
            {
                return false;
            }
        }
    }
}
