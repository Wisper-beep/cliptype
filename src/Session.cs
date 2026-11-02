using System;

namespace ClipType
{
    internal enum InputState { Idle, Countdown, Running, Paused, Completed, Cancelled, Failed }

    internal sealed class InputSession
    {
        internal InputState State { get; private set; }
        internal int Completed { get; set; }
        internal int Total { get; private set; }
        internal bool NeedsKeyRelease = true;
        private InputState resumeState;
        internal InputSession(int total) { Total = total; State = InputState.Idle; }
        internal void Begin() { if (State != InputState.Idle) throw new InvalidOperationException("任务已启动。"); State = InputState.Countdown; }
        internal void Running() { if (State == InputState.Countdown) State = InputState.Running; }
        internal bool Pause()
        {
            if (State != InputState.Running && State != InputState.Countdown) return false;
            resumeState = State; State = InputState.Paused; return true;
        }
        internal bool Resume()
        {
            if (State != InputState.Paused) return false;
            NeedsKeyRelease = true; State = resumeState; return true;
        }
        internal void Finish(InputState state)
        {
            if (state != InputState.Completed && state != InputState.Cancelled && state != InputState.Failed)
                throw new ArgumentException("Invalid terminal state.");
            State = state;
        }
    }

    internal static class Rhythm
    {
        internal static int DelayAfter(string text, string next, Settings settings, double sample)
        {
            double factor = settings.Randomize ? 1 + (Math.Max(0, Math.Min(1, sample)) * 2 - 1) * settings.VariationPercent / 100.0 : 1;
            int delay = (int)Math.Round(settings.IntervalMs * factor);
            if (text == "\n") delay += next == "\n" ? settings.ParagraphPauseMs : 0;
            else if (text.Length > 0 && ".!?。！？".IndexOf(text[0]) >= 0) delay += settings.SentencePauseMs;
            else if (text.Length > 0 && ",;:，；：、".IndexOf(text[0]) >= 0) delay += settings.PunctuationPauseMs;
            return Math.Max(0, delay);
        }
    }
}
