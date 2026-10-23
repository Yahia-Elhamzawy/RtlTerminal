using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RtlTerminal;

internal static partial class Program
{
    private static void CheckUiLanguages()
    {
        var initial = UiText.Current;
        var persisted = UiText.Load();
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var main = new MainWindow();
        var language = (MenuItem)main.FindName("LanguageMenuItem");
        var help = (MenuItem)ItemsControl.ItemsControlFromItemContainer(language);
        var originalItems = help.Items.Cast<object>().ToArray();
        var directions = Descendants(main).OfType<FrameworkElement>().ToDictionary(e => e, e => (e.FlowDirection, e.HorizontalAlignment));
        var typedInput = new TextBox { Text = "Settings / فارسی / العربية / C:\\user-data" };
        var macro = new TerminalMacro { Name = "Settings", Steps = [new() { Input = "Run\r", ExpectedPrompt = "Name:" }] };
        try
        {
            foreach (var choice in new[] { UiLanguage.English, UiLanguage.Persian, UiLanguage.Arabic, UiLanguage.English })
            {
                UiText.Select(choice, persist: false);
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                if (language.Header.ToString() != (choice == UiLanguage.English ? "LANGUAGE" : choice == UiLanguage.Persian ? "زبان" : "اللغة"))
                    throw new Exception("Language menu did not update immediately.");
                if (help.Header.ToString() != UiText.Get("_Help")) throw new Exception("Help menu translation did not update.");
                if (!help.Items.Cast<object>().SequenceEqual(originalItems)) throw new Exception("Changing language reordered menus.");
                foreach (var (element, previous) in directions)
                    if ((element.FlowDirection, element.HorizontalAlignment) != previous)
                        throw new Exception("Changing language changed UI direction or alignment.");
                var checks = new[] { "EnglishLanguageItem", "PersianLanguageItem", "ArabicLanguageItem" }.Select(n => (MenuItem)main.FindName(n)).ToArray();
                if (checks.Count(m => m.IsChecked) != 1 || !checks[(int)choice].IsChecked)
                    throw new Exception("Language selection check marks are inconsistent.");
                if (typedInput.Text != "Settings / فارسی / العربية / C:\\user-data" || macro.Name != "Settings" || macro.Steps[0].Input != "Run\r")
                    throw new Exception("Language selection modified user content.");
                if (CultureInfo.CurrentCulture != culture || CultureInfo.CurrentUICulture != uiCulture)
                    throw new Exception("UI language changed formatting culture.");
                if (UiText.Load() != persisted) throw new Exception("Non-persisted test selection changed user settings.");
                var settings = new FontSettingsWindow("Consolas", 14, FontWeights.Normal, FontStyles.Normal, 2000);
                if (settings.Title != UiText.Get("Settings") || settings.FlowDirection != FlowDirection.LeftToRight)
                    throw new Exception("Settings language or direction is wrong.");
                if (choice != UiLanguage.English && UiText.Get("Don't show this update again").Contains("Don't"))
                    throw new Exception("Apostrophe-containing UI key was not translated.");
                var update = new UpdateAvailableWindow(new Version(1,1,3), new Version(1,1,4));
                var updateText = ((TextBlock)update.FindName("VersionTextBlock")).Text;
                if (updateText != UiText.Format("Installed: {0}    Available: {1}", "1.1.3", "1.1.4"))
                    throw new Exception("Update version label is not translated.");
                var editor = new MacroEditorWindow(macro);
                if (choice != UiLanguage.English)
                {
                    Capture(settings, "settings-" + choice + ".png", 860, 680);
                    Capture(editor, "macro-" + choice + ".png", 880, 720);
                }
                var guide = new GuideWindow();
                if (((FrameworkElement)guide.FindName("ContentRoot")).FlowDirection != FlowDirection.RightToLeft)
                    throw new Exception("Guide language mirrored its established layout.");
                guide.Close();
                editor.Close(); settings.Close(); update.Close();
            }
            // Programmatically created context menus must use the current UI language too.
            UiText.Select(UiLanguage.Persian, false);
            var menu = (ContextMenu)typeof(MainWindow).GetMethod("CreateTerminalContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            if (!menu.Items.OfType<MenuItem>().Any(m => m.Header?.ToString() == UiText.Get("_Copy")))
                throw new Exception("Dynamic context menu was not localized.");
            Console.WriteLine("PASS English/Persian/Arabic live menus, one checked choice, fixed UI direction/order, literal user data and translated dialogs");
        }
        finally { UiText.Select(initial, false); main.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
