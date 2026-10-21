using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RtlTerminal;

public sealed class MacroEditorWindow : Window
{
    public string Password { get; private set; } = "";
    public MacroEditorWindow(TerminalMacro macro, string initialPassword = "")
    {
        Title = "Save macro"; Width = 700; Height = 520; MinWidth = 540; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(244,245,247));
        Foreground = new SolidColorBrush(Color.FromRgb(24,32,51));
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var fields = new StackPanel(); DockPanel.SetDock(fields, Dock.Top); root.Children.Add(fields);
        fields.Children.Add(new TextBlock { Text = "Name" });
        var name = new TextBox { Text = macro.Name, MaxLength = 80, Margin = new Thickness(0,4,0,10) }; fields.Children.Add(name);
        fields.Children.Add(new TextBlock { Text = $"Mode: {macro.Mode}. The first step runs immediately. Inputs stay hidden; edit prompts to match the server exactly.", TextWrapping = TextWrapping.Wrap });
        fields.Children.Add(new TextBlock { Text = "Password (optional; leave empty for Windows account protection only)", Margin = new Thickness(0,10,0,4) });
        var password = new PasswordBox { Password = initialPassword }; fields.Children.Add(password);
        fields.Children.Add(new TextBlock { Text = "Confirm password", Margin = new Thickness(0,8,0,4) });
        var confirm = new PasswordBox { Password = initialPassword }; fields.Children.Add(confirm);
        fields.Children.Add(new TextBlock { Text = "Shortcut: Ctrl+Shift+Alt + number", Margin = new Thickness(0,10,0,4) });
        var shortcut = new ComboBox { ItemsSource = new[] { "None", "1", "2", "3", "4", "5", "6", "7", "8", "9" }, SelectedIndex = macro.Shortcut, Margin = new Thickness(0,0,0,12) }; fields.Children.Add(shortcut);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var save = new Button { Content = "Save", MinWidth = 90, Padding = new Thickness(10,5,10,5), IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(8,0,0,0), IsCancel = true };
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        var grid = new DataGrid { ItemsSource = macro.Steps, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false };
        grid.Columns.Add(new DataGridTextColumn { Header = "Input", Binding = new Binding("InputSummary"), IsReadOnly = true, Width = 170 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Wait for prompt", Binding = new Binding("ExpectedPrompt"), MinWidth = 240, Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = macro.Mode == MacroMode.Timed });
        grid.Columns.Add(new DataGridTextColumn { Header = "Delay (ms)", Binding = new Binding("DelayMilliseconds"), Width = 85, IsReadOnly = macro.Mode == MacroMode.Interactive });
        root.Children.Add(grid);
        save.Click += (_, _) =>
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))
            { MessageBox.Show(this, "Correct the invalid step value before saving.", Title); return; }
            if (password.Password != confirm.Password) { MessageBox.Show(this, "Passwords do not match.", Title); return; }
            macro.Name = name.Text.Trim(); macro.Shortcut = shortcut.SelectedIndex;
            try { macro.Validate(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); return; }
            Password = password.Password; DialogResult = true;
        };
        Loaded += (_, _) => name.Focus();
    }

    public static string? Unlock(Window owner, string name)
    {
        var window = new Window { Owner = owner, Title = "Unlock macro", Width = 380, Height = 190,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Background = new SolidColorBrush(Color.FromRgb(244,245,247));
        window.Foreground = new SolidColorBrush(Color.FromRgb(24,32,51));
        var stack = new StackPanel { Margin = new Thickness(20) }; window.Content = stack;
        stack.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        var password = new PasswordBox { Margin = new Thickness(0,12,0,12) }; stack.Children.Add(password);
        var button = new Button { Content = "Unlock", IsDefault = true, Padding = new Thickness(10,5,10,5) }; stack.Children.Add(button);
        button.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => password.Focus();
        return window.ShowDialog() == true ? password.Password : null;
    }
}
