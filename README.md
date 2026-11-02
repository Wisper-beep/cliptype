# ClipType

把剪贴板文字输入到光标处。Windows 原生应用、Chrome / Edge / Firefox 扩展，以及 Linux / Niri 原生原型各自使用合适的输入后端。

当前源码为 **vNext 1.1.0 开发预览**，对应 `v1.1.0-rc.1`。Firefox 包未签名，Linux 仍是原型；稳定版 `v1.0.0` 保留。[预览下载](https://github.com/Wisper-beep/cliptype/releases/tag/v1.1.0-rc.1) · [所有版本](https://github.com/Wisper-beep/cliptype/releases)

[架构审查](docs/ARCHITECTURE_REVIEW.md) · [竞品研究](docs/COMPETITOR_ANALYSIS.md) · [兼容矩阵](docs/COMPATIBILITY.md) · [构建与测试](docs/BUILD_AND_TEST.md) · [实际测试结果](docs/VNEXT_TEST_RESULTS.md) · [CHANGELOG](CHANGELOG.md)

## 选择版本

| | Windows EXE | 浏览器扩展 | Linux 原型 |
| --- | --- | --- | --- |
| 输入方式 | Windows SendInput / Ctrl+V | DOM 编辑及合成事件 | wtype / Wayland virtual-keyboard |
| 使用位置 | 网页和桌面程序 | 常见网页输入框 | Arch + Niri 中已聚焦窗口 |
| 调用 | Ctrl+Alt+V | Alt+Shift+V | 用户配置 Niri 绑定 |
| 暂停/继续 | 默认 Ctrl+Alt+Space，可更换 | 页内按钮或 Alt+Shift+P | pause / resume 命令 |
| 停止 | Esc、焦点或窗口改变 | Esc、焦点/选区改变、页面按钮 | stop 命令、Niri 停止绑定、窗口/IPC 改变 |
| 主要边界 | 无网页光标 API，遵守 UIPI | 不产生可信硬件按键 | 只验证窗口，不能保证同窗口控件和光标安全 |

Windows EXE 是持续维护的核心产品，继续使用 WinForms 和 .NET Framework，没有迁移到 Electron 或依赖浏览器扩展。

## 使用

**Windows**：运行 `dist/ClipType.exe`，复制文字，在目标框放好光标，按 Ctrl+Alt+V 选择模拟输入或普通粘贴。主窗有本地试输入区；关闭会收起到托盘。[完整说明](docs/windows.md)

**Chrome / Edge**：解压 `ClipType-Chromium.zip`，在扩展管理页开启开发者模式，加载包含 manifest.json 的目录。点击网页输入框，再调用扩展检查文字与位置。[扩展说明](extension/README.md)

**Firefox 桌面 142+**：解压 `ClipType-Firefox.zip`，在 `about:debugging#/runtime/this-firefox` 临时加载 manifest.json。它是**未签名开发包**，不等于可以永久安装的正式 XPI；后续签名需单独授权和 Mozilla 流程。

**Linux / Niri**：解压 `ClipType-Linux.tar.gz`，安装 Python、wtype、wl-clipboard 和 Niri 后，用 compositor 绑定调用 `cliptype.pyz run`；先运行 doctor 做环境预检查。没有 root/uinput 服务，没有通过 Wine 运行 EXE。[Linux 说明与限制](linux/README.md)

## 新增功能

- 固定或有界随机间隔，标点、句尾和空行停顿；默认保持原有固定速度。
- 任务状态、暂停、恢复、停止和进度；恢复校验原目标，不自动把失效焦点拉回。
- 常见中文、Emoji、组合字符；在原光标/选区处插入，Tab 转为 4 个空格。
- 浏览器速度预设、参数即时保存、内部节奏测试区，以及仅在本次授权页面生效的可选快捷按钮。
- 不联网、不保存剪贴板历史；浏览器没有全站常驻脚本或 host_permissions。

浏览器逐字输入不发送 Enter 或 paste 事件，也不主动提交表单；网站自己的处理仍可能产生行为。特殊编辑器、跨域 iframe 和关闭 Shadow DOM 不在保证范围内。Linux 是有明确焦点限制、尚待真实 Niri 试打的原型。详见[兼容矩阵](docs/COMPATIBILITY.md)。

## 构建

```sh
npm ci
npm test
python3 scripts/build_extensions.py
npm run lint:firefox
npx playwright install --with-deps chromium firefox
npm run test:browser
npm run test:firefox
python3 -m unittest discover -s linux/tests -v
python3 scripts/package_linux.py
```

Windows 下可用 python 替代 python3。桌面构建和所有本地打包：

```powershell
.\build.ps1 -Test
.\scripts\package.ps1
```

`dist` 包含 Windows、Chromium、Firefox、Linux 的独立包、可直接调用的 `cliptype.pyz` 和 `SHA256SUMS.txt`。保留 `ClipType-Extension.zip` 作为 Chromium 包的旧名称别名。Node/Python 只是 Windows 构建/测试工具，不是运行 EXE 的依赖。

CI 配置包含 Windows 构建、Chromium/Gecko 检查、Linux socket 测试和打包；远程结果见 [Actions](https://github.com/Wisper-beep/cliptype/actions)。真实桌面与 Niri 验证流程见文档，未执行的测试不算通过。

MIT License。参考项目只借鉴设计概念；源码、授权核实及取舍见竞品分析。
