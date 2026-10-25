using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RtlTerminal.Ai;

/// <summary>
/// Gathers terminal environment state and generates specialized system prompts
/// for autonomous AI agent reasoning in RtlTerminal.
/// </summary>
public sealed class AgentContext
{
    public string WorkingDirectory { get; init; } = "";
    public string ShellProfile { get; init; } = "PowerShell 7";
    public string OperatingSystem { get; init; } = "";
    public IReadOnlyList<string> DirectoryContents { get; init; } = Array.Empty<string>();
    public string? GitBranch { get; init; }
    public string? CustomInstructions { get; init; }

    /// <summary>
    /// Gathers live environment state from the current working directory, shell profile,
    /// OS details, top filesystem entries, and git repository status.
    /// </summary>
    public static AgentContext Create(
        string? workingDirectory = null,
        string? shellProfile = null,
        string? customInstructions = null)
    {
        var cwd = string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(workingDirectory);

        var shell = string.IsNullOrWhiteSpace(shellProfile)
            ? DetectDefaultShell()
            : NormalizeShellName(shellProfile);

        var os = DetectOperatingSystem();
        var files = GatherDirectoryContents(cwd);
        var branch = DetectGitBranch(cwd);

        return new AgentContext
        {
            WorkingDirectory = cwd,
            ShellProfile = shell,
            OperatingSystem = os,
            DirectoryContents = files,
            GitBranch = branch,
            CustomInstructions = customInstructions
        };
    }

