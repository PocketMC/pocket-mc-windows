using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.AI;

public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public LlmProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ILlmProvider GetProvider(AiProviderType type)
    {
        var providers = _serviceProvider.GetServices<ILlmProvider>();
        var provider = providers.FirstOrDefault(p => p.ProviderType == type);
        return provider ?? throw new InvalidOperationException($"AI Provider for {type} is not registered.");
    }

    public IReadOnlyList<string> GetProviderNames()
    {
        var names = new List<string>();
        foreach (AiProviderType p in Enum.GetValues(typeof(AiProviderType)))
            names.Add(GetDisplayName(p));
        return names;
    }

    public string GetDisplayName(AiProviderType provider)
    {
        return provider switch
        {
            AiProviderType.Gemini => "Google Gemini",
            AiProviderType.OpenAI => "OpenAI",
            AiProviderType.Claude => "Anthropic Claude",
            AiProviderType.Mistral => "Mistral AI",
            AiProviderType.Groq => "Groq",
            AiProviderType.Ollama => "Ollama",
            _ => provider.ToString()
        };
    }

    public AiProviderType ParseProvider(string name)
    {
        foreach (AiProviderType p in Enum.GetValues(typeof(AiProviderType)))
        {
            if (GetDisplayName(p).Equals(name, StringComparison.OrdinalIgnoreCase) ||
                p.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }
        return AiProviderType.Gemini;
    }

    public (string DefaultModel, string DefaultEndpoint) GetProviderDefaults(AiProviderType provider)
    {
        return provider switch
        {
            AiProviderType.Gemini => ("gemini-2.0-flash", "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent"),
            AiProviderType.OpenAI => ("gpt-4o-mini", "https://api.openai.com/v1/chat/completions"),
            AiProviderType.Claude => ("claude-3-5-haiku-latest", "https://api.anthropic.com/v1/messages"),
            AiProviderType.Mistral => ("mistral-large-latest", "https://api.mistral.ai/v1/chat/completions"),
            AiProviderType.Groq => ("llama-3.3-70b-versatile", "https://api.groq.com/openai/v1/chat/completions"),
            AiProviderType.Ollama => ("llama3.2", "http://localhost:11434/api/chat"),
            _ => (string.Empty, string.Empty)
        };
    }
}
