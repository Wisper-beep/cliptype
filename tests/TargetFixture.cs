using System;
using System.IO;
using System.Windows.Forms;

internal static class TargetFixture
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        using (var form = new Form { Text="ClipType external E2E fixture", Width=600, Height=250 })
        using (var box = new TextBox { Multiline=true, Dock=DockStyle.Fill })
        {
            Directory.CreateDirectory(".test-artifacts");
            Action record = delegate {
                using(var file=new FileStream(".test-artifacts/native-target.txt",FileMode.Create,FileAccess.Write,FileShare.ReadWrite))
                // TextChanged can observe the high surrogate before the next WM_CHAR.
                // Record that transient state with replacement fallback, never a modal exception.
                using(var writer=new StreamWriter(file,new System.Text.UTF8Encoding(false,false))) writer.Write(box.Text);
            };
            box.TextChanged += delegate { record(); };
            form.Controls.Add(box);
            form.Shown += delegate { box.Focus(); record(); };
            Application.Run(form);
        }
    }
}
