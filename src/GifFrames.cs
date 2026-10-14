using System.Runtime.InteropServices;
using Godot;

namespace CustomCursorLoader;

// GIF 使用 Windows 自带 GDI+，不要求玩家安装或下载解码器。
internal static class GifFrames
{
    public static void Read(string path, bool firstOnly, Action<Image, int> accept)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("GIF 导入目前支持 Windows。");
        var startup = new Startup { Version = 1, SuppressExternalCodecs = 1 };
        Check(GdiplusStartup(out nint token, ref startup, 0));
        nint image = 0;
        try
        {
            Check(GdipLoadImageFromFile(path, out image));
            Check(GdipGetImageWidth(image, out uint width)); Check(GdipGetImageHeight(image, out uint height));
            Guid dimension = new("6aedbd6d-3fb5-418a-83a6-7f45229dc872");
            Check(GdipImageGetFrameCount(image, ref dimension, out uint count));
            if (width > 2048 || height > 2048) throw new InvalidDataException("GIF 源图超过 2048 像素。");
            AnimationPackets.CheckBudget((int)width, (int)height, (int)count);
            int[] delays = Enumerable.Repeat(100, (int)count).ToArray();
            if (GdipGetPropertyItemSize(image, 0x5100, out uint propertySize) == 0 && propertySize < 65536)
            {
                nint property = Marshal.AllocHGlobal((int)propertySize);
                try
                {
                    Check(GdipGetPropertyItem(image, 0x5100, propertySize, property));
                    var item = Marshal.PtrToStructure<Property>(property);
                    for (int frame = 0; frame < count && frame * 4 + 4 <= item.Length; frame++)
                        delays[frame] = (int)Math.Clamp((long)(uint)Marshal.ReadInt32(item.Value, frame * 4) * 10, 20, int.MaxValue);
                }
                finally { Marshal.FreeHGlobal(property); }
            }
            for (uint frame = 0; frame < (firstOnly ? 1 : count); frame++)
            {
                Check(GdipImageSelectActiveFrame(image, ref dimension, frame));
                var rect = new Rectangle { Width = (int)width, Height = (int)height };
                Check(GdipBitmapLockBits(image, ref rect, 1, 0x26200A, out Bitmap pixels));
                byte[] rgba = new byte[checked((int)(width * height * 4))];
                try
                {
                    for (int y = 0; y < height; y++) Marshal.Copy(pixels.Scan0 + y * pixels.Stride, rgba, y * (int)width * 4, (int)width * 4);
                    for (int pixel = 0; pixel < rgba.Length; pixel += 4) (rgba[pixel], rgba[pixel + 2]) = (rgba[pixel + 2], rgba[pixel]);
                }
                finally { Check(GdipBitmapUnlockBits(image, ref pixels)); }
                accept(Image.CreateFromData((int)width, (int)height, false, Image.Format.Rgba8, rgba), delays[frame]);
            }
        }
        finally { if (image != 0) GdipDisposeImage(image); GdiplusShutdown(token); }
    }

    private static void Check(int status) { if (status != 0) throw new InvalidDataException($"Windows GIF 解码失败：{status}。"); }
    [StructLayout(LayoutKind.Sequential)] private struct Startup { public uint Version; public nint Callback; public int SuppressBackground, SuppressExternalCodecs; }
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap { public uint Width, Height; public int Stride, PixelFormat; public nint Scan0, Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Property { public uint Id, Length; public short Type; public nint Value; }
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdiplusStartup(out nint token, ref Startup input, nint output);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern void GdiplusShutdown(nint token);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int GdipLoadImageFromFile(string path, out nint image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetImageWidth(nint image, out uint width);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetImageHeight(nint image, out uint height);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipImageGetFrameCount(nint image, ref Guid dimension, out uint count);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipImageSelectActiveFrame(nint image, ref Guid dimension, uint frame);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetPropertyItemSize(nint image, uint id, out uint size);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetPropertyItem(nint image, uint id, uint size, nint item);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipBitmapLockBits(nint image, ref Rectangle rect, uint flags, int format, out Bitmap pixels);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipBitmapUnlockBits(nint image, ref Bitmap pixels);
}
