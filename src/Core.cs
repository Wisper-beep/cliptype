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
        public bool Randomize;
        public int VariationPercent = 20;
        public int PunctuationPauseMs;
        public int SentencePauseMs;
        public int ParagraphPauseMs;
        public int PauseHotkeyIndex;

        internal void Validate()
        {
            IntervalMs = Math.Max(0, Math.Min(500, IntervalMs));
            StartDelayMs = Math.Max(300, Math.Min(5000, StartDelayMs));
            if (HotkeyIndex < 0 || HotkeyIndex > 2) HotkeyIndex = 0;
            if (!Enum.IsDefined(typeof(NewlineMode), Newlines)) Newlines = NewlineMode.ShiftEnter;
            VariationPercent = Math.Max(0, Math.Min(90, VariationPercent));
            PunctuationPauseMs = Math.Max(0, Math.Min(5000, PunctuationPauseMs));
            SentencePauseMs = Math.Max(0, Math.Min(5000, SentencePauseMs));
            ParagraphPauseMs = Math.Max(0, Math.Min(10000, ParagraphPauseMs));
            if (PauseHotkeyIndex < 0 || PauseHotkeyIndex > 1) PauseHotkeyIndex = 0;
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
        internal readonly string Text;
        internal InputStep(Native.INPUT[] keys, string text = "") { Keys = keys; Text = text; }
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
            foreach (string element in Elements(Normalize(value)))
            {
                if (element == "\n")
                {
                    if (mode == NewlineMode.Space) steps.Add(new InputStep(Native.TextKeys(" "), element));
                    else steps.Add(new InputStep(Native.Chord(mode == NewlineMode.ShiftEnter ? (ushort)0x10 : (ushort)0, 0x0D), element));
                }
                else steps.Add(new InputStep(Native.TextKeys(element), element));
            }
            return steps;
        }

        // Framework's older Unicode tables do not group emoji ZWJ / modifiers / flags.
        // This lightweight grouping covers those sequences without a runtime dependency.
        internal static IEnumerable<string> Elements(string value)
        {
            var part = new StringBuilder();
            int previous = -1, regionalCount = 0;
            for (int i = 0; i < value.Length; )
            {
                int width = char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) ? 2 : 1;
                int point = width == 2 ? char.ConvertToUtf32(value, i) : value[i];
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value, i);
                bool regional = point >= 0x1F1E6 && point <= 0x1F1FF;
                bool extend = category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark ||
                    category == UnicodeCategory.EnclosingMark || point == 0x200D || previous == 0x200D ||
                    point >= 0x1F3FB && point <= 0x1F3FF || point >= 0xE0020 && point <= 0xE007F ||
                    regional && regionalCount % 2 == 1;
                if (part.Length > 0 && (!extend || point == 10 || previous == 10)) { yield return part.ToString(); part.Clear(); }
                part.Append(value, i, width);
                regionalCount = regional ? regionalCount + 1 : 0;
                previous = point; i += width;
            }
            if (part.Length > 0) yield return part.ToString();
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
            await RunAsync(target, focus, steps, new Settings { IntervalMs = interval, StartDelayMs = startDelay }, new InputSession(steps.Count), token, progress);
        }

        internal async Task RunAsync(IntPtr target, IntPtr focus, IList<InputStep> steps, Settings options,
            InputSession session, CancellationToken token, Action<int> progress)
        {
            uint process;
            uint thread = Native.GetWindowThreadProcessId(target, out process);
            Action verify = delegate
            {
                CheckTarget(target, focus, token);
                uint currentProcess;
                if (Native.GetWindowThreadProcessId(target, out currentProcess) != thread || currentProcess != process)
                    throw new OperationCanceledException("目标窗口身份已改变，输入已停止。");
            };
            session.Begin();
            var random = new Random();
            try
            {
                await WaitAsync(options.StartDelayMs, session, verify, token);
                session.Running();
                for (int i = 0; i < steps.Count; i++)
                {
                    await ReadyAsync(session, verify, token);
                    Native.Send(steps[i].Keys);
                    session.Completed = i + 1;
                    progress(i + 1);
                    if (i < steps.Count - 1)
                        await WaitAsync(Math.Max(1, Rhythm.DelayAfter(steps[i].Text, steps[i + 1].Text, options, random.NextDouble())), session, verify, token);
                }
                session.Finish(InputState.Completed);
            }
            catch (OperationCanceledException) { session.Finish(InputState.Cancelled); throw; }
            catch { session.Finish(InputState.Failed); throw; }
        }

        private static async Task ReadyAsync(InputSession session, Action verify, CancellationToken token)
        {
            int waited = 0;
            bool unrecognizedModifier = false;
            while (true)
            {
                verify();
                if (session.State == InputState.Paused)
                {
                    waited = 0;
                    unrecognizedModifier = false;
                    await Task.Delay(20, token);
                    continue;
                }
                if (!Native.ModifiersDown())
                {
                    if (unrecognizedModifier && !session.NeedsKeyRelease)
                        throw new OperationCanceledException("检测到修饰键操作，输入已停止。");
                    session.NeedsKeyRelease = false;
                    return;
                }
                if (!session.NeedsKeyRelease) unrecognizedModifier = true;
                // Physical pause chords arrive one key at a time. Suppress input
                // while their modifiers settle so WM_HOTKEY can pause the session.
                await Task.Delay(20, token);
                verify();
                waited += 20;
                if (session.State != InputState.Paused && Native.ModifiersDown() &&
                    waited >= (session.NeedsKeyRelease ? 4000 : 500))
                    throw new OperationCanceledException("请松开快捷键后重试。");
            }
        }

        private static async Task WaitAsync(int delay, InputSession session, Action verify, CancellationToken token)
        {
            while (delay > 0)
            {
                await ReadyAsync(session, verify, token);
                int slice = Math.Min(20, delay);
                await Task.Delay(slice, token);
                delay -= slice;
            }
            await ReadyAsync(session, verify, token);
        }
    }
}
