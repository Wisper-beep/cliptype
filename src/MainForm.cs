using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipType
{
    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 41, EscapeId = 42, PauseId = 43;
        private static readonly string[] PauseNames = { "Ctrl + Alt + Space", "Ctrl + Shift + F9" };
        private static readonly string[] HotkeyNames = { "Ctrl + Alt + V", "Ctrl + Alt + P", "Ctrl + Shift + F8" };
        private static readonly uint[] HotkeyModifiers = { 3, 3, 6 };
        private static readonly uint[] HotkeyKeys = { 0x56, 0x50, 0x77 };
        private readonly Settings settings;
        private readonly NotifyIcon tray;
        private readonly Label status, shortcut, speedDescription;
        private readonly ComboBox hotkeys, newlines;
        private readonly NumericUpDown interval, delay;
        private readonly TextBox sandbox;
        private readonly Button stop;
        private readonly CheckBox randomize;
        private readonly NumericUpDown variation, punctuation, sentence, paragraph;
        private readonly ComboBox pauseKeys;
        private readonly ProgressBar progressBar;
        private readonly Func<CancellationToken, Task<string>> readClipboard;
        private InputSession session;
        private bool registered, busy, exiting, ready;
        private ChoiceForm chooser;
        private CancellationTokenSource cancellation;

        internal MainForm(Func<CancellationToken, Task<string>> clipboardReader = null)
        {
            readClipboard = clipboardReader ?? new Func<CancellationToken, Task<string>>(ReadClipboardAsync);
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            settings = Settings.Load();
            Text = "ClipType · 剪贴板模拟输入";
            ClientSize = new Size(740, 746);
            AutoScroll = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            Font = Theme.Font(10);
            Icon = Theme.MakeIcon();

            Controls.Add(Theme.Label("ClipType", 28, 23, 300, 42, 25, true));
            var tag = Theme.Label("剪贴板 → 光标处", 509, 36, 203, 26, 11, true);
            tag.ForeColor = Theme.Accent; tag.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(tag);
            var sub = Theme.Label("把复制的文字，像打字一样输入。", 30, 75, 650, 28, 11);
            sub.ForeColor = Theme.Muted; Controls.Add(sub);

            var hero = Theme.Card(28, 119, 684, 145);
            Controls.Add(hero);
            hero.Controls.Add(Theme.Label("在目标输入框放好光标，然后按", 22, 18, 630, 26, 11));
            shortcut = Theme.Label(HotkeyNames[settings.HotkeyIndex], 20, 49, 400, 44, 24, true);
            shortcut.ForeColor = Theme.Accent; hero.Controls.Add(shortcut);
            hero.Controls.Add(Theme.Label("选择「模拟输入」或「普通粘贴」，文字会回到原来的位置。", 22, 108, 640, 24, 9.5f));

            var tabs = new TabControl { Location = new Point(28, 278), Size = new Size(684, 245) };
            var basic = new TabPage("基础设置");
            var rhythm = new TabPage("节奏与暂停") { BackColor = Color.White };
            tabs.TabPages.Add(basic); tabs.TabPages.Add(rhythm); Controls.Add(tabs);
            var options = Theme.Card(0, 0, 674, 207);
            basic.Controls.Add(options);
            options.Controls.Add(Theme.Label("输入设置", 20, 15, 620, 27, 12, true));
            options.Controls.Add(Theme.Label("全局快捷键", 22, 59, 117, 25, 10));
            hotkeys = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(141, 55), Size = new Size(201, 30), Font = Theme.Font(10) };
            hotkeys.Items.AddRange(HotkeyNames); hotkeys.SelectedIndex = settings.HotkeyIndex;
            options.Controls.Add(hotkeys);
            options.Controls.Add(Theme.Label("开始前等待", 364, 59, 112, 25, 10));
            delay = Number(settings.StartDelayMs, 300, 5000, 477, 55, 110, 100);
            options.Controls.Add(delay); options.Controls.Add(Theme.Label("ms", 599, 60, 50, 25, 9));
            options.Controls.Add(Theme.Label("逐字间隔", 22, 109, 110, 25, 10));
            interval = Number(settings.IntervalMs, 0, 500, 141, 104, 94, 5);
            options.Controls.Add(interval); options.Controls.Add(Theme.Label("ms", 247, 109, 40, 25, 9));
            speedDescription = Theme.Label("", 295, 109, 364, 25, 9);
            speedDescription.ForeColor = Theme.Muted; options.Controls.Add(speedDescription);
            options.Controls.Add(Theme.Label("文本里的换行", 22, 157, 120, 25, 10));
            newlines = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(141, 151), Size = new Size(282, 30), Font = Theme.Font(10) };
            newlines.Items.AddRange(new object[] { "Shift + Enter（聊天换行）", "Enter（编辑器换行）", "替换为空格（单行输入）" });
            newlines.SelectedIndex = (int)settings.Newlines; options.Controls.Add(newlines);
            var hint = Theme.Label("Tab 转为 4 个空格", 441, 157, 221, 26, 9);
            hint.ForeColor = Theme.Muted; options.Controls.Add(hint);

            randomize = new CheckBox { Text = "随机间隔", Checked = settings.Randomize, Location = new Point(20, 20), Size = new Size(125, 28) };
            variation = Number(settings.VariationPercent, 0, 90, 150, 18, 75, 5);
            rhythm.Controls.Add(randomize); rhythm.Controls.Add(variation);
            rhythm.Controls.Add(Theme.Label("± %（关闭时使用固定间隔）", 237, 23, 395, 25, 9));
            rhythm.Controls.Add(Theme.Label("标点停顿 ms", 20, 69, 120, 26, 9));
            punctuation = Number(settings.PunctuationPauseMs, 0, 5000, 150, 62, 100, 50); rhythm.Controls.Add(punctuation);
            rhythm.Controls.Add(Theme.Label("句尾停顿 ms", 340, 69, 125, 26, 9));
            sentence = Number(settings.SentencePauseMs, 0, 5000, 474, 62, 120, 50); rhythm.Controls.Add(sentence);
            rhythm.Controls.Add(Theme.Label("空行停顿 ms", 20, 114, 125, 26, 9));
            paragraph = Number(settings.ParagraphPauseMs, 0, 10000, 150, 107, 100, 100); rhythm.Controls.Add(paragraph);
            rhythm.Controls.Add(Theme.Label("暂停 / 继续", 20, 159, 125, 26, 9));
            pauseKeys = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 153), Size = new Size(255, 30) };
            pauseKeys.Items.AddRange(PauseNames); pauseKeys.SelectedIndex = settings.PauseHotkeyIndex; rhythm.Controls.Add(pauseKeys);
            rhythm.Controls.Add(Theme.Label("保持目标窗口；Esc 停止", 423, 159, 235, 26, 8.5f));

            var test = Theme.Card(28, 537, 684, 119);
            Controls.Add(test);
            test.Controls.Add(Theme.Label("本地试输入", 20, 14, 160, 26, 11, true));
            var testHint = Theme.Label("复制一段文字，点击下方后按快捷键试试。", 190, 17, 474, 24, 9);
            testHint.ForeColor = Theme.Muted; test.Controls.Add(testHint);
            sandbox = new TextBox { Location = new Point(22, 48), Size = new Size(640, 52),
                Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical,
                Font = Theme.Font(10), BorderStyle = BorderStyle.FixedSingle, AccessibleName = "本地试输入文本框" };
            test.Controls.Add(sandbox);

            progressBar = new ProgressBar { Location = new Point(30, 666), Size = new Size(454, 8), Maximum = 100 };
            Controls.Add(progressBar);
            status = Theme.Label("正在注册快捷键…", 30, 683, 466, 53, 9);
            status.ForeColor = Theme.Muted; Controls.Add(status);
            stop = Theme.Button("停止  Esc", 500, 680, 108, 37, false);
            stop.Enabled = false; stop.Click += delegate { CancelInput(); };
            Controls.Add(stop);
            var hide = Theme.Button("收起", 620, 680, 92, 37, true);
            hide.Click += delegate { Hide(); };
            Controls.Add(hide);

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开设置", null, delegate { ShowSettings(); });
            menu.Items.Add("停止输入", null, delegate { CancelInput(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出 ClipType", null, delegate { ExitApp(); });
            tray = new NotifyIcon { Icon = Icon, Text = "ClipType · 剪贴板模拟输入", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(); };
            Shown += delegate
            {
                registered = Register(settings.HotkeyIndex);
                SetStatus(registered ? "已就绪 · 关闭窗口后继续在托盘运行" : "快捷键被占用，请在上方换一个组合。", !registered);
                ready = true; UpdateSpeed();
            };
            hotkeys.SelectedIndexChanged += delegate { SaveOptions(); };
            newlines.SelectedIndexChanged += delegate { SaveOptions(); };
            interval.ValueChanged += delegate { SaveOptions(); };
            delay.ValueChanged += delegate { SaveOptions(); };
            randomize.CheckedChanged += delegate { SaveOptions(); };
            variation.ValueChanged += delegate { SaveOptions(); };
            punctuation.ValueChanged += delegate { SaveOptions(); };
            sentence.ValueChanged += delegate { SaveOptions(); };
            paragraph.ValueChanged += delegate { SaveOptions(); };
            pauseKeys.SelectedIndexChanged += delegate { SaveOptions(); };
            ResumeLayout(true);
        }

        private static NumericUpDown Number(int value, int min, int max, int x, int y, int width, int step)
        {
            return new NumericUpDown { Maximum = max, Minimum = min, Value = value, Increment = step,
                Location = new Point(x, y), Size = new Size(width, 30), Font = Theme.Font(10) };
        }
        private bool Register(int index)
        { return Native.RegisterHotKey(Handle, HotkeyId, HotkeyModifiers[index] | 0x4000, HotkeyKeys[index]); }

        private void SaveOptions()
        {
            if (!ready) return;
            int index = hotkeys.SelectedIndex;
            if (index != settings.HotkeyIndex || !registered)
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
                if (!Register(index))
                {
                    registered = Register(settings.HotkeyIndex);
                    ready = false; hotkeys.SelectedIndex = settings.HotkeyIndex; ready = true;
                    SetStatus("该快捷键被占用，请选择其他组合。", true);
                    return;
                }
                registered = true;
                settings.HotkeyIndex = index;
            }
            settings.IntervalMs = (int)interval.Value;
            settings.StartDelayMs = (int)delay.Value;
            settings.Newlines = (NewlineMode)newlines.SelectedIndex;
            settings.Randomize = randomize.Checked;
            settings.VariationPercent = (int)variation.Value;
            settings.PunctuationPauseMs = (int)punctuation.Value;
            settings.SentencePauseMs = (int)sentence.Value;
            settings.ParagraphPauseMs = (int)paragraph.Value;
            settings.PauseHotkeyIndex = pauseKeys.SelectedIndex;
            shortcut.Text = HotkeyNames[settings.HotkeyIndex]; UpdateSpeed();
            try { settings.Save(); SetStatus("设置已保存 · " + HotkeyNames[settings.HotkeyIndex] + " 呼出选择面板", false); }
            catch (Exception ex) { SetStatus("本次设置已生效，但保存失败：" + ex.Message, true); }
        }
        private void UpdateSpeed()
        {
            speedDescription.Text = settings.IntervalMs == 0 ? "最快速度；若漏字，请增加间隔。"
                : "约 " + (1000.0 / settings.IntervalMs).ToString("0") + " 字 / 秒；间隔越大越慢。";
        }
        private void SetStatus(string message, bool error)
        {
            if (IsDisposed || exiting) return;
            status.Text = message;
            status.ForeColor = error ? Color.FromArgb(175, 65, 51) : Theme.Muted;
        }
        private void Notify(string message, bool error)
        {
            if (IsDisposed || exiting) return;
            SetStatus(message, error);
            if (error)
            {
                tray.BalloonTipTitle = "ClipType";
                tray.BalloonTipText = message;
                tray.BalloonTipIcon = ToolTipIcon.Warning;
                tray.ShowBalloonTip(3500);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                if (m.WParam.ToInt32() == EscapeId) { CancelInput(); return; }
                if (m.WParam.ToInt32() == PauseId) { TogglePause(); return; }
                if (m.WParam.ToInt32() == HotkeyId)
                {
                    if (!busy) BeginPaste();
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private async Task<string> ReadClipboardAsync(CancellationToken token)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { return Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : ""; }
                catch (ExternalException) { if (attempt == 4) throw; }
                await Task.Delay(60, token);
            }
            return "";
        }

        private async void BeginPaste()
        {
            IntPtr target = Native.GetForegroundWindow();
            IntPtr focus = Native.FocusWindow(target);
            uint targetProcess;
            uint targetThread = Native.GetWindowThreadProcessId(target, out targetProcess);
            if (target == IntPtr.Zero || focus == IntPtr.Zero || (target == Handle && !sandbox.Focused))
            {
                Notify("请先点击目标输入框，或下方的本地试输入框，再按快捷键。", true);
                return;
            }
            busy = true;
            try
            {
                cancellation = new CancellationTokenSource();
                CancellationToken token = cancellation.Token;
                string text = await readClipboard(token);
                token.ThrowIfCancellationRequested();
                if (text.Length == 0) { Notify("剪贴板里没有文字。请先复制文字。", true); return; }
                if (text.Length > InputPlan.MaxLength) { Notify("单次最多输入 100,000 个字符，请分段复制。", true); return; }
                // Clipboard retries must not capture a newly selected window by accident.
                if (Native.GetForegroundWindow() != target) return;
                bool simulate;
                using (chooser = new ChoiceForm(text, Native.WindowTitle(target), settings))
                {
                    DialogResult result = chooser.ShowDialog();
                    simulate = chooser.Simulate;
                    if (result != DialogResult.OK)
                    {
                        if (!exiting) Native.SetForegroundWindow(target);
                        SetStatus("已取消 · 未输入文字", false);
                        return;
                    }
                }
                chooser = null;
                token.ThrowIfCancellationRequested();
                if (!simulate && await readClipboard(token) != text)
                    throw new InvalidOperationException("剪贴板内容已改变，请重新按快捷键选择。");
                token.ThrowIfCancellationRequested();
                var steps = simulate ? InputPlan.Build(text, settings.Newlines)
                    : new System.Collections.Generic.List<InputStep> { new InputStep(Native.Chord(0x11, 0x56)) };
                if (steps.Count == 0) throw new InvalidOperationException("这段内容没有可输入的文字。");
                uint currentProcess;
                if (!Native.IsWindow(target) || Native.GetWindowThreadProcessId(target, out currentProcess) != targetThread || currentProcess != targetProcess)
                    throw new InvalidOperationException("无法返回目标窗口。请重新点击输入框后再试。");
                if (!Native.RegisterHotKey(Handle, EscapeId, 0x4000, 0x1B))
                    throw new InvalidOperationException("Esc 停止快捷键注册失败，请关闭占用它的程序后重试。");
                if (simulate && !Native.RegisterHotKey(Handle, PauseId, (settings.PauseHotkeyIndex == 0 ? 3u : 6u) | 0x4000, settings.PauseHotkeyIndex == 0 ? 0x20u : 0x78u))
                    throw new InvalidOperationException("暂停快捷键被占用，请在节奏设置中更换组合。");
                stop.Enabled = true;
                SetOptionsEnabled(false);
                SetStatus("准备输入… 松开快捷键；按 Esc 可停止。", false);
                int total = steps.Count;
                session = new InputSession(total); progressBar.Value = 0;
                // Disabling the main form's selected control can activate another control.
                // Restore the target only after every UI state change has finished.
                if (!Native.SetForegroundWindow(target))
                    throw new InvalidOperationException("无法返回目标窗口，请重新点击输入框后再试。");
                await new InputEngine().RunAsync(target, focus, steps, settings, session, cancellation.Token,
                    delegate(int count)
                    {
                        int percent = count * 100 / total; progressBar.Value = percent;
                        tray.Text = "ClipType · " + percent + "%";
                        SetStatus("输入 " + count + " / " + total + " · " + PauseNames[settings.PauseHotkeyIndex] + " 暂停", false);
                    });
                SetStatus(simulate ? "模拟输入完成 · " + total + " 个输入单元" : "已向目标窗口发送 Ctrl + V", false);
            }
            catch (OperationCanceledException ex) { SetStatus(ex.Message.StartsWith("A task") || cancellation != null && cancellation.IsCancellationRequested ? "输入已停止 · 已输入的文字会保留" : ex.Message, false); }
            catch (Exception ex) { Notify(ex.Message, true); }
            finally
            {
                if (IsHandleCreated) { Native.UnregisterHotKey(Handle, EscapeId); Native.UnregisterHotKey(Handle, PauseId); }
                if (cancellation != null) { cancellation.Dispose(); cancellation = null; }
                if (!IsDisposed) { stop.Enabled = false; SetOptionsEnabled(true); }
                session = null; chooser = null; busy = false;
                if (!IsDisposed) tray.Text = "ClipType · 剪贴板模拟输入";
                if (exiting && !IsDisposed) Close();
            }
        }

        private void SetOptionsEnabled(bool value)
        {
            hotkeys.Enabled = value; interval.Enabled = value; delay.Enabled = value; newlines.Enabled = value;
            randomize.Enabled = value; variation.Enabled = value; punctuation.Enabled = value;
            sentence.Enabled = value; paragraph.Enabled = value; pauseKeys.Enabled = value;
        }
        private void TogglePause()
        {
            if (session == null) return;
            if (session.State == InputState.Paused)
            {
                session.Resume(); SetStatus("恢复前校验目标… Esc 停止", false);
            }
            else if (session.Pause()) SetStatus("已暂停 " + session.Completed + " / " + session.Total + " · " + PauseNames[settings.PauseHotkeyIndex] + " 继续", false);
        }
        private void CancelInput() { if (cancellation != null) cancellation.Cancel(); }
        private void ShowSettings()
        {
            if (busy) CancelInput();
            Show(); WindowState = FormWindowState.Normal; Activate();
        }
        private void ExitApp()
        {
            exiting = true;
            CancelInput();
            if (chooser != null && !chooser.IsDisposed) chooser.DialogResult = DialogResult.Cancel;
            if (!busy) Close();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                CancelInput();
                if (IsHandleCreated) { Native.UnregisterHotKey(Handle, HotkeyId); Native.UnregisterHotKey(Handle, EscapeId); Native.UnregisterHotKey(Handle, PauseId); }
                tray.Visible = false; tray.Dispose();
                if (Icon != null) Icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
