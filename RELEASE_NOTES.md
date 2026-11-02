ClipType vNext `v1.1.0-rc.1` 开发预览。稳定版 `v1.0.0` 保留。

- Windows：保留原生后端，新增节奏、状态、暂停恢复、进度和退出清理。
- 扩展：新增节奏、暂停恢复、内部测试、参数即时保存、本页快捷按钮和 Firefox MV3 开发构建。
- Linux：新增无特权 wtype / wl-paste / Niri IPC 原型和本地 Arch 包方案；尚未完成真实 Niri 输入验证。
- 两个版本都只在本地运行，不保存剪贴板历史。

**浏览器扩展**：Chromium 包 `ClipType-Chromium.zip` 解压加载。Firefox 包 `ClipType-Firefox.zip` 为未签名开发版，通过 about:debugging 临时加载；正式持久安装需单独签名。先点网页输入框，再按 Alt+Shift+V；Alt+Shift+P 暂停/继续。

**Windows 桌面版**：下载 `ClipType-Windows.zip`，解压运行 ClipType.exe，在目标输入框按 Ctrl + Alt + V。

**Linux**：`ClipType-Linux.tar.gz` 包含 pyz、README、Niri 示例与本地 PKGBUILD；依赖 Python、wtype、wl-clipboard 和 Niri。其窗口级校验不能复现 Windows 控件保护，也不能保证切换边界零泄漏。

扩展未上架商店，通过 DOM 编辑和合成事件输入，不等同真实键盘。平台差异、实际通过与尚未执行的测试见 docs/COMPATIBILITY.md、docs/VNEXT_TEST_RESULTS.md。本预览不替代稳定版本；真实 Niri 输入、特殊网站和最终完整桌面场景仍需补充实际验证。
