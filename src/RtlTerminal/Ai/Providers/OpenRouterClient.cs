using System.Net.Http;
using System.Text.Json;

namespace RtlTerminal.Ai.Providers;

public sealed class OpenRouterClient : OpenAiCompatibleProviderClient
{
    public override AiProviderKind Provider => AiProviderKind.OpenRouter;

    protected override string BaseUrl => "https://openrouter.ai/api/v1/";

    private static readonly IReadOnlyList<AiModelInfo> DefaultModels = new[]
    {
        new AiModelInfo("anthropic/claude-3.5-sonnet", "Anthropic: Claude 3.5 Sonnet", "Most intelligent Claude model"),
        new AiModelInfo("meta-llama/llama-3.3-70b-instruct", "Meta: Llama 3.3 70B Instruct", "Open weights leader for chat & coding"),
        new AiModelInfo("google/gemini-2.0-flash-exp:free", "Google: Gemini 2.0 Flash (Free)", "High speed experimental Gemini model"),
        new AiModelInfo("deepseek/deepseek-r1", "DeepSeek: R1", "Open reasoning model"),
        new AiModelInfo("openai/gpt-4o", "OpenAI: GPT-4o", "OpenAI flagship multimodal model")
    };

    public override IReadOnlyList<AiModelInfo> GetDefaultModels() => DefaultModels;

    protected override void AddCustomHeaders(HttpRequestMessage request, string apiKey)
    {
        base.AddCustomHeaders(request, apiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/behnamapps/RtlTerminal");
        request.Headers.TryAddWithoutValidation("X-Title", "RtlTerminal");
    }

    protected override bool FilterModel(AiModelInfo model, JsonElement rawElement)
    {
        // Exclude embedding, image generation, and audio models
        if (model.Id.Contains(":embed", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("/embed", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("moderation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    protected override IReadOnlyList<AiModelInfo> PostProcessModels(List<AiModelInfo> models)
    {
        return models
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
