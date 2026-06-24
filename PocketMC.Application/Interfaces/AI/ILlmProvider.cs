using System.Threading;
using System.Threading.Tasks;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces.AI;

public interface ILlmProvider
{
    AiProviderType ProviderType { get; }

    /// <summary>
    /// Send a request to the AI provider.
    /// </summary>
    Task<AiApiResult> GenerateCompletionAsync(
        string apiKey,
        string model,
        string endpoint,
        string systemPrompt,
        string userContent,
        CancellationToken ct = default);

    /// <summary>
    /// Validates the API key by sending a minimal test request.
    /// </summary>
    Task<AiApiResult> ValidateKeyAsync(
        string apiKey,
        string model,
        string endpoint,
        CancellationToken ct = default);
}

public interface ILlmProviderFactory
{
    ILlmProvider GetProvider(AiProviderType type);
    System.Collections.Generic.IReadOnlyList<string> GetProviderNames();
    string GetDisplayName(AiProviderType provider);
    AiProviderType ParseProvider(string name);
    (string DefaultModel, string DefaultEndpoint) GetProviderDefaults(AiProviderType provider);
}
