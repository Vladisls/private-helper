using System;
using System.Drawing;
using System.Windows.Forms;

namespace CAHelper
{
    /// Small normal window (it takes focus so you can type) for entering your CP.
    sealed class CpDialog : Form
    {
        public long Cp { get; private set; }
        readonly TextBox box = new TextBox { Width = 150, Font = new Font("Segoe UI", 12f) };
        readonly Label err = new Label { AutoSize = true, ForeColor = Theme.Bad, Font = Theme.Small };

        public CpDialog(long current)
        {
            Text = "Your CP";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = false;
            BackColor = Theme.Panel; ForeColor = Theme.Text; Font = Theme.Normal;
            ClientSize = new Size(300, 130);

            var lbl = new Label { Text = "Combat power (e.g. 518k, 1.1m, 518000):", AutoSize = true, Location = new Point(12, 12) };
            box.Location = new Point(12, 36); box.BackColor = Color.FromArgb(15, 19, 23); box.ForeColor = Theme.Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Text = current >= 0 ? Todo.FormatCp(current) : "";
            err.Location = new Point(12, 70);
            var ok = Theme.MakeButton("Save", primary: true); ok.Location = new Point(118, 92); ok.Width = 80;
            var cancel = Theme.MakeButton("Cancel"); cancel.Location = new Point(206, 92); cancel.Width = 80;
            ok.Click += (s, e) => Accept();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { lbl, box, err, ok, cancel });
            Shown += (s, e) => { Activate(); box.Focus(); box.SelectAll(); };
        }

        void Accept()
        {
            if (Todo.TryParseCp(box.Text, out long v)) { Cp = v; DialogResult = DialogResult.OK; Close(); }
            else err.Text = "Type a number like 518k or 518000";
        }
    }
}
