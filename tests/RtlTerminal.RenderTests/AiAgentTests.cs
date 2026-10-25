using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Win32;
using RtlTerminal.Ai;
using RtlTerminal.Ai.Providers;

internal static class AiAgentTests
{
    public static void Run()
    {
        Console.WriteLine("=== Running AI Agent & Session Brain Unit Tests ===");
        TestAiKeyVaultDpapi();
        TestAgentContext();
        TestAgentSession();
        TestAgentActionParser();
        TestTerminalAgentEngineMultiStepSuccess().GetAwaiter().GetResult();
        TestTerminalAgentEngineApprovalDeclined().GetAwaiter().GetResult();
        TestTerminalAgentEngineMaxStepsLimit().GetAwaiter().GetResult();
        TestAiDispatcherAndWizard().GetAwaiter().GetResult();
        TestOpenAiGeminiJsonResponseParsing();
        Console.WriteLine("=== All AI Agent Tests Passed Successfully! ===");
    }

    private static void TestAiKeyVaultDpapi()
    {
        Console.Write("Testing AiKeyVault DPAPI encryption roundtrip... ");

        const string testSecret = "sk-super-secret-dpapi-key-test-42-xyz!";
        var provider = AiProviderKind.GoogleGemini;

        // 1. Save key
        AiKeyVault.SaveKey(provider, testSecret);

        // 2. Read raw binary from Windows Registry and verify it is encrypted
        using (var regKey = Registry.CurrentUser.OpenSubKey(@"Software\RtlTerminal\Ai\Keys"))
        {
            Assert(regKey != null, "Registry key HKCU\\Software\\RtlTerminal\\Ai\\Keys must exist");
            var rawBytes = regKey!.GetValue(provider.ToString()) as byte[];
            Assert(rawBytes != null && rawBytes.Length > 0, "Stored DPAPI binary must not be empty");

            // Verify raw bytes are NOT the plain UTF-8 bytes (it must be encrypted ciphertext)
            var plainBytes = System.Text.Encoding.UTF8.GetBytes(testSecret);
            Assert(rawBytes != null && !rawBytes.SequenceEqual(plainBytes), "Stored key must be DPAPI ciphertext, not plaintext UTF-8!");
        }

        // 3. Load and decrypt key
        var loaded = AiKeyVault.LoadKey(provider);
        Assert(loaded == testSecret, $"Decrypted key mismatch: expected '{testSecret}', got '{loaded}'");

        // 4. Test HasKey / HasApiKey helper
        Assert(AiKeyVault.HasKey(provider), "HasKey should be true for configured provider");
        Assert(AiKeyVault.HasApiKey(provider), "HasApiKey should be true for configured provider");

        // 5. Test key cleanup (empty or null removes key)
        AiKeyVault.SaveKey(provider, "");
        Assert(AiKeyVault.LoadKey(provider) == null, "Empty key should remove stored credentials");
        Assert(!AiKeyVault.HasKey(provider), "HasKey should be false after removal");

        // 6. Test AiConfig roundtrip
        var config = new AiConfig
        {
            Provider = AiProviderKind.OpenAi,
            SelectedModel = "gpt-4o",
            AutoExecuteCommands = true
        };
        AiKeyVault.SaveConfig(config);
        var loadedConfig = AiKeyVault.LoadConfig();
        Assert(loadedConfig.Provider == AiProviderKind.OpenAi, "Config provider mismatch");
        Assert(loadedConfig.SelectedModel == "gpt-4o", "Config model mismatch");
        Assert(loadedConfig.AutoExecuteCommands == true, "Config AutoExecuteCommands mismatch");

        Console.WriteLine("OK");
    }

