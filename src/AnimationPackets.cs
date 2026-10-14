using System.Buffers.Binary;
using System.Text;

namespace CustomCursorLoader;

internal sealed record AnimationPacket(byte[] Bytes, int X, int Y, int Width, int Height, int Delay, int Dispose, bool Blend);

// 仅拆分容器；PNG/WebP 像素解码继续使用 Godot。处置、混合及时间单位遵循对应格式规范。
internal static class AnimationPackets
{
    public const int MaxFrames = 120;
    public static void CheckBudget(int width, int height, int count)
    {
        if (count < 1 || count > MaxFrames || (long)width * height * count * 4 > 128 * 1024 * 1024)
            throw new InvalidDataException("动画最多 120 帧，解码像素预算最多 128 MiB。");
    }

    public static List<AnimationPacket> Parse(byte[] bytes)
        => CursorFile.Format(bytes) == CursorFormat.Png ? Png(bytes) : Webp(bytes);

    private static List<AnimationPacket> Png(byte[] bytes)
    {
        var (width, height) = CursorFile.Dimensions(bytes);
        byte[] header = bytes[16..29];
        var shared = new List<(string Type, byte[] Data)>();
        var result = new List<AnimationPacket>();
        byte[]? control = null;
        var compressed = new MemoryStream();
        int declared = 0, sequence = 0;
        bool ended = false;
        void Finish()
        {
            if (control == null) return;
            if (compressed.Length == 0) throw new InvalidDataException("APNG 缺少帧数据。");
            int w = Read32(control.AsSpan(4)), h = Read32(control.AsSpan(8));
            int x = Read32(control.AsSpan(12)), y = Read32(control.AsSpan(16));
            CheckRect(x, y, w, h, width, height);
            if (control[24] > 2 || control[25] > 1) throw new InvalidDataException("APNG 帧模式无效。");
            byte[] ihdr = (byte[])header.Clone(); Write32(ihdr, w); Write32(ihdr.AsSpan(4), h);
            using var png = new MemoryStream(); png.Write(bytes, 0, 8); Chunk(png, "IHDR", ihdr);
            foreach (var (type, data) in shared) Chunk(png, type, data);
            Chunk(png, "IDAT", compressed.ToArray()); Chunk(png, "IEND", []);
            int num = BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(20)), den = BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(22));
            int delay = Math.Max(20, (int)Math.Round(num * 1000.0 / (den == 0 ? 100 : den)));
            result.Add(new(png.ToArray(), x, y, w, h, delay, control[24], control[25] == 1));
            CheckBudget(width, height, result.Count); compressed.SetLength(0);
        }
        using (compressed)
        {
            int offset = 8;
            while (offset + 12 <= bytes.Length)
            {
                int length = Read32(bytes.AsSpan(offset));
                if (length > bytes.Length - offset - 12) throw new InvalidDataException("PNG 数据块截断。");
                string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                ReadOnlySpan<byte> data = bytes.AsSpan(offset + 8, length);
                if (Crc(bytes.AsSpan(offset + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length)))
                    throw new InvalidDataException("PNG 数据块校验失败。");
                switch (type)
                {
                    case "acTL":
                        if (length != 8 || declared != 0) throw new InvalidDataException("APNG 动画头无效。");
                        declared = Read32(data); CheckBudget(width, height, declared); break;
                    case "fcTL":
                        if (declared == 0 || length != 26 || Read32(data) != sequence++) throw new InvalidDataException("APNG 帧顺序无效。");
                        Finish(); control = data.ToArray(); break;
                    case "fdAT":
                        if (control == null || length < 4 || Read32(data) != sequence++) throw new InvalidDataException("APNG 帧数据顺序无效。");
                        compressed.Write(data[4..]); break;
                    case "IDAT": if (control != null) compressed.Write(data); break;
                    case "PLTE": case "tRNS": shared.Add((type, data.ToArray())); break;
                    case "IEND": Finish(); ended = true; break;
                }
                offset += length + 12;
                if (ended) break;
            }
            if (!ended || (declared != 0 && result.Count != declared)) throw new InvalidDataException("PNG 动画帧数或结束块无效。");
        }
        if (declared == 0) result.Add(new(bytes, 0, 0, width, height, 100, 0, false));
        return result;
    }

    private static List<AnimationPacket> Webp(byte[] bytes)
    {
        var (width, height) = CursorFile.Dimensions(bytes);
        if ((long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) + 8 != bytes.Length)
            throw new InvalidDataException("WebP 容器长度无效。");
        var result = new List<AnimationPacket>();
        bool animated = bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8) && (bytes[20] & 2) != 0;
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4)));
            if (length > bytes.Length - offset - 8) throw new InvalidDataException("WebP 数据块截断。");
            if (animated && bytes.AsSpan(offset, 4).SequenceEqual("ANMF"u8))
            {
                ReadOnlySpan<byte> data = bytes.AsSpan(offset + 8, length);
                if (length < 16) throw new InvalidDataException("WebP 帧头截断。");
                int x = CursorFile.U24(data) * 2, y = CursorFile.U24(data[3..]) * 2;
                int w = CursorFile.U24(data[6..]) + 1, h = CursorFile.U24(data[9..]) + 1;
                CheckRect(x, y, w, h, width, height);
                // 加入 VP8X 以支持含 ALPH 的有损透明帧；移除动画标记。
                byte[] frame = new byte[30 + length - 16];
                "RIFF"u8.CopyTo(frame); BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint)frame.Length - 8);
                "WEBPVP8X"u8.CopyTo(frame.AsSpan(8)); BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16), 10);
                frame[20] = 16; Put24(frame.AsSpan(24), w - 1); Put24(frame.AsSpan(27), h - 1);
                data[16..].CopyTo(frame.AsSpan(30));
                result.Add(new(frame, x, y, w, h, Math.Max(20, CursorFile.U24(data[12..])), data[15] & 1, (data[15] & 2) == 0));
                CheckBudget(width, height, result.Count);
            }
            offset += 8 + length + (length & 1);
        }
        if (offset != bytes.Length || (animated && result.Count == 0)) throw new InvalidDataException("WebP 动画数据无效。");
        if (!animated) result.Add(new(bytes, 0, 0, width, height, 100, 0, false));
        return result;
    }

    private static void CheckRect(int x, int y, int width, int height, int canvasWidth, int canvasHeight)
    {
        if (width < 1 || height < 1 || x < 0 || y < 0 || width > canvasWidth || height > canvasHeight || x > canvasWidth - width || y > canvasHeight - height)
            throw new InvalidDataException("动画帧超出画布。");
    }
    private static int Read32(ReadOnlySpan<byte> bytes) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes));
    private static void Write32(Span<byte> bytes, int value) => BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
    private static void Put24(Span<byte> bytes, int value) { bytes[0] = (byte)value; bytes[1] = (byte)(value >> 8); bytes[2] = (byte)(value >> 16); }
    private static void Chunk(Stream stream, string type, byte[] data)
    {
        byte[] chunk = new byte[data.Length + 12]; Write32(chunk, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4); data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length + 8), Crc(chunk.AsSpan(4, data.Length + 4))); stream.Write(chunk);
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320); }
        return ~crc;
    }
}
