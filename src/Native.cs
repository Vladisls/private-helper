using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CAHelper
{
    /// Only passive Windows APIs: a registered hotkey (like Discord/OBS use) and the system idle counter.
    /// Nothing reads, hooks or sends input to the game.
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                         WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int max);

        /// True if the window in front has `match` in its process name or title. Reads only the window's name.
        public static bool ForegroundMatches(string match)
        {
            if (string.IsNullOrWhiteSpace(match)) return true;
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.ToString().IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            try
            {
                GetWindowThreadProcessId(h, out uint pid);
                using (var p = System.Diagnostics.Process.GetProcessById((int)pid))
                    return p.ProcessName.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static TimeSpan IdleTime()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            uint ms = unchecked((uint)Environment.TickCount - info.dwTime);
            return TimeSpan.FromMilliseconds(ms);
        }
    }

    sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        public event Action<int> Pressed;
        readonly System.Collections.Generic.List<int> ids = new System.Collections.Generic.List<int>();

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(int id, string combo, out string error)
        {
            error = null;
            if (!TryParse(combo, out uint mods, out uint vk)) { error = $"'{combo}' is not a valid hotkey"; return false; }
            if (!Native.RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, vk)) { error = $"'{combo}' is already used by another program"; return false; }
            ids.Add(id);
            return true;
        }

        public void UnregisterAll() { foreach (var id in ids) Native.UnregisterHotKey(Handle, id); ids.Clear(); }

        public static bool TryParse(string combo, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(combo)) return false;
            foreach (var part0 in combo.Split('+'))
            {
                var part = part0.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= MOD_CONTROL; continue;
                    case "alt": mods |= MOD_ALT; continue;
                    case "shift": mods |= MOD_SHIFT; continue;
                    case "win": mods |= MOD_WIN; continue;
                }
                if (vk != 0) return false;                     // two non-modifier keys
                if (part.Length == 1 && char.IsDigit(part[0])) part = "D" + part;
                if (!Enum.TryParse(part, true, out Keys k) || k == Keys.None) return false;
                vk = (uint)k;
            }
            return vk != 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) Pressed?.Invoke(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose() { UnregisterAll(); DestroyHandle(); }
    }

    /// Borderless, always-on-top window that never takes focus and lets every click pass through to the game.
    class OverlayForm : Form
    {
        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                            | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }
        /// Borderless-windowed games can push themselves on top; re-assert without stealing focus.
        public void KeepOnTop()
        {
            if (Visible) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                             Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        public void ShowQuiet()
        {
            if (Visible) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            Visible = true;
        }
    }
}
