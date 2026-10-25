using System.IO;
using System.Text;
using System.Text.Json;

namespace RtlTerminal.Ai;

/// <summary>
/// Maintains ongoing conversation history and execution state for terminal agent interactions.
/// Supports adding user messages, assistant responses, command execution tool outputs,
/// complete reset for new sessions, and persistence to/from JSON.
/// </summary>
public sealed class AgentSession
{
    private readonly List<AiChatMessage> _messages = new();
    private readonly object _lock = new();

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; private set; } = DateTime.UtcNow;

    public IReadOnlyList<AiChatMessage> Messages
    {
        get
        {
            lock (_lock)
            {
                return _messages.ToArray();
            }
        }
    }

    public int MessageCount
    {
        get
        {
            lock (_lock)
            {
                return _messages.Count;
            }
        }
    }

    /// <summary>
    /// Appends a user message to the session history.
    /// </summary>
    public void AddUserMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_lock)
        {
            _messages.Add(new AiChatMessage("user", message.Trim()));
            LastUpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Appends an assistant response to the session history.
    /// </summary>
    public void AddAssistantMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_lock)
        {
            _messages.Add(new AiChatMessage("assistant", message.Trim()));
            LastUpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Appends command execution output to the session history. Formatted so any LLM provider
    /// can reliably parse the execution result, stdout, stderr, and exit code.
    /// </summary>
    public void AddToolOutput(string command, int exitCode, string stdout, string stderr)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Command Execution Result]");
        sb.AppendLine($"Command: {command}");
        sb.AppendLine($"Exit Code: {exitCode}");

        var outTrim = stdout?.Trim();
        var errTrim = stderr?.Trim();

        if (!string.IsNullOrEmpty(outTrim))
        {
            sb.AppendLine("Stdout:");
            sb.AppendLine(TruncateText(outTrim, maxLines: 120, maxChars: 10000));
        }

        if (!string.IsNullOrEmpty(errTrim))
        {
            sb.AppendLine("Stderr:");
            sb.AppendLine(TruncateText(errTrim, maxLines: 60, maxChars: 4000));
        }

        if (string.IsNullOrEmpty(outTrim) && string.IsNullOrEmpty(errTrim))
        {
            sb.AppendLine("(No text output produced)");
        }

        lock (_lock)
        {
            _messages.Add(new AiChatMessage("user", sb.ToString().TrimEnd()));
            LastUpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Completely clears conversation history and resets the session for /ai-new.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _messages.Clear();
            LastUpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns the complete chat history prefixed by the system prompt built from the given AgentContext.
    /// </summary>
    public IReadOnlyList<AiChatMessage> GetHistoryWithContext(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_lock)
        {
            var result = new List<AiChatMessage>(_messages.Count + 1)
            {
                new AiChatMessage("system", context.BuildSystemPrompt())
            };
            result.AddRange(_messages);
            return result;
        }
    }

    /// <summary>
    /// Serializes session state and history to a JSON string.
    /// </summary>
    public string ToJson()
    {
        lock (_lock)
        {
            return JsonSerializer.Serialize(new SerializedSession
            {
                Id = Id,
                CreatedAt = CreatedAt,
                LastUpdatedAt = LastUpdatedAt,
                Messages = _messages
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Deserializes an AgentSession from a JSON string.
    /// </summary>
    public static AgentSession FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<SerializedSession>(json);
        var session = new AgentSession
        {
            Id = data?.Id ?? Guid.NewGuid().ToString("N"),
            CreatedAt = data?.CreatedAt ?? DateTime.UtcNow,
            LastUpdatedAt = data?.LastUpdatedAt ?? DateTime.UtcNow
        };
        if (data?.Messages != null)
        {
            foreach (var m in data.Messages)
            {
                session._messages.Add(m);
            }
        }
        return session;
    }

    /// <summary>
    /// Saves the session state to a file.
    /// </summary>
    public void SaveToFile(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, ToJson());
    }

    /// <summary>
    /// Loads a saved session from a file.
    /// </summary>
    public static AgentSession LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return new AgentSession();
        var json = File.ReadAllText(filePath);
        return FromJson(json);
    }

    private static string TruncateText(string text, int maxLines, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            var lineCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') lineCount++;
            }
            if (lineCount <= maxLines) return text;
        }

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (lines.Length <= maxLines && text.Length <= maxChars)
            return text;

        var headCount = Math.Max(1, maxLines / 2);
        var tailCount = Math.Max(1, maxLines / 2);

        var head = string.Join(Environment.NewLine, lines.Take(headCount));
        var tail = string.Join(Environment.NewLine, lines.TakeLast(tailCount));

        return $"{head}{Environment.NewLine}... [Output truncated ({lines.Length} lines total)] ...{Environment.NewLine}{tail}";
    }

    private sealed class SerializedSession
    {
        public string Id { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public List<AiChatMessage> Messages { get; set; } = new();
    }
}
