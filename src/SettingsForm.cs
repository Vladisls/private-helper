using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CAHelper
{
    /// Settings window: your CP, CA Runner timings, alerts, hotkeys, to-do resets, updates.
    /// Saves cabal-helper.ini (and your CP to cabal-helper-profile.txt).
    sealed class SettingsForm : Form
    {
        readonly TextBox cp = Box(), reentry = Box(), boss = Box(), final = Box(), idle = Box(),
                         gameMatch = Box(), startKey = Box(), resetKey = Box(), dailyReset = Box(), serverOffset = Box();
        readonly CheckBox sound = Check("Alert sound"), notFocused = Check("Flash when another window is in front of the game"),
                          whileActive = Check("Flash even while I'm playing"), autoUpdate = Check("Update from GitHub when the helper starts");
        readonly ComboBox weeklyDay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        readonly Label error = new Label { AutoSize = true, ForeColor = Theme.Bad, MaximumSize = new Size(420, 0) };
        public event Action Saved;

        public SettingsForm(Settings s, long currentCp)
        {
            Text = "Cabal Helper settings";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = true;
            BackColor = Theme.Panel; ForeColor = Theme.Text; Font = Theme.Normal;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(14);

            foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek))) weeklyDay.Items.Add(d);
            cp.Text = currentCp >= 0 ? Todo.FormatCp(currentCp) : "";
            reentry.Text = Fmt(s.ReentryWindow); boss.Text = Fmt(s.BossSpawnAfter); final.Text = Fmt(s.FinalWarning); idle.Text = Fmt(s.IdleAfter);
            sound.Checked = s.Sound; notFocused.Checked = s.FlashWhenGameNotFocused; whileActive.Checked = s.FlashWhenActive; autoUpdate.Checked = s.AutoUpdate;
            gameMatch.Text = s.GameMatch; startKey.Text = s.RestartHotkey; resetKey.Text = s.StopHotkey;
            dailyReset.Text = s.DailyResetTime.ToString(@"hh\:mm"); weeklyDay.SelectedItem = s.WeeklyResetDay;
            serverOffset.Text = Settings.FormatOffset(s.ServerTimeOffset);

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            void Section(string t) { var l = new Label { Text = t, AutoSize = true, Font = Theme.Title, ForeColor = Theme.Accent, Margin = new Padding(0, 12, 0, 4) }; grid.Controls.Add(l); grid.SetColumnSpan(l, 2); }
            void Row(string label, Control c, string hint = null)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 12, 0) });
                var cell = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                cell.Controls.Add(c);
                if (hint != null) cell.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(6, 6, 0, 0) });
                grid.Controls.Add(cell);
            }
            void Full(Control c) { grid.Controls.Add(c); grid.SetColumnSpan(c, 2); }

            Section("You");
            Row("Your CP", cp, "e.g. 518k or 1.1m · hides tasks you can't do yet");
            Section("CA Runner");
            Row("Re-entry window", reentry, "m:ss, shown by the game when you change channel");
            Row("Boss spawns after", boss, "m:ss after Start");
            Row("Last call at", final, "m:ss left: flash even while fighting");
            Row("Not moving after", idle, "m:ss without mouse/keyboard");
            Full(notFocused); Full(whileActive); Full(sound);
            Row("Game window text", gameMatch, "part of Cabal's exe name or window title");
            Section("Hotkeys");
            Row("Start / restart", startKey, "e.g. Ctrl+F9, Alt+Q, Pause");
            Row("Reset", resetKey);
            Section("Daily To-do");
            Row("Daily reset", dailyReset, "hh:mm, your PC's clock");
            Row("Weekly reset", weeklyDay);
            Section("Alarms");
            Row("Server time offset", serverOffset, "server minus your PC, e.g. -1:00 (server 18:30 = PC 19:30)");
            Section("Updates");
            Full(autoUpdate);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top, Margin = new Padding(0, 14, 0, 0) };
            var save = Theme.MakeButton("Save", primary: true); save.Width = 90;
            var cancel = Theme.MakeButton("Cancel"); cancel.Width = 90;
            save.Click += (o, e) => TrySave(s);
            cancel.Click += (o, e) => Close();
            buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            AcceptButton = save; CancelButton = cancel;

            var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            outer.Controls.Add(grid); outer.Controls.Add(error); outer.Controls.Add(buttons);
            Controls.Add(outer);
            Shown += (o, e) => { Activate(); cp.Focus(); cp.SelectAll(); };
        }

        void TrySave(Settings current)
        {
            var problems = new List<string>();
            long newCp = -1;
            if (cp.Text.Trim().Length > 0 && !Todo.TryParseCp(cp.Text, out newCp)) problems.Add("Your CP: type a number like 518k");
            foreach (var (box, name) in new[] { (startKey, "Start hotkey"), (resetKey, "Reset hotkey") })
                if (!HotkeyWindow.TryParse(box.Text, out _, out _)) problems.Add(name + ": '" + box.Text + "' is not a hotkey");

            // Build the ini from the form and run it through the normal parser, so the rules stay in one place.
            var draft = new Settings
            {
                FlashWhenGameNotFocused = notFocused.Checked, FlashWhenActive = whileActive.Checked, Sound = sound.Checked,
                AutoUpdate = autoUpdate.Checked, GameMatch = gameMatch.Text.Trim(), RestartHotkey = startKey.Text.Trim(),
                StopHotkey = resetKey.Text.Trim(), WeeklyResetDay = (DayOfWeek)weeklyDay.SelectedItem, UpdateUrl = current.UpdateUrl,
            };
            string ini = draft.ToIni()
                .Replace("ReentryWindow=" + Fmt(draft.ReentryWindow), "ReentryWindow=" + reentry.Text.Trim())
                .Replace("BossSpawnAfter=" + Fmt(draft.BossSpawnAfter), "BossSpawnAfter=" + boss.Text.Trim())
                .Replace("FinalWarning=" + Fmt(draft.FinalWarning), "FinalWarning=" + final.Text.Trim())
                .Replace("IdleAfter=" + Fmt(draft.IdleAfter), "IdleAfter=" + idle.Text.Trim())
                .Replace("DailyResetTime=" + draft.DailyResetTime.ToString(@"hh\:mm"), "DailyResetTime=" + dailyReset.Text.Trim())
                .Replace("ServerTimeOffset=" + Settings.FormatOffset(draft.ServerTimeOffset), "ServerTimeOffset=" + serverOffset.Text.Trim());
            var parsed = Settings.Parse(ini, problems);
            if (problems.Count > 0) { error.Text = string.Join("\n", problems); return; }

            try { parsed.Save(); } catch (Exception ex) { error.Text = "Could not save: " + ex.Message; return; }
            if (newCp >= 0) Todo.SaveCp(newCp);
            Saved?.Invoke();
            Close();
        }

        static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        static TextBox Box() => new TextBox { Width = 110, BackColor = Color.FromArgb(15, 19, 23), ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        static CheckBox Check(string t) => new CheckBox { Text = t, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 4, 0, 0) };
    }
}
