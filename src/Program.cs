using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows.Forms;

namespace CAHelper
{
    static class Program
    {
        public static Settings CurrentSettings = new Settings();

        public static string StartupMessage;

        [STAThread]
        static void Main(string[] args)
        {
            bool justUpdated = Array.IndexOf(args, "--updated") >= 0;
            bool restarted = Array.IndexOf(args, "--restart") >= 0;
            using (var mutex = new Mutex(true, "CabalHelper_SingleInstance", out bool first))
            {
                // After an update the old process may still be closing: wait for it instead of refusing.
                if (!first && !((justUpdated || restarted) && WaitFor(mutex)))
                { MessageBox.Show("Cabal Helper is already running. Look for it in the tray.", "Cabal Helper"); return; }

                Updater.CleanupOld();
                var settings = Settings.Load(out _);
                if (justUpdated) StartupMessage = "Updated to v" + Updater.Short(Updater.Current);
                else if (settings.AutoUpdate)
                {
                    var r = Updater.CheckAndApply(settings.UpdateUrl, 4000, out string msg);
                    if (r == Updater.Result.Updated) { mutex.ReleaseMutex(); Updater.Restart(); return; }
                    if (r == Updater.Result.Failed) StartupMessage = msg;   // offline etc.: start normally, say why
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new HelperContext());
            }
        }

        static bool WaitFor(Mutex m)
        {
            try { return m.WaitOne(15000); }
            catch (AbandonedMutexException) { return true; }
        }
    }

    /// Registry of available helpers. Add a line here to add a new helper to the "+" menu.
    static class HelperCatalog
    {
        public static readonly (string Key, string Name, string Description, Func<HelperWidget> Create)[] All =
        {
            ("CARunner", "CA Runner", "Two-channel Chaos Arena timer", () => new CaRunnerWidget()),
            ("Todo", "Daily To-do", "Daily and weekly checklist with counters", () => new TodoWidget()),
        };
    }

    sealed class HelperContext : ApplicationContext
    {
        const int HK_START = 1, HK_RESET = 2;

        Settings settings;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly HotkeyWindow hotkeys = new HotkeyWindow();
        readonly LauncherForm launcher = new LauncherForm();
        readonly FlashForm flash = new FlashForm();
        readonly List<HelperWidget> widgets = new List<HelperWidget>();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 200 };
        readonly Dictionary<string, string> layout = Layout.Load();
        HelperWidget lastTouched;
        ToolStripMenuItem launcherItem;
        DateTime testFlashUntil = DateTime.MinValue;
        bool blinkOn; int blinkCounter;

        public HelperContext()
        {
            tray.Icon = MakeIcon();
            tray.Text = "Cabal Helper";
            tray.Visible = true;
            tray.ContextMenuStrip = BuildTrayMenu();
            tray.DoubleClick += (s, e) => SetLauncherVisible(true);

            hotkeys.Pressed += OnHotkey;
            ApplySettings(showSummary: true);

            var wa = Screen.PrimaryScreen.WorkingArea;
            launcher.PlaceSafely(Layout.GetPoint(layout, "Launcher"), new Point(wa.Right - 60, wa.Top + 140));
            launcher.Moved += () => { layout["Launcher"] = Layout.Pt(launcher.Location); Layout.Save(layout); };
            launcher.Clicked += p => BuildPickerMenu().Show(p);
            bool launcherVisible = !layout.TryGetValue("LauncherVisible", out var lv) || lv != "0";
            SetLauncherVisible(launcherVisible);

            // Reopen helpers that were open last time.
            foreach (var h in HelperCatalog.All)
                if (layout.TryGetValue(h.Key + ".Open", out var open) && open == "1") OpenHelper(h.Key);

            flash.Show(); flash.Hide();   // create the handle once so later shows never steal focus
            tick.Tick += (s, e) => OnTick();
            tick.Start();
        }

