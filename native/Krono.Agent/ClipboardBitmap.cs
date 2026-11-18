using System.Runtime.InteropServices;

namespace Krono_Agent;

internal static class ClipboardBitmap
{
    const uint BitmapFormat = 2;
    internal sealed record Pixels(uint Width, uint Height, byte[] Data);

    internal static Pixels? Read()
    {
        if (!IsClipboardFormatAvailable(BitmapFormat) || !OpenClipboard(0)) return null;
        try {
            var handle = GetClipboardData(BitmapFormat);
            if (handle == 0 || GetObject(handle, Marshal.SizeOf<Bitmap>(), out var bitmap) == 0) return null;
            if (bitmap.Width is < 1 or > 4096 || bitmap.Height is < 1 or > 4096)
                throw new InvalidOperationException("Paste an image no larger than 4096 × 4096 pixels.");
            var info = new BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, BitsPerPixel = 32 };
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            var dc = GetDC(0);
            if (dc == 0) return null;
            try {
                return GetDIBits(dc, handle, 0, (uint)bitmap.Height, pixels, ref info, 0) == bitmap.Height
                    ? new((uint)bitmap.Width, (uint)bitmap.Height, pixels) : null;
            } finally { ReleaseDC(0, dc); }
        } finally { CloseClipboard(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPerPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitsPerPixel;
        public uint Compression, ImageSize;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint ColorsUsed, ImportantColors;
    }

    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] static extern int GetObject(nint handle, int bytes, out Bitmap bitmap);
    [DllImport("gdi32.dll")] static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] pixels, ref BitmapInfo info, uint usage);
}
