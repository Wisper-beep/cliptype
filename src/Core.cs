using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace ClipType
{
    public enum NewlineMode { ShiftEnter, Enter, Space }

    public sealed class Settings
    {
        public int IntervalMs = 30;
        public int StartDelayMs = 800;
        public int HotkeyIndex;
        public NewlineMode Newlines = NewlineMode.ShiftEnter;

        internal void Validate()
        {
            IntervalMs = Math.Max(0, Math.Min(500, IntervalMs));
            StartDelayMs = Math.Max(300, Math.Min(5000, StartDelayMs));
            if (HotkeyIndex < 0 || HotkeyIndex > 2) HotkeyIndex = 0;
            if (!Enum.IsDefined(typeof(NewlineMode), Newlines)) Newlines = NewlineMode.ShiftEnter;
        }

        internal static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipType", "settings.xml"); }
        }

        internal static Settings Load()
        {
            try
            {
                using (var file = File.OpenRead(FilePath))
                {
                    var settings = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(file);
                    settings.Validate();
                    return settings;
                }
            }
            catch (IOException) { return new Settings(); }
            catch (UnauthorizedAccessException) { return new Settings(); }
            catch (InvalidOperationException) { return new Settings(); }
        }

        internal void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            using (var file = File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(file, this);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
    }

    internal sealed class InputStep
    {
        internal readonly Native.INPUT[] Keys;
        internal InputStep(Native.INPUT[] keys) { Keys = keys; }
    }

    internal static class InputPlan
    {
        internal const int MaxLength = 100000;
        internal static string Normalize(string value)
        {
            var text = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\r')
                {
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    text.Append('\n');
                }
                else if (c == '\n' || c == '\u2028' || c == '\u2029') text.Append('\n');
                else if (c == '\t') text.Append("    ");
                else if (!char.IsControl(c)) text.Append(c);
            }
            return text.ToString();
        }

        internal static List<InputStep> Build(string value, NewlineMode mode)
        {
            var steps = new List<InputStep>();
            var elements = StringInfo.GetTextElementEnumerator(Normalize(value));
            while (elements.MoveNext())
            {
                string element = (string)elements.Current;
                if (element == "\n")
                {
                    if (mode == NewlineMode.Space) steps.Add(new InputStep(Native.TextKeys(" ")));
                    else steps.Add(new InputStep(Native.Chord(mode == NewlineMode.ShiftEnter ? (ushort)0x10 : (ushort)0, 0x0D)));
                }
                else steps.Add(new InputStep(Native.TextKeys(element)));
            }
            return steps;
        }
    }

    internal sealed class InputEngine
    {
        internal static void CheckTarget(IntPtr target, IntPtr focus, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Native.Down(0x1B)) throw new OperationCanceledException("已按 Esc 停止输入。");
            if (!Native.IsWindow(target) || Native.GetForegroundWindow() != target)
                throw new OperationCanceledException("目标窗口已切换，输入已停止。");
            if (focus != IntPtr.Zero && Native.FocusWindow(target) != focus)
                throw new OperationCanceledException("输入焦点已改变，输入已停止。");
            if (Native.Down(0x01) || Native.Down(0x02))
                throw new OperationCanceledException("检测到鼠标操作，输入已停止。");
        }

        internal async Task RunAsync(IntPtr target, IntPtr focus, IList<InputStep> steps, int interval,
            int startDelay, CancellationToken token, Action<int> progress)
        {
            // Short waits keep Escape and focus checks responsive, including during countdown.
            await WaitAsync(startDelay, target, focus, token);
            int waited = 0;
            while (Native.ModifiersDown())
            {
                await WaitAsync(20, target, focus, token);
                waited += 20;
                if (waited >= 4000) throw new OperationCanceledException("修饰键仍被按住，已取消。请松开快捷键后重试。");
            }
            for (int i = 0; i < steps.Count; i++)
            {
                CheckTarget(target, focus, token);
                if (Native.ModifiersDown()) throw new OperationCanceledException("检测到新的修饰键操作，输入已停止。");
                Native.Send(steps[i].Keys);
                if ((i + 1) % 10 == 0 || i == steps.Count - 1) progress(i + 1);
                // Even at 0 ms, yield between characters so cancellation remains possible.
                await WaitAsync(Math.Max(1, interval), target, focus, token);
            }
        }

        private static async Task WaitAsync(int delay, IntPtr target, IntPtr focus, CancellationToken token)
        {
            while (delay > 0)
            {
                CheckTarget(target, focus, token);
                int slice = Math.Min(20, delay);
                await Task.Delay(slice, token);
                delay -= slice;
            }
        }
    }
}
