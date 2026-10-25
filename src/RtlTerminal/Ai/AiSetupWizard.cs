using System.Text;
using RtlTerminal.Ai.Providers;

namespace RtlTerminal.Ai;

/// <summary>
/// Interactive and programmatic Setup Wizard for RtlTerminal AI Assistant.
/// Guides the user through provider selection, DPAPI encrypted API key storage,
/// live model validation, and default model configuration.
/// </summary>
public static class AiSetupWizard
{
    public static readonly IReadOnlyList<(AiProviderKind Kind, string Name, string Description)> SupportedProviders =
    [
        (AiProviderKind.GoogleGemini, "Google Gemini", "Fast, powerful & generous free tier (gemini-2.0-flash)"),
        (AiProviderKind.Groq, "Groq Cloud", "Ultra-low latency inference for Llama 3.3 & open models"),
        (AiProviderKind.OpenRouter, "OpenRouter", "Unified aggregator for Claude, GPT, Mistral & 100+ models"),
        (AiProviderKind.NvidiaNim, "NVIDIA NIM", "Enterprise-grade microservices for Llama, DeepSeek & Nemotron"),
        (AiProviderKind.OpenAi, "OpenAI", "Industry standard GPT-4o, GPT-4o Mini & o3-mini reasoning")
    ];

    /// <summary>
    /// Runs an interactive terminal-based setup flow inside the active terminal pane.
    /// </summary>
    public static async Task<bool> RunTerminalSetupAsync(
        Func<string, Task<string>> promptAsync,
        Action<string> writeOutput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(promptAsync);
        ArgumentNullException.ThrowIfNull(writeOutput);

        writeOutput("\r\n\x1b[1;36m╔══════════════════════════════════════════════════════════════════════════╗\x1b[0m\r\n");
        writeOutput("\x1b[1;36m║\x1b[0m          \x1b[1;37mRtlTerminal AI Assistant Setup  |  إعداد مساعد الذكاء الاصطناعي\x1b[0m         \x1b[1;36m║\x1b[0m\r\n");
        writeOutput("\x1b[1;36m╚══════════════════════════════════════════════════════════════════════════╝\x1b[0m\r\n\r\n");

        writeOutput("\x1b[1;33mSelect AI Provider (اختر مزود الخدمة):\x1b[0m\r\n");
        for (int i = 0; i < SupportedProviders.Count; i++)
        {
            var p = SupportedProviders[i];
            var star = i == 0 ? " \x1b[1;32m★ (Recommended / مستحسن)\x1b[0m" : "";
            writeOutput($"  \x1b[1;36m[{i + 1}]\x1b[0m \x1b[1;37m{p.Name}\x1b[0m{star}\r\n      \x1b[90m{p.Description}\x1b[0m\r\n");
        }
        writeOutput("\r\n");

        // 1. Prompt for provider
        var providerInput = await promptAsync("\x1b[1;33mEnter provider choice [1-5, default: 1]: \x1b[0m");
        if (cancellationToken.IsCancellationRequested) return false;

        var selectedProvider = ParseProviderChoice(providerInput);
        var providerInfo = SupportedProviders.First(p => p.Kind == selectedProvider);
        writeOutput($"\r\n\x1b[32m✔ Selected:\x1b[0m \x1b[1;37m{providerInfo.Name}\x1b[0m\r\n\r\n");

        // 2. Prompt for API key
        writeOutput($"\x1b[1;33mEnter API Key for {providerInfo.Name} (أدخل مفتاح الـ API):\x1b[0m\r\n");
        writeOutput("\x1b[90m(Your key will be securely encrypted with Windows DPAPI under your user account)\x1b[0m\r\n");

        var apiKey = await promptAsync("\x1b[1;36mAPI Key: \x1b[0m");
        if (cancellationToken.IsCancellationRequested) return false;

        apiKey = apiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            writeOutput("\x1b[1;31m✖ Setup cancelled: API key cannot be empty (تم إلغاء الإعداد).\x1b[0m\r\n\r\n");
            return false;
        }

