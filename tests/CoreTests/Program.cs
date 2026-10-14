using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using CustomCursorLoader;

int passed = 0;
void Check(string name, Action action)
{
    action();
    Console.WriteLine("PASS " + name);
    passed++;
}
void Equal<T>(T actual, T expected) { if (!Equals(actual, expected)) throw new Exception($"{actual} != {expected}"); }
void Reject(Action action)
{
    try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException) { return; }
    throw new Exception("预期拒绝此输入。");
}
byte[] Header(uint width, uint height)
{
    byte[] data = new byte[24];
    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(data, 0);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), 13);
    "IHDR"u8.CopyTo(data.AsSpan(12));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), width);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20, 4), height);
    return data;
}
string testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "work", "core-tests"));
string root = Path.Combine(testRoot, "CustomCursorLoaderTests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var store = new ConfigStore(root);
    Check("配置保存与新实例重启读取", () => {
        var config = new CursorConfig { CursorImage = "cursors/中文 空格.png", Scale = 1.5, Opacity = 0.4, HotspotX = 7, HotspotY = 8 };
        store.Save(config);
        Equal(new ConfigStore(root).Read(), config);
    });
    Check("原配置备份", () => {
        CursorConfig before = store.Read();
        store.Save(before with { Enabled = false });
        Equal(JsonSerializer.Deserialize<CursorConfig>(File.ReadAllText(store.ConfigPath + ".bak")), before);
        Equal(store.Read().Enabled, false);
    });
    Check("画笔和橡皮擦图片配置持久化", () => {
        var config = new CursorConfig { DrawCursorImage = "cursors/画笔.png", DrawHotspotX = 15, DrawHotspotY = 23,
            EraseCursorImage = "cursors/橡皮擦.png", EraseHotspotX = 18, EraseHotspotY = 25 };
        store.Save(config);
        Equal(new ConfigStore(root).Read(), config);
    });
    Check("旧配置没有帧号时仍使用第一帧", () => {
        File.WriteAllText(store.ConfigPath, "{\"cursorImage\":\"old.png\"}");
        CursorConfig config = store.Read();
        Equal(config.CursorIdleFrame, 0); Equal(config.DrawIdleFrame, 0); Equal(config.EraseIdleFrame, 0);
    });
    Check("三种光标默认帧独立持久化", () => {
        var config = new CursorConfig { CursorIdleFrame = 3, DrawIdleFrame = 7, EraseIdleFrame = 9 };
        store.Save(config); Equal(new ConfigStore(root).Read(), config);
    });
    Check("负帧号规范化为首帧", () => {
        var config = new CursorConfig { CursorIdleFrame = -1, DrawIdleFrame = -2, EraseIdleFrame = -3 }.Normalize();
        Equal(config.CursorIdleFrame, 0); Equal(config.DrawIdleFrame, 0); Equal(config.EraseIdleFrame, 0);
    });
    Check("选帧只修改当前模式并保留大小热点", () => {
        var config = new CursorConfig { CursorIdleFrame = 3, DrawIdleFrame = 7, EraseIdleFrame = 9 };
        CursorConfig normal = CursorLibrary.Select(config, 0, config.CursorImage, 32, 32, config.HotspotX, config.HotspotY, 32, 5);
        Equal(normal, config with { CursorIdleFrame = 5 });
        CursorConfig draw = CursorLibrary.Select(config, 1, "draw.png", 32, 32, 8, 9, 64, 2);
        Equal(draw, config with { DrawCursorImage = "draw.png", DrawHotspotX = 8, DrawHotspotY = 9, DrawIdleFrame = 2 });
        CursorConfig erase = CursorLibrary.Select(config, 2, "erase.png", 32, 32, 6, 7, 64, 4);
        Equal(erase, config with { EraseCursorImage = "erase.png", EraseHotspotX = 6, EraseHotspotY = 7, EraseIdleFrame = 4 });
    });
    Check("换图片默认重置为新图第一帧", () => {
        CursorConfig config = CursorLibrary.Select(new() { CursorIdleFrame = 8, DrawIdleFrame = 3 }, 0, "new.png", 32, 32, 3, 3, 32);
        Equal(config.CursorIdleFrame, 0); Equal(config.DrawIdleFrame, 3);
    });
    Check("地图光标空路径与非法热点规范化", () => {
        var config = new CursorConfig { DrawCursorImage = "  ", EraseCursorImage = " 橡皮擦.png ",
            DrawHotspotX = -2, DrawHotspotY = double.NaN, EraseHotspotX = double.PositiveInfinity, EraseHotspotY = -1 }.Normalize();
        Equal(config.DrawCursorImage, ""); Equal(config.EraseCursorImage, "橡皮擦.png");
        Equal(config.DrawHotspotX, 0.0); Equal(config.DrawHotspotY, 0.0);
        Equal(config.EraseHotspotX, 0.0); Equal(config.EraseHotspotY, 0.0);
    });
    Check("UTF-8 BOM 配置读取保留中文路径与地图设置", () => {
        var config = new CursorConfig { CursorImage = "cursors/中文 空格.png", Scale = 0.5,
            DrawCursorImage = "cursors/画笔.png", EraseCursorImage = "cursors/橡皮擦.png" };
        File.WriteAllBytes(store.ConfigPath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config))]);
        Equal(store.Read(), config);
    });
    Check("BOM 配置保存后可重读且保留原文件备份", () => {
        byte[] before = File.ReadAllBytes(store.ConfigPath);
        CursorConfig config = store.Read() with { Enabled = false };
        store.Save(config);
        Equal(new ConfigStore(root).Read(), config);
        Equal(File.ReadAllBytes(store.ConfigPath + ".bak").SequenceEqual(before), true);
    });
    Check("UTF-8 BOM 后的非法 JSON 仍拒绝", () => {
        File.WriteAllBytes(store.ConfigPath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{")]);
        Reject(() => store.Read());
    });
    Check("中文相对路径", () => Equal(store.ResolveImagePath("cursors/中文 空格.png"), Path.Combine(root, "cursors", "中文 空格.png")));
    Check("绝对路径", () => Equal(store.ResolveImagePath(Path.Combine(root, "猫.png")), Path.Combine(root, "猫.png")));
    Check("拒绝 URI", () => Reject(() => store.ResolveImagePath("https://example.org/a.png")));
    Check("拒绝空路径", () => Reject(() => store.ResolveImagePath("")));
    foreach (string bad in new[] { "{", "null", "[]", "{\"scale\":\"bad\"}", "{\"enabled\":1}" })
        Check("非法配置 " + bad, () => { File.WriteAllText(store.ConfigPath, bad); Reject(() => store.Read()); });
    Check("JSON数值溢出使用安全默认值", () => { File.WriteAllText(store.ConfigPath, "{\"scale\":1e999}"); Equal(store.Read().Scale, 1.0); });
    Check("配置不可读取", () => { using var locked = File.Open(store.ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); Reject(() => store.Read()); });
    Check("拒绝过大配置", () => { File.WriteAllText(store.ConfigPath, new string(' ', 65537)); Reject(() => store.Read()); });
    Check("缺失配置", () => { File.Delete(store.ConfigPath); Reject(() => store.Read()); });
    Check("非法参数限幅", () => {
        var c = new CursorConfig { Scale = -1, Opacity = 5, HotspotX = -7, HotspotY = double.PositiveInfinity }.Normalize();
        Equal(c.Scale, CursorConfig.MinScale); Equal(c.Opacity, 1.0); Equal(c.HotspotX, 0.0); Equal(c.HotspotY, 0.0);
    });
    Check("非有限参数默认值", () => {
        var c = new CursorConfig { Scale = double.NaN, Opacity = double.NaN }.Normalize();
        Equal(c.Scale, 1.0); Equal(c.Opacity, 1.0);
    });
    Check("2倍缩放与热点", () => Equal(CursorGeometry.Calculate(32, 32, new() { Scale = 2, HotspotX = 5, HotspotY = 7 }), new CursorGeometry(64, 64, 10, 14)));
    Check("保持比例限制256", () => Equal(CursorGeometry.Calculate(512, 256, new() { Scale = 16, HotspotX = 256, HotspotY = 128 }), new CursorGeometry(256, 128, 128, 64)));
    Check("热点右下越界限幅", () => Equal(CursorGeometry.Calculate(32, 32, new() { Scale = 0.5, HotspotX = 999, HotspotY = 999 }), new CursorGeometry(16, 16, 15, 15)));
    Check("最小1像素", () => Equal(CursorGeometry.Calculate(1, 1, new() { Scale = 0.05 }), new CursorGeometry(1, 1, 0, 0)));
    Check("宽图窄边仍合法", () => Equal(CursorGeometry.Calculate(2048, 1, new()), new CursorGeometry(256, 1, 0, 0)));
    Check("大源图可以缩小到56像素", () => Equal(CursorGeometry.Calculate(1254, 1254, new() { Scale = 56.0 / 1254 }).Width, 56));
    Check("快捷缩小大图并保留配置", () => {
        var c = new CursorConfig { CursorImage = "cursors/咖波.png", Scale = 64.0 / 1254, Opacity = 0.6, HotspotX = 242, HotspotY = 290 };
        var resized = CursorGeometry.Resize(c, 1254, 1254, -8);
        Equal(CursorGeometry.Calculate(1254, 1254, resized), new CursorGeometry(56, 56, 11, 13));
        Equal(resized with { Scale = c.Scale }, c);
    });
    Check("快捷缩放下限16", () => Equal(CursorGeometry.Calculate(1254, 1254, CursorGeometry.Resize(new() { Scale = 16.0 / 1254 }, 1254, 1254, -8)).Width, 16));
    Check("快捷缩放上限256", () => Equal(CursorGeometry.Calculate(1254, 1254, CursorGeometry.Resize(new() { Scale = 256.0 / 1254 }, 1254, 1254, 8)).Width, 256));
    Check("被上限截断的倍率仍可缩小", () => Equal(CursorGeometry.Calculate(1254, 1254, CursorGeometry.Resize(new() { Scale = 16 }, 1254, 1254, -8)).Width, 248));
    Check("非正方形快捷缩放保持比例", () => Equal(CursorGeometry.Calculate(64, 32, CursorGeometry.Resize(new() { Scale = 1.5, HotspotX = 0, HotspotY = 0 }, 64, 32, 8)), new CursorGeometry(104, 52, 0, 0)));
    Check("小源图可以放大到256", () => Equal(CursorGeometry.Calculate(1, 1, CursorGeometry.Resize(new() { Scale = 248 }, 1, 1, 8)).Width, 256));
    Check("现有小于16的光标缩小时不会变大", () => Equal(CursorGeometry.Calculate(8, 8, CursorGeometry.Resize(new(), 8, 8, -8)).Width, 8));
    Check("合法PNG头", () => Equal(PngData.Dimensions(Header(32, 64)), (32, 64)));
    foreach (var dimensions in new[] { (0u, 32u), (32u, 0u), (2049u, 32u), (uint.MaxValue, uint.MaxValue) })
        Check("拒绝PNG尺寸 " + dimensions, () => Reject(() => PngData.Dimensions(Header(dimensions.Item1, dimensions.Item2))));
    Check("拒绝伪PNG", () => Reject(() => PngData.Dimensions(new byte[24])));
    Check("缺少文件", () => Reject(() => PngData.Read(Path.Combine(root, "missing.png"))));
    string png = Path.Combine(root, "中文 空格.PNG");
    Check("大写扩展名与中文PNG读取", () => { File.WriteAllBytes(png, Header(32, 32)); Equal(PngData.Read(png).Length, 24); });
    Check("截断PNG", () => { File.WriteAllBytes(png, new byte[8]); Reject(() => PngData.Read(png)); });
    Check("读取前拒绝巨大IHDR", () => { File.WriteAllBytes(png, Header(uint.MaxValue, 1)); Reject(() => PngData.Read(png)); });
    Check("图库导入去重且保留源文件", () => {
        byte[] bytes = Header(32, 32); File.WriteAllBytes(png, bytes);
        string imported = CursorLibrary.Import(store, png);
        Equal(CursorLibrary.Import(store, png), imported);
        Equal(Directory.GetFiles(Path.Combine(root, "gallery")).Length, 1);
        Equal(File.ReadAllBytes(png).SequenceEqual(bytes), true);
        Equal(File.ReadAllBytes(imported).SequenceEqual(bytes), true);
        Equal(CursorLibrary.List(store).Contains(imported), true);
        Equal(CursorLibrary.Label(imported), "中文 空格");
    });
    var bundledStore = new ConfigStore(Path.Combine(root, "bundled-user"));
    string bundle = Path.Combine(root, "bundle");
    Check("没有随附图库的旧包仍可加载", () => Equal(CursorLibrary.ImportBundled(bundledStore, bundle), 0));
    Directory.CreateDirectory(bundle);
    File.WriteAllBytes(Path.Combine(bundle, "咖波.png"), Header(64, 32));
    File.WriteAllBytes(Path.Combine(bundle, "重复.png"), Header(64, 32));
    File.WriteAllText(Path.Combine(bundle, "说明.txt"), "只导入图片");
    Check("首次安装导入随附图库并按内容去重", () => {
        Equal(CursorLibrary.ImportBundled(bundledStore, bundle), 1);
        Equal(CursorLibrary.List(bundledStore).Length, 1);
        Equal(File.ReadAllBytes(CursorLibrary.List(bundledStore)[0]).SequenceEqual(Header(64, 32)), true);
    });
    Check("重复启动不产生重复图库文件", () => {
        Equal(CursorLibrary.ImportBundled(bundledStore, bundle), 0);
        Equal(CursorLibrary.List(bundledStore).Length, 1);
    });
    Directory.CreateDirectory(Path.Combine(bundledStore.Root, "cursors"));
    File.WriteAllBytes(bundledStore.ResolveImagePath("cursors/我的图片.png"), Header(48, 48));
    File.WriteAllBytes(Path.Combine(bundle, "同一图片新名字.png"), Header(48, 48));
    Check("随附图与玩家 cursors 中同内容图片不重复导入", () => Equal(CursorLibrary.ImportBundled(bundledStore, bundle), 0));
    Check("随附图库不改玩家选图帧号和配置", () => {
        bundledStore.Save(new CursorConfig { Enabled = false, CursorImage = "cursors/我的图片.png", CursorIdleFrame = 3,
            Scale = 0.5, DrawCursorImage = "pen.png", DrawIdleFrame = 2, EraseIdleFrame = 7 });
        byte[] configBytes = File.ReadAllBytes(bundledStore.ConfigPath);
        CursorLibrary.ImportBundled(bundledStore, bundle);
        Equal(File.ReadAllBytes(bundledStore.ConfigPath).SequenceEqual(configBytes), true);
        Equal(File.Exists(bundledStore.ConfigPath + ".bak"), false);
    });
    Check("图库更新只添加新内容且保留用户文件与原始素材", () => {
        byte[] selected = File.ReadAllBytes(bundledStore.ResolveImagePath("cursors/我的图片.png"));
        File.WriteAllBytes(Path.Combine(bundle, "咖波.png"), Header(96, 64));
        Equal(CursorLibrary.ImportBundled(bundledStore, bundle), 1);
        Equal(CursorLibrary.List(bundledStore).Length, 3);
        Equal(File.ReadAllBytes(bundledStore.ResolveImagePath("cursors/我的图片.png")).SequenceEqual(selected), true);
        Equal(File.ReadAllBytes(Path.Combine(bundle, "咖波.png")).SequenceEqual(Header(96, 64)), true);
    });
    Check("图库选择普通图保留地图配置并换算大小", () => {
        var config = new CursorConfig { DrawCursorImage = "pen.png", EraseCursorImage = "eraser.png", Opacity = 0.4, Enabled = false };
        CursorConfig next = CursorLibrary.Select(config, 0, "gallery/cat.png", 128, 64, 8, 9, 72);
        Equal(next with { CursorImage = config.CursorImage, HotspotX = config.HotspotX, HotspotY = config.HotspotY, Scale = config.Scale }, config);
        Equal(CursorGeometry.Calculate(128, 64, next), new CursorGeometry(72, 36, 4, 5));
    });
    Check("图库选择地图图不覆盖普通配置", () => {
        var config = new CursorConfig { CursorImage = "paw.png", Scale = 0.5, HotspotX = 64, HotspotY = 22 };
        CursorConfig next = CursorLibrary.Select(config, 1, "pen.png", 128, 64, 9, 10, 72);
        Equal(next with { DrawCursorImage = "", DrawHotspotX = 0, DrawHotspotY = 0 }, config);
        Equal(next.DrawCursorImage, "pen.png"); Equal(next.DrawHotspotX, 9.0); Equal(next.DrawHotspotY, 10.0);
    });
    string fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../fixtures"));
    foreach (string extension in new[] { "png", "webp" })
        Check("动画容器尺寸和非均匀延时 " + extension, () => {
            byte[] bytes = CursorFile.Read(Path.Combine(fixtures, "two-frame." + extension));
            Equal(CursorFile.Dimensions(bytes), (16, 16));
            var frames = AnimationPackets.Parse(bytes);
            Equal(frames.Count, 2); Equal(frames[0].Delay, 50); Equal(frames[1].Delay, 100);
            Equal(frames[0].Width, extension == "png" ? 16 : 12);
            Equal(frames[1].Height, extension == "png" ? 16 : 12);
        });
    Check("扩展名伪装的动态图按实际格式识别", () => {
        byte[] bytes = File.ReadAllBytes(Path.Combine(fixtures, "two-frame.webp"));
        File.WriteAllBytes(png, bytes); Equal(CursorFile.Format(CursorFile.Read(png)), CursorFormat.Webp);
        string imported = CursorLibrary.Import(store, png); Equal(Path.GetExtension(imported), ".webp");
    });
    Check("动画文件损坏CRC拒绝", () => {
        byte[] bytes = File.ReadAllBytes(Path.Combine(fixtures, "two-frame.png"));
        bytes[29] ^= 1; Reject(() => AnimationPackets.Parse(bytes));
    });
    Check("动画WebP越界帧拒绝", () => {
        byte[] bytes = File.ReadAllBytes(Path.Combine(fixtures, "two-frame.webp"));
        int offset = Encoding.ASCII.GetString(bytes).IndexOf("ANMF", StringComparison.Ordinal);
        bytes[offset + 8] = 20; Reject(() => AnimationPackets.Parse(bytes));
    });
    Check("动画帧数预算拒绝", () => Reject(() => AnimationPackets.CheckBudget(128, 128, 121)));
    Check("动画解码像素预算拒绝", () => Reject(() => AnimationPackets.CheckBudget(2048, 2048, 120)));
    Check("GIF中文路径与原始尺寸", () => {
        byte[] bytes = File.ReadAllBytes(Path.Combine(fixtures, "two-frame.gif"));
        Equal(CursorFile.Dimensions(bytes), (16, 16)); Equal(CursorFile.Format(bytes), CursorFormat.Gif);
        Equal(CursorFile.GifCount(bytes), 2);
    });
    Check("GIF截断数据拒绝", () => Reject(() => CursorFile.GifCount(File.ReadAllBytes(Path.Combine(fixtures, "two-frame.gif"))[..^1])));
    Check("文件大小限制", () => { using (var f = File.Create(png)) f.SetLength(PngData.MaxFileBytes + 1); Reject(() => PngData.Read(png)); });
    Check("不读取脚本", () => Reject(() => PngData.Read(Path.Combine(root, "script.cs"))));
    Console.WriteLine($"完成：{passed} 项核心测试通过。PNG 完整解码及实际光标需 Godot/游戏测试。");
}
finally
{
    if (!Path.GetFullPath(root).StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("测试清理目录越界。");
    Directory.Delete(root, recursive: true);
}
