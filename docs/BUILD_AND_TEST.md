# 构建、安装和实际验证

## Windows

```powershell
.\build.ps1 -Test
.\dist\ClipType.Tests.exe --integration
.\dist\ClipType.Tests.exe --ui-smoke
.\dist\ClipType.Tests.exe --desktop
.\scripts\package.ps1
```

Framework 编译器生成 `dist/ClipType.exe`，不依赖 Node/Python 运行。`--integration` 和 `--desktop` 需要正常可交互 Windows 桌面、剪贴板和输入权限；请勿切换窗口或运行会抢焦点的其他软件。受限沙箱拒绝 SendInput/剪贴板时应在正常会话运行，不要自动提权或关闭安全限制。

`--desktop` 使用自建独立进程目标，验证热键、实际原生面板、慢速按下暂停组合、普通粘贴、Esc、托盘和生命周期。它不会向任意用户窗口写入；目标切换应使测试失败。测试夹具仅在被忽略目录内记录示例文字，处理输入事件中暂时不完整的 UTF-16；不把夹具日志编码问题算作产品输入失败。

主程序可直接解压运行。已有 XML 设置继续兼容；新节奏项默认关闭，旧速度不变。更换暂停组合后会在任务启动时检查冲突。

## 浏览器

```sh
npm ci
npm test
python3 scripts/build_extensions.py
npm run lint:firefox
npx playwright install --with-deps chromium firefox
npm run test:browser
npm run test:firefox
npm run test:firefox:install
```

Windows 可用 `python` 代替 python3。可选 `CLIPTYPE_CHROMIUM` / `CLIPTYPE_FIREFOX` 指定已安装的测试浏览器可执行文件；Playwright 的 Firefox 协议需与测试库匹配。临时安装使用 `CLIPTYPE_FIREFOX_INSTALL`，缺省为 Playwright Firefox。测试均使用独立配置，不修改个人浏览器。

Chromium 集成测试为临时副本增加 localhost 权限来替代人工 activeTab 授权；生产清单没有该权限。弹窗用固定文字替代剪贴板读取、固定测试活动标签，其余注入/消息传递真实执行。Gecko DOM 测试的消息通道是替身，不能当作完整 Firefox 扩展端到端测试；另有 Mozilla lint 和实际临时安装检查。

Chrome/Edge：解压 Chromium ZIP，在扩展管理页加载目录。Firefox：解压 Firefox ZIP，在 `about:debugging#/runtime/this-firefox` 临时加载其中 manifest.json。正式持久安装需要 Mozilla 签名，不更改浏览器签名设置。

手工实际浏览器检查：工具栏/快捷键 activeTab、真实剪贴板读取、页内暂停/停止按钮、设置保存、同源 iframe、限制页面提示。对第三方网站先用示例短文本，核对是否发生网站特有处理。内部测试页只演示节奏，不代表第三方网站兼容。

## Linux

```sh
python3 -m unittest discover -s linux/tests -v
python3 scripts/package_linux.py
python3 dist/cliptype.pyz doctor
```

`doctor` 仅做环境/依赖预检查。真实 Niri 验证需按 `linux/README.md` 安装依赖、配置快捷键，逐项检查 CJK/Emoji、多行、中间光标、暂停恢复、停止、目标切换、关闭目标、IPC 断开和错误状态。只有在真实 Arch+Niri 下执行后，才能确认输入精度和停止延迟。WSL 的替身后端测试不能替代它。

本地 Arch 包使用压缩包附带 PKGBUILD，由用户执行 makepkg；本次没有安装系统组件或提交 AUR。

## CI 与发布边界

仓库配置 Windows 基础构建、Linux Python/socket 测试、Chromium/Gecko 检查和开发包构建。需要真实桌面的用例保留独立流程；托管 runner 的编译/单元成功不等于原生交互成功。

远程结果以 [vnext 分支 Actions](https://github.com/Wisper-beep/cliptype/actions) 为准，不由本地通过推断。自动流程只检查和上传工作流产物，不创建 Release。用户已授权本次 vnext 推送与预览包；签名、商店和稳定版发布不包含在内。`scripts/github.ps1` 不会被打包或 CI 自动调用。
