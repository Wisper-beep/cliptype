# vNext 功能与兼容矩阵

| 能力 | Windows EXE | Chromium 扩展 | Firefox 扩展 | Linux / Niri 原型 |
| --- | --- | --- | --- | --- |
| 技术路线 | WinForms / .NET Framework / Win32 | MV3 service worker + DOM | MV3 background scripts + DOM | Python 标准库 + wtype / wl-paste / Niri IPC |
| 运行条件 | Windows 10/11，Framework 4.5+ | Chrome/Edge 109+ | Firefox 桌面 142+；开发包临时加载 | Python 3.10+，Niri 提供虚拟键盘及 IPC，依赖已安装 |
| 实际验证 | 本地 Windows 编译、真实按键和 GUI；结果见测试报告 | 独立 Chromium 的真实扩展 API 和 DOM 测试 | Gecko 150.0.2 DOM、官方 lint、临时安装 | Ubuntu WSL 逻辑/真实 Unix socket + 后端替身；**没有真实 Niri 试打** |
| 普通模式 | Ctrl+V，保留系统粘贴语义 | 一次插入纯文本，不发 paste | 同 Chromium | 暂无普通粘贴，只逐字输入 |
| 文本与节奏 | 固定、可选随机、标点/句尾/空行停顿 | 同左 | 同左 | 同类规则，CLI 参数 |
| 暂停/继续 | 可配置全局组合；默认 Ctrl+Alt+Space | 页内按钮、Alt+Shift+P | 同左，快捷键由浏览器分配 | Niri 配置触发 pause/resume 命令 |
| 停止 | Esc、窗口/控件焦点变化、取消 | Esc、页面按钮、目标/选区/内容变化 | 同左 | 专用 Niri 停止组合、stop 命令、信号、窗口/IPC 变化；不截获全局普通 Esc |
| 目标保护 | HWND + PID/TID + GUI focus；无网页光标/内容 API | 原元素、可观察选区和值；不自动改回失效目标 | 同左 | 只到 Niri 窗口 id；不能可靠观察同窗口控件/光标 |
| Emoji | 常见 ZWJ、旗帜、肤色/重音 | Intl.Segmenter | Intl.Segmenter | 常见组合的轻量分组 |
| 换行 | Shift+Enter 默认；可选 Enter / 空格 | 文字换行；不发送 Enter 键 | 同左 | 默认空格；显式 Shift+Return / Return |
| 设置 | XML，兼容旧文件 | storage.local，仅参数 | 同左，背景 API 按能力使用 | CLI 参数；不保存正文 |
| 入口/UI | 预览、全局热键、托盘、进度、本地试输入 | 工具栏、可选本页按钮、进度浮层、内部演示 | 共用 UI | CLI + Niri 绑定；可接 bar/menu，无额外 GUI 框架 |
| 权限 | 不自动提权，遵守 UIPI | activeTab/scripting/storage/clipboardRead；无全站主机权限 | 同左；声明不收集数据 | 不使用 root/uinput 守护进程；同 uid 私有控制 socket |
| 产物 | ClipType.exe / ClipType-Windows.zip | ClipType-Chromium.zip | ClipType-Firefox.zip（未签名） | cliptype.pyz / ClipType-Linux.tar.gz / 本地 PKGBUILD |

Windows/Linux 的轻量字符分组不是完整的 Unicode UAX29 实现，不宣称覆盖所有复杂文字。浏览器合成 KeyboardEvent 不等于硬件输入，`isTrusted` 不可伪造。富文本 execCommand 兼容依赖编辑器，较早的 window capture 处理器和自定义规范化不可能通用验证。

跨域 iframe、关闭 Shadow DOM、Google Docs 等特殊编辑器不在扩展支持保证内；普通 input/textarea、常见 contenteditable、同源 iframe 和开放 Shadow DOM 有相应用例。Firefox Android、其他 Wayland compositor 和实际第三方网站尚未完成验证。

Firefox ZIP 是开发构建，不是可永久安装的正式签名 XPI。临时安装会在关闭浏览器后失效；公开分发/持久安装需要另走 Mozilla 签名流程。本次没有签名、提交商店或远程发布。
