using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RtlTerminal.Ai;

namespace RtlTerminal;

public partial class MainWindow
{
    private readonly AiCommandDispatcher _aiDispatcher = new();
    private readonly Dictionary<TerminalPane, StringBuilder> _paneCommandBuffers = new();
    private TaskCompletionSource<string>? _pendingAiInputTcs;
    private readonly StringBuilder _activeAiInputLine = new();
    private TerminalPane? _activeAiInputPane;

    public bool IsAwaitingAiInput => _pendingAiInputTcs != null;

    /// <summary>
    /// Writes text into the active pane's terminal buffer with SmartRtl and ANSI color rendering.
    /// </summary>
    private void WriteTerminalOutput(TerminalPane? pane, string text)
    {
        if (pane?.Buffer is null || string.IsNullOrEmpty(text)) return;

        // Ensure proper CRLF line endings for ConPTY terminal buffer
        var formatted = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var snapshot = pane.Buffer.Process(formatted);
        QueueRender(pane, snapshot);
    }

    /// <summary>
    /// Prompts the user for interactive text input directly through the active terminal pane.
    /// </summary>
    private async Task<string> PromptAiTerminalInputAsync(TerminalPane? pane, string prompt)
    {
        if (pane == null) return string.Empty;

        WriteTerminalOutput(pane, prompt);
        _activeAiInputPane = pane;
        _activeAiInputLine.Clear();

        var tcs = new TaskCompletionSource<string>();
        _pendingAiInputTcs = tcs;

        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pendingAiInputTcs = null;
            _activeAiInputPane = null;
        }
    }

    /// <summary>
    /// Intercepts character text input for either interactive AI prompts or tracking typed commands.
    /// </summary>
    private bool HandleAiTextInput(TextCompositionEventArgs e)
    {
        var pane = ActivePane;
        if (pane == null || string.IsNullOrEmpty(e.Text)) return false;

        // 1. If an AI prompt or setup wizard is currently awaiting user input
        if (IsAwaitingAiInput && pane == _activeAiInputPane)
        {
            _activeAiInputLine.Append(e.Text);
            WriteTerminalOutput(pane, e.Text);
            return true;
        }

        // 2. Otherwise track keystrokes on the active pane to detect /ai command submission
        if (!_paneCommandBuffers.TryGetValue(pane, out var buffer))
        {
            buffer = new StringBuilder();
            _paneCommandBuffers[pane] = buffer;
        }

        buffer.Append(e.Text);
        return false;
    }

    /// <summary>
    /// Intercepts special keys (Enter, Backspace, Ctrl+C) for AI prompts or command dispatch.
    /// </summary>
    private bool HandleAiKeyDown(KeyEventArgs e)
    {
        var pane = ActivePane;
        if (pane == null) return false;

        var key = GetEffectiveKey(e);
        var controlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // 1. If currently in interactive AI wizard/prompt mode
        if (IsAwaitingAiInput && pane == _activeAiInputPane)
        {
            if (key == Key.Return || key == Key.Enter)
            {
                WriteTerminalOutput(pane, "\r\n");
                var input = _activeAiInputLine.ToString();
                _activeAiInputLine.Clear();
                _pendingAiInputTcs?.TrySetResult(input);
                return true;
            }

            if (key == Key.Back)
            {
                if (_activeAiInputLine.Length > 0)
                {
                    _activeAiInputLine.Length--;
                    WriteTerminalOutput(pane, "\b \b");
                }
                return true;
            }

            if (controlPressed && key == Key.C)
            {
                WriteTerminalOutput(pane, "^C\r\n");
                _pendingAiInputTcs?.TrySetCanceled();
                return true;
            }

            return false;
        }

        // 2. Normal terminal typing: check for Enter to intercept /ai commands
        if (key == Key.Return || key == Key.Enter)
        {
            if (_paneCommandBuffers.TryGetValue(pane, out var buf))
            {
                var commandLine = buf.ToString().Trim();
                buf.Clear();

                if (AiCommandDispatcher.IsAiCommand(commandLine))
                {
                    // Cancel current shell command line via SIGINT
                    pane.Session?.Write("\x03");
                    WriteTerminalOutput(pane, "\r\n");

                    _ = DispatchAiCommandAsync(pane, commandLine);
                    return true;
                }
            }
            return false;
        }

        if (key == Key.Back)
        {
            if (_paneCommandBuffers.TryGetValue(pane, out var buf) && buf.Length > 0)
            {
                buf.Length--;
            }
            return false;
        }

        if (controlPressed && key == Key.C)
        {
            if (_paneCommandBuffers.TryGetValue(pane, out var buf))
            {
                buf.Clear();
            }
            return false;
        }

        return false;
    }

    /// <summary>
    /// Intercepts paste events containing `/ai` commands.
    /// </summary>
    private bool HandleAiPaste(TerminalPane pane, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        if (AiCommandDispatcher.IsAiCommand(trimmed))
        {
            pane.Session?.Write("\x03");
            WriteTerminalOutput(pane, "\r\n");

            _ = DispatchAiCommandAsync(pane, trimmed);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Dispatches an intercepted or submitted `/ai` command to the dispatcher.
    /// </summary>
    private async Task DispatchAiCommandAsync(TerminalPane pane, string commandLine)
    {
        var workingDir = GetPaneWorkingDirectory(pane);
        var shellProfile = pane.Profile.ToString();

        await _aiDispatcher.ExecuteAsync(
            commandLine,
            workingDir,
            shellProfile,
            promptAsync: p => PromptAiTerminalInputAsync(pane, p),
            runCommandAsync: cmd => RunAgentShellCommandAsync(pane, cmd),
            writeOutput: outText => WriteTerminalOutput(pane, outText));
    }

    /// <summary>
    /// Executes a shell command requested by the AI agent, capturing stdout/stderr and exit code.
    /// </summary>
    private async Task<(int ExitCode, string Output)> RunAgentShellCommandAsync(TerminalPane pane, string command)
    {
        var workingDir = GetPaneWorkingDirectory(pane);
        var (fileName, args) = BuildShellCommandArgs(pane.Profile, command);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            return (process.ExitCode, outputBuilder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return (-1, $"Failed to start shell process '{fileName}': {ex.Message}");
        }
    }

    private string GetPaneWorkingDirectory(TerminalPane pane)
    {
        return pane.CurrentDirectory ??
               pane.Session?.ReadWorkingDirectory() ??
               Environment.CurrentDirectory;
    }

    private static (string FileName, string Arguments) BuildShellCommandArgs(TerminalProfile profile, string command)
    {
        var escaped = command.Replace("\"", "\\\"");

        return profile switch
        {
            TerminalProfile.PowerShell7 =>
                ("pwsh.exe", $"-NoProfile -NonInteractive -Command \"{escaped}\""),
            TerminalProfile.PowerShell =>
                ("powershell.exe", $"-NoProfile -NonInteractive -Command \"{escaped}\""),
            TerminalProfile.GitBash =>
                (ShellProfiles.GitBashPath ?? "bash.exe", $"-c \"{escaped}\""),
            TerminalProfile.Wsl =>
                ("wsl.exe", $"-e sh -c \"{escaped}\""),
            _ =>
                ("cmd.exe", $"/c \"{command}\"")
        };
    }

    // --- Floating AI Prompt Bar & UI Integration ---

    private void ToggleAiPromptBar()
    {
        if (AiPromptBar.Visibility == Visibility.Visible)
        {
            CloseAiPromptBar();
        }
        else
        {
            OpenAiPromptBar();
        }
    }

    private void OpenAiPromptBar()
    {
        AiPromptBar.Visibility = Visibility.Visible;
        AiPromptTextBox.Focus();
        AiPromptTextBox.SelectAll();
        AiPlaceholderText.Visibility = string.IsNullOrEmpty(AiPromptTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseAiPromptBar()
    {
        if (AiPromptBar.Visibility != Visibility.Visible) return;
        AiPromptBar.Visibility = Visibility.Collapsed;
        ActiveView.Focus();
    }

    private void AiPromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AiPlaceholderText.Visibility = string.IsNullOrEmpty(AiPromptTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AiPromptTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitAiPrompt();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseAiPromptBar();
            e.Handled = true;
        }
    }

    private void AiSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitAiPrompt();
    }

    private void AiCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAiPromptBar();
    }

    private void SubmitAiPrompt()
    {
        var text = AiPromptTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        CloseAiPromptBar();
        AiPromptTextBox.Clear();

        var pane = ActivePane;
        if (pane == null) return;

        var fullCommand = text.StartsWith("/ai", StringComparison.OrdinalIgnoreCase) ? text : $"/ai {text}";
        _ = DispatchAiCommandAsync(pane, fullCommand);
    }

    // --- Menu Click Handlers ---

    private void AiPromptMenuItem_Click(object sender, RoutedEventArgs e) => OpenAiPromptBar();

    private void AiSetupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePane is { } pane)
        {
            _ = DispatchAiCommandAsync(pane, "/ai-setup");
        }
    }

    private void AiModelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePane is { } pane)
        {
            _ = DispatchAiCommandAsync(pane, "/ai-model");
        }
    }

    private void AiStatusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePane is { } pane)
        {
            _ = DispatchAiCommandAsync(pane, "/ai-status");
        }
    }

    private void AiResetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePane is { } pane)
        {
            _ = DispatchAiCommandAsync(pane, "/ai-new");
        }
    }

    private void AiStopMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePane is { } pane)
        {
            _ = DispatchAiCommandAsync(pane, "/ai-stop");
        }
    }

    private void AiAutoPilotMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePane is { } pane)
        {
            _ = DispatchAiCommandAsync(pane, "/ai-auto");
        }
    }

    private void AiMenu_Opened(object sender, RoutedEventArgs e)
    {
        var config = AiKeyVault.LoadConfig();
        AiAutoPilotMenuItem.IsChecked = config.AutoExecuteCommands;
    }
}
