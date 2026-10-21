using System.Diagnostics;
using System.IO;
using System.Text;

namespace RtlTerminal;

public enum MacroMode { Interactive, Timed }

public sealed class MacroStep
{
    public string Input { get; set; } = "";
    public string ExpectedPrompt { get; set; } = "";
    public int DelayMilliseconds { get; set; }
    public string InputSummary => $"{Input.Length} characters (hidden)";
}

public sealed class TerminalMacro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public MacroMode Mode { get; set; }
    public int Shortcut { get; set; }
    public List<MacroStep> Steps { get; set; } = [];

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 ||
            !Enum.IsDefined(Mode) || Shortcut is < 0 or > 9 || Steps.Count is < 1 or > 10000 ||
            Steps.Sum(s => (long)s.Input.Length + s.ExpectedPrompt.Length) > 1_000_000)
            throw new InvalidDataException("Invalid macro or recording is too large.");
        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (step.Input.Length == 0 || step.DelayMilliseconds is < 0 or > 86400000)
                throw new InvalidDataException("Invalid macro step.");
            if (Mode == MacroMode.Interactive && i > 0 && string.IsNullOrWhiteSpace(step.ExpectedPrompt))
                throw new InvalidDataException($"Step {i + 1} needs a prompt to wait for. Edit the prompt before saving.");
        }
    }
}

// Owned by the UI thread. Only actual user input is recorded, never terminal protocol replies.
public sealed class MacroRecorder(MacroMode mode)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastInput;
    private MacroStep? _pending;
    public TerminalMacro Macro { get; } = new() { Mode = mode };

    public void Record(string input, string prompt)
    {
        if (input.Length == 0) return;
        if (Macro.Steps.Count >= 10000 || Macro.Steps.Sum(s => (long)s.Input.Length) + input.Length > 1_000_000)
            throw new InvalidOperationException("Recording limit reached. Stop and save this recording.");
        var now = _clock.ElapsedMilliseconds;
        if (Macro.Mode == MacroMode.Timed)
            Macro.Steps.Add(new() { Input = input, DelayMilliseconds = (int)Math.Min(86400000, now - _lastInput) });
        else
        {
            if (_pending is null)
            {
                _pending = new() { ExpectedPrompt = Macro.Steps.Count == 0 ? "" : prompt.TrimEnd() };
                Macro.Steps.Add(_pending);
            }
            _pending.Input += input;
            if (input.Contains('\r') || input.Contains('\n')) _pending = null;
        }
        _lastInput = now;
    }

    public static string Prompt(TerminalSnapshot snapshot)
    {
        var row = snapshot.CursorRow;
        if (row < 0 || row >= snapshot.Lines.Count) return "";
        var text = string.Concat(snapshot.Lines[row].Runs.Select(r => r.Text)).TrimEnd();
        // Keep the whole logical prompt when it wraps across terminal rows.
        while (row > 0 && snapshot.Lines[row].WrappedFromPrevious)
            text = string.Concat(snapshot.Lines[--row].Runs.Select(r => r.Text)) + text;
        return text.Length > 1024 ? text[^1024..] : text;
    }
}

// Output is published by the ConPTY reader; playback always targets the original pane.
public sealed class MacroPlayback
{
    private readonly object _gate = new();
    private long _generation;
    private string _prompt = "";
    private bool _closed;

    public void Observe(TerminalSnapshot snapshot)
    {
        lock (_gate) { _prompt = MacroRecorder.Prompt(snapshot); _generation++; }
    }

    public void Close() { lock (_gate) _closed = true; }

    public async Task RunAsync(TerminalMacro macro, Action<string> send, int timeoutSeconds,
        Action<int, bool> progress, CancellationToken cancellationToken)
    {
        macro.Validate();
        long sentAt;
        lock (_gate) sentAt = _generation;
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var step = macro.Steps[i];
            cancellationToken.ThrowIfCancellationRequested();
            progress(i + 1, true);
            if (macro.Mode == MacroMode.Timed)
                await Task.Delay(step.DelayMilliseconds, cancellationToken);
            else if (i > 0)
            {
                var wait = Stopwatch.StartNew();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (_closed) throw new IOException("The target terminal has closed.");
                        if (_generation > sentAt && _prompt.TrimEnd().EndsWith(step.ExpectedPrompt.TrimEnd(), StringComparison.Ordinal)) break;
                    }
                    if (wait.Elapsed.TotalSeconds >= timeoutSeconds)
                        throw new TimeoutException($"Step {i + 1}: prompt was not detected. Nothing further was sent.");
                    await Task.Delay(25, cancellationToken);
                }
            }
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_closed) throw new IOException("The target terminal has closed.");
                sentAt = _generation;
            }
            send(step.Input);
            progress(i + 1, false);
        }
    }
}
