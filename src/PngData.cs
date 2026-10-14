using System.Buffers.Binary;

namespace CustomCursorLoader;

// 在 Godot 解码前检查大小，防止巨大 IHDR 导致不必要的内存分配。
public static class PngData
{
    public const int MaxSourceSide = 2048;
    public const int MaxFileBytes = 8 * 1024 * 1024;

    public static byte[] Read(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("只支持 .png 文件。");
        using var file = File.OpenRead(path);
        if (file.Length > MaxFileBytes) throw new InvalidDataException("PNG 超过 8 MiB。");
        Span<byte> header = stackalloc byte[24];
        file.ReadExactly(header);
        Dimensions(header);
        byte[] bytes = new byte[(int)file.Length];
        file.Position = 0;
        file.ReadExactly(bytes);
        return bytes;
    }

    public static (int Width, int Height) Dimensions(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13
            || !bytes[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("不是合法的 PNG 头部。");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        if (width == 0 || height == 0 || width > MaxSourceSide || height > MaxSourceSide)
            throw new InvalidDataException($"PNG 尺寸必须在 1–{MaxSourceSide} 像素内。");
        return ((int)width, (int)height);
    }
}

public readonly record struct CursorGeometry(int Width, int Height, int HotspotX, int HotspotY)
{
    public static CursorConfig Resize(CursorConfig config, int sourceWidth, int sourceHeight, int delta)
    {
        config = config.Normalize();
        CursorGeometry current = Calculate(sourceWidth, sourceHeight, config);
        if (delta < 0 && Math.Max(current.Width, current.Height) <= 16) return config;
        int side = Math.Clamp(Math.Max(current.Width, current.Height) + delta, 16, 256);
        return config with { Scale = (double)side / Math.Max(sourceWidth, sourceHeight) };
    }

    public static CursorGeometry Calculate(int width, int height, CursorConfig config)
    {
        if (width < 1 || height < 1 || width > PngData.MaxSourceSide || height > PngData.MaxSourceSide)
            throw new ArgumentOutOfRangeException(nameof(width));
        config = config.Normalize();
        double factor = Math.Min(config.Scale, 256.0 / Math.Max(width, height));
        int scaledWidth = Math.Clamp((int)Math.Round(width * factor), 1, 256);
        int scaledHeight = Math.Clamp((int)Math.Round(height * factor), 1, 256);
        int x = (int)Math.Round(Math.Clamp(config.HotspotX, 0, width - 1) * scaledWidth / width);
        int y = (int)Math.Round(Math.Clamp(config.HotspotY, 0, height - 1) * scaledHeight / height);
        return new(scaledWidth, scaledHeight, Math.Clamp(x, 0, scaledWidth - 1), Math.Clamp(y, 0, scaledHeight - 1));
    }
}