    /// <summary>
    /// Builds an intelligent system prompt instructing the model to act as an autonomous terminal agent.
    /// Supports native Arabic and English, adapts syntax to the detected shell, and enforces step-by-step
    /// command execution via structured tags.
    /// </summary>
    public string BuildSystemPrompt(string? additionalInstructions = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an autonomous AI terminal agent embedded inside RtlTerminal, a high-performance terminal emulator with native Right-to-Left (Arabic / Hebrew) and Left-to-Right bilingual support.");
        sb.AppendLine();
        sb.AppendLine("### Environment State");
        sb.AppendLine($"- Operating System: {OperatingSystem}");
        sb.AppendLine($"- Current Working Directory (CWD): {WorkingDirectory}");
        sb.AppendLine($"- Active Shell: {ShellProfile}");
        sb.AppendLine($"- Git Branch: {(string.IsNullOrWhiteSpace(GitBranch) ? "Not a git repository" : GitBranch)}");
        sb.AppendLine();
        sb.AppendLine("### Current Directory Contents (Top files & folders in CWD)");
        if (DirectoryContents.Count > 0)
        {
            foreach (var item in DirectoryContents)
            {
                sb.AppendLine($"- {item}");
            }
        }
        else
        {
            sb.AppendLine("(Empty directory or inaccessible)");
        }
        sb.AppendLine();
        sb.AppendLine("### Core Operational Directives");
        sb.AppendLine("1. Bilingual Fluency (Arabic & English):");
        sb.AppendLine("   - You natively understand and communicate in Arabic and English.");
        sb.AppendLine("   - If the user writes or queries in Arabic, provide all explanations, rationales, and summaries in fluent Arabic.");
        sb.AppendLine("   - If the user writes in English, communicate in English.");
        sb.AppendLine("   - Shell commands must always remain standard executable CLI commands regardless of conversation language.");
        sb.AppendLine();
        sb.AppendLine("2. Shell Syntax Specialization:");
        sb.AppendLine($"   - You are running commands inside: {ShellProfile}.");
        sb.AppendLine(GetShellGuidance(ShellProfile));
        sb.AppendLine("   - Wrap paths containing spaces or special characters in quotes.");
        sb.AppendLine();
        sb.AppendLine("3. Autonomous Multi-Step Reasoning & Structured Format:");
        sb.AppendLine("   - When pursuing a goal, break it down into clean, incremental steps.");
        sb.AppendLine("   - In each turn, explain what you are doing in 1-2 brief sentences, then provide EXACTLY ONE shell command to execute next using the structured tag:");
        sb.AppendLine("     COMMAND: <single shell command to execute>");
        sb.AppendLine("   - Provide ONLY ONE command per step so its execution output can be inspected before proceeding.");
        sb.AppendLine("   - When the user's goal has been fully accomplished, or if no command is needed, explain the outcome and conclude with:");
        sb.AppendLine("     DONE: <concise summary of completed goal>");
        sb.AppendLine("   - If you need critical user clarification before proceeding, output:");
        sb.AppendLine("     ASK: <question for the user>");
        sb.AppendLine("   - If a command fails or returns a non-zero exit code, inspect the error output and adapt subsequent commands to resolve the issue.");
        sb.AppendLine("   - Never execute destructive commands (e.g., formatting drives or mass unrecoverable deletions) without explicit user warning.");

        if (!string.IsNullOrWhiteSpace(CustomInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("### Custom Instructions");
            sb.AppendLine(CustomInstructions.Trim());
        }

        if (!string.IsNullOrWhiteSpace(additionalInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("### Additional Instructions");
            sb.AppendLine(additionalInstructions.Trim());
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Detects friendly OS name (e.g., Windows 11 Build 26200 vs Windows 10 vs Linux).
    /// </summary>
    public static string DetectOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var build = Environment.OSVersion.Version.Build;
            return build >= 22000
                ? $"Windows 11 (Build {build})"
                : $"Windows 10 (Build {build})";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return $"Linux ({RuntimeInformation.OSDescription})";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"macOS ({RuntimeInformation.OSDescription})";
        }

        return RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// Detects current git branch by inspecting .git/HEAD up the directory hierarchy,
    /// supporting regular repos, worktrees, and submodules with 0 subprocess overhead.
    /// </summary>
    public static string? DetectGitBranch(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        try
        {
            var dir = new DirectoryInfo(directory);
            while (dir != null)
            {
                var gitPath = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitPath))
                {
                    var headFile = Path.Combine(gitPath, "HEAD");
                    if (File.Exists(headFile))
                    {
                        return ParseGitHead(headFile);
                    }
                }
                else if (File.Exists(gitPath))
                {
                    // Worktree or submodule reference: "gitdir: <path>"
                    var firstLine = File.ReadLines(gitPath).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(firstLine) && firstLine.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                    {
                        var relPath = firstLine.Substring("gitdir:".Length).Trim();
                        var resolvedPath = Path.IsPathRooted(relPath) ? relPath : Path.Combine(dir.FullName, relPath);
                        var headFile = Path.Combine(resolvedPath, "HEAD");
                        if (File.Exists(headFile))
                        {
                            return ParseGitHead(headFile);
                        }
                    }
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // Suppress file access errors
        }

        return null;
    }

    private static string? ParseGitHead(string headFile)
    {
        try
        {
            var content = File.ReadAllText(headFile).Trim();
            const string refPrefix = "ref: refs/heads/";
            if (content.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return content.Substring(refPrefix.Length).Trim();
            }

            // Detached HEAD or commit hash
            return content.Length >= 7 ? content.Substring(0, Math.Min(7, content.Length)) : content;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gathers top files and folders in the target directory.
    /// </summary>
    public static IReadOnlyList<string> GatherDirectoryContents(string directory, int maxEntries = 25)
    {
        var list = new List<string>();
        if (!Directory.Exists(directory))
            return list;

        try
        {
            var di = new DirectoryInfo(directory);
            var entries = di.EnumerateFileSystemInfos()
                .Take(maxEntries)
                .Select(e => e is DirectoryInfo ? $"{e.Name}/" : e.Name);
            list.AddRange(entries);
        }
        catch
        {
            // Suppress I/O or permission errors
        }

        return list;
    }

    /// <summary>
    /// Detects default available shell on the system.
    /// </summary>
    public static string DetectDefaultShell()
    {
        if (ShellProfiles.IsPowerShell7Available)
            return "PowerShell 7";

        return "Windows PowerShell";
    }

    /// <summary>
    /// Normalizes raw profile strings to standard friendly shell names.
    /// </summary>
    public static string NormalizeShellName(string shellProfile)
    {
        var lower = shellProfile.Trim().ToLowerInvariant();
        if (lower.Contains("pwsh") || lower.Contains("powershell 7") || lower.Contains("powershell7"))
            return "PowerShell 7";
        if (lower.Contains("powershell"))
            return "Windows PowerShell";
        if (lower.Contains("cmd") || lower.Contains("command prompt"))
            return "Command Prompt (CMD)";
        if (lower.Contains("bash") || lower.Contains("git bash") || lower.Contains("gitbash"))
            return "Git Bash";
        if (lower.Contains("wsl") || lower.Contains("ubuntu") || lower.Contains("debian"))
            return "WSL";

        return shellProfile.Trim();
    }

    private static string GetShellGuidance(string shell)
    {
        var lower = shell.ToLowerInvariant();
        if (lower.Contains("pwsh") || lower.Contains("powershell"))
        {
            return "   - Format all commands specifically for PowerShell (e.g. Get-ChildItem, Select-String, Test-Path, $env:VAR, chaining with ; or pipe |).";
        }
        if (lower.Contains("cmd") || lower.Contains("command prompt"))
        {
            return "   - Format all commands specifically for Windows CMD (e.g. dir, findstr, type, echo, %VAR%, chaining with & or &&).";
        }
        if (lower.Contains("bash") || lower.Contains("wsl") || lower.Contains("sh"))
        {
            return "   - Format all commands specifically for Linux/POSIX Bash (e.g. ls -la, grep, cat, $VAR, chaining with && or ;).";
        }
        return "   - Format commands using syntax appropriate for this shell environment.";
    }
}
