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
        static string DebugPath => Path.Combine(Dir, "cabal-helper-farm-debug.txt");
        static string InvShotPath => Path.Combine(Dir, "cabal-helper-farm-inventory.png");
        static string InvRawPath => Path.Combine(Dir, "cabal-helper-farm-inventory-raw.png");
        readonly List<string> debug = new List<string>();
        string waitReason;                                                      // why the last inventory check didn't produce counts
        string lastDebugKey;

        /// Diagnostics: a rolling text log of what the tracker decided (last 300 lines).
        void Debug(string msg, string dedupeKey = null)
        {
            if (dedupeKey != null && dedupeKey == lastDebugKey) return;         // don't repeat the same "skipped" every 500 ms
            lastDebugKey = dedupeKey;
            debug.Add(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + msg);
            if (debug.Count > 300) debug.RemoveRange(0, debug.Count - 300);
            try { File.WriteAllLines(DebugPath, debug); } catch { }
        }

        double? expectedPitchOverride;                                           // slot size of the inventory found on screen
        double ExpectedPitch => expectedPitchOverride ?? FarmCheck.RefPitch * PartyOcr.PhysicalScreenHeight() / 1440.0;

        // areas (screen pixels). Defaults measured on 2560x1440 screenshots (2026-09-28).
        Rectangle invArea = new Rectangle(1905, 240, 620, 615), endArea = new Rectangle(209, 284, 630, 745), lootArea = new Rectangle(2105, 1195, 395, 160);
        List<(string name, double[] f)> icons = FarmCheck.DefaultIcons();
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
        double[] coreTab;                                                       // tab-strip fingerprint of the core tab
        DateTime lastLocate = DateTime.MinValue;
        bool busyEnd, busyLoot;
        string warning; DateTime warningAt;
        readonly HashSet<string> confirmedEmpty = new HashSet<string>();
        (List<string> names, Dictionary<string, int> counts, bool start)? pending;       // waiting for the player's answer
        readonly FlowLayoutPanel confirmBox = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Padding = new Padding(0, 4, 0, 4) };
        readonly Label confirmText = new Label { AutoSize = true, ForeColor = Color.FromArgb(245, 196, 81), Font = Theme.Title, UseMnemonic = false };
        readonly Button confirmYes = Theme.MakeButton("Yes, none", primary: true), confirmAgain = Theme.MakeButton("Read again");
        void Warn(string text) { warning = text; warningAt = DateTime.Now; }

        readonly Label status = new Label { Dock = DockStyle.Top, Height = 34, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false };
        readonly Button startStop = Theme.MakeButton("Start session", primary: true);
        readonly FlowLayoutPanel body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
        readonly Label footer = new Label { Dock = DockStyle.Bottom, Height = 20, Font = Theme.Small, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Text = "Areas & learning ▾", TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };

        public FarmWidget() : base(320, 200)
        {
            Content.Padding = new Padding(10, 6, 8, 4);
            startStop.Dock = DockStyle.Top; startStop.Height = 30;
            confirmYes.Width = 110; confirmAgain.Width = 110;
            var confirmButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            confirmButtons.Controls.Add(confirmYes); confirmButtons.Controls.Add(confirmAgain);
            confirmBox.Controls.Add(confirmText); confirmBox.Controls.Add(confirmButtons);
            confirmYes.Click += (s, e) => { Touch(); ResolvePending(true); };
            confirmAgain.Click += (s, e) => { Touch(); ResolvePending(false); };
            Content.Controls.Add(body); Content.Controls.Add(confirmBox); Content.Controls.Add(footer); Content.Controls.Add(startStop); Content.Controls.Add(status);
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
                else if (k == "coretab") coreTab = v.Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            }
            if (learnedIcons.Count > 0) icons = learnedIcons;
        }

        void SaveConfig()
        {
            string Rs(Rectangle r) => $"{r.X},{r.Y},{r.Width},{r.Height}";
            var lines = new List<string> { "# Cabal Helper Farm Tracker settings (written by the helper)", "inventory=" + Rs(invArea), "end=" + Rs(endArea), "loot=" + Rs(lootArea), "rare=" + string.Join(", ", rareWords) };
            if (!ReferenceEquals(icons, null) && icons.Count > 0 && !icons.SequenceEqual(FarmCheck.DefaultIcons()) && !icons.SequenceEqual(FarmCheck.Icons))
                foreach (var (n, f) in icons) lines.Add("icon:" + n + "=" + string.Join(" ", f.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture))));
            foreach (var (d, cell) in extraDigits) lines.Add("digit:" + d + "=" + cell);
            if (coreTab != null) lines.Add("coretab=" + string.Join(" ", coreTab.Select(x => x.ToString("0.0", CultureInfo.InvariantCulture))));
            try { File.WriteAllLines(ConfigPath, lines); } catch { }
        }

        List<(char d, string cell)> Digits => FarmCheck.Digits.Concat(extraDigits).ToList();

        ContextMenuStrip Menu2()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Set inventory area (the core tab's slot grid)…", null, (s, e) => Pick(ref invArea, "Drag a box around the inventory's slot grid (the whole inventory window is fine too). Esc cancels.", resetGrid: true));
            m.Items.Add("Set end-window area (dungeon cleared window)…", null, (s, e) => Pick(ref endArea, "Drag a box around the dungeon end window (\"Quest Dungeon Cleared!\"). Esc cancels."));
            m.Items.Add("Set loot-feed area (\"Obtain ... x 1\" lines)…", null, (s, e) => Pick(ref lootArea, "Drag a box around the loot messages (\"Obtain ... x 1\"). Esc cancels."));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Learn core icons from the open inventory", null, (s, e) => LearnIcons());
            m.Items.Add("Fix counts (and teach digits)…", null, (s, e) => FixCounts());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Open farm log", null, (s, e) => Files.OpenInTextEditor(LogPath));
            m.Items.Add("Diagnostics: what the tracker decided", null, (s, e) => Files.OpenInTextEditor(DebugPath));
            m.Items.Add("Diagnostics: last inventory read (picture)", null, (s, e) => { if (File.Exists(InvShotPath)) Files.OpenFolder(InvShotPath); else MessageBox.Show("No inventory read yet.", "Farm Tracker"); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Reset areas and everything learned…", null, (s, e) => ResetAll());
            return m;
        }

        void ResetAll()
        {
            if (MessageBox.Show("Reset the Farm Tracker?\n\nAreas go back to the defaults and the learned core icons, digits and core tab are forgotten. The farm log is kept.",
                                "Farm Tracker", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            invArea = new Rectangle(1905, 240, 620, 615); endArea = new Rectangle(209, 284, 630, 745); lootArea = new Rectangle(2105, 1195, 395, 160);
            icons = FarmCheck.DefaultIcons(); extraDigits.Clear(); coreTab = null; grid = null; expectedPitchOverride = null;
            try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); } catch { }
            Debug("RESET: areas back to defaults, learned icons/digits/core tab forgotten");
            Render();
        }

        void Pick(ref Rectangle area, string msg, bool resetGrid = false)
        {
            var r = AreaPicker.Pick(msg);
            if (r == null) return;
            area = r.Value; if (resetGrid) { grid = null; coreTab = null; }
            Debug($"area set: {r.Value.X},{r.Value.Y} {r.Value.Width}x{r.Value.Height}");
            SaveConfig(); Render();
        }

        // ---------- session ----------
        void StartSession()
        {
            Debug($"session started (screen {PartyOcr.PhysicalScreenWidth()}x{PartyOcr.PhysicalScreenHeight()}, all screens {PartyOcr.PhysicalVirtualScreen()}, inventory area {invArea.X},{invArea.Y} {invArea.Width}x{invArea.Height}, expected slot {ExpectedPitch:0.0} px)");
            running = true; started = DateTime.Now; lastLocate = DateTime.MinValue; runs.Clear(); rare.Clear(); baseline = null; current = null; lastLoot = null; endLatched = false;
            confirmedEmpty.Clear(); pending = null;
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
            Debug($"session stopped: {runs.Count} runs, gains " + string.Join(", ", Gains().Select(k => Short(k.Key) + (k.Value > 0 ? "+" : "") + k.Value)));
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

        Dictionary<string, int> Gains() => FarmCheck.Gains(baseline, current);

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
                        if (r != null) { runs.Add((DateTime.Now, r)); endLatched = true; Debug($"run counted: {r.Dungeon}, {r.Seconds} s, {r.Dp} DP"); if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play(); }
                        else Debug("end-window check fired but the text wasn't a cleared window: " + string.Join(" | ", lines.Take(4)), "endmiss");
                    }
                    catch (Exception ex) { Warn("⚠ " + ex.Message); }
                    finally { copy.Dispose(); busyEnd = false; Render(); }
                }));
            }
        }

        /// Searches the whole screen for the inventory grid (every 3 s at most, only while waiting for counts),
        /// so it works after a reset or after moving the inventory window without setting the area again.
        void TryLocateInventory(DateTime now)
        {
            if ((now - lastLocate).TotalSeconds < 3) return;
            lastLocate = now;
            var screen = PartyOcr.PhysicalVirtualScreen();                        // every monitor
            using (var bmp = PartyOcr.Capture(screen))
            {
                var img = PartyOcr.ToImg(bmp);
                // Slot size from the desktop resolution and from a 2560x1440 game; keep the stronger grid.
                Grid? found = null; double bestWeak = 0;
                foreach (var p in new[] { ExpectedPitch, FarmCheck.RefPitch }.Distinct())
                {
                    var g1 = FarmCheck.LocateInventory(img, p);
                    if (g1 == null) continue;
                    double w = FarmCheck.WeakestLine(img, g1.Value);
                    if (w > bestWeak) { bestWeak = w; found = g1; }
                }
                if (found == null) { Debug($"searched all screens ({screen.Width}x{screen.Height}): no inventory grid visible", "locate-none"); return; }
                var g = found.Value; int m = (int)Math.Round(g.PitchX * 0.2);
                invArea = new Rectangle(screen.X + (int)g.X - m, screen.Y + (int)g.Y - m, (int)Math.Round(8 * g.PitchX) + 2 * m, (int)Math.Round(8 * g.PitchY) + 2 * m);
                expectedPitchOverride = g.PitchX;
                grid = null; lastInvRead = DateTime.MinValue; SaveConfig();
                Debug($"found the inventory elsewhere on screen: area set to {invArea.X},{invArea.Y} {invArea.Width}x{invArea.Height}");
            }
        }

        /// The inventory area plus the tab strip above it (so the active tab can be checked).
        int TabMargin => (int)Math.Round(70 * invArea.Height / 614.0);
        Rectangle InvCapture => new Rectangle(invArea.X, Math.Max(0, invArea.Y - TabMargin), invArea.Width, invArea.Height + Math.Min(invArea.Y, TabMargin));

        void CheckInventory(DateTime now)
        {
            if (pending != null) return;                                       // waiting for the player's answer
            using (var bmp = PartyOcr.Capture(InvCapture))
            {
                var img = PartyOcr.ToImg(bmp);
                int top = img.H - invArea.Height;
                if (grid == null)
                {
                    var g0 = FarmCheck.FindGrid(img, 0, top, img.W, invArea.Height, 8, 8, ExpectedPitch);
                    if (!FarmCheck.InventoryOpen(img, g0) && Math.Abs(ExpectedPitch - FarmCheck.RefPitch) > 3)
                        g0 = FarmCheck.FindGrid(img, 0, top, img.W, invArea.Height, 8, 8, FarmCheck.RefPitch);   // game at 2560x1440 on another desktop size
                    if (!FarmCheck.InventoryOpen(img, g0))
                    {
                        invOpenSince = DateTime.MinValue; Debug($"inventory not open in its area (grid contrast {g0.Score:0.00}, needs 1.70)", "closed");
                        waitReason = "no inventory grid visible yet (searching the screen every 3 s)";
                        if (baseline == null || finishing) TryLocateInventory(now);          // waiting for counts: maybe it's elsewhere
                        return;
                    }
                    grid = g0; Debug($"grid found at {g0.X:0},{g0.Y:0} in the capture, slot {g0.PitchX:0.0}x{g0.PitchY:0.0} px, contrast {g0.Score:0.00}");
                }
                if (!FarmCheck.InventoryOpen(img, grid.Value))
                {
                    invOpenSince = DateTime.MinValue; Debug("inventory closed (or moved)", "closed");
                    waitReason = "no inventory grid visible yet (searching the screen every 3 s)";
                    if (baseline == null || finishing) TryLocateInventory(now);
                    return;
                }
                if (invOpenSince == DateTime.MinValue) invOpenSince = now;
                if ((now - invOpenSince).TotalSeconds < 0.5 || (now - lastInvRead).TotalSeconds < 1) return;   // open for 0.5 s, read once a second
                lastInvRead = now;
                // Another inventory tab open? Compare the tab strip with the core tab's.
                double tabDist = coreTab == null ? 0 : FarmCheck.Dist(coreTab, FarmCheck.TabPrint(img, grid.Value));
                var (g, slots) = FarmCheck.BestRead(img, grid.Value, icons, Digits);
                int found = slots.Select(x => x.Item).Distinct().Count();
                int allCores = icons.Select(i => i.name).Distinct().Count();
                if (tabDist > FarmCheck.TabMatchMax)
                {
                    // Most of the cores recognised: this IS the core tab, the saved tab strip is stale. Re-learn it.
                    if (found >= Math.Max(5, allCores * 7 / 10))
                    { coreTab = FarmCheck.TabPrint(img, g); SaveConfig(); Debug($"tab strip looked different ({tabDist:0.0}) but {found} cores were recognised: core tab re-learned"); }
                    else
                    {
                        Debug($"skipped: another inventory tab is open (tab strip differs {tabDist:0.0}, limit {FarmCheck.TabMatchMax}; {found} core types recognised)", "othertab");
                        waitReason = "another inventory tab seems to be open - switch to the core tab";
                        return;
                    }
                }
                int expectedFound = baseline != null ? baseline.Count(kv => kv.Value > 0) : Math.Min(5, icons.Count / 2);
                SaveAnnotated(bmp, g, slots, top);
                if (found == 0 || found * 2 < expectedFound)
                {
                    Debug($"skipped: only {found} core types recognised (need {Math.Max(1, (expectedFound + 1) / 2)}) - wrong tab or icons need re-learning", "few:" + found);
                    waitReason = $"only {found} core types recognised - core tab open? (else Areas & learning > Learn core icons)";
                    return;
                }
                waitReason = null;
                if (coreTab == null) { coreTab = FarmCheck.TabPrint(img, g); SaveConfig(); Debug("core tab remembered from this read"); }
                Debug("read: " + string.Join(", ", slots.Select(x => $"{Short(x.Item)}@{x.Row},{x.Col}={(x.Count?.ToString() ?? "?")} (icon {x.IconDist:0.00})")), "read:" + string.Join(",", slots.Select(x => x.Item + x.Count)));
                grid = g; lastInvImg = img; lastSlots = slots;
                var counts = new Dictionary<string, int>();
                foreach (var sl in slots) if (sl.Count.HasValue) counts[sl.Item] = counts.TryGetValue(sl.Item, out int c) ? c + sl.Count.Value : sl.Count.Value;
                if (slots.Any(x => !x.Count.HasValue)) Warn("Some counts unreadable: Areas & learning > Fix counts");
                ApplyRead(counts);
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
                                { rare[p.Value.item] = (rare.TryGetValue(p.Value.item, out int n) ? n : 0) + p.Value.qty; Debug($"rare drop: {p.Value.item} x{p.Value.qty}"); }
                            }
                        lastLoot = lines;
                    }
                    catch { }
                    finally { copy.Dispose(); busyLoot = false; }
                }));
            }
        }

        /// Takes a finished inventory read. Missing cores are confirmed with the player before they count as 0.
        void ApplyRead(Dictionary<string, int> counts)
        {
            var missing = FarmCheck.MissingToConfirm(icons.Select(i => i.name), counts, baseline, confirmedEmpty);
            if (missing.Count > 0)
            {
                Debug("asking about missing cores: " + string.Join(", ", missing));
                bool start = baseline == null;
                pending = (missing, counts, start);
                confirmText.Text = start
                    ? "Not found: " + string.Join(", ", missing) + ". You have none of these?"
                    : "Not found now: " + string.Join(", ", missing.Select(n => $"{n} (had {baseline[n]})")) + ". Used them all?";
                confirmYes.Text = start ? "Yes, none" : "Yes, used up";
                return;
            }
            pending = null;
            Commit(counts);
        }

        void Commit(Dictionary<string, int> counts)
        {
            foreach (var n in confirmedEmpty) if (!counts.ContainsKey(n)) counts[n] = 0;
            if (baseline == null) { baseline = counts; if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play(); }
            else current = counts;
            if (finishing) StopSession();
        }

        void ResolvePending(bool yes)
        {
            if (pending == null) return;
            var p = pending.Value; pending = null;
            if (yes) { foreach (var n in p.names) confirmedEmpty.Add(n); Commit(p.counts); }
            else lastInvRead = DateTime.MinValue;                               // read again on the next check
            Render();
        }

        static string Short(string n) => n.Replace("Upgrade Core", "UC").Replace("Force Core", "FC").Replace("(", "").Replace(")", "");

        /// Saves the capture with the found grid and each slot's result drawn on it (Diagnostics > picture).
        void SaveAnnotated(Bitmap src, Grid g, List<SlotRead> slots, int top)
        {
            try
            {
                src.Save(InvRawPath, System.Drawing.Imaging.ImageFormat.Png);    // plain capture too, for checking icons later
                using (var bmp = new Bitmap(src))
                using (var gr = Graphics.FromImage(bmp))
                using (var pen = new Pen(Color.Lime, 1))
                using (var f = new Font("Segoe UI", 8f, FontStyle.Bold))
                {
                    double sx = g.X - FarmCheck.SlotInset * g.Scale;   // grid lines at the slot borders
                    for (int k = 0; k <= 8; k++)
                    {
                        float x = (float)(g.X + k * g.PitchX), y = (float)(g.Y + k * g.PitchY);
                        gr.DrawLine(pen, x, (float)g.Y, x, (float)(g.Y + 8 * g.PitchY));
                        gr.DrawLine(pen, (float)g.X, y, (float)(g.X + 8 * g.PitchX), y);
                    }
                    foreach (var sl in slots)
                    {
                        float x = (float)(g.X + sl.Col * g.PitchX + 2), y = (float)(g.Y + sl.Row * g.PitchY + 2);
                        string t = Short(sl.Item) + "\n" + (sl.Count?.ToString() ?? "?");
                        gr.FillRectangle(new SolidBrush(Color.FromArgb(170, 0, 0, 0)), x, y, (float)g.PitchX - 4, 26);
                        gr.DrawString(t, f, sl.Count.HasValue ? Brushes.Yellow : Brushes.Red, x, y);
                    }
                    bmp.Save(InvShotPath, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        // ---------- learning ----------
        void LearnIcons()
        {
            using (var bmp = PartyOcr.Capture(InvCapture))
            {
                var img = PartyOcr.ToImg(bmp);
                var g = FarmCheck.FindGrid(img, 0, img.H - invArea.Height, img.W, invArea.Height, 8, 8, ExpectedPitch);
                if (!FarmCheck.InventoryOpen(img, g))
                {
                    lastLocate = DateTime.MinValue; TryLocateInventory(DateTime.Now);
                    if (grid == null && invArea != Rectangle.Empty && lastInvRead == DateTime.MinValue && debug.LastOrDefault()?.Contains("found the inventory") == true)
                    { MessageBox.Show("Found the inventory at a new spot. Press Learn core icons once more.", "Farm Tracker"); return; }
                    Debug($"learn icons: inventory not found (grid contrast {g.Score:0.00})");
                    MessageBox.Show("Open the inventory on the core tab first (and check Set inventory area).", "Farm Tracker"); return;
                }
                g = FarmCheck.BestRead(img, g, icons, Digits).grid;
                // same layout as taught: top row Upgrade Cores highest -> lowest, bottom row Force Cores
                var learned = new List<(string, double[])>();
                var emptySlots = new List<string>();
                for (int i = 0; i < FarmCheck.Icons.Length; i++)
                {
                    var f = FarmCheck.IconFeature(img, g, i / 5, i % 5);
                    if (FarmCheck.Dist(f, FarmCheck.EmptySlot) < 1.0) emptySlots.Add(FarmCheck.Icons[i].name); else learned.Add((FarmCheck.Icons[i].name, f));
                }
                // Check the new icons before keeping them: every learned core must be found again, in its own slot.
                var verify = FarmCheck.ReadInventory(img, g, learned, Digits);
                bool ok = learned.Count >= 5 && learned.All(l => verify.Any(v => v.Item == l.Item1));
                SaveAnnotated(bmp, g, verify, img.H - invArea.Height);
                Debug($"learn icons: {learned.Count} learned, empty slots: {(emptySlots.Count == 0 ? "none" : string.Join(", ", emptySlots))}, verify {(ok ? "OK" : "FAILED")}");
                if (!ok)
                {
                    MessageBox.Show("Learning didn't check out, so the old icons were kept.\n\nMake sure the core tab is open with the cores in the first two rows (top: Upgrade Core Ultimate to Low, bottom: Force Core Ultimate to Low) and the mouse away from the inventory.\n\nDiagnostics > last inventory read shows what was seen.", "Farm Tracker");
                    return;
                }
                // keep defaults for cores whose slot was empty, so they're still recognised later
                foreach (var name in emptySlots) { var d = icons.FirstOrDefault(x => x.name == name); if (d.f != null) learned.Add((name, d.f)); }
                icons = learned; grid = g; coreTab = FarmCheck.TabPrint(img, g); SaveConfig();
                MessageBox.Show($"Learned {learned.Count - emptySlots.Count} core icons and remembered this tab as the core tab." + (emptySlots.Count > 0 ? "\nEmpty slots (kept the defaults): " + string.Join(", ", emptySlots) : ""), "Farm Tracker");
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
            {
                Line("Open the core tab for a moment to save the start counts. Runs and drops are already being counted.", Color.FromArgb(245, 196, 81), true);
                if (waitReason != null) Line("Waiting: " + waitReason, Theme.Muted);
            }
            if (finishing && waitReason != null) Line("Waiting: " + waitReason, Theme.Muted);
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
            bool showConfirm = pending != null;
            if (confirmBox.Visible != showConfirm) confirmBox.Visible = showConfirm;
            if (confirmText.MaximumSize.Width != Width - 30) confirmText.MaximumSize = new Size(Width - 30, 0);
            Ui.SetLines(body, lines, Width - 30);
            FitHeight();
        }

        /// Grow or shrink to the content instead of a fixed size.
        void FitHeight()
        {
            int want = 26 /*title*/ + Content.Padding.Vertical + status.Height + startStop.Height + 6 + Ui.LinesHeight(body) + footer.Height + 8
                     + (confirmBox.Visible ? confirmBox.PreferredSize.Height : 0);
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
