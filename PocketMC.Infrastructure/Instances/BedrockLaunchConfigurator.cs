using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances;

public class BedrockLaunchConfigurator
{
    public ProcessStartInfo Configure(InstanceMetadata meta, string workingDir, Action<string> onLog)
    {
        onLog("[PocketMC] Launching Bedrock Dedicated Server...");

        string executablePath = Path.Combine(workingDir, "bedrock_server.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException($"Bedrock server executable not found at {executablePath}. Ensure the ZIP was extracted correctly.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        return psi;
    }
}
