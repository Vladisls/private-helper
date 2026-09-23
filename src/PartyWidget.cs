using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CAHelper
{
    /// Party Check: fix the roster once, validate after every re-form. Reads names from a screen area you pick.
    sealed class PartyWidget : HelperWidget
    {
        public override string HelperName => "Party Check";
        public override bool SupportsHotkeys => false;

        static string RosterPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-party.txt");

        readonly Label status = new Label { Dock = DockStyle.Top, Height = 20, Font = Theme.Small, ForeColor = Theme.Muted };
        readonly Button fix = Theme.MakeButton("Fix party"), validate = Theme.MakeButton("Validate", primary: true);
        readonly FlowLayoutPanel result = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        readonly Label footer = new Label { Dock = DockStyle.Bottom, Height = 20, Font = Theme.Small, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Text = "Area & roster ▾", TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolTip tips = new ToolTip { ShowAlways = true, AutoPopDelay = 20000 };
        List<string> roster = new List<string>();
        Rectangle? area; DateTime? fixedAt; string lastRaw = "";
        bool busy;

        public PartyWidget() : base(300, 250)
        {
            Content.Padding = new Padding(10, 6, 8, 4);
            var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 32, ColumnCount = 2 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            fix.Dock = DockStyle.Fill; validate.Dock = DockStyle.Fill; fix.Margin = new Padding(0, 0, 4, 0); validate.Margin = new Padding(4, 0, 0, 0);
            row.Controls.Add(fix, 0, 0); row.Controls.Add(validate, 1, 0);
            Content.Controls.Add(result); Content.Controls.Add(footer); Content.Controls.Add(row); Content.Controls.Add(status);
            fix.Click += async (s, e) => { Touch(); await Run(fixRoster: true); };
            validate.Click += async (s, e) => { Touch(); await Run(fixRoster: false); };
            footer.Click += (s, e) => { Touch(); AreaMenu().Show(footer, new Point(0, footer.Height)); };
            tips.SetToolTip(fix, "Read the names now and save them as the party that should be there");
            tips.SetToolTip(validate, "Read the names again and compare with the fixed party");
            Load += (s, e) => { LoadFile(); ShowStatus(); Say(roster.Count == 0 ? "Set the area, then press Fix party while the party is right." : "Press Validate after the party re-forms.", Theme.Muted); };
            MakeDraggable(status);
        }

        // ---------- storage: first line "# area x,y,w,h", "# fixed <time>", then one name per line ----------
        void LoadFile()
        {
            roster.Clear(); area = null; fixedAt = null;
            if (!File.Exists(RosterPath)) return;
            foreach (var raw in File.ReadAllLines(RosterPath))
            {
                var l = raw.Trim();
                if (l.StartsWith("# area "))
                {
                    var p = l.Substring(7).Split(',');
                    if (p.Length == 4 && p.All(x => int.TryParse(x, out _))) area = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                }
                else if (l.StartsWith("# fixed ") && DateTime.TryParse(l.Substring(8), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) fixedAt = t;
                else if (l.Length > 0 && !l.StartsWith("#")) roster.Add(l);
            }
        }

        void SaveFile()
        {
            var lines = new List<string> { "# Cabal Helper party roster. One name per line; edit freely." };
            if (area.HasValue) lines.Add($"# area {area.Value.X},{area.Value.Y},{area.Value.Width},{area.Value.Height}");
            if (fixedAt.HasValue) lines.Add("# fixed " + fixedAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            lines.AddRange(roster);
            try { File.WriteAllLines(RosterPath, lines); } catch { }
        }

        void ShowStatus()
        {
            status.Text = !area.HasValue ? "⚠ No area yet: Area & roster > Set area"
                        : roster.Count == 0 ? "No party fixed yet"
                        : $"Party fixed: {roster.Count} names" + (fixedAt.HasValue ? $" at {fixedAt.Value:HH:mm}" : "");
            status.ForeColor = area.HasValue ? Theme.Muted : Theme.Bad;
        }

        ContextMenuStrip AreaMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Set area (drag a box around the member list)…", null, (s, e) => PickArea());
            m.Items.Add("Edit roster…", null, (s, e) => EditRoster());
            m.Items.Add("Show what was read last time", null, (s, e) => MessageBox.Show(lastRaw.Length > 0 ? lastRaw : "Nothing read yet.", "Party Check: last read"));
            m.Items.Add("Open last capture image", null, (s, e) =>
            {
                if (File.Exists(PartyOcr.LastCapturePath)) try { System.Diagnostics.Process.Start(PartyOcr.LastCapturePath); } catch { }
            });
            m.Items.Add("Clear roster", null, (s, e) => { roster.Clear(); fixedAt = null; SaveFile(); ShowStatus(); ClearResult(); });
            return m;
        }

        void PickArea()
        {
            var picked = AreaPicker.Pick();
            if (picked == null) return;
            area = picked; SaveFile(); ShowStatus();
            Say("Area saved. Keep all names visible (make the chat box smaller if it covers the list).", Theme.Muted);
        }

        void EditRoster()
        {
            using (var d = new RosterDialog(roster))
                if (d.ShowDialog() == DialogResult.OK) { roster = d.Names; fixedAt = DateTime.Now; SaveFile(); ShowStatus(); Say($"Roster saved: {roster.Count} names.", Theme.Muted); }
        }

        async System.Threading.Tasks.Task Run(bool fixRoster)
        {
            if (busy) return;
            if (!area.HasValue) { PickArea(); if (!area.HasValue) return; }
            busy = true; fix.Enabled = validate.Enabled = false;
            ClearResult(); Say("Reading…", Theme.Muted);
            try
            {
                var (read, raw) = await PartyOcr.ReadAsync(area.Value);
                lastRaw = raw;
                ClearResult();
                if (fixRoster) ShowFixed(read); else ShowValidation(read);
            }
            catch (Exception ex) { ClearResult(); Say("Could not read: " + ex.Message, Theme.Bad); }
            finally { busy = false; fix.Enabled = validate.Enabled = true; }
        }

        void ShowFixed(PartyRead read)
        {
            if (read.Names.Count == 0) { Say("No names found in the area. Check it with Area & roster > Open last capture.", Theme.Bad); return; }
            roster = read.Names.ToList(); fixedAt = DateTime.Now; SaveFile(); ShowStatus();
            Say($"Fixed {roster.Count} names.", Theme.Good);
            if (read.Count.HasValue && read.Count.Value != read.Names.Count)
                Say($"⚠ The window says {read.Count} members but {read.Names.Count} were read. Make every name visible (chat box?) and press Fix party again.", Theme.Bad);
            else if (!read.Count.HasValue)
                Say("Tip: include the \"member (19/25)\" line in the area so hidden names get noticed.", Theme.Muted);
            Say(string.Join(", ", roster), Theme.Text);
            Say("Wrong spelling? Area & roster > Edit roster.", Theme.Muted);
        }

        void ShowValidation(PartyRead read)
        {
            if (roster.Count == 0) { Say("Fix the party first.", Theme.Bad); return; }
            var r = PartyCheck.Compare(roster, read);
            if (r.AllGood)
            {
                Say($"✓ All {r.Seen.Count} are the fixed party", Theme.Good);
                if (System.Linq.Enumerable.Any(r.Probable)) Say("Close matches: " + string.Join(", ", r.Probable.Select(p => $"{p.read} = {p.roster}")), Theme.Muted);
                if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play();
                return;
            }
            foreach (var n in r.NewNames) Say("NEW: " + n, Theme.Bad);
            foreach (var n in r.Missing) Say("Missing: " + n, Color.FromArgb(245, 196, 81));
            if (r.Hidden > 0) Say($"⚠ {r.Hidden} name(s) not visible: the window says {r.Count} members. Make every name visible and validate again.", Theme.Bad);
            if (System.Linq.Enumerable.Any(r.Probable)) Say("Close matches (probably fine): " + string.Join(", ", r.Probable.Select(p => $"{p.read} = {p.roster}")), Theme.Muted);
            if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Exclamation.Play();
        }

        void ClearResult() { foreach (Control c in result.Controls.Cast<Control>().ToArray()) { result.Controls.Remove(c); c.Dispose(); } }

        void Say(string text, Color color)
        {
            result.Controls.Add(new Label
            {
                Text = text, ForeColor = color, AutoSize = true, MaximumSize = new Size(result.ClientSize.Width - 20, 0),
                Font = color == Theme.Bad || color == Theme.Good ? Theme.Title : Theme.Normal, Margin = new Padding(0, 2, 0, 2)
            });
        }

        public override AlertState Tick(DateTime now, TimeSpan idle, bool gameFocused, Settings s, bool blinkOn) => default;
    }

    /// Plain editor for the roster: one name per line.
    sealed class RosterDialog : Form
    {
        public List<string> Names { get; private set; }
        readonly TextBox box = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 260, Height = 300, Font = new Font("Segoe UI", 10f) };
        public RosterDialog(List<string> names)
        {
            Text = "Party roster (one name per line)";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = false;
            BackColor = Theme.Panel; ForeColor = Theme.Text; Font = Theme.Normal; Padding = new Padding(12);
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            box.BackColor = Color.FromArgb(15, 19, 23); box.ForeColor = Theme.Text; box.BorderStyle = BorderStyle.FixedSingle;
            box.Text = string.Join("\r\n", names);
            var save = Theme.MakeButton("Save", primary: true); save.Width = 80;
            var cancel = Theme.MakeButton("Cancel"); cancel.Width = 80;
            save.Click += (s, e) =>
            {
                Names = box.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
                DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            CancelButton = cancel;
            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            outer.Controls.Add(box); outer.Controls.Add(buttons);
            Controls.Add(outer);
            Shown += (s, e) => { Activate(); box.Focus(); };
        }
    }
}
