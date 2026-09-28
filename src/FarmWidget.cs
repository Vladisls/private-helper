using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CAHelper
{
    /// Farm Tracker: counts runs, run times and DP from the dungeon end window, core gains from the inventory,
    /// and rare drops from the loot feed. Looks at three small screen areas once a second while a session runs.
    sealed class FarmWidget : HelperWidget
    {
        public override string HelperName => "Farm Tracker";
        public override bool SupportsHotkeys => false;

        static string Dir => AppDomain.CurrentDomain.BaseDirectory;
        static string ConfigPath => Path.Combine(Dir, "cabal-helper-farm.txt");
        static string LogPath => Path.Combine(Dir, "cabal-helper-farm-log.csv");

        // areas (screen pixels). Defaults measured on 2560x1440 screenshots (2026-09-28).
        Rectangle invArea = new Rectangle(1905, 240, 620, 615), endArea = new Rectangle(209, 284, 630, 745), lootArea = new Rectangle(2105, 1195, 395, 160);
        List<(string name, double[] f)> icons = FarmCheck.Icons.ToList();
        readonly List<(char d, string cell)> extraDigits = new List<(char, string)>();
        List<string> rareWords = new List<string> { "Jewel", "Slot Extender", "Potion of Luck", "Stone" };

        // session
        bool running, finishing; DateTime started, finishStarted; DateTime lastCheck = DateTime.MinValue;
        const int FreshCountsSeconds = 10, FinishTimeoutSeconds = 90;
        readonly List<(DateTime at, FarmCheck.RunResult run)> runs = new List<(DateTime, FarmCheck.RunResult)>();
        Dictionary<string, int> baseline, current;
        readonly Dictionary<string, int> rare = new Dictionary<string, int>();
        List<string> lastLoot; long lootPrint;
        bool endLatched; DateTime endGoneSince = DateTime.MinValue, invOpenSince = DateTime.MinValue, lastInvRead = DateTime.MinValue;
        Grid? grid; Img lastInvImg; List<SlotRead> lastSlots = new List<SlotRead>();
        bool busyEnd, busyLoot;
        string warning; DateTime warningAt;
        void Warn(string text) { warning = text; warningAt = DateTime.Now; }

        readonly Label status = new Label { Dock = DockStyle.Top, Height = 34, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false };
        readonly Button startStop = Theme.MakeButton("Start session", primary: true);
        readonly FlowLayoutPanel body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
        readonly Label footer = new Label { Dock = DockStyle.Bottom, Height = 20, Font = Theme.Small, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Text = "Areas & learning ▾", TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };

        public FarmWidget() : base(320, 200)
        {
            Content.Padding = new Padding(10, 6, 8, 4);
            startStop.Dock = DockStyle.Top; startStop.Height = 30;
            Content.Controls.Add(body); Content.Controls.Add(footer); Content.Controls.Add(startStop); Content.Controls.Add(status);
            startStop.Click += (s, e) => { Touch(); if (!running) StartSession(); else if (finishing) StopSession(); else RequestStop(); };
            footer.Click += (s, e) => { Touch(); Menu2().Show(footer, new Point(0, footer.Height)); };
            Load += (s, e) => { LoadConfig(); Render(); };
            MakeDraggable(status);
        }

        // ---------- config ----------
        void LoadConfig()
        {
            if (!File.Exists(ConfigPath)) return;
            var learnedIcons = new List<(string, double[])>();
            foreach (var raw in File.ReadAllLines(ConfigPath))
            {
                var l = raw.Trim(); int eq = l.IndexOf('='); if (eq < 0 || l.StartsWith("#")) continue;
                string k = l.Substring(0, eq), v = l.Substring(eq + 1);
                Rectangle? R() { var p = v.Split(','); return p.Length == 4 && p.All(x => int.TryParse(x, out _)) ? new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3])) : (Rectangle?)null; }
                if (k == "inventory" && R() is Rectangle a) invArea = a;
                else if (k == "end" && R() is Rectangle b) endArea = b;
                else if (k == "loot" && R() is Rectangle c) lootArea = c;
                else if (k == "rare") rareWords = v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                else if (k.StartsWith("icon:")) learnedIcons.Add((k.Substring(5), v.Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray()));
                else if (k.StartsWith("digit:") && k.Length == 7) extraDigits.Add((k[6], v));
            }
            if (learnedIcons.Count > 0) icons = learnedIcons;
        }

        void SaveConfig()
        {
            string Rs(Rectangle r) => $"{r.X},{r.Y},{r.Width},{r.Height}";
            var lines = new List<string> { "# Cabal Helper Farm Tracker settings (written by the helper)", "inventory=" + Rs(invArea), "end=" + Rs(endArea), "loot=" + Rs(lootArea), "rare=" + string.Join(", ", rareWords) };
            if (!ReferenceEquals(icons, null) && icons.Count > 0 && !icons.SequenceEqual(FarmCheck.Icons))
                foreach (var (n, f) in icons) lines.Add("icon:" + n + "=" + string.Join(" ", f.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture))));
            foreach (var (d, cell) in extraDigits) lines.Add("digit:" + d + "=" + cell);
            try { File.WriteAllLines(ConfigPath, lines); } catch { }
        }

        List<(char d, string cell)> Digits => FarmCheck.Digits.Concat(extraDigits).ToList();

        ContextMenuStrip Menu2()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Set inventory area (the core tab's slot grid)…", null, (s, e) => Pick(ref invArea, "Drag a box around the inventory slot grid (all 8 x 8 slots). Esc cancels.", resetGrid: true));
            m.Items.Add("Set end-window area (dungeon cleared window)…", null, (s, e) => Pick(ref endArea, "Drag a box around the dungeon end window (\"Quest Dungeon Cleared!\"). Esc cancels."));
            m.Items.Add("Set loot-feed area (\"Obtain ... x 1\" lines)…", null, (s, e) => Pick(ref lootArea, "Drag a box around the loot messages (\"Obtain ... x 1\"). Esc cancels."));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Learn core icons from the open inventory", null, (s, e) => LearnIcons());
            m.Items.Add("Fix counts (and teach digits)…", null, (s, e) => FixCounts());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Open farm log (CSV)", null, (s, e) => { if (File.Exists(LogPath)) try { System.Diagnostics.Process.Start(LogPath); } catch { } });
            return m;
        }

        void Pick(ref Rectangle area, string msg, bool resetGrid = false)
        {
            var r = AreaPicker.Pick(msg);
            if (r == null) return;
            area = r.Value; if (resetGrid) grid = null;
            SaveConfig(); Render();
        }

        // ---------- session ----------
        void StartSession()
        {
            running = true; started = DateTime.Now; runs.Clear(); rare.Clear(); baseline = null; current = null; lastLoot = null; endLatched = false;
            startStop.Text = "Stop session";
            if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play();
            Render();
        }

        /// Before stopping, make sure the final core counts are recent: if the core tab wasn't read in the last
        /// few seconds, ask for it and stop as soon as it has been read (or on "Stop now" / after 90 s).
        void RequestStop()
        {
            if (baseline == null || (DateTime.Now - lastInvRead).TotalSeconds <= FreshCountsSeconds) { StopSession(); return; }
            finishing = true; finishStarted = DateTime.Now;
            startStop.Text = "Stop now (skip final counts)";
            if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play();
            Render();
        }

        void StopSession()
        {
            running = false; finishing = false; startStop.Text = "Start session";
            WriteLog(); Render();
        }

        void WriteLog()
        {
            if (runs.Count == 0 && (baseline == null || current == null)) return;
            try
            {
                bool header = !File.Exists(LogPath);
                using (var w = new StreamWriter(LogPath, append: true))
                {
                    if (header) w.WriteLine("session_start,kind,dungeon_or_item,seconds_or_count,dp");
                    string s0 = started.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    foreach (var (at, r) in runs) w.WriteLine($"{s0},run,\"{r.Dungeon}\",{r.Seconds},{r.Dp}");
                    foreach (var kv in Gains()) w.WriteLine($"{s0},core,\"{kv.Key}\",{kv.Value},");
                    foreach (var kv in rare) w.WriteLine($"{s0},rare,\"{kv.Key}\",{kv.Value},");
                    w.WriteLine($"{s0},session,\"minutes\",{(int)(DateTime.Now - started).TotalMinutes},{runs.Sum(x => x.run.Dp)}");
                }
            }
            catch { }
        }

        Dictionary<string, int> Gains()
        {
            var g = new Dictionary<string, int>();
            if (baseline == null || current == null) return g;
            foreach (var kv in current) if (baseline.TryGetValue(kv.Key, out int b) && kv.Value != b) g[kv.Key] = kv.Value - b;
            return g;
        }

        // ---------- once a second ----------
        public override AlertState Tick(DateTime now, TimeSpan idle, bool gameFocused, Settings s, bool blinkOn)
        {
            if (!running || (now - lastCheck).TotalMilliseconds < 500) return default;
            lastCheck = now;
            try
            {
                CheckEnd(now); CheckInventory(now);
                if (!running) return default;                                     // stopped by the final inventory read
                CheckLoot();
                if (finishing && (now - finishStarted).TotalSeconds >= FinishTimeoutSeconds) { StopSession(); return default; }
            }
            catch (Exception ex) { Warn("⚠ " + ex.Message); }
            Render();
            return default;
        }

        void CheckEnd(DateTime now)
        {
            if (busyEnd) return;
            using (var bmp = PartyOcr.Capture(endArea))
            {
                bool likely = FarmCheck.EndWindowLikely(PartyOcr.ToImg(bmp));
                if (!likely) { if (endLatched && endGoneSince == DateTime.MinValue) endGoneSince = now; if (endLatched && (now - endGoneSince).TotalSeconds >= 2) endLatched = false; return; }
                endGoneSince = DateTime.MinValue;
                if (endLatched) return;                                            // this window was already counted
                busyEnd = true;
                var copy = (Bitmap)bmp.Clone();
                BeginInvoke((Action)(async () =>
                {
                    try
                    {
                        var lines = await PartyOcr.ReadLinesAsync(copy);
                        var r = FarmCheck.ParseEndWindow(lines);
                        if (r != null) { runs.Add((DateTime.Now, r)); endLatched = true; if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play(); }
                    }
                    catch (Exception ex) { Warn("⚠ " + ex.Message); }
                    finally { copy.Dispose(); busyEnd = false; Render(); }
                }));
            }
        }

        void CheckInventory(DateTime now)
        {
            using (var bmp = PartyOcr.Capture(invArea))
            {
                var img = PartyOcr.ToImg(bmp);
                if (grid == null) { var g0 = FarmCheck.FindGrid(img, 0, 0, img.W, img.H); if (!FarmCheck.InventoryOpen(img, g0)) { invOpenSince = DateTime.MinValue; return; } grid = g0; }
                if (!FarmCheck.InventoryOpen(img, grid.Value)) { invOpenSince = DateTime.MinValue; return; }
                if (invOpenSince == DateTime.MinValue) invOpenSince = now;
                if ((now - invOpenSince).TotalSeconds < 0.5 || (now - lastInvRead).TotalSeconds < 1) return;   // open for 0.5 s, read once a second
                lastInvRead = now;
                var (g, slots) = FarmCheck.BestRead(img, grid.Value, icons, Digits);
                if (slots.Count == 0) return;                                      // another tab: no cores here
                grid = g; lastInvImg = img; lastSlots = slots;
                var counts = new Dictionary<string, int>();
                foreach (var sl in slots) if (sl.Count.HasValue) counts[sl.Item] = counts.TryGetValue(sl.Item, out int c) ? c + sl.Count.Value : sl.Count.Value;
                if (slots.Any(x => !x.Count.HasValue)) Warn("Some counts unreadable: Areas & learning > Fix counts");
                if (baseline == null) { baseline = counts; if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play(); }
                else current = counts;
                if (finishing) { StopSession(); return; }
            }
        }

        void CheckLoot()
        {
            if (busyLoot) return;
            using (var bmp = PartyOcr.Capture(lootArea))
            {
                var img = PartyOcr.ToImg(bmp);
                long print = 0; for (int i = 0; i < img.Px.Length; i += 64) print = print * 31 + img.Px[i];   // cheap "did it change?" check
                if (print == lootPrint) return;
                lootPrint = print; busyLoot = true;
                var copy = (Bitmap)bmp.Clone();
                BeginInvoke((Action)(async () =>
                {
                    try
                    {
                        var lines = (await PartyOcr.ReadLinesAsync(copy)).Where(l => l.IndexOf("Obtain", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                        var fresh = FarmCheck.NewLines(lastLoot, lines);
                        if (lastLoot != null)
                            foreach (var l in fresh)
                            {
                                var p = FarmCheck.ParseLoot(l);
                                if (p != null && rareWords.Any(w => p.Value.item.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                                    rare[p.Value.item] = (rare.TryGetValue(p.Value.item, out int n) ? n : 0) + p.Value.qty;
                            }
                        lastLoot = lines;
                    }
                    catch { }
                    finally { copy.Dispose(); busyLoot = false; }
                }));
            }
        }

        // ---------- learning ----------
        void LearnIcons()
        {
            using (var bmp = PartyOcr.Capture(invArea))
            {
                var img = PartyOcr.ToImg(bmp);
                var g = FarmCheck.FindGrid(img, 0, 0, img.W, img.H);
                if (!FarmCheck.InventoryOpen(img, g)) { MessageBox.Show("Open the inventory on the core tab first (and check Set inventory area).", "Farm Tracker"); return; }
                g = FarmCheck.BestRead(img, g, icons, Digits).grid;
                // same layout as taught: top row Upgrade Cores highest -> lowest, bottom row Force Cores
                var learned = new List<(string, double[])>();
                for (int i = 0; i < FarmCheck.Icons.Length; i++) learned.Add((FarmCheck.Icons[i].name, FarmCheck.IconFeature(img, g, i / 5, i % 5)));
                icons = learned; grid = g; SaveConfig();
                MessageBox.Show("Learned 10 core icons from the first two rows (top: Upgrade Core Ultimate to Low, bottom: Force Core Ultimate to Low).", "Farm Tracker");
            }
        }

        void FixCounts()
        {
            if (lastInvImg == null || lastSlots.Count == 0) { MessageBox.Show("Start a session and open the core tab for a second first.", "Farm Tracker"); return; }
            using (var d = new FixCountsDialog(lastSlots))
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                var counts = current ?? baseline ?? new Dictionary<string, int>();
                foreach (var (slot, value) in d.Values)
                {
                    extraDigits.AddRange(FarmCheck.LearnDigits(lastInvImg, grid.Value, slot.Row, slot.Col, value, Digits));
                    slot.Count = value; counts[slot.Item] = value;
                }
                if (current != null) current = counts; else baseline = counts;
                SaveConfig(); Render();
            }
        }

        // ---------- display ----------
        void Render()
        {
            var el = running ? DateTime.Now - started : TimeSpan.Zero;
            int dp = runs.Sum(x => x.run.Dp);
            double hours = Math.Max(el.TotalHours, 1e-6);
            string st = running
                ? $"Session {Fmt(el)} · {runs.Count} runs · {dp} DP ({(el.TotalMinutes >= 1 ? (dp / hours).ToString("0") : "–")}/h)"
                : runs.Count > 0 ? $"Last session: {runs.Count} runs, {dp} DP. Start a new one when you begin farming." : "Start a session when you begin farming. Open the core tab for a second at the start and the end.";
            if (warning != null && (DateTime.Now - warningAt).TotalSeconds < 10) st = warning;
            if (status.Text != st) status.Text = st;
            var lines = new List<Ui.Line>();
            void Line(string t, Color col, bool bold = false) => lines.Add(new Ui.Line(t, col, bold));
            if (running && !finishing && baseline == null)
                Line("Open the core tab for a moment to save the start counts. Runs and drops are already being counted.", Color.FromArgb(245, 196, 81), true);
            if (finishing)
                Line($"Open the core tab for a moment to save the final counts. Stops by itself once read ({Math.Max(0, FinishTimeoutSeconds - (int)(DateTime.Now - finishStarted).TotalSeconds)} s).", Color.FromArgb(245, 196, 81), true);

            if (runs.Count > 0)
            {
                Line("Runs", Theme.Accent, true);
                foreach (var grp in runs.GroupBy(x => x.run.Dungeon))
                {
                    var secs = grp.Where(x => x.run.Seconds > 0).Select(x => x.run.Seconds).ToList();
                    Line($"{grp.Key}: {grp.Count()} runs · avg {(secs.Count > 0 ? Fmt(TimeSpan.FromSeconds(secs.Average())) : "–")} · {grp.Sum(x => x.run.Dp)} DP", Theme.Text);
                }
            }
            var gains = Gains();
            if (baseline != null)
            {
                Line("Cores", Theme.Accent, true);
                if (current == null) Line("Start counts saved. Open the core tab again at the end.", Theme.Muted);
                else if (gains.Count == 0) Line("No change yet.", Theme.Muted);
                foreach (var kv in gains.OrderByDescending(k => k.Value))
                    Line($"{kv.Key}: {(kv.Value > 0 ? "+" : "")}{kv.Value}" + (el.TotalMinutes >= 5 ? $" ({kv.Value / hours:0}/h)" : ""), kv.Value > 0 ? Theme.Good : Theme.Bad);
            }
            if (rare.Count > 0)
            {
                Line("Rare drops", Theme.Accent, true);
                foreach (var kv in rare) Line($"{kv.Key} x{kv.Value}", Theme.Good);
            }
            Ui.SetLines(body, lines, Width - 30);
            FitHeight();
        }

        /// Grow or shrink to the content instead of a fixed size.
        void FitHeight()
        {
            int want = 26 /*title*/ + Content.Padding.Vertical + status.Height + startStop.Height + 6 + Ui.LinesHeight(body) + footer.Height + 8;
            int max = Screen.FromControl(this).WorkingArea.Height - 40;
            int h = Math.Max(150, Math.Min(max, want));
            if (Height != h) Height = h;
        }

        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// Lets the player type the real stack counts; wrong or unreadable digits are learned from them.
    sealed class FixCountsDialog : Form
    {
        public List<(SlotRead slot, int value)> Values { get; } = new List<(SlotRead, int)>();
        readonly List<(SlotRead slot, TextBox box)> rows = new List<(SlotRead, TextBox)>();
        public FixCountsDialog(List<SlotRead> slots)
        {
            Text = "Fix counts"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = false;
            BackColor = Theme.Panel; ForeColor = Theme.Text; Font = Theme.Normal; Padding = new Padding(12);
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
            grid.Controls.Add(new Label { Text = "Type the real count where it's wrong or \"?\":", AutoSize = true, ForeColor = Theme.Muted }); grid.SetColumnSpan(grid.Controls[0], 2);
            foreach (var s in slots)
            {
                grid.Controls.Add(new Label { Text = s.Item, AutoSize = true, Margin = new Padding(0, 6, 12, 0) });
                var tb = new TextBox { Width = 70, Text = s.Count?.ToString() ?? "", BackColor = Color.FromArgb(15, 19, 23), ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
                grid.Controls.Add(tb); rows.Add((s, tb));
            }
            var save = Theme.MakeButton("Save", primary: true); save.Width = 80;
            var cancel = Theme.MakeButton("Cancel"); cancel.Width = 80;
            save.Click += (o, e) =>
            {
                foreach (var (s, tb) in rows)
                    if (int.TryParse(tb.Text.Trim(), out int v) && v >= 0 && v != (s.Count ?? -1)) Values.Add((s, v));
                DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += (o, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = save; CancelButton = cancel;
            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) }; buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            outer.Controls.Add(grid); outer.Controls.Add(buttons); Controls.Add(outer);
            Shown += (o, e) => Activate();
        }
    }
}
