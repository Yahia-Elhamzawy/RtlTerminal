using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RtlTerminal;

public enum ProfileIconKind { CommandPrompt, PowerShell, Linux }

public static class ProfileIcons
{
    private static readonly Dictionary<ProfileIconKind, ImageSource> Cache = [];
    public static ImageSource Get(ProfileIconKind profile)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(profile, out var cached)) return cached;
            var executable = Path.Combine(Environment.SystemDirectory, profile switch
            {
                ProfileIconKind.PowerShell => @"WindowsPowerShell\v1.0\powershell.exe",
                ProfileIconKind.Linux => "wsl.exe",
                _ => "cmd.exe"
            });
            var icon = ReadNativeIcon(executable) ?? Fallback(profile);
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
    private static ImageSource Fallback(ProfileIconKind kind)
    {
        var drawing = new DrawingGroup();
        void Shape(string geometry, Brush fill, Pen? pen = null) => drawing.Children.Add(new GeometryDrawing(fill, pen, Geometry.Parse(geometry)));
        if (kind == ProfileIconKind.Linux)
        {
            Shape("M8,0 C3,0 3,5 3,8 L1,13 L5,15 L11,15 L15,13 L13,8 C13,5 13,0 8,0 Z", Brushes.Black);
            Shape("M8,5 C4,5 4,9 4,12 C4,15 12,15 12,12 C12,9 12,5 8,5 Z", Brushes.White);
            Shape("M4,13 L0,15 L6,16 L8,14 L10,16 L16,15 L12,13 Z M5,5 L8,8 L11,5 L8,4 Z", Brushes.Goldenrod);
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