    private static void TestAgentContext()
    {
        Console.Write("Testing AgentContext... ");

        var ctx = AgentContext.Create();
        Assert(Directory.Exists(ctx.WorkingDirectory), "WorkingDirectory must exist");
        Assert(!string.IsNullOrWhiteSpace(ctx.OperatingSystem), "OperatingSystem must not be empty");
        Assert(!string.IsNullOrWhiteSpace(ctx.ShellProfile), "ShellProfile must not be empty");
        Assert(ctx.GitBranch == "feature/modern-terminal-experience", $"Expected feature/modern-terminal-experience, got {ctx.GitBranch}");
        Assert(ctx.DirectoryContents.Count > 0, "DirectoryContents should not be empty");

        // Shell normalization
        Assert(AgentContext.NormalizeShellName("pwsh.exe") == "PowerShell 7", "Normalize pwsh.exe");
        Assert(AgentContext.NormalizeShellName("powershell") == "Windows PowerShell", "Normalize powershell");
        Assert(AgentContext.NormalizeShellName("cmd") == "Command Prompt (CMD)", "Normalize cmd");
        Assert(AgentContext.NormalizeShellName("git bash") == "Git Bash", "Normalize git bash");
        Assert(AgentContext.NormalizeShellName("wsl") == "WSL", "Normalize wsl");

        // System prompt generation
        var prompt = ctx.BuildSystemPrompt();
        Assert(prompt.Contains("RtlTerminal"), "Prompt should mention RtlTerminal");
        Assert(prompt.Contains(ctx.WorkingDirectory), "Prompt should contain CWD");
        Assert(prompt.Contains(ctx.ShellProfile), "Prompt should contain ShellProfile");
        Assert(prompt.Contains("Arabic and English"), "Prompt should require bilingual fluency");
        Assert(prompt.Contains("COMMAND:"), "Prompt should define COMMAND: tag");
        Assert(prompt.Contains("DONE:"), "Prompt should define DONE: tag");
        Assert(prompt.Contains("ASK:"), "Prompt should define ASK: tag");

        Console.WriteLine("OK");
    }

    private static void TestAgentSession()
    {
        Console.Write("Testing AgentSession... ");

        var session = new AgentSession();
        Assert(session.MessageCount == 0, "Initial message count should be 0");

        session.AddUserMessage("List all .cs files");
        Assert(session.MessageCount == 1, "Count should be 1 after user message");
        Assert(session.Messages[0].Role == "user", "Role should be user");
        Assert(session.Messages[0].Content == "List all .cs files", "User content match");

        session.AddAssistantMessage("I will run Get-ChildItem.\nCOMMAND: dir *.cs");
        Assert(session.MessageCount == 2, "Count should be 2 after assistant message");
        Assert(session.Messages[1].Role == "assistant", "Role should be assistant");

        session.AddToolOutput("dir *.cs", 0, "Program.cs\nTest.cs", "");
        Assert(session.MessageCount == 3, "Count should be 3 after tool output");
        Assert(session.Messages[2].Role == "user", "Tool output should use user role");
        Assert(session.Messages[2].Content.Contains("Command: dir *.cs"), "Tool output contains command");
        Assert(session.Messages[2].Content.Contains("Exit Code: 0"), "Tool output contains exit code");
        Assert(session.Messages[2].Content.Contains("Program.cs"), "Tool output contains stdout");

        // History with context
        var ctx = AgentContext.Create();
        var fullHistory = session.GetHistoryWithContext(ctx);
        Assert(fullHistory.Count == 4, "fullHistory should contain system prompt + 3 session messages");
        Assert(fullHistory[0].Role == "system", "First message must be system prompt");
        Assert(fullHistory[1].Role == "user", "Second message user");

        // JSON Serialization roundtrip
        var json = session.ToJson();
        var restored = AgentSession.FromJson(json);
        Assert(restored.Id == session.Id, "Restored session ID match");
        Assert(restored.MessageCount == 3, "Restored count match");
        Assert(restored.Messages[0].Content == session.Messages[0].Content, "Restored message content match");

        // Reset
        session.Reset();
        Assert(session.MessageCount == 0, "Session should be empty after Reset()");
        Assert(session.Messages.Count == 0, "Messages list should be empty after Reset()");

        Console.WriteLine("OK");
    }

