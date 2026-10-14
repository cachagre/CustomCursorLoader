# 开发与关键代码

## 本机研究结果（2026-10-13）

- Windows 11 内核 10.0.26200，x64。
- .NET SDK 9.0.200，已安装但本会话 PATH 未包含它；脚本支持标准安装目录回退。
- 本机游戏 v0.111.0，commit 41cef1ea，release_info 日期 2026-08-13，Steam build 24724944、public-beta。
- 游戏 runtimeconfig 是 net9.0，内置 .NET 9.0.7；引擎 `4.5.1.m.14.mono.custom_build`。
- 没有现有源码需要覆盖，工程从空工作区新建。
- 不需要安装 Godot 编辑器，也不需要制作 PCK。ILSpy 仅用于本地只读研究，没有加入发布依赖。

本机只读反编译了 NCursorManager、NGame、ModManager、ModManifest、ModSettings 及运行测试相关的存储类。反编译源码与游戏 DLL 不放进交付源码或安装包。

## 实际加载契约

ModManager 递归扫描游戏 mods 及工坊目录中的 .json；Manifest 的 id 决定同目录 DLL/PCK 名称。`[ModInitializer(nameof(Initialize))]` 标记的静态入口由真实加载器调用。当前依赖为 `{id,min_version}` 对象；本项目没有外部 Mod 依赖。

Manifest 设置 has_dll=true、has_pck=false、affects_gameplay=false、min_game_version=0.111.0。affects_gameplay 用于游戏的玩法 Mod 列表与多人一致性判断；不表示游戏会完全忽略此 Mod。配置 JSON 放在 user://，避免被扫描成 Manifest。

## 真实光标 API 和补丁

| 位置 | 原游戏行为 | 本 Mod 行为 |
|---|---|---|
| NCursorManager._EnterTree() | 设置 Help，然后调用 UpdateCursor | Harmony postfix 绑定本地实例，加载配置，订阅 SceneTree.ProcessFrame |
| NCursorManager.UpdateCursor()，private void，无参数 | 在普通/按住 Image 中选择，调用 Input.SetCustomMouseCursor(Image, Arrow, HotSpot) | Harmony prefix 在没有游戏临时覆盖、且图片有效时使用自定义 Image |
| NCursorManager.OverrideCursor(Image,Image,Vector2) | 游戏临时覆盖普通和按住外观 | 不补丁；只有已识别的原地图画笔/橡皮擦资源对使用对应自定义图片，其他覆盖运行原方法 |
| NMapDrawings.UpdateLocalCursor() | 根据本地画图/擦除状态选择原覆盖图片 | Prefix 从游戏缓存获取四张原资源引用，用于精确识别；原方法完整执行 |
| NCursorManager.StopOverridingCursor() | 清临时覆盖并更新 | 不补丁；更新时自然恢复自定义图片 |
| NCursorManager._Input(InputEvent) | 左/右/中键改变 _isDown | 不补丁；按住行为原样运行 |

仅反射使用已验证的 `_lastSetCursor`、`_overriddenCursorTilted`、`_overriddenCursorNotTilted`；启动时检查字段类型与方法签名。任一补丁失败会撤销本 Mod 已安装的补丁并记录错误。未来版本是否兼容仍需重新构建和运行测试；不能保证任意重大 API 变化都能自动适配。

0.1.1 在安装补丁后检查 `NGame.Instance?.CursorManager`：若该节点已在场景树中，立即调用 CursorService.Attach。Steam 工坊分支检查有异步等待，不能假定 Mod 总会在 NCursorManager._EnterTree 之前初始化；节点尚未进入时则仍由原来的 postfix 绑定。

Help 用 `_cursorInspect`，原版热点 (12,12)；普通热点原版 (14,5)。本项目不更改 Help，也不假设悬停按钮有独立游戏回调。原版 `_isDown` 是按住状态，不能直接当作“hover”或独立点击动画事件。

