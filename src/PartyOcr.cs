using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
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

        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        /// Real screen height in pixels, even with Windows display scaling on.
        public static int PhysicalScreenHeight() { using (new DpiAware()) return GetSystemMetrics(1); }
        public static int PhysicalScreenWidth() { using (new DpiAware()) return GetSystemMetrics(0); }
        /// All monitors together, in real pixels (can start at negative coordinates).
        public static Rectangle PhysicalVirtualScreen()
        { using (new DpiAware()) return new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)); }

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


        /// Reads the text lines of a bitmap (enlarged first for small fonts).
        public static async Task<List<string>> ReadLinesAsync(Bitmap bmp, int enlarge = 2)
        {
            var lines = new List<string>();
            foreach (var line in await ReadWordsAsync(bmp, enlarge)) lines.Add(string.Join(" ", line.ConvertAll(w => w.Text)));
            return lines;
        }

        /// Reads the text of a bitmap as lines of words, each with its box in the bitmap's own pixels
        /// (the enlargement is divided back out).
        public static async Task<List<List<OcrWord>>> ReadWordsAsync(Bitmap bmp, int enlarge = 2)
        {
            var engine = CreateEngine();
            if (engine == null) throw new InvalidOperationException("Windows has no text-recognition language installed (add English in Settings > Time & language > Language).");
            byte[] png;
            if (enlarge > 1) using (var big = Enlarge(bmp, enlarge)) png = Png(big);
            else png = Png(bmp);
            using (var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                await ras.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(png));
                ras.Seek(0);
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
                using (var sb = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied))
                {
                    var result = await engine.RecognizeAsync(sb);
                    var lines = new List<List<OcrWord>>();
                    double f = Math.Max(1, enlarge);
                    foreach (var line in result.Lines)
                    {
                        var words = new List<OcrWord>();
                        foreach (var w in line.Words)
                        {
                            var b = w.BoundingRect;
                            words.Add(new OcrWord(w.Text, b.X / f, b.Y / f, b.Width / f, b.Height / f));
                        }
                        lines.Add(words);
                    }
                    return lines;
                }
            }
        }

        /// Reads a bitmap of any size (e.g. all monitors together): the text reader only takes images up to
        /// OcrEngine.MaxImageDimension, so it is read in overlapping pieces. Word boxes are in the bitmap's pixels.
        public static async Task<List<List<OcrWord>>> ReadWordsTiledAsync(Bitmap bmp)
        {
            int max = (int)Math.Min(4096u, Windows.Media.Ocr.OcrEngine.MaxImageDimension);
            var all = new List<List<OcrWord>>();
            foreach (var (x, y, w, h) in FarmCheck.Tiles(bmp.Width, bmp.Height, max, 200))
            {
                List<List<OcrWord>> lines;
                if (x == 0 && y == 0 && w == bmp.Width && h == bmp.Height) lines = await ReadWordsAsync(bmp, 1);
                else using (var tile = bmp.Clone(new Rectangle(x, y, w, h), PixelFormat.Format32bppArgb)) lines = await ReadWordsAsync(tile, 1);
                foreach (var line in lines) all.Add(line.ConvertAll(o => new OcrWord(o.Text, o.X + x, o.Y + y, o.W, o.H)));
            }
            return all;
        }

        public static Bitmap FromImg(Img img)
        {
            var bmp = new Bitmap(img.W, img.H, PixelFormat.Format32bppArgb);
            var d = bmp.LockBits(new Rectangle(0, 0, img.W, img.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < img.H; y++) Marshal.Copy(img.Px, y * img.W * 4, d.Scan0 + y * d.Stride, img.W * 4); }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }

        /// Copies a bitmap's pixels into the platform-neutral Img the farm logic works on.
        public static Img ToImg(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var d = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[bmp.Width * bmp.Height * 4];
                for (int y = 0; y < bmp.Height; y++) Marshal.Copy(d.Scan0 + y * d.Stride, buf, y * bmp.Width * 4, bmp.Width * 4);
                return new Img(bmp.Width, bmp.Height, buf);
            }
            finally { bmp.UnlockBits(d); }
        }

        /// Windows' text reader is built for lines of text: a lone digit, a very short string or a big glyph with no
        /// margin often returns nothing. So each count region is read on its own picture, padded, with a printed
        /// prefix in front ("Qty ") so it reads a line; the count is the trailing run of digits, so the prefix never
        /// interferes. Several variants are tried per region until one reads. glyphCounts and names are for diagnostics.
        public static readonly (string name, int enlarge, bool prefix)[] Variants = { ("prefix 2x", 2, true), ("plain 3x", 3, false), ("prefix 3x", 3, true), ("plain 2x", 2, false), ("prefix 4x", 4, true) };

        public static async Task<int?[]> ReadCountStripsAsync(IList<Img> strips, IList<int> glyphCounts, IList<string> names = null)
        {
            var result = new int?[strips.Count];
            var diag = new List<(string name, Bitmap pic, List<OcrWord> words, int? got, string variant)>();
            for (int i = 0; i < strips.Count; i++)
            {
                if (strips[i] == null) continue;
                string label = names != null && i < names.Count ? names[i] + $" ({glyphCounts[i]} glyphs)" : $"band {i}";
                Bitmap keep = null; List<OcrWord> keepWords = null; string keepVariant = "none";
                foreach (var v in Variants)
                {
                    var pic = MakeCountPicture(strips[i], v.enlarge, v.prefix);
                    List<OcrWord> words;
                    try { var lines = await ReadWordsAsync(pic, 1); words = new List<OcrWord>(); foreach (var l in lines) words.AddRange(l); }
                    catch { pic.Dispose(); throw; }
                    var text = string.Join("", words.OrderBy(w => w.X).Select(w => w.Text));
                    var got = FarmCheck.TrailingCount(text);
                    if (keep == null) { keep = pic; keepWords = words; keepVariant = v.name; } else if (got.HasValue) { keep.Dispose(); keep = pic; keepWords = words; keepVariant = v.name; } else pic.Dispose();
                    if (got.HasValue) { result[i] = got; break; }
                }
                diag.Add((label, keep, keepWords ?? new List<OcrWord>(), result[i], result[i].HasValue ? keepVariant : "none of " + Variants.Length));
            }
            lock (DiagLock) { foreach (var d in LastDiag) d.pic?.Dispose(); LastDiag = diag; }
            return result;
        }

        /// One count on its own picture: dark margin, optional printed prefix, the region enlarged.
        static Bitmap MakeCountPicture(Img region, int enlarge, bool prefix)
        {
            int pad = 10 * enlarge, prefixW = prefix ? 28 * enlarge : 0;
            var bmp = new Bitmap(prefixW + region.W * enlarge + 2 * pad, region.H * enlarge + 2 * pad, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var src = FromImg(region))
            {
                g.Clear(Color.FromArgb(30, 30, 30));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                if (prefix)
                    using (var f = new Font("Segoe UI", 7f * enlarge, FontStyle.Bold))
                        g.DrawString("Qty", f, Brushes.White, pad / 2, pad + (region.H * enlarge - 10f * enlarge) / 2);
                g.DrawImage(src, new Rectangle(prefixW + pad, pad, region.W * enlarge, region.H * enlarge));
            }
            return bmp;
        }

        /// What the last count read looked like, per slot: the picture the reader got, its words, the result, the variant.
        static readonly object DiagLock = new object();
        public static List<(string name, Bitmap pic, List<OcrWord> words, int? got, string variant)> LastDiag = new List<(string, Bitmap, List<OcrWord>, int?, string)>();

        /// Saves the last count read for diagnostics: per slot the name, result and variant, the picture the reader
        /// received, and the same picture with the reader's words boxed.
        public static void SaveLastStrip(string path)
        {
            List<(string name, Bitmap pic, List<OcrWord> words, int? got, string variant)> diag;
            lock (DiagLock) diag = LastDiag.Select(d => (d.name, d.pic == null ? null : (Bitmap)d.pic.Clone(), d.words, d.got, d.variant)).ToList();
            if (diag.Count == 0) return;
            int labelW = 300, gap = 8;
            int picW = diag.Max(d => d.pic?.Width ?? 0), totalH = diag.Sum(d => (d.pic?.Height ?? 20) + gap);
            using (var bmp = new Bitmap(labelW + picW * 2 + 30, Math.Max(totalH, 20), PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                g.Clear(Color.FromArgb(40, 40, 40));
                int y = 0;
                foreach (var d in diag)
                {
                    string got = d.got.HasValue ? d.got.Value.ToString() : "?";
                    g.DrawString($"{d.name}  →  {got}   [{d.variant}]", f, got == "?" ? Brushes.OrangeRed : Brushes.White, 4, y + 2);
                    if (d.pic != null)
                    {
                        g.DrawImageUnscaled(d.pic, labelW, y); g.DrawImageUnscaled(d.pic, labelW + picW + 10, y);
                        foreach (var w in d.words)
                        {
                            var r = new Rectangle(labelW + picW + 10 + (int)w.X, y + (int)w.Y, (int)w.W, (int)w.H);
                            g.DrawRectangle(Pens.Red, r); g.DrawString(w.Text, f, Brushes.Red, r.Right + 2, r.Top);
                        }
                        d.pic.Dispose();
                    }
                    y += (d.pic?.Height ?? 20) + gap;
                }
                bmp.Save(path, ImageFormat.Png);
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

        readonly string message;
        AreaPicker(Bitmap shot, Rectangle virt, string message)
        {
            this.shot = shot; this.virt = virt; this.message = message;
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
            var msg = message;
            using (var f = new Font("Segoe UI", 14f, FontStyle.Bold))
            {
                var size = g.MeasureString(msg, f);
                var r = new RectangleF((Width - size.Width) / 2 - 12, 40, size.Width + 24, size.Height + 12);
                using (var b = new SolidBrush(Color.FromArgb(220, 26, 31, 38))) g.FillRectangle(b, r);
                g.DrawString(msg, f, Brushes.White, r.X + 12, r.Y + 6);
            }
        }

        public static Rectangle? Pick(string message = "Drag a box around the party member list, including the \"member (19/25)\" line. Esc cancels.")
        {
            using (new DpiAware())
            {
                var virt = SystemInformation.VirtualScreen;
                using (var shot = new Bitmap(virt.Width, virt.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(virt.Location, Point.Empty, virt.Size);
                    using (var f = new AreaPicker(shot, virt, message))
                        return f.ShowDialog() == DialogResult.OK ? f.Picked : (Rectangle?)null;
                }
            }
        }
    }
}
