using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public class PlayitService
    {
        private readonly HttpClient _httpClient;
        private readonly SettingsManager _settingsManager;

        public PlayitService(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
        }

        public async Task<string> ClaimPlayitAccountAsync(string appRootPath)
        {
            string playitExePath = Path.Combine(appRootPath, "runtime", "playit", "playit.exe");
            if (!File.Exists(playitExePath)) return "https://playit.gg/login";
            
            var psi = new ProcessStartInfo
            {
                FileName = playitExePath,
                Arguments = "claim",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            try
            {
                var process = Process.Start(psi);
                if (process == null) return "https://playit.gg/login";

                string claimUrl = "https://playit.gg/login";
                var tcs = new TaskCompletionSource<string>();

                // Continuously drain stdout to prevent deadlocks when pipe buffer gets full
                Task.Run(async () =>
                {
                    try
                    {
                        while (!process.StandardOutput.EndOfStream)
                        {
                            string? line = await process.StandardOutput.ReadLineAsync();
                            if (line == null) break;

                            if (claimUrl == "https://playit.gg/login")
                            {
                                var match = Regex.Match(line, @"https:/\/playit\.gg\/claim\/[a-zA-Z0-9\-]+");
                                if (match.Success)
                                {
                                    claimUrl = match.Value;
                                    tcs.TrySetResult(match.Value);
                                }
                            }
                        }
                    }
                    catch { }
                });

                // Continuously drain stderr for same reason
                Task.Run(async () =>
                {
                    try
                    {
                        while (!process.StandardError.EndOfStream)
                        {
                            await process.StandardError.ReadLineAsync();
                        }
                    }
                    catch { }
                });

                // Wait for the URL or give up after 10 seconds (it falls back to generic login url)
                var delayTask = Task.Delay(10000);
                if (await Task.WhenAny(tcs.Task, delayTask) == tcs.Task)
                {
                    return tcs.Task.Result;
                }

                return claimUrl;
            }
            catch {}

            return "https://playit.gg/login";
        }

        public async Task<string?> TryExtractSecretAsync(string appRootPath)
        {
            string playitExePath = Path.Combine(appRootPath, "runtime", "playit", "playit.exe");
            if (!File.Exists(playitExePath)) return null;

            var psi = new ProcessStartInfo
            {
                FileName = playitExePath,
                Arguments = "secret-path",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            try
            {
                var process = Process.Start(psi);
                if (process != null)
                {
                    string pathOutput = await process.StandardOutput.ReadToEndAsync();
                    
                    // Regex to find potential paths, stripping ansi color codes if any
                    string cleanPath = Regex.Replace(pathOutput, @"\e\[[0-9;]*m", "").Trim();
                    
                    // Look for the TOML file line specifically if it outputs multiple lines
                    foreach (var line in cleanPath.Split('\n'))
                    {
                        string path = line.Trim();
                        if (File.Exists(path))
                        {
                            string content = File.ReadAllText(path);
                            // Either TOML format
                            var match = Regex.Match(content, @"secret_key\s*=\s*""([^""]+)""");
                            if (match.Success) return match.Groups[1].Value;
                            
                            // Or raw string
                            if (content.Length > 20 && !content.Contains("=")) return content.Trim();
                        }
                    }
                }
            }
            catch {}

            return null;
        }

        public async Task<string> GetPublicAddressForPortAsync(int localPort)
        {
            var settings = _settingsManager.Load();
            if (string.IsNullOrEmpty(settings.PlayitSecretKey))
                return string.Empty;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.playit.cloud/agent/tunnels");
                // The API format might differ; handling gracefully if it fails
                request.Headers.Add("Authorization", $"agent {settings.PlayitSecretKey}");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var tunnels = JsonSerializer.Deserialize<PlayitTunnelsResponse>(content);
                    if (tunnels != null)
                    {
                        foreach (var t in tunnels.Tunnels)
                        {
                            if (t.LocalPort == localPort || (t.PortMapping != null && t.PortMapping.To == localPort))
                            {
                                return string.IsNullOrEmpty(t.CustomDomain) ? t.AssignedDomain : t.CustomDomain;
                            }
                        }
                    }
                }
            }
            catch {}

            return string.Empty;
        }
    }
}
