using System.Text.RegularExpressions;

namespace RtlTerminal.Ai;

public enum AgentActionType
{
    Command,
    Done,
    Clarification,
    Message
}

public sealed record AgentAction(
    AgentActionType Type,
    string? Command = null,
    string? Summary = null,
    string? Explanation = null);

/// <summary>
/// Parses raw LLM assistant responses into structured agent actions (Command, Done, Clarification, Message).
/// Handles variations in markdown tags, code blocks, backticks, and bilingual content.
/// </summary>
public static class AgentActionParser
{
    private static readonly Regex CommandRegex = new(
        @"(?:^|\n)\s*(?:\*\*)?COMMAND(?::\*\*|\*\*:|:\s*\*\*|:)\s*([^\r\n]*(?:\r?\n(?!DONE:|COMMAND:|ASK:)[^\r\n]*)*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DoneRegex = new(
        @"(?:^|\n)\s*(?:\*\*)?DONE(?::\*\*|\*\*:|:\s*\*\*|:)\s*([\s\S]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AskRegex = new(
        @"(?:^|\n)\s*(?:\*\*)?ASK(?::\*\*|\*\*:|:\s*\*\*|:)\s*([\s\S]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static AgentAction Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new AgentAction(AgentActionType.Message, Summary: string.Empty);

        var cleanText = text.Trim();

        // 1. Check for COMMAND: tag
        var commandMatch = CommandRegex.Match(cleanText);
        if (commandMatch.Success)
        {
            var rawCommandPart = commandMatch.Groups[1].Value.Trim();
            var explanation = cleanText.Substring(0, commandMatch.Index).Trim();
            var command = ExtractCommand(rawCommandPart);

            if (!string.IsNullOrWhiteSpace(command))
            {
                return new AgentAction(
                    AgentActionType.Command,
                    Command: command,
                    Explanation: string.IsNullOrWhiteSpace(explanation) ? null : explanation);
            }
        }

        // 2. Check for DONE: tag
        var doneMatch = DoneRegex.Match(cleanText);
        if (doneMatch.Success)
        {
            var summary = doneMatch.Groups[1].Value.Trim().Trim('*').Trim();
            var explanation = cleanText.Substring(0, doneMatch.Index).Trim();

            return new AgentAction(
                AgentActionType.Done,
                Summary: summary,
                Explanation: string.IsNullOrWhiteSpace(explanation) ? null : explanation);
        }

        // 3. Check for ASK: tag
        var askMatch = AskRegex.Match(cleanText);
        if (askMatch.Success)
        {
            var question = askMatch.Groups[1].Value.Trim().Trim('*').Trim();
            var explanation = cleanText.Substring(0, askMatch.Index).Trim();

            return new AgentAction(
                AgentActionType.Clarification,
                Summary: question,
                Explanation: string.IsNullOrWhiteSpace(explanation) ? null : explanation);
        }

        // 4. Default: Treat as conversational response / direct message
        return new AgentAction(AgentActionType.Message, Summary: cleanText);
    }

    private static string ExtractCommand(string raw)
    {
        var text = raw.Trim();

        // Check for fenced code block: ```[language] ... ```
        if (text.StartsWith("```"))
        {
            var endFence = text.IndexOf("```", 3, StringComparison.Ordinal);
            var content = endFence > 0
                ? text.Substring(3, endFence - 3).Trim()
                : text.Substring(3).Trim();

            // Strip language specifier on first line if present
            var firstBreak = content.IndexOfAny(['\r', '\n']);
            if (firstBreak > 0)
            {
                var lang = content.Substring(0, firstBreak).Trim();
                if (IsKnownShellIdentifier(lang))
                {
                    content = content.Substring(firstBreak).Trim();
                }
            }

            return content.Trim();
        }

        // Single line or simple text: get first meaningful line
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";

        // Strip surrounding asterisks if bold
        firstLine = firstLine.Trim('*').Trim();

        // Strip inline backticks: `cmd` -> cmd
        if (firstLine.StartsWith('`') && firstLine.EndsWith('`') && firstLine.Length >= 2)
        {
            firstLine = firstLine.Substring(1, firstLine.Length - 2).Trim();
        }

        return firstLine;
    }

    private static bool IsKnownShellIdentifier(string lang)
    {
        return lang.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("bat", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("batch", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("bash", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("sh", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("shell", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("zsh", StringComparison.OrdinalIgnoreCase);
    }
}