        ContextMenuStrip BuildTrayMenu()
        {
            var m = new ContextMenuStrip();
            var helpers = new ToolStripMenuItem("Open helper");
            foreach (var h in HelperCatalog.All) { var key = h.Key; helpers.DropDownItems.Add(h.Name, null, (s, e) => OpenHelper(key)); }
            m.Items.Add(helpers);
            launcherItem = new ToolStripMenuItem("Show \"+\" button", null, (s, e) => SetLauncherVisible(!launcher.Visible)) { CheckOnClick = false };
            m.Items.Add(launcherItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Test flash (5 seconds)", null, (s, e) => testFlashUntil = DateTime.Now.AddSeconds(5));
            m.Items.Add("Settings…", null, (s, e) => OpenSettings());
            m.Items.Add("Edit settings file", null, (s, e) =>
            {
                try { System.Diagnostics.Process.Start("notepad.exe", "\"" + Settings.IniPath + "\""); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Cabal Helper"); }
            });
            m.Items.Add("Reload settings", null, (s, e) => ApplySettings(showSummary: true));
            m.Items.Add("Check for updates", null, (s, e) => CheckForUpdatesNow());
            m.Items.Add(new ToolStripLabel("Cabal Helper v" + Updater.Short(Updater.Current)) { ForeColor = Color.Gray });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Restart", null, (s, e) => RestartHelper());
            m.Items.Add("Exit", null, (s, e) => ExitThread());
            return m;
        }

        ContextMenuStrip BuildPickerMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add(new ToolStripLabel("Add a helper") { ForeColor = Color.Gray });
            foreach (var h in HelperCatalog.All)
            {
                var key = h.Key;
                bool open = widgets.Any(w => w.HelperName == h.Name);
                m.Items.Add(new ToolStripMenuItem(h.Name + (open ? "  (open)" : ""), null, (s, e) => OpenHelper(key)) { ToolTipText = h.Description });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Settings…", null, (s, e) => OpenSettings());
            m.Items.Add("Hide \"+\" button (tray icon brings it back)", null, (s, e) => SetLauncherVisible(false));
            m.Items.Add("Restart helper", null, (s, e) => RestartHelper());
            return m;
        }

        /// Closes this copy and starts a fresh one (which also checks GitHub for an update).
        void RestartHelper()
        {
            if (widgets.OfType<CaRunnerWidget>().Any(w => w.Running) &&
                MessageBox.Show("A CA Runner countdown is running and will be lost. Restart anyway?", "Cabal Helper",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try { Updater.Launch("--restart"); }
            catch (Exception ex) { MessageBox.Show("Could not restart: " + ex.Message, "Cabal Helper"); return; }
            ExitThread();
        }

        SettingsForm settingsForm;
        void OpenSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
            settingsForm = new SettingsForm(settings, Todo.LoadCp());
            settingsForm.Saved += () => { ApplySettings(showSummary: false); foreach (var w in widgets) w.OnSettingsChanged(); };
            settingsForm.Show();
        }

        void CheckForUpdatesNow()
        {
            if (widgets.OfType<CaRunnerWidget>().Any(w => w.Running))
            { tray.ShowBalloonTip(5000, "Cabal Helper", "Finish or reset the CA Runner first, the update restarts the helper.", ToolTipIcon.Info); return; }
            var r = Updater.CheckAndApply(settings.UpdateUrl, 8000, out string msg);
            if (r == Updater.Result.Updated) { tray.Visible = false; Updater.Restart(); ExitThread(); return; }
            tray.ShowBalloonTip(5000, "Cabal Helper", msg, r == Updater.Result.Failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        void SetLauncherVisible(bool v)
        {
            if (v) { launcher.Show(); launcher.KeepOnTop(); } else launcher.Hide();
            if (launcherItem != null) launcherItem.Checked = v;
            layout["LauncherVisible"] = v ? "1" : "0"; Layout.Save(layout);
        }

        HelperWidget OpenHelper(string key)
        {
            var def = HelperCatalog.All.First(h => h.Key == key);
            var existing = widgets.FirstOrDefault(w => w.HelperName == def.Name);
            if (existing != null) { existing.KeepOnTop(); lastTouched = existing; return existing; }

            var w = def.Create();
            var wa = Screen.PrimaryScreen.WorkingArea;
            w.PlaceSafely(Layout.GetPoint(layout, key), new Point(wa.Left + (wa.Width - w.Width) / 2, wa.Top + 12));
            w.Moved += () => { layout[key] = Layout.Pt(w.Location); Layout.Save(layout); };
            w.Touched += x => lastTouched = x;
            w.CloseRequested += x => CloseHelper(key, x);
            if (w is CaRunnerWidget ca) ca.SetHint(settings);
            widgets.Add(w);
            w.Show(); w.KeepOnTop();
            lastTouched = w;
            layout[key + ".Open"] = "1"; Layout.Save(layout);
            return w;
        }

        void CloseHelper(string key, HelperWidget w)
        {
            widgets.Remove(w);
            if (lastTouched == w) lastTouched = widgets.LastOrDefault();
            w.Close(); w.Dispose();
            layout[key + ".Open"] = "0"; Layout.Save(layout);
        }

        /// Hotkeys go to the helper you used last; if no CA Runner is open, Start opens one.
        void OnHotkey(int id)
        {
            var target = lastTouched != null && lastTouched.SupportsHotkeys ? lastTouched
                       : widgets.LastOrDefault(w => w.SupportsHotkeys);
            if (target == null && id == HK_START) target = OpenHelper("CARunner");
            if (target == null) return;
            if (id == HK_START) target.HotkeyStart(); else target.HotkeyReset();
            OnTick();
        }

        void ApplySettings(bool showSummary)
        {
            settings = Settings.Load(out List<string> problems);
            Program.CurrentSettings = settings;
            hotkeys.UnregisterAll();
            if (!hotkeys.Register(HK_START, settings.RestartHotkey, out string e1)) problems.Add("Start hotkey: " + e1);
            if (!hotkeys.Register(HK_RESET, settings.StopHotkey, out string e2)) problems.Add("Reset hotkey: " + e2);
            foreach (var w in widgets) if (w is CaRunnerWidget ca) ca.SetHint(settings);

            if (Program.StartupMessage != null) { problems.Insert(0, Program.StartupMessage); Program.StartupMessage = null; showSummary = true; }
            if (problems.Count > 0)
                tray.ShowBalloonTip(8000, "Cabal Helper v" + Updater.Short(Updater.Current), string.Join("\n", problems), ToolTipIcon.Info);
            else if (showSummary)
                tray.ShowBalloonTip(5000, "Cabal Helper is running",
                    $"Click the blue + to add a helper. {settings.RestartHotkey} starts, {settings.StopHotkey} resets.", ToolTipIcon.Info);
        }

        void OnTick()
        {
            var now = DateTime.Now;
            if (++blinkCounter >= 2) { blinkCounter = 0; blinkOn = !blinkOn; }   // ~400 ms blink

            var idle = Native.IdleTime();
            bool anyRunning = widgets.OfType<CaRunnerWidget>().Any(w => w.Running);
            bool focused = !anyRunning || Native.ForegroundMatches(settings.GameMatch);

            HelperWidget flashFrom = null; AlertState flashState = default; bool beep = false;
            foreach (var w in widgets)
            {
                var st = w.Tick(now, idle, focused, settings, blinkOn);
                if (st.Beep) beep = true;
                if (st.Flash && (flashFrom == null || st.Left < flashState.Left)) { flashFrom = w; flashState = st; }
                w.KeepOnTop();
            }
            if (launcher.Visible) launcher.KeepOnTop();

            bool testing = now < testFlashUntil;
            if (flashFrom != null || testing)
            {
                flash.Text1 = flashFrom != null ? flashFrom.FlashTitle(flashState) : "TEST";
                flash.Sub = flashFrom != null ? flashFrom.FlashSub(flashState) : "This is how the alert looks over the game";
                flash.Opacity = blinkOn ? 0.45 : 0.12;
                flash.ShowQuiet(); flash.KeepOnTop(); flash.Invalidate();
            }
            else if (flash.Visible) flash.Hide();

            if (beep) SystemSounds.Exclamation.Play();
        }

        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillEllipse(new SolidBrush(Theme.Accent), 1, 1, 30, 30);
                using (var p = new Pen(Color.FromArgb(6, 18, 31), 4f)) { g.DrawLine(p, 16, 8, 16, 24); g.DrawLine(p, 8, 16, 24, 16); }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        protected override void ExitThreadCore()
        {
            tick.Stop();
            hotkeys.Dispose();
            tray.Visible = false; tray.Dispose();
            foreach (var w in widgets.ToArray()) w.Close();
            launcher.Close(); flash.Close();
            base.ExitThreadCore();
        }
    }
}