## 面向 C/C++ 使用者的解释

- ModEntry 相当于 DLL 的显式加载入口，Harmony 是在已存在方法前后安装拦截函数。prefix 返回 false 时跳过原方法；遇到禁用、游戏特殊状态或异常时返回 true，原游戏继续执行。
- CursorConfig 是配置数据结构，record 的 `with` 表达式生成修改后的副本。ConfigStore 使用 System.Text.Json 保存和读取，F9 保存时先备份旧文件。
- CursorFile 先检查文件大小、格式和源尺寸。AnimationPackets 拆分 APNG/WebP 容器；PNG/WebP 像素由 Godot 解码，GIF 由 Windows GDI+ 解码。CursorAnimation 合成固定画布并交给 CursorLoader 缩放、计算 Alpha，只在加载时处理。
- CursorGeometry 是独立于 Godot 的尺寸/热点数学，所以可以用普通 .NET 进程测试。源热点 × 实际输出/源尺寸，不改变真实鼠标位置。
- 0.1.2 的组合键读取 Ctrl、Alt 与主键盘等号/减号，按键按下边沿触发。CursorGeometry.Resize 从实际输出最长边计算新倍率，避免原配置被 256 上限截断后无法缩小；源热点和透明度保持不变，随后保存并重载。按键模拟测试携带正确的修饰键状态，并刷新输入缓冲后检查真实 Godot Input 查询。
- CursorService 持有普通、可选画笔和橡皮擦三个 Image，配置变化时换新对象并 Dispose 旧对象。地图大小跟随普通光标最长边。游戏原覆盖字段仍持有原资源，不写入自定义 Image，防止重载后悬挂引用。恢复或配置变更时清 `_lastSetCursor`，避免跳过必要更新。
- SceneTree.ProcessFrame 是信号订阅，不是自定义 Node._Process；不依赖 Godot 源码生成器。每帧只检查两个快捷键，每秒检查配置的时间/大小；不逐帧读 PNG 或创建纹理。
- 没有叠加光标节点、CanvasLayer 或鼠标事件过滤器。生产代码不引用远端玩家光标、网络、卡牌、战斗或存档 API。

## 构建

```powershell
.\tools\Build.ps1 -GameDir "游戏安装目录"
.\tools\Test.ps1
```

csproj 使用 Microsoft.NET.Sdk 和 net9.0，直接引用本机真实 DLL，Private=false。没有 NuGet 运行依赖。macOS/Linux 的托管目录布局未检测；手工构建可通过 `-p:GameDataDir=实际DLL目录`，但未验证跨平台运行。

## 重现隔离运行测试

```powershell
.\tools\Test-Runtime.ps1 -GameDir "游戏安装目录" -WorkDir "独立测试工作目录"
.\tools\Test-Runtime.ps1 -GameDir "游戏安装目录" -WorkDir "独立测试工作目录" -LateInitialization
```

此脚本需要 PowerShell 7。它复制约 3 GB 的本机游戏运行文件到独立目录，检查重定向后的 user://，禁用测试副本 Sentry autoload 和 Steam 初始化，仅修改新生成的测试设置，再让真实 ModManager 加载生产 DLL 与测试驱动。驱动确认图片处理、补丁执行、回退与自动重载后主动退出，再移出测试驱动并启动新进程检查配置持久化。测试副本不放进发布包，不访问原存档；脚本会还原当前进程环境变量。

RuntimeTests DLL 只用于隔离副本；没有 `--cursor-isolated-test` 标记就拒绝运行。headless 检查不会证明硬件光标的屏幕外观、点击命中、多人或拖牌正确。

LateInitialization 用真实已进入场景的 NGame.CursorManager，清理测试副本里的绑定和补丁，再重新执行生产初始化入口，验证无需再次 _EnterTree 就能加载 PNG。它复现异步启动造成的顺序差异，不启用 Steam 云存档。

## 参考资料

