using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces.AI;

public interface IOllamaService
{
    Task<OllamaDaemonStatus> CheckDaemonHealthAsync(
        string endpoint,
        CancellationToken ct = default);

    Task<IReadOnlyList<OllamaModelInfo>> GetInstalledModelsAsync(
        string endpoint,
        string? apiKey = null,
        CancellationToken ct = default);

    Task PullModelAsync(
        string endpoint,
        string modelName,
        string? apiKey = null,
        IProgress<OllamaPullProgress>? progress = null,
        CancellationToken ct = default);
}