        // 3. Validate Key & Discover Models
        writeOutput("\r\n\x1b[36m├── 🔍 Validating API key & discovering models... (جارٍ التحقق وجلب النماذج)\x1b[0m\r\n");

        IReadOnlyList<AiModelInfo> models;
        try
        {
            var client = AiProviderFactory.Create(selectedProvider);
            models = await client.FetchModelsAsync(apiKey, cancellationToken);
        }
        catch (Exception ex)
        {
            writeOutput($"\x1b[1;31m└── ❌ API validation failed (فشل التحقق من المفتاح): {ex.Message}\x1b[0m\r\n");
            writeOutput("\x1b[33mPlease verify your API key and network connection, then run \x1b[1;37m/ai-setup\x1b[0;33m again.\x1b[0m\r\n\r\n");
            return false;
        }

        if (models == null || models.Count == 0)
        {
            writeOutput("\x1b[1;33m└── ⚠ Key validated, but no models were returned. Using default configuration.\x1b[0m\r\n");
            models = [new AiModelInfo(GetDefaultModelId(selectedProvider), GetDefaultModelId(selectedProvider))];
        }
        else
        {
            writeOutput($"\x1b[1;32m└── ✔ API Key validated successfully! Discovered {models.Count} model(s).\x1b[0m\r\n\r\n");
        }

        // 4. Select Default Model
        var selectedModelId = await PromptModelSelectionAsync(models, promptAsync, writeOutput, cancellationToken);
        if (cancellationToken.IsCancellationRequested) return false;

        // 5. Save encrypted key and config
        AiKeyVault.SaveKey(selectedProvider, apiKey);

        var config = AiKeyVault.LoadConfig() ?? new AiConfig();
        config.Provider = selectedProvider;
        config.SelectedModel = selectedModelId;
        AiKeyVault.SaveConfig(config);

        // 6. Confirmation Banner
        writeOutput("\r\n\x1b[1;32m╔══════════════════════════════════════════════════════════════════════════╗\x1b[0m\r\n");
        writeOutput("\x1b[1;32m║\x1b[0m   \x1b[1;37m✔ AI Assistant Configured Successfully! (تم إعداد المساعد بنجاح)\x1b[0m       \x1b[1;32m║\x1b[0m\r\n");
        writeOutput("\x1b[1;32m╚══════════════════════════════════════════════════════════════════════════╝\x1b[0m\r\n");
        writeOutput($"  \x1b[1;37mProvider:\x1b[0m   {providerInfo.Name}\r\n");
        writeOutput($"  \x1b[1;37mModel:\x1b[0m      {selectedModelId}\r\n");
        writeOutput($"  \x1b[1;37mSecurity:\x1b[0m   Windows DPAPI Encrypted (مشفر بأمان على جهازك)\r\n");
        writeOutput($"  \x1b[1;37mAuto-Pilot:\x1b[0m {(config.AutoExecuteCommands ? "Enabled" : "Disabled (confirms before each command)")}\r\n\r\n");
        writeOutput("\x1b[36mTip: Type \x1b[1;37m/ai <goal>\x1b[0;36m to execute any terminal task!\x1b[0m\r\n");
        writeOutput("\x1b[90mExample: /ai list modified files and explain git status\x1b[0m\r\n\r\n");

