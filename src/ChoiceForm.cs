using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ClipType
{
    internal sealed class ChoiceForm : Form
    {
        internal bool Simulate = true;
        internal ChoiceForm(string text, string target, Settings settings)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "选择粘贴方式 · ClipType";
            ClientSize = new Size(570, 410);
            BackColor = Theme.Background;
            Font = Theme.Font(10);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            var area = Screen.FromHandle(Native.GetForegroundWindow()).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            Icon = Theme.MakeIcon();
            Controls.Add(Theme.Label("选择粘贴方式", 24, 22, 500, 34, 17, true));
            var destination = Theme.Label("目标：" + target, 24, 67, 520, 24, 9);
            destination.AutoEllipsis = true; destination.ForeColor = Theme.Muted;
            Controls.Add(destination);
            string preview = text.Length > 1200 ? text.Substring(0, 1200) + "\r\n…（仅预览前 1200 个字符）" : text;
            Controls.Add(new TextBox { Text = preview, ReadOnly = true, Multiline = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(24, 104), Size = new Size(522, 130),
                BackColor = Color.White, Font = Theme.Font(10), TabStop = false });
            int count = 0; foreach (var part in InputPlan.Elements(InputPlan.Normalize(text))) count++;
            Controls.Add(Theme.Label(count.ToString("N0") + " 个输入单元 · " + settings.IntervalMs + " ms" + (settings.Randomize ? " 随机" : " 固定") + " · 支持标点/空行停顿", 24, 248, 520, 24, 9));
            var simulate = Theme.Button("模拟输入  ↵", 24, 286, 251, 46, true);
            var paste = Theme.Button("普通粘贴", 289, 286, 257, 46, false);
            var cancel = Theme.Button("取消", 444, 351, 102, 34, false);
            simulate.Click += delegate { Simulate = true; DialogResult = DialogResult.OK; };
            paste.Click += delegate { Simulate = false; DialogResult = DialogResult.OK; };
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(simulate); Controls.Add(paste); Controls.Add(cancel);
            Controls.Add(Theme.Label("Esc 取消；模拟输入会使用设置中的换行方式。", 24, 359, 410, 25, 8.5f));
            AcceptButton = simulate; CancelButton = cancel;
            Shown += delegate { if (DialogResult == DialogResult.None && !IsDisposed) { simulate.Focus(); Activate(); } };
            ResumeLayout(true);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && Icon != null) Icon.Dispose();
            base.Dispose(disposing);
        }
    }
}
