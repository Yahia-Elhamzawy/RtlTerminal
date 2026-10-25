using System.Text;

namespace RtlTerminal.Ai;

/// <summary>
/// Result of an autonomous agent goal execution run.
/// Implicitly convertible to string for convenient display or logging.
/// </summary>
public sealed record AgentGoalResult(
    bool Success,
    string Summary,
    int StepsExecuted,
    IReadOnlyList<string> ExecutedCommands)
{
    public override string ToString() => Summary;
    public static implicit operator string(AgentGoalResult result) => result.Summary;
}

/// <summary>
/// Autonomous agent engine for RtlTerminal.
/// Executes high-level user goals through a step-by-step reasoning and terminal command loop.
/// </summary>
public sealed class TerminalAgentEngine
{
    public IAiProviderClient ProviderClient { get; }
    public string ApiKey { get; set; }
    public string Model { get; set; }
    public bool AutoExecuteCommands { get; set; }
    public int MaxSteps { get; set; }

    public TerminalAgentEngine(
        IAiProviderClient providerClient,
        string apiKey,
        string model,
        bool autoExecuteCommands = false,
        int maxSteps = 10)
    {
        ProviderClient = providerClient ?? throw new ArgumentNullException(nameof(providerClient));
        ApiKey = apiKey ?? string.Empty;
        Model = model ?? string.Empty;
        AutoExecuteCommands = autoExecuteCommands;
        MaxSteps = maxSteps > 0 ? maxSteps : 10;
    }

    public TerminalAgentEngine(
        IAiProviderClient providerClient,
        AiConfig config,
        string apiKey,
        int maxSteps = 10)
        : this(
            providerClient,
            apiKey,
            config?.SelectedModel ?? string.Empty,
            config?.AutoExecuteCommands ?? false,
            maxSteps)
    {
    }

