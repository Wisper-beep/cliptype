# v1.0.0 验证范围（历史记录）

vNext 的结果见 `VNEXT_TEST_RESULTS.md`；本文件保留首次版本的验证范围。

## 浏览器扩展

- 5 项 Node 基础测试通过：文本标准化、完整字符切分、设置校验、最小权限清单和脚本语法。
- 12 项独立 Chromium 集成测试通过：选区替换、中文/表情/多行输入、单行转换、一次插入、富文本、Shadow DOM、同源 iframe、Esc 停止、焦点切换、maxlength / beforeinput 取消、受控输入框的原生 setter、重复注入和无效任务处理，以及弹窗 UI 消息流程与存储检查。
- 输入测试确认没有派发 paste 或 Enter 事件。保存结果只有 settings，无剪贴板文字。
- 测试使用独立 Chromium 用户配置和 127.0.0.1 测试服务器，不操作现有浏览器。
- 为替代人工通过工具栏授予 activeTab，临时测试副本增加了 localhost 站点权限。生产 manifest 没有 host_permissions；基础测试会检查这一点。
- 弹窗以扩展页面形式测试，其中剪贴板读取返回固定测试文字，活动标签查询固定为测试网页。脚本注入和消息传递均使用真实的 Chrome 扩展 API。
- 工具栏真实激活、系统剪贴板读取、Edge 专属行为和第三方网站仍需手动试用，不作全网站兼容保证。

运行方式见根目录 README。图标为项目自绘几何图形；截图来自本地弹窗测试。

## Windows 桌面版

14 项基础检查通过，之前已完成原生 SendInput 集成测试及快捷键 UI 验证。桌面版的详细验证范围见 `windows-verification.md`。
