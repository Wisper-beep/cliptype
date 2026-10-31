# ClipType 浏览器扩展

支持 Chrome / Edge 的 Manifest V3 扩展。把剪贴板文字逐字输入或一次插入到网页光标处。

## 安装

1. 解压 `ClipType-Extension.zip`，保留整个目录。
2. 打开 `chrome://extensions`（Chrome）或 `edge://extensions`（Edge）。
3. 开启开发者模式，点击「加载已解压的扩展程序」，选择包含 `manifest.json` 的目录。
4. 将 ClipType 固定在浏览器工具栏，方便调用。

尚未上架 Chrome Web Store / Edge Add-ons。不要移动或删除已加载的目录。更新时替换目录内文件，再在扩展管理页面点击重新加载。

## 使用

复制文字 → 点击目标输入框并放好光标或选区 → 点击 ClipType 图标或按 **Alt + Shift + V** → 检查预览 → 选择 **逐字输入** 或 **普通插入**。

逐字输入可调整间隔（默认 30 ms）和开始前等待（默认 500 ms）。支持保留换行或改为空格；单行 input 始终把换行改为空格。Tab 转为 4 个空格。

任务开始后面板会关闭，网页右下角显示进度和停止按钮。按 **Esc**、切换标签页/窗口、改变输入框或光标、进行键盘和鼠标操作时会停止。已经输入的文字会保留。

如果快捷键冲突，打开 `chrome://extensions/shortcuts` 或 `edge://extensions/shortcuts` 重新指定。如果剪贴板读取失败，可点击「读取剪贴板」重试，或直接在预览框里粘贴、修改文字。

## 支持范围

- 支持具有 selectionStart/selectionEnd 的 text/search/url/tel/password input、textarea 和常见 contenteditable；只读框、数字/日期/email 等不提供文字选区 API 的 input 不支持。
- 支持同源 iframe 和开放的 Shadow DOM；跨域 iframe、关闭的 Shadow DOM 不支持。
- 不支持浏览器内部页面、扩展商店和特殊画布编辑器，例如 Google Docs。
- 本地 HTML 文件需要在扩展详情中手动开启「允许访问文件网址」。
- 本扩展使用 DOM 编辑和合成事件。逐字输入并非真实键盘事件，不能将 isTrusted 设为 true。部分受控或自定义编辑器可能拒绝输入；可以尝试 Windows 桌面版。
- 「普通插入」一次写入纯文本，不发送 Ctrl + V、不派发 paste 事件，也不包含富文本格式。需要原生粘贴语义时，请直接使用浏览器粘贴或桌面版的普通粘贴。
- 不发送 Enter 键或主动提交表单，网页自己的 input 处理仍可能触发其他行为。请先用短文本确认。
- 字数上限 100,000 个 UTF-16 字符；网站本身的 maxlength 和 beforeinput 取消会被尊重。

## 隐私与权限

无联网、无历史记录；本地只存设置。权限用途见 `PRIVACY.md`。

可用仓库里的 `tests/browser-fixture.html` 进行手动测试，或按根目录 README 运行独立 Chromium 自动化测试。