- [PRTSCursor 实际源码](https://github.com/s1f102500012/sts2mod/tree/main/PRTS%E5%8A%A8%E6%80%81%E5%85%89%E6%A0%87%20PRTSCursor)：它采用透明硬件光标加动画叠层。本项目只借鉴 API 定位与生命周期研究，静态 PNG 使用本机已确认的硬件光标路径。
- [Modding Tutorial 的项目与 Manifest](https://fresh-milkshake.github.io/Modding-Tutorial/03-mod-loading-and-manifests/)：确认代码 Mod 可省略 PCK；具体字段以本机为准。
- [Godot 4.5 Input](https://docs.godotengine.org/en/4.5/classes/class_input.html#class-input-method-set-custom-mouse-cursor)：Image、热点和 256 像素限制。
- [Godot 4.5 Image](https://docs.godotengine.org/en/4.5/classes/class_image.html)：PNG 解码、RGBA 与 Resize。

## 0.2.0 图库与动画

CursorGallery 使用独立 Godot Window、ItemList、TextureRect 和原生 FileDialog，主菜单入口与 F10 打开；没有补丁原设置场景或消耗游戏鼠标事件。CursorLibrary 按 SHA-256 导入去重，选择普通图片时换算大小，选择地图图片时仅替换对应路径和热点。

CursorService 持有三组 CursorAnimation，_image/_drawImage/_eraseImage 为各组首帧的借用引用。按住右键按各帧延时选 Image，松开回到首帧；仅换帧时调用硬件光标 API，无每帧文件读取或图片分配。隐藏与其他特殊覆盖会停止本 Mod 的播放。重载、恢复和关闭释放全部自有帧，原游戏地图资源仍只借用。

APNG/WebP 容器实现依据 [W3C PNG 第三版](https://www.w3.org/TR/png-3/) 与 [Google WebP 容器规范](https://developers.google.com/speed/webp/docs/riff_container)，包括区域、混合、处置和延时；没有复制其示例源码。GIF 通过系统 GDI+ 选择已合成帧，不随包分发任何额外解码 DLL。按住时循环的行为优先于文件的有限循环次数。

运行脚本可加 `-Visual` 用真实 Windows 渲染窗口验证布局、预览点击与鼠标隐藏，保存 gallery.png；仍用隔离用户目录和模拟输入。GIF 目前仅 Windows，其他平台未验收。

## 0.2.1 默认帧选择

CursorConfig 的 cursorIdleFrame、drawIdleFrame、eraseIdleFrame 分别保存三种光标的零起始帧号，缺省为 0。CursorAnimation 加载后将帧号限制在实际范围内，StaticImage 返回所选帧；CursorService 的三个静态引用借用该帧。右键仍调用原动画的 At，从开头播放，松开恢复 StaticImage。

CursorGallery 在选图时缓存 224 像素的整组预览帧，默认帧 SpinBox 与 HSlider 使用从 1 开始的帧号并保持同步。预览只替换 ImageTexture，保存时才写入对应模式的帧号；切换图片或关闭窗口释放全部预览帧。原动画文件不变，不生成单独 PNG。隔离测试脚本额外启动新进程检查第二帧默认外观，再恢复缩放检查的测试配置。

## 0.2.2 随附咖波图库

assets/gallery/ 只包含 128 张去重图片。Build.ps1 将它们复制到 DLL 同目录的 gallery/，工坊与手动安装均使用同一布局。CursorService.Attach 从实际加载 DLL 的 Assembly.Location 定位该文件夹，调用 CursorLibrary.ImportBundled。

导入先对玩家 cursors/ 与 gallery/ 的文件建立内容哈希集合，再导入未存在的随附图片；复用现有命名与格式识别逻辑，不写玩家 config.json、不覆盖已存在的文件。更新图片内容会作为新文件追加。缺少随附文件夹的旧包仍可加载。首次、晚初始化与重启路径使用真实 ModManager 验证。