        return true;
    }

    /// <summary>
    /// Prompts user to select from available models for the current provider.
    /// </summary>
    public static async Task<bool> RunModelSelectionAsync(
        Func<string, Task<string>> promptAsync,
        Action<string> writeOutput,
        CancellationToken cancellationToken = default)
    {
        var config = AiKeyVault.LoadConfig();
        var apiKey = AiKeyVault.LoadKey(config.Provider);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            writeOutput("\x1b[1;31m✖ No API key found for provider\x1b[0m " + config.Provider + ". Please run \x1b[1;37m/ai-setup\x1b[0m first.\r\n\r\n");
            return false;
        }

        writeOutput($"\r\n\x1b[36m├── 🔍 Fetching available models for {config.Provider}...\x1b[0m\r\n");

        IReadOnlyList<AiModelInfo> models;
        try
        {
            var client = AiProviderFactory.Create(config.Provider);
            models = await client.FetchModelsAsync(apiKey, cancellationToken);
        }
        catch (Exception ex)
        {
            writeOutput($"\x1b[1;31m└── ❌ Failed to fetch models: {ex.Message}\x1b[0m\r\n\r\n");
            return false;
        }

        if (models.Count == 0)
        {
            writeOutput("\x1b[1;33m└── ⚠ No models discovered for this provider.\x1b[0m\r\n\r\n");
            return false;
        }

        writeOutput($"\x1b[1;32m└── ✔ Found {models.Count} model(s). Current model is: \x1b[1;37m{config.SelectedModel}\x1b[0m\r\n\r\n");

        var selectedModelId = await PromptModelSelectionAsync(models, promptAsync, writeOutput, cancellationToken);
        if (cancellationToken.IsCancellationRequested) return false;

        config.SelectedModel = selectedModelId;
        AiKeyVault.SaveConfig(config);

        writeOutput($"\x1b[1;32m✔ Active model updated to:\x1b[0m \x1b[1;37m{selectedModelId}\x1b[0m\r\n\r\n");
        return true;
    }

    private static async Task<string> PromptModelSelectionAsync(
        IReadOnlyList<AiModelInfo> models,
        Func<string, Task<string>> promptAsync,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        const int maxDisplay = 12;
        var displayCount = Math.Min(models.Count, maxDisplay);

        writeOutput("\x1b[1;33mAvailable Models (النماذج المتاحة):\x1b[0m\r\n");
        for (int i = 0; i < displayCount; i++)
        {
            var m = models[i];
            var desc = !string.IsNullOrWhiteSpace(m.Description) ? $" \x1b[90m- {m.Description}\x1b[0m" : "";
            writeOutput($"  \x1b[1;36m[{i + 1}]\x1b[0m \x1b[1;37m{m.DisplayName}\x1b[0m \x1b[36m({m.Id})\x1b[0m{desc}\r\n");
        }

        if (models.Count > maxDisplay)
        {
            writeOutput($"  \x1b[90m... and {models.Count - maxDisplay} more model(s). You can enter any model ID directly.\x1b[0m\r\n");
        }
        writeOutput("\r\n");

        var modelPrompt = $"\x1b[1;33mSelect model [1-{displayCount}, or type model ID, default: 1]: \x1b[0m";
        var input = await promptAsync(modelPrompt);

        if (cancellationToken.IsCancellationRequested) return models[0].Id;

        input = input?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(input))
        {
            return models[0].Id;
        }

        if (int.TryParse(input, out var idx) && idx >= 1 && idx <= models.Count)
        {
            return models[idx - 1].Id;
        }

        // If the user typed an exact or custom model id
        var match = models.FirstOrDefault(m =>
            string.Equals(m.Id, input, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.DisplayName, input, StringComparison.OrdinalIgnoreCase));

        return match.Id ?? input;
    }

    private static AiProviderKind ParseProviderChoice(string? input)
    {
        var trimmed = input?.Trim().ToLowerInvariant() ?? "";
        return trimmed switch
        {
            "2" or "groq" => AiProviderKind.Groq,
            "3" or "openrouter" or "router" => AiProviderKind.OpenRouter,
            "4" or "nvidia" or "nim" => AiProviderKind.NvidiaNim,
            "5" or "openai" or "chatgpt" => AiProviderKind.OpenAi,
            _ => AiProviderKind.GoogleGemini
        };
    }

    private static string GetDefaultModelId(AiProviderKind provider) => provider switch
    {
        AiProviderKind.GoogleGemini => "gemini-2.0-flash",
        AiProviderKind.Groq => "llama-3.3-70b-versatile",
        AiProviderKind.OpenRouter => "google/gemini-2.0-flash-001",
        AiProviderKind.NvidiaNim => "meta/llama-3.3-70b-instruct",
        AiProviderKind.OpenAi => "gpt-4o-mini",
        _ => "gemini-2.0-flash"
    };
}