    private static void TestAgentActionParser()
    {
        Console.Write("Testing AgentActionParser... ");

        // Simple COMMAND
        var act1 = AgentActionParser.Parse("I will inspect the files.\nCOMMAND: git status");
        Assert(act1.Type == AgentActionType.Command, "Expected Command type");
        Assert(act1.Command == "git status", $"Expected 'git status', got '{act1.Command}'");
        Assert(act1.Explanation == "I will inspect the files.", "Explanation match");

        // Bold tag
        var act2 = AgentActionParser.Parse("**COMMAND:** dir /w");
        Assert(act2.Type == AgentActionType.Command, "Expected Command type");
        Assert(act2.Command == "dir /w", $"Expected 'dir /w', got '{act2.Command}'");

        // Code block
        var act3 = AgentActionParser.Parse("COMMAND:\n```powershell\nGet-Process | Select-Object -First 5\n```");
        Assert(act3.Type == AgentActionType.Command, "Expected Command type");
        Assert(act3.Command == "Get-Process | Select-Object -First 5", $"Code block command mismatch: {act3.Command}");

        // Inline backticks
        var act4 = AgentActionParser.Parse("COMMAND: `dotnet test`");
        Assert(act4.Type == AgentActionType.Command, "Expected Command type");
        Assert(act4.Command == "dotnet test", $"Backticks strip mismatch: {act4.Command}");

        // DONE
        var act5 = AgentActionParser.Parse("All tests executed.\nDONE: 12 tests passed, 0 failed.");
        Assert(act5.Type == AgentActionType.Done, "Expected Done type");
        Assert(act5.Summary == "12 tests passed, 0 failed.", $"Summary mismatch: {act5.Summary}");

        // Bold DONE
        var act6 = AgentActionParser.Parse("**DONE:** Build completed with 0 errors.");
        Assert(act6.Type == AgentActionType.Done, "Expected Done type");
        Assert(act6.Summary == "Build completed with 0 errors.", "Summary match");

        // ASK
        var act7 = AgentActionParser.Parse("ASK: Which git remote do you want to push to?");
        Assert(act7.Type == AgentActionType.Clarification, "Expected Clarification type");
        Assert(act7.Summary == "Which git remote do you want to push to?", "Ask question match");

        // Message
        var act8 = AgentActionParser.Parse("PowerShell 7 is the recommended shell for Windows.");
        Assert(act8.Type == AgentActionType.Message, "Expected Message type");
        Assert(act8.Summary == "PowerShell 7 is the recommended shell for Windows.", "Message summary match");

        Console.WriteLine("OK");
    }

    private static async Task TestTerminalAgentEngineMultiStepSuccess()
    {
        Console.Write("Testing TerminalAgentEngine multi-step goal execution... ");

        var responses = new Queue<string>(new[]
        {
            "Checking git status.\nCOMMAND: git status --short",
            "Everything is clean.\nDONE: Repository working tree is clean."
        });

        var mockClient = new MockAiProviderClient(responses);
        var engine = new TerminalAgentEngine(mockClient, apiKey: "test-key", model: "gemini-2.0-flash", autoExecuteCommands: true);
        var session = new AgentSession();
        var context = AgentContext.Create();

        var executedCommands = new List<string>();

        var result = await engine.ExecuteGoalAsync(
            goal: "Check if the git repo is clean",
            session: session,
            context: context,
            requestApprovalAsync: _ => Task.FromResult(true),
            runCommandAsync: cmd =>
            {
                executedCommands.Add(cmd);
                return Task.FromResult((ExitCode: 0, Output: ""));
            },
            onProgressMessage: null);

        Assert(result.Success, "Goal should succeed");
        Assert(result.StepsExecuted == 2, $"Expected 2 steps, got {result.StepsExecuted}");
        Assert(executedCommands.Count == 1, "Should have executed 1 command");
        Assert(executedCommands[0] == "git status --short", "Command mismatch");
        Assert(result.Summary.Contains("clean"), "Summary mismatch");

        Console.WriteLine("OK");
    }

    private static async Task TestTerminalAgentEngineApprovalDeclined()
    {
        Console.Write("Testing TerminalAgentEngine approval decline handling... ");

        var responses = new Queue<string>(new[]
        {
            "Deleting files.\nCOMMAND: rm -rf /",
            "Understood, aborting deletion.\nDONE: Action cancelled by user."
        });

        var mockClient = new MockAiProviderClient(responses);
        var engine = new TerminalAgentEngine(mockClient, apiKey: "test-key", model: "test-model", autoExecuteCommands: false);
        var session = new AgentSession();
        var context = AgentContext.Create();

        var executedCommands = new List<string>();

        var result = await engine.ExecuteGoalAsync(
            goal: "Clean up directory",
            session: session,
            context: context,
            requestApprovalAsync: _ => Task.FromResult(false), // User declines
            runCommandAsync: cmd =>
            {
                executedCommands.Add(cmd);
                return Task.FromResult((ExitCode: 0, Output: ""));
            },
            onProgressMessage: null);

        Assert(result.Success, "Goal should conclude with user cancellation");
        Assert(executedCommands.Count == 0, "No command should have executed");
        Assert(result.StepsExecuted == 2, $"Expected 2 steps, got {result.StepsExecuted}");

        Console.WriteLine("OK");
    }

