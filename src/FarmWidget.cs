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
        readonly CheckBox debugBox = new CheckBox { Width = 22, Text = "", Cursor = Cursors.Hand, Margin = new Padding(0), Padding = new Padding(4, 0, 0, 0), BackColor = Theme.Line };
        readonly ToolTip debugTip = new ToolTip { ShowAlways = true };
        DebugOverlay overlay; DateTime lastProbe = DateTime.MinValue;
        // what the overlay shows (screen pixels)
        Rectangle ovInvCap, ovTitle; string ovInv = "not checked yet"; Grid? ovGrid; List<SlotRead> ovSlots = new List<SlotRead>(); string ovTab; Color ovTabColor = Color.Orange;
        string ovEnd = "not seen"; Color ovEndColor = Color.HotPink; string ovLoot = "";
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

        /// Logs which reader the counts use (Tesseract, or Windows' reader and why Tesseract isn't there) once, and again
        /// only if it changes.
        string countEngineLogged;
        void LogCountEngine()
        {
            string st = PartyOcr.CountEngineStatus;
            if (st != countEngineLogged) { countEngineLogged = st; Debug("count reader: " + st); }
        }

        // Slot size: nothing is guessed from the screen resolution (the game can run at another size than the
        // desktop, or on another monitor). slotSize is saved once a read has recognised cores; foundPitch comes
        // from the last on-screen search. Unknown = 0 = FindGrid tries every slot size.
        double? slotSize, foundPitch;
        double KnownPitch => slotSize ?? foundPitch ?? 0;
        // The UI scale the sword button was last found at (1 = the 2560x1440 reference; the game's UI size option
        // changes it). The anchor matches the button (and checks the close cross) at this scale; unknown = from the
        // slot size. Saved as "scale=".
        double? buttonScale;
        double AnchorScale => buttonScale ?? (KnownPitch > 0 ? KnownPitch / FarmCheck.RefPitch : 0);
        // The inventory went missing from its area (grid or anchor): one screen search right away, then only the
        // 3 s schedule until it is read again.
        bool locatedSinceMissing;
        string locatedBy = "saved area";                                        // how the inventory area was found: title text / band search / saved area

        // areas (screen pixels). Defaults measured on 2560x1440 screenshots (2026-09-28).
        Rectangle invArea = new Rectangle(1905, 240, 620, 615), endArea = new Rectangle(209, 284, 630, 745), lootArea = new Rectangle(2105, 1195, 395, 160);
        List<(string name, double[] f)> icons = FarmCheck.DefaultIcons();
        List<string> rareWords = new List<string> { "Jewel", "Slot Extender", "Potion of Luck", "Stone" };

        // session
        bool running, finishing; DateTime started, finishStarted; DateTime lastCheck = DateTime.MinValue;
        const int FreshCountsSeconds = 10, FinishTimeoutSeconds = 90;
        readonly List<(DateTime at, FarmCheck.RunResult run)> runs = new List<(DateTime, FarmCheck.RunResult)>();
        Dictionary<string, int> baseline, current;
        readonly Dictionary<string, int> rare = new Dictionary<string, int>();
        List<string> lastLoot; long lootPrint;
        bool endLatched; DateTime endGoneSince = DateTime.MinValue, invOpenSince = DateTime.MinValue, lastInvRead = DateTime.MinValue;
        Grid? grid; Grid? readGrid;
        string lastAnchorInfo, ovAnchor = ""; bool ovAnchorOk; FarmCheck.AnchorResult ovAnchorRes; Rectangle ovAnchorCap;
        readonly Dictionary<(int r, int c), string> slotIdentity = new Dictionary<(int, int), string>();   // sticky icon identities per slot
        // the reader's last answer per slot (null = "not read"), so the overlay keeps it while a fresh icon read waits for counts
        readonly Dictionary<(int r, int c, string item), int?> lastKnownCounts = new Dictionary<(int, int, string), int?>();
        double[] coreTab;                                                       // tab-strip fingerprint of the core tab
        DateTime lastLocate = DateTime.MinValue;
        bool busyEnd, busyLoot, busyInv, busyProbeOcr; int stripSaves;
        readonly Dictionary<string, int> lastCounts = new Dictionary<string, int>();
        string warning; DateTime warningAt;
        readonly HashSet<string> confirmedEmpty = new HashSet<string>();
        readonly FarmCheck.CountSmoother smoother = new FarmCheck.CountSmoother();
        int readsSinceStart;
        (List<string> names, Dictionary<string, int> counts, bool start)? pending;       // waiting for the player's answer
        readonly FlowLayoutPanel confirmBox = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Padding = new Padding(0, 4, 0, 4) };
        readonly Label confirmText = new Label { AutoSize = true, ForeColor = Color.FromArgb(245, 196, 81), Font = Theme.Title, UseMnemonic = false };
        readonly Button confirmYes = Theme.MakeButton("Yes, none", primary: true), confirmAgain = Theme.MakeButton("Read again");
        void Warn(string text) { warning = text; warningAt = DateTime.Now; }

        readonly Label status = new Label { Dock = DockStyle.Top, Height = 34, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false };
        readonly Button startStop = Theme.MakeButton("Start session", primary: true);
        readonly FlowLayoutPanel body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
        readonly Label footer = new Label { Dock = DockStyle.Bottom, Height = 20, Font = Theme.Small, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Text = "Areas & learning ▾", TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        readonly Label saveImage = new Label { Dock = DockStyle.Bottom, Height = 20, Font = Theme.Small, ForeColor = Theme.Accent, Cursor = Cursors.Hand, Text = "Save image (screen + debug lines)", TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };

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
            Content.Controls.Add(body); Content.Controls.Add(confirmBox); Content.Controls.Add(saveImage); Content.Controls.Add(footer); Content.Controls.Add(startStop); Content.Controls.Add(status);
            saveImage.Click += (s, e) => { Touch(); SaveView(); };
            startStop.Click += (s, e) => { Touch(); if (!running) StartSession(); else if (finishing) StopSession(); else RequestStop(); };
            footer.Click += (s, e) => { Touch(); Menu2().Show(footer, new Point(0, footer.Height)); };
            debugTip.SetToolTip(debugBox, "Debug: draw what the tracker sees on screen");
            debugBox.CheckedChanged += (s, e) => { Touch(); SetOverlay(debugBox.Checked); };
            Load += (s, e) => { LoadConfig(); Render(); AddTitleControl(debugBox); };
            FormClosed += (s, e) => SetOverlay(false);
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
                else if (k == "slot") slotSize = FarmCheck.ParseSlotSize(v);
                else if (k == "scale") buttonScale = FarmCheck.ParseUiScale(v);
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
            if (slotSize != null) lines.Add("slot=" + FarmCheck.FormatSlotSize(slotSize.Value));
            if (buttonScale != null) lines.Add("scale=" + FarmCheck.FormatUiScale(buttonScale.Value));
            if (coreTab != null) lines.Add("coretab=" + string.Join(" ", coreTab.Select(x => x.ToString("0.0", CultureInfo.InvariantCulture))));
            try { File.WriteAllLines(ConfigPath, lines); } catch { }
        }

        ContextMenuStrip Menu2()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Set inventory area (the core tab's slot grid)…", null, (s, e) => Pick(ref invArea, "Drag a box around the inventory's slot grid (the whole inventory window is fine too). Esc cancels.", resetGrid: true));
            m.Items.Add("Set end-window area (dungeon cleared window)…", null, (s, e) => Pick(ref endArea, "Drag a box around the dungeon end window (\"Quest Dungeon Cleared!\"). Esc cancels."));
            m.Items.Add("Set loot-feed area (\"Obtain ... x 1\" lines)…", null, (s, e) => Pick(ref lootArea, "Drag a box around the loot messages (\"Obtain ... x 1\"). Esc cancels."));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Learn core icons from the open inventory", null, (s, e) => LearnIcons());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Open farm log", null, (s, e) => Files.OpenInTextEditor(LogPath));
            m.Items.Add("Diagnostics: what the tracker decided", null, (s, e) => Files.OpenInTextEditor(DebugPath));
            m.Items.Add("Diagnostics: last count strip (picture)", null, (s, e) =>
            {
                var pth = Path.Combine(Dir, "cabal-helper-farm-digits-last.png");
                try { if (PartyOcr.SaveLastStrip(pth)) Files.OpenFolder(pth); else MessageBox.Show("No count read yet.", "Farm Tracker"); }
                catch (Exception ex) { Debug($"count strip picture failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}"); MessageBox.Show("Could not save the picture: " + ex.Message, "Farm Tracker"); }
            });
            m.Items.Add("Diagnostics: last inventory read (picture)", null, (s, e) => { if (File.Exists(InvShotPath)) Files.OpenFolder(InvShotPath); else MessageBox.Show("No inventory read yet.", "Farm Tracker"); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Reset areas and everything learned…", null, (s, e) => ResetAll());
            return m;
        }

        void ResetAll()
        {
            if (MessageBox.Show("Reset the Farm Tracker?\n\nAreas go back to the defaults and the learned core icons and core tab are forgotten. The farm log is kept.",
                                "Farm Tracker", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            invArea = new Rectangle(1905, 240, 620, 615); endArea = new Rectangle(209, 284, 630, 745); lootArea = new Rectangle(2105, 1195, 395, 160);
            icons = FarmCheck.DefaultIcons(); coreTab = null; grid = null; readGrid = null; slotSize = null; foundPitch = null; buttonScale = null; locatedBy = "saved area"; ovTitle = Rectangle.Empty;
            slotIdentity.Clear(); lastKnownCounts.Clear();
            try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); } catch { }
            Debug("RESET: areas back to defaults, learned icons/core tab forgotten");
            Render();
        }

        void Pick(ref Rectangle area, string msg, bool resetGrid = false)
        {
            var r = AreaPicker.Pick(msg);
            if (r == null) return;
            area = r.Value; if (resetGrid) { grid = null; coreTab = null; foundPitch = null; buttonScale = null; locatedBy = "saved area"; ovTitle = Rectangle.Empty; lastKnownCounts.Clear(); }
            Debug($"area set: {r.Value.X},{r.Value.Y} {r.Value.Width}x{r.Value.Height}");
            SaveConfig(); Render();
        }

        // ---------- session ----------
        void StartSession()
        {
            Debug($"session started (screen {PartyOcr.PhysicalScreenWidth()}x{PartyOcr.PhysicalScreenHeight()}, all screens {PartyOcr.PhysicalVirtualScreen()}, inventory area {invArea.X},{invArea.Y} {invArea.Width}x{invArea.Height} ({locatedBy}), slot size {(KnownPitch > 0 ? KnownPitch.ToString("0.0", CultureInfo.InvariantCulture) + " px" : "not known yet")})");
            running = true; started = DateTime.Now; lastLocate = DateTime.MinValue; locatedSinceMissing = false; runs.Clear(); rare.Clear(); baseline = null; current = null; lastLoot = null; endLatched = false;
            confirmedEmpty.Clear(); pending = null; smoother.Clear(); readsSinceStart = 0; lastCounts.Clear(); slotIdentity.Clear(); lastKnownCounts.Clear();
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
            if (!running) { if (overlay != null) Probe(now); return default; }
            if ((now - lastCheck).TotalMilliseconds < 500) return default;
            lastCheck = now;
            try
            {
                using (OverlayHidden()) { CheckEnd(now); CheckInventory(now); }
                if (!running) return default;                                     // stopped by the final inventory read
                using (OverlayHidden()) CheckLoot();
                if (finishing && (now - finishStarted).TotalSeconds >= FinishTimeoutSeconds) { StopSession(); return default; }
            }
            catch (Exception ex) { Warn("⚠ " + ex.Message); }
            Render();
            DrawOverlay();
            return default;
        }

        void CheckEnd(DateTime now)
        {
            if (busyEnd) return;
            using (var bmp = PartyOcr.Capture(endArea))
            {
                bool likely = FarmCheck.EndWindowLikely(PartyOcr.ToImg(bmp));
                ovEnd = likely ? (endLatched ? "counted, waiting for it to close" : "seen, reading…") : "not seen"; ovEndColor = likely ? Color.Lime : Color.HotPink;
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
                        if (r != null) { runs.Add((DateTime.Now, r)); endLatched = true; ovEnd = $"run counted: {r.Dungeon} {r.Seconds / 60}:{r.Seconds % 60:00}, {r.Dp} DP"; Debug($"run counted: {r.Dungeon}, {r.Seconds} s, {r.Dp} DP"); if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play(); }
                        else Debug("end-window check fired but the text wasn't a cleared window: " + string.Join(" | ", lines.Take(4)), "endmiss");
                    }
                    catch (Exception ex) { Warn("⚠ " + ex.Message); }
                    finally { copy.Dispose(); busyEnd = false; Render(); }
                }));
            }
        }

        /// Older Windows can't exclude windows from captures: hide the overlay while capturing instead.
        IDisposable OverlayHidden()
        {
            if (overlay == null || Native.CaptureExclusionSupported) return null;
            overlay.Visible = false;
            return new Restore(() => { if (overlay != null) overlay.Visible = true; });
        }
        sealed class Restore : IDisposable { readonly Action a; public Restore(Action a) { this.a = a; } public void Dispose() => a(); }

        /// Screenshot of the screen with the debug drawings painted on it (the helper's own windows are invisible to
        /// screen captures, so the snipping tool can't show them). Saved as cabal-helper-farm-view-N.png (last 5),
        /// with the last count read (-digits.png) and an OCR input experiment (-ocr-test.png): every count region
        /// of the open inventory in Windows' 5 treatments and Tesseract's 2 inputs. Each file is saved on its own.
        int viewSaves; bool savingView;
        async void SaveView()
        {
            if (savingView) return;
            savingView = true;
            viewSaves = (viewSaves + 1) % 5;
            string path = Path.Combine(Dir, $"cabal-helper-farm-view-{viewSaves}.png");
            string digitsPath = Path.Combine(Dir, $"cabal-helper-farm-view-{viewSaves}-digits.png");
            string sheetPath = Path.Combine(Dir, $"cabal-helper-farm-view-{viewSaves}-ocr-test.png");
            var saved = new List<string>(); var failed = new List<string>();
            void Fail(string what, Exception ex) { failed.Add(what + ": " + ex.Message); Debug($"{what} failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}"); }
            try
            {
                foreach (var p in new[] { path, digitsPath, sheetPath }) try { if (File.Exists(p)) File.Delete(p); } catch { }   // no stale file from 5 saves ago
                bool hadOverlay = overlay != null;
                try
                {
                    if (!hadOverlay) overlay = DebugOverlay.Create();
                    Probe(DateTime.Now.AddSeconds(5));                               // refresh what the overlay shows
                    try { SaveScreenWithOverlay(path); saved.Add(path); }
                    catch (Exception ex) { Fail("view picture", ex); }
                }
                finally { if (!hadOverlay && overlay != null) { overlay.Close(); overlay.Dispose(); overlay = null; } }
                // what the text reader last received for the counts, with its words and results
                try { if (PartyOcr.SaveLastStrip(digitsPath)) saved.Add(digitsPath); }
                catch (Exception ex) { Fail("digits picture", ex); }
                // OCR input experiment on the inventory as it is now
                List<(string label, Img region, Img digits, int glyphs)> regions = null;
                try { regions = CaptureCountRegions(out string why); if (regions == null) Debug("OCR test sheet skipped: " + why); }
                catch (Exception ex) { Fail("OCR test capture", ex); }
                if (regions != null)
                {
                    sheetBusy = true; Render();
                    try
                    {
                        var summary = await PartyOcr.SaveOcrTestSheetAsync(sheetPath, regions);
                        saved.Add(sheetPath);
                        foreach (var line in summary) Debug("OCR test: " + line);
                    }
                    catch (Exception ex) { Fail("OCR test sheet", ex); }
                    finally { sheetBusy = false; Render(); }
                }
                Debug("view saved: " + (saved.Count > 0 ? string.Join(" + ", saved.Select(Path.GetFileName)) : "nothing") + (failed.Count > 0 ? "; failed: " + string.Join("; ", failed) : ""));
                if (saved.Count > 0) Files.OpenFolder(saved[0]);
                if (failed.Count > 0 && !saved.Contains(path))
                    MessageBox.Show("Could not save the image: " + string.Join("\n", failed) + "\n\nDetails are in Areas & learning > Diagnostics.", "Farm Tracker");
            }
            catch (Exception ex)
            {
                Fail("save image", ex);
                MessageBox.Show("Could not save the image: " + ex.Message + "\n\nDetails are in Areas & learning > Diagnostics.", "Farm Tracker");
            }
            finally { savingView = false; }
        }
        bool sheetBusy;

        /// All monitors with the overlay's drawings and a label on top, saved as png.
        void SaveScreenWithOverlay(string path)
        {
            var screen = PartyOcr.PhysicalVirtualScreen();
            using (var bmp = PartyOcr.Capture(screen))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                overlay?.Render(g, -screen.X, -screen.Y);                             // screen can start at negative x/y
                string label = $"Cabal Helper v{Updater.Short(Updater.Current)} · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {status.Text}";
                using (var f = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                {
                    var size = g.MeasureString(label, f);
                    g.FillRectangle(bg, 8, 8, size.Width + 12, size.Height + 6);
                    g.DrawString(label, f, Brushes.White, 14, 11);
                }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// The count region of every recognised core slot in the inventory as it is now, found the same way as the
        /// preview read (Probe): pinned grid when the anchor works, else the best read. null (+ why) = no inventory.
        List<(string label, Img region, Img digits, int glyphs)> CaptureCountRegions(out string why)
        {
            var cap = InvCapture;
            if (cap.Width <= 0 || cap.Height <= 0) { why = $"inventory area is off screen ({cap.Width}x{cap.Height})"; return null; }
            Img img;
            using (OverlayHidden())
            using (var bmp = PartyOcr.Capture(cap)) img = PartyOcr.ToImg(bmp);
            int top = invArea.Y - cap.Y;
            var g = FarmCheck.FindGridNear(img, 0, top, img.W, invArea.Height, KnownPitch);
            if (!FarmCheck.InventoryOpen(img, g)) { why = $"inventory not visible (contrast {g.Score:0.00}, needs 1.70)"; return null; }
            var anch = FarmCheck.AnchorByButtons(img, g, out FarmCheck.AnchorResult res, AnchorScale);
            var (g2, slots) = res.Ok ? (anch, FarmCheck.ReadInventory(img, anch, icons, 8, 8, new Dictionary<(int, int), string>(slotIdentity))) : FarmCheck.BestRead(img, g, icons);
            if (slots.Count == 0) { why = "no core slots recognised"; return null; }
            var list = new List<(string, Img, Img, int)>();
            foreach (var sl in slots.OrderBy(x => x.Row * 8 + x.Col))
            {
                var region = FarmCheck.CountRegionImage(img, g2, sl.Row, sl.Col, out int n, out Img digits);
                bool none = region == null || region.W <= 0 || region.H <= 0;
                list.Add(($"{Short(sl.Item)} ({n} digits)", none ? null : region, none ? null : digits, n));
            }
            why = null;
            return list;
        }

        void SetOverlay(bool on)
        {
            if (on && overlay == null) { overlay = DebugOverlay.Create(); lastProbe = DateTime.MinValue; }
            else if (!on && overlay != null) { overlay.Close(); overlay.Dispose(); overlay = null; }
            DrawOverlay();
        }

        void DrawOverlay()
        {
            if (overlay == null) return;
            overlay.Clear();
            var invC = ovInvCap == Rectangle.Empty ? InvCapture : ovInvCap;
            overlay.Boxes.Add(new DebugOverlay.Box { R = invArea, C = Color.DeepSkyBlue, Label = $"Inventory ({locatedBy}): " + ovInv });
            if (ovTitle != Rectangle.Empty) overlay.Boxes.Add(new DebugOverlay.Box { R = ovTitle, C = Color.DeepSkyBlue, Label = "title" });
            if (ovAnchorRes != null)
            {
                Rectangle Sc(Rectangle r) => new Rectangle(ovAnchorCap.X + r.X, ovAnchorCap.Y + r.Y, r.Width, r.Height);
                var a = ovAnchorRes; var violet = Color.FromArgb(200, 120, 255);
                if (a.ButtonSearch != Rectangle.Empty) overlay.Boxes.Add(new DebugOverlay.Box { R = Sc(a.ButtonSearch), C = Color.FromArgb(120, 70, 160), Label = "button search" });
                overlay.Boxes.Add(new DebugOverlay.Box { R = Sc(a.ButtonAt), C = a.ButtonDiff <= FarmCheck.ButtonGoodMax ? Color.Lime : Color.Red, Label = $"sword button {(a.ButtonDiff <= FarmCheck.ButtonGoodMax ? "✓" : "✗")} diff {a.ButtonDiff:0.0}" });
                if (a.CrossSearch != Rectangle.Empty)
                {
                    overlay.Boxes.Add(new DebugOverlay.Box { R = Sc(a.CrossSearch), C = Color.FromArgb(120, 70, 160), Label = "cross search" });
                    overlay.Boxes.Add(new DebugOverlay.Box { R = Sc(a.CrossAt), C = a.CrossDiff <= FarmCheck.CrossGoodMax ? Color.Lime : Color.Red, Label = $"close cross {(a.CrossDiff <= FarmCheck.CrossGoodMax ? "✓" : "✗")} diff {a.CrossDiff:0.0} scale {a.ScaleCheck:0.000}" });
                    if (a.Ok) overlay.Lines.Add(new DebugOverlay.Seg { A = new Point(Sc(a.ButtonAt).X, Sc(a.ButtonAt).Y), B = new Point(Sc(a.CrossAt).X, Sc(a.CrossAt).Y), C = violet });
                }
                if (a.Ok && ovGrid is Grid gg2)
                    overlay.Lines.Add(new DebugOverlay.Seg { A = new Point(Sc(a.ButtonAt).X, Sc(a.ButtonAt).Y), B = new Point(ovAnchorCap.X + (int)gg2.X, ovAnchorCap.Y + (int)gg2.Y), C = violet });
            }
            if (ovGrid is Grid g)
            {
                int gx = invC.X, gy = invC.Y;
                for (int k = 0; k <= 8; k++)
                {
                    int x = gx + (int)Math.Round(g.X + k * g.PitchX), y = gy + (int)Math.Round(g.Y + k * g.PitchY);
                    overlay.Lines.Add(new DebugOverlay.Seg { A = new Point(x, gy + (int)g.Y), B = new Point(x, gy + (int)Math.Round(g.Y + 8 * g.PitchY)), C = Color.Lime });
                    overlay.Lines.Add(new DebugOverlay.Seg { A = new Point(gx + (int)g.X, y), B = new Point(gx + (int)Math.Round(g.X + 8 * g.PitchX), y), C = Color.Lime });
                }
                foreach (var t in FarmCheck.MergeCountsForDisplay(ovSlots, lastKnownCounts))
                {
                    // Read: yellow; Pending (waiting for the reader): last known count in dim yellow with "…"; Failed: red "?"
                    var sl = t.Slot;
                    string shown = t.State == SlotCountState.Pending ? (t.Count?.ToString() ?? "") + "…" : t.Count?.ToString() ?? "?";
                    var col = t.State == SlotCountState.Read ? Color.Yellow : t.State == SlotCountState.Pending ? Color.FromArgb(190, 180, 90) : Color.Red;
                    overlay.Tags.Add(new DebugOverlay.Note { P = new Point(gx + (int)(g.X + sl.Col * g.PitchX) + 2, gy + (int)(g.Y + sl.Row * g.PitchY) + 2),
                        Text = Short(sl.Item) + " " + shown + $" ·{sl.IconDist:0.0}", C = col });
                }
                if (ovTab != null)
                {
                    double sc = g.Scale;
                    var tr = new Rectangle(gx + (int)g.X, gy + (int)(g.Y - 58 * sc), (int)(8 * g.PitchX), (int)(38 * sc));
                    overlay.Boxes.Add(new DebugOverlay.Box { R = tr, C = ovTabColor, Label = ovTab });
                }
            }
            overlay.Boxes.Add(new DebugOverlay.Box { R = endArea, C = ovEndColor, Label = "End window: " + ovEnd });
            overlay.Boxes.Add(new DebugOverlay.Box { R = lootArea, C = Color.Gold, Label = "Loot feed" + (ovLoot.Length > 0 ? ": " + ovLoot : "") });
            overlay.KeepOnTop(); overlay.Invalidate();
        }

        /// Debug on, no session running: look at the areas once a second just to draw them (nothing is counted).
        void Probe(DateTime now)
        {
            if ((now - lastProbe).TotalSeconds < 1) return;
            lastProbe = now;
            try
            {
                using (OverlayHidden())
                using (var bmp = PartyOcr.Capture(InvCapture))
                {
                    var img = PartyOcr.ToImg(bmp); int top = invArea.Y - InvCapture.Y;
                    ovInvCap = InvCapture;
                    var g = FarmCheck.FindGridNear(img, 0, top, img.W, invArea.Height, KnownPitch);
                    if (!FarmCheck.InventoryOpen(img, g)) { ovInv = $"not visible (contrast {g.Score:0.00}, needs 1.70)"; ovGrid = null; ovSlots = new List<SlotRead>(); ovTab = null; }
                    else
                    {
                        var anch = FarmCheck.AnchorByButtons(img, g, out FarmCheck.AnchorResult pres, AnchorScale);
                        ovAnchorRes = pres; ovAnchorCap = InvCapture; ovAnchor = pres.Info; ovAnchorOk = pres.Ok;
                        var (g2, slots) = pres.Ok ? (anch, FarmCheck.ReadInventory(img, anch, icons, 8, 8, slotIdentity)) : FarmCheck.BestRead(img, g, icons);
                        if (pres.Ok)
                        {
                            foreach (var sl in slots) slotIdentity[(sl.Row, sl.Col)] = sl.Item;
                            foreach (var k in slotIdentity.Keys.ToList()) if (!slots.Any(x => x.Row == k.r && x.Col == k.c)) slotIdentity.Remove(k);
                        }
                        FarmCheck.ForgetMissingCounts(slots, lastKnownCounts);
                        ovGrid = g2; ovSlots = slots; ovInv = $"open, contrast {g.Score:0.00}, {slots.Select(x => x.Item).Distinct().Count()} core types";
                        // Preview counts too (no session): read them with the text reader in the background.
                        if (pres.Ok && !busyProbeOcr && slots.Count > 0)
                        {
                            busyProbeOcr = true;
                            var copyP = (Bitmap)bmp.Clone(); var gridP = g2; var slotsP = slots;
                            BeginInvoke((Action)(async () =>
                            {
                                try
                                {
                                    var imgP = PartyOcr.ToImg(copyP); var stripsP = new List<Img>(); var glyphsP = new List<int>(); var digitsP = new List<Img>();
                                    foreach (var sl in slotsP) { stripsP.Add(FarmCheck.CountRegionImage(imgP, gridP, sl.Row, sl.Col, out int n, out Img dg)); glyphsP.Add(n); digitsP.Add(dg); }
                                    var ocrP = await PartyOcr.ReadCountStripsAsync(stripsP, glyphsP, slotsP.Select(x => Short(x.Item)).ToList(), digitsP);
                                    LogCountEngine();
                                    for (int i = 0; i < slotsP.Count; i++) slotsP[i].Count = ocrP[i] ?? (glyphsP[i] == 0 ? 1 : (int?)null);
                                    if (!running) { FarmCheck.RecordCounts(slotsP, lastKnownCounts); ovSlots = slotsP; DrawOverlay(); }
                                }
                                catch (Exception ex) { Debug("preview count read failed: " + ex.Message, "previewocr"); }
                                finally { copyP.Dispose(); busyProbeOcr = false; }
                            }));
                        }
                        double td = coreTab == null ? -1 : FarmCheck.Dist(coreTab, FarmCheck.TabPrint(img, g2));
                        ovTab = td < 0 ? "Tab: not learned yet" : td <= FarmCheck.TabMatchMax ? $"Core tab ✓ ({td:0.0})" : $"Other tab? ({td:0.0} > {FarmCheck.TabMatchMax})";
                        ovTabColor = td >= 0 && td > FarmCheck.TabMatchMax ? Color.OrangeRed : Color.Orange;
                    }
                }
                using (OverlayHidden())
                using (var bmp = PartyOcr.Capture(endArea))
                { bool seen = FarmCheck.EndWindowLikely(PartyOcr.ToImg(bmp)); ovEnd = seen ? "seen" : "not seen"; ovEndColor = seen ? Color.Lime : Color.HotPink; }
            }
            catch (Exception ex) { ovInv = "error: " + ex.Message; }
            DrawOverlay();
        }

        /// Searches every monitor for the inventory (every 3 s at most, only while waiting for counts), so it
        /// works after a reset, after moving the inventory window or at any resolution or UI scale without setting
        /// the area.
        void TryLocateInventory(DateTime now)
        {
            if (busyLocate || (now - lastLocate).TotalSeconds < 3) return;
            lastLocate = now;
            _ = LocateInventoryAsync();
        }

        /// The inventory isn't where its area says (no grid there, or the button anchor failed): search the screen
        /// at once, the first time; after that (it wasn't found) only on TryLocateInventory's schedule.
        void InventoryMissing(DateTime now, string why)
        {
            if (!locatedSinceMissing && !busyLocate)
            {
                locatedSinceMissing = true; lastLocate = now;
                Debug($"{why}: searching the screen now", "missing-now");
                _ = LocateInventoryAsync();
            }
            else if (baseline == null || finishing) TryLocateInventory(now);   // waiting for counts: keep looking
        }

        /// Sets the inventory area around a grid (screen pixels): the 8x8 slots plus 0.2 slot on every side.
        void SetInventoryArea(Grid g)
        {
            int m = (int)Math.Round(g.PitchX * 0.2);
            invArea = new Rectangle((int)g.X - m, (int)g.Y - m, (int)Math.Round(8 * g.PitchX) + 2 * m, (int)Math.Round(8 * g.PitchY) + 2 * m);
        }

        bool busyLocate;
        /// 0) the sword button at any UI scale (FarmCheck.LocateByButton; off the UI thread); 1) the text reader looks
        /// for the "Inventory" title and the grid is searched just below it (slot size from the title's text height);
        /// 2) if that finds nothing, the grid pattern is searched on the whole picture (FarmCheck.LocateInventory).
        /// Coordinates are physical desktop pixels (may be negative). A slot size more than 3% off the saved one is
        /// logged as a UI scale change and saved at once.
        async Task<bool> LocateInventoryAsync()
        {
            if (busyLocate) return false;
            busyLocate = true;
            try
            {
                var screen = PartyOcr.PhysicalVirtualScreen();                    // every monitor
                Img img; List<List<OcrWord>> lines = null; string ocrError = null;
                Grid? found = null; string how = null; var titleRect = Rectangle.Empty; double? foundScale = null;
                using (var bmp = PartyOcr.Capture(screen))
                {
                    img = PartyOcr.ToImg(bmp);
                    // 0) the sword button under the inventory, at any UI scale: unique, never animates, no text reading needed
                    var shot = img; var sw = System.Diagnostics.Stopwatch.StartNew();
                    var (byButton, btnDiff, btnScale) = await Task.Run(() => { var gb = FarmCheck.LocateByButton(shot, out double d, out double sc); return (gb, d, sc); });
                    if (byButton != null)
                    {
                        found = byButton; how = "sword button"; foundScale = btnScale;
                        Debug($"sword button found on screen (diff {btnDiff:0.0}, UI scale {btnScale:0.000}, {sw.ElapsedMilliseconds} ms)");
                    }
                    else
                    {
                        Debug($"sword button not on screen (best diff {(btnDiff == double.MaxValue ? "-" : btnDiff.ToString("0.0", CultureInfo.InvariantCulture))}, {sw.ElapsedMilliseconds} ms)", "button-none");
                        try { lines = await PartyOcr.ReadWordsTiledAsync(bmp); }
                        catch (Exception ex) { ocrError = ex.Message; }
                    }
                }
                var titles = lines == null ? new List<OcrWord>() : FarmCheck.InventoryTitles(lines);
                foreach (var t in titles)
                {
                    if (found != null) break;
                    var tg = FarmCheck.FindGridBelowTitle(img, t);
                    if (tg == null) continue;
                    found = tg; how = "title text";
                    titleRect = new Rectangle(screen.X + (int)t.X, screen.Y + (int)t.Y, (int)Math.Ceiling(t.W), (int)Math.Ceiling(t.H));
                    break;
                }
                if (found == null)
                {
                    found = FarmCheck.LocateInventory(img, KnownPitch);
                    if (found == null && KnownPitch > 0) found = FarmCheck.LocateInventory(img, 0);   // known size stale (resolution changed)
                    if (found != null) how = "band search";
                }
                string titleInfo = how == "sword button" ? "button match" : lines == null ? $"text reader failed ({ocrError})"
                                 : titles.Count == 0 ? "no \"Inventory\" title read"
                                 : $"{titles.Count} \"Inventory\" title(s) at " + string.Join(" ", titles.Select(t => $"{screen.X + t.X:0},{screen.Y + t.Y:0} h{t.H:0}")) + (how == "title text" ? "" : " but no grid below");
                if (found == null) { Debug($"searched all screens ({screen.X},{screen.Y} {screen.Width}x{screen.Height}): {titleInfo}; no inventory grid visible", "locate-none"); return false; }
                var g = found.Value;
                double oldPitch = KnownPitch;
                SetInventoryArea(new Grid { X = screen.X + g.X, Y = screen.Y + g.Y, PitchX = g.PitchX, PitchY = g.PitchY });
                foundPitch = g.PitchX; locatedBy = how; ovTitle = titleRect;
                buttonScale = foundScale;                                            // unknown when found another way: from the slot size
                if (oldPitch > 0 && Math.Abs(g.PitchX / oldPitch - 1) > 0.03)
                {
                    Debug($"UI scale changed: slot {oldPitch:0.0} px -> {g.PitchX:0.0} px");
                    slotSize = g.PitchX; slotIdentity.Clear();
                }
                grid = null; readGrid = null; lastInvRead = DateTime.MinValue; lastKnownCounts.Clear(); SaveConfig();
                Debug($"found the inventory by {how}: area set to {invArea.X},{invArea.Y} {invArea.Width}x{invArea.Height}, slot {g.PitchX:0.0} px ({titleInfo})");
                return true;
            }
            catch (Exception ex) { Debug("inventory search failed: " + ex.Message, "locate-error"); return false; }
            finally { busyLocate = false; }
        }

        /// The inventory area plus the window parts the checks need: the title bar with the close cross above the
        /// grid (~92 px at 2560x1440) and the button row below it (button top ~20 px under the grid, 56 px tall).
        double UiScale => invArea.Height / 640.0;                                   // area = 8 slots + 2 x 0.2 slot margin
        int TabMargin => (int)Math.Round(115 * UiScale);
        int ButtonMargin => (int)Math.Round(95 * UiScale);
        /// (Clipped to the monitors, which can start below 0 when a monitor sits above the main one.)
        Rectangle InvCapture
        {
            get
            {
                var v = PartyOcr.PhysicalVirtualScreen();
                int top = Math.Max(v.Y, invArea.Y - TabMargin), bottom = Math.Min(v.Bottom, invArea.Bottom + ButtonMargin);
                return new Rectangle(invArea.X, top, invArea.Width, bottom - top);
            }
        }

        void CheckInventory(DateTime now)
        {
            var cap = InvCapture;
            using (var bmp = PartyOcr.Capture(cap))
            {
                var img = PartyOcr.ToImg(bmp);
                int top = invArea.Y - cap.Y;
                // The cached grid can be stale (inventory moved) or wrong: search again before calling it closed.
                bool hadGrid = grid != null;
                var cur = FarmCheck.CurrentGrid(img, grid, () => FarmCheck.FindGridNear(img, 0, top, img.W, invArea.Height, KnownPitch), out bool replaced);
                if (cur == null)
                {
                    invOpenSince = DateTime.MinValue;
                    Debug(hadGrid ? $"inventory closed (or moved): not found again in its area ({locatedBy})" : $"inventory not open in its area ({locatedBy}; no grid with contrast 1.70)", "closed");
                    ovInv = hadGrid ? "closed" : "not visible (no grid with contrast 1.70)"; ovGrid = null; ovSlots = new List<SlotRead>(); ovTab = null;
                    waitReason = "no inventory grid visible yet (searching the screen every 3 s)";
                    InventoryMissing(now, "inventory not in its area");                 // moved, or another UI scale: search now
                    return;
                }
                if (replaced)
                {
                    var g0 = cur.Value; grid = g0;
                    Debug($"grid {(hadGrid ? "found again" : "found")} at {g0.X:0},{g0.Y:0} in the capture ({locatedBy}), slot {g0.PitchX:0.0}x{g0.PitchY:0.0} px, contrast {g0.Score:0.00}");
                }
                if (invOpenSince == DateTime.MinValue) invOpenSince = now;
                if ((now - invOpenSince).TotalSeconds < 0.25 || (now - lastInvRead).TotalSeconds < 0.5) return;   // open for 0.25 s, read twice a second
                lastInvRead = now;
                // Another inventory tab open? Compare the tab strip with the core tab's.
                double tabDist = coreTab == null ? 0 : FarmCheck.Dist(coreTab, FarmCheck.TabPrint(img, grid.Value));
                var anchored = FarmCheck.AnchorByButtons(img, grid.Value, out FarmCheck.AnchorResult ares, AnchorScale);
                bool anchorOk = ares.Ok; string anchorInfo = ares.Info;
                if (anchorInfo != lastAnchorInfo) { Debug("anchor: " + anchorInfo, "anchor"); lastAnchorInfo = anchorInfo; }
                ovAnchor = anchorInfo; ovAnchorOk = anchorOk; ovAnchorRes = ares; ovAnchorCap = cap;
                if (anchorOk && ares.ScaleSearched)
                {
                    // the button wasn't at the known scale: the UI size changed. New slot size and area at once; the
                    // next check reads the inventory there.
                    double oldPitch = KnownPitch > 0 ? KnownPitch : grid.Value.PitchX, newPitch = FarmCheck.RefPitch * ares.Scale;
                    buttonScale = ares.Scale;
                    if (Math.Abs(newPitch / oldPitch - 1) > 0.03)
                    {
                        Debug($"UI scale changed: slot {oldPitch:0.0} px -> {newPitch:0.0} px");
                        SetInventoryArea(new Grid { X = cap.X + anchored.X, Y = cap.Y + anchored.Y, PitchX = newPitch, PitchY = newPitch });
                        slotSize = newPitch; foundPitch = newPitch; locatedBy = "sword button";
                        grid = null; readGrid = null; lastKnownCounts.Clear(); slotIdentity.Clear(); invOpenSince = DateTime.MinValue; SaveConfig();
                        return;
                    }
                    SaveConfig();
                }
                if (!anchorOk) InventoryMissing(now, "button anchor not found in the inventory area");
                var (g, slots) = anchorOk
                    ? (anchored, FarmCheck.ReadInventory(img, anchored, icons, 8, 8, slotIdentity))   // pinned: no position search, sticky identities
                    : FarmCheck.BestRead(img, grid.Value, icons, keep: readGrid);
                if (anchorOk)
                {
                    foreach (var sl in slots) slotIdentity[(sl.Row, sl.Col)] = sl.Item;
                    foreach (var k in slotIdentity.Keys.ToList()) if (!slots.Any(x => x.Row == k.r && x.Col == k.c)) slotIdentity.Remove(k);   // now empty / unknown
                }
                FarmCheck.ForgetMissingCounts(slots, lastKnownCounts);                  // empty / other item: its last count no longer applies
                int found = slots.Select(x => x.Item).Distinct().Count();
                int allCores = icons.Select(i => i.name).Distinct().Count();
                ovInvCap = cap; ovGrid = g; ovInv = $"open, {found} core types";
                ovTab = coreTab == null ? "Tab: learning from this read" : tabDist <= FarmCheck.TabMatchMax ? $"Core tab ✓ ({tabDist:0.0})" : $"Other tab? ({tabDist:0.0})";
                ovTabColor = tabDist > FarmCheck.TabMatchMax ? Color.OrangeRed : Color.Orange;
                if (tabDist > FarmCheck.TabMatchMax)
                {
                    // Most of the cores recognised: this IS the core tab, the saved tab strip is stale. Re-learn it.
                    if (found >= Math.Max(5, allCores * 7 / 10))
                    { coreTab = FarmCheck.TabPrint(img, g); SaveConfig(); Debug($"tab strip looked different ({tabDist:0.0}) but {found} cores were recognised: core tab re-learned"); }
                    else
                    {
                        Debug($"skipped: another inventory tab is open (tab strip differs {tabDist:0.0}, limit {FarmCheck.TabMatchMax}; {found} core types recognised)", "othertab");
                        waitReason = "another inventory tab seems to be open - switch to the core tab";
                        ovSlots = slots;
                        return;
                    }
                }
                int expectedFound = baseline != null ? baseline.Count(kv => kv.Value > 0) : Math.Min(5, icons.Count / 2);
                if (found == 0 || found * 2 < expectedFound)
                {
                    ovSlots = slots; SaveAnnotated(bmp, g, slots, top);
                    Debug($"skipped: only {found} core types recognised (need {Math.Max(1, (expectedFound + 1) / 2)}) - wrong tab or icons need re-learning", "few:" + found);
                    waitReason = $"only {found} core types recognised - core tab open? (else Areas & learning > Learn core icons)";
                    if (found == 0)
                    {
                        // Nothing recognised: the grid may be the wrong one (e.g. a lookalike pattern). Search again next time,
                        // and look for the inventory's title on screen while waiting for counts.
                        grid = null; readGrid = null; lastKnownCounts.Clear();
                        if (baseline == null || finishing) TryLocateInventory(now);
                    }
                    return;
                }
                waitReason = null;
                if (anchorOk) locatedSinceMissing = false;                             // found where it should be: search at once next time
                if (slotSize == null || Math.Abs(slotSize.Value - g.PitchX) > 0.5) { slotSize = g.PitchX; SaveConfig(); Debug($"slot size {g.PitchX:0.0} px saved (read recognised {found} core types)"); }
                if (coreTab == null) { coreTab = FarmCheck.TabPrint(img, g); SaveConfig(); Debug("core tab remembered from this read"); }
                Debug("read: " + string.Join(", ", slots.Select(x => $"{Short(x.Item)}@{x.Row},{x.Col} ({x.GlyphCount} glyphs, icon {x.IconDist:0.00})")), "read:" + string.Join(",", slots.Select(x => x.Item + x.GlyphCount)));
                if (readGrid == null || Math.Abs(readGrid.Value.X - g.X) > 0.5 || Math.Abs(readGrid.Value.Y - g.Y) > 0.5)
                    Debug($"read grid {(readGrid == null ? "set" : "moved")} to {g.X:0},{g.Y:0} (slot {g.PitchX:0.0} px)");
                readGrid = g;
                // Counts come only from the text reader, on the raw pixels of each slot's count region.
                if (busyInv) return;
                busyInv = true;
                var copy = (Bitmap)bmp.Clone(); var frameNow = (Bitmap)bmp.Clone(); var gridNow = g; var slotsNow = slots;
                BeginInvoke((Action)(async () =>
                {
                    int?[] ocr = null;
                    try
                    {
                        // Generic count reading: the raw pixels of each slot's count region (found by glyph size and
                        // position only, no font shapes) go to the text reader; the count is the trailing run of digits.
                        // Tesseract (OCR package) reads the digits-isolated picture, the raw one only if that gives nothing; without the
                        // package, Windows' text reader reads the raw region in its treatments.
                        var imgNow = PartyOcr.ToImg(copy); var strips = new List<Img>(); var glyphCounts = new List<int>(); var digits = new List<Img>();
                        foreach (var sl in slotsNow) { strips.Add(FarmCheck.CountRegionImage(imgNow, gridNow, sl.Row, sl.Col, out int n, out Img dg)); glyphCounts.Add(n); digits.Add(dg); }
                        ocr = await PartyOcr.ReadCountStripsAsync(strips, glyphCounts, slotsNow.Select(x => Short(x.Item)).ToList(), digits);
                        LogCountEngine();
                    }
                    catch (Exception ex) { Debug("text reader failed on the counts: " + ex.Message, "ocrfail"); }
                    finally { copy.Dispose(); }
                    try
                    {
                        var byReader = new HashSet<SlotRead>();
                        var sure = PartyOcr.LastCountConfident();                         // false: only the raw picture read it, or B doesn't match its digits
                        var unsure = new HashSet<SlotRead>();
                        for (int i = 0; i < slotsNow.Count; i++)
                        {
                            int? o = ocr != null && i < ocr.Length ? ocr[i] : null;
                            if (o.HasValue) { slotsNow[i].Count = o; byReader.Add(slotsNow[i]); if (i < sure.Length && !sure[i]) unsure.Add(slotsNow[i]); }
                            else if (slotsNow[i].GlyphCount == 0) slotsNow[i].Count = 1;          // no count drawn: a single item
                        }
                        var unread = slotsNow.Where(x => !x.Count.HasValue).Select(x => Short(x.Item)).ToList();
                        if (ocr != null) Debug($"counts by text reader: {byReader.Count}/{slotsNow.Count} read ({PartyOcr.LastCountSourceSummary()})" + (unread.Count > 0 ? "; unreadable: " + string.Join(", ", unread) : ""), "ocr:" + byReader.Count + string.Join("", unread));
                        // Keep the strips of reads where a count changed since the last read (last 5).
                        bool changed = false;
                        foreach (var sl in slotsNow)
                            if (sl.Count.HasValue) { if (lastCounts.TryGetValue(sl.Item, out int prev) && prev != sl.Count.Value) changed = true; lastCounts[sl.Item] = sl.Count.Value; }
                        if (changed)
                        {
                            stripSaves = (stripSaves + 1) % 5;
                            try { PartyOcr.SaveLastStrip(Path.Combine(Dir, $"cabal-helper-farm-digits-{stripSaves}.png")); }
                            catch (Exception ex) { Debug($"count strip picture failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}", "stripfail"); }
                            try { frameNow.Save(Path.Combine(Dir, $"cabal-helper-farm-frame-{stripSaves}.png"), System.Drawing.Imaging.ImageFormat.Png); } catch { }
                            Debug($"count strip saved as cabal-helper-farm-digits-{stripSaves}.png (a count changed): " + string.Join(", ", slotsNow.Select(x => Short(x.Item) + "=" + (x.Count?.ToString() ?? "?"))));
                        }
                        SaveAnnotated(frameNow, gridNow, slotsNow, top);
                        var counts = new Dictionary<string, int>();
                        foreach (var sl in slotsNow)
                        {
                            if (!sl.Count.HasValue) continue;
                            counts[sl.Item] = counts.TryGetValue(sl.Item, out int c) ? c + sl.Count.Value : sl.Count.Value;
                        }
                        // confident = every stack of the core was read by the text reader this time: shown at once
                        // (a count Tesseract's two pictures disagreed on is not confident: it goes through the majority)
                        var confident = new HashSet<string>(counts.Keys.Where(name => slotsNow.Where(x => x.Item == name).All(x => byReader.Contains(x) && !unsure.Contains(x))));
                        if (unread.Count > 0) Warn("Some counts unreadable by the text reader: see Areas & learning > Diagnostics: last count strip");
                        smoother.Add(counts, confident); readsSinceStart++;
                        ApplyRead(smoother.Stable(), smoother.Gone());
                        FarmCheck.RecordCounts(slotsNow, lastKnownCounts);
                        ovSlots = slotsNow; Render(); DrawOverlay();
                    }
                    finally { busyInv = false; frameNow.Dispose(); }
                }));
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
                        if (fresh.Count > 0) ovLoot = "new: " + fresh.Last();
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
        void ApplyRead(Dictionary<string, int> counts, List<string> goneNow = null)
        {
            var missing = FarmCheck.MissingToConfirm(icons.Select(i => i.name), counts, baseline, confirmedEmpty);
            // At the start every never-seen core is a candidate; later only cores absent for several reads in a row.
            if (baseline != null) missing = missing.Where(n => goneNow != null && goneNow.Contains(n)).ToList();
            else if (readsSinceStart < smoother.MissingAfter) missing.Clear();     // give the first reads a chance to see everything
            if (missing.Count == 0 && pending != null) { pending = null; Debug("missing-core question cleared: the cores were seen again"); }
            if (missing.Count > 0)
            {
                if (pending != null && pending.Value.names.SequenceEqual(missing)) { pending = (missing, counts, pending.Value.start); return; }
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
        async void LearnIcons()
        {
            using (var bmp = PartyOcr.Capture(InvCapture))
            {
                var img = PartyOcr.ToImg(bmp);
                var g = FarmCheck.FindGridNear(img, 0, invArea.Y - InvCapture.Y, img.W, invArea.Height, KnownPitch);
                if (!FarmCheck.InventoryOpen(img, g))
                {
                    Debug($"learn icons: inventory not in its area (grid contrast {g.Score:0.00}), searching all screens");
                    lastLocate = DateTime.Now;
                    if (await LocateInventoryAsync())
                    { MessageBox.Show("Found the inventory at a new spot. Press Learn core icons once more.", "Farm Tracker"); return; }
                    Debug("learn icons: inventory not found");
                    MessageBox.Show("Open the inventory on the core tab first (and check Set inventory area).", "Farm Tracker"); return;
                }
                g = FarmCheck.BestRead(img, g, icons).grid;
                // same layout as taught: top row Upgrade Cores highest -> lowest, bottom row Force Cores
                var learned = new List<(string, double[])>();
                var emptySlots = new List<string>();
                for (int i = 0; i < FarmCheck.Icons.Length; i++)
                {
                    var f = FarmCheck.IconFeature(img, g, i / 5, i % 5);
                    if (FarmCheck.Dist(f, FarmCheck.EmptySlot) < 1.0) emptySlots.Add(FarmCheck.Icons[i].name); else learned.Add((FarmCheck.Icons[i].name, f));
                }
                // Check the new icons before keeping them: every learned core must be found again, in its own slot.
                var verify = FarmCheck.ReadInventory(img, g, learned);
                bool ok = learned.Count >= 5 && learned.All(l => verify.Any(v => v.Item == l.Item1));
                SaveAnnotated(bmp, g, verify, invArea.Y - InvCapture.Y);
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
            if (sheetBusy) st = "Building OCR test sheet… (the text reader reads every count 5 ways)";
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
            int want = 26 /*title*/ + Content.Padding.Vertical + status.Height + startStop.Height + 6 + Ui.LinesHeight(body) + footer.Height + saveImage.Height + 8
                     + (confirmBox.Visible ? confirmBox.PreferredSize.Height : 0);
            int max = Screen.FromControl(this).WorkingArea.Height - 40;
            int h = Math.Max(150, Math.Min(max, want));
            if (Height != h) Height = h;
        }

        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
