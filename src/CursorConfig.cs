using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomCursorLoader;

public sealed record CursorConfig
{
    public const double MinScale = 1.0 / PngData.MaxSourceSide;
    public const double MaxScale = 256;
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
    [JsonPropertyName("cursorImage")] public string CursorImage { get; init; } = "cursors/default.png";
    [JsonPropertyName("cursorIdleFrame")] public int CursorIdleFrame { get; init; }
    [JsonPropertyName("scale")] public double Scale { get; init; } = 1;
    [JsonPropertyName("opacity")] public double Opacity { get; init; } = 1;
    // 热点使用原始 PNG 的像素坐标；缩放时一起换算，不移动真实鼠标。
    [JsonPropertyName("hotspotX")] public double HotspotX { get; init; } = 3;
    [JsonPropertyName("hotspotY")] public double HotspotY { get; init; } = 3;
    [JsonPropertyName("drawCursorImage")] public string DrawCursorImage { get; init; } = "";
    [JsonPropertyName("drawIdleFrame")] public int DrawIdleFrame { get; init; }
    [JsonPropertyName("drawHotspotX")] public double DrawHotspotX { get; init; }
    [JsonPropertyName("drawHotspotY")] public double DrawHotspotY { get; init; }
    [JsonPropertyName("eraseCursorImage")] public string EraseCursorImage { get; init; } = "";
    [JsonPropertyName("eraseIdleFrame")] public int EraseIdleFrame { get; init; }
    [JsonPropertyName("eraseHotspotX")] public double EraseHotspotX { get; init; }
    [JsonPropertyName("eraseHotspotY")] public double EraseHotspotY { get; init; }

    public CursorConfig Normalize() => this with
    {
        CursorImage = CursorImage?.Trim() ?? "",
        CursorIdleFrame = Math.Max(0, CursorIdleFrame),
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, MinScale, MaxScale) : 1,
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : 1,
        HotspotX = double.IsFinite(HotspotX) ? Math.Max(0, HotspotX) : 0,
        HotspotY = double.IsFinite(HotspotY) ? Math.Max(0, HotspotY) : 0,
        DrawCursorImage = DrawCursorImage?.Trim() ?? "",
        DrawIdleFrame = Math.Max(0, DrawIdleFrame),
        DrawHotspotX = double.IsFinite(DrawHotspotX) ? Math.Max(0, DrawHotspotX) : 0,
        DrawHotspotY = double.IsFinite(DrawHotspotY) ? Math.Max(0, DrawHotspotY) : 0,
        EraseCursorImage = EraseCursorImage?.Trim() ?? "",
        EraseIdleFrame = Math.Max(0, EraseIdleFrame),
        EraseHotspotX = double.IsFinite(EraseHotspotX) ? Math.Max(0, EraseHotspotX) : 0,
        EraseHotspotY = double.IsFinite(EraseHotspotY) ? Math.Max(0, EraseHotspotY) : 0
    };
}

public sealed class ConfigStore(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string ConfigPath => Path.Combine(Root, "config.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public CursorConfig Read()
    {
        using var file = File.OpenRead(ConfigPath);
        if (file.Length > 64 * 1024) throw new InvalidDataException("配置超过 64 KiB。");
        byte[] bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        ReadOnlySpan<byte> json = bytes;
        if (json.StartsWith("\uFEFF"u8)) json = json[3..];
        return (JsonSerializer.Deserialize<CursorConfig>(json, Options)
            ?? throw new InvalidDataException("配置不能是 null。")).Normalize();
    }

    public void Save(CursorConfig config)
    {
        Directory.CreateDirectory(Root);
        string temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config.Normalize(), Options));
        if (File.Exists(ConfigPath)) File.Copy(ConfigPath, ConfigPath + ".bak", overwrite: true);
        File.Move(temp, ConfigPath, overwrite: true);
    }

    public string ResolveImagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://", StringComparison.Ordinal))
            throw new InvalidDataException("cursorImage 必须是本地文件路径。");
        return Path.GetFullPath(path, Root);
    }
}