    private static async Task TestTerminalAgentEngineMaxStepsLimit()
    {
        Console.Write("Testing TerminalAgentEngine max steps limit... ");

        var mockClient = new MockAiProviderClient(new Queue<string>(new[]
        {
            "Step 1\nCOMMAND: echo 1",
            "Step 2\nCOMMAND: echo 2",
            "Step 3\nCOMMAND: echo 3",
            "Step 4\nCOMMAND: echo 4"
        }));

        var engine = new TerminalAgentEngine(mockClient, apiKey: "key", model: "model", autoExecuteCommands: true, maxSteps: 2);
        var session = new AgentSession();
        var context = AgentContext.Create();

        var result = await engine.ExecuteGoalAsync(
            goal: "Run infinite commands",
            session: session,
            context: context,
            requestApprovalAsync: _ => Task.FromResult(true),
            runCommandAsync: _ => Task.FromResult((0, "ok")),
            onProgressMessage: null);

        Assert(!result.Success, "Should fail due to max steps");
        Assert(result.StepsExecuted == 2, $"Expected 2 steps, got {result.StepsExecuted}");
        Assert(result.Summary.Contains("limit"), "Summary should mention step limit");

        Console.WriteLine("OK");
    }

    private static async Task TestAiDispatcherAndWizard()
    {
        Console.Write("Testing AiCommandDispatcher & AiSetupWizard... ");

        // 1. Test IsAiCommand matching
        Assert(AiCommandDispatcher.IsAiCommand("/ai"), "Should match /ai");
        Assert(AiCommandDispatcher.IsAiCommand("/ai list files"), "Should match /ai with args");
        Assert(AiCommandDispatcher.IsAiCommand("/ai-new"), "Should match /ai-new");
        Assert(AiCommandDispatcher.IsAiCommand("/ai-setup"), "Should match /ai-setup");
        Assert(AiCommandDispatcher.IsAiCommand("/ai-model"), "Should match /ai-model");
        Assert(AiCommandDispatcher.IsAiCommand("/ai-auto"), "Should match /ai-auto");
        Assert(AiCommandDispatcher.IsAiCommand("/ai-status"), "Should match /ai-status");
        Assert(AiCommandDispatcher.IsAiCommand("/ai-stop"), "Should match /ai-stop");
        Assert(AiCommandDispatcher.IsAiCommand("/ai-help"), "Should match /ai-help");
        Assert(!AiCommandDispatcher.IsAiCommand("git commit"), "Should not match git commit");
        Assert(!AiCommandDispatcher.IsAiCommand("/air"), "Should not match /air (word boundary)");

        // 2. Test ParseCommand
        var (cmd1, arg1) = AiCommandDispatcher.ParseCommand("/ai list top 5 files");
        Assert(cmd1 == "/ai", "Parsed command /ai");
        Assert(arg1 == "list top 5 files", "Parsed argument");

        var (cmd2, arg2) = AiCommandDispatcher.ParseCommand("/ai-model gemini-2.0-flash");
        Assert(cmd2 == "/ai-model", "Parsed command /ai-model");
        Assert(arg2 == "gemini-2.0-flash", "Parsed model name");

        var (cmd3, arg3) = AiCommandDispatcher.ParseCommand("/ai-new");
        Assert(cmd3 == "/ai-new", "Parsed command /ai-new");
        Assert(arg3 == "", "Parsed empty argument for /ai-new");

        var (cmd4, arg4) = AiCommandDispatcher.ParseCommand("/ai-setup");
        Assert(cmd4 == "/ai-setup", "Parsed command /ai-setup");

        var (cmd5, arg5) = AiCommandDispatcher.ParseCommand("/ai-auto on");
        Assert(cmd5 == "/ai-auto", "Parsed command /ai-auto");
        Assert(arg5 == "on", "Parsed on argument for /ai-auto");

        var (cmd6, arg6) = AiCommandDispatcher.ParseCommand("/ai-status");
        Assert(cmd6 == "/ai-status", "Parsed command /ai-status");

        var (cmd7, arg7) = AiCommandDispatcher.ParseCommand("/ai-stop");
        Assert(cmd7 == "/ai-stop", "Parsed command /ai-stop");

        // 3. Test AiSetupWizard provider metadata
        Assert(AiSetupWizard.SupportedProviders.Count == 5, "Expected 5 supported providers");

        // 4. Test Dispatcher commands execution
        var dispatcher = new AiCommandDispatcher();
        var outputLines = new List<string>();
        Action<string> capture = s => outputLines.Add(s);

        // Test /ai-new
        dispatcher.Session.AddUserMessage("hello");
        Assert(dispatcher.Session.MessageCount == 1, "Should have 1 message");
        await dispatcher.ExecuteAsync("/ai-new", ".", "PowerShell 7", _ => Task.FromResult(""), _ => Task.FromResult((0, "")), capture);
        Assert(dispatcher.Session.MessageCount == 0, "Session should be reset after /ai-new");

        // Test /ai-auto
        await dispatcher.ExecuteAsync("/ai-auto on", ".", "PowerShell 7", _ => Task.FromResult(""), _ => Task.FromResult((0, "")), capture);
        Assert(AiKeyVault.LoadConfig().AutoExecuteCommands, "AutoExecute should be true");
        await dispatcher.ExecuteAsync("/ai-auto off", ".", "PowerShell 7", _ => Task.FromResult(""), _ => Task.FromResult((0, "")), capture);
        Assert(!AiKeyVault.LoadConfig().AutoExecuteCommands, "AutoExecute should be false");

        // Test /ai-model direct setting
        await dispatcher.ExecuteAsync("/ai-model test-model-abc", ".", "PowerShell 7", _ => Task.FromResult(""), _ => Task.FromResult((0, "")), capture);
        Assert(AiKeyVault.LoadConfig().SelectedModel == "test-model-abc", "Model should be updated");

        // Test /ai-status
        outputLines.Clear();
        await dispatcher.ExecuteAsync("/ai-status", ".", "PowerShell 7", _ => Task.FromResult(""), _ => Task.FromResult((0, "")), capture);
        var statusText = string.Concat(outputLines);
        Assert(statusText.Contains("RtlTerminal AI Status"), "Status should contain title");
        Assert(statusText.Contains("test-model-abc"), "Status should contain selected model");

        // Test /ai-stop when not executing
        outputLines.Clear();
        await dispatcher.ExecuteAsync("/ai-stop", ".", "PowerShell 7", _ => Task.FromResult(""), _ => Task.FromResult((0, "")), capture);

        Console.WriteLine("OK");
    }

