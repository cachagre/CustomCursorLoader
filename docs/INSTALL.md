# 安装 0.2.2

1. 关闭游戏，将发布包里的 `mods/CustomCursorLoader/` 复制到包含 `SlayTheSpire2.exe` 的游戏目录中。
2. 确认最终布局是 `游戏目录/mods/CustomCursorLoader/CustomCursorLoader.dll` 和同目录的 `CustomCursorLoader.json`，以及随包的 `gallery/` 图片文件夹。
3. 启动游戏，在游戏原有的 Mods 提示中同意加载，必要时重启。不要手动修改游戏存档或同意状态文件。
4. 第一次加载会在 `user://CustomCursorLoader/` 创建 `config.json` 和原创 `cursors/default.png`，将随附 128 张咖波图按内容去重导入本地 `gallery/`；按 F10 即可选图。实际绝对路径会写入游戏日志，搜索 `[CustomCursorLoader] 配置目录`。
5. Windows 的默认位置通常是 `%APPDATA%/SlayTheSpire2/CustomCursorLoader/`；以日志中的路径为准。

## 从源码构建和安装

需要 Windows 上的 .NET 9 SDK 与合法安装的游戏。PowerShell 在本项目目录执行：

```powershell
.\tools\Build.ps1 -GameDir "你的 Steam 库\steamapps\common\Slay the Spire 2"
.\tools\Test.ps1
.\tools\Install.ps1 -GameDir "你的 Steam 库\steamapps\common\Slay the Spire 2"
```

也可以省略 GameDir，让脚本从 Steam 注册表和库配置查找。环境变量 `STS2_GAME_DIR` 可指定安装路径。安装脚本只写入本 Mod 目录；已有同名目录先备份到源码的 `install-backups/`，备份不留在游戏的 mods 扫描目录内。

## 快捷键和恢复

- Ctrl＋Alt＋＝：放大 8 像素，自动保存；Ctrl＋Alt＋－：缩小 8 像素，自动保存。使用主键盘，不需要 Shift，通常在 16–256 像素之间调整。
- F8：重新读取配置与 PNG，适合替换了图片内容但文件名未变时使用。
- F9：立即恢复原版普通光标，并保存 `enabled=false`；上次配置保存为 `config.json.bak`。
- F10：打开/关闭游戏内图库；可启用/关闭，导入 PNG/APNG、WebP、GIF，选择普通、画笔和橡皮擦图片，预览、设置热点并保存。
- 动态图可用“默认帧”帧号或滑条选择平时外观；按住右键从原动画开头循环播放，松开恢复选定帧，游戏原右键操作仍保留。
- 修改配置通常在约 1 秒内生效。启用设为 `true`，关闭设为 `false`。
- 如果当前游戏版本完全无法加载 Mod：关闭游戏，将 `mods/CustomCursorLoader/` 移出 mods 目录即可。启动时没有此 DLL 就没有补丁。

不要同时启用 PRTSCursor 或其他修改本地光标的 Mod，它们可能互相覆盖。

更新到 0.2.2 后请完全退出并重新启动游戏。F8 只重载图片和配置，不能替换已加载的 DLL。你的旧图片和配置不会因安装更新被替换。

游戏自身的 Mod 加载机制可能使用单独的 modded 存档目录；这是游戏行为。本 Mod 不调用存档 API。安装文件不会修改存档。

## 创意工坊准备

`dist/mods/CustomCursorLoader/` 是候选发布 payload，不需要 PCK。正式发布前必须完成 `docs/TESTING.md` 的真实游戏测试，并填写作者、说明和自行制作的工坊封面。本项目没有自动上传或创建工坊条目。请使用游戏当前支持的上传流程，勿上传游戏 DLL、个人配置或无授权图片。

