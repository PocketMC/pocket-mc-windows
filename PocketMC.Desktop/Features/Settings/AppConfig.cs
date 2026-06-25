using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Settings
{
    public static class AppConfig
    {
        public static IReadOnlyList<string> AuthProxies { get; } = new List<string>
        {
            "https://pocket-mc-proxy-20d5.onrender.com",
            "https://pocket-mc-proxy-n2qx.onrender.com"
        };
        
        public static IReadOnlyList<string> TelemetryProxies { get; } = new List<string>
        {
            "https://pocket-mc-proxy-3fqm.onrender.com/",
            "https://pocket-mc-proxy.onrender.com/"
        };

        static AppConfig()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("PocketMC.Desktop.pocketmc.yml");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();

                    var authProxies = new List<string>();
                    var telemetryProxies = new List<string>();

                    bool inAuth = false;
                    bool inTelemetry = false;

                    foreach (var line in content.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("auth_proxies:"))
                        {
                            inAuth = true;
                            inTelemetry = false;
                            continue;
                        }
                        if (trimmed.StartsWith("telemetry_proxies:"))
                        {
                            inTelemetry = true;
                            inAuth = false;
                            continue;
                        }

                        if (trimmed.StartsWith("-") && (inAuth || inTelemetry))
                        {
                            var match = Regex.Match(trimmed, @"-\s*""?([^""\r\n]+)""?");
                            if (match.Success)
                            {
                                var url = match.Groups[1].Value;
                                if (inAuth) authProxies.Add(url);
                                if (inTelemetry) telemetryProxies.Add(url);
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                        {
                            if (inAuth || inTelemetry)
                            {
                                inAuth = false;
                                inTelemetry = false;
                            }
                        }
                    }

                    if (authProxies.Count > 0) AuthProxies = authProxies;
                    if (telemetryProxies.Count > 0) TelemetryProxies = telemetryProxies;
                }
            }
            catch { }
        }
    }
}