    private static void TestOpenAiGeminiJsonResponseParsing()
    {
        Console.Write("Testing OpenAI & Gemini JSON response parsing with mock payloads... ");

        var openAiTester = new TestableOpenAiClient();
        var geminiTester = new TestableGeminiClient();

        // 1. Test OpenAI /models response payload parsing
        const string mockOpenAiModelsJson = """
        {
          "object": "list",
          "data": [
            { "id": "gpt-4o", "object": "model", "owned_by": "system" },
            { "id": "o3-mini", "object": "model", "owned_by": "system" },
            { "id": "text-embedding-3-small", "object": "model", "owned_by": "system" }
          ]
        }
        """;
        var openAiModels = openAiTester.ParseModelsFromJson(mockOpenAiModelsJson);
        Assert(openAiModels.Count == 3, $"Expected 3 OpenAI models, got {openAiModels.Count}");
        Assert(openAiModels.Any(m => m.Id == "gpt-4o"), "OpenAI models must contain gpt-4o");
        Assert(openAiModels.Any(m => m.Id == "o3-mini"), "OpenAI models must contain o3-mini");

        // 2. Test Gemini /models response payload parsing with filtering and prioritization
        const string mockGeminiModelsJson = """
        {
          "object": "list",
          "data": [
            { "id": "gemini-1.5-pro", "object": "model" },
            { "id": "gemini-2.0-flash", "object": "model" },
            { "id": "text-embedding-004", "object": "model" },
            { "id": "other-non-gemini-model", "object": "model" }
          ]
        }
        """;
        var geminiModels = geminiTester.ParseModelsFromJson(mockGeminiModelsJson);
        Assert(geminiModels.Count == 2, $"Expected 2 filtered Gemini models, got {geminiModels.Count}");
        Assert(geminiModels[0].Id == "gemini-2.0-flash", $"Gemini 2.0 Flash should be prioritized first, got {geminiModels[0].Id}");
        Assert(geminiModels[1].Id == "gemini-1.5-pro", $"Gemini 1.5 Pro should be second, got {geminiModels[1].Id}");

        // 3. Test SSE streaming chunk delta extraction
        const string mockSseChunkJson = """
        {
          "id": "chatcmpl-test-chunk-1",
          "object": "chat.completion.chunk",
          "created": 1729890000,
          "model": "gpt-4o",
          "choices": [
            {
              "index": 0,
              "delta": {
                "content": "مرحبا بك في RtlTerminal"
              },
              "finish_reason": null
            }
          ]
        }
        """;
        var delta = openAiTester.TestExtractDelta(mockSseChunkJson);
        Assert(delta == "مرحبا بك في RtlTerminal", $"Extracted delta mismatch: '{delta}'");

        // 4. Test API error message extraction
        const string mockErrorJson = """
        {
          "error": {
            "message": "The model 'invalid-model' does not exist or you do not have access to it.",
            "type": "invalid_request_error",
            "param": null,
            "code": "model_not_found"
          }
        }
        """;
        var errorMsg = openAiTester.TestExtractError(mockErrorJson);
        Assert(errorMsg.Contains("invalid-model"), $"Expected extracted error to contain model name, got: '{errorMsg}'");

        Console.WriteLine("OK");
    }

