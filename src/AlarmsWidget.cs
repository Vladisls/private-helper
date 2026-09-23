using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CAHelper
{
    /// Shared alarm list: loaded from cabal-helper-alarms.txt, watched for hand edits, used by the engine and the panel.
    static class AlarmStore
    {
        public static List<Alarm> All = new List<Alarm>();
        public static event Action Changed;
        public static string Problem;
        static DateTime stamp = DateTime.MinValue, lastCheck = DateTime.MinValue;

        public static void Load()
        {
            try { if (!File.Exists(Alarms.ListPath)) File.WriteAllText(Alarms.ListPath, Alarms.DefaultList); } catch { }
            var problems = new List<string>();
            string text = "";
            try { text = File.Exists(Alarms.ListPath) ? File.ReadAllText(Alarms.ListPath) : Alarms.DefaultList; } catch { }
            All = Alarms.Parse(text, problems);
            Problem = problems.FirstOrDefault();
            stamp = File.Exists(Alarms.ListPath) ? File.GetLastWriteTimeUtc(Alarms.ListPath) : DateTime.MinValue;
            Changed?.Invoke();
        }

        public static void Save()
        {
            try { File.WriteAllText(Alarms.ListPath, Alarms.Serialize(All)); stamp = File.GetLastWriteTimeUtc(Alarms.ListPath); } catch { }
            Changed?.Invoke();
        }

        /// Reload if the file was edited by hand (checked about once a second).
        public static void CheckFile(DateTime now)
        {
            if ((now - lastCheck).TotalSeconds < 1) return;
            lastCheck = now;
            var s = File.Exists(Alarms.ListPath) ? File.GetLastWriteTimeUtc(Alarms.ListPath) : DateTime.MinValue;
            if (s != stamp) Load();
        }
    }

    /// Panel listing your alarms with a countdown to each. "+ Add" and click-to-edit open a small dialog.
    sealed class AlarmsWidget : HelperWidget
    {
        public override string HelperName => "Alarms";
        public override bool SupportsHotkeys => false;

        readonly Panel rows = new Panel { Dock = DockStyle.Fill };
        readonly Label add = new Label { Dock = DockStyle.Bottom, Height = 22, Text = "+ Add alarm", Cursor = Cursors.Hand, ForeColor = Theme.Accent, Font = Theme.Title, TextAlign = ContentAlignment.MiddleLeft };
        readonly List<(Alarm a, Label count, Panel row)> live = new List<(Alarm, Label, Panel)>();
        readonly ToolTip tips = new ToolTip { ShowAlways = true, InitialDelay = 350 };

        public AlarmsWidget() : base(290, 140)
        {
            Content.Padding = new Padding(8, 4, 6, 4);
            Content.Controls.Add(rows);
            Content.Controls.Add(add);
            add.Click += (s, e) => { Touch(); Edit(null); };
            AlarmStore.Changed += OnStoreChanged;
            Load += (s, e) => Rebuild();
            FormClosed += (s, e) => AlarmStore.Changed -= OnStoreChanged;
        }

        void OnStoreChanged() { if (IsHandleCreated) BeginInvoke((Action)Rebuild); }

        void Edit(Alarm existing)
        {
            using (var d = new AlarmDialog(existing, Program.CurrentSettings.ServerTimeOffset))
            {
                var r = d.ShowDialog();
                if (r == DialogResult.OK)
                {
                    if (existing == null) AlarmStore.All.Add(d.Result);
                    else { int i = AlarmStore.All.IndexOf(existing); if (i >= 0) AlarmStore.All[i] = d.Result; }
                    AlarmStore.Save();
                }
                else if (r == DialogResult.Abort && existing != null) { AlarmStore.All.Remove(existing); AlarmStore.Save(); }
            }
        }

        void Rebuild()
        {
            rows.SuspendLayout();
            foreach (Control c in rows.Controls.Cast<Control>().ToArray()) { rows.Controls.Remove(c); c.Dispose(); }
            live.Clear(); tips.RemoveAll();
            var now = DateTime.Now;
            var ordered = AlarmStore.All.OrderBy(a => a.Enabled ? 0 : 1).ThenBy(a => a.Next(now) ?? DateTime.MaxValue).ToList();
            int y = 0;
            var off = Program.CurrentSettings.ServerTimeOffset;
            foreach (var a in ordered)
            {
                var row = new Panel { Height = 38, Top = y, Left = 0, Width = rows.ClientSize.Width - 2, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Cursor = Cursors.Hand };
                var title = new Label { Text = a.Title, AutoEllipsis = true, Font = Theme.Title, Location = new Point(0, 2), Size = new Size(row.Width - 90, 18), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
                var when = new Label
                {
                    Text = $"{a.Time:hh\\:mm} · server {Alarms.ToServer(a.Time, off):hh\\:mm} · {a.DaysText()}" + (a.Enabled ? "" : " · off"),
                    Font = Theme.Small, ForeColor = Theme.Muted, Location = new Point(0, 20), Size = new Size(row.Width - 90, 16), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
                };
                var count = new Label { Font = Theme.Title, TextAlign = ContentAlignment.MiddleRight, Size = new Size(84, 36), Location = new Point(row.Width - 86, 1), Anchor = AnchorStyles.Right | AnchorStyles.Top };
                if (!a.Enabled) { title.ForeColor = Theme.Muted; count.ForeColor = Theme.Muted; }
                row.Controls.Add(title); row.Controls.Add(when); row.Controls.Add(count);
                var alarm = a;
                foreach (Control c in new Control[] { row, title, when, count })
                {
                    c.Click += (s, e) => { Touch(); BeginInvoke((Action)(() => Edit(alarm))); };
                    tips.SetToolTip(c, "Click to edit, turn off or delete");
                }
                rows.Controls.Add(row);
                live.Add((a, count, row));
                y += row.Height;
            }
            if (ordered.Count == 0) rows.Controls.Add(new Label { Text = "No alarms yet.", ForeColor = Theme.Muted, Top = 4, AutoSize = true });
            if (AlarmStore.Problem != null)
            { add.Text = "+ Add alarm   ⚠ " + AlarmStore.Problem; add.ForeColor = Theme.Bad; }
            else { add.Text = "+ Add alarm"; add.ForeColor = Theme.Accent; }
            rows.ResumeLayout();
            Height = Math.Max(110, Math.Min(Screen.FromControl(this).WorkingArea.Height - 40, 26 + 8 + Math.Max(y, 24) + add.Height + 10));
            UpdateCountdowns(now, false);
        }

        void UpdateCountdowns(DateTime now, bool blinkOn)
        {
            foreach (var (a, count, row) in live)
            {
                var next = a.Next(now);
                if (!a.Enabled || next == null) { count.Text = "off"; continue; }
                var left = next.Value - now;
                count.Text = left <= TimeSpan.Zero ? "NOW" : Alarms.Countdown(left);
                bool warn = AlarmEngine.InWarning(a, now);
                var back = warn ? (blinkOn ? Theme.AlertBright : Theme.AlertDark) : Theme.Panel;
                if (row.BackColor != back) row.BackColor = back;
                count.ForeColor = warn ? Color.White : a.Enabled ? Theme.Accent : Theme.Muted;
            }
        }

        public override AlertState Tick(DateTime now, TimeSpan idle, bool gameFocused, Settings s, bool blinkOn)
        {
            UpdateCountdowns(now, blinkOn);
            return default;
        }
    }

    /// Add / edit one alarm. Takes focus so you can type. Delete returns DialogResult.Abort.
    sealed class AlarmDialog : Form
    {
        public Alarm Result { get; private set; }
        readonly TextBox title = Box(200), time = Box(70), warn = Box(50);
        readonly RadioButton pcTime = new RadioButton { Text = "my PC time", AutoSize = true, Checked = true, ForeColor = Theme.Text },
                             serverTime = new RadioButton { Text = "server time", AutoSize = true, ForeColor = Theme.Text };
        readonly CheckBox everyDay = new CheckBox { Text = "Every day", AutoSize = true, ForeColor = Theme.Text };
        readonly CheckBox[] day = new CheckBox[7];
        readonly CheckBox enabled = new CheckBox { Text = "Alarm on", AutoSize = true, Checked = true, ForeColor = Theme.Text };
        readonly Label hint = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small };
        readonly Label error = new Label { AutoSize = true, ForeColor = Theme.Bad };
        readonly TimeSpan offset;

        public AlarmDialog(Alarm a, TimeSpan serverOffset)
        {
            offset = serverOffset;
            Text = a == null ? "New alarm" : "Edit alarm";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = false;
            BackColor = Theme.Panel; ForeColor = Theme.Text; Font = Theme.Normal;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(14);

            var names = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            var idx = new[] { 1, 2, 3, 4, 5, 6, 0 };
            var dayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            for (int i = 0; i < 7; i++)
            {
                var cb = new CheckBox { Text = names[i], AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 6, 0) };
                day[idx[i]] = cb; dayRow.Controls.Add(cb);
                cb.CheckedChanged += (s, e) => { everyDay.CheckedChanged -= EveryDayChanged; everyDay.Checked = day.All(d => d.Checked); everyDay.CheckedChanged += EveryDayChanged; };
            }
            everyDay.CheckedChanged += EveryDayChanged;

            var src = a ?? new Alarm { Title = "", Time = new TimeSpan(DateTime.Now.Hour, 0, 0).Add(TimeSpan.FromHours(1)) };
            if (src.Time >= TimeSpan.FromDays(1)) src.Time -= TimeSpan.FromDays(1);
            title.Text = src.Title; time.Text = src.Time.ToString(@"hh\:mm"); warn.Text = src.WarnMinutes.ToString();
            for (int i = 0; i < 7; i++) day[i].Checked = src.Days[i];
            enabled.Checked = src.Enabled;
            pcTime.CheckedChanged += (s, e) => ConvertShownTime();
            time.TextChanged += (s, e) => UpdateHint();

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
            void Row(string label, Control c) { grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 12, 0) }); grid.Controls.Add(c); }
            FlowLayoutPanel Flow(params Control[] cs) { var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) }; f.Controls.AddRange(cs); return f; }
            Row("Title", title);
            Row("Time", Flow(time, pcTime, serverTime));
            Row("", hint);
            Row("Days", Flow(everyDay));
            Row("", dayRow);
            Row("Warn", Flow(warn, new Label { Text = "minutes before (0 = no warning)", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(6, 6, 0, 0) }));
            Row("", enabled);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            var save = Theme.MakeButton("Save", primary: true); save.Width = 80;
            var cancel = Theme.MakeButton("Cancel"); cancel.Width = 80;
            buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            if (a != null)
            {
                var del = Theme.MakeButton("Delete"); del.Width = 80; del.ForeColor = Theme.Bad;
                del.Click += (s, e) => { DialogResult = DialogResult.Abort; Close(); };
                buttons.Controls.Add(del);
            }
            save.Click += (s, e) => Accept();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = save; CancelButton = cancel;

            var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            outer.Controls.Add(grid); outer.Controls.Add(error); outer.Controls.Add(buttons);
            Controls.Add(outer);
            UpdateHint();
            Shown += (s, e) => { Activate(); title.Focus(); };
        }

        void EveryDayChanged(object s, EventArgs e) { if (everyDay.Checked) foreach (var d in day) d.Checked = true; }

        void ConvertShownTime()
        {
            if (!Alarms.TryParseTime(time.Text, out var t)) return;
            time.Text = (pcTime.Checked ? Alarms.FromServer(t, offset) : Alarms.ToServer(t, offset)).ToString(@"hh\:mm");
        }

        void UpdateHint()
        {
            if (!Alarms.TryParseTime(time.Text, out var t)) { hint.Text = "type a time like 19:30"; return; }
            var local = pcTime.Checked ? t : Alarms.FromServer(t, offset);
            hint.Text = $"= {local:hh\\:mm} on your PC · {Alarms.ToServer(local, offset):hh\\:mm} server (offset {Settings.FormatOffset(offset)}, see Settings)";
        }

        void Accept()
        {
            if (title.Text.Trim().Length == 0) { error.Text = "Give it a title, e.g. GDG"; return; }
            if (!Alarms.TryParseTime(time.Text, out var t)) { error.Text = "Time should look like 19:30"; return; }
            if (!int.TryParse(warn.Text.Trim(), out int w) || w < 0 || w > 240) { error.Text = "Warn minutes: 0 to 240"; return; }
            var days = day.Select(d => d.Checked).ToArray();
            if (!days.Any(d => d)) { error.Text = "Pick at least one day"; return; }
            Result = new Alarm
            {
                Title = title.Text.Trim(), Time = pcTime.Checked ? t : Alarms.FromServer(t, offset),
                Days = days, WarnMinutes = w, Enabled = enabled.Checked
            };
            DialogResult = DialogResult.OK; Close();
        }

        static TextBox Box(int w) => new TextBox { Width = w, BackColor = Color.FromArgb(15, 19, 23), ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
    }
}
