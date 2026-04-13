using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Intelligence;

/// <summary>
/// Orchestrates the full session summarization flow:
/// read logs → preprocess → send to AI → store result.
/// </summary>
public class SessionSummarizationService
{
    private readonly AiApiClient _aiClient;
    private readonly SummaryStorageService _storageService;
    private readonly ILogger<SessionSummarizationService> _logger;

    private const string SystemPrompt = @"You are a Minecraft server session analyst. Given server logs from a play session, produce a structured summary covering:

1. **Session Overview** — Duration, peak player count, overall tone (peaceful, chaotic, productive)
2. **Player Activity** — Who joined and left, with approximate times
3. **Deaths & PvP** — Who died, how they died, any PvP kills (attacker → victim)
4. **Notable Events** — Advancements earned, boss fights, significant commands run by operators
5. **Errors & Issues** — Any warnings, errors, or crashes that occurred
6. **Session Highlights** — 2-3 sentence narrative summary of the most interesting moments

Format the output in clean Markdown. Be concise but thorough. If no events occurred in a category, skip it.
Do NOT include raw log lines — only summarized information.";

    public SessionSummarizationService(
        AiApiClient aiClient,
        SummaryStorageService storageService,
        ILogger<SessionSummarizationService> logger)
    {
        _aiClient = aiClient;
        _storageService = storageService;
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
        DateTime sessionStart,
        DateTime sessionEnd,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Read session log (use FileShare.ReadWrite since the log writer may still hold the file)
            var logPath = Path.Combine(serverDir, "logs", "pocketmc-session.log");
            if (!File.Exists(logPath))
                return SummarizationResult.Fail("No session log found. The server may not have generated any output.");

            string rawLog;
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                rawLog = await reader.ReadToEndAsync(ct);
            }
            if (string.IsNullOrWhiteSpace(rawLog))
                return SummarizationResult.Fail("Session log is empty.");

            // 2. Preprocess
            var processedLog = SessionLogPreprocessor.Preprocess(rawLog);
            if (processedLog == null)
                return SummarizationResult.Fail("Session was too short to summarize (fewer than 5 meaningful events).");

            // 3. Chunk if necessary and summarize
            var chunks = SessionLogPreprocessor.ChunkLog(processedLog);
            string finalContent;

            if (chunks.Count == 1)
            {
                // Single chunk — direct summarization
                var result = await _aiClient.SendAsync(provider, apiKey, SystemPrompt, chunks[0], ct);
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
                    var result = await _aiClient.SendAsync(provider, apiKey, chunkPrompt, chunks[i], ct);
                    if (!result.Success)
                        return SummarizationResult.Fail($"AI API error on chunk {i + 1}: {result.Error}");

                    partialSummaries.AppendLine($"--- Part {i + 1} ---");
                    partialSummaries.AppendLine(result.Content);
                    partialSummaries.AppendLine();
                }

                // Meta-summarize
                var metaResult = await _aiClient.SendAsync(provider, apiKey,
                    SystemPrompt + "\n\nYou are given partial summaries of a long session. Combine them into a single cohesive summary.",
                    partialSummaries.ToString(), ct);

                if (!metaResult.Success)
                    return SummarizationResult.Fail($"AI API error during final summary: {metaResult.Error}");

                finalContent = metaResult.Content;
            }

            // 4. Save
            var summary = new SessionSummary
            {
                ServerName = serverName,
                SessionStart = sessionStart,
                SessionEnd = sessionEnd,
                Duration = sessionEnd - sessionStart,
                Content = finalContent,
                AiProvider = AiApiClient.GetDisplayName(provider),
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
