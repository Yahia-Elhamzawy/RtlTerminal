using System.Collections;

namespace RtlTerminal;

public static class TerminalEnvironment
{
    public static Dictionary<string, string> Create(IDictionary inherited)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in inherited) values[(string)entry.Key] = (string)entry.Value!;
        values["COLORTERM"] = "truecolor";
        values["TERM_PROGRAM"] = "RtlTerminal";
        values[ShellPath.PercentVariable] = "%";
        var entries = values.GetValueOrDefault("WSLENV", "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !e.Split('/')[0].Equals("COLORTERM", StringComparison.OrdinalIgnoreCase) &&
                        !e.Split('/')[0].Equals("TERM_PROGRAM", StringComparison.OrdinalIgnoreCase));
        values["WSLENV"] = string.Join(':', entries.Concat(new[] { "COLORTERM", "TERM_PROGRAM" }));
        return values;
    }
    public static string Block() => string.Join('\0', Create(Environment.GetEnvironmentVariables())
        .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)) + "\0\0";
}
