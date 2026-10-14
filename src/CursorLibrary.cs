using System.Security.Cryptography;

namespace CustomCursorLoader;

internal static class CursorLibrary
{
    public static bool Supports(string path) => CursorFile.Supports(path);

    public static string[] List(ConfigStore store) => new[] { "cursors", "gallery" }
        .Select(folder => Path.Combine(store.Root, folder)).Where(Directory.Exists)
        .SelectMany(folder => Directory.EnumerateFiles(folder)).Where(Supports)
        .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string Import(ConfigStore store, string source)
    {
        byte[] bytes = CursorFile.Read(source);
        string folder = Path.Combine(store.Root, "gallery");
        Directory.CreateDirectory(folder);
        string name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string extension = CursorFile.Format(bytes) switch { CursorFormat.Png => ".png", CursorFormat.Gif => ".gif", _ => ".webp" };
        string? existing = Directory.EnumerateFiles(folder, "*_" + name + extension).FirstOrDefault();
        if (existing != null) return existing;
        string label = Path.GetFileNameWithoutExtension(source);
        if (label.Length > 40) label = label[..40];
        string target = Path.Combine(folder, label + "_" + name + extension);
        if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
        return target;
    }

    public static int ImportBundled(ConfigStore store, string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in List(store))
        {
            using var file = File.OpenRead(path);
            existing.Add(Convert.ToHexString(SHA256.HashData(file)));
        }
        int added = 0;
        foreach (string path in Directory.EnumerateFiles(folder).Where(Supports))
        {
            string hash = Convert.ToHexString(SHA256.HashData(CursorFile.Read(path)));
            if (!existing.Add(hash)) continue;
            Import(store, path); added++;
        }
        return added;
    }

    public static string Label(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int split = name.LastIndexOf('_');
        return split >= 0 && name.Length - split == 65 && name[(split + 1)..].All(Uri.IsHexDigit) ? name[..split] : name;
    }

    public static CursorConfig Select(CursorConfig config, int mode, string path, int width, int height, double x, double y, int size, int idleFrame = 0)
        => mode switch
        {
            0 => config with { CursorImage = path, CursorIdleFrame = idleFrame, HotspotX = x, HotspotY = y, Scale = (double)size / Math.Max(width, height) },
            1 => config with { DrawCursorImage = path, DrawIdleFrame = idleFrame, DrawHotspotX = x, DrawHotspotY = y },
            2 => config with { EraseCursorImage = path, EraseIdleFrame = idleFrame, EraseHotspotX = x, EraseHotspotY = y },
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}
