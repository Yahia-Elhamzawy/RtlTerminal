using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RtlTerminal;

public partial class MainWindow
{
    private TerminalTab? _renamingTab;
    private TextBox? _tabRenameEditor;
    private string _tabRenameDraft = "";

    private void BeginTabRename(TerminalTab tab)
    {
        if (!_tabs.Contains(tab)) return;
        FinishTabRename(true, false, false);
        _renamingTab = tab; _tabRenameDraft = tab.Title;
        if (!ReferenceEquals(_activeTab, tab)) ShowTab(tab);
        else RebuildTabStrip();
    }

    private FrameworkElement CreateTabRenameEditor(TerminalTab tab)
    {
        var editor = new TextBox
        {
            Text = _tabRenameDraft, MaxLength = 128, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(5,0,0,0), VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 20, AcceptsReturn = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        editor.SetResourceReference(Control.ForegroundProperty, "ChromeForegroundBrush");
        System.Windows.Automation.AutomationProperties.SetName(editor, UiText.Get("Tab name"));
        _tabRenameEditor = editor;
        editor.TextChanged += (_, _) => { if (ReferenceEquals(_tabRenameEditor, editor)) _tabRenameDraft = editor.Text; };
        editor.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (ReferenceEquals(_tabRenameEditor, editor) && !editor.IsKeyboardFocusWithin) FinishTabRename(true, false);
        }));
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (ReferenceEquals(_tabRenameEditor, editor) && ReferenceEquals(_activeTab, tab))
            { editor.Focus(); editor.SelectAll(); }
        }));
        var cancel = new Button
        {
            Name = "CancelTabRenameButton", Width = 18, Height = 18, Margin = new Thickness(0,0,2,0),
            Focusable = false, IsTabStop = false,
            ToolTip = UiText.Get("Cancel rename"), Style = (Style)FindResource("ProfileActionSegmentStyle")
        };
        System.Windows.Automation.AutomationProperties.SetName(cancel, UiText.Get("Cancel rename"));
        var cross = new System.Windows.Shapes.Path
        {
            Width = 6, Height = 6, Data = Geometry.Parse("M0,0 L6,6 M6,0 L0,6"), StrokeThickness = 1
        };
        cross.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ChromeForegroundBrush");
        cancel.Content = cross;
        cancel.Click += (_, e) => { e.Handled = true; FinishTabRename(false); };
        var contents = new Grid();
        contents.ColumnDefinitions.Add(new ColumnDefinition());
        contents.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        contents.RowDefinitions.Add(new RowDefinition());
        contents.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
        contents.Children.Add(editor);
        Grid.SetColumn(cancel, 1); contents.Children.Add(cancel);
        var underline = new Border { CornerRadius = new CornerRadius(0,0,2,2) };
        underline.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        Grid.SetRow(underline, 1); Grid.SetColumnSpan(underline, 2); contents.Children.Add(underline);
        var field = new Border { Name = "TabRenameField", Height = 24, CornerRadius = new CornerRadius(3), Child = contents, VerticalAlignment = VerticalAlignment.Center };
        field.SetResourceReference(Border.BackgroundProperty, "TabRenameBackgroundBrush");
        return field;
    }

    private void FinishTabRename(bool commit, bool focusTerminal = true, bool rebuild = true)
    {
        if (_renamingTab is not { } tab) return;
        if (commit && _tabs.Contains(tab))
        {
            var value = new string(_tabRenameDraft.Where(c => !char.IsControl(c)).ToArray()).Trim();
            tab.CustomTitle = value.Length == 0 ? null : value;
        }
        _renamingTab = null; _tabRenameEditor = null; _tabRenameDraft = "";
        if (rebuild) RebuildTabStrip();
        if (focusTerminal) ActiveView.Focus();
    }

    private bool HandleTabRenameKey(KeyEventArgs e)
    {
        if (_renamingTab is null || _tabRenameEditor?.IsKeyboardFocusWithin != true) return false;
        if (e.Key == Key.Enter) { FinishTabRename(true); e.Handled = true; return true; }
        if (e.Key == Key.Escape) { FinishTabRename(false); e.Handled = true; return true; }
        // Text-editing keys and shortcuts belong to the editor, not the shell/window.
        return true;
    }
}
