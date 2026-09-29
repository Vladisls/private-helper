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
            if (area.Width <= 0 || area.Height <= 0) throw new ArgumentException($"screen area to capture is empty ({area.Width}x{area.Height} at {area.X},{area.Y})");
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
            if (img == null) throw new ArgumentNullException(nameof(img), "image to convert is missing");
            if (img.W <= 0 || img.H <= 0) throw new ArgumentException($"image is empty ({img.W}x{img.H})");
            if (img.Px == null || img.Px.Length < img.W * img.H * 4) throw new ArgumentException($"image pixels don't match its size ({img.W}x{img.H}, {img.Px?.Length ?? 0} bytes)");
            var bmp = new Bitmap(img.W, img.H, PixelFormat.Format32bppArgb);
            var d = bmp.LockBits(new Rectangle(0, 0, img.W, img.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < img.H; y++) Marshal.Copy(img.Px, y * img.W * 4, d.Scan0 + y * d.Stride, img.W * 4); }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }

        /// Copies a bitmap's pixels into the platform-neutral Img the farm logic works on.
        public static Img ToImg(Bitmap bmp)
        {
            if (bmp == null) throw new ArgumentNullException(nameof(bmp), "bitmap to convert is missing");
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
        /// margin often returns nothing. So each count region is read on its own picture, in the treatments of
        /// FarmCheck.MakeCountVariants (tried in FarmCheck.CountRetryOrder until one gives a count). The count is the
        /// trailing run of digits among the words over the region itself, so printed words never interfere.
        /// glyphCounts and names are for diagnostics.
        public static async Task<int?[]> ReadCountStripsAsync(IList<Img> strips, IList<int> glyphCounts, IList<string> names = null)
        {
            var result = new int?[strips.Count];
            var diag = new List<CountDiag>();
            for (int i = 0; i < strips.Count; i++)
            {
                if (strips[i] == null || strips[i].W <= 0 || strips[i].H <= 0) continue;
                string label = names != null && i < names.Count ? names[i] + $" ({(i < glyphCounts.Count ? glyphCounts[i] : 0)} glyphs)" : $"band {i}";
                var variants = FarmCheck.MakeCountVariants(strips[i], PrintText)
                    .OrderBy(v => { int k = Array.IndexOf(FarmCheck.CountRetryOrder, v.name); return k < 0 ? int.MaxValue : k; }).ToList();
                CountDiag keep = null;
                foreach (var v in variants)
                {
                    var words = await ReadImgWordsAsync(v.picture);
                    var got = FarmCheck.CountFromWords(words, v.number);
                    if (keep == null || got.HasValue) keep = new CountDiag { Name = label, Pic = v.picture, Words = words, Got = got, Variant = got.HasValue ? v.name : "none of " + variants.Count };
                    if (got.HasValue) { result[i] = got; break; }
                }
                if (keep != null) diag.Add(keep);
            }
            lock (DiagLock) LastDiag = diag;
            return result;
        }

        /// All words the reader finds on a picture (in the picture's pixels), left to right per line.
        static async Task<List<OcrWord>> ReadImgWordsAsync(Img picture)
        {
            List<List<OcrWord>> lines;
            using (var bmp = FromImg(picture)) lines = await ReadWordsAsync(bmp, 1);
            var words = new List<OcrWord>(); foreach (var l in lines) words.AddRange(l);
            return words;
        }

        /// Prints a word for a count picture (FarmCheck.TextPrinter): white Segoe UI bold on black, sized so that its
        /// digits are about digitHeight tall (Segoe UI digits are ~0.72 em), with the baseline from the font's metrics.
        public static (Img pic, int baseline) PrintText(string text, int digitHeight)
        {
            if (string.IsNullOrEmpty(text) || digitHeight <= 0) return (null, 0);
            float em = Math.Max(6f, digitHeight / 0.72f);
            using (var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var ff = font.FontFamily; double emH = Math.Max(1, ff.GetEmHeight(FontStyle.Bold));
                double asc = em * ff.GetCellAscent(FontStyle.Bold) / emH, desc = em * ff.GetCellDescent(FontStyle.Bold) / emH;
                SizeF size;
                using (var probe = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(probe)) size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
                int m = 2, w = Math.Max(1, (int)Math.Ceiling(size.Width) + 2 * m), h = Math.Max(1, (int)Math.Ceiling(asc + desc) + 2 * m);
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Black);
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        g.DrawString(text, font, Brushes.White, m, m, StringFormat.GenericTypographic);
                    }
                    return (ToImg(bmp), m + (int)Math.Round(asc));
                }
            }
        }

        /// What the last count read looked like, per slot: the picture the reader got, its words, the result, the
        /// treatment. Pictures are kept as Img (plain pixel arrays), so nothing here can be disposed while in use.
        public sealed class CountDiag { public string Name; public Img Pic; public List<OcrWord> Words; public int? Got; public string Variant; }
        static readonly object DiagLock = new object();
        static List<CountDiag> LastDiag = new List<CountDiag>();

        /// Saves the last count read for diagnostics: per slot the name, result and treatment, the picture the reader
        /// received, and the same picture with the reader's words boxed. Returns false if there was nothing to save.
        public static bool SaveLastStrip(string path)
        {
            List<CountDiag> diag;
            lock (DiagLock) diag = LastDiag.ToList();
            diag = diag.Where(d => d.Pic != null && d.Pic.W > 0 && d.Pic.H > 0).ToList();
            if (diag.Count == 0) return false;
            const int labelW = 300, gap = 8;
            int picW = diag.Max(d => d.Pic.W), totalH = diag.Sum(d => Math.Max(d.Pic.H, 20) + gap);
            using (var bmp = NewBitmap(labelW + picW * 2 + 30, Math.Max(totalH, 20), "count strip picture"))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                g.Clear(Color.FromArgb(40, 40, 40));
                int y = 0;
                foreach (var d in diag)
                {
                    string got = d.Got.HasValue ? d.Got.Value.ToString(CultureInfo.InvariantCulture) : "?";
                    g.DrawString($"{d.Name}  →  {got}   [{d.Variant}]", f, got == "?" ? Brushes.OrangeRed : Brushes.White, 4, y + 2);
                    using (var pic = FromImg(d.Pic))
                    {
                        g.DrawImageUnscaled(pic, labelW, y); g.DrawImageUnscaled(pic, labelW + picW + 10, y);
                    }
                    foreach (var w in d.Words ?? new List<OcrWord>())
                    {
                        var r = new Rectangle(labelW + picW + 10 + (int)w.X, y + (int)w.Y, Math.Max(1, (int)w.W), Math.Max(1, (int)w.H));
                        g.DrawRectangle(Pens.Red, r); g.DrawString(w.Text, f, Brushes.Red, r.Right + 2, r.Top);
                    }
                    y += Math.Max(d.Pic.H, 20) + gap;
                }
                bmp.Save(path, ImageFormat.Png);
            }
            return true;
        }

        /// A new 32bpp bitmap, with a clear message instead of GDI+'s "Parameter is not valid" for bad sizes.
        static Bitmap NewBitmap(int w, int h, string what)
        {
            if (w <= 0 || h <= 0) throw new ArgumentException($"{what}: bitmap size must be positive (got {w}x{h})");
            if ((long)w * h > 200_000_000L) throw new ArgumentException($"{what}: bitmap too large ({w}x{h})");
            return new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }

        /// OCR input experiment: every count region in every treatment of FarmCheck.MakeCountVariants, each read by the
        /// text reader. Saves a sheet: one row per slot, one column per treatment; each cell shows the picture with the
        /// reader's words boxed in red, the parsed count ("?" in orange) and the raw text. Last row: read N/M per
        /// treatment. Returns one summary line per treatment.
        public static async Task<List<string>> SaveOcrTestSheetAsync(string path, IList<(string label, Img region)> slots)
        {
            var names = FarmCheck.CountVariantNames;
            var rows = new List<(string label, List<(Img pic, List<OcrWord> words, string raw, int? got)> cells)>();
            foreach (var (label, region) in slots)
            {
                var cells = new List<(Img, List<OcrWord>, string, int?)>();
                if (region != null && region.W > 0 && region.H > 0)
                    foreach (var v in FarmCheck.MakeCountVariants(region, PrintText))
                    {
                        List<OcrWord> words; string raw; int? got;
                        try
                        {
                            words = await ReadImgWordsAsync(v.picture);
                            raw = string.Join(" ", words.Select(w => w.Text));
                            got = FarmCheck.CountFromWords(words, v.number);
                        }
                        catch (Exception ex) { words = new List<OcrWord>(); raw = "error: " + ex.GetType().Name + ": " + ex.Message; got = null; }
                        cells.Add((v.picture, words, raw, got));
                    }
                rows.Add((label, cells));
            }
            int withRegion = rows.Count(r => r.cells.Count > 0);
            var readPer = names.Select((n, j) => rows.Count(r => j < r.cells.Count && r.cells[j].got.HasValue)).ToArray();
            var summary = names.Select((n, j) => $"{n}: read {readPer[j]}/{withRegion}" +
                (withRegion > 0 ? " (" + string.Join(", ", rows.Where(r => j < r.cells.Count).Select(r => r.cells[j].got?.ToString(CultureInfo.InvariantCulture) ?? "?")) + ")" : "")).ToList();

            const int pad = 8, lineH = 18, labelW = 230, headerH = 30, minCellW = 150;
            var colW = names.Select((n, j) => Math.Max(minCellW, rows.Where(r => j < r.cells.Count).Select(r => r.cells[j].pic.W).DefaultIfEmpty(0).Max()) + 2 * pad).ToArray();
            var rowH = rows.Select(r => Math.Max(lineH * 2, r.cells.Select(c => c.pic.H).DefaultIfEmpty(0).Max()) + 2 * lineH + 2 * pad).ToArray();
            int W = labelW + colW.Sum(), H = headerH + rowH.Sum() + (lineH + 2 * pad);
            using (var bmp = NewBitmap(W, H, "OCR test sheet"))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var fSmall = new Font("Segoe UI", 8f, FontStyle.Regular))
            using (var line = new Pen(Color.FromArgb(80, 80, 80)))
            using (var orange = new SolidBrush(Color.Orange))
            using (var muted = new SolidBrush(Color.FromArgb(190, 190, 190)))
            using (var red = new Pen(Color.Red, 1))
            using (var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.Clear(Color.FromArgb(28, 28, 28));
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString("slot (glyphs)", f, Brushes.White, pad, pad);
                for (int j = 0, x = labelW; j < names.Length; x += colW[j], j++) g.DrawString(names[j], f, Brushes.White, x + pad, pad);
                g.DrawLine(line, 0, headerH - 1, W, headerH - 1);
                int y = headerH;
                for (int i = 0; i < rows.Count; i++)
                {
                    var (label, cells) = rows[i];
                    g.DrawString(label, f, Brushes.White, new RectangleF(pad, y + pad, labelW - 2 * pad, rowH[i] - 2 * pad));
                    if (cells.Count == 0) g.DrawString("no count region", fSmall, muted, labelW + pad, y + pad);
                    for (int j = 0, x = labelW; j < cells.Count && j < colW.Length; x += colW[j], j++)
                    {
                        var (pic, words, raw, got) = cells[j];
                        using (var b = FromImg(pic)) g.DrawImageUnscaled(b, x + pad, y + pad);
                        foreach (var w in words) g.DrawRectangle(red, x + pad + (int)w.X, y + pad + (int)w.Y, Math.Max(1, (int)w.W), Math.Max(1, (int)w.H));
                        int ty = y + pad + Math.Max(lineH * 2, cells.Select(c => c.pic.H).Max());
                        if (got.HasValue) g.DrawString("→ " + got.Value.ToString(CultureInfo.InvariantCulture), f, Brushes.White, x + pad, ty);
                        else g.DrawString("?", f, orange, x + pad, ty);
                        g.DrawString("\"" + raw + "\"", fSmall, muted, new RectangleF(x + pad, ty + lineH, colW[j] - 2 * pad, lineH), clip);
                    }
                    y += rowH[i];
                    g.DrawLine(line, 0, y - 1, W, y - 1);
                }
                g.DrawString("summary", f, Brushes.White, pad, y + pad);
                for (int j = 0, x = labelW; j < names.Length; x += colW[j], j++)
                    g.DrawString($"read {readPer[j]}/{withRegion}", f, readPer[j] == withRegion && withRegion > 0 ? Brushes.LightGreen : orange, x + pad, y + pad);
                for (int j = 0, x = labelW; j < colW.Length; x += colW[j], j++) g.DrawLine(line, x, 0, x, H);
                bmp.Save(path, ImageFormat.Png);
            }
            return summary;
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
