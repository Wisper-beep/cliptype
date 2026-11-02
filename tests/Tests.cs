using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using System.Reflection;
using ClipType;

internal static class Tests
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++; Console.WriteLine("PASS: " + name);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Check(Marshal.SizeOf(typeof(Native.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "Win32 INPUT ABI layout");
            Check(InputPlan.Normalize("A\r\nB\rC\nD\u2028E\u2029F\tG\0\b") == "A\nB\nC\nD\nE\nF    G", "Normalize newlines, tabs and control characters");
            Check(InputPlan.Normalize("中文🙂 e\u0301") == "中文🙂 e\u0301", "Preserve Unicode and combining characters");
            Check(InputPlan.Build("👩‍💻🇨🇳👍🏽", NewlineMode.Enter).Count == 3, "Group ZWJ emoji, flags and skin tone modifiers");
            var rhythmSettings = new Settings { IntervalMs=100, Randomize=true, VariationPercent=20, PunctuationPauseMs=200, SentencePauseMs=400, ParagraphPauseMs=800 };
            Check(Rhythm.DelayAfter("字","字",rhythmSettings,0)==80 && Rhythm.DelayAfter("字","字",rhythmSettings,1)==120, "Random rhythm stays in configured bounds");
            Check(Rhythm.DelayAfter("，","字",rhythmSettings,.5)==300 && Rhythm.DelayAfter("。","字",rhythmSettings,.5)==500 && Rhythm.DelayAfter("\n","\n",rhythmSettings,.5)==900, "Semantic pauses support Chinese punctuation and blank lines");
            var state = new InputSession(5); Check(!state.Resume(), "Idle task cannot resume"); state.Begin(); state.Pause();
            Check(state.State==InputState.Paused && state.Resume() && state.State==InputState.Countdown, "Pause and resume countdown");
            state.Running(); state.Pause(); state.Resume(); state.Finish(InputState.Completed);
            Check(state.State==InputState.Completed && !state.Resume() && !state.Pause(), "Terminal task cannot restart");
            var unicode = InputPlan.Build("中🙂e\u0301", NewlineMode.ShiftEnter);
            Check(unicode.Count == 3, "Do not split surrogate pairs or combining text elements");
            Check(unicode[1].Keys.Length == 4 && unicode[1].Keys[0].data.keyboard.scan == '\uD83D' && unicode[1].Keys[2].data.keyboard.scan == '\uDE42', "Emoji sends complete UTF-16 pair");
            Check(unicode[0].Keys[0].data.keyboard.vk == 0 && unicode[0].Keys[0].data.keyboard.flags == 4 && unicode[0].Keys[1].data.keyboard.flags == 6, "Unicode key down/up events");
            var shift = InputPlan.Build("\r\n", NewlineMode.ShiftEnter)[0].Keys;
            Check(shift.Length == 4 && shift[0].data.keyboard.vk == 0x10 && shift[1].data.keyboard.vk == 0x0D && shift[3].data.keyboard.flags == Native.KeyUp, "Shift+Enter has balanced modifier events");
            var enter = InputPlan.Build("\n", NewlineMode.Enter)[0].Keys;
            Check(enter.Length == 2 && enter[0].data.keyboard.vk == 0x0D, "Enter newline");
            Check(InputPlan.Build("\n", NewlineMode.Space)[0].Keys[0].data.keyboard.scan == 0x20, "Single-line newline replacement");
            Check(InputPlan.Build("\0\b", NewlineMode.Enter).Count == 0, "Control characters cannot become shortcuts");
            var paste = Native.Chord(0x11, 0x56);
            Check(paste.Length == 4 && paste[3].data.keyboard.vk == 0x11 && paste[3].data.keyboard.flags == 2, "Ctrl+V releases Ctrl");
            var settings = new Settings { IntervalMs = -1, StartDelayMs = 99999, HotkeyIndex = 90, Newlines = (NewlineMode)99 };
            settings.Validate();
            Check(settings.IntervalMs == 0 && settings.StartDelayMs == 5000 && settings.HotkeyIndex == 0 && settings.Newlines == NewlineMode.ShiftEnter, "Invalid settings are clamped");
            using (var source = new CancellationTokenSource())
            {
                source.Cancel(); bool cancelled = false;
                try { InputEngine.CheckTarget(IntPtr.Zero, IntPtr.Zero, source.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Explicit cancellation prevents injection");
            }
            bool invalidTarget = false;
            try { InputEngine.CheckTarget(IntPtr.Zero, IntPtr.Zero, CancellationToken.None); }
            catch (OperationCanceledException) { invalidTarget = true; }
            Check(invalidTarget, "Missing target prevents injection");
            if (args.Length > 0 && args[0] == "--integration") Integration();
            if (args.Length > 0 && args[0] == "--ui-smoke") UiSmoke();
            if (args.Length > 0 && args[0] == "--desktop") Desktop();
            Console.WriteLine("All " + passed + " checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void UiSmoke()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var main = new MainForm())
        {
            main.Shown += async delegate
            {
                await Task.Delay(500);
                Check(main.Visible && main.Controls.Count > 0, "Full main window starts");
                using (var chooser = new ChoiceForm("本地测试 Chinese 🙂", "Test target", new Settings()))
                {
                    chooser.Show();
                    await Task.Delay(300);
                    Check(chooser.Visible, "Choice window starts");
                    chooser.Close();
                }
                Application.ExitThread();
            };
            Application.Run(main);
        }
    }

    private static void Integration()
    {
        Application.EnableVisualStyles();
        Exception failure = null;
        var savedClipboard = Clipboard.GetDataObject();
        try
        {
        using (var form = new Form { Text = "ClipType integration test", Width = 500, Height = 220 })
        using (var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, AcceptsReturn = true })
        {
            form.Controls.Add(box);
            form.Shown += async delegate
            {
                try
                {
                    form.Activate(); box.Focus();
                    await Task.Delay(250);
                    Check(Native.GetForegroundWindow() == form.Handle, "Integration target is foreground");
                    IntPtr focus = Native.FocusWindow(form.Handle);
                    var engine = new InputEngine();
                    await engine.RunAsync(form.Handle, focus, InputPlan.Build("中文 English 🙂\r\n第二行", NewlineMode.Enter), 10, 300, CancellationToken.None, delegate { });
                    await Task.Delay(100);
                    Check(box.Text.Replace("\r\n", "\n") == "中文 English 🙂\n第二行", "Real SendInput delivers Chinese, English, emoji and newline");
                    box.Text = "abcdef"; box.Select(2, 2);
                    await engine.RunAsync(form.Handle, focus, InputPlan.Build("中文", NewlineMode.Enter), 10, 300, CancellationToken.None, delegate { });
                    await Task.Delay(100);
                    Check(box.Text == "ab中文ef", "Typing replaces selected text at original caret");
                    box.Clear();
                    var pausedSession = new InputSession(12);
                    using (var timer = new System.Windows.Forms.Timer { Interval=180 })
                    {
                        int pausedLength = -1;
                        bool held = false;
                        timer.Tick += delegate {
                            if (pausedLength < 0) { pausedLength = box.Text.Length; return; }
                            timer.Stop(); held = box.Text.Length == pausedLength && pausedSession.State == InputState.Paused; pausedSession.Resume();
                        };
                        await engine.RunAsync(form.Handle, focus, InputPlan.Build("暂停🙂恢复继续abcde",NewlineMode.Enter), new Settings {IntervalMs=10,StartDelayMs=300}, pausedSession, CancellationToken.None,
                            delegate(int count) { if(count==3) { pausedSession.Pause(); timer.Start(); } });
                        await Task.Delay(100);
                        Check(box.Text=="暂停🙂恢复继续abcde" && pausedSession.State==InputState.Completed, "Resume preserves original target and exact Unicode text");
                        Check(held, "Pause sends no extra characters after queued events drain");
                    }
                    box.Clear();
                    using (var cancel = new CancellationTokenSource())
                    {
                        bool cancelled = false;
                        try { await engine.RunAsync(form.Handle, focus, InputPlan.Build(new string('x', 80), NewlineMode.Enter), 10, 300, cancel.Token, delegate(int count) { if (count >= 10) cancel.Cancel(); }); }
                        catch (OperationCanceledException) { cancelled = true; }
                        await Task.Delay(100);
                        Check(cancelled && box.Text.Length > 0 && box.Text.Length < 80, "Cancellation stops a real partial input");
                    }
                    box.Clear();
                    using (var releaseModifier = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        bool stopped = false;
                        releaseModifier.Tick += delegate { releaseModifier.Stop(); Native.Send(new[] { Native.Key(0x11, 0, Native.KeyUp) }); };
                        try
                        {
                            await engine.RunAsync(form.Handle, focus, InputPlan.Build("abcdefghij", NewlineMode.Enter), 10, 300, CancellationToken.None,
                                delegate(int count) { if (count == 3) { Native.Send(new[] { Native.Key(0x11, 0, 0) }); releaseModifier.Start(); } });
                        }
                        catch (OperationCanceledException) { stopped = true; }
                        finally { Native.Send(new[] { Native.Key(0x11, 0, Native.KeyUp) }); }
                        await Task.Delay(100);
                        Check(stopped && box.Text == "abc", "Unrecognized modifier cancels without sending during chord grace");
                    }
                    box.Clear();
                    await engine.RunAsync(form.Handle, focus, InputPlan.Build("一\n二", NewlineMode.ShiftEnter), 10, 300, CancellationToken.None, delegate { });
                    await Task.Delay(100);
                    Check(box.Text.Replace("\r\n", "\n") == "一\n二", "Shift+Enter delivers a newline in a multiline input");
                    Check(!Native.Down(0x10), "Shift is released after simulated newline");
                    Clipboard.SetText("普通粘贴 中文🙂"); box.Clear();
                    await engine.RunAsync(form.Handle, focus, new[] { new InputStep(Native.Chord(0x11, 0x56)) }, 0, 300, CancellationToken.None, delegate { });
                    await Task.Delay(100);
                    Check(box.Text == "普通粘贴 中文🙂", "Ordinary paste sends Ctrl+V");
                    Check(!Native.Down(0x11), "Ctrl is released after ordinary paste");
                    box.Clear();
                    using (var other = new TextBox { Top = 100, Width = 200 })
                    {
                        form.Controls.Add(other); other.BringToFront(); other.Focus();
                        bool stopped = false;
                        try { await engine.RunAsync(form.Handle, focus, InputPlan.Build("SHOULD NOT TYPE", NewlineMode.Enter), 10, 300, CancellationToken.None, delegate { }); }
                        catch (OperationCanceledException) { stopped = true; }
                        Check(stopped && other.Text == "" && box.Text == "", "Focus change prevents typing into another control");
                    }
                    box.Focus();
                    await ExitDuringClipboardAwait(form.Handle, false, delegate { return box.Text; });
                    await ExitDuringClipboardAwait(form.Handle, true, delegate { return box.Text; });
                }
                catch (Exception ex) { failure = ex; }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        if (failure != null) throw failure;
        }
        finally
        {
            if (savedClipboard != null) Clipboard.SetDataObject(savedClipboard, true);
        }
    }

    private static T Field<T>(object instance, string name)
    { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
    private static async Task Until(Func<bool> condition, string message)
    {
        for (int i=0;i<250;i++) { if(condition()) return; await Task.Delay(20); }
        throw new Exception("Timeout: " + message);
    }
    private static async Task SetClipboardAsync(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                await Task.Delay(80);
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText) && Clipboard.GetText(TextDataFormat.UnicodeText) == text) return;
            }
            catch (ExternalException) { }
        }
        throw new Exception("Test clipboard text did not become readable");
    }
    private static void Hotkey(uint modifiers, uint key)
    {
        var events = new System.Collections.Generic.List<Native.INPUT>();
        if ((modifiers & 2)!=0) events.Add(Native.Key(0x11,0,0));
        if ((modifiers & 1)!=0) events.Add(Native.Key(0x12,0,0));
        if ((modifiers & 4)!=0) events.Add(Native.Key(0x10,0,0));
        events.Add(Native.Key((ushort)key,0,0)); events.Add(Native.Key((ushort)key,0,2));
        if ((modifiers & 4)!=0) events.Add(Native.Key(0x10,0,2));
        if ((modifiers & 1)!=0) events.Add(Native.Key(0x12,0,2));
        if ((modifiers & 2)!=0) events.Add(Native.Key(0x11,0,2));
        Native.Send(events.ToArray());
    }
    private static async Task SlowHotkey(uint modifiers, uint key)
    {
        var held = new System.Collections.Generic.List<ushort>();
        try
        {
            foreach (ushort modifier in new ushort[] { 0x11, 0x12, 0x10 })
            {
                uint flag = modifier == 0x11 ? 2u : modifier == 0x12 ? 1u : 4u;
                if ((modifiers & flag) == 0) continue;
                Native.Send(new[] { Native.Key(modifier, 0, 0) }); held.Add(modifier);
                await Task.Delay(80);
            }
            Native.Send(Native.Chord(0, (ushort)key));
            await Task.Delay(40);
        }
        finally
        {
            for (int i = held.Count - 1; i >= 0; i--)
                Native.Send(new[] { Native.Key(held[i], 0, Native.KeyUp) });
        }
    }
    private static async Task ExitDuringClipboardAwait(IntPtr target, bool ordinaryPasteRead, Func<string> targetText)
    {
        const string text = "EXIT MUST NOT TYPE";
        var completion = new TaskCompletionSource<string>();
        int reads = 0; CancellationToken readToken = CancellationToken.None;
        using (var pending = new MainForm(delegate(CancellationToken token)
        {
            reads++; readToken = token;
            return ordinaryPasteRead && reads == 1 ? Task.FromResult(text) : completion.Task;
        }))
        {
            try
            {
                pending.Show(); Native.SetForegroundWindow(target);
                await Until(() => Native.GetForegroundWindow() == target, "exit regression target activation");
                string before = targetText();
                pending.BeginInvoke(new Action(delegate {
                    typeof(MainForm).GetMethod("BeginPaste", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pending, null);
                }));
                if (ordinaryPasteRead)
                {
                    await Until(() => Field<ChoiceForm>(pending, "chooser") != null && Field<ChoiceForm>(pending, "chooser").Visible,
                        "exit regression paste chooser");
                    var choice = Field<ChoiceForm>(pending, "chooser");
                    choice.Controls.OfType<Button>().First(b => b.Text == "普通粘贴").PerformClick();
                }
                await Until(() => reads == (ordinaryPasteRead ? 2 : 1) && Field<bool>(pending, "busy") && Field<ChoiceForm>(pending, "chooser") == null,
                    "clipboard preflight is awaiting retry");
                var tray = Field<NotifyIcon>(pending, "tray");
                tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count - 1].PerformClick();
                Check(readToken.IsCancellationRequested, ordinaryPasteRead ? "Exit cancels ordinary-paste clipboard token" : "Exit cancels initial clipboard token");
                // Even a reader that completes after cancellation must not restart preflight.
                completion.TrySetResult(text);
                await Until(() => pending.IsDisposed || Field<ChoiceForm>(pending, "chooser") != null,
                    "exit during clipboard retry finishes");
                Check(pending.IsDisposed, ordinaryPasteRead ? "Exit cancels ordinary-paste clipboard retry" : "Exit cancels initial clipboard retry before chooser");
                await Task.Delay(80);
                Check(targetText() == before, ordinaryPasteRead ? "Exit during paste preflight sends no keys" : "Exit during initial preflight sends no keys");
            }
            finally
            {
                completion.TrySetCanceled();
                var choice = Field<ChoiceForm>(pending, "chooser");
                if (choice != null && !choice.IsDisposed) choice.DialogResult = DialogResult.Cancel;
            }
        }
    }
    private static void Desktop()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Exception failure=null; var clipboard=Clipboard.GetDataObject();
        using (var main=new MainForm())
        {
            System.Diagnostics.Process fixture = null;
            main.Shown += async delegate
            {
                try
                {
                    // Run after the main window's initial activation has completed.
                    await Task.Delay(500); main.Activate(); await Task.Delay(100);
                    Check(Field<bool>(main,"registered"),"Main global hotkey registered (no conflicting instance)");
                    var options=Field<Settings>(main,"settings"); options.IntervalMs=20;options.StartDelayMs=300;
                    var keys=new uint[]{0x56,0x50,0x77};var modifiers=new uint[]{3,3,6};
                    fixture = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ClipType.Target.exe"))
                        { UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=Environment.CurrentDirectory });
                    await Until(delegate { fixture.Refresh(); return fixture.MainWindowHandle!=IntPtr.Zero; },"external fixture starts");
                    await Until(delegate { fixture.Refresh(); Native.SetForegroundWindow(fixture.MainWindowHandle); return Native.GetForegroundWindow()==fixture.MainWindowHandle && Native.FocusWindow(fixture.MainWindowHandle)!=IntPtr.Zero; },"fixture activation");
                    await Task.Delay(150);
                    Check(Native.GetForegroundWindow()==fixture.MainWindowHandle,"External fixture is the foreground target");
                    string sample="中文🙂"+new string('x',35);
                    await SetClipboardAsync(sample);
                    Hotkey(modifiers[options.HotkeyIndex],keys[options.HotkeyIndex]);
                    await Until(()=>Application.OpenForms.OfType<ChoiceForm>().Any(),"hotkey opens chooser");
                    var chooser=Application.OpenForms.OfType<ChoiceForm>().Single();
                    await Task.Delay(200);
                    Check(chooser.Visible,"Actual global hotkey opens native choice panel");
                    Native.Send(Native.Chord(0,0x0D));
                    await Until(()=>FixtureText().Length>=3,"first characters");
                    await SlowHotkey(options.PauseHotkeyIndex==0?3u:6u,options.PauseHotkeyIndex==0?0x20u:0x78u);
                    await Until(()=>Field<InputSession>(main,"session")!=null && Field<InputSession>(main,"session").State==InputState.Paused,"pause hotkey");
                    await Task.Delay(80);string held=FixtureText();await Task.Delay(150);
                    Check(FixtureText()==held,"Global pause shortcut freezes native input");
                    await SetClipboardAsync("clipboard changed after capture");
                    await SlowHotkey(options.PauseHotkeyIndex==0?3u:6u,options.PauseHotkeyIndex==0?0x20u:0x78u);
                    await Until(()=>!Field<bool>(main,"busy"),"resume completes");
                    Check(FixtureText()==sample,"Global resume keeps snapshot text even after clipboard changes");
                    Check(Field<ProgressBar>(main,"progressBar").Value==100,"Native progress reaches 100 percent");
                    Hotkey(2,0x41);await SetClipboardAsync("普通粘贴🙂");
                    Hotkey(modifiers[options.HotkeyIndex],keys[options.HotkeyIndex]);
                    await Until(()=>Application.OpenForms.OfType<ChoiceForm>().Any(),"paste chooser");
                    chooser=Application.OpenForms.OfType<ChoiceForm>().Single();await Task.Delay(150);
                    chooser.Controls.OfType<Button>().First(b=>b.Text=="普通粘贴").Focus();Native.Send(Native.Chord(0,0x0D));
                    await Until(()=>!Field<bool>(main,"busy"),"native paste finishes");await Task.Delay(100);
                    Check(FixtureText()=="普通粘贴🙂","Native choice panel ordinary paste remains functional");
                    Hotkey(2,0x41);await SetClipboardAsync(new string('q',80));
                    Hotkey(modifiers[options.HotkeyIndex],keys[options.HotkeyIndex]);
                    await Until(()=>Application.OpenForms.OfType<ChoiceForm>().Any(),"cancel chooser");
                    await Task.Delay(150);Native.Send(Native.Chord(0,0x0D));
                    await Until(()=>FixtureText().StartsWith("qqq"),"cancel prefix");
                    Hotkey(0,0x1B);await Until(()=>!Field<bool>(main,"busy"),"Escape stops task");
                    Check(FixtureText().Length<80,"Global Escape shortcut cancels native input");
                    var tray=Field<NotifyIcon>(main,"tray");tray.ContextMenuStrip.Items[0].PerformClick();
                    Check(main.Visible,"Tray settings action reopens main window");
                    using(var bitmap=new System.Drawing.Bitmap(main.Width,main.Height))
                    { main.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,main.Width,main.Height));System.IO.Directory.CreateDirectory(".test-artifacts");bitmap.Save(".test-artifacts/windows-vnext.png"); }
                    main.Close();Check(!main.Visible && tray.Visible,"Closing main window preserves tray residency");
                    tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count-1].PerformClick();
                }
                catch(Exception ex) {
                    uint pid;Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out pid);
                    Console.Error.WriteLine("Desktop status: " + Field<Label>(main,"status").Text + "; foreground process=" + System.Diagnostics.Process.GetProcessById((int)pid).ProcessName);
                    failure=ex;Application.ExitThread();
                }
                finally { if(fixture!=null) { if(!fixture.HasExited) { fixture.CloseMainWindow(); if(!fixture.WaitForExit(2000))fixture.Kill(); } fixture.Dispose(); } }
            };
            Application.Run(main);
        }
        if(clipboard!=null)Clipboard.SetDataObject(clipboard,true);
        if(failure!=null)throw failure;
    }
    private static string FixtureText()
    {
        try
        {
            using(var file=new System.IO.FileStream(".test-artifacts/native-target.txt",System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite))
            using(var reader=new System.IO.StreamReader(file)) return reader.ReadToEnd();
        }
        catch(System.IO.IOException) { return ""; }
    }
}
