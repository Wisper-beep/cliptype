# ClipType vNext 架构审查

审查基线：本地干净工作区，提交 `e7995ae21fb015c76a0735447480d589aeef2892`。已逐一读取 src、extension、tests、构建/打包/发布脚本及 CI。最初按要求只在本地实施；用户随后明确授权推送 vnext 和预览包，稳定版保留。

## 基线及主要结论

Windows 是长期维护的核心产品，保留 WinForms + .NET Framework + SendInput。浏览器保持纯 WebExtensions，无原生主机；Linux 单独用 Python 标准库和 Wayland 命令行后端。共享的是文本、节奏和状态约定，不是跨语言运行时。

基线执行：Windows 14 项基础检查、Node 5 项基础测试、Chromium 12 项集成测试通过。既有浏览器测试使用 localhost 权限的临时副本；弹窗测试替换剪贴板和活动标签查询，不能据此宣称工具栏真实授权已验证。新功能结果另记 `VNEXT_TEST_RESULTS.md`。

| 层/问题 | 当前实现及价值 | 成本/风险 | vNext 决策 |
| --- | --- | --- | --- |
| Windows 输入 | ABI 正确，Unicode UTF-16 down/up，代理对在同次 SendInput 中发送 | .NET Framework StringInfo 不完整识别 ZWJ/旗帜；部分发送后的错误处理可能改变原错误码 | P0 保持后端，扩展字符分组测试，保存原错误码 |
| Windows 目标保护 | 前台 HWND、原生焦点 HWND、Esc、鼠标检查 | 无控件内容/网页光标 API；窗口句柄可能重用 | P0 绑定窗口身份；暂停/恢复始终重新验证，不承诺网页光标保护 |
| 原生调度 | 20 ms 小片等待，UI 线程异步 | 布尔 busy + CancellationToken 无正式暂停；长停顿也须响应取消 | P1 显式状态及会话对象，可测试的节奏计算，短片调度 |
| 原生界面 | 全局快捷键、托盘、预览、试输入区 | 固定布局、退出时异步任务仍可能访问已释放托盘；Escape 注册未检查 | P0 生命周期清理；P1 增加节奏设置、进度、暂停热键及冲突提示 |
| 原生设置 | XML、范围校验、原子替换 | 新字段必须兼容旧 XML；不能保存剪贴板 | 追加有默认值的字段；旧文件迁移无需额外运行时 |
| DOM 编辑 | 原生 value setter + input；富文本 execCommand | execCommand 已弃用但仍保留 undo/编辑器兼容价值；不能伪装可信硬件事件 | 保留经测试路径，不扩大特殊编辑器兼容宣称 |
| DOM 目标 | 活动元素、选区、Shadow DOM、同源 iframe | 启动时 focus()/setSelectionRange 会覆盖用户在面板期间的新选择；未校验输入内容变更 | P0 启动不抢回目标，校验原选区/内容；恢复同样校验 |
| DOM 中断 | Esc、鼠标、键盘、隐藏页、焦点轮询 | 无暂停/恢复；keydown 被取消时缺少对称 keyup | P1 状态、进度、暂停控制；异常仍释放逻辑按键；焦点变化立即停止 |
| Firefox | 仅 service_worker，未支持 browser Promise API | Firefox background scripts、签名与清单要求不同 | P0 按目标生成清单；共用逻辑/UI；声明无数据收集；开发包与签名版区分 |
| 浏览器设置 | 执行前保存三项参数 | 关闭面板前修改丢失；打开面板没有内部测试区 | P1 修改即保存设置；独立内部演示不访问网页 |
| 悬浮入口 | 仅任务进度/停止浮层 | 常驻 all_urls 会增大权限；页面 CSS/焦点可能干扰 | P1 可选、仅本次授权页面有效；不申请常驻主机权限 |
| Linux | 尚无实现 | Wayland 不能读取其他进程控件/光标；Niri IPC 与键盘发送之间有竞争窗口 | P2 无特权 wtype + wl-paste；验证 Niri 窗口并监控变化；不伪装 Windows 等级保护 |
| 测试 | 原生单元/集成/窗口 smoke，Chromium 扩展集成 | 缺暂停、Unicode family、生命周期、Gecko DOM 和 Linux 后端测试 | P0/P1 补关键行为回归；按真实执行与替身测试分别记录 |
| 打包/发布 | Windows + 单一扩展 ZIP，手动远程脚本 | 没有 Firefox/Linux 包，旧发布脚本产物名称固定 | 本地多端打包与校验；CI 仅检查/产物，不自动 Release；本次不调用远程写脚本 |

## 状态与暂停约定

会话为 preparing/countdown → running ↔ paused → completed/cancelled/failed。暂停不移动目标、不改变选区；恢复校验身份、焦点以及平台可观察的原目标状态。失败不能通过自动聚焦“修好”。停顿剩余时间在暂停期间冻结，取消从任何活动状态退出。一次普通插入/粘贴是原子任务，不在中间暂停。

Windows 以全局快捷键控制暂停，避免点击设置窗导致目标切换。浏览器以不抢焦点的页内按钮控制，跨标签页直接取消。Linux 以 compositor 绑定调用命令/用户运行目录内控制接口；不假设能截获全局 Esc 或任意控件光标。

## 节奏与范围

固定间隔、可选有界随机比例、标点/句尾及段落额外停顿，默认保持既有固定速度。每次按完整字符单元发送；不插入错字或删除用户内容来制造效果。所有延迟均可中断。各语言实现相同可解释规则，分别测试，不通过共享服务引入运行时依赖。

## 保留的限制

SendInput 受 UIPI 限制，不自动提权。网页合成 KeyboardEvent 不可信，富文本/框架兼容取决于网页。Linux 窗口级 IPC 不等于控件级验证，不保证焦点切换边界的零泄漏。Firefox 正式持久安装需要 Mozilla 签名，本次只生成开发构建。

依据：[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)、[MDN background](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/background)、[Firefox 签名](https://extensionworkshop.com/documentation/publish/signing-and-distribution-overview/)、[Niri IPC](https://github.com/niri-wm/niri/blob/main/docs/wiki/IPC.md)。

## 实施后复审

另行复审了三条后端的边界：Windows 对物理分段按下的暂停组合先抑制输入并等待识别，非暂停修饰键仍取消；取消令牌从第一次剪贴板读取前就存在，退出不会让延迟预读重新打开界面或发送 Ctrl+V。浏览器比较同步处理器执行后的目标值/选区，并修复快捷入口的 hidden 样式与任务生命周期。Linux 处理新开窗口自带焦点事件，重查查询期间的失焦/取消，并串行确认暂停与发送边界。所有修复附行为回归；真实 Niri 和最终完整原生 GUI 的未完成项仍保留在测试报告中。
