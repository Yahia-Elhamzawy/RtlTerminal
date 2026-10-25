using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RtlTerminal;

public enum ProfileIconKind { CommandPrompt, PowerShell, Linux, PowerShell7, GitBash }

public static class ProfileIcons
{
    private static readonly Dictionary<ProfileIconKind, ImageSource> Cache = [];
    public static ImageSource Get(ProfileIconKind profile)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(profile, out var cached)) return cached;
            string? executable = profile switch
            {
                ProfileIconKind.PowerShell7 => ShellProfiles.PowerShell7Path,
                ProfileIconKind.GitBash => ShellProfiles.GitBashPath,
                ProfileIconKind.PowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                ProfileIconKind.Linux => Path.Combine(Environment.SystemDirectory, "wsl.exe"),
                _ => Path.Combine(Environment.SystemDirectory, "cmd.exe")
            };
            var icon = (executable is not null ? ReadNativeIcon(executable) : null) ?? Fallback(profile);
            Cache[profile] = icon; return icon;
        }
    }
    private static ImageSource? ReadNativeIcon(string path)
    {
        if (!File.Exists(path)) return null;
        var info = new ShellFileInfo();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100) == IntPtr.Zero || info.Icon == IntPtr.Zero)
            return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); return image;
        }
        finally { DestroyIcon(info.Icon); }
    }
    public static ImageSource Fallback(ProfileIconKind kind)
    {
        var drawing = new DrawingGroup();
        void Shape(string geometry, Brush fill, Pen? pen = null) => drawing.Children.Add(new GeometryDrawing(fill, pen, Geometry.Parse(geometry)));
        if (kind == ProfileIconKind.Linux)
        {
            Shape("M8,0 C3,0 3,5 3,8 L1,13 L5,15 L11,15 L15,13 L13,8 C13,5 13,0 8,0 Z", Brushes.Black);
            Shape("M8,5 C4,5 4,9 4,12 C4,15 12,15 12,12 C12,9 12,5 8,5 Z", Brushes.White);
            Shape("M4,13 L0,15 L6,16 L8,14 L10,16 L16,15 L12,13 Z M5,5 L8,8 L11,5 L8,4 Z", Brushes.Goldenrod);
        }
        else if (kind == ProfileIconKind.PowerShell7)
        {
            var bgBrush = new SolidColorBrush(Color.FromRgb(24, 30, 42));
            bgBrush.Freeze();
            var fgPen = new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 1.4);
            fgPen.Freeze();
            Shape("M1,2 L15,2 L15,14 L1,14 Z", bgBrush);
            Shape("M4,5 L7,8 L4,11 M8,11 L12,11", Brushes.Transparent, fgPen);
        }
        else if (kind == ProfileIconKind.GitBash)
        {
            var gitOrange = new SolidColorBrush(Color.FromRgb(240, 80, 50));
            gitOrange.Freeze();
            var branchPen = new Pen(Brushes.White, 1.2);
            branchPen.Freeze();
            Shape("M8,1 L15,8 L8,15 L1,8 Z", gitOrange);
            Shape("M5.5,11 L5.5,5 M5.5,9 C5.5,7.5 7,6.5 10.5,6.5", Brushes.Transparent, branchPen);
            Shape("M4.5,11 A1,1 0 1 0 6.5,11 A1,1 0 1 0 4.5,11 M4.5,5 A1,1 0 1 0 6.5,5 A1,1 0 1 0 4.5,5 M9.5,6.5 A1,1 0 1 0 11.5,6.5 A1,1 0 1 0 9.5,6.5", Brushes.White);
        }
        else
        {
            Shape("M1,2 L15,2 L15,14 L1,14 Z", kind == ProfileIconKind.PowerShell ? Brushes.RoyalBlue : Brushes.DimGray);
            Shape("M4,5 L7,8 L4,11 M8,11 L12,11", Brushes.Transparent, new Pen(Brushes.White, 1.4));
        }
        drawing.Freeze(); var result = new DrawingImage(drawing); result.Freeze(); return result;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
