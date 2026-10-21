using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RtlTerminal;

public partial class MainWindow
{
    private readonly MacroStore _macroStore = new();
    private MacroSettings _macroSettings = MacroSettings.Load();
    private MacroRecorder? _macroRecorder;
    private TerminalPane? _macroPane;
    private CancellationTokenSource? _macroCancellation;
    private bool _macroPreparing;

    private void SendUserInput(TerminalPane? pane, string input)
    {
        if (pane?.Session is not { } session || pane.SessionEnded) return;
        if (_macroPane == pane && (_macroCancellation is not null || _macroPreparing)) return;
        if (_macroPane == pane && _macroRecorder is { } recorder)
        {
            try { recorder.Record(input, pane.Buffer is { } buffer ? MacroRecorder.Prompt(buffer.CaptureSnapshot()) : ""); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Macro recording"); return; }
        }
        session.Write(input);
    }

    private void SetMacroStatus(string? text)
    {
        MacroStatusButton.Content = text ?? "";
        MacroStatusButton.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        MacroRecordMenuItem.Header = _macroRecorder is null ? "_Start recording" : "_Stop recording...";
        MacroRecordMenuItem.IsEnabled = _macroCancellation is null && !_macroPreparing;
        MacroStopMenuItem.IsEnabled = _macroCancellation is not null;
    }

    private void MacroRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_macroCancellation is not null || _macroPreparing) return;
        if (_macroRecorder is null)
        {
            if (ActivePane?.Session is null || ActivePane.SessionEnded) return;
            _macroPane = ActivePane;
            _macroRecorder = new(_macroSettings.Mode);
            SetMacroStatus($"● Recording · pane {_macroPane.Number}");
            ActiveView.Focus();
            return;
        }
        var macro = _macroRecorder.Macro;
        _macroRecorder = null; _macroPane = null; SetMacroStatus(null);
        if (macro.Steps.Count == 0) return;
        EditMacro(macro, "");
    }

    private void EditMacro(TerminalMacro macro, string password)
    {
        // Keep the recording available if saving fails; never discard it on an I/O error.
        while (true)
        {
            var editor = new MacroEditorWindow(macro, password) { Owner = this };
            if (editor.ShowDialog() != true) return;
            password = editor.Password;
            try { _macroStore.Save(macro, password); return; }
            catch (Exception ex) { MessageBox.Show(this, ex is System.Security.Cryptography.CryptographicException ? "Could not encrypt the macro." : ex.Message, "Save macro"); }
        }
    }

    private void MacroStop_Click(object sender, RoutedEventArgs e) => _macroCancellation?.Cancel();
    private void MacroStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_macroRecorder is not null) MacroRecord_Click(sender, e);
        else _macroCancellation?.Cancel();
    }
    private void MacroSettings_Click(object sender, RoutedEventArgs e) => ShowSettings(false, true);

    private void MacroMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender) return;
        MacroRunMenu.Items.Clear();
        try
        {
            foreach (var entry in _macroStore.List())
            {
                var item = new MenuItem { Header = entry.ToString(), IsEnabled = _macroRecorder is null && _macroCancellation is null && !_macroPreparing };
                item.Click += async (_, _) => await RunMacro(entry);
                MacroRunMenu.Items.Add(item);
            }
            if (MacroRunMenu.Items.Count == 0) MacroRunMenu.Items.Add(new MenuItem { Header = "No recorded macros", IsEnabled = false });
        }
        catch { MacroRunMenu.Items.Add(new MenuItem { Header = "Unable to read macros — open Manage", IsEnabled = false }); }
    }

    private async Task RunMacro(MacroEntry entry)
    {
        if (_macroRecorder is not null || _macroCancellation is not null || _macroPreparing || ActivePane?.Session is null) return;
        var pane = ActivePane;
        var session = pane.Session;
        _macroPane = pane; _macroPreparing = true; SetMacroStatus("Unlocking macro…");
        try
        {
            var password = entry.PasswordProtected ? MacroEditorWindow.Unlock(this, entry.Name) : "";
            if (password is null) return;
            var macro = await Task.Run(() => _macroStore.Load(entry.Id, password));
            if (pane.Session != session || pane.SessionEnded) throw new IOException("The target terminal has closed.");
            _macroPane = pane;
            using var cancellation = new CancellationTokenSource();
            _macroCancellation = cancellation;
            var playback = new MacroPlayback(); pane.Playback = playback;
            SetMacroStatus($"▶ Running · pane {pane.Number}");
            await playback.RunAsync(macro, input =>
            {
                if (pane.Session != session || pane.SessionEnded) throw new IOException("The target terminal has closed.");
                session.Write(input);
            }, _macroSettings.TimeoutSeconds, (step, waiting) =>
                SetMacroStatus($"{(waiting ? "◷" : "▶")} {step}/{macro.Steps.Count} · pane {pane.Number}"), cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (System.Security.Cryptography.CryptographicException) { MessageBox.Show(this, "Incorrect password or damaged macro file.", "Macro"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Macro stopped"); }
        finally
        {
            pane.Playback = null; _macroCancellation = null; _macroPane = null; _macroPreparing = false; SetMacroStatus(null);
        }
    }

    private async void MacroManage_Click(object sender, RoutedEventArgs e)
    {
        if (_macroRecorder is not null || _macroCancellation is not null || _macroPreparing) return;
        var window = new Window { Owner = this, Title = "Recorded macros", Width = 620, Height = 400, MinWidth = 450, MinHeight = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244,245,247));
        window.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24,32,51));
        var root = new DockPanel { Margin = new Thickness(16) }; window.Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,12,0,0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var list = new ListBox(); root.Children.Add(list);
        bool Refresh()
        {
            try { list.ItemsSource = _macroStore.List(); return true; }
            catch { MessageBox.Show(this, "Unable to read macro storage. A file may be damaged or belong to another Windows account.", "Macros"); return false; }
        }
        if (!Refresh()) return;
        MacroEntry? selectedRun = null;
        foreach (var label in new[] { "Run", "Edit / password…", "Delete" })
        {
            var button = new Button { Content = label, Padding = new Thickness(12,6,12,6), Margin = new Thickness(0,0,8,0), IsEnabled = false }; buttons.Children.Add(button);
            list.SelectionChanged += (_, _) => button.IsEnabled = list.SelectedItem is MacroEntry;
            button.Click += (_, _) =>
            {
                if (list.SelectedItem is not MacroEntry entry) return;
                if (label == "Run") { selectedRun = entry; window.Close(); return; }
                try
                {
                    if (label == "Delete")
                    {
                        if (MessageBox.Show(window, $"Delete '{entry.Name}'?", "Delete macro", MessageBoxButton.YesNo) == MessageBoxResult.Yes) _macroStore.Delete(entry.Id);
                    }
                    else
                    {
                        var password = entry.PasswordProtected ? MacroEditorWindow.Unlock(window, entry.Name) : "";
                        if (password is null) return;
                        var macro = _macroStore.Load(entry.Id, password);
                        EditMacro(macro, password);
                    }
                    Refresh();
                }
                catch (System.Security.Cryptography.CryptographicException) { MessageBox.Show(window, "Incorrect password or damaged macro file.", "Macros"); }
                catch (Exception ex) { MessageBox.Show(window, ex.Message, "Macros"); }
            };
        }
        window.ShowDialog();
        if (selectedRun is not null) await RunMacro(selectedRun);
    }

    private bool HandleMacroKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _macroCancellation is not null) { _macroCancellation.Cancel(); return true; }
        if (!_macroSettings.Shortcuts || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) return false;
        var key = GetEffectiveKey(e);
        if (key < Key.D1 || key > Key.D9) return false;
        try
        {
            var entry = _macroStore.List().FirstOrDefault(m => m.Shortcut == key - Key.D0);
            if (entry is null) return false;
            _ = RunMacro(entry); return true;
        }
        catch { return false; }
    }
}
