using Godot;
using System.Text.Json;
using MegaCrit.Sts2.Core.Nodes;

namespace CustomCursorLoader;

// 使用原生 Godot 节点和信号，不依赖额外场景或修改游戏设置场景。
internal static class CursorGallery
{
    private static Window? _window;
    private static CanvasLayer? _entry;
    private static Button? _entryButton;
    private static ItemList _list = null!;
    private static OptionButton _mode = null!;
    private static CheckButton _enabled = null!;
    private static SpinBox _x = null!, _y = null!, _size = null!;
    private static SpinBox _frame = null!;
    private static HSlider _frameSlider = null!;
    private static CursorAnimation? _previewAnimation;
    private static TextureRect _preview = null!;
    private static Label _marker = null!, _status = null!;
    private static FileDialog _picker = null!;
    private static ImageTexture? _previewTexture;
    private static readonly List<ImageTexture> Textures = [];
    private static string[] _paths = [];
    private static string? _selected;
    private static int _width, _height;

    public static void Attach(SceneTree tree)
    {
        if (_entry != null) return;
        _entry = new CanvasLayer { Name = "CustomCursorLoaderEntry", Layer = 80 };
        _entryButton = new Button { Text = "光标图库 · F10", Position = new Vector2(20, 20), Visible = false };
        _entryButton.Pressed += Toggle;
        _entry.AddChild(_entryButton);
        tree.Root.AddChild(_entry);
    }

    public static void OnFrame()
    {
        if (_entryButton != null) _entryButton.Visible = NGame.Instance?.MainMenu != null;
        if (_window?.Visible == true) UpdateMarker();
    }

    internal static void SyncEnabled(bool enabled) { if (_window != null) _enabled.SetPressedNoSignal(enabled); }
    internal static void SyncSize(int size) { if (_window?.Visible == true) _size.Value = size; }

    public static void Toggle()
    {
        if (_window?.Visible == true) { Close(); return; }
        try
        {
            if (_window == null) Build();
            LoadMode();
            _window!.PopupCenteredClamped(new Vector2I(960, 650), 0.85f);
        }
        catch (Exception error) { ModLog.Error(error); }
    }

