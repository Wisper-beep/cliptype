ClipType 的首个版本，包含 Windows 桌面工具与 Chrome / Edge 浏览器扩展。

- 扩展：逐字输入、普通插入、光标与选区保留、中文和表情、调速、Esc 和焦点改变时停止。
- 桌面：全局快捷键、Windows Unicode 模拟按键、普通 Ctrl + V 粘贴、托盘常驻。
- 两个版本都只在本地运行，不保存剪贴板历史。

**浏览器扩展**：下载 `ClipType-Extension.zip`，解压，在 Chrome / Edge 扩展管理页开启开发者模式，选择「加载已解压的扩展程序」，选择包含 manifest.json 的目录。先点击网页输入框，再按 Alt + Shift + V。

**Windows 桌面版**：下载 `ClipType-Windows.zip`，解压运行 ClipType.exe，在目标输入框按 Ctrl + Alt + V。

扩展尚未上架商店；它通过 DOM 编辑和合成事件输入，不等同于真实键盘事件。浏览器内部页面、跨域 iframe 和 Google Docs 等特殊编辑器不支持。详见仓库 README。
