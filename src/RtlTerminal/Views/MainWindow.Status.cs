using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RtlTerminal;

public partial class MainWindow
{
    private void InitializeWindowStatus()
    {
        SetElevationBadge(ProcessPrivileges.IsElevated());
        RefreshActiveDirectory();
    }
    private void SetElevationBadge(bool elevated)
    {
        AdministratorBadge.Visibility = elevated ? Visibility.Visible : Visibility.Collapsed;
        TabScroller.MaxWidth = Math.Max(0, TitleTabsHost.ActualWidth - 72 - (elevated ? 30 : 0));
    }
    private void SetPaneDirectory(TerminalPane pane, string? path)
    {
        if (DirectoryHistoryStore.Normalize(path) is not { } directory) return;
        lock (pane.DirectoryHistoryGate)
        {
            if (pane.CurrentDirectory == directory) return;
            pane.CurrentDirectory = directory;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (ReferenceEquals(ExplorerTargetPane(), pane)) RefreshActiveDirectory();
        }));
    }
    private static string DirectoryLabel(string path)
    {
        var normalized = DirectoryHistoryStore.Normalize(path);
        if (normalized is null) return "";
        var leaf = Path.GetFileName(normalized);
        return leaf.Length > 0 ? leaf : normalized; // Drive/UNC roots have no last component.
    }
    private void RefreshActiveDirectory()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshActiveDirectory));
            return;
        }
        var pane = ExplorerTargetPane();
        string? directory = null;
        if (pane is not null)
        {
            lock (pane.DirectoryHistoryGate) directory = pane.CurrentDirectory;
            directory ??= TerminalDirectory.FromReport(pane.Buffer?.CaptureSnapshot().WorkingDirectory, pane.WslDistribution);
        }
        ActiveDirectoryText.Text = directory is null ? "…" : DirectoryLabel(directory);
        ActiveDirectoryButton.IsEnabled = directory is not null;
        ActiveDirectoryButton.ToolTip = directory is null ? UiText.Get("Current directory is not available yet") :
            directory + "\n" + UiText.Get("Open current directory in Explorer");
    }
    private async void ActiveDirectory_Click(object sender, RoutedEventArgs e)
        => await OpenPaneDirectoryAsync(ExplorerTargetPane());

    private async void UpdateSelectedDirectory()
    {
        RefreshActiveDirectory();
        if (ExplorerTargetPane() is { } pane) await PollDirectoryAsync(pane);
        RefreshActiveDirectory();
    }
}
