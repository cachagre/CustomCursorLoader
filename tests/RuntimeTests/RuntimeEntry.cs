using System.Reflection;
using Godot;
using HarmonyLib;
using CustomCursorLoader;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Saves.MapDrawing;

namespace RuntimeTests;

// 仅放进隔离的运行副本；绝不能安装到玩家的日常游戏 mods。
[ModInitializer(nameof(Initialize))]
public static class RuntimeEntry
{
    private static readonly Type Service = typeof(ModEntry).Assembly.GetType("CustomCursorLoader.CursorService")!;
    private static int _passed;
    private static bool _started;
    private static bool _ctrlDown, _altDown;
    private static object? Get(string name) => Service.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
    private static void Call(string name) => Service.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    private static object? GameGet(NCursorManager manager, string name) => typeof(NCursorManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager);
    private static void SendKey(Key key, bool pressed)
    {
        if (key == Key.Ctrl) _ctrlDown = pressed;
        if (key == Key.Alt) _altDown = pressed;
        using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed,
            CtrlPressed = _ctrlDown, AltPressed = _altDown };
        Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }
    private static void ResizeChord(Key key, bool pressed)
    {
        if (pressed) { SendKey(Key.Ctrl, true); SendKey(Key.Alt, true); SendKey(key, true); }
        else { SendKey(key, false); SendKey(Key.Alt, false); SendKey(Key.Ctrl, false); }
        Call("OnFrame");
    }
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        GD.Print("RUNTIME PASS ", ++_passed, " ", name);
    }

    public static void Initialize()
    {
        // 独立测试 DLL 会改配置并退出进程，必须先检查隔离标记。
        if (!OS.GetCmdlineUserArgs().Contains("--cursor-isolated-test"))
        {
            GD.PrintErr("RuntimeTests 未发现隔离标记，拒绝运行。");
            return;
        }
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame += () =>
            {
                if (_started) return;
                _started = true;
                _ = Run(tree);
            };
        }
    }

    private static async Task Run(SceneTree tree)
    {
        try
        {
            string expected = OS.GetEnvironment("CCL_ISOLATED_USER_ROOT");
            Assert(!string.IsNullOrEmpty(expected)
                && Path.GetFullPath(OS.GetUserDataDir()).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase), "用户数据隔离");
            if (OS.GetCmdlineUserArgs().Contains("--cursor-late-init-test"))
            {
                var existing = NGame.Instance?.CursorManager;
                Assert(existing != null && existing.IsInsideTree(), "晚初始化前光标节点已进入场景");
                Call("Shutdown");
                new Harmony("RuntimeTests.ResetCursorHooks").UnpatchAll("CustomCursorLoader.LocalCursor");
                typeof(ModEntry).GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
                ModEntry.Initialize();
                Assert(ReferenceEquals(Get("_manager"), existing), "晚初始化主动绑定已存在的光标节点");
            }
            ulong deadline = Time.GetTicksMsec() + 20000;
            while (Get("_manager") == null && Time.GetTicksMsec() < deadline)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var manager = Get("_manager") as NCursorManager ?? throw new Exception("光标管理器未绑定。");
            Assert(ModManager.GetLoadedMods().Any(m => m.manifest?.id == "CustomCursorLoader"), "真实 ModManager 加载 DLL");
            TestBundledGallery(new ConfigStore(ProjectSettings.GlobalizePath("user://CustomCursorLoader")));
            if (OS.GetCmdlineUserArgs().Contains("--cursor-frame-restart-test"))
            {
                var restartStore = new ConfigStore(ProjectSettings.GlobalizePath("user://CustomCursorLoader"));
                object animation = Get("_animation")!;
                var frames = (Image[])animation.GetType().GetProperty("Frames")!.GetValue(animation)!;
                Assert(restartStore.Read().CursorIdleFrame == 1 && ReferenceEquals(Get("_image"), frames[1]), "选定默认帧跨游戏进程保留");
                await NGame.Instance!.GameStartupComplete;
                while (NAssetLoader.Instance.IsProcessing()) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                GD.Print("RUNTIME FRAME RESTART PASS 默认帧选择跨进程保留"); tree.Quit(); return;
            }
            Assert(Get("_image") is Image initial && initial.GetWidth() == 32 && initial.GetPixel(31, 0).A == 0, "Godot 默认PNG解码与透明背景");
            ModEntry.Initialize(); ModEntry.Initialize();
            MethodInfo update = typeof(NCursorManager).GetMethod("UpdateCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert(Harmony.GetPatchInfo(update)!.Prefixes.Count(p => p.owner == "CustomCursorLoader.LocalCursor") == 1, "重复初始化不重复补丁");
            object? inspect = GameGet(manager, "_cursorInspect");
            var store = new ConfigStore(ProjectSettings.GlobalizePath("user://CustomCursorLoader"));
            string png = Path.Combine(store.Root, "cursors", "中文 空格.png");
            using (var source = Image.CreateEmpty(64, 32, false, Image.Format.Rgba8))
            {
                source.Fill(new Color(1, 0.5f, 0.25f, 0.75f));
                Assert(source.SavePng(png) == Error.Ok, "创建隔离PNG数据");
            }
            var config = new CursorConfig { CursorImage = "cursors/中文 空格.png", Scale = 1.5, Opacity = 0.4, HotspotX = 8, HotspotY = 4 };
            store.Save(config); Call("Reload");
            var loaded = (Image)Get("_image")!;
            Assert(loaded.GetWidth() == 96 && loaded.GetHeight() == 48, "C#加载器实际缩放");
            Assert(Math.Abs(loaded.GetPixel(20, 20).A - 0.3) < 0.01, "C#加载器实际透明度");
            Assert((Vector2)Get("_hotspot")! == new Vector2(12, 6), "热点随缩放换算");
            Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), loaded), "Harmony前缀实际执行");
            ResizeChord(Key.Equal, true);
            Assert(Get("_image") is Image grown && grown.GetWidth() == 104 && grown.GetHeight() == 52, "真实输入组合键放大8像素");
            Call("OnFrame");
            Assert(((Image)Get("_image")!).GetWidth() == 104, "按住组合键不连续重载");
            ResizeChord(Key.Equal, false);
            ResizeChord(Key.Minus, true); ResizeChord(Key.Minus, false);
            Assert(((Image)Get("_image")!).GetWidth() == 96 && store.Read() == config, "组合键缩小并持久化且保留热点透明度");
            SendKey(Key.Equal, true); Call("OnFrame"); SendKey(Key.Equal, false); Call("OnFrame");
            Assert(((Image)Get("_image")!).GetWidth() == 96, "没有CtrlAlt时不调整大小");
            loaded = (Image)Get("_image")!;
            Image retired = loaded;
            for (int i = 0; i < 20; i++) Call("Reload");
            loaded = (Image)Get("_image")!;
            Assert(!GodotObject.IsInstanceValid(retired), "连续重载释放旧Image包装器");
            using (var special = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8))
            {
                special.Fill(Colors.White);
                manager.OverrideCursor(special, special, Vector2.Zero);
                Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), special), "游戏特殊OverrideCursor优先");
                manager.StopOverridingCursor();
                Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), loaded), "结束特殊状态后恢复自定义");
            }
            using (var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true })
            {
                manager._Input(click);
                Assert((bool)GameGet(manager, "_isDown")!, "原版按下状态保留");
                click.Pressed = false; manager._Input(click);
                Assert(!(bool)GameGet(manager, "_isDown")!, "原版释放状态保留");
            }
            store.Save(config with { Enabled = false }); Call("Reload");
            Assert(Get("_image") == null && ReferenceEquals(GameGet(manager, "_lastSetCursor"), GameGet(manager, "_cursorNotTilted")), "禁用恢复原游戏Image");
            ResizeChord(Key.Equal, true); ResizeChord(Key.Equal, false);
            Assert(!store.Read().Enabled && Get("_image") == null, "缩放快捷键不会重新启用已禁用光标");
            store.Save(config with { CursorImage = "missing.png" }); Call("Reload");
            Assert(Get("_image") == null, "图片不存在安全回退");
            string broken = Path.Combine(store.Root, "cursors", "broken.png");
            byte[] header = File.ReadAllBytes(png)[..24]; File.WriteAllBytes(broken, header);
            store.Save(config with { CursorImage = "cursors/broken.png" }); Call("Reload");
            Assert(Get("_image") == null, "损坏PNG解码失败安全回退");
            header[16] = 127; File.WriteAllBytes(broken, header); Call("Reload");
            Assert(Get("_image") == null, "超大PNG在解码前回退");
            File.WriteAllText(store.ConfigPath, "{"); Call("Reload");
            Assert(Get("_image") == null, "配置格式错误安全回退");
            store.Save(config); Call("Reload");
            using (var locked = File.Open(png, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None))
            {
                Call("Reload"); Assert(Get("_image") == null, "PNG不可读安全回退");
            }
            Call("Reload");
            using (var locked = File.Open(store.ConfigPath, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None))
            {
                Call("Reload"); Assert(Get("_image") == null, "配置不可读安全回退");
            }
            store.Save(config); Call("Reload"); Call("RestoreAndSave");
            Assert(Get("_image") == null && !new ConfigStore(store.Root).Read().Enabled, "F9处理逻辑恢复并持久化");
            string largePng = Path.Combine(store.Root, "cursors", "大图缩放.png");
            using (var large = Image.CreateEmpty(1254, 1254, false, Image.Format.Rgba8))
            {
                large.Fill(Colors.LightBlue);
                Assert(large.SavePng(largePng) == Error.Ok, "创建大尺寸源图");
            }
            var largeConfig = new CursorConfig { CursorImage = "cursors/大图缩放.png", Scale = 64.0 / 1254, HotspotX = 242, HotspotY = 290 };
            store.Save(largeConfig); Call("Reload");
            ResizeChord(Key.Minus, true); ResizeChord(Key.Minus, false);
            Assert(((Image)Get("_image")!).GetWidth() == 56 && (Vector2)Get("_hotspot")! == new Vector2(11, 13), "大源图缩小到56且热点同步");
            for (int i = 0; i < 6; i++) { ResizeChord(Key.Minus, true); ResizeChord(Key.Minus, false); }
            Assert(((Image)Get("_image")!).GetWidth() == 16, "快捷缩放最小16像素");
            store.Save(largeConfig with { Scale = 256.0 / 1254 }); Call("Reload");
            ResizeChord(Key.Equal, true); ResizeChord(Key.Equal, false);
            Assert(((Image)Get("_image")!).GetWidth() == 256, "快捷缩放最大256像素");
            store.Save(config); Call("Reload");
            using (var locked = File.Open(store.ConfigPath, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None))
            {
                ResizeChord(Key.Equal, true); ResizeChord(Key.Equal, false);
                Assert(Get("_image") == null, "快捷缩放配置不可读时安全恢复");
            }
            store.Save(config with { Scale = 1 });
            ulong pollDeadline = Time.GetTicksMsec() + 2500;
            while (Get("_image") == null && Time.GetTicksMsec() < pollDeadline)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Assert(Get("_image") is Image polled && polled.GetWidth() == 64, "文件修改自动重载");
            Assert(ReferenceEquals(inspect, GameGet(manager, "_cursorInspect")), "检查光标资源未修改");
            ResizeChord(Key.Equal, true); ResizeChord(Key.Equal, false);
            Assert(((Image)Get("_image")!).GetWidth() == 72 && store.Read().Scale == 1.125, "最后一次快捷键大小保存为72像素供重启验证");
            // 等待原游戏后台资源加载结束后退出，避免打断预加载造成关闭阶段解析告警。
            await NGame.Instance!.GameStartupComplete;
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            while (NAssetLoader.Instance.IsProcessing())
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            TestMapCursors(manager, store);
            await TestGallery(store, tree);
            TestAnimation(manager, store);
            await TestFramePicker(store, tree);
            GD.Print("RUNTIME COMPLETE ", _passed, " checks passed; simulated inputs are not complete gameplay validation.");
            tree.Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr("RUNTIME FAIL ", error);
            tree.Quit(1);
        }
    }

    public class MapNetProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == "get_NetId" ? 123UL : throw new NotSupportedException(method?.Name);
    }

    private static void TestBundledGallery(ConfigStore store)
    {
        Type library = typeof(ModEntry).Assembly.GetType("CustomCursorLoader.CursorLibrary")!;
        string folder = Path.Combine(Path.GetDirectoryName(typeof(ModEntry).Assembly.Location)!, "gallery");
        string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)); }
        string[] bundled = Directory.GetFiles(folder);
        var hashes = bundled.Select(Hash).ToHashSet();
        Assert(bundled.Length == 128 && hashes.Count == 128, "发布包包含 128 张不重复的咖波图片");
        string[] local = (string[])library.GetMethod("List")!.Invoke(null, [store])!;
        Assert(hashes.IsSubsetOf(local.Select(Hash).ToHashSet()), "首次游戏启动自动导入全部随附图库且字节一致");
        byte[] config = File.ReadAllBytes(store.ConfigPath);
        Assert((int)library.GetMethod("ImportBundled")!.Invoke(null, [store, folder])! == 0 &&
            ((string[])library.GetMethod("List")!.Invoke(null, [store])!).Length == local.Length, "重复启动图库去重不新增文件");
        Assert(File.ReadAllBytes(store.ConfigPath).SequenceEqual(config), "随附图库导入不修改玩家配置");
    }

    private static async Task TestGallery(ConfigStore store, SceneTree tree)
    {
        CursorConfig before = store.Read();
        Type gallery = typeof(ModEntry).Assembly.GetType("CustomCursorLoader.CursorGallery")!;
        Type library = typeof(ModEntry).Assembly.GetType("CustomCursorLoader.CursorLibrary")!;
        object? Field(string name) => gallery.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        void Invoke(string name, params object[] args) => gallery.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        string importedPath = (string)library.GetMethod("Import")!.Invoke(null, [store, store.ResolveImagePath(before.CursorImage)])!;
        Invoke("Toggle");
        var window = (Window)Field("_window")!;
        var list = (ItemList)Field("_list")!;
        var enabled = (CheckButton)Field("_enabled")!;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Assert(window.Visible && list.ItemCount >= 2 && enabled.ButtonPressed, "游戏内图库打开并列出玩家图片");
        enabled.ButtonPressed = false;
        Assert(!store.Read().Enabled && Get("_image") == null, "图库关闭开关恢复原版且保存");
        enabled.ButtonPressed = true;
        Assert(store.Read().Enabled && Get("_image") is Image, "图库开关可重新启用F9关闭的光标");
        var mode = (OptionButton)Field("_mode")!;
        mode.Select(1); mode.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Assert(!((SpinBox)Field("_size")!).Editable, "地图图片大小跟随普通光标不独立覆盖");
        var paths = (string[])Field("_paths")!;
        int imported = Array.IndexOf(paths, importedPath);
        list.Select(imported); list.EmitSignal(ItemList.SignalName.ItemSelected, (long)imported);
        ((SpinBox)Field("_x")!).Value = 9; ((SpinBox)Field("_y")!).Value = 10;
        var save = window.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "保存并应用");
        save.EmitSignal(Button.SignalName.Pressed);
        CursorConfig pen = store.Read();
        Assert(pen.DrawHotspotX == 9 && pen.DrawHotspotY == 10 && pen.CursorImage == before.CursorImage && pen.Scale == before.Scale, "图库保存地图图与热点且保留普通配置");
        mode.Select(0); mode.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        list.Select(imported); list.EmitSignal(ItemList.SignalName.ItemSelected, (long)imported);
        ((SpinBox)Field("_size")!).Value = 80;
        ((SpinBox)Field("_x")!).Value = 6; ((SpinBox)Field("_y")!).Value = 7;
        save.EmitSignal(Button.SignalName.Pressed);
        Assert(Get("_image") is Image image && image.GetWidth() == 80 && store.Read().DrawCursorImage == pen.DrawCursorImage, "图库普通图大小立即应用且保留地图设置");
        ResizeChord(Key.Equal, true); ResizeChord(Key.Equal, false);
        Assert(((Image)Get("_image")!).GetWidth() == 88 && ((SpinBox)Field("_size")!).Value == 88, "图库打开期间缩放快捷键与尺寸显示同步");
        Call("RestoreAndSave"); Assert(!enabled.ButtonPressed, "图库打开期间F9与启用开关同步");
        enabled.ButtonPressed = true;
        if (DisplayServer.GetName() != "headless")
        {
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image capture = window.GetTexture().GetImage();
            string capturePath = OS.GetEnvironment("CCL_CAPTURE_PATH");
            if (!string.IsNullOrEmpty(capturePath)) Assert(capture.SavePng(capturePath) == Error.Ok, "实际选图窗口截图保存");
            var board = (Control)((TextureRect)Field("_preview")!).GetParent();
            using var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = board.Size / 2 };
            board.EmitSignal(Control.SignalName.GuiInput, click);
            Assert(((SpinBox)Field("_x")!).Value == 32 && ((SpinBox)Field("_y")!).Value == 16, "实际布局预览点击映射源图热点");
            window.Size = new Vector2I(640, 460);
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Assert(((ScrollContainer)board.GetParent().GetParent()).GetVScrollBar().Visible, "小窗口中编辑区可滚动避免控件截断");
        }
        ImageTexture preview = (ImageTexture)Field("_previewTexture")!;
        Invoke("Toggle");
        Assert(!window.Visible && !GodotObject.IsInstanceValid(preview), "关闭图库释放预览资源");
        SendKey(Key.F10, true); Call("OnFrame");
        Assert(window.Visible, "F10物理输入路径打开图库");
        Call("OnFrame"); Assert(window.Visible, "按住F10不重复开关");
        SendKey(Key.F10, false); Call("OnFrame");
        SendKey(Key.F10, true); Call("OnFrame"); SendKey(Key.F10, false); Call("OnFrame");
        Assert(!window.Visible, "再次F10关闭图库");
        File.WriteAllText(store.ConfigPath, "{"); Call("Reload"); Invoke("Toggle");
        Assert(window.Visible, "坏JSON仍可打开图库恢复配置");
        window.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "使用原版 / 默认箭头").EmitSignal(Button.SignalName.Pressed);
        Assert(store.Read().CursorImage == "cursors/default.png" && File.ReadAllText(store.ConfigPath + ".bak") == "{", "图库默认按钮恢复坏配置并保留备份");
        Invoke("Toggle");
        store.Save(before); Call("Reload");
    }

    private static void TestAnimation(NCursorManager manager, ConfigStore store)
    {
        CursorConfig before = store.Read();
        string fixtures = OS.GetEnvironment("CCL_ANIMATION_FIXTURES");
        Type animationType = typeof(ModEntry).Assembly.GetType("CustomCursorLoader.CursorAnimation")!;
        Image[] Frames(object asset) => (Image[])animationType.GetProperty("Frames")!.GetValue(asset)!;
        int[] Delays(object asset) => (int[])animationType.GetProperty("Delays")!.GetValue(asset)!;
        void Mouse(MouseButton button, bool pressed)
        {
            using var input = new InputEventMouseButton { ButtonIndex = button, Pressed = pressed };
            Input.ParseInputEvent(input); Input.FlushBufferedEvents(); Call("OnFrame");
        }
        void Advance()
        {
            Service.GetField("_animationStarted", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Time.GetTicksMsec() - 65);
            Call("OnFrame");
        }
        foreach (string extension in new[] { "png", "webp", "gif" })
        {
            string relative = "cursors/animation." + extension;
            File.Copy(Path.Combine(fixtures, "two-frame." + extension), store.ResolveImagePath(relative), true);
            var config = new CursorConfig { CursorImage = relative, Scale = 2, HotspotX = 4, HotspotY = 5 };
            store.Save(config); Call("Reload");
            object asset = Get("_animation") ?? throw new Exception("动画加载失败 " + extension);
            Image[] frames = Frames(asset); int[] delays = Delays(asset);
            Assert(frames.Length == 2 && delays.SequenceEqual(new[] { 50, 100 }), extension + " 两帧与原始延时解码");
            Assert(frames[0].GetPixel(16, 16).R > 0.9 && frames[1].GetPixel(16, 16).G > 0.9 && frames[0].GetPixel(0, 0).A == 0, extension + " 全帧像素颜色与透明背景");
            Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), frames[0]) && (Vector2)Get("_hotspot")! == new Vector2(8, 10), "平时首帧静态与统一热点 " + extension);
            Mouse(MouseButton.Left, true); Advance();
            Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), frames[0]), "左键保持首帧且原版输入仍收到 " + extension);
            Mouse(MouseButton.Left, false); Mouse(MouseButton.Right, true); Advance();
            Assert((bool)GameGet(manager, "_isDown")! && ReferenceEquals(GameGet(manager, "_lastSetCursor"), frames[1]), "按住右键播放且不吞原游戏输入 " + extension);
            Mouse(MouseButton.Right, false);
            Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), frames[0]), "松开右键立即回到首帧 " + extension);
            store.Save(config with { CursorIdleFrame = 1 }); Call("Reload");
            frames = Frames(Get("_animation")!);
            Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), frames[1]) && ReferenceEquals(Get("_image"), frames[1]), "选定第二帧作为默认光标 " + extension);
            Mouse(MouseButton.Right, true);
            Service.GetField("_animationStarted", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Time.GetTicksMsec()); Call("OnFrame");
            Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), frames[0]), "右键仍从原动画开头播放 " + extension);
            Mouse(MouseButton.Right, false);
            Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), frames[1]) && (Vector2)Get("_hotspot")! == new Vector2(8, 10), "松开回选定帧且热点不变 " + extension);
            store.Save(config with { CursorIdleFrame = 999 }); Call("Reload");
            frames = Frames(Get("_animation")!);
            Assert(ReferenceEquals(Get("_image"), frames[1]), "越界默认帧限于实际有效范围 " + extension);
            Mouse(MouseButton.Right, true); Advance();
            using (var special = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8))
            {
                special.Fill(Colors.White); manager.OverrideCursor(special, special, Vector2.Zero); Call("OnFrame");
                Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), special), "右键动画期间其他特殊光标优先 " + extension);
                manager.StopOverridingCursor();
            }
            Input.MouseMode = Input.MouseModeEnum.Hidden; Call("OnFrame");
            if (DisplayServer.GetName() == "headless") GD.Print("RUNTIME SKIP headless 不支持鼠标隐藏，需窗口测试 " + extension);
            else Assert(Get("_playingAnimation") == null && Input.MouseMode == Input.MouseModeEnum.Hidden, "手柄隐藏期间动画停止且不修改隐藏模式 " + extension);
            Input.MouseMode = Input.MouseModeEnum.Visible; Mouse(MouseButton.Right, false);
            Image retired = frames[1]; Call("Reload");
            Assert(!GodotObject.IsInstanceValid(retired), "重载释放所有动画帧 " + extension);
            Mouse(MouseButton.Right, true); Advance(); Call("RestoreAndSave");
            Assert(Get("_activeAnimation") == null && Get("_animation") == null && !store.Read().Enabled, "F9停止动画并恢复原版 " + extension);
            Mouse(MouseButton.Right, false);
        }
        string assets = OS.GetEnvironment("CCL_ANIMATION_TEST_ASSETS");
        store.Save(before with { DrawCursorImage = "cursors/animation.png", DrawIdleFrame = 1, DrawHotspotX = 4, DrawHotspotY = 5,
            EraseCursorImage = "cursors/animation.webp", EraseIdleFrame = 1, EraseHotspotX = 4, EraseHotspotY = 5 }); Call("Reload");
        manager.OverrideCursor((Image)Get("_drawPressed")!, (Image)Get("_drawIdle")!, Vector2.Zero);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), Frames(Get("_drawAnimation")!)[1]), "地图画笔使用自己选定的默认帧");
        object drawing = Get("_drawAnimation")!; Mouse(MouseButton.Right, true); Advance();
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), Frames(drawing)[1]), "地图画笔状态支持右键动画");
        manager.OverrideCursor((Image)Get("_erasePressed")!, (Image)Get("_eraseIdle")!, Vector2.Zero); Advance();
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), Frames(Get("_eraseAnimation")!)[1]), "地图橡皮擦状态切换到自己的动画");
        Mouse(MouseButton.Right, false);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), Frames(Get("_eraseAnimation")!)[1]), "地图动画松开后恢复该模式选定帧");
        manager.StopOverridingCursor();
        if (!string.IsNullOrEmpty(assets))
        {
            int decoded = 0;
            foreach (string path in Directory.GetFiles(assets).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".webp" or ".gif"))
            {
                object asset = animationType.GetMethod("Load")!.Invoke(null, [store, new CursorConfig { CursorImage = path }, 64, false])!;
                try { Assert(Frames(asset).Length > 0 && Frames(asset).All(frame => Math.Max(frame.GetWidth(), frame.GetHeight()) == 64), "图库原图完整解码 " + Path.GetFileName(path)); decoded++; }
                finally { ((IDisposable)asset).Dispose(); }
            }
            GD.Print("ANIMATION LIBRARY DECODED ", decoded, " original files.");
        }
        store.Save(before); Call("Reload");
    }

    private static async Task TestFramePicker(ConfigStore store, SceneTree tree)
    {
        CursorConfig before = store.Read();
        Type gallery = typeof(ModEntry).Assembly.GetType("CustomCursorLoader.CursorGallery")!;
        object? Field(string name) => gallery.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        void Invoke(string name) => gallery.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        var source = store.ResolveImagePath("cursors/animation.png");
        byte[] original = File.ReadAllBytes(source);
        Invoke("Toggle");
        var window = (Window)Field("_window")!;
        var mode = (OptionButton)Field("_mode")!;
        var list = (ItemList)Field("_list")!;
        var frame = (SpinBox)Field("_frame")!;
        var slider = (HSlider)Field("_frameSlider")!;
        void SelectMode(int value, string relative)
        {
            mode.Select(value); mode.EmitSignal(OptionButton.SignalName.ItemSelected, (long)value);
            string[] paths = (string[])Field("_paths")!;
            int index = Array.IndexOf(paths, store.ResolveImagePath(relative));
            list.Select(index); list.EmitSignal(ItemList.SignalName.ItemSelected, (long)index);
        }
        var save = window.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "保存并应用");
        SelectMode(0, "cursors/animation.png");
        Assert(frame.MaxValue == 2 && slider.Editable && frame.Value == 1, "动态图选帧范围与原始帧数一致");
        slider.Value = 2;
        using (Image preview = ((TextureRect)Field("_preview")!).Texture.GetImage())
            Assert(frame.Value == 2 && preview.GetPixel(112, 112).G > 0.9 && store.Read() == before, "滑条逐帧预览且未保存时不改配置");
        frame.Value = 1;
        Assert(slider.Value == 1, "帧号与滑条双向同步");
        frame.Value = 2; ((SpinBox)Field("_x")!).Value = 4; ((SpinBox)Field("_y")!).Value = 5;
        save.EmitSignal(Button.SignalName.Pressed);
        var applied = (Image)Get("_image")!;
        Assert(store.Read().CursorIdleFrame == 1 && applied.GetPixel(applied.GetWidth() / 2, applied.GetHeight() / 2).G > 0.9, "保存选定帧立即作为普通光标默认外观");
        if (DisplayServer.GetName() != "headless")
        {
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string capture = OS.GetEnvironment("CCL_CAPTURE_PATH");
            using Image screenshot = window.GetTexture().GetImage();
            if (!string.IsNullOrEmpty(capture)) Assert(screenshot.SavePng(Path.Combine(Path.GetDirectoryName(capture)!, "frame-picker.png")) == Error.Ok, "实际选帧窗口截图保存");
        }
        Image retired = ((Image[])Field("_previewAnimation")!.GetType().GetProperty("Frames")!.GetValue(Field("_previewAnimation"))!)[1];
        Invoke("Toggle");
        Assert(!GodotObject.IsInstanceValid(retired), "关闭图库释放所有预览动画帧");
        Invoke("Toggle");
        Assert(frame.Value == 2 && slider.Value == 2, "重新打开图库恢复已保存的默认帧");
        SelectMode(1, "cursors/animation.png"); slider.Value = 2; save.EmitSignal(Button.SignalName.Pressed);
        SelectMode(2, "cursors/animation.webp"); slider.Value = 2; save.EmitSignal(Button.SignalName.Pressed);
        Assert(store.Read().CursorIdleFrame == 1 && store.Read().DrawIdleFrame == 1 && store.Read().EraseIdleFrame == 1, "图库三种模式独立保存默认帧");
        SelectMode(0, before.CursorImage);
        Assert(frame.Value == 1 && !frame.Editable && !slider.Editable, "切换静态图时禁用选帧并重置预览");
        Assert(File.ReadAllBytes(source).SequenceEqual(original), "选帧不修改原始动态图");
        Invoke("Toggle"); store.Save(before); Call("Reload");
    }

    private static void TestMapCursors(NCursorManager manager, ConfigStore store)
    {
        CursorConfig before = store.Read();
        string drawPath = Path.Combine(store.Root, "cursors", "map-draw.png");
        string erasePath = Path.Combine(store.Root, "cursors", "map-erase.png");
        string assets = OS.GetEnvironment("CCL_MAP_TEST_ASSETS");
        foreach (var (target, name, color) in new[] { (drawPath, "capoo-map-draw.png", Colors.LightBlue), (erasePath, "capoo-map-erase.png", Colors.HotPink) })
        {
            if (!string.IsNullOrEmpty(assets)) File.Copy(Path.Combine(assets, name), target);
            else
            {
                using var fixture = Image.CreateEmpty(80, 60, false, Image.Format.Rgba8);
                fixture.Fill(color); fixture.FillRect(new Rect2I(0, 0, 8, 8), Colors.Transparent);
                fixture.SavePng(target);
            }
        }
        var config = before with { DrawCursorImage = "cursors/map-draw.png", DrawHotspotX = 16, DrawHotspotY = 24,
            EraseCursorImage = "cursors/map-erase.png", EraseHotspotX = 18, EraseHotspotY = 26 };
        store.Save(config); Call("Reload");
        int size = Math.Max(((Image)Get("_image")!).GetWidth(), ((Image)Get("_image")!).GetHeight());
        var draw = (Image)Get("_drawImage")!;
        var erase = (Image)Get("_eraseImage")!;
        Assert(Math.Max(draw.GetWidth(), draw.GetHeight()) == size && Math.Max(erase.GetWidth(), erase.GetHeight()) == size, "画笔及橡皮擦大小跟随普通光标");
        Assert(draw.GetPixel(0, 0).A == 0 && erase.GetPixel(0, 0).A == 0, "两张地图PNG透明背景实际解码");

        using var map = new NMapDrawings();
        Type mapType = typeof(NMapDrawings);
        Type stateType = mapType.GetNestedType("DrawingState", BindingFlags.NonPublic)!;
        object state = Activator.CreateInstance(stateType, nonPublic: true)!;
        stateType.GetField("playerId")!.SetValue(state, 123UL);
        ((System.Collections.IList)mapType.GetField("_drawingStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(map)!).Add(state);
        mapType.GetField("_netService", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(map, DispatchProxy.Create<INetGameService, MapNetProxy>());
        mapType.GetField("_cursorManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(map, manager);
        MethodInfo updateMap = mapType.GetMethod("UpdateLocalCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Mode(DrawingMode mode, DrawingMode? temporary = null)
        {
            stateType.GetField("drawingMode")!.SetValue(state, mode);
            stateType.GetField("overrideDrawingMode")!.SetValue(state, temporary);
            updateMap.Invoke(map, null);
        }
        Assert(Harmony.GetPatchInfo(updateMap)!.Prefixes.Count(p => p.owner == "CustomCursorLoader.LocalCursor") == 1, "地图原方法只有一个识别补丁");
        Mode(DrawingMode.Drawing);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), draw), "真实地图画图状态使用左图");
        object quill = GameGet(manager, "_overriddenCursorNotTilted")!;
        Assert(ReferenceEquals(quill, Get("_drawIdle")), "原地图覆盖资源仍由游戏持有");
        using (var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true })
        {
            manager._Input(click);
            Assert((bool)GameGet(manager, "_isDown")! && ReferenceEquals(GameGet(manager, "_lastSetCursor"), draw), "画图按下状态与自定义光标共存");
            click.Pressed = false; manager._Input(click);
        }
        Mode(DrawingMode.Erasing);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), erase), "真实地图擦除状态使用右图");
        object eraser = GameGet(manager, "_overriddenCursorNotTilted")!;
        Mode(DrawingMode.None);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), Get("_image")), "结束绘图恢复普通咖波");
        Mode(DrawingMode.None, DrawingMode.Drawing);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), draw), "原游戏临时绘图模式也使用画笔图");
        Mode(DrawingMode.None, DrawingMode.Erasing);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), erase), "原游戏临时擦除模式也使用橡皮擦图");

        Mode(DrawingMode.Drawing);
        ResizeChord(Key.Equal, true); ResizeChord(Key.Equal, false);
        draw = (Image)Get("_drawImage")!;
        Assert(Math.Max(draw.GetWidth(), draw.GetHeight()) == size + 8 && ReferenceEquals(GameGet(manager, "_lastSetCursor"), draw), "地图绘图期间快捷放大8像素");
        Assert(store.Read().DrawCursorImage == config.DrawCursorImage && store.Read().EraseCursorImage == config.EraseCursorImage, "缩放保存保留两张地图图片路径");
        Image retiredDraw = draw, retiredErase = (Image)Get("_eraseImage")!;
        Call("Reload");
        Assert(!GodotObject.IsInstanceValid(retiredDraw) && !GodotObject.IsInstanceValid(retiredErase)
            && ReferenceEquals(GameGet(manager, "_lastSetCursor"), Get("_drawImage")), "绘图期间重载替换并释放旧地图Image");
        Call("RestoreAndSave");
        Assert(Get("_drawImage") == null && Get("_eraseImage") == null && ReferenceEquals(GameGet(manager, "_lastSetCursor"), quill), "绘图期间F9立即恢复原羽毛画笔");
        Mode(DrawingMode.Erasing);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), eraser), "禁用后擦除恢复原橡皮擦");

        store.Save(config with { DrawCursorImage = "missing-pen.png" }); Call("Reload");
        Mode(DrawingMode.Drawing);
        Assert(Get("_image") != null && Get("_drawImage") == null && ReferenceEquals(GameGet(manager, "_lastSetCursor"), quill), "画笔文件缺失只回退画笔模式");
        Mode(DrawingMode.Erasing);
        Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), Get("_eraseImage")), "画笔文件错误时橡皮擦仍可使用");
        using (var unrelated = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8))
        {
            unrelated.Fill(Colors.White);
            manager.OverrideCursor(unrelated, unrelated, Vector2.Zero);
            Assert(ReferenceEquals(GameGet(manager, "_lastSetCursor"), unrelated), "地图资源识别不替换其他特殊覆盖光标");
        }
        Mode(DrawingMode.None);
        store.Save(before); Call("Reload");
    }
}