    /// <summary>
    /// Executes a multi-step goal autonomously using the provided session, context, and callbacks.
    /// In each step:
    ///   1. Sends prompt/goal + session history with context to the AI provider.
    ///   2. Parses the model response for commands, completion signals, or questions.
    ///   3. If a command is proposed:
    ///      - Prompts for approval via requestApprovalAsync if needed.
    ///      - If declined, records the decline in session so the agent can adapt.
    ///      - If approved or auto-execute is enabled, executes the command via runCommandAsync.
    ///      - Feeds the execution result (exit code + stdout/stderr) back to session history.
    ///   4. Repeats until the agent signals completion (DONE), asks a question (ASK),
    ///      or reaches MaxSteps.
    /// </summary>
    public async Task<AgentGoalResult> ExecuteGoalAsync(
        string goal,
        AgentSession session,
        AgentContext context,
        Func<string, Task<bool>> requestApprovalAsync,
        Func<string, Task<(int ExitCode, string Output)>> runCommandAsync,
        Action<string>? onProgressMessage = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(runCommandAsync);

        if (string.IsNullOrWhiteSpace(goal))
        {
            return new AgentGoalResult(false, "Goal cannot be empty.", 0, Array.Empty<string>());
        }

        // Add goal to session history if not already present as the last message
        var messages = session.Messages;
        if (messages.Count == 0 || messages[^1].Role != "user" || messages[^1].Content != goal.Trim())
        {
            session.AddUserMessage(goal);
        }

        var executedCommands = new List<string>();
        int step = 0;
        string? lastDeclinedCommand = null;

        while (step < MaxSteps && !ct.IsCancellationRequested)
        {
            step++;
            onProgressMessage?.Invoke($"[Step {step}/{MaxSteps}] Analyzing goal and planning next action...");

            var history = session.GetHistoryWithContext(context);
            var responseBuilder = new StringBuilder();

            try
            {
                await foreach (var chunk in ProviderClient.StreamChatAsync(ApiKey, Model, history, ct))
                {
                    responseBuilder.Append(chunk);
                }
            }
            catch (OperationCanceledException)
            {
                onProgressMessage?.Invoke("Execution cancelled by user.");
                return new AgentGoalResult(false, "Execution cancelled by user.", step, executedCommands);
            }
            catch (Exception ex)
            {
                var errorMsg = $"AI provider request failed: {ex.Message}";
                onProgressMessage?.Invoke(errorMsg);
                return new AgentGoalResult(false, errorMsg, step, executedCommands);
            }

            var responseText = responseBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                var errorMsg = "AI provider returned an empty response.";
                onProgressMessage?.Invoke(errorMsg);
                return new AgentGoalResult(false, errorMsg, step, executedCommands);
            }

            session.AddAssistantMessage(responseText);

            var action = AgentActionParser.Parse(responseText);

            if (!string.IsNullOrWhiteSpace(action.Explanation))
            {
                onProgressMessage?.Invoke(action.Explanation);
            }

            switch (action.Type)
            {
                case AgentActionType.Done:
                {
                    var summary = !string.IsNullOrWhiteSpace(action.Summary)
                        ? action.Summary
                        : responseText;
                    onProgressMessage?.Invoke($"Goal completed: {summary}");
                    return new AgentGoalResult(true, summary, step, executedCommands);
                }

                case AgentActionType.Clarification:
                {
                    var question = action.Summary ?? responseText;
                    onProgressMessage?.Invoke($"Clarification requested: {question}");
                    return new AgentGoalResult(true, question, step, executedCommands);
                }

                case AgentActionType.Command:
                {
                    var command = action.Command!;
                    onProgressMessage?.Invoke($"Proposed command: {command}");

                    // Avoid endless repetition if the exact same command was declined
                    if (string.Equals(command, lastDeclinedCommand, StringComparison.Ordinal))
                    {
                        var haltMsg = $"Command '{command}' was proposed again after being declined. Halting execution loop.";
                        onProgressMessage?.Invoke(haltMsg);
                        return new AgentGoalResult(false, haltMsg, step, executedCommands);
                    }

                    bool approved = AutoExecuteCommands;
                    if (!approved)
                    {
                        if (requestApprovalAsync != null)
                        {
                            onProgressMessage?.Invoke($"Awaiting approval for: {command}");
                            approved = await requestApprovalAsync(command);
                        }
                        else
                        {
                            approved = false;
                        }
                    }

                    if (!approved)
                    {
                        lastDeclinedCommand = command;
                        onProgressMessage?.Invoke($"Command execution declined: {command}");
                        session.AddUserMessage($"The user declined to execute the command: `{command}`. Please propose an alternative approach or conclude the task.");
                        continue;
                    }

                    lastDeclinedCommand = null;
                    onProgressMessage?.Invoke($"Executing: {command}");

                    (int ExitCode, string Output) runResult;
                    try
                    {
                        runResult = await runCommandAsync(command);
                    }
                    catch (Exception ex)
                    {
                        runResult = (-1, $"Command execution failed with exception: {ex.Message}");
                    }

                    executedCommands.Add(command);
                    onProgressMessage?.Invoke($"Command exited with code {runResult.ExitCode}.");

                    session.AddToolOutput(
                        command,
                        runResult.ExitCode,
                        stdout: runResult.Output,
                        stderr: runResult.ExitCode != 0 ? runResult.Output : string.Empty);

                    break;
                }

                case AgentActionType.Message:
                default:
                {
                    onProgressMessage?.Invoke(responseText);
                    return new AgentGoalResult(true, responseText, step, executedCommands);
                }
            }
        }

        if (ct.IsCancellationRequested)
        {
            onProgressMessage?.Invoke("Goal execution was cancelled.");
            return new AgentGoalResult(false, "Operation cancelled.", step, executedCommands);
        }

        var maxStepsMsg = $"Reached maximum step limit ({MaxSteps}) before goal completion.";
        onProgressMessage?.Invoke(maxStepsMsg);
        return new AgentGoalResult(false, maxStepsMsg, step, executedCommands);
    }
}
