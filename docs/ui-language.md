# Interface language

Use **Help → LANGUAGE** to choose **English**, **فارسی**, or **العربية**. English is the default when no preference is saved. The chosen item is checked, and the setting is retained for the next launch.

Only application text is localized. Changing language never sets FlowDirection, changes control alignment, reverses menu/tab order, changes the formatting culture, or translates the terminal's output, shell commands, paths, font/theme names, macro names or entered values. Existing layout directions are preserved, including the guide's established layout. Operating-system dialogs and errors retain their Windows-provided text where applicable.

Main-window XAML strings use live bindings through UiText; programmatically created menus and modal dialogs use the selected language when created. Keep UI source strings in the shared English/Persian/Arabic catalog and pass format arguments separately. Do not apply localization to user data or terminal buffers.

The per-user selection is stored as UiLanguage in HKCU\Software\RtlTerminal. Tests switch all three languages without persisting their selections, verify menu checkmarks and live text changes, compare control directions/alignment and menu order, and check that input values and formatting culture remain unchanged. Persian and Arabic dialog previews are generated in artifacts/render-checks.
