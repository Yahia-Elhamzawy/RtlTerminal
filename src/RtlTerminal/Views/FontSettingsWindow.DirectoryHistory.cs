using System.Windows;
using System.Windows.Controls;

namespace RtlTerminal;

public partial class FontSettingsWindow
{
    private readonly CheckBox _directoryHistoryEnabled = new() { Content = UiText.Get("Remember visited directories"), IsChecked = true, Margin = new Thickness(0,16,0,12) };
    public bool SelectedDirectoryHistoryEnabled => _directoryHistoryEnabled.IsChecked == true;
    public void SetDirectoryHistoryEnabled(bool enabled) => _directoryHistoryEnabled.IsChecked = enabled;
    private void InitializeDirectoryHistorySettings()
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = UiText.Get("Directory history"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_directoryHistoryEnabled);
        panel.Children.Add(new TextBlock { Text = UiText.Paragraphs("Keep up to 200 recently visited directories across tabs and split panes. File → Last directories shows the most recent 20. More opens the searchable history with visit dates.\n\nTurning history off stops recording new visits and keeps existing entries. Clear history from the directory history window to remove saved entries. Apply saves this preference."), TextWrapping = TextWrapping.Wrap });
        SettingsTabs.Items.Add(new TabItem { Header = UiText.Get("History"), Content = panel });
    }
}
