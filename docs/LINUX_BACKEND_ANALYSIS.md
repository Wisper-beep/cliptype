# Linux / Niri 后端选择

目标是 Arch Linux + Niri + Wayland 的原生用户进程。Windows SendInput 不移植到 Linux，也不通过 Wine 使用 EXE。

| 方案 | 权限/依赖 | Unicode 与目标保护 | 取舍 |
| --- | --- | --- | --- |
| 自写 Wayland virtual-keyboard 客户端 | compositor 必须公开协议；需要维护键盘映射、连接和生命周期 | 能用 Unicode keysym；协议本身不给外部控件/光标身份 | 可行但原型成本高，不重复实现成熟客户端 |
| wtype | 无特权客户端；依赖 virtual-keyboard、Wayland/xkbcommon | 上游支持 Unicode；每次一个完整字符缩小在途输入；没有目标原子绑定 | **选用**，以外部工具进程隔离后端，正文走 stdin |
| wl-clipboard | 剪贴板协议由 compositor 提供 | 只负责一次读取文字，不是输入模拟器 | **选用 wl-paste**，不使用 watch、不保存历史 |
| XDG Desktop Portal RemoteDesktop / GlobalShortcuts | 依赖 portal 后端与用户会话授权，能力随桌面而异 | 远程输入会话不是通用的文本/光标 API；仍需布局和焦点策略 | 后续候选，不把 Niri 的 portal 能力当作既定事实 |
| ydotool / uinput | 通常需要对 /dev/uinput 的额外权限和后台服务 | 内核事件不绑定指定窗口；布局/Unicode 需额外处理 | 不作为默认后端，不为了功能部署高权限服务 |
| X11 xdotool | 依赖 X server | 不覆盖原生 Wayland 窗口 | 不适用于当前目标 |

## 确认的上游接口

Niri 官方源码快照 `a8562a1126922ed4c1c94d34c1dbb26633491d05` 的 `src/handlers/virtual_keyboard.rs` 实现 `zwp_virtual_keyboard_v1`。IPC 文档定义 `$NIRI_SOCKET` 上的单行 JSON 请求、`FocusedWindow` 与 `EventStream`，事件提供初始 `WindowsChanged` 和后续焦点/窗口事件。脚本使用稳定 JSON，而非解析人类格式输出。

wtype 研究快照 `4b9cc29664f178cf380ccfe7f98405d38dffda3c` 的 manpage 定义 stdin `-`、`-k`、`-M/-m`。本程序不嵌入其源码，而是调用系统安装的命令。普通文字不进入 argv，换行可显式选择 Shift+Return / Return，默认渲染为空格。逻辑 LF 保留用于段落计时。

## 安全边界

初始窗口 id + 窗口事件流 + 每次输入前的同步窗口查询。事件流检测切走再返回，不因为当前 id 恢复就清除失焦标记；连接异常也停止。暂停和恢复使用同一 guard。控制接口为当前 uid 的私有 Unix socket，禁止外部任意命令执行。

这些接口不能原子地“验证窗口并发送键盘事件”，也无法获得同窗口中其他进程控件的真实光标。Niri 事件延迟和最后查询后的切换仍有竞争窗口，不能保证零泄漏。暂停/停止正常情况下按约 20 ms 小片响应，还受 IPC 和子进程调度延迟影响，不是实时系统。缺少 Niri 接口会拒绝输入，不静默取消保护。

UI 选择轻量 CLI/用户级单次进程，通过 Niri 配置触发 run/pause/resume/stop；可供 bar/menu 调用 status。原型不部署常驻服务或托盘依赖。Arch PKGBUILD 仅用于用户本地构建，没有 AUR 上传。真实 Niri 试打尚未执行；Ubuntu WSL 只验证标准库、socket 和替身后端。

来源：[Niri IPC](https://github.com/niri-wm/niri/blob/main/docs/wiki/IPC.md)、[Niri virtual-keyboard](https://github.com/niri-wm/niri/blob/a8562a1126922ed4c1c94d34c1dbb26633491d05/src/handlers/virtual_keyboard.rs)、[Niri 事件定义](https://docs.rs/niri-ipc/latest/niri_ipc/enum.Event.html)、[wtype](https://github.com/atx/wtype)、[wl-clipboard](https://github.com/bugaevc/wl-clipboard)、[Portal RemoteDesktop](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.RemoteDesktop.html)、[ydotool](https://github.com/ReimuNotMoe/ydotool)。
