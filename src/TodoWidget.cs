using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CAHelper
{
    /// Daily / weekly checklist with +/- counters. Finished rows collapse into a "Done" section.
    sealed class TodoWidget : HelperWidget
    {
        public override string HelperName => "Daily To-do";
        public override bool SupportsHotkeys => false;

        const int RowH = 26, Width0 = 310;
        List<TodoItem> items = new List<TodoItem>();
        readonly Panel rows = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        readonly Label footer = new Label { Dock = DockStyle.Bottom, Height = 20, Font = Theme.Small, ForeColor = Theme.Muted, Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolTip tips = new ToolTip { ShowAlways = true, AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 100 };
        string presetName = "Custom";
        bool doneExpanded;
        string dailyId, weeklyId;
        DateTime listStamp = DateTime.MinValue, lastFileCheck = DateTime.MinValue;

        public TodoWidget() : base(Width0, 200)
        {
            Content.Padding = new Padding(8, 4, 4, 4);
            Content.Controls.Add(rows);
            Content.Controls.Add(footer);
            footer.Text = "List ▾";
            footer.Click += (s, e) => { Touch(); ListMenu().Show(footer, new Point(0, footer.Height)); };
            Load += (s, e) => { Reload(); Rebuild(); };
        }

        // ---------- data ----------
        void Reload()
        {
            var problems = new List<string>();
            if (!File.Exists(Todo.ListPath)) { try { File.WriteAllText(Todo.ListPath, Todo.DefaultList); } catch { } }
            string text = File.Exists(Todo.ListPath) ? SafeRead(Todo.ListPath) : Todo.DefaultList;
            listStamp = File.Exists(Todo.ListPath) ? File.GetLastWriteTimeUtc(Todo.ListPath) : DateTime.MinValue;
            items = Todo.ParseList(text, problems);
            presetName = TodoPresets.NameOf(text);
            var s = Program.CurrentSettings;
            dailyId = Todo.DailyId(DateTime.Now, s.DailyResetTime);
            weeklyId = Todo.WeeklyId(DateTime.Now, s.WeeklyResetDay, s.DailyResetTime);
            Todo.ApplyProgress(items, File.Exists(Todo.ProgressPath) ? SafeRead(Todo.ProgressPath) : "", dailyId, weeklyId);
            footer.Text = $"List: {presetName} ▾   ·   hover a task for why"
                        + (problems.Count > 0 ? "   ·   ⚠ " + problems[0] : "");
            footer.ForeColor = problems.Count > 0 ? Theme.Bad : Theme.Muted;
        }

        static string SafeRead(string path) { try { return File.ReadAllText(path); } catch { return ""; } }

        void SaveProgress()
        {
            try { File.WriteAllText(Todo.ProgressPath, Todo.SerializeProgress(items, dailyId, weeklyId)); } catch { }
        }

        void OpenListInNotepad()
        {
            if (!File.Exists(Todo.ListPath)) { try { File.WriteAllText(Todo.ListPath, Todo.DefaultList); } catch { } }
            try { System.Diagnostics.Process.Start("notepad.exe", "\"" + Todo.ListPath + "\""); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Cabal Helper"); }
        }

        ContextMenuStrip ListMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add(new ToolStripLabel("Load a list for your CP bracket") { ForeColor = Color.Gray });
            foreach (var pr in TodoPresets.All)
            {
                var preset = pr;
                m.Items.Add(new ToolStripMenuItem(preset.Name, null, (s, e) => LoadPreset(preset.Name, preset.Text)) { Checked = preset.Name == presetName });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Edit list in Notepad", null, (s, e) => OpenListInNotepad());
            m.Items.Add("Clear today's progress", null, (s, e) => { foreach (var it in items) if (!it.Weekly) it.Count = 0; SaveProgress(); BeginInvoke((Action)Rebuild); });
            var s0 = Program.CurrentSettings;
            m.Items.Add(new ToolStripLabel($"Daily reset {s0.DailyResetTime:hh\\:mm} · weekly {s0.WeeklyResetDay} (settings file)") { ForeColor = Color.Gray });
            return m;
        }

        /// Replaces the list with a preset. The old list is copied to cabal-helper-todo.backup.txt first;
        /// progress carries over for tasks with the same name.
        void LoadPreset(string name, string text)
        {
            SaveProgress();
            try
            {
                if (File.Exists(Todo.ListPath)) File.Copy(Todo.ListPath, Todo.BackupPath, overwrite: true);
                File.WriteAllText(Todo.ListPath, text);
            }
            catch (Exception ex) { MessageBox.Show("Could not write the list: " + ex.Message, "Cabal Helper"); return; }
            Reload();
            BeginInvoke((Action)Rebuild);
        }

        static string Wrap(string text, int width = 60)
        {
            var sb = new System.Text.StringBuilder(); int col = 0;
            foreach (var word in text.Split(' '))
            {
                if (col > 0 && col + word.Length > width) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(word); col += word.Length;
            }
            return sb.ToString();
        }

        void Change(TodoItem it, int delta)
        {
            Touch();
            int before = it.Count;
            it.Count = Math.Max(0, Math.Min(it.Target, it.Count + delta));
            if (it.Count == before) return;
            SaveProgress();
            BeginInvoke((Action)Rebuild);      // not inside the click: Rebuild disposes the clicked button
            if (it.Done && Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play();
        }

        // ---------- UI ----------
        void Rebuild()
        {
            rows.SuspendLayout();
            rows.AutoScrollPosition = Point.Empty;     // lay rows out from the top
            tips.RemoveAll();
            foreach (Control c in rows.Controls.Cast<Control>().ToArray()) { rows.Controls.Remove(c); c.Dispose(); }

            var open = items.Where(i => !i.Done).ToList();
            var done = items.Where(i => i.Done).ToList();
            int y = 0;
            void Add(Control c) { c.Top = y; c.Left = 0; c.Width = rows.ClientSize.Width - 2; c.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; rows.Controls.Add(c); y += c.Height; }

            bool shownWeeklyHeader = false;
            foreach (var it in open.OrderBy(i => i.Weekly))
            {
                if (it.Weekly && !shownWeeklyHeader) { Add(Header("Weekly")); shownWeeklyHeader = true; }
                Add(Row(it, dim: false));
            }
            if (open.Count == 0) Add(Header(items.Count == 0 ? "No tasks. Click \"Edit list\"." : "All done for today ✓"));

            if (done.Count > 0)
            {
                var h = Header((doneExpanded ? "▾" : "▸") + $" Done ({done.Count})");
                h.Cursor = Cursors.Hand;
                h.Click += (s, e) => { Touch(); doneExpanded = !doneExpanded; BeginInvoke((Action)Rebuild); };
                Add(h);
                if (doneExpanded) foreach (var it in done) Add(Row(it, dim: true));
            }
            rows.ResumeLayout();

            // Grow/shrink to fit, up to most of the screen height.
            int wanted = 26 /*title*/ + 8 + y + footer.Height + 10;
            int max = Screen.FromControl(this).WorkingArea.Height - 40;
            Height = Math.Max(110, Math.Min(max, wanted));
        }

        Label Header(string text) => new Label
        {
            Text = text, Height = 22, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.BottomLeft
        };

        Control Row(TodoItem it, bool dim)
        {
            var row = new Panel { Height = RowH };
            var name = new Label
            {
                Text = (dim ? "✓ " : "") + it.Name, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = dim ? Theme.Muted : Theme.Text, Dock = DockStyle.Fill
            };
            var count = new Label
            {
                Text = $"{it.Count}/{it.Target}", Width = 42, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right,
                ForeColor = dim ? Theme.Good : (it.Count > 0 ? Theme.Accent : Theme.Muted), Font = Theme.Title
            };
            var minus = SmallButton("−"); var plus = SmallButton("+");
            minus.Click += (s, e) => Change(it, -1);
            plus.Click += (s, e) => Change(it, +1);
            plus.Enabled = !it.Done;
            // Right-click the name: set straight to done / back to zero.
            name.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) Change(it, it.Done ? -it.Target : it.Target); };
            row.Controls.Add(name); row.Controls.Add(count); row.Controls.Add(minus); row.Controls.Add(plus);
            plus.Dock = DockStyle.Right; minus.Dock = DockStyle.Right;
            plus.BringToFront(); minus.BringToFront(); count.BringToFront(); name.BringToFront();   // dock order: name fills what's left
            if (!string.IsNullOrEmpty(it.Tip))
            {
                string tip = it.Name + "\n" + Wrap(it.Tip) + "\n\n+ / - to count · right-click the name: done / undo";
                tips.SetToolTip(name, tip); tips.SetToolTip(count, tip);
                name.Text += "  ⓘ";
            }
            MakeDraggable(name);
            return row;
        }

        static readonly Font ButtonFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        static Button SmallButton(string t)
        {
            var b = Theme.MakeButton(t);
            b.Width = 28; b.Height = RowH - 4; b.Margin = new Padding(2);
            b.Font = ButtonFont;
            b.Padding = new Padding(0); b.TextAlign = ContentAlignment.MiddleCenter;
            return b;
        }

        public override AlertState Tick(DateTime now, TimeSpan idle, bool gameFocused, Settings s, bool blinkOn)
        {
            // Period rollover (daily / weekly reset) and list-file edits, checked about once a second.
            if ((now - lastFileCheck).TotalSeconds >= 1)
            {
                lastFileCheck = now;
                string d = Todo.DailyId(now, s.DailyResetTime), w = Todo.WeeklyId(now, s.WeeklyResetDay, s.DailyResetTime);
                DateTime stamp = File.Exists(Todo.ListPath) ? File.GetLastWriteTimeUtc(Todo.ListPath) : DateTime.MinValue;
                if (d != dailyId || w != weeklyId || stamp != listStamp)
                {
                    SaveProgress();            // keep counts of tasks that still exist in the current period
                    Reload(); Rebuild();
                }
            }
            return default;
        }
    }
}
