# Custom Cursor Loader · 自定义光标加载器

![Custom Cursor Loader](docs/images/cover.png)

0.2.2 下载：[安装包](releases/v0.2.2/CustomCursorLoader-v0.2.2.zip) · [源码包](releases/v0.2.2/CustomCursorLoader-v0.2.2-source.zip) · [工坊上传包](releases/v0.2.2/CustomCursorLoader-v0.2.2-workshop.zip) · [SHA-256](releases/v0.2.2/SHA256SUMS.txt)。三个包均包含 128 张咖波图库，安装步骤见下方中文安装说明。

让《杀戮尖塔2》用本地 PNG/APNG、WebP、GIF 作为普通、地图画笔和橡皮擦光标。图片无需编译进 Mod，支持透明背景、缩放、源图热点和透明度，保留其他游戏特殊光标与手柄隐藏逻辑。

当前版本 0.2.2，作者 Toyer1209。附带 128 张不重复的咖波图片，首次启动自动导入图库，按 F10 即可选择；玩家仍可自行导入更多图片。主菜单点击“光标图库”，或按 F10 打开选图窗口；点选动态图后，用“默认帧”帧号或滑条逐帧预览，设置热点，保存应用。平时显示选定帧，按住右键从原动画开头循环播放，松开恢复选定帧；右键仍执行游戏原操作。三种光标独立保存默认帧，画笔和橡皮擦大小跟随普通光标。更新 DLL 后需重启游戏。

当前兼容目标：Windows x64，本机 **public-beta v0.111.0 / 41cef1ea，Godot 4.5.1，net9.0**。核心测试和真实游戏隔离窗口运行检查已通过；用户反馈 0.1.3 与朋友联机正常。新增界面与动画还需正常 Steam 游戏中的手动体验确认，不能把模拟输入测试当作完整游戏验收。

- [中文安装](docs/INSTALL.md)
- [换自己的图片、大小与热点](docs/CONFIG.md)
- [真实 API、C# 代码解释与开发](docs/DEVELOPMENT.md)
- [完成与待完成的测试](docs/TESTING.md)
- [引用来源与许可](THIRD_PARTY_NOTICES.md)

## 使用

安装后，首次加载会创建 `user://CustomCursorLoader/config.json` 与 `cursors/default.png`。Windows 默认位于 `%APPDATA%/SlayTheSpire2/CustomCursorLoader/`，确切路径写入游戏日志。

```json
{
  "enabled": true,
  "cursorImage": "cursors/我的光标.png",
  "scale": 1.5,
  "opacity": 0.8,
  "hotspotX": 8,
  "hotspotY": 6
}
```

热点单位是**原始 PNG 像素**，会随大小换算。配置保存约 1 秒内应用；F8 重新加载同名图片，F9 立即恢复原版并保存关闭状态。32×32 原图乘以 1.5 得到 48×48。

Ctrl＋Alt＋＝ 放大，Ctrl＋Alt＋－ 缩小（主键盘加减号所在的两个键，不必按 Shift）。每按一次把光标最长边调整 8 像素，通常限制在 16–256 像素，保持图片比例，热点自动换算，修改自动保存。按住不会连续重载；禁用时缩放键不会重新启用光标。可选的地图画笔、橡皮擦图片会一起调整大小；未配置的模式沿用原版。

支持静态或动态 PNG/APNG、WebP、GIF；文件 ≤8 MiB、源图每边 ≤2048、动画 ≤120 帧且解码像素预算 ≤128 MiB、输出每边 ≤256，建议最终尺寸 ≤128。GIF 使用 Windows 自带 GDI+。路径可以包含中文和空格。文件异常或补丁失败会记录日志并保留原版行为。

默认图片仍是项目生成的原创几何箭头。随附咖波图库来自用户提供的图片，包括原始表情和已处理的透明成品；角色素材不纳入软件的 MIT 许可。不要与 PRTSCursor 等其他本地光标替换 Mod 同时使用。

## 构建与安装

```powershell
.\tools\Build.ps1 -GameDir "你的游戏安装目录"
.\tools\Test.ps1
.\tools\Install.ps1 -GameDir "你的游戏安装目录"
```

需要 .NET 9 SDK；运行时无需额外安装 SDK。发布内容位于 `dist/mods/CustomCursorLoader/`，Manifest 与 DLL 放同一目录，无 PCK 和外部 Mod 依赖。Manifest 的 affects_gameplay=false；生产代码不修改卡牌、战斗、网络、鼠标坐标或存档。

## 工程

```text
CustomCursorLoader/
  CustomCursorLoader.csproj / CustomCursorLoader.json
  src/ModEntry.cs          初始化与三个 Harmony 补丁
  src/CursorService.cs     主线程生命周期、恢复、热重载
  src/CursorLoader.cs      Godot 图片解码与处理
  src/CursorConfig.cs      JSON、路径与持久化
  src/PngData.cs           解码前保护与热点数学
  assets/default.png      原创示例
  examples/config.json
  tools/                  构建、安装、核心和隔离测试
  tests/                  无依赖核心测试、隔离运行驱动
  docs/
```

图库导入会复制原文件到玩家数据目录的 gallery/，按完整文件 SHA-256 去重，并保留图片原名称供界面展示；不会把图片上传或发给其他玩家。静态图始终显示同图；动态图默认首帧，可改为任一帧。选帧只保存帧号，不修改原文件或生成额外 PNG。Help 和其他临时特殊状态保留游戏行为。随附图片在 DLL 同目录的 gallery/ 中，启动时按内容去重导入玩家图库，只增加新图片，保留个人配置和原图。工坊 0.2.2 私密条目已更新成功，后续保留 mod_id.txt 使用同一工作区。提交配置已省略官方提醒可能异常的 minBranch/maxBranch，兼容分支在网页核对。

研究了 [PRTSCursor](https://github.com/s1f102500012/sts2mod) 和 [Modding-Tutorial](https://github.com/fresh-milkshake/Modding-Tutorial)，以本机真实 DLL 为最终依据。许可说明见 THIRD_PARTY_NOTICES.md；随附咖波角色图片来自用户提供的素材，不打包游戏 DLL。