    private static void Build()
    {
        _window = new Window { Name = "CustomCursorLoaderGallery", Title = "自定义光标图库", Transient = true, Exclusive = true,
            Size = new Vector2I(960, 650), MinSize = new Vector2I(640, 460), Visible = false };
        CursorService.Tree!.Root.AddChild(_window);
        _window.CloseRequested += Close;
        var margin = new MarginContainer();
        _window.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        var column = new VBoxContainer(); margin.AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        _enabled = new CheckButton { Text = "启用自定义光标" }; header.AddChild(_enabled);
        _enabled.Toggled += enabled => Guard(() => { CursorService.ApplyConfig(Current() with { Enabled = enabled }); Status(enabled ? "已启用" : "已恢复原版"); });
        _mode = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (string name in new[] { "普通光标", "地图画笔", "地图橡皮擦" }) _mode.AddItem(name);
        header.AddChild(_mode); _mode.ItemSelected += _ => Guard(LoadMode);
        AddButton(header, "导入图片…", () => _picker.PopupCenteredClamped(new Vector2I(850, 600), 0.85f));
        AddButton(header, "刷新图库", () => Guard(LoadMode));
        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; column.AddChild(body);
        _list = new ItemList { IconMode = ItemList.IconModeEnum.Top, MaxColumns = 3, FixedColumnWidth = 170,
            FixedIconSize = new Vector2I(72, 72), SameColumnWidth = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddChild(_list); _list.ItemSelected += index => Guard(() => Select((int)index));
        var editorScroll = new ScrollContainer { CustomMinimumSize = new Vector2(235, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(editorScroll);
        var editor = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; editorScroll.AddChild(editor);
        var board = new Panel { CustomMinimumSize = new Vector2(224, 224), MouseFilter = Control.MouseFilterEnum.Stop };
        editor.AddChild(board);
        _preview = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        board.AddChild(_preview); _preview.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _marker = new Label { Text = "+", MouseFilter = Control.MouseFilterEnum.Ignore }; _marker.AddThemeColorOverride("font_color", Colors.Red); board.AddChild(_marker);
        board.GuiInput += input => {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click && _selected != null)
            {
                Rect2 rect = PreviewRect();
                if (rect.HasPoint(click.Position))
                {
                    _x.Value = Math.Round((click.Position.X - rect.Position.X) * _width / rect.Size.X);
                    _y.Value = Math.Round((click.Position.Y - rect.Position.Y) * _height / rect.Size.Y);
                }
            }
        };
        editor.AddChild(new Label { Text = "点预览图设置点击作用点（红色＋）", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _frame = AddNumber(editor, "默认帧", 1, 1); _frame.Editable = false;
        _frameSlider = new HSlider { MinValue = 1, MaxValue = 1, Step = 1, Editable = false };
        editor.AddChild(_frameSlider);
        _frame.ValueChanged += value => Guard(() => ShowFrame((int)value - 1));
        _frameSlider.ValueChanged += value => Guard(() => ShowFrame((int)value - 1));
        _x = AddNumber(editor, "热点 X", 0, 2047); _y = AddNumber(editor, "热点 Y", 0, 2047);
        _size = AddNumber(editor, "最长边（像素）", 16, 256); _size.Step = 8;
        _x.ValueChanged += _ => UpdateMarker(); _y.ValueChanged += _ => UpdateMarker();
        AddButton(editor, "保存并应用", () => Guard(SaveSelection));
        AddButton(editor, "使用原版 / 默认箭头", () => Guard(UseDefault));
        var footer = new HBoxContainer(); column.AddChild(footer);
        _status = new Label { Text = "点选图片预览，再保存应用", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart }; footer.AddChild(_status);
        AddButton(footer, "关闭", Close);
        _picker = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFiles,
            UseNativeDialog = true, Filters = ["*.png,*.apng,*.webp,*.gif ; 光标图片（PNG / APNG / WebP / GIF）"] };
        _window.AddChild(_picker);
        _picker.FilesSelected += files => Guard(() => {
            int imported = 0; var errors = new List<string>();
            foreach (string file in files)
                try {
                    using CursorAnimation animation = CursorAnimation.Load(CursorService.Store!, new CursorConfig { CursorImage = file }, 72);
                    CursorLibrary.Import(CursorService.Store!, file); imported++;
                }
                catch (Exception error) { errors.Add(Path.GetFileName(file) + ": " + error.Message); }
            LoadMode(); Status($"已导入 {imported} 张（相同文件自动去重）" + (errors.Count == 0 ? "" : "；失败：" + string.Join("；", errors)));
        });
    }

    private static SpinBox AddNumber(Control parent, string text, double min, double max)
    {
        var row = new HBoxContainer(); parent.AddChild(row);
        row.AddChild(new Label { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var number = new SpinBox { MinValue = min, MaxValue = max, Step = 1, CustomMinimumSize = new Vector2(100, 0) };
        row.AddChild(number); return number;
    }

    private static void AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text }; button.Pressed += action; parent.AddChild(button);
    }

    private static void LoadMode()
    {
        ConfigStore store = CursorService.Store!;
        CursorConfig config = Current();
        _enabled.SetPressedNoSignal(config.Enabled);
        _size.Editable = _mode.Selected == 0;
        try {
            var dimensions = CursorFile.Dimensions(CursorFile.Read(store.ResolveImagePath(config.CursorImage)));
            var geometry = CursorGeometry.Calculate(dimensions.Width, dimensions.Height, config);
            _size.Value = Math.Max(geometry.Width, geometry.Height);
        }
        catch { _size.Value = 64; }
        _list.Clear(); ClearPreview();
        foreach (ImageTexture texture in Textures) texture.Dispose(); Textures.Clear();
        _paths = CursorLibrary.List(store); _selected = null;
        string current = _mode.Selected switch { 0 => config.CursorImage, 1 => config.DrawCursorImage, _ => config.EraseCursorImage };
        int active = -1;
        for (int index = 0; index < _paths.Length; index++)
        {
            string path = _paths[index]; ImageTexture? texture = null;
            try
            {
                using Image image = CursorLoader.Load(store, new CursorConfig { CursorImage = path, HotspotX = 0, HotspotY = 0 }, 72).Image;
                texture = ImageTexture.CreateFromImage(image); Textures.Add(texture);
            }
            catch (Exception error) { ModLog.Error(error); }
            _list.AddItem(CursorLibrary.Label(path), texture);
            _list.SetItemDisabled(index, texture == null);
            _list.SetItemTooltip(index, path);
            if (!string.IsNullOrWhiteSpace(current) && store.ResolveImagePath(current).Equals(path, StringComparison.OrdinalIgnoreCase)) active = index;
        }
        if (active >= 0 && !_list.IsItemDisabled(active)) { _list.Select(active); Select(active); }
        else { _marker.Visible = false; Status("点选图片预览，再保存应用；地图图片大小跟随普通光标"); }
    }

    private static void Select(int index)
    {
        ConfigStore store = CursorService.Store!;
        string path = _paths[index];
        var dimensions = CursorFile.Dimensions(CursorFile.Read(path));
        CursorAnimation animation = CursorAnimation.Load(store, new CursorConfig { CursorImage = path, HotspotX = 0, HotspotY = 0 }, 224);
        ClearPreview(); _previewAnimation = animation;
        (_width, _height) = dimensions; _selected = path;
        _x.MaxValue = _width - 1; _y.MaxValue = _height - 1;
        CursorConfig config = Current();
        var (current, x, y, idleFrame) = _mode.Selected switch {
            0 => (config.CursorImage, config.HotspotX, config.HotspotY, config.CursorIdleFrame),
            1 => (config.DrawCursorImage, config.DrawHotspotX, config.DrawHotspotY, config.DrawIdleFrame),
            _ => (config.EraseCursorImage, config.EraseHotspotX, config.EraseHotspotY, config.EraseIdleFrame) };
        bool same = !string.IsNullOrWhiteSpace(current) && store.ResolveImagePath(current).Equals(path, StringComparison.OrdinalIgnoreCase);
        _x.Value = same ? x : 0; _y.Value = same ? y : 0;
        _frame.MaxValue = _frameSlider.MaxValue = animation.Frames.Length;
        _frame.Editable = _frameSlider.Editable = animation.Frames.Length > 1;
        ShowFrame(same ? idleFrame : 0);
    }

    private static void ShowFrame(int index)
    {
        if (_previewAnimation == null) return;
        index = Math.Clamp(index, 0, _previewAnimation.Frames.Length - 1);
        _frame.SetValueNoSignal(index + 1); _frameSlider.SetValueNoSignal(index + 1);
        ImageTexture texture = ImageTexture.CreateFromImage(_previewAnimation.Frames[index]);
        _preview.Texture = texture; _previewTexture?.Dispose(); _previewTexture = texture;
        UpdateMarker();
        Status(CursorLibrary.Label(_selected!) + $" · 默认帧 {index + 1}/{_previewAnimation.Frames.Length} · 保存后生效，右键仍播放原动画");
    }

    private static void ClearPreview()
    {
        _preview.Texture = null; _previewTexture?.Dispose(); _previewTexture = null;
        _previewAnimation?.Dispose(); _previewAnimation = null;
        _frame.Editable = _frameSlider.Editable = false;
    }

    private static Rect2 PreviewRect()
    {
        Vector2 pixels = _preview.Texture?.GetSize() ?? new Vector2(_width, _height);
        float factor = Math.Min(_preview.Size.X / Math.Max(1, pixels.X), _preview.Size.Y / Math.Max(1, pixels.Y));
        Vector2 size = pixels * factor;
        return new Rect2((_preview.Size - size) / 2, size);
    }

    private static void UpdateMarker()
    {
        if (_selected == null || _marker == null) return;
        Rect2 rect = PreviewRect(); _marker.Visible = true;
        _marker.Position = rect.Position + new Vector2((float)_x.Value * rect.Size.X / _width, (float)_y.Value * rect.Size.Y / _height) - _marker.Size / 2;
    }

    private static void SaveSelection()
    {
        if (_selected == null) { Status("请先选择图片"); return; }
        ConfigStore store = CursorService.Store!;
        CursorConfig config = CursorLibrary.Select(Current(), _mode.Selected, Path.GetRelativePath(store.Root, _selected),
            _width, _height, _x.Value, _y.Value, (int)_size.Value, (int)_frame.Value - 1);
        CursorService.ApplyConfig(config);
        Status("已保存并应用；F10 关闭图库");
    }

    private static void UseDefault()
    {
        ConfigStore store = CursorService.Store!;
        CursorConfig config = Current();
        config = _mode.Selected switch {
            0 => config with { CursorImage = "cursors/default.png", CursorIdleFrame = 0, Scale = 1, HotspotX = 3, HotspotY = 3 },
            1 => config with { DrawCursorImage = "", DrawIdleFrame = 0 },
            _ => config with { EraseCursorImage = "", EraseIdleFrame = 0 } };
        CursorService.ApplyConfig(config); LoadMode(); Status("已使用原版 / 默认箭头");
    }

    private static void Status(string text) => _status.Text = text;
    private static CursorConfig Current()
    {
        try { return CursorService.Store!.Read(); }
        catch (Exception error) when (error is IOException or JsonException) { return CursorService.Config; }
    }
    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) { _enabled.SetPressedNoSignal(CursorService.Config.Enabled); Status("未能应用：" + error.Message); ModLog.Error(error); }
    }

    private static void Close()
    {
        _picker?.Hide(); _window?.Hide();
        ClearPreview(); _list.Clear(); _selected = null;
        foreach (ImageTexture texture in Textures) texture.Dispose(); Textures.Clear();
    }

    public static void Shutdown()
    {
        if (_window != null) { Close(); _window.QueueFree(); _window = null; }
        _entry?.QueueFree(); _entry = null; _entryButton = null;
    }
}
