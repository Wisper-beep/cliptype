using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipType
{
    internal static class Theme
    {
        internal static readonly Color Ink = Color.FromArgb(30, 40, 58);
        internal static readonly Color Muted = Color.FromArgb(101, 114, 134);
        internal static readonly Color Accent = Color.FromArgb(47, 93, 220);
        internal static readonly Color Background = Color.FromArgb(245, 247, 251);
        internal static Font Font(float size, bool bold = false)
        { return new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }
        internal static Label Label(string text, int x, int y, int w, int h, float size, bool bold = false)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h),
                Font = Font(size, bold), ForeColor = Ink, BackColor = Color.Transparent,
                UseMnemonic = false };
        }
        internal static Button Button(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h),
                Font = Font(10, true), BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Ink, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(218, 224, 235);
            return b;
        }
        internal static Panel Card(int x, int y, int w, int h)
        {
            return new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.White };
        }
        internal static Icon MakeIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Accent);
                using (var pen = new Pen(Color.White, 2))
                {
                    g.DrawRectangle(pen, 9, 9, 15, 19);
                    g.DrawLine(pen, 12, 15, 21, 15);
                    g.DrawLine(pen, 12, 20, 21, 20);
                    g.DrawLine(pen, 12, 25, 18, 25);
                }
                g.FillRectangle(Brushes.White, 13, 5, 7, 6);
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}
