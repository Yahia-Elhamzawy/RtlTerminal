using System.Text.Json;

namespace RtlTerminal.Ai.Providers;

public sealed class GroqClient : OpenAiCompatibleProviderClient
{
    public override AiProviderKind Provider => AiProviderKind.Groq;

    protected override string BaseUrl => "https://api.groq.com/openai/v1/";

    private static readonly IReadOnlyList<AiModelInfo> DefaultModels = new[]
    {
        new AiModelInfo("llama-3.3-70b-versatile", "Llama 3.3 70B Versatile", "High performance general purpose chat model"),
        new AiModelInfo("llama-3.1-8b-instant", "Llama 3.1 8B Instant", "Ultra-fast response for immediate tasks"),
        new AiModelInfo("deepseek-r1-distill-llama-70b", "DeepSeek R1 Distill Llama 70B", "Reasoning-focused open weights model"),
        new AiModelInfo("mixtral-8x7b-32768", "Mixtral 8x7B 32k", "Mixture of Experts model with long context")
    };

    public override IReadOnlyList<AiModelInfo> GetDefaultModels() => DefaultModels;

    protected override bool FilterModel(AiModelInfo model, JsonElement rawElement)
    {
        // Check "active" boolean property if present
        if (rawElement.TryGetProperty("active", out var activeProp) &&
            activeProp.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        // Exclude audio/whisper and embedding models
        if (model.Id.Contains("whisper", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("embed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    protected override IReadOnlyList<AiModelInfo> PostProcessModels(List<AiModelInfo> models)
    {
        // Put recommended/flagship models (llama-3.3, deepseek, llama-3.1) first
        return models
            .OrderByDescending(m => m.Id.StartsWith("llama-3.3", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.StartsWith("deepseek-r1", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.StartsWith("llama-3.1-70b", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.StartsWith("llama-3.1", StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
