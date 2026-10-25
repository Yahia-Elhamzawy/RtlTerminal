namespace RtlTerminal.Ai;

public enum AiProviderKind
{
    GoogleGemini,
    Groq,
    OpenRouter,
    NvidiaNim,
    OpenAi
}

public readonly record struct AiModelInfo(
    string Id,
    string DisplayName,
    string? Description = null);

public readonly record struct AiChatMessage(
    string Role,
    string Content);

public sealed class AiConfig
{
    public AiProviderKind Provider { get; set; } = AiProviderKind.GoogleGemini;
    public string SelectedModel { get; set; } = "";
    public bool AutoExecuteCommands { get; set; } = false;
}

public interface IAiProviderClient
{
    AiProviderKind Provider { get; }
    Task<IReadOnlyList<AiModelInfo>> FetchModelsAsync(string apiKey, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamChatAsync(
        string apiKey,
        string model,
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken = default);
}
