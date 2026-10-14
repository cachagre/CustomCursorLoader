using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace CustomCursorLoader;

// 只有 Godot 主线程访问此类。每帧复用动画 Image；解码只在加载/重载时发生。
internal static class CursorService
{
    private static FieldInfo _lastCursor = null!;
    private static MethodInfo _update = null!;
    private static ConfigStore? _store;
    private static CursorConfig _config = new();
    private static Image? _image;
    private static Vector2 _hotspot;
    private static Image? _drawImage, _eraseImage;
    private static CursorAnimation? _animation, _drawAnimation, _eraseAnimation, _activeAnimation, _playingAnimation;
    private static ulong _animationStarted;
    private static Vector2 _drawHotspot, _eraseHotspot;
    // 只借用原游戏资源来识别地图状态；不修改或释放这些资源。
    private static Image? _drawIdle, _drawPressed, _eraseIdle, _erasePressed;
    private static NCursorManager? _manager;
    private static SceneTree? _tree;
    private static bool _reloadHeld, _restoreHeld, _growHeld, _shrinkHeld, _galleryHeld;
    private static ulong _nextPoll;
    private static (long Time, long Length) _stamp;

    internal static ConfigStore? Store => _store;
    internal static SceneTree? Tree => _tree;
    internal static CursorConfig Config => _config;
    internal static void ApplyConfig(CursorConfig config)
    {
        _store!.Save(config); Reload();
        if (config.Enabled && _image == null) throw new InvalidDataException("配置已保存，但图片加载失败，暂时使用原版；请查看日志或选择其他图片。");
    }

