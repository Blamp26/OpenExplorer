using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenExplorer.ShellInterop;

public sealed class WindowsShellIconExtractor : IShellIconExtractor
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint ShgfiLargeIcon = 0;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint DiNormal = 0x3;
    private const int BiRgb = 0;

    public ShellIconImage Extract(string path, ShellIconKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var flags = ShgfiIcon | ShgfiLargeIcon;
        var attributes = kind == ShellIconKind.Directory ? FileAttributeDirectory : FileAttributeNormal;
        if (!File.Exists(path) && !Directory.Exists(path)) flags |= ShgfiUseFileAttributes;

        var info = new ShFileInfo();
        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == IntPtr.Zero || info.Icon == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not provide a shell icon.");

        try
        {
            return RenderIcon(info.Icon, 32);
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    private static ShellIconImage RenderIcon(IntPtr icon, int size)
    {
        var header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = size, Height = -size, Planes = 1, BitCount = 32, Compression = BiRgb };
        var bitmapInfo = new BitmapInfo { Header = header };
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            bitmap = CreateDibSection(dc, ref bitmapInfo, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var old = SelectObject(dc, bitmap);
            try
            {
                if (!DrawIconEx(dc, 0, 0, icon, size, size, 0, IntPtr.Zero, DiNormal))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var pixels = new byte[size * size * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                return new ShellIconImage(size, size, pixels);
            }
            finally { SelectObject(dc, old); }
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo { public IntPtr Icon; public int IconIndex; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? DisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string? TypeName; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true)] private static extern IntPtr CreateDibSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
