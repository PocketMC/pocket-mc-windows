using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Console;
using PocketMC.Domain.Models;
using PocketMC.Application.Interfaces.AI;

namespace PocketMC.Desktop.Features.Intelligence;

/// <summary>
/// Orchestrates the full session summarization flow:
/// read logs → preprocess → send to AI → store result.
/// </summary>
public class SessionSummarizationService
{
    private readonly ILlmProviderFactory _providerFactory;
    private readonly SummaryStorageService _storageService;
    private readonly ConsoleLogHistoryService _logHistoryService;
    private readonly ILogger<SessionSummarizationService> _logger;

    private const string SystemPrompt = @"You are an expert Minecraft server analyst. Summarize the entire session log provided earlier. 
Focus on:
- Important events
- Crashes, warnings, and lag spikes
- Player activity
- Plugin/mod issues
- Configuration problems
- Performance metrics
- Recommendations for improvement

Keep the structure clean and easy to skim.

If the logs include sensitive data (IPs, emails), DO NOT include them in the summary.";

    public SessionSummarizationService(
        ILlmProviderFactory providerFactory,
        SummaryStorageService storageService,
        ConsoleLogHistoryService logHistoryService,
        ILogger<SessionSummarizationService> logger)
    {
        _providerFactory = providerFactory;
        _storageService = storageService;
        _logHistoryService = logHistoryService;
        _logger = logger;
    }

    /// <summary>
    /// Generate and store a session summary. Returns the saved summary or null on failure.
    /// </summary>
    public async Task<SummarizationResult> SummarizeAsync(
        string serverDir,
        string serverName,
        AiProviderType provider,
        string apiKey,
        string? modelName,
        string? endpointUrl,
        DateTime sessionStart,
        DateTime sessionEnd,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Read session log (use FileShare.ReadWrite since the log writer may still hold the file)
            var logPath = _logHistoryService.GetSessionLogPath(serverDir, preferCurrentSession: true);
            if (logPath == null || !File.Exists(logPath))
                return SummarizationResult.Fail("No session log found. The server may not have generated any output.");

            // 1 & 2. Read and Preprocess log directly via Stream (use FileShare.ReadWrite since the log writer may still hold the file)
            string? processedLog;
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                processedLog = SessionLogPreprocessor.Preprocess(fs);
            }

            if (string.IsNullOrWhiteSpace(processedLog))
                return SummarizationResult.Fail("Session was too short to summarize (fewer than 5 meaningful events) or log is empty.");

            // 3. Chunk if necessary and summarize
            var chunks = SessionLogPreprocessor.ChunkLog(processedLog);
            string finalContent;

            if (chunks.Count == 1)
            {
                // Single chunk — direct summarization
                var providerImpl = _providerFactory.GetProvider(provider);
                var result = await providerImpl.GenerateCompletionAsync(apiKey, modelName ?? "", endpointUrl ?? "", SystemPrompt, chunks[0], ct);
                if (!result.Success)
                    return SummarizationResult.Fail($"AI API error: {result.Error}");
                finalContent = result.Content;
            }
            else
            {
                // Multiple chunks — summarize each, then meta-summarize
                _logger.LogInformation("Large log detected for {Server}. Splitting into {Count} chunks.", serverName, chunks.Count);
                var partialSummaries = new StringBuilder();

                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunkPrompt = $"This is part {i + 1} of {chunks.Count} of the server logs. Summarize this section:";
                    var providerImpl = _providerFactory.GetProvider(provider);
                    var result = await providerImpl.GenerateCompletionAsync(apiKey, modelName ?? "", endpointUrl ?? "", chunkPrompt, chunks[i], ct);
                    if (!result.Success)
                        return SummarizationResult.Fail($"AI API error on chunk {i + 1}: {result.Error}");

                    partialSummaries.AppendLine($"--- Part {i + 1} ---");
                    partialSummaries.AppendLine(result.Content);
                    partialSummaries.AppendLine();
                }

                // Meta-summarize
                var providerImplMeta = _providerFactory.GetProvider(provider);
                var metaResult = await providerImplMeta.GenerateCompletionAsync(apiKey, modelName ?? "", endpointUrl ?? "",
                    SystemPrompt + "\n\nYou are given partial summaries of a long session. Combine them into a single cohesive summary.",
                    partialSummaries.ToString(), ct);

                if (!metaResult.Success)
                    return SummarizationResult.Fail($"AI API error during final summary: {metaResult.Error}");

                finalContent = metaResult.Content;
            }

            finalContent = SummaryEmojiFormatter.Apply(finalContent);

            // 4. Save
            var summary = new SessionSummary
            {
                ServerName = serverName,
                SessionStart = sessionStart,
                SessionEnd = sessionEnd,
                Duration = sessionEnd - sessionStart,
                Content = finalContent,
                AiProvider = _providerFactory.GetDisplayName(provider),
                GeneratedAt = DateTime.UtcNow
            };

            var savedPath = _storageService.Save(serverDir, summary);
            _logger.LogInformation("Session summary saved for {Server} at {Path}.", serverName, savedPath);

            return SummarizationResult.Ok(summary);
        }
        catch (OperationCanceledException)
        {
            return SummarizationResult.Fail("Summarization was cancelled.");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error during summarization for {Server}.", serverName);
            return SummarizationResult.Fail($"File system error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during summarization for {Server}.", serverName);
            return SummarizationResult.Fail($"Unexpected error: {ex.Message}");
        }
    }
}

public class SummarizationResult
{
    public bool Success { get; init; }
    public SessionSummary? Summary { get; init; }
    public string? Error { get; init; }

    public static SummarizationResult Ok(SessionSummary summary) => new() { Success = true, Summary = summary };
    public static SummarizationResult Fail(string error) => new() { Success = false, Error = error };
}

