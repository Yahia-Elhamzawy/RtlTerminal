using Microsoft.Win32;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RtlTerminal;

internal static partial class Program
{
    private static void CheckDirectoryHistory()
    {
        var registryPath = @"Software\RtlTerminal.Tests\History-" + Guid.NewGuid().ToString("N");
        MainWindow? main = null;
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
                key.SetValue("LastCmdDirectories", new[] { @"C:\legacy\", @"c:\LEGACY", @"D:\old" }, RegistryValueKind.MultiString);
            var store = new DirectoryHistoryStore(registryPath);
            if (store.Snapshot().Count != 2 || store.Snapshot().Any(e => e.LastVisitedUtc is not null))
                throw new Exception("Legacy history migration invented dates or duplicated paths.");
            var first = DateTimeOffset.Parse("2026-10-20T10:20:30Z");
            store.Visit(@"C:\visited", "CommandPrompt", null, first);
            store.Visit(@"c:\visited\", "PowerShell", null, first.AddHours(2));
            var entry = store.Snapshot()[0];
            if (entry.FirstVisitedUtc != first || entry.LastVisitedUtc != first.AddHours(2) || entry.Profile != "PowerShell" || store.Snapshot().Count != 3)
                throw new Exception("Revisit did not preserve first visit, update last visit, or deduplicate Windows paths.");
            store.Clear();
            for (var i = 0; i < 205; i++) store.Visit(@"C:\history\" + i, "CommandPrompt", null, first.AddMinutes(i));
            if (store.Snapshot().Count != 200 || store.Snapshot()[0].Path != @"C:\history\204" || store.Snapshot()[^1].Path != @"C:\history\5")
                throw new Exception("History capacity or newest-first order is incorrect.");
            store.SetEnabled(false); store.Visit(@"C:\must-not-record", "CommandPrompt", null);
            var reopened = new DirectoryHistoryStore(registryPath);
            if (reopened.Enabled || reopened.Snapshot().Count != 200 || reopened.Snapshot().Any(e => e.Path.Contains("must-not-record")))
                throw new Exception("Disabled preference or history persistence failed.");
            store.SetEnabled(true);
            main = new MainWindow(store);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            object? Call(string method, params object?[] args) => typeof(MainWindow).GetMethod(method, flags | BindingFlags.Static)!.Invoke(main, args);
            var menu = (MenuItem)main.FindName("LastDirectoriesMenuItem");
            Call("LastDirectoriesMenuItem_SubmenuOpened", menu, new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, menu));
            if (menu.Items.OfType<MenuItem>().Count(m => m.Header is TextBlock) != 20 || !((MenuItem)menu.Items[^1]).Header.ToString()!.StartsWith("More"))
                throw new Exception("Last directories must show 20 entries followed by More.");
            var history = new DirectoryHistoryWindow(store);
            var list = (ListBox)history.FindName("HistoryList");
            if (list.Items.Count != 200 || store.Snapshot()[0].LastUsed.Length < 10) throw new Exception("History page is missing rows or visit timestamps.");
            CaptureHistory(history);
            ((TextBox)history.FindName("SearchBox")).Text = "history\\204";
            if (list.Items.Count != 1) throw new Exception("Directory history search failed.");
            history.Close();
            var profileType = typeof(MainWindow).GetNestedType("TerminalProfile", BindingFlags.NonPublic)!;
            var paneA = Call("CreatePane", Enum.ToObject(profileType, 0))!;
            var paneB = Call("CreatePane", Enum.ToObject(profileType, 1))!;
            store.Clear();
            Call("ObserveDirectory", paneA, @"C:\first");
            var originalDate = store.Snapshot()[0].LastVisitedUtc;
            Call("ObserveDirectory", paneB, @"C:\second");
            Call("ObserveDirectory", paneA, @"C:\first");
            if (store.Snapshot()[0].Path != @"C:\second" || store.Snapshot().Single(e => e.Path == @"C:\first").LastVisitedUtc != originalDate)
                throw new Exception("Repeated output or polling changed the visit order/time.");
            Call("ObserveDirectory", paneA, @"C:\third"); Call("ObserveDirectory", paneA, @"C:\first");
            if (store.Snapshot()[0].Path != @"C:\first" || store.Snapshot().Count != 3)
                throw new Exception("Independent panes or returning to a directory failed.");
            store.Clear(); Call("ObserveDirectory", paneA, @"C:\first"); Call("ObserveDirectory", paneB, @"C:\second");
            if (store.Snapshot().Count != 0 || new DirectoryHistoryStore(registryPath).Snapshot().Count != 0)
                throw new Exception("Cleared history immediately refilled or legacy history reappeared.");
            store.SetEnabled(false); Call("ObserveDirectory", paneA, @"C:\disabled");
            if (store.Snapshot().Count != 0) throw new Exception("Disabled recording accepted a pane visit.");
            store.SetEnabled(true); Call("ObserveDirectory", paneA, @"C:\enabled");
            var wsl = Call("CreatePane", Enum.ToObject(profileType, 2))!;
            wsl.GetType().GetProperty("WslDistribution")!.SetValue(wsl, "Ubuntu");
            Call("ObserveDirectory", wsl, @"\\wsl.localhost\Ubuntu\home\user\Repo");
            Call("ObserveDirectory", wsl, @"\\wsl.localhost\Ubuntu\home\user\repo");
            if (store.Snapshot().Count != 3 || store.Snapshot()[0].Distribution != "Ubuntu")
                throw new Exception("WSL distribution metadata or case-sensitive directories were lost.");
            var settings = new FontSettingsWindow("Consolas", 14, FontWeights.Normal, FontStyles.Normal, 2000);
            settings.SetDirectoryHistoryEnabled(false);
            if (settings.SelectedDirectoryHistoryEnabled || !store.Enabled) throw new Exception("Settings preview/cancel changed persisted history preference.");
            settings.Close();
            foreach (var instagram in new[] { false, true })
            {
                var social = (string)typeof(MainWindow).GetMethod("StartupSocialLine", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [instagram])!;
                if (social != (instagram ? "Instagram: @BehnamTjd" : "YouTube: @aka_techno")) throw new Exception("Startup social identity is wrong.");
            }
            Console.WriteLine("PASS directory history migration, 200/20 limits, dates, clear/disable, search, per-pane transitions, WSL case and startup identities");
        }
        finally { main?.Close(); Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
    }

    private static void CaptureHistory(DirectoryHistoryWindow window)
    {
        var content = (FrameworkElement)window.Content;
        ((Panel)content).Background = window.Background;
        content.Measure(new Size(860, 610)); content.Arrange(new Rect(0, 0, 860, 610)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(860, 610, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/render-checks");
        using var output = File.Create("artifacts/render-checks/directory-history.png"); encoder.Save(output);
    }
}
