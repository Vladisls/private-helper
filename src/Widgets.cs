using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CAHelper
{
    /// Always-on-top window you can click and drag, but which never takes focus from the game,
    /// so your keyboard stays in Cabal after pressing a button.
    class FloatingForm : Form
    {
        public event Action Moved;
        Point dragStart; bool dragging, dragMoved;

        public FloatingForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.Normal;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }
        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        /// Borderless-windowed games can push themselves on top; re-assert without stealing focus.
        public void KeepOnTop()
        {
            if (Visible) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                             Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// Makes a control drag the whole window. Returns true from IsClick() if the press didn't move.
        protected void MakeDraggable(Control c)
        {
            c.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragging = true; dragMoved = false; dragStart = Cursor.Position; } };
            c.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var p = Cursor.Position;
                int dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
                if (!dragMoved && Math.Abs(dx) + Math.Abs(dy) < 4) return;
                dragMoved = true;
                Location = new Point(Location.X + dx, Location.Y + dy);
                dragStart = p;
            };
            c.MouseUp += (s, e) => { if (dragging && dragMoved) Moved?.Invoke(); dragging = false; };
        }
        protected bool LastPressWasDrag => dragMoved;

        public void PlaceSafely(Point? saved, Point fallback)
        {
            var p = saved ?? fallback;
            bool onScreen = false;
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.IntersectsWith(new Rectangle(p, Size))) { onScreen = true; break; }
            Location = onScreen ? p : fallback;
        }
    }

    static class Theme
    {
        public static readonly Color Panel = Color.FromArgb(26, 31, 38), Line = Color.FromArgb(42, 49, 59),
            Text = Color.FromArgb(230, 233, 238), Muted = Color.FromArgb(154, 164, 178), Accent = Color.FromArgb(90, 169, 255),
            Good = Color.FromArgb(79, 209, 139), Bad = Color.FromArgb(255, 107, 107), Button = Color.FromArgb(35, 42, 52),
            AlertDark = Color.FromArgb(70, 0, 0), AlertBright = Color.FromArgb(190, 0, 0);
        public static readonly Font Normal = new Font("Segoe UI", 9f), Small = new Font("Segoe UI", 8f),
            Title = new Font("Segoe UI", 9f, FontStyle.Bold), Big = new Font("Segoe UI", 24f, FontStyle.Bold);

        public static Button MakeButton(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, Height = 28, Cursor = Cursors.Hand, TabStop = false,
                BackColor = primary ? Accent : Button, ForeColor = primary ? Color.FromArgb(6, 18, 31) : Text,
                Font = primary ? Title : Normal
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Line;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(120, 185, 255) : Color.FromArgb(48, 57, 70);
            return b;
        }
    }

    /// The floating "+" that opens the helper picker. Drag it anywhere.
    sealed class LauncherForm : FloatingForm
    {
        public event Action<Point> Clicked;
        bool hover;

        public LauncherForm()
        {
            Size = new Size(40, 40);
            Opacity = 0.9;
            using (var path = new GraphicsPath()) { path.AddEllipse(0, 0, 40, 40); Region = new Region(path); }
            MakeDraggable(this);
            MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && !LastPressWasDrag))
                    Clicked?.Invoke(PointToScreen(new Point(0, Height)));
            };
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Panel);
            using (var b = new SolidBrush(hover ? Color.FromArgb(120, 185, 255) : Theme.Accent)) g.FillEllipse(b, 0, 0, 39, 39);
            using (var p = new Pen(Color.FromArgb(6, 18, 31), 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            { g.DrawLine(p, 20, 11, 20, 29); g.DrawLine(p, 11, 20, 29, 20); }
        }
    }

    /// Base for helper panels: title bar you can drag, a close button, content below.
    abstract class HelperWidget : FloatingForm
    {
        public abstract string HelperName { get; }
        public event Action<HelperWidget> CloseRequested, Touched;
        protected readonly Panel Content = new Panel { Dock = DockStyle.Fill };
        readonly Label title;

        protected HelperWidget(int width, int height)
        {
            Size = new Size(width, height);
            Padding = new Padding(1);            // 1px border drawn in OnPaint
            var bar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Theme.Line };
            title = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Title, Padding = new Padding(6, 0, 0, 0) };
            var close = new Label { Dock = DockStyle.Right, Width = 26, Text = "✕", TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, ForeColor = Theme.Muted };
            close.Click += (s, e) => CloseRequested?.Invoke(this);
            close.MouseEnter += (s, e) => close.ForeColor = Theme.Bad;
            close.MouseLeave += (s, e) => close.ForeColor = Theme.Muted;
            bar.Controls.Add(title); bar.Controls.Add(close);
            Content.Padding = new Padding(10, 8, 10, 8);
            Controls.Add(Content); Controls.Add(bar);
            MakeDraggable(bar); MakeDraggable(title);
            Load += (s, e) => title.Text = HelperName;
        }

        protected void Touch() => Touched?.Invoke(this);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        /// Called ~5x per second. Return what the widget wants from the global flash/beep.
        public abstract AlertState Tick(DateTime now, TimeSpan idle, bool gameFocused, Settings s, bool blinkOn);
        public virtual bool SupportsHotkeys => true;
        public virtual void OnSettingsChanged() { }
        public virtual void HotkeyStart() { }
        public virtual void HotkeyReset() { }
        public virtual string FlashTitle(AlertState st) => "";
        public virtual string FlashSub(AlertState st) => "";
    }

    /// Two-channel Chaos Arena: Start at the channel switch, boss alert at 6:00, re-entry window 9:00.
    sealed class CaRunnerWidget : HelperWidget
    {
        public override string HelperName => "CA Runner";
        readonly AlertTracker tracker = new AlertTracker();
        readonly Label status = new Label { Dock = DockStyle.Top, Height = 18, Font = Theme.Small, ForeColor = Theme.Muted };
        readonly Label time = new Label { Dock = DockStyle.Top, Height = 44, Font = Theme.Big, TextAlign = ContentAlignment.MiddleLeft };
        readonly Button start = Theme.MakeButton("Start", primary: true), reset = Theme.MakeButton("Reset");
        readonly Label hint = new Label { Dock = DockStyle.Bottom, Height = 16, Font = Theme.Small, ForeColor = Theme.Muted };
        DateTime? startedAt;
        public bool Running => startedAt.HasValue;

        public CaRunnerWidget() : base(230, 160)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 32, ColumnCount = 2, Padding = new Padding(0, 2, 0, 0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            start.Dock = DockStyle.Fill; reset.Dock = DockStyle.Fill;
            start.Margin = new Padding(0, 0, 4, 0); reset.Margin = new Padding(4, 0, 0, 0);
            row.Controls.Add(start, 0, 0); row.Controls.Add(reset, 1, 0);
            Content.Controls.Add(hint); Content.Controls.Add(row); Content.Controls.Add(time); Content.Controls.Add(status);
            start.Click += (s, e) => { DoStart(); Touch(); };
            reset.Click += (s, e) => { DoReset(); Touch(); };
            foreach (Control c in new Control[] { Content, status, time, hint }) c.MouseDown += (s, e) => Touch();
            MakeDraggable(status); MakeDraggable(time);
        }

        public void SetHint(Settings s) => hint.Text = $"{s.RestartHotkey} start · {s.StopHotkey} reset";
        void DoStart() { startedAt = DateTime.Now; tracker.Reset(); if (Program.CurrentSettings.Sound) System.Media.SystemSounds.Asterisk.Play(); }
        void DoReset() { startedAt = null; tracker.Reset(); }
        public override void HotkeyStart() => DoStart();
        public override void HotkeyReset() => DoReset();

        public override AlertState Tick(DateTime now, TimeSpan idle, bool gameFocused, Settings s, bool blinkOn)
        {
            TimeSpan? elapsed = startedAt.HasValue ? now - startedAt.Value : (TimeSpan?)null;
            var st = tracker.Evaluate(elapsed, idle, gameFocused, s, now);
            Color back = Theme.Panel, fore = Theme.Text;
            switch (st.Phase)
            {
                case Phase.Off:
                    status.Text = "Press Start at the channel switch";
                    time.Text = AlertTracker.Format(s.BossSpawnAfter); fore = Theme.Muted; break;
                case Phase.Counting:
                    status.Text = "2nd CA boss spawns in";
                    time.Text = AlertTracker.Format(st.ToBoss); break;
                case Phase.BossUp:
                    status.Text = st.LastCall ? "LAST CALL · back to 1st CA" : "BOSS UP · back to 1st CA in";
                    time.Text = AlertTracker.Format(st.Left);
                    back = blinkOn ? Theme.AlertBright : Theme.AlertDark; break;
                case Phase.Expired:
                    status.Text = "1st CA re-entry closed";
                    time.Text = "LATE " + AlertTracker.Format(st.Left); back = Theme.AlertDark; break;
            }
            status.ForeColor = st.Phase == Phase.BossUp || st.Phase == Phase.Expired ? Color.White : Theme.Muted;
            if (Content.BackColor != back) Content.BackColor = back;
            if (time.ForeColor != fore) time.ForeColor = fore;
            start.Text = Running ? "Restart" : "Start";
            return st;
        }

        public override string FlashTitle(AlertState st) => st.Phase == Phase.Expired ? "TOO LATE" : "BOSS UP  " + AlertTracker.Format(st.Left);
        public override string FlashSub(AlertState st) => st.Phase == Phase.Expired ? "1st CA re-entry window closed"
            : st.LastCall ? "LAST CALL: change channel and re-enter the 1st CA" : "Kill the boss, then go back to the 1st CA";
    }

    /// Full-screen red flash. Click-through, never takes focus.
    sealed class FlashForm : OverlayForm
    {
        public string Text1 = "", Sub = "";
        readonly Font font = new Font("Segoe UI", 64f, FontStyle.Bold), sub = new Font("Segoe UI", 22f);
        public FlashForm() { BackColor = Color.FromArgb(200, 0, 0); Bounds = SystemInformation.VirtualScreen; Opacity = 0.4; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var screen = Screen.PrimaryScreen.Bounds;
            var r = new RectangleF(screen.Left - Left, screen.Top - Top, screen.Width, screen.Height);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Text1, font, Brushes.White, r, fmt);
            g.DrawString(Sub, sub, Brushes.White, new RectangleF(r.X, r.Y + 110, r.Width, r.Height), fmt);
        }
    }
}
