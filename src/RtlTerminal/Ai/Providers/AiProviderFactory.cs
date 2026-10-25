using System.Collections.Concurrent;

namespace RtlTerminal.Ai.Providers;

public static class AiProviderFactory
{
    private static readonly ConcurrentDictionary<AiProviderKind, IAiProviderClient> CachedClients = new();

    /// <summary>
    /// Creates or retrieves a cached provider client for the specified AI provider kind.
    /// </summary>
    public static IAiProviderClient Create(AiProviderKind kind)
    {
        return CachedClients.GetOrAdd(kind, static k => k switch
        {
            AiProviderKind.GoogleGemini => new GoogleGeminiClient(),
            AiProviderKind.Groq => new GroqClient(),
            AiProviderKind.OpenRouter => new OpenRouterClient(),
            AiProviderKind.NvidiaNim => new NvidiaNimClient(),
            AiProviderKind.OpenAi => new OpenAiClient(),
            _ => throw new ArgumentOutOfRangeException(nameof(k), k, $"Unsupported AI provider: {k}")
        });
    }
}
