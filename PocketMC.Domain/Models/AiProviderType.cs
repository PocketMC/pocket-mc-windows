using System;

namespace PocketMC.Domain.Models;

public enum AiProviderType
{
    Gemini,
    OpenAI,
    Claude,
    Mistral,
    Groq,
    Ollama
}

public class AiApiResult
{
    public bool Success { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? Error { get; init; }

    public static AiApiResult Ok(string content) => new() { Success = true, Content = content };
    public static AiApiResult Fail(string error) => new() { Success = false, Error = error };
}
