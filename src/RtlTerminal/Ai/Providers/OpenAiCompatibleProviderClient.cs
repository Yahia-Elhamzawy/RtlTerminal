using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RtlTerminal.Ai.Providers;

public abstract class OpenAiCompatibleProviderClient : IAiProviderClient
{
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public abstract AiProviderKind Provider { get; }

    /// <summary>
    /// Base URL for the OpenAI-compatible API endpoint (e.g. "https://api.openai.com/v1/").
    /// </summary>
    protected abstract string BaseUrl { get; }

    /// <summary>
    /// Gets default fallback models for this provider if live fetching is unavailable.
    /// </summary>
    public virtual IReadOnlyList<AiModelInfo> GetDefaultModels() => Array.Empty<AiModelInfo>();

    /// <summary>
    /// Builds an endpoint URI relative to the base URL.
    /// </summary>
    protected virtual Uri GetEndpointUri(string relativePath)
    {
        var normalizedBase = BaseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBase), relativePath.TrimStart('/'));
    }

    /// <summary>
    /// Adds authorization and custom headers to the HTTP request.
    /// </summary>
    protected virtual void AddCustomHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
    }

    /// <summary>
    /// Fetches the list of models from the provider's /models endpoint.
    /// </summary>
    public virtual async Task<IReadOnlyList<AiModelInfo>> FetchModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        }

        var uri = GetEndpointUri("models");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddCustomHeaders(request, apiKey);

        using var response = await SharedHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Failed to fetch models from {Provider} ({(int)response.StatusCode} {response.ReasonPhrase}): {ExtractErrorMessage(errorContent)}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var models = new List<AiModelInfo>();

        if (document.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataElement.EnumerateArray())
            {
                if (TryParseModel(item, out var modelInfo) && FilterModel(modelInfo, item))
                {
                    models.Add(modelInfo);
                }
            }
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (TryParseModel(item, out var modelInfo) && FilterModel(modelInfo, item))
                {
                    models.Add(modelInfo);
                }
            }
        }

        if (models.Count == 0)
        {
            var defaults = GetDefaultModels();
            if (defaults.Count > 0)
            {
                return defaults;
            }
        }

        return PostProcessModels(models);
    }

    /// <summary>
    /// Streams chat completions using server-sent events (SSE) from the /chat/completions endpoint.
    /// </summary>
    public virtual async IAsyncEnumerable<string> StreamChatAsync(
        string apiKey,
        string model,
        IReadOnlyList<AiChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be specified.", nameof(model));
        ArgumentNullException.ThrowIfNull(messages);

        var uri = GetEndpointUri("chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        AddCustomHeaders(request, apiKey);

        var payload = CreateChatPayload(model, messages);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        request.Content = new ByteArrayContent(jsonBytes)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
        };

        using var response = await SharedHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Chat request failed on {Provider} ({(int)response.StatusCode} {response.ReasonPhrase}): {ExtractErrorMessage(errorContent)}",
                null,
                response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
                break;

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(':'))
                continue;

            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var data = trimmed[5..].TrimStart();
                if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                    yield break;

                if (data.Length == 0)
                    continue;

                var content = ExtractDeltaContent(data);
                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                }
            }
        }
    }

    /// <summary>
    /// Creates the chat completion request payload.
    /// </summary>
    protected virtual object CreateChatPayload(string model, IReadOnlyList<AiChatMessage> messages)
    {
        return new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            stream = true
        };
    }

    /// <summary>
    /// Parses a model info record from a JSON element.
    /// </summary>
    protected virtual bool TryParseModel(JsonElement element, out AiModelInfo modelInfo)
    {
        modelInfo = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (!element.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            return false;

        var id = idProp.GetString();
        if (string.IsNullOrWhiteSpace(id))
            return false;

        string displayName = id;
        if (element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
        {
            var name = nameProp.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                displayName = name;
        }

        string? description = null;
        if (element.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
        {
            description = descProp.GetString();
        }

        modelInfo = new AiModelInfo(id, displayName, description);
        return true;
    }

    /// <summary>
    /// Determines whether a model should be included in the models list.
    /// </summary>
    protected virtual bool FilterModel(AiModelInfo model, JsonElement rawElement) => true;

    /// <summary>
    /// Sorts and post-processes the filtered list of models.
    /// </summary>
    protected virtual IReadOnlyList<AiModelInfo> PostProcessModels(List<AiModelInfo> models)
    {
        return models
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extracts delta content text from a server-sent events JSON payload.
    /// </summary>
    protected virtual string? ExtractDeltaContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("delta", out var delta) &&
                    delta.ValueKind == JsonValueKind.Object)
                {
                    if (delta.TryGetProperty("content", out var contentProp) &&
                        contentProp.ValueKind == JsonValueKind.String)
                    {
                        return contentProp.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Skip invalid JSON chunks gracefully
        }

        return null;
    }

    /// <summary>
    /// Extracts a user-friendly error message from an API error response body.
    /// </summary>
    protected static string ExtractErrorMessage(string errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
            return "Unknown error";

        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                if (errorProp.ValueKind == JsonValueKind.Object &&
                    errorProp.TryGetProperty("message", out var msgProp) &&
                    msgProp.ValueKind == JsonValueKind.String)
                {
                    return msgProp.GetString() ?? errorBody;
                }
                if (errorProp.ValueKind == JsonValueKind.String)
                {
                    return errorProp.GetString() ?? errorBody;
                }
            }
            if (doc.RootElement.TryGetProperty("message", out var messageProp) &&
                messageProp.ValueKind == JsonValueKind.String)
            {
                return messageProp.GetString() ?? errorBody;
            }
        }
        catch
        {
            // Not a JSON error body
        }

        return errorBody.Length > 300 ? errorBody[..300] + "..." : errorBody;
    }
}
