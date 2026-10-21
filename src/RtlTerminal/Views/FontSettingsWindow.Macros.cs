using System.Windows;
using System.Windows.Controls;

namespace RtlTerminal;

public partial class FontSettingsWindow
{
    private ComboBox _macroMode = null!;
    private TextBox _macroTimeout = null!;
    private CheckBox _macroShortcuts = null!;
    private TabItem _macroTab = null!;
    public MacroSettings SelectedMacroSettings => new((MacroMode)_macroMode.SelectedIndex,
        int.TryParse(_macroTimeout.Text, out var timeout) ? Math.Clamp(timeout, 5, 600) : 30,
        _macroShortcuts.IsChecked == true);

    private void InitializeMacroSettings()
    {
        var settings = MacroSettings.Load();
        var panel = new StackPanel { Margin = new Thickness(16) };
        _macroTab = new TabItem { Header = "Macros", Content = panel }; SettingsTabs.Items.Add(_macroTab);
        panel.Children.Add(new TextBlock { Text = "Default recording mode", Margin = new Thickness(0,0,0,6) });
        _macroMode = new ComboBox { ItemsSource = new[] { "Interactive — wait for terminal prompts", "Timed — reproduce recorded delays" }, SelectedIndex = (int)settings.Mode }; panel.Children.Add(_macroMode);
        panel.Children.Add(new TextBlock { Text = "Prompt timeout in seconds (5–600)", Margin = new Thickness(0,16,0,6) });
        _macroTimeout = new TextBox { Text = settings.TimeoutSeconds.ToString() }; panel.Children.Add(_macroTimeout);
        _macroShortcuts = new CheckBox { Content = "Enable macro shortcuts (Ctrl+Shift+Alt+1–9)", IsChecked = settings.Shortcuts, Margin = new Thickness(0,16,0,16) }; panel.Children.Add(_macroShortcuts);
        panel.Children.Add(new TextBlock { Text = "Macros are stored encrypted for your Windows account. Add an independent password to protect sensitive recordings from other apps running as you. Passwords cannot be recovered.\n\nInteractive recordings group input until Enter. Before saving, review each expected prompt. Use timed mode for applications without stable prompts. Playback stays in the original pane; Escape or the toolbar status stops playback.", TextWrapping = TextWrapping.Wrap });
    }
    public void ShowMacroTab() => SettingsTabs.SelectedItem = _macroTab;
}
