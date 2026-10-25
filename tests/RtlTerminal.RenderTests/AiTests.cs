using System.Text.Json;
using RtlTerminal.Ai;
using RtlTerminal.Ai.Providers;

internal static partial class Program
{
    private static void CheckAi()
    {
        Console.WriteLine("Running CheckAi...");

        // 1. Verify AiKeyVault DPAPI key save and load
        foreach (var kind in Enum.GetValues<AiProviderKind>())
        {
            var testKey = $"test-api-key-for-{kind}-12345!@#";
            AiKeyVault.SaveKey(kind, testKey);
            var loadedKey = AiKeyVault.LoadKey(kind);
            if (loadedKey != testKey)
            {
                throw new Exception($"AiKeyVault failed for {kind}: expected '{testKey}', got '{loadedKey}'");
            }

            // Cleanup
            AiKeyVault.SaveKey(kind, "");
            var emptyLoaded = AiKeyVault.LoadKey(kind);
            if (emptyLoaded != null)
            {
                throw new Exception($"AiKeyVault cleanup failed for {kind}: expected null, got '{emptyLoaded}'");
            }
        }

        // 2. Verify AiKeyVault Config save and load
        var testConfig = new AiConfig
        {
            Provider = AiProviderKind.Groq,
            SelectedModel = "llama-3.3-70b-versatile",
            AutoExecuteCommands = true
        };
        AiKeyVault.SaveConfig(testConfig);
        var loadedConfig = AiKeyVault.LoadConfig();
        if (loadedConfig.Provider != testConfig.Provider ||
            loadedConfig.SelectedModel != testConfig.SelectedModel ||
            loadedConfig.AutoExecuteCommands != testConfig.AutoExecuteCommands)
        {
            throw new Exception($"AiKeyVault config mismatch: expected Provider={testConfig.Provider}, Model={testConfig.SelectedModel}, AutoExec={testConfig.AutoExecuteCommands}; got Provider={loadedConfig.Provider}, Model={loadedConfig.SelectedModel}, AutoExec={loadedConfig.AutoExecuteCommands}");
        }

        // 3. Verify AiProviderFactory for all providers
        foreach (var kind in Enum.GetValues<AiProviderKind>())
        {
            var client = AiProviderFactory.Create(kind);
            if (client == null)
            {
                throw new Exception($"AiProviderFactory returned null for {kind}");
            }
            if (client.Provider != kind)
            {
                throw new Exception($"AiProviderFactory client provider mismatch: expected {kind}, got {client.Provider}");
            }

            if (client is OpenAiCompatibleProviderClient openAiClient)
            {
                var defaultModels = openAiClient.GetDefaultModels();
                if (defaultModels.Count == 0)
                {
                    throw new Exception($"Provider {kind} returned 0 default models");
                }
            }
        }

        // 4. Specific provider type assertions
        if (AiProviderFactory.Create(AiProviderKind.GoogleGemini) is not GoogleGeminiClient)
            throw new Exception("Expected GoogleGeminiClient for GoogleGemini");
        if (AiProviderFactory.Create(AiProviderKind.Groq) is not GroqClient)
            throw new Exception("Expected GroqClient for Groq");
        if (AiProviderFactory.Create(AiProviderKind.OpenRouter) is not OpenRouterClient)
            throw new Exception("Expected OpenRouterClient for OpenRouter");
        if (AiProviderFactory.Create(AiProviderKind.NvidiaNim) is not NvidiaNimClient)
            throw new Exception("Expected NvidiaNimClient for NvidiaNim");
        if (AiProviderFactory.Create(AiProviderKind.OpenAi) is not OpenAiClient)
            throw new Exception("Expected OpenAiClient for OpenAi");

        Console.WriteLine("CheckAi passed successfully!");
        AiAgentTests.Run();
    }
}
