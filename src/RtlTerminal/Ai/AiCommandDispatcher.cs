using System.Text;
using RtlTerminal.Ai.Providers;

namespace RtlTerminal.Ai;

/// <summary>
/// Dispatches and processes all terminal-intercepted `/ai*` commands.
/// Handles session persistence, interactive setup wizard handoff,
/// and autonomous agent execution loops with rich ANSI/RTL tree formatting.
/// </summary>
public sealed class AiCommandDispatcher
{
    private readonly AgentSession _session = new();
    private CancellationTokenSource? _activeCts;
    private readonly object _stateLock = new();

    public AgentSession Session => _session;
    public bool IsExecuting
    {
        get
        {
            lock (_stateLock)
            {
                return _activeCts is { IsCancellationRequested: false };
            }
        }
    }

    /// <summary>
    /// Checks whether an input line starts with an `/ai` CLI command prefix.
    /// </summary>
    public static bool IsAiCommand(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.TrimStart();
        if (!trimmed.StartsWith("/ai", StringComparison.OrdinalIgnoreCase))
            return false;

        // Ensure word boundary or hyphen: /ai, /ai-..., /ai <prompt>
        if (trimmed.Length == 3) return true;
        char next = trimmed[3];
        return char.IsWhiteSpace(next) || next == '-' || next == '_' || next == ':';
    }