    private static void Assert(bool condition, string message, [CallerLineNumber] int line = 0)
    {
        if (!condition)
            throw new Exception($"Assertion failed at line {line}: {message}");
    }

    private sealed class TestableOpenAiClient : OpenAiCompatibleProviderClient
    {
        public override AiProviderKind Provider => AiProviderKind.OpenAi;
        protected override string BaseUrl => "https://api.openai.com/v1/";

        public string? TestExtractDelta(string json) => ExtractDeltaContent(json);
        public string TestExtractError(string body) => ExtractErrorMessage(body);

        public List<AiModelInfo> ParseModelsFromJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var models = new List<AiModelInfo>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (TryParseModel(item, out var m) && FilterModel(m, item))
                        models.Add(m);
                }
            }
            return PostProcessModels(models).ToList();
        }
    }

    private sealed class TestableGeminiClient : OpenAiCompatibleProviderClient
    {
        public override AiProviderKind Provider => AiProviderKind.GoogleGemini;
        protected override string BaseUrl => "https://generativelanguage.googleapis.com/v1beta/openai/";

        protected override bool FilterModel(AiModelInfo model, JsonElement rawElement)
        {
            if (!model.Id.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                return false;

            if (model.Id.Contains("embedding", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        protected override IReadOnlyList<AiModelInfo> PostProcessModels(List<AiModelInfo> models)
        {
            return models
                .OrderByDescending(m => m.Id.StartsWith("gemini-2.", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(m => m.Id.StartsWith("gemini-1.5", StringComparison.OrdinalIgnoreCase))
                .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<AiModelInfo> ParseModelsFromJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var models = new List<AiModelInfo>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (TryParseModel(item, out var m) && FilterModel(m, item))
                        models.Add(m);
                }
            }
            return PostProcessModels(models).ToList();
        }
    }

    private sealed class MockAiProviderClient : IAiProviderClient
    {
        private readonly Queue<string> _responses;

        public MockAiProviderClient(Queue<string> responses)
        {
            _responses = responses;
        }

        public AiProviderKind Provider => AiProviderKind.GoogleGemini;

        public Task<IReadOnlyList<AiModelInfo>> FetchModelsAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AiModelInfo>>(new[] { new AiModelInfo("test", "Test Model") });
        }

        public async IAsyncEnumerable<string> StreamChatAsync(
            string apiKey,
            string model,
            IReadOnlyList<AiChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_responses.Count > 0)
            {
                yield return _responses.Dequeue();
            }
            else
            {
                yield return "DONE: Finished.";
            }
        }
    }
}
