using Microsoft.Win32;

namespace RtlTerminal;

public sealed record MacroSettings(MacroMode Mode = MacroMode.Interactive, int TimeoutSeconds = 30, bool Shortcuts = true)
{
    public static MacroSettings Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\RtlTerminal\Macros");
        return new(key?.GetValue("Mode") is int mode && mode == 1 ? MacroMode.Timed : MacroMode.Interactive,
            Math.Clamp(key?.GetValue("Timeout") is int timeout ? timeout : 30, 5, 600),
            key?.GetValue("Shortcuts") is not int shortcuts || shortcuts != 0);
    }
    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\RtlTerminal\Macros");
        key.SetValue("Mode", (int)Mode); key.SetValue("Timeout", TimeoutSeconds); key.SetValue("Shortcuts", Shortcuts ? 1 : 0);
    }
}
