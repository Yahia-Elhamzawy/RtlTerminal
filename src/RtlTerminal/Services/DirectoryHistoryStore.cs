using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Globalization;

namespace RtlTerminal;

public sealed record DirectoryHistoryEntry(string Path, string Profile, string? Distribution,
    DateTimeOffset? FirstVisitedUtc, DateTimeOffset? LastVisitedUtc)
{
    public string LastUsed => LastVisitedUtc?.ToLocalTime().ToString("yyyy/MM/dd  HH:mm:ss", CultureInfo.CurrentCulture) ?? UiText.Get("Time unavailable (imported)");
    public string Source => Distribution is { Length: > 0 } ? "WSL · " + Distribution : Profile == "PowerShell" ? "PowerShell" : "Command Prompt";
    public string Details => Path + "\n" + Source + "\n" + UiText.Get("First visit") + ": " + (FirstVisitedUtc?.ToLocalTime().ToString("G", CultureInfo.CurrentCulture) ?? UiText.Get("Unknown")) + "\n" + UiText.Get("Last visit") + ": " + LastUsed;
}

public sealed class DirectoryHistoryStore
{
    public const int MenuLimit = 20;
    public const int Capacity = 200;
    private readonly object _gate = new();
    private readonly string _registryPath;
    private List<DirectoryHistoryEntry> _entries;
    private bool _enabled;
    public bool Enabled { get { lock (_gate) return _enabled; } }

    public DirectoryHistoryStore(string registryPath = @"Software\RtlTerminal")
    {
        _registryPath = registryPath;
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        _enabled = key?.GetValue("DirectoryHistoryEnabled") is not int value || value != 0;
        _entries = [];
        if (key?.GetValue("DirectoryHistoryV2") is string json)
        {
            try { _entries = JsonSerializer.Deserialize<List<DirectoryHistoryEntry>>(json) ?? []; }
            catch (JsonException) { }
        }
        else
        {
            var legacy = key?.GetValue("LastCmdDirectories") switch { string[] values => values, string legacyValue => [legacyValue], _ => Array.Empty<string>() };
            _entries = legacy.Select(Normalize).Where(p => p is not null).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => new DirectoryHistoryEntry(p!, "CommandPrompt", null, null, null)).ToList();
        }
        _entries = _entries.Where(e => e is not null && Normalize(e.Path) is not null).Take(Capacity).ToList();
    }

    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || !System.IO.Path.IsPathFullyQualified(path)) return null;
        try { return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public IReadOnlyList<DirectoryHistoryEntry> Snapshot() { lock (_gate) return _entries.ToArray(); }
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("DirectoryHistoryEnabled", enabled ? 1 : 0);
            _enabled = enabled;
        }
    }
    public bool Visit(string path, string profile, string? distribution, DateTimeOffset? timestamp = null)
    {
        var normalized = Normalize(path); if (normalized is null) return false;
        lock (_gate)
        {
            if (!_enabled) return false;
            var comparison = distribution is null ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var existing = _entries.FindIndex(e => string.Equals(e.Path, normalized, comparison) && string.Equals(e.Distribution, distribution, StringComparison.OrdinalIgnoreCase));
            var previous = existing >= 0 ? _entries[existing] : null;
            var now = (timestamp ?? DateTimeOffset.UtcNow).ToUniversalTime();
            var next = _entries.ToList();
            if (existing >= 0) next.RemoveAt(existing);
            next.Insert(0, new(normalized, profile, distribution, previous?.FirstVisitedUtc ?? now, now));
            next = next.Take(Capacity).ToList();
            Persist(next); _entries = next; return true;
        }
    }
    public void Clear() { lock (_gate) { Persist([]); _entries = []; } }
    private void Persist(List<DirectoryHistoryEntry> entries)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
        key.SetValue("DirectoryHistoryV2", JsonSerializer.Serialize(entries), RegistryValueKind.String);
        key.DeleteValue("LastCmdDirectories", false);
    }
}
