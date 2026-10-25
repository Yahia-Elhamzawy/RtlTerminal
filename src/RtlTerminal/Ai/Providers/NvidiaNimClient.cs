using System.Text.Json;

namespace RtlTerminal.Ai.Providers;

public sealed class NvidiaNimClient : OpenAiCompatibleProviderClient
{
    public override AiProviderKind Provider => AiProviderKind.NvidiaNim;

    protected override string BaseUrl => "https://integrate.api.nvidia.com/v1/";

    private static readonly IReadOnlyList<AiModelInfo> DefaultModels = new[]
    {
        new AiModelInfo("meta/llama-3.3-70b-instruct", "Meta Llama 3.3 70B Instruct", "High-performance open weights model running on NVIDIA NIM"),
        new AiModelInfo("deepseek-ai/deepseek-r1", "DeepSeek R1", "Advanced open-source reasoning model"),
        new AiModelInfo("nvidia/llama-3.1-nemotron-70b-instruct", "Nvidia Nemotron 70B Instruct", "NVIDIA fine-tuned enterprise model"),
        new AiModelInfo("mistralai/mistral-large-2-instruct", "Mistral Large 2 Instruct", "Mistral's flagship reasoning & coding model")
    };

    public override IReadOnlyList<AiModelInfo> GetDefaultModels() => DefaultModels;

    protected override bool FilterModel(AiModelInfo model, JsonElement rawElement)
    {
        // Exclude embedding, reranking, audio, and safety guard models
        if (model.Id.Contains("embed", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("rerank", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("guard", StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains("whisper", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    protected override IReadOnlyList<AiModelInfo> PostProcessModels(List<AiModelInfo> models)
    {
        return models
            .OrderByDescending(m => m.Id.StartsWith("meta/llama-3.3", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Id.StartsWith("nvidia/llama", StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