    /// <summary>
    /// Splits an input string into the root command and its argument.
    /// e.g. "/ai list files" -> ("/ai", "list files")
    /// e.g. "/ai-model gemini-2.0-flash" -> ("/ai-model", "gemini-2.0-flash")
    /// </summary>
    public static (string Command, string Argument) ParseCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return ("", "");
        var trimmed = input.Trim();
        int spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return (trimmed.ToLowerInvariant(), "");
        }

        var cmd = trimmed.Substring(0, spaceIndex).ToLowerInvariant();
        var arg = trimmed.Substring(spaceIndex + 1).Trim();
        return (cmd, arg);
    }

    /// <summary>
    /// Main entry point to dispatch any `/ai` command intercepted from the terminal or AI bar.
    /// </summary>
    public async Task ExecuteAsync(
        string input,
        string workingDirectory,
        string shellProfile,
        Func<string, Task<string>> promptAsync,
        Func<string, Task<(int ExitCode, string Output)>> runCommandAsync,
        Action<string> writeOutput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(promptAsync);
        ArgumentNullException.ThrowIfNull(runCommandAsync);
        ArgumentNullException.ThrowIfNull(writeOutput);

        var (command, argument) = ParseCommand(input);

        switch (command)
        {
            case "/ai-new":
            case "/ai_new":
            case "/ai-reset":
                HandleAiNew(writeOutput);
                break;

            case "/ai-setup":
            case "/ai_setup":
            case "/ai-config":
                await AiSetupWizard.RunTerminalSetupAsync(promptAsync, writeOutput, cancellationToken);
                break;

            case "/ai-model":
            case "/ai_model":
            case "/ai-models":
                await HandleAiModelAsync(argument, promptAsync, writeOutput, cancellationToken);
                break;

            case "/ai-auto":
            case "/ai_auto":
                HandleAiAuto(argument, writeOutput);
                break;

            case "/ai-status":
            case "/ai_status":
            case "/ai-info":
                HandleAiStatus(workingDirectory, shellProfile, writeOutput);
                break;

            case "/ai-stop":
            case "/ai_stop":
            case "/ai-abort":
                HandleAiStop(writeOutput);
                break;

            case "/ai-help":
            case "/ai_help":
            case "/ai-?":
            case "/ai?":
                DisplayAiHelp(writeOutput);
                break;

            case "/ai":
            default:
                await ExecuteAiGoalAsync(
                    argument,
                    workingDirectory,
                    shellProfile,
                    promptAsync,
                    runCommandAsync,
                    writeOutput,
                    cancellationToken);
                break;
        }
    }

    private void HandleAiNew(Action<string> writeOutput)
    {
        _session.Reset();

        writeOutput("\r\n\x1b[1;32m╔══════════════════════════════════════════════════════════════════════════╗\x1b[0m\r\n");
        writeOutput("\x1b[1;32m║\x1b[0m       \x1b[1;37m✔ AI Session Reset  |  تم تصفير ذاكرة وسياق الجلسة بنجاح\x1b[0m          \x1b[1;32m║\x1b[0m\r\n");
        writeOutput("\x1b[1;32m╚══════════════════════════════════════════════════════════════════════════╝\x1b[0m\r\n");
        writeOutput("\x1b[90mActive conversation history cleared. Next prompt will start with fresh context.\x1b[0m\r\n\r\n");
    }

    private void HandleAiStatus(string workingDirectory, string shellProfile, Action<string> writeOutput)
    {
        var config = AiKeyVault.LoadConfig();
        var hasKey = AiKeyVault.HasKey(config.Provider);

        writeOutput("\r\n\x1b[1;36m╔══════════════════════════════════════════════════════════════════════════╗\x1b[0m\r\n");
        writeOutput("\x1b[1;36m║\x1b[0m                   \x1b[1;37mRtlTerminal AI Status  |  حالة المساعد الذكي\x1b[0m                 \x1b[1;36m║\x1b[0m\r\n");
        writeOutput("\x1b[1;36m╚══════════════════════════════════════════════════════════════════════════╝\x1b[0m\r\n");
        writeOutput($"  \x1b[1;37mProvider:\x1b[0m        \x1b[1;33m{config.Provider}\x1b[0m {(hasKey ? "\x1b[32m✔ (API Key configured / المفتاح مفعل)\x1b[0m" : "\x1b[31m✖ (Missing key - run /ai-setup)\x1b[0m")}\r\n");
        writeOutput($"  \x1b[1;37mSelected Model:\x1b[0m  \x1b[1;36m{(string.IsNullOrWhiteSpace(config.SelectedModel) ? "(default)" : config.SelectedModel)}\x1b[0m\r\n");
        writeOutput($"  \x1b[1;37mAuto-Pilot:\x1b[0m      {(config.AutoExecuteCommands ? "\x1b[1;32mENABLED (الأوتوبايلوت مفعل - تنفيذ الأوامر تلقائياً)\x1b[0m" : "\x1b[1;33mDISABLED (الأوتوبايلوت معطل - تأكيد يدوي قبل التنفيذ)\x1b[0m")}\r\n");
        writeOutput($"  \x1b[1;37mSession Memory:\x1b[0m  {_session.MessageCount} message(s) in context\r\n");
        writeOutput($"  \x1b[1;37mWorking Dir:\x1b[0m     {workingDirectory}\r\n");
        writeOutput($"  \x1b[1;37mActive Shell:\x1b[0m    {shellProfile}\r\n");
        writeOutput($"  \x1b[1;37mAgent State:\x1b[0m     {(IsExecuting ? "\x1b[1;33mRUNNING (قيد التنفيذ)\x1b[0m" : "\x1b[32mIDLE (جاهز)\x1b[0m")}\r\n\r\n");
    }

    private void HandleAiAuto(string argument, Action<string> writeOutput)
    {
        var config = AiKeyVault.LoadConfig();

        if (string.Equals(argument, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "true", StringComparison.OrdinalIgnoreCase))
        {
            config.AutoExecuteCommands = true;
        }
        else if (string.Equals(argument, "off", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(argument, "0", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(argument, "false", StringComparison.OrdinalIgnoreCase))
        {
            config.AutoExecuteCommands = false;
        }
        else
        {
            config.AutoExecuteCommands = !config.AutoExecuteCommands;
        }

        AiKeyVault.SaveConfig(config);

        writeOutput("\r\n\x1b[1;33m⚙ Auto-pilot mode is now:\x1b[0m " +
                    (config.AutoExecuteCommands
                        ? "\x1b[1;32mENABLED (الأوتوبايلوت مفعل - سيتم تنفيذ الأوامر مباشرة دون توقف)\x1b[0m"
                        : "\x1b[1;33mDISABLED (الأوتوبايلوت معطل - سيطلب المساعد تأكيدك قبل تنفيذ أي أمر)\x1b[0m") +
                    "\r\n\r\n");
    }

    private void HandleAiStop(Action<string> writeOutput)
    {
        lock (_stateLock)
        {
            if (_activeCts is { IsCancellationRequested: false })
            {
                _activeCts.Cancel();
                writeOutput("\r\n\x1b[1;31m⏹ Stopping AI Agent execution loop... (تم إرسال إشارة الإيقاف)\x1b[0m\r\n\r\n");
                return;
            }
        }

        writeOutput("\r\n\x1b[90mNo active AI Agent loop is currently running.\x1b[0m\r\n\r\n");
    }

    private async Task HandleAiModelAsync(
        string argument,
        Func<string, Task<string>> promptAsync,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        var config = AiKeyVault.LoadConfig();

        if (!string.IsNullOrWhiteSpace(argument))
        {
            config.SelectedModel = argument.Trim();
            AiKeyVault.SaveConfig(config);
            writeOutput($"\r\n\x1b[1;32m✔ Selected model set to:\x1b[0m \x1b[1;37m{config.SelectedModel}\x1b[0m\r\n\r\n");
            return;
        }

        await AiSetupWizard.RunModelSelectionAsync(promptAsync, writeOutput, cancellationToken);
    }

    private async Task ExecuteAiGoalAsync(
        string goal,
        string workingDirectory,
        string shellProfile,
        Func<string, Task<string>> promptAsync,
        Func<string, Task<(int ExitCode, string Output)>> runCommandAsync,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        var config = AiKeyVault.LoadConfig();
        var apiKey = AiKeyVault.LoadKey(config.Provider);

        // 1. If key is missing, trigger setup wizard first
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            writeOutput("\r\n\x1b[1;33m⚠ No API key configured for provider\x1b[0m \x1b[1;37m" + config.Provider + "\x1b[0m.\r\n");
            writeOutput("\x1b[36mStarting interactive Setup Wizard first (بدء معالج الإعداد أولاً)...\x1b[0m\r\n");

            var setupOk = await AiSetupWizard.RunTerminalSetupAsync(promptAsync, writeOutput, cancellationToken);
            if (!setupOk)
            {
                writeOutput("\x1b[90mAI goal aborted: configuration not completed.\x1b[0m\r\n\r\n");
                return;
            }

            config = AiKeyVault.LoadConfig();
            apiKey = AiKeyVault.LoadKey(config.Provider);
            if (string.IsNullOrWhiteSpace(apiKey)) return;
        }

        // 2. If prompt was empty (`/ai` with no arguments), ask user for goal
        if (string.IsNullOrWhiteSpace(goal))
        {
            writeOutput("\r\n\x1b[1;36m╭─── 🤖 RtlTerminal AI Assistant\x1b[0m\r\n");
            goal = await promptAsync("\x1b[1;33m├── 🎯 What is your goal? (ما الذي تود إنجازه؟): \x1b[0m");
            goal = goal?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(goal))
            {
                writeOutput("\x1b[1;31m╰─── ✖ Goal cancelled (no input provided).\x1b[0m\r\n\r\n");
                return;
            }
        }

        // 3. Setup execution cancellation
        CancellationTokenSource cts;
        lock (_stateLock)
        {
            if (_activeCts is { IsCancellationRequested: false })
            {
                writeOutput("\x1b[1;33m⚠ Another AI task is already running. Run \x1b[1;37m/ai-stop\x1b[0;33m first.\x1b[0m\r\n\r\n");
                return;
            }
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts = _activeCts;
        }

        try
        {
            var providerClient = AiProviderFactory.Create(config.Provider);
            var context = AgentContext.Create(workingDirectory, shellProfile);

            var engine = new TerminalAgentEngine(
                providerClient,
                config,
                apiKey);

            writeOutput($"\r\n\x1b[1;36m╭─── 🤖 AI Agent Started: \x1b[1;37m{goal}\x1b[0m\r\n");
            writeOutput($"\x1b[90m├── 🌐 Provider: {config.Provider} | Model: {config.SelectedModel} | Shell: {shellProfile}\x1b[0m\r\n");

            // Callback to request approval for commands if auto-pilot is off
            async Task<bool> RequestApprovalAsync(string cmd)
            {
                writeOutput($"\x1b[1;33m├── ⚡ Proposed Command: \x1b[1;37;40m {cmd} \x1b[0m\r\n");
                var reply = await promptAsync("\x1b[1;33m├── ❓ Approve command execution? [Y/n] (تأكيد التنفيذ؟): \x1b[0m");
                var trimmed = reply?.Trim().ToLowerInvariant() ?? "";
                return trimmed == "" || trimmed == "y" || trimmed == "yes" || trimmed == "نعم";
            }

            // Callback to execute command via terminal runner
            async Task<(int ExitCode, string Output)> ExecuteCommandCallbackAsync(string cmd)
            {
                writeOutput($"\x1b[36m├── 🚀 Running:\x1b[0m \x1b[90m{cmd}\x1b[0m\r\n");
                var res = await runCommandAsync(cmd);
                var statusColor = res.ExitCode == 0 ? "\x1b[32m✔" : "\x1b[31m✖";
                writeOutput($"\x1b[36m├── 📥 Output {statusColor} (exit {res.ExitCode}):\x1b[0m\r\n");

                if (!string.IsNullOrWhiteSpace(res.Output))
                {
                    var lines = res.Output.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
                    var preview = lines.Take(12);
                    foreach (var l in preview)
                    {
                        writeOutput($"\x1b[90m│   {l}\x1b[0m\r\n");
                    }
                    if (lines.Length > 12)
                    {
                        writeOutput($"\x1b[90m│   ... and {lines.Length - 12} more lines.\x1b[0m\r\n");
                    }
                }
                return res;
            }

            // Progress formatter with tree branches
            void OnProgress(string message)
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                if (message.StartsWith("[Step ", StringComparison.OrdinalIgnoreCase))
                {
                    writeOutput($"\x1b[1;36m├── 🔍 {message}\x1b[0m\r\n");
                }
                else if (message.StartsWith("Goal completed:", StringComparison.OrdinalIgnoreCase))
                {
                    // Will be output at final summary
                }
                else if (!message.StartsWith("Executing:", StringComparison.OrdinalIgnoreCase) &&
                         !message.StartsWith("Proposed command:", StringComparison.OrdinalIgnoreCase))
                {
                    writeOutput($"\x1b[37m├── 💡 {message}\x1b[0m\r\n");
                }
            }

            var result = await engine.ExecuteGoalAsync(
                goal,
                _session,
                context,
                RequestApprovalAsync,
                ExecuteCommandCallbackAsync,
                OnProgress,
                cts.Token);

            if (result.Success)
            {
                writeOutput($"\x1b[1;32m╰─── ✔ {result.Summary}\x1b[0m\r\n\r\n");
            }
            else
            {
                writeOutput($"\x1b[1;31m╰─── ❌ {result.Summary}\x1b[0m\r\n\r\n");
            }
        }
        catch (OperationCanceledException)
        {
            writeOutput("\x1b[1;31m╰─── ⏹ AI Goal aborted by user.\x1b[0m\r\n\r\n");
        }
        catch (Exception ex)
        {
            writeOutput($"\x1b[1;31m╰─── ❌ AI Execution Error: {ex.Message}\x1b[0m\r\n\r\n");
        }
        finally
        {
            lock (_stateLock)
            {
                _activeCts?.Dispose();
                _activeCts = null;
            }
        }
    }

    private static void DisplayAiHelp(Action<string> writeOutput)
    {
        writeOutput("\r\n\x1b[1;36m╔══════════════════════════════════════════════════════════════════════════╗\x1b[0m\r\n");
        writeOutput("\x1b[1;36m║\x1b[0m              \x1b[1;37mRtlTerminal AI Commands  |  أوامر المساعد الذكي\x1b[0m             \x1b[1;36m║\x1b[0m\r\n");
        writeOutput("\x1b[1;36m╚══════════════════════════════════════════════════════════════════════════╝\x1b[0m\r\n");
        writeOutput("  \x1b[1;32m/ai <goal>\x1b[0m        Execute an autonomous task (e.g. \x1b[90m/ai list top 5 files\x1b[0m)\r\n");
        writeOutput("  \x1b[1;32m/ai\x1b[0m               Interactive AI prompt mode\r\n");
        writeOutput("  \x1b[1;32m/ai-setup\x1b[0m         Launch setup wizard for provider, API key, & model\r\n");
        writeOutput("  \x1b[1;32m/ai-status\x1b[0m        Display current provider, model, autopilot & session info\r\n");
        writeOutput("  \x1b[1;32m/ai-new\x1b[0m           Clear session history memory for a fresh conversation\r\n");
        writeOutput("  \x1b[1;32m/ai-auto [on|off]\x1b[0m Toggle autopilot mode (auto-execute commands without prompt)\r\n");
        writeOutput("  \x1b[1;32m/ai-model [name]\x1b[0m  Switch or pick from available models\r\n");
        writeOutput("  \x1b[1;32m/ai-stop\x1b[0m          Abort any actively running AI agent loop\r\n\r\n");
    }
}
