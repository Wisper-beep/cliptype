# 参考项目研究与取舍

研究时间：2026-11-02。参考源码仅下载到被 Git 忽略的 `.research/`；没有复制第三方实现到产品。

## 证据与许可

- [AutoType Clipboard](https://github.com/arunroy6/autotype-clipboard/tree/0fe0b9c16e9a6e98ff9f7ac2d35238d3f41c14b4)：读取 manifest、background、content 的输入循环与 setter、popup 与 README。MV3 Firefox background scripts、全站 content script、随机延迟、悬浮按钮、测试框、Esc。`performTyping` 按 UTF-16 索引，循环重新 focus，延迟整段等待；没有可见自动化测试。README 有 MIT 徽章，但该快照没有 LICENSE 授权正文，因此不复制源码，也不把徽章当作已核实授权。
- [Text to Write](https://github.com/minkrov/ProjectsVC/tree/992f5973e9447104d41982715b61dc9856c2f0b1/Text%20to%20Write)：读取 manifest、typing-options、content、sidebar 和 background 控制/调度代码及手工 test-page。MV2 Firefox 侧栏、帧路由、暂停、节奏选项、错字修正；背景计时转发试图保持后台输入。根目录和该子目录没有 LICENSE 正文，不能推断可复用授权。无自动测试套件可见。只借鉴交互概念。

参考项目描述中的“真实键盘”不构成能力证据；其 JavaScript KeyboardEvent 仍是合成事件。ClipType 不采用相应营销说法。

## 功能对标与优先级

| 功能 | ClipType 现状 | 参考设计 | 技术价值 | 成本/风险 | 建议与适用端 |
| --- | --- | --- | --- | --- | --- |
| 随机间隔 | 固定毫秒 | A：比例抖动；B：多种变速 | 简单、有界且可解释 | 低；极端配置拖慢输入 | P1 双端，可关闭，默认固定 |
| 标点/句尾停顿 | 无 | B：标点与句子阶段 | 长文本节奏易控制 | 低；中英文标点应一致 | P1 Windows/浏览器；Linux 同规则 |
| 段落停顿 | 无 | B：段落犹豫 | 阅读/校对便利 | 低；换行须防误发送 | P1，仅计时，不制造 Enter |
| 进度 | 原生文字计数、网页浮层 | B：侧栏进度、节流 | 用户能确认剩余内容 | 低；不能记录原文 | P1 双端，计数/百分比 |
| 暂停/恢复 | 仅取消 | B：会话与帧状态 | 避免重新复制长文本 | 中；恢复时目标可能已变 | P1 双端，校验原目标，绝不自动改焦点 |
| 快速速度设置 | 毫秒数 | B：slow/medium/fast；A：滑块 | 降低设置成本 | 低；不要宣称精确 WPM | P1 预设 + 毫秒精调 |
| 悬浮快捷入口 | 仅任务浮层 | A：输入旁按钮/可拖动入口 | 重复调用方便 | 中；全站权限、焦点干扰 | P1 浏览器，仅当前授权页面、可关闭 |
| 内部测试区 | Windows 已有 | A：面板演示；B：test-page | 快速确认速度、无需改网页 | 低；演示与真实网页测试须区分 | P1 浏览器增加，不保存测试文字 |
| 设置持久化 | 开始任务时保存 | A/B：交互保存 | 重开保持用户偏好 | 低；不得顺带保存待输入正文 | P1 修改即保存参数 |
| 模拟错字/修正 | 无 | B：邻键/漏字/退格修正 | 对准确输入价值有限 | 高；中英文、组合字符、网站副作用 | 暂不实现，P3 明确延后 |
| Firefox | 无 | A：MV3 scripts；B：MV2 侧栏 | 覆盖 Gecko 用户 | 中；签名、背景/API 差异 | P0 独立清单，共用 MV3 逻辑 |
| 后台持续输入 | 隐藏页立即停止 | B：背景计时转发 | 某些场景不中断 | 高；违背当前目标安全约定 | 不采纳，保留离开页面停止 |
| 帧路由 | 同源递归 | B：跟踪活动帧与跨帧控制 | 可扩大编辑器覆盖 | 中高；权限及目标复杂 | 本轮不扩跨域，保持最小权限 |

A = AutoType Clipboard，B = Text to Write。两者均是浏览器实现，不能替代 Windows SendInput，也不能作为 Linux Wayland 后端。

## 实施记录

采用独立实现的有界抖动、语义停顿、显式会话状态、进度与内部测试。保留 ClipType 已有完整字符切分、原生 setter、目标/选区校验和不发 Enter 的浏览器策略。Firefox 通过生成不同 background 声明支持，不搬入全站脚本或侧栏常驻调度。实际成果、测试和遗留差异见 `VNEXT_TEST_RESULTS.md`、`COMPATIBILITY.md`。
