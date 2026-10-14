using System.Buffers.Binary;

namespace CustomCursorLoader;

internal enum CursorFormat { Png, Webp, Gif }

internal static class CursorFile
{
    public static bool Supports(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".apng" or ".webp" or ".gif";

    public static byte[] Read(string path)
    {
        if (!Supports(path)) throw new InvalidDataException("支持 PNG、APNG、WebP 和 GIF 图片。");
        using var file = File.OpenRead(path);
        if (file.Length > PngData.MaxFileBytes) throw new InvalidDataException("图片超过 8 MiB。");
        byte[] bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
        Dimensions(bytes);
        return bytes;
    }

    public static CursorFormat Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return CursorFormat.Png;
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return CursorFormat.Webp;
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return CursorFormat.Gif;
        throw new InvalidDataException("无法识别图片格式。");
    }

    public static (int Width, int Height) Dimensions(ReadOnlySpan<byte> bytes)
    {
        int width, height;
        switch (Format(bytes))
        {
            case CursorFormat.Png: return PngData.Dimensions(bytes);
            case CursorFormat.Gif when bytes.Length >= 13:
                width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..8]); height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..10]); break;
            case CursorFormat.Webp when bytes.Length >= 30:
                if (bytes[12..16].SequenceEqual("VP8X"u8)) { width = U24(bytes[24..]) + 1; height = U24(bytes[27..]) + 1; }
                else if (bytes[12..16].SequenceEqual("VP8L"u8) && bytes[20] == 0x2F)
                {
                    uint bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..25]);
                    width = (int)(bits & 0x3FFF) + 1; height = (int)((bits >> 14) & 0x3FFF) + 1;
                }
                else if (bytes[12..16].SequenceEqual("VP8 "u8) && bytes[23..26].SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
                { width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..28]) & 0x3FFF; height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..30]) & 0x3FFF; }
                else throw new InvalidDataException("WebP 头部无效。");
                break;
            default: throw new InvalidDataException("图片头部截断。");
        }
        if (width < 1 || height < 1 || width > PngData.MaxSourceSide || height > PngData.MaxSourceSide)
            throw new InvalidDataException("源图片每边必须在 1–2048 像素内。");
        return (width, height);
    }

    internal static int U24(ReadOnlySpan<byte> bytes) => bytes[0] | bytes[1] << 8 | bytes[2] << 16;

    internal static int GifCount(ReadOnlySpan<byte> bytes)
    {
        var (width, height) = Dimensions(bytes);
        int offset = 13 + ((bytes[10] & 128) == 0 ? 0 : 3 * (1 << ((bytes[10] & 7) + 1))), count = 0;
        while (offset < bytes.Length)
        {
            byte marker = bytes[offset++];
            if (marker == 0x3B) { AnimationPackets.CheckBudget(width, height, count); return count; }
            if (marker == 0x21) { if (offset >= bytes.Length) break; offset++; }
            else if (marker == 0x2C)
            {
                if (offset + 9 >= bytes.Length) break;
                byte packed = bytes[offset + 8]; offset += 9;
                if ((packed & 128) != 0) offset += 3 * (1 << ((packed & 7) + 1));
                offset++; count++; AnimationPackets.CheckBudget(width, height, count);
            }
            else throw new InvalidDataException("GIF 数据块无效。");
            while (offset < bytes.Length)
            {
                int length = bytes[offset++];
                if (length == 0) break;
                if (length > bytes.Length - offset) throw new InvalidDataException("GIF 数据块截断。");
                offset += length;
            }
        }
        throw new InvalidDataException("GIF 缺少结束块。");
    }
}
