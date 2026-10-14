using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace CustomCursorLoader;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private const string HarmonyId = "CustomCursorLoader.LocalCursor";
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        var harmony = new Harmony(HarmonyId);
        try
        {
            Type type = typeof(NCursorManager);
            MethodInfo update = type.GetMethod("UpdateCursor", BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null)
                ?? throw new MissingMethodException(type.FullName, "UpdateCursor");
            MethodInfo enter = type.GetMethod(nameof(NCursorManager._EnterTree), BindingFlags.Instance | BindingFlags.Public,
                binder: null, types: Type.EmptyTypes, modifiers: null)
                ?? throw new MissingMethodException(type.FullName, "_EnterTree");
            if (update.ReturnType != typeof(void) || enter.DeclaringType != type)
                throw new InvalidOperationException("游戏光标方法签名发生变化。");
            CursorService.ValidateFields(type);
            MethodInfo mapUpdate = typeof(NMapDrawings).GetMethod("UpdateLocalCursor", BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null)
                ?? throw new MissingMethodException(typeof(NMapDrawings).FullName, "UpdateLocalCursor");
            if (mapUpdate.ReturnType != typeof(void)) throw new InvalidOperationException("游戏地图光标方法签名发生变化。");
            harmony.Patch(update, prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeUpdate)));
            harmony.Patch(enter, postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterEnterTree)));
            harmony.Patch(mapUpdate, prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeMapUpdate)));
            _initialized = true;
            // Steam 工坊查询可能让 Mod 加载晚于光标节点进入场景。
            if (NGame.Instance?.CursorManager is { } cursor && cursor.IsInsideTree())
                CursorService.Attach(cursor);
            ModLog.Info("V0.2.2 已初始化；随附咖波图库自动导入，F10 选图与默认帧，PNG/APNG/WebP/GIF 按住右键播放。Ctrl+Alt+=/- 缩放，F8 重载，F9 恢复原版。");
        }
        catch (Exception error)
        {
            try { harmony.UnpatchAll(HarmonyId); } catch (Exception cleanup) { ModLog.Error(cleanup); }
            ModLog.Error(error);
        }
    }

    private static void AfterEnterTree(NCursorManager __instance) => CursorService.Attach(__instance);
    private static void BeforeMapUpdate() => CursorService.CaptureMapCursors();

    private static bool BeforeUpdate(Image? ____overriddenCursorTilted, Image? ____overriddenCursorNotTilted,
        ref Image? ____lastSetCursor)
        => CursorService.BeforeUpdate(____overriddenCursorTilted, ____overriddenCursorNotTilted, ref ____lastSetCursor);
}

internal static class ModLog
{
    public static void Info(string text) => Write(text, false);
    public static void Error(Exception error) => Write($"恢复原版光标：{error}", true);
    private static void Write(string text, bool warning)
    {
        try
        {
            if (warning) MegaCrit.Sts2.Core.Logging.Log.Warn("[CustomCursorLoader] " + text);
            else MegaCrit.Sts2.Core.Logging.Log.Info("[CustomCursorLoader] " + text);
        }
        catch { Console.Error.WriteLine("[CustomCursorLoader] " + text); }
    }
}