    public static void ValidateFields(Type type)
    {
        foreach (var (name, fieldType) in new[]
        {
            ("_lastSetCursor", typeof(Image)),
            ("_overriddenCursorTilted", typeof(Image)),
            ("_overriddenCursorNotTilted", typeof(Image))
        })
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(type.FullName, name);
            if (field.FieldType != fieldType) throw new InvalidOperationException($"游戏字段 {name} 类型发生变化。");
            if (name == "_lastSetCursor") _lastCursor = field;
        }
        _update = type.GetMethod("UpdateCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    public static void Attach(NCursorManager manager)
    {
        try
        {
            if (ReferenceEquals(_manager, manager)) return;
            _manager = manager;
            manager.TreeExiting += () => { if (ReferenceEquals(_manager, manager)) _manager = null; };
            if (_tree == null)
            {
                _tree = manager.GetTree();
                _tree.ProcessFrame += OnFrame;
                _tree.Root.TreeExiting += Shutdown;
                _store = new ConfigStore(ProjectSettings.GlobalizePath("user://CustomCursorLoader"));
                Directory.CreateDirectory(Path.Combine(_store.Root, "cursors"));
                string sample = Path.Combine(_store.Root, "cursors", "default.png");
                if (!File.Exists(sample))
                {
                    using Stream resource = typeof(ModEntry).Assembly.GetManifestResourceStream("CustomCursorLoader.default.png")!;
                    using var output = File.Create(sample);
                    resource.CopyTo(output);
                }
                if (!File.Exists(_store.ConfigPath)) _store.Save(new CursorConfig());
                try
                {
                    string folder = Path.Combine(Path.GetDirectoryName(typeof(ModEntry).Assembly.Location)!, "gallery");
                    int added = CursorLibrary.ImportBundled(_store, folder);
                    ModLog.Info($"随附图库新增 {added} 张（已按内容去重）。");
                }
                catch (IOException error) { ModLog.Info("随附图库导入失败：" + error.Message); }
                catch (UnauthorizedAccessException error) { ModLog.Info("随附图库导入失败：" + error.Message); }
                CursorGallery.Attach(_tree);
                ModLog.Info("配置目录：" + _store.Root);
                Reload();
            }
            else Refresh();
        }
        catch (Exception error) { Fail(error); }
    }

    public static bool BeforeUpdate(Image? tiltedOverride, Image? idleOverride, ref Image? lastCursor)
    {
        CursorAnimation? animation = _animation;
        if (tiltedOverride != null || idleOverride != null)
        {
            if (ReferenceEquals(tiltedOverride, _drawPressed) && ReferenceEquals(idleOverride, _drawIdle))
                animation = _drawAnimation;
            else if (ReferenceEquals(tiltedOverride, _erasePressed) && ReferenceEquals(idleOverride, _eraseIdle))
                animation = _eraseAnimation;
            else { _activeAnimation = _playingAnimation = null; return true; }
        }
        _activeAnimation = animation;
        if (animation == null) { _playingAnimation = null; return true; }
        try
        {
            Image image = SelectFrame(animation);
            // 完全沿用原游戏的隐藏逻辑；不更改 MouseMode 或输入事件。
            if (Input.MouseMode != Input.MouseModeEnum.Hidden && !ReferenceEquals(lastCursor, image))
            {
                Input.SetCustomMouseCursor(image, Input.CursorShape.Arrow, animation.Hotspot);
                lastCursor = image;
            }
            return false;
        }
        catch (Exception error)
        {
            lastCursor = null;
            Fail(error, refresh: false);
            return true;
        }
    }

    public static void CaptureMapCursors()
    {
        try
        {
            _drawIdle = PreloadManager.Cache.GetAsset<Image>("res://images/packed/common_ui/cursor_quill.png");
            _drawPressed = PreloadManager.Cache.GetAsset<Image>("res://images/packed/common_ui/cursor_quill_tilted.png");
            _eraseIdle = PreloadManager.Cache.GetAsset<Image>("res://images/packed/common_ui/cursor_eraser.png");
            _erasePressed = PreloadManager.Cache.GetAsset<Image>("res://images/packed/common_ui/cursor_eraser_tilted.png");
        }
        catch (Exception error) { ModLog.Error(error); }
    }

    private static void OnFrame()
    {
        try
        {
            bool gallery = Input.IsKeyPressed(Key.F10);
            if (gallery && !_galleryHeld) CursorGallery.Toggle();
            _galleryHeld = gallery;
            CursorGallery.OnFrame();
            bool reload = Input.IsKeyPressed(Key.F8);
            bool restore = Input.IsKeyPressed(Key.F9);
            bool modifiers = Input.IsKeyPressed(Key.Ctrl) && Input.IsKeyPressed(Key.Alt);
            bool grow = modifiers && Input.IsPhysicalKeyPressed(Key.Equal);
            bool shrink = modifiers && Input.IsPhysicalKeyPressed(Key.Minus);
            bool growPressed = grow && !_growHeld;
            bool shrinkPressed = shrink && !_shrinkHeld;
            _growHeld = grow;
            _shrinkHeld = shrink;
            if (restore && !_restoreHeld) RestoreAndSave();
            else if (reload && !_reloadHeld) Reload();
            else if (growPressed && !shrink) ChangeSize(8);
            else if (shrinkPressed && !grow) ChangeSize(-8);
            _reloadHeld = reload;
            _restoreHeld = restore;
            if (Input.MouseMode is Input.MouseModeEnum.Hidden or Input.MouseModeEnum.ConfinedHidden) _playingAnimation = null;
            else if (_activeAnimation != null && _manager != null
                && !ReferenceEquals(_lastCursor.GetValue(_manager), SelectFrame(_activeAnimation))) Refresh();
            ulong now = Time.GetTicksMsec();
            if (_store == null || now < _nextPoll) return;
            _nextPoll = now + 1000;
            if (Stamp() != _stamp) Reload();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void ChangeSize(int delta)
    {
        if (_store == null || _image == null) return;
        CursorConfig config = _store.Read();
        if (!config.Enabled) return;
        var (width, height) = CursorFile.Dimensions(CursorFile.Read(_store.ResolveImagePath(config.CursorImage)));
        CursorConfig next = CursorGeometry.Resize(config, width, height, delta);
        if (next.Scale == config.Scale) return;
        _store.Save(next);
        Reload();
        if (_image != null) CursorGallery.SyncSize(Math.Max(_image.GetWidth(), _image.GetHeight()));
    }

    private static (long Time, long Length) Stamp()
    {
        var info = new FileInfo(_store!.ConfigPath);
        return info.Exists ? (info.LastWriteTimeUtc.Ticks, info.Length) : (0, 0);
    }

    private static void Reload()
    {
        if (_store == null) return;
        try
        {
            _stamp = Stamp();
            CursorConfig config = _store.Read();
            CursorAnimation? next = null;
            Vector2 hotspot = Vector2.Zero;
            if (config.Enabled) { next = CursorAnimation.Load(_store, config); hotspot = next.Hotspot; }
            CursorAnimation? old = _animation, oldDraw = _drawAnimation, oldErase = _eraseAnimation;
            _config = config;
            CursorGallery.SyncEnabled(config.Enabled);
            _animation = next; _image = next?.StaticImage;
            _hotspot = hotspot;
            int size = _image == null ? 0 : Math.Max(_image.GetWidth(), _image.GetHeight());
            _drawAnimation = LoadMapImage(config, config.DrawCursorImage, config.DrawHotspotX, config.DrawHotspotY, size, config.DrawIdleFrame);
            _eraseAnimation = LoadMapImage(config, config.EraseCursorImage, config.EraseHotspotX, config.EraseHotspotY, size, config.EraseIdleFrame);
            _drawImage = _drawAnimation?.StaticImage; _drawHotspot = _drawAnimation?.Hotspot ?? Vector2.Zero;
            _eraseImage = _eraseAnimation?.StaticImage; _eraseHotspot = _eraseAnimation?.Hotspot ?? Vector2.Zero;
            try { Refresh(); }
            finally { old?.Dispose(); oldDraw?.Dispose(); oldErase?.Dispose(); }
            ModLog.Info(config.Enabled ? "配置已应用。" : "已恢复原版光标。");
        }
        catch (Exception error) { Fail(error); }
    }

    private static CursorAnimation? LoadMapImage(CursorConfig config, string path, double x, double y, int size, int idleFrame)
    {
        if (size == 0 || string.IsNullOrWhiteSpace(path)) return null;
        try { return CursorAnimation.Load(_store!, config with { CursorImage = path, CursorIdleFrame = idleFrame, HotspotX = x, HotspotY = y }, size); }
        catch (Exception error) { ModLog.Error(error); return null; }
    }

    private static Image SelectFrame(CursorAnimation animation)
    {
        if (!Input.IsMouseButtonPressed(MouseButton.Right) || animation.Frames.Length == 1
            || Input.MouseMode is Input.MouseModeEnum.Hidden or Input.MouseModeEnum.ConfinedHidden)
        { _playingAnimation = null; return animation.StaticImage; }
        if (!ReferenceEquals(_playingAnimation, animation)) { _playingAnimation = animation; _animationStarted = Time.GetTicksMsec(); }
        return animation.At(Time.GetTicksMsec() - _animationStarted);
    }

    private static void RestoreAndSave()
    {
        CursorAnimation? old = _animation, oldDraw = _drawAnimation, oldErase = _eraseAnimation;
        _animation = _drawAnimation = _eraseAnimation = _activeAnimation = _playingAnimation = null;
        _image = _drawImage = _eraseImage = null;
        try { Refresh(); }
        finally { old?.Dispose(); oldDraw?.Dispose(); oldErase?.Dispose(); }
        _config = _config with { Enabled = false };
        CursorGallery.SyncEnabled(false);
        // 先恢复视觉，再保存。即使目录只读，F9 仍能恢复本次会话的光标。
        try
        {
            _store?.Save(_config);
            if (_store != null) _stamp = Stamp();
        }
        catch (Exception error) { ModLog.Error(error); }
        ModLog.Info("F9：原版光标已恢复；enabled=false。");
    }

    private static void Refresh()
    {
        if (_manager == null || !GodotObject.IsInstanceValid(_manager)) return;
        // 原版缓存只比较 Image，不比较热点。必须清缓存，否则仅热点变化不会生效。
        _lastCursor.SetValue(_manager, null);
        _update.Invoke(_manager, null);
    }

    private static void Fail(Exception error, bool refresh = true)
    {
        CursorAnimation? old = _animation, oldDraw = _drawAnimation, oldErase = _eraseAnimation;
        _animation = _drawAnimation = _eraseAnimation = _activeAnimation = _playingAnimation = null;
        _image = _drawImage = _eraseImage = null;
        try { if (refresh) Refresh(); }
        catch (Exception restoreError) { ModLog.Error(restoreError); }
        finally { old?.Dispose(); oldDraw?.Dispose(); oldErase?.Dispose(); }
        ModLog.Error(error);
    }

    private static void Shutdown()
    {
        CursorGallery.Shutdown();
        if (_tree != null && GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= OnFrame;
        _animation?.Dispose(); _drawAnimation?.Dispose(); _eraseAnimation?.Dispose();
        _animation = _drawAnimation = _eraseAnimation = _activeAnimation = _playingAnimation = null;
        _image = _drawImage = _eraseImage = null;
        _drawIdle = _drawPressed = _eraseIdle = _erasePressed = null;
        _manager = null;
        _tree = null;
    }
}
