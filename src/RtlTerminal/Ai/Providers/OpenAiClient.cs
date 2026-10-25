using System.Text.Json;

namespace RtlTerminal.Ai.Providers;

public sealed class OpenAiClient : OpenAiCompatibleProviderClient
{
    public override AiProviderKind Provider => AiProviderKind.OpenAi;

    protected override string BaseUrl => "https://api.openai.com/v1/";

    private static readonly IReadOnlyList<AiModelInfo> DefaultModels = new[]
    {
        new AiModelInfo("gpt-4o", "GPT-4o", "OpenAI's flagship multimodal model"),
        new AiModelInfo("gpt-4o-mini", "GPT-4o Mini", "Fast and lightweight intelligent model"),
        new AiModelInfo("o3-mini", "o3-mini", "High-speed reasoning model for STEM and code"),
        new AiModelInfo("o1", "o1", "Full reasoning model for complex challenges"),
        new AiModelInfo("gpt-4-turbo", "GPT-4 Turbo", "Previous generation GPT-4 model")
    };

    public override IReadOnlyList<AiModelInfo> GetDefaultModels() => DefaultModels;

    protected override bool FilterModel(AiModelInfo model, JsonElement rawElement)
    {
        // Must match chat completion models: gpt-*, o3*, o1*, chatgpt-*
        bool isChatPrefix =
            model.Id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
            model.Id.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
            model.Id.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
            model.Id.StartsWith("chatgpt-", StringComparison.OrdinalIgnoreCase);

        if (!isChatPrefix)
            return false;

        // Exclude non-chat, audio, realtime, or specialized variants
        if (model.Id.Contains("realtime", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("transcription", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("moderation", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("embedding", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("tts", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("dall-e", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    protected override IReadOnlyList<AiModelInfo> PostProcessModels(List<AiModelInfo> models)
    {
        // Prioritize current flagship models: gpt-4o, gpt-4o-mini, o3-mini, o1
        return models
            .OrderByDescending(m => m.Id.Equals("gpt-4o", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.Equals("gpt-4o-mini", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.StartsWith("o3", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.StartsWith("o1", StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
