using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipType
{
    internal static class Native
    {
        internal const int WM_HOTKEY = 0x0312;
        internal const uint KeyUp = 2, Unicode = 4;

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT { public uint type; public UNION data; }
        [StructLayout(LayoutKind.Explicit)]
        internal struct UNION
        {
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public MOUSEINPUT mouse;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort vk, scan;
            public uint flags, time;
            public UIntPtr extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, flags, time;
            public UIntPtr extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int size, flags;
            public IntPtr active, focus, capture, menuOwner, moveSize, caret;
            public int left, top, right, bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        internal static INPUT Key(ushort vk, ushort scan, uint flags)
        {
            return new INPUT { type = 1, data = new UNION { keyboard = new KEYBDINPUT { vk = vk, scan = scan, flags = flags } } };
        }

        internal static INPUT[] TextKeys(string text)
        {
            var keys = new List<INPUT>();
            foreach (char c in text)
            {
                keys.Add(Key(0, c, Unicode));
                keys.Add(Key(0, c, Unicode | KeyUp));
            }
            return keys.ToArray();
        }

        internal static INPUT[] Chord(ushort modifier, ushort key)
        {
            return modifier == 0
                ? new[] { Key(key, 0, 0), Key(key, 0, KeyUp) }
                : new[] { Key(modifier, 0, 0), Key(key, 0, 0), Key(key, 0, KeyUp), Key(modifier, 0, KeyUp) };
        }

        internal static void Send(INPUT[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent == inputs.Length) return;
            int error = Marshal.GetLastWin32Error();
            // A partial insertion must not leave our synthetic modifier held down.
            foreach (INPUT input in inputs)
                if (input.data.keyboard.vk == 0x10 || input.data.keyboard.vk == 0x11)
                {
                    INPUT[] release = { Key(input.data.keyboard.vk, 0, KeyUp) };
                    SendInput(1, release, Marshal.SizeOf(typeof(INPUT)));
                }
            throw new Win32Exception(error,
                "Windows 未能发送全部按键。目标程序若以管理员身份运行，本工具也需要相同权限；部分程序不接受模拟输入。");
        }

        internal static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        internal static bool ModifiersDown()
        {
            return Down(0x10) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C);
        }

        internal static IntPtr FocusWindow(IntPtr hwnd)
        {
            uint pid;
            uint thread = GetWindowThreadProcessId(hwnd, out pid);
            if (thread == 0) return IntPtr.Zero;
            var info = new GUITHREADINFO { size = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            return GetGUIThreadInfo(thread, ref info) ? info.focus : IntPtr.Zero;
        }

        internal static string WindowTitle(IntPtr hwnd)
        {
            var buffer = new StringBuilder(260);
            GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.Length == 0 ? "当前窗口" : buffer.ToString();
        }
    }
}
