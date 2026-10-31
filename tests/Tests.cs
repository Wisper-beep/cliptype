using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
                    using (var cancel = new CancellationTokenSource())
                    {
                        bool cancelled = false;
                        try { await engine.RunAsync(form.Handle, focus, InputPlan.Build(new string('x', 80), NewlineMode.Enter), 10, 300, cancel.Token, delegate(int count) { if (count >= 10) cancel.Cancel(); }); }
                        catch (OperationCanceledException) { cancelled = true; }
                        await Task.Delay(100);
                        Check(cancelled && box.Text.Length > 0 && box.Text.Length < 80, "Cancellation stops a real partial input");
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
}
