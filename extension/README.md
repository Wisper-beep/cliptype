# ClipType 浏览器扩展

共用的 Manifest V3 输入逻辑与 UI，分别生成 Chromium 和 Firefox 清单。不需要 Windows EXE 或原生主机。

## 安装

- **Chrome / Edge 109+**：解压 `ClipType-Chromium.zip`，打开 chrome://extensions 或 edge://extensions，开启开发者模式，加载包含 manifest.json 的目录。
- **Firefox 桌面 142+**：解压 `ClipType-Firefox.zip`，在 about:debugging#/runtime/this-firefox 临时加载 manifest.json。后台使用 scripts，而非 service_worker；声明不收集数据。
- Firefox ZIP 尚未签名，重启浏览器后临时安装失效。正式持久安装须提交 Mozilla 签名流程；本次没有签名、上架或变更浏览器安全设置。
- 不移动已经加载的目录。源代码的 extension/manifest.json 是 Chromium 入口；Firefox 应使用构建出的 dist/firefox 或 Firefox ZIP。

## 使用

复制文字 → 点击目标输入框放好光标/选区 → 点击 ClipType 图标或按 **Alt+Shift+V** → 检查预览 → 逐字输入或普通插入。

逐字输入支持 0–500 ms 间隔、速度预设、±0–90% 浮动及标点/句尾/空行额外停顿。默认固定 30 ms，500 ms 后开始。单行框或选择空格模式时，换行渲染为空格，但仍保留原空行的节奏；Tab 转为 4 个空格。

任务浮层显示进度，可暂停/继续/停止。**Alt+Shift+P** 暂停或继续，**Esc** 停止。暂停期间仍检查目标；恢复不抢回已改变的焦点、选区或内容。切换页面、目标失效或用户操作会停止，已经输入的文字保留。

设置修改即保存在本地，正文不会保存。内部测试区只演示切分/节奏和状态，不代表第三方编辑器兼容。

## 可选本页快捷按钮

展开“节奏与本页快捷按钮”后启用。按钮仅在已调用/授权的当前页面生效，不申请所有网站权限，也不会自动注入新页面。输入任务期间隐藏，离开输入框隐藏；可随时关闭。较老浏览器不能由按钮打开面板时，会提示手动点击工具栏图标。

## 支持与限制

- 普通 text/search/url/tel/password input、textarea、常见 contenteditable；只读和不提供文字选区 API 的 input（如 number/date/email）会拒绝。
- 同源 iframe 和开放的 Shadow DOM；跨域 iframe、关闭 Shadow DOM、浏览器内部页面、扩展商店和 Google Docs 等特殊编辑器不在支持范围。本地文件需手动允许文件网址访问。
- 逐字输入使用 DOM 编辑和合成事件，不能伪装硬件键盘或修改 KeyboardEvent.isTrusted。网站可拒绝 beforeinput/keydown；程序会停止并提示。
- 标准文本框会验证写入后的值和选区。富文本依赖编辑器的原生编辑命令，提前注册的 window capture 处理器和自定义规范化无法通用验证，不保证所有富文本平台兼容。
- 普通插入一次写入纯文本，不发送 Ctrl+V 或 paste，不带富文本格式；真实粘贴语义请用浏览器原生粘贴或桌面版。
- 不发送 Enter 或主动提交表单，网页自身的事件处理仍可能产生其他行为。先用短示例确认目标程序行为。
- 单次最多 100,000 个 UTF-16 字符，尊重 maxlength 和 beforeinput 取消。

快捷键冲突可在浏览器扩展快捷键页修改。读取剪贴板失败时可重试，或在预览框手动粘贴、编辑。[权限说明](PRIVACY.md)
