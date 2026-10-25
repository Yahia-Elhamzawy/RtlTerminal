using System.Text.Json;

namespace RtlTerminal.Ai.Providers;

public sealed class GoogleGeminiClient : OpenAiCompatibleProviderClient
{
    public override AiProviderKind Provider => AiProviderKind.GoogleGemini;

    protected override string BaseUrl => "https://generativelanguage.googleapis.com/v1beta/openai/";

    private static readonly IReadOnlyList<AiModelInfo> DefaultModels = new[]
    {
        new AiModelInfo("gemini-2.0-flash", "Gemini 2.0 Flash", "Fast, high quality multimodal model"),
        new AiModelInfo("gemini-1.5-pro", "Gemini 1.5 Pro", "High intelligence for complex reasoning"),
        new AiModelInfo("gemini-1.5-flash", "Gemini 1.5 Flash", "Lightweight, high-speed model")
    };

    public override IReadOnlyList<AiModelInfo> GetDefaultModels() => DefaultModels;

    protected override bool FilterModel(AiModelInfo model, JsonElement rawElement)
    {
        // Filter for gemini* models and exclude embedding or non-chat models
        if (!model.Id.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
            return false;

        if (model.Id.Contains("embedding", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    protected override IReadOnlyList<AiModelInfo> PostProcessModels(List<AiModelInfo> models)
    {
        // Prioritize gemini-2.0 and gemini-1.5 models at the top
        return models
            .OrderByDescending(m => m.Id.StartsWith("gemini-2.", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.StartsWith("gemini-1.5", StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
