# 参考与许可

- 研究了 [Natsuki 的 PRTSCursor](https://github.com/s1f102500012/sts2mod/tree/main/PRTS%E5%8A%A8%E6%80%81%E5%85%89%E6%A0%87%20PRTSCursor)，尤其是 `NCursorManager` 定位、逐项捕获补丁失败和 Godot 生命周期。没有复制它的动画素材、叠加渲染器或整个源码。为保留参考来源，附上其 MIT 许可。
- 研究了 [fresh-milkshake/Modding-Tutorial](https://github.com/fresh-milkshake/Modding-Tutorial) 的工程、初始化与打包说明。当前实现以本机 v0.111.0 的反编译结果为准；文档基线 v0.103.3 与本机版本有差异，例如 dependencies 现为对象列表。
- `sts2.dll`、`GodotSharp.dll`、`0Harmony.dll` 只作为本机编译引用，`Private=false`；不随源码或安装包分发。运行时使用游戏已提供的 Harmony 和 Godot。
- `assets/default.png` 是本项目生成的原创几何箭头，按本项目 MIT 许可发布。
- `assets/gallery/` 的 128 张咖波图片来自用户提供的素材，包含原始表情和已处理的透明图；咖波角色及原始素材的权利归原权利人所有，不纳入本项目的原创素材 MIT 授权范围。文件目录与 SHA-256 见 `docs/gallery-catalog.csv`。

## PRTSCursor / sts2mod 的 MIT 许可

MIT License

Copyright (c) 2026 Natsuki

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

