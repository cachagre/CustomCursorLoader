using Godot;

namespace CustomCursorLoader;

internal sealed class CursorAnimation(Image[] frames, int[] delays, Vector2 hotspot, int sourceWidth, int sourceHeight) : IDisposable
{
    public Image[] Frames { get; } = frames;
    public int[] Delays { get; } = delays;
    public Vector2 Hotspot { get; } = hotspot;
    public int SourceWidth { get; } = sourceWidth;
    public int SourceHeight { get; } = sourceHeight;
    public int IdleFrame { get; init; }
    public Image StaticImage => Frames[IdleFrame];
    private readonly ulong _duration = (ulong)delays.Sum(delay => (long)delay);

    public Image At(ulong elapsed)
    {
        elapsed %= _duration;
        for (int index = 0; index < Delays.Length; index++)
        {
            if (elapsed < (ulong)Delays[index]) return Frames[index];
            elapsed -= (ulong)Delays[index];
        }
        return Frames[0];
    }

    public static CursorAnimation Load(ConfigStore store, CursorConfig config, int? longestSide = null, bool firstOnly = false)
    {
        string path = store.ResolveImagePath(config.CursorImage);
        byte[] bytes = CursorFile.Read(path);
        var (width, height) = CursorFile.Dimensions(bytes);
        if (longestSide is int side) config = config with { Scale = (double)side / Math.Max(width, height) };
        var frames = new List<Image>(); var delays = new List<int>();
        CursorGeometry geometry = CursorGeometry.Calculate(width, height, config);
        void Accept(Image image, int delay)
        {
            try { CursorLoader.Process(image, geometry, config.Opacity); frames.Add(image); delays.Add(delay); }
            catch { image.Dispose(); throw; }
        }
        try
        {
            if (CursorFile.Format(bytes) == CursorFormat.Gif) { CursorFile.GifCount(bytes); GifFrames.Read(path, firstOnly, Accept); }
            else
            {
                List<AnimationPacket> packets = AnimationPackets.Parse(bytes);
                byte[] canvas = new byte[checked(width * height * 4)];
                foreach (AnimationPacket packet in firstOnly ? packets.Take(1) : packets)
                {
                    using var image = new Image();
                    Error error = CursorFile.Format(packet.Bytes) == CursorFormat.Png ? image.LoadPngFromBuffer(packet.Bytes) : image.LoadWebpFromBuffer(packet.Bytes);
                    if (error != Error.Ok || image.IsEmpty() || image.GetWidth() != packet.Width || image.GetHeight() != packet.Height)
                        throw new InvalidDataException($"Godot 无法解码图片帧：{error}。");
                    image.Convert(Image.Format.Rgba8);
                    byte[]? previous = packet.Dispose == 2 ? (byte[])canvas.Clone() : null;
                    Compose(canvas, width, image.GetData(), packet);
                    Accept(Image.CreateFromData(width, height, false, Image.Format.Rgba8, canvas), packet.Delay);
                    if (packet.Dispose == 1)
                        for (int row = packet.Y; row < packet.Y + packet.Height; row++) Array.Clear(canvas, (row * width + packet.X) * 4, packet.Width * 4);
                    else if (previous != null) canvas = previous;
                }
            }
            ModLog.Info($"已加载 {config.CursorImage}；{width}×{height} → {geometry.Width}×{geometry.Height}；{frames.Count} 帧；热点 {geometry.HotspotX},{geometry.HotspotY}。");
            return new(frames.ToArray(), delays.ToArray(), new Vector2(geometry.HotspotX, geometry.HotspotY), width, height)
                { IdleFrame = Math.Clamp(config.CursorIdleFrame, 0, frames.Count - 1) };
        }
        catch { foreach (Image image in frames) image.Dispose(); throw; }
    }

    private static void Compose(byte[] canvas, int width, byte[] source, AnimationPacket frame)
    {
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                int src = (y * frame.Width + x) * 4, dst = ((y + frame.Y) * width + x + frame.X) * 4;
                if (!frame.Blend) { Array.Copy(source, src, canvas, dst, 4); continue; }
                int alpha = source[src + 3], background = canvas[dst + 3] * (255 - alpha), combined = alpha * 255 + background;
                for (int channel = 0; channel < 3; channel++)
                    canvas[dst + channel] = combined == 0 ? (byte)0 : (byte)((source[src + channel] * alpha * 255 + canvas[dst + channel] * background + combined / 2) / combined);
                canvas[dst + 3] = (byte)((combined + 127) / 255);
            }
    }

    public void Dispose() { foreach (Image image in Frames) image.Dispose(); }
}
