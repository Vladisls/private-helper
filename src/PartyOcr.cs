using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CAHelper
{
    /// Lets the capture code use real screen pixels even when Windows display scaling is 125%/150%.
    /// Only the area picker and the capture run DPI-aware; the rest of the helper stays as it is.
    sealed class DpiAware : IDisposable
    {
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        static readonly IntPtr PerMonitorV2 = new IntPtr(-4), PerMonitor = new IntPtr(-3);
        readonly IntPtr old; readonly bool active;
        public DpiAware()
        {
            try
            {
                old = SetThreadDpiAwarenessContext(PerMonitorV2);
                if (old == IntPtr.Zero) old = SetThreadDpiAwarenessContext(PerMonitor);
                active = old != IntPtr.Zero;
            }
            catch (EntryPointNotFoundException) { }      // Windows older than 10 1607: nothing to do
        }
        public void Dispose() { if (active) try { SetThreadDpiAwarenessContext(old); } catch { } }
    }

    /// Screenshot of one screen area + Windows' built-in text reader (Windows.Media.Ocr). Offline, no key.
    static class PartyOcr
    {
        public static string LastCapturePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-party-last.png");

        public static Bitmap Capture(Rectangle area)
        {
            using (new DpiAware())
            {
                var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                return bmp;
            }
        }

        /// Doubling the size made the macOS reader get every name right on the test screenshot.
        static Bitmap Enlarge(Bitmap src, int factor)
        {
            var big = new Bitmap(src.Width * factor, src.Height * factor, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, big.Width, big.Height);
            }
            return big;
        }

        public static async Task<(PartyRead read, string raw)> ReadAsync(Rectangle area)
        {
            byte[] png;
            using (var shot = Capture(area))
            using (var big = Enlarge(shot, 2))
            using (var ms = new MemoryStream())
            {
                big.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
                try { shot.Save(LastCapturePath, ImageFormat.Png); } catch { }   // for "Show last capture"
            }

            var engine = CreateEngine();
            if (engine == null)
                throw new InvalidOperationException("Windows has no text-recognition language installed. Settings > Time & language > Language: add English.");

            using (var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                await ras.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(png));
                ras.Seek(0);
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
                using (var sb = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied))
                {
                    var result = await engine.RecognizeAsync(sb);
                    var lines = new List<IList<OcrWord>>();
                    foreach (var line in result.Lines)
                    {
                        var words = new List<OcrWord>();
                        foreach (var w in line.Words)
                        {
                            var b = w.BoundingRect;
                            words.Add(new OcrWord(w.Text, b.X, b.Y, b.Width, b.Height));
                        }
                        lines.Add(words);
                    }
                    return (PartyCheck.Extract(lines), result.Text);
                }
            }
        }

        static Windows.Media.Ocr.OcrEngine CreateEngine()
        {
            try
            {
                var en = new Windows.Globalization.Language("en-US");
                if (Windows.Media.Ocr.OcrEngine.IsLanguageSupported(en)) return Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(en);
            }
            catch { }
            return Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        }
    }

    /// Full-screen "drag a box" picker over a frozen screenshot. Esc cancels.
    sealed class AreaPicker : Form
    {
        public Rectangle Picked { get; private set; }
        readonly Bitmap shot; readonly Rectangle virt;
        Point start; Rectangle sel; bool dragging;

        AreaPicker(Bitmap shot, Rectangle virt)
        {
            this.shot = shot; this.virt = virt;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = virt;
            TopMost = true; ShowInTaskbar = false; DoubleBuffered = true; Cursor = Cursors.Cross; KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragging = true; start = e.Location; sel = Rectangle.Empty; } };
            MouseMove += (s, e) => { if (dragging) { sel = Norm(start, e.Location); Invalidate(); } };
            MouseUp += (s, e) =>
            {
                if (!dragging) return; dragging = false;
                if (sel.Width < 20 || sel.Height < 20) { sel = Rectangle.Empty; Invalidate(); return; }
                Picked = new Rectangle(sel.X + virt.X, sel.Y + virt.Y, sel.Width, sel.Height);
                DialogResult = DialogResult.OK; Close();
            };
        }

        static Rectangle Norm(Point a, Point b) => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(shot, 0, 0);
            using (var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            using (var region = new Region(new Rectangle(0, 0, Width, Height)))
            {
                if (!sel.IsEmpty) region.Exclude(sel);
                g.FillRegion(dim, region);
            }
            if (!sel.IsEmpty) using (var p = new Pen(Theme.Accent, 2)) g.DrawRectangle(p, sel);
            var msg = "Drag a box around the party member list, including the \"member (19/25)\" line. Esc cancels.";
            using (var f = new Font("Segoe UI", 14f, FontStyle.Bold))
            {
                var size = g.MeasureString(msg, f);
                var r = new RectangleF((Width - size.Width) / 2 - 12, 40, size.Width + 24, size.Height + 12);
                using (var b = new SolidBrush(Color.FromArgb(220, 26, 31, 38))) g.FillRectangle(b, r);
                g.DrawString(msg, f, Brushes.White, r.X + 12, r.Y + 6);
            }
        }

        public static Rectangle? Pick()
        {
            using (new DpiAware())
            {
                var virt = SystemInformation.VirtualScreen;
                using (var shot = new Bitmap(virt.Width, virt.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(virt.Location, Point.Empty, virt.Size);
                    using (var f = new AreaPicker(shot, virt))
                        return f.ShowDialog() == DialogResult.OK ? f.Picked : (Rectangle?)null;
                }
            }
        }
    }
}
