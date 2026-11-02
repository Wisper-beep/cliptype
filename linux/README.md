# ClipType Linux / Niri 原型

这是 Python 标准库实现的原生 Linux 用户进程，通过 `wtype` 的 Wayland virtual-keyboard 协议输入；不使用 Wine、不部署 root/uinput 服务。

依赖：Python 3.10+、Niri、`wtype`、`wl-clipboard`。Arch 可由用户自行安装这些包。实际 Niri 会话应提供 `WAYLAND_DISPLAY`、`NIRI_SOCKET`、`XDG_RUNTIME_DIR`。WSL 的 Linux 测试通过不代表在 Niri 下完成真实按键验证。

## 启动

解压 `ClipType-Linux.tar.gz`，保留 `cliptype.pyz`。tar 包包含执行位，也可用 `python3 cliptype.pyz` 运行。

```sh
./cliptype.pyz doctor
./cliptype.pyz --help
./cliptype.pyz run --interval 30 --delay 800
./cliptype.pyz status
./cliptype.pyz pause
./cliptype.pyz resume
./cliptype.pyz stop
```

从终端运行 `run` 会把**终端自身**视为目标；实际输入请用 Niri 快捷键触发，让原输入框保持焦点。程序不打开窗口、不自动抢回焦点。任务运行到完成/停止后退出，无常驻剪贴板监控。

将 `niri-bindings.kdl` 中的绑定合并到现有配置，改为 pyz 的绝对路径。默认建议：Mod+Shift+V 开始，Mod+Shift+P 暂停，Mod+Shift+R 恢复，Mod+Shift+Escape 停止。不要设置 allow-when-locked 或 pass-through，也不要覆盖自己的已有绑定。调用后松开快捷键，等待默认的 800 ms。

## 行为

- 只在明确调用 `run` 时读取一次剪贴板，不保存正文或历史。
- 输入间隔 0–500 ms；`--variation 20` 表示 ±20% 随机浮动（默认 0 为固定）。
- `--punctuation 200 --sentence 400 --paragraph 800` 为额外毫秒停顿；段落指连续两个换行。所有停顿均可取消。
- 默认 `--newlines space` 将换行改为空格，避免向未知应用发送 Enter；可显式选 `shift-enter` 或 `enter`。Tab 转为四个空格。
- CJK、普通 Emoji、组合重音、旗帜、ZWJ 和肤色修饰符按完整单元调度；轻量分组不是完整 UAX29 实现。
- `stop` 可在另一进程通过用户运行目录的 mode-0600 socket 取消任务。子进程尚未退出时会终止本任务的 wtype。普通 Esc 不会被全局截获；请用配置好的停止绑定。
- `status` 只含状态和计数，适合 bar/menu 调用；控制 socket 仅接受同 uid 的状态/暂停/恢复/停止请求。目录 mode-0700，锁防止重复输入。

## 焦点保护的实际边界

同时使用 Niri 初始窗口快照、持续事件流和每次输入前的 focused-window 查询。窗口改变、IPC 出错、事件流断开时取消；即使切走后返回，也不恢复旧任务。暂停期间继续监控，恢复再次验证。

**窗口级校验不等于控件/光标级校验。** Wayland/Niri 不提供 Windows GUIThreadInfo 等价能力。相同窗口内点击另一控件或移动光标不能可靠发现。Niri IPC 与虚拟键盘发送也不是一个原子操作；焦点切换或事件延迟的边界仍可能投递少量已发出的字符。每次仅发送一个完整字符单元以缩小范围，不能承诺零泄漏。

因此当前是 Niri 专用原型，不建议用于密码、付款、终端命令或其他误输入后果严重的场景。不要在输入中移动光标。其他 compositor 若没有所需接口，会明确拒绝运行，不会静默降级到无保护输入。程序不自动提权、不操作锁屏、不启用高权限后台服务。

## 构建与检查

仓库根目录运行：

```sh
python3 -m unittest discover -s linux/tests -v
python3 scripts/package_linux.py
python3 dist/cliptype.pyz doctor
```

单元/Unix socket 测试的 Niri 和 wtype 是替身；必须另在真实 Arch+Niri 中执行：普通文本编辑器中间光标、CJK/Emoji、多行、暂停恢复、停止、切窗、关闭目标、停止 compositor、长文本、缺依赖。使用自绘测试窗口，逐项核对输出及停止延迟。尚未执行的真实平台用例见 `docs/VNEXT_TEST_RESULTS.md`。

本地 Arch 包：把压缩包中的 PKGBUILD、pyz、README、LICENSE 放在同一目录，用户可运行 `makepkg -si`。`SKIP` 校验只适用于自己构建的本地原型；没有提交 AUR，也没有自动安装或正式发行。
