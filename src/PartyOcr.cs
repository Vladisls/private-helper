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

        /// Keeps only white text and turns it into dark text on light: game world, red HP bars and
        /// grey class icons fade out. Tuned on a real GDG screenshot (all 16 names read correctly).
        static Bitmap Clean(Bitmap enlarged, double lo, double hi, double colorPenalty)
        {
            var bmp = new Bitmap(enlarged.Width, enlarged.Height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var sd = enlarged.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[sd.Stride * bmp.Height];
            Marshal.Copy(sd.Scan0, buf, 0, buf.Length);
            for (int i = 0; i + 3 < buf.Length; i += 4)
            {
                double b = buf[i], g = buf[i + 1], r = buf[i + 2];
                double mn = Math.Min(r, Math.Min(g, b)), mx = Math.Max(r, Math.Max(g, b));
                double v = (mn - colorPenalty * (mx - mn) - lo) / (hi - lo);
                v = v < 0 ? 0 : v > 1 ? 1 : v;
                byte o = (byte)(255 - v * 255);
                buf[i] = buf[i + 1] = buf[i + 2] = o; buf[i + 3] = 255;
            }
            Marshal.Copy(buf, 0, dd.Scan0, buf.Length);
            enlarged.UnlockBits(sd); bmp.UnlockBits(dd);
            return bmp;
        }

        static byte[] Png(Bitmap b) { using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }

        /// Reads the area several ways (two cleanups + plain) and returns every read; the caller picks the best.
        public static async Task<(List<PartyRead> reads, string raw)> ReadAllAsync(Rectangle area)
        {
            var engine = CreateEngine();
            if (engine == null)
                throw new InvalidOperationException("Windows has no text-recognition language installed. Settings > Time & language > Language: add English.");

            var images = new List<(string name, byte[] png)>();
            using (var shot = Capture(area))
            {
                try { shot.Save(LastCapturePath, ImageFormat.Png); } catch { }   // for "Open last capture image"
                using (var x3 = Enlarge(shot, 3))
                {
                    using (var c1 = Clean(x3, 120, 240, 1.5)) images.Add(("clean", Png(c1)));
                    using (var c2 = Clean(x3, 90, 230, 1.0)) images.Add(("clean-soft", Png(c2)));
                }
                using (var x2 = Enlarge(shot, 2)) images.Add(("plain", Png(x2)));
            }

            var reads = new List<PartyRead>();
            var raw = new System.Text.StringBuilder();
            foreach (var (name, png) in images)
            {
                var (read, text) = await ReadPngAsync(engine, png);
                reads.Add(read);
                raw.Append("[").Append(name).Append("] ").Append(string.Join(", ", read.Names))
                   .Append(read.Count.HasValue ? $"  (window says {read.Count})" : "").Append("\n");
            }
            return (reads, raw.ToString());
        }

        static async Task<(PartyRead read, string text)> ReadPngAsync(Windows.Media.Ocr.OcrEngine engine, byte[] png)
        {
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
