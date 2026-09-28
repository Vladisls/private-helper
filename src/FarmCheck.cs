using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CAHelper
{
    /// Raw screen pixels, BGRA like System.Drawing's 32bpp format (so tests can feed screenshots on any OS).
    public sealed class Img
    {
        public readonly int W, H; public readonly byte[] Px;   // B,G,R,A per pixel
        public Img(int w, int h, byte[] px) { W = w; H = h; Px = px; }
        public void Rgb(int x, int y, out int r, out int g, out int b)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) { r = g = b = 0; return; }
            int i = (y * W + x) * 4; b = Px[i]; g = Px[i + 1]; r = Px[i + 2];
        }
        public double Lum(int x, int y) { Rgb(x, y, out int r, out int g, out int b); return (r + g + b) / 3.0; }
    }

    public struct Grid { public double X, Y, PitchX, PitchY, Score; public double Scale => PitchX / FarmCheck.RefPitch; }

    public sealed class SlotRead { public int Row, Col; public string Item; public double IconDist; public int? Count; public List<string> Cells = new List<string>(); }

    /// Pixel logic for the Farm Tracker: inventory grid, core icons, stack digits, end-window trigger, loot-feed diff.
    /// Numbers were tuned on real 2560x1440 screenshots; other resolutions scale by the detected slot size.
    public static class FarmCheck
    {
        public const double RefPitch = 76.75;
        public const int CW = 11, CH = 17, DigitRight = 69, DigitTop = 46, SlotInset = 8;

        // digit cells (11x17) sampled from a 2560x1440 screenshot, 2026-09-28
        public static readonly (char d, string cell)[] Digits = {
            // '7' from a live helper capture (Upgrade Core (High) = 172), 2026-09-28
            ('7', "...........##########.##########.........##........##.........##........##.........##........##.........##........##.........##........##.........##........##.........##........##........"),
            ('3', "..............#####.....#######..........##.........##.........##........##.......###.......####..........##..........##.........##.........##.........##..#.....##...########....#####...."),
            ('_', ".....................................................................................................................................#..........#..........#..............................."),
            ('8', "..............###......#######....#.....##..##.....##..##.....##..###....#....###..##.....#####.....##..###....#.....##..##......##.##......##.##......##..##....##...#######......####...."),
            ('0', ".............####......######....##....##...#...#..#...#...##.#...#......##..#......##..#......##..#......##..#......##..#......##..#......##..#......#...##....##....######......####....."),
            ('2', ".............###........#####.........##..........##.........##..#......##..#......#...#.....##...#....##....#....#.....##.###..##.#..##....#.#.##........##.........########...########..."),
            ('_', ".............................................................................................................................#######...............#...........................#..........#"),
            ('4', "..................##........###.......####.......#.##......#..##.....##..##....##...##...##....##...#.....##...#########..#########........##.........##.........##.........##.........##.."),
            ('4', "...............#..##........###.......####.......#.##......#..##.....##..##....##...##...##....##...#.....##...#########...########........##.........##.........##.........##.........##.."),
            ('1', "................##.........##......#####......#####.........##.........##.........##.........##.........##.........##.......#.##.......#.##.#..#..#.##.........##...#.....##......#######.."),
            ('_', "...............................................................................................................................................#.#.............#..........................."),
            ('4', "..................##........###.......####.......#.##......#..##.....##..##....##...##...##....##...#.....##...#########..#########........##.........##.........##.........##.........##.."),
            ('1', "................##.........##......#####......#####.........##.........##.........##.........##.........##.........##.........##.........##.........##.......#.##.........##......#######.."),
            ('_', "..........................................................................................................................................................................................."),
            ('9', "..............####......######....##....##..##......#..##......##.##......##.##......##..##.....##..#########....####.##.........#..........#.........##........##....######.....####......"),
            ('_', "...............................................#..........#..........#..........#..........#..............................................................................................."),
            ('5', ".............########...########...##.........##.........##.........##.........#####......#######.........##..........##.........##.........##.........##........##....#######.....####...."),
            ('_', ".................................#........................................................#..........##.........#.........#................................................................"),
            ('4', "..................##........###.......####......##.##......#..##.....##..##....##...##...##....##...#.....##...#########..#########........##.........##.........##.........##.........##.."),
            ('3', "..............#####....########...#......##.........##.........##........##.......###.......####..........##....#.....##..#......##.........##.........##..#.....##...########.....####...."),
            ('_', "........................................####.........##...........................#........................................................................................................"),
            ('8', "..............###......#######....#.....##..##.....##..##.....##..###....#....###..##.....#####.....##..###....#.....##..##......##.##......##.##......##..##....##...#######......####...."),
            ('5', "............########...########...##.........##.........##.........##.........#####......#######.........##..........##.........##.........##.........##........##....#######.....####....."),
            ('1', "........#......##..#......##..#...##.##..###.##.##.........##.........##.........##.........##.........##.........##.........##.........##.........##.........##.........##......#######..."),
            ('_', "..............................................................................................................................................................#..###......................."),
            ('6', "................###......######....##........##.........#..........#.........##.####....#########..##.....##..##......##.##......##.##......##..#......##..##....##....######......####...."),
            ('1', "...............##.........##......#####......#####.........##.........##.........##.........##.........##.........##.........##.........##.........##.........##.........##......#######..."),
            ('_', "...............................#..........##..............###.....##.###..................................................................................................................."),
            ('2', "..............####.....########.........##..........##.........##.........##.........#.........##........##........###........##........##........##........##........#########..#########."),
            ('_', "..............#...............................#...........#...........#.................#.................................................................................................."),
        };
        // core icon colour histograms from the same screenshot (top row Upgrade Cores, bottom row Force Cores)
        public static readonly (string name, double[] f)[] Icons = {
            ("Upgrade Core (Ultimate)", new[] { 0.00, 0.00, 0.09, 7.50, 21.55, 6.09, 0.36, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 39.68, 8.18, 16.55 }),
            ("Upgrade Core (Highest)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.14, 3.91, 27.27, 8.27, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 36.05, 7.05, 17.32 }),
            ("Upgrade Core (High)", new[] { 11.95, 0.91, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.14, 12.50, 58.77, 14.45, 1.27 }),
            ("Upgrade Core (Medium)", new[] { 2.23, 23.36, 1.50, 0.05, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 52.68, 19.41, 0.77 }),
            ("Upgrade Core (Low)", new[] { 0.05, 3.32, 19.14, 0.32, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 56.68, 19.50, 1.00 }),
            ("Force Core (Ultimate)", new[] { 0.00, 0.00, 0.00, 2.36, 5.59, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 46.32, 13.14, 32.59 }),
            ("Force Core (Highest)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.55, 3.09, 10.45, 3.32, 0.00, 0.00, 0.00, 0.00, 0.00, 44.36, 12.55, 25.68 }),
            ("Force Core (High)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.14, 25.95, 5.18, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 46.18, 14.95, 7.59 }),
            ("Force Core (Medium)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 2.23, 13.86, 0.32, 0.00, 0.00, 0.00, 52.91, 21.77, 8.91 }),
            ("Force Core (Low)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 3.18, 13.41, 0.05, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 56.00, 19.55, 7.82 }),
        };
        // live captures (2026-09-28): JPG colours sit a little off the live ones, so these match better in game
        public static readonly (string name, double[] f)[] IconsLive = {
            ("Force Core (Medium)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 5.55, 15.77, 8.36, 0.00, 0.00, 0.00, 0.00, 46.68, 18.18, 5.45 }),
            ("Force Core (Low)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.23, 6.23, 7.91, 2.09, 2.64, 8.32, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 46.86, 17.73, 8.00 }),
        };
        /// Default icon set: the screenshot icons plus the live ones.
        public static List<(string name, double[] f)> DefaultIcons() => Icons.Concat(IconsLive).ToList();

        public static readonly double[] EmptySlot = { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 100.00, 0.00, 0.00 };

        // ---------- inventory grid ----------
        /// Finds the 8x8 slot grid inside a box (the box may be loose, even the whole inventory window).
        /// Builds a brightness profile along each axis once, then searches every slot size and start for the
        /// pattern "bright border line, darker slot interior" repeated for all slots.
        public static Grid FindGrid(Img img, int bx, int by, int bw, int bh, int cols = 8, int rows = 8, double expectedPitch = RefPitch)
        {
            // Look a little past the box so a tight or slightly clipped box still finds the whole grid.
            int pad = (int)Math.Round(expectedPitch * 0.6);
            int x0 = Math.Max(0, bx - pad), y0 = Math.Max(0, by - pad);
            int x1 = Math.Min(img.W, bx + bw + pad), y1 = Math.Min(img.H, by + bh + pad);
            int w = x1 - x0, h = y1 - y0;

            (double start, double pitch, double score) Axis(double[] prof, int n)
            {
                double best = double.MinValue, bs = 0, bp = expectedPitch;
                for (double p = expectedPitch * 0.8; p <= expectedPitch * 1.25; p += 0.25)
                {
                    int inset = Math.Max(2, (int)Math.Round(6 * p / RefPitch));
                    for (double st = 0; st + n * p < prof.Length - 1 - inset; st += 1)
                    {
                        double sc = 0;
                        for (int k = 0; k <= n; k++)
                        {
                            int pos = (int)Math.Round(st + k * p);
                            sc += prof[pos];
                            if (k < n) sc -= prof[pos + inset];
                        }
                        if (sc > best) { best = sc; bs = st; bp = p; }
                    }
                }
                return (bs, bp, best);
            }

            // 1) columns, from the whole (padded) box
            var colProf = new double[w];
            for (int y = 0; y < h; y += 2) for (int x = 0; x < w; x++) colProf[x] += img.Lum(x0 + x, y0 + y);
            var hc = Axis(colProf, cols);
            // 2) rows, only across the grid's own columns (so the buttons under the grid can't pass as a row)
            int gx0 = x0 + (int)hc.start, gx1 = Math.Min(img.W, gx0 + (int)Math.Round(cols * hc.pitch));
            var rowProf = new double[h];
            for (int y = 0; y < h; y++) for (int x = gx0; x < gx1; x += 2) rowProf[y] += img.Lum(x, y0 + y);
            var vr = Axis(rowProf, rows);
            // 3) columns again, only across the grid's rows
            int gy0 = y0 + (int)vr.start, gy1 = Math.Min(img.H, gy0 + (int)Math.Round(rows * vr.pitch));
            Array.Clear(colProf, 0, colProf.Length);
            for (int y = gy0; y < gy1; y += 2) for (int x = 0; x < w; x++) colProf[x] += img.Lum(x0 + x, y);
            hc = Axis(colProf, cols);

            var g = new Grid { X = x0 + hc.start, Y = y0 + vr.start, PitchX = hc.pitch, PitchY = vr.pitch };
            // A neighbouring window edge can mimic one column/row of slots: also try one slot left/right/up/down
            // and keep the position with the strongest border-vs-interior contrast.
            // Every real column has horizontal slot borders running through it (and every row vertical ones);
            // a gap next to the window doesn't. Keep the position whose weakest column/row is strongest.
            Grid best = g; double bestWeak = WeakestLine(img, g, cols, rows);
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                var c = g; c.X += dx * g.PitchX; c.Y += dy * g.PitchY;
                if (c.X < 0 || c.Y < 0 || c.X + cols * c.PitchX >= img.W || c.Y + rows * c.PitchY >= img.H) continue;
                double weak = WeakestLine(img, c, cols, rows);
                if (weak > bestWeak) { best = c; bestWeak = weak; }
            }
            best.Score = BorderContrast(img, best, cols, rows);
            return best;
        }

        /// Border contrast of the weakest column (its horizontal borders) or row (its vertical borders).
        public static double WeakestLine(Img img, Grid g, int cols = 8, int rows = 8)
        {
            int inset = Math.Max(2, (int)Math.Round(6 * g.Scale));
            double weakest = double.MaxValue;
            for (int c = 0; c < cols; c++)
            {
                double s = 0; int n = 0;
                for (int k = 0; k <= rows; k++)
                {
                    int y = (int)Math.Round(g.Y + k * g.PitchY);
                    for (double f = 0.25; f <= 0.75; f += 0.05) { int x = (int)(g.X + (c + f) * g.PitchX); s += img.Lum(x, y) - img.Lum(x, y + (k < rows ? inset : -inset)); n++; }
                }
                weakest = Math.Min(weakest, s / n);
            }
            for (int r = 0; r < rows; r++)
            {
                double s = 0; int n = 0;
                for (int k = 0; k <= cols; k++)
                {
                    int x = (int)Math.Round(g.X + k * g.PitchX);
                    for (double f = 0.25; f <= 0.75; f += 0.05) { int y = (int)(g.Y + (r + f) * g.PitchY); s += img.Lum(x, y) - img.Lum(x + (k < cols ? inset : -inset), y); n++; }
                }
                weakest = Math.Min(weakest, s / n);
            }
            return weakest;
        }

        /// Slots whose centre is near-black, i.e. empty inventory slots.
        public static int DarkSlots(Img img, Grid g, int cols = 8, int rows = 8)
        {
            int n = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double sum = 0; int k = 0;
                    for (int j = 0; j < 5; j++) for (int i = 0; i < 5; i++)
                    { sum += img.Lum((int)(g.X + c * g.PitchX + g.PitchX * (0.3 + 0.1 * i)), (int)(g.Y + r * g.PitchY + g.PitchY * (0.3 + 0.1 * j))); k++; }
                    if (sum / k < 45) n++;
                }
            return n;
        }

        /// Looks for the 8x8 inventory grid anywhere on the screen (used when the saved area doesn't show it).
        /// Returns null when there's no convincing grid.
        public static Grid? LocateInventory(Img screen, double expectedPitch = RefPitch)
        {
            var g = FindGrid(screen, 0, 0, screen.W, screen.H, 8, 8, expectedPitch);
            if (!InventoryOpen(screen, g)) return null;
            if (WeakestLine(screen, g) < MinWeakestLine) return null;
            return g;
        }
        public const double MinWeakestLine = 8.0;

        /// Brightness on border lines vs just inside the slots. Inventory open ~2.2, anything else ~1.3.
        public static double BorderContrast(Img img, Grid g, int cols = 8, int rows = 8)
        {
            double on = 0, inside = 0; int inset = (int)Math.Round(6 * g.Scale);
            for (int k = 0; k <= cols; k++)
            {
                int x = (int)Math.Round(g.X + k * g.PitchX);
                for (int y = (int)g.Y; y < g.Y + rows * g.PitchY; y += 2) { on += img.Lum(x, y); if (k < cols) inside += img.Lum(x + inset, y); }
            }
            return on / Math.Max(1, inside);
        }
        public static bool InventoryOpen(Img img, Grid g) => BorderContrast(img, g) > 1.7;

        static (int x, int y) SlotOrigin(Grid g, int r, int c) =>
            ((int)Math.Round(g.X + SlotInset * g.Scale + c * g.PitchX), (int)Math.Round(g.Y + r * g.PitchY));

        // ---------- inventory tab ----------
        /// Fingerprint of the tab strip above the grid (brightness in 64 columns x 3 rows). The highlighted
        /// tab changes it, so the core tab can be told apart from the other tabs.
        public static double[] TabPrint(Img img, Grid g)
        {
            const int C = 64, R = 3; var f = new double[C * R]; var n = new int[C * R];
            double s = g.Scale, x0 = g.X, x1 = g.X + 8 * g.PitchX, y0 = g.Y - 58 * s, y1 = g.Y - 20 * s;
            for (int y = (int)y0; y < (int)y1; y++)
                for (int x = (int)x0; x < (int)x1; x += 2)
                {
                    int c = (int)((x - x0) / (x1 - x0) * C), r = (int)((y - y0) / (y1 - y0) * R);
                    if (c < 0 || c >= C || r < 0 || r >= R) continue;
                    f[r * C + c] += img.Lum(x, y); n[r * C + c]++;
                }
            for (int i = 0; i < f.Length; i++) f[i] = n[i] > 0 ? f[i] / n[i] : 0;
            return f;
        }
        public const double TabMatchMax = 6.0;

        // ---------- icons ----------
        /// Colour mix of the icon (18 hue bins + 3 brightness bins). Position-independent, so a pixel of grid drift doesn't matter.
        public static double[] IconFeature(Img img, Grid g, int r, int c)
        {
            var (sx, sy) = SlotOrigin(g, r, c); double s = g.Scale;
            var h = new double[21]; double n = 0;
            for (int y = 0; y < 44; y++)
                for (int x = 0; x < 50; x++)
                {
                    img.Rgb(sx + (int)Math.Round((4 + x) * s), sy + (int)Math.Round((2 + y) * s), out int R, out int G, out int B);
                    double rr = R / 255.0, gg = G / 255.0, bb = B / 255.0, mx = Math.Max(rr, Math.Max(gg, bb)), mn = Math.Min(rr, Math.Min(gg, bb)), d = mx - mn;
                    n++;
                    if (d > 0.18 && mx > 0.25)
                    {
                        double hue = mx == rr ? ((gg - bb) / d) % 6 : mx == gg ? (bb - rr) / d + 2 : (rr - gg) / d + 4;
                        hue = (hue < 0 ? hue + 6 : hue) / 6;
                        h[Math.Min(17, (int)(hue * 18))]++;
                    }
                    else h[18 + Math.Min(2, (int)(mx * 3))]++;
                }
            for (int i = 0; i < 21; i++) h[i] = h[i] / n * 100;
            return h;
        }
        public static double Dist(double[] a, double[] b) { double s = 0; for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]); return s / a.Length; }

        // ---------- digits ----------
        static bool White(Img img, int x, int y) { img.Rgb(x, y, out int r, out int g, out int b); int mn = Math.Min(r, Math.Min(g, b)), mx = Math.Max(r, Math.Max(g, b)); return mn >= 165 && mx - mn < 70; }
        static bool Dark(Img img, int x, int y) { img.Rgb(x, y, out int r, out int g, out int b); return Math.Max(r, Math.Max(g, b)) < 70; }
        /// White pixel with a dark outline next to it: stack digits have one, icon glows don't.
        static bool DigitPx(Img img, int x, int y)
        {
            if (!White(img, x, y)) return false;
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) if (Dark(img, x + dx, y + dy)) return true;
            return false;
        }
        public static string Cell(Img img, Grid g, int r, int c, int i)
        {
            var (sx, sy) = SlotOrigin(g, r, c); double s = g.Scale;
            var chars = new char[CW * CH];
            for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int px = sx + (int)Math.Round((DigitRight - (i + 1) * CW + x) * s), py = sy + (int)Math.Round((DigitTop + y) * s);
                    chars[y * CW + x] = DigitPx(img, px, py) ? '#' : '.';
                }
            return new string(chars);
        }
        /// Hamming distance allowing up to 2 px of shift.
        public static int CellDist(string a, string b)
        {
            int best = int.MaxValue;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int d = 0;
                    for (int y = 0; y < CH; y++)
                        for (int x = 0; x < CW; x++)
                        {
                            int sx = x - dx, sy = y - dy;
                            char ca = sx >= 0 && sy >= 0 && sx < CW && sy < CH ? a[sy * CW + sx] : '.';
                            if (ca != b[y * CW + x]) d++;
                        }
                    if (d < best) best = d;
                }
            return best;
        }
        public const int DigitMatchMax = 21;   // same digit <= 16, closest different digits 26 on the test screenshot

        /// Reads a stack count right to left. null = a glyph wasn't recognised (e.g. a 7 before it has been learned).
        public static int? ReadCount(Img img, Grid g, int r, int c, IList<(char d, string cell)> templates, List<string> cellsOut)
        {
            var digits = new List<char>();
            for (int i = 0; i < 5; i++)
            {
                string cell = Cell(img, g, r, c, i);
                if (cell.Count(ch => ch == '#') < 6) break;                       // nothing here: number ended
                var best = templates.Select(t => (t.d, dist: CellDist(cell, t.cell))).OrderBy(t => t.dist).First();
                if (best.dist > DigitMatchMax) { cellsOut?.Add(cell); return null; }
                if (best.d == '_') break;
                cellsOut?.Add(cell);
                digits.Insert(0, best.d);
            }
            if (digits.Count == 0) return 1;                                      // single item: no number shown
            return int.Parse(new string(digits.ToArray()), CultureInfo.InvariantCulture);
        }

        public static List<SlotRead> ReadInventory(Img img, Grid g, IList<(string name, double[] f)> icons, IList<(char d, string cell)> digits, int cols = 8, int rows = 8)
        {
            var list = new List<SlotRead>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var f = IconFeature(img, g, r, c);
                    double empty = Dist(f, EmptySlot);
                    var best = icons.Select(t => (t.name, d: Dist(f, t.f))).OrderBy(t => t.d).First();
                    if (empty < best.d || best.d > 1.0) continue;                   // empty slot or something that isn't a known core
                    var s = new SlotRead { Row = r, Col = c, Item = best.name, IconDist = best.d };
                    s.Count = ReadCount(img, g, r, c, digits, s.Cells);
                    list.Add(s);
                }
            return list;
        }

        /// The slot frame has two bright lines ~8 px apart, so the snap can land on either one. Try the neighbours
        /// and keep the grid that reads best: most recognised cores, fewest unreadable counts, closest icons.
        public static (Grid grid, List<SlotRead> slots) BestRead(Img img, Grid g, IList<(string name, double[] f)> icons, IList<(char d, string cell)> digits)
        {
            (Grid, List<SlotRead>, double) best = (g, new List<SlotRead>(), double.MinValue);
            double step = SlotInset * g.Scale;
            foreach (var dx in new[] { 0.0, -step, step })
                foreach (var dy in new[] { 0.0, -step, step })
                {
                    var cand = g; cand.X += dx; cand.Y += dy;
                    var read = ReadInventory(img, cand, icons, digits);
                    double score = read.Count * 10 + read.Count(r => r.Count.HasValue) * 5 - read.Sum(r => r.IconDist) - read.Count(r => r.Count == 1) * 2;
                    if (score > best.Item3) best = (cand, read, score);
                }
            return (best.Item1, best.Item2);
        }

        /// Teaches digits from a count the player typed: pairs each digit cell (right to left) with the typed digit
        /// and returns the cells that the current templates don't already match closely.
        public static List<(char d, string cell)> LearnDigits(Img img, Grid g, int r, int c, int typed, IList<(char d, string cell)> templates)
        {
            var learned = new List<(char, string)>();
            if (typed < 10 && typed <= 1) return learned;             // "1" has no number drawn
            string t = typed.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < t.Length; i++)
            {
                char d = t[t.Length - 1 - i];
                string cell = Cell(img, g, r, c, i);
                if (cell.Count(ch => ch == '#') < 6) break;
                var near = templates.Where(x => x.d == d).Select(x => CellDist(cell, x.cell)).DefaultIfEmpty(int.MaxValue).Min();
                if (near > 8) learned.Add((d, cell));
            }
            return learned;
        }

        /// Cores that should be checked with the player: at the start every known core that wasn't found;
        /// later every core that was there before (count > 0) but isn't found now. Already-confirmed ones are skipped.
        public static List<string> MissingToConfirm(IEnumerable<string> expected, IDictionary<string, int> found,
                                                    IDictionary<string, int> baseline, ICollection<string> confirmedEmpty)
        {
            var candidates = baseline == null ? expected : baseline.Where(kv => kv.Value > 0).Select(kv => kv.Key);
            return candidates.Where(n => !found.ContainsKey(n) && !confirmedEmpty.Contains(n)).Distinct().ToList();
        }

        /// Gains over every core seen at either end; a core missing on one side counts as 0 there.
        public static Dictionary<string, int> Gains(IDictionary<string, int> baseline, IDictionary<string, int> current)
        {
            var g = new Dictionary<string, int>();
            if (baseline == null || current == null) return g;
            foreach (var k in baseline.Keys.Union(current.Keys))
            {
                int b = baseline.TryGetValue(k, out int bv) ? bv : 0, c = current.TryGetValue(k, out int cv) ? cv : 0;
                if (b != c) g[k] = c - b;
            }
            return g;
        }

        // ---------- dungeon end window ----------
        /// Cheap check before reading text: the end window is a dark panel with yellow text lines.
        public static bool EndWindowLikely(Img img)
        {
            int yellow = 0, dark = 0, tot = 0;
            for (int y = 0; y < img.H; y += 2)
                for (int x = 0; x < img.W; x += 2)
                {
                    img.Rgb(x, y, out int r, out int g, out int b); tot++;
                    if (r > 180 && g > 110 && b < 90 && r > g) yellow++;
                    if (Math.Max(r, Math.Max(g, b)) < 45) dark++;
                }
            return tot > 0 && yellow * 1000 >= tot * 3 && dark * 100 >= tot * 55;
        }

        public sealed class RunResult { public string Dungeon; public int Seconds; public int Dp; }
        static readonly Regex TimeRx = new Regex(@"Time\s*[:;.]?\s*(\d+)\s*min\S*\s*(\d+)\s*sec", RegexOptions.IgnoreCase);
        static readonly Regex DpRx = new Regex(@"Point\s*Gained\s*[:;.]?\s*(\d+)", RegexOptions.IgnoreCase);

        /// Parses the end window's text. null when it isn't a "Quest Dungeon Cleared!" window.
        public static RunResult ParseEndWindow(IList<string> lines)
        {
            int cleared = -1;
            for (int i = 0; i < lines.Count; i++) if (lines[i].IndexOf("Cleared", StringComparison.OrdinalIgnoreCase) >= 0) { cleared = i; break; }
            if (cleared < 0) return null;
            string all = string.Join(" ", lines);
            var tm = TimeRx.Match(all); var dp = DpRx.Match(all);
            string name = null;
            for (int i = cleared - 1; i >= 0; i--)
            {
                var l = lines[i].Trim();
                if (l.Length < 3 || l.Equals("Dungeon", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase)) continue;
                name = l; break;
            }
            return new RunResult
            {
                Dungeon = name ?? "Unknown dungeon",
                Seconds = tm.Success ? int.Parse(tm.Groups[1].Value) * 60 + int.Parse(tm.Groups[2].Value) : 0,
                Dp = dp.Success ? int.Parse(dp.Groups[1].Value) : 0
            };
        }

        // ---------- loot feed ----------
        /// The feed scrolls up; new lines are what follows the longest overlap between the old tail and the new head.
        /// Identical lines in a row are ambiguous, which is why this is only used for rare drops.
        public static List<string> NewLines(IList<string> prev, IList<string> cur)
        {
            if (prev == null || prev.Count == 0) return cur.ToList();
            for (int k = Math.Min(prev.Count, cur.Count); k > 0; k--)
            {
                bool ok = true;
                for (int i = 0; i < k && ok; i++) if (!Same(prev[prev.Count - k + i], cur[i])) ok = false;
                if (ok) return cur.Skip(k).ToList();
            }
            return cur.ToList();
        }
        static bool Same(string a, string b) => Norm(a) == Norm(b);
        static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        static readonly Regex LootRx = new Regex(@"Obtain\s+(.+?)\s*x\s*(\d+)\s*$", RegexOptions.IgnoreCase);
        public static (string item, int qty)? ParseLoot(string line)
        {
            var m = LootRx.Match((line ?? "").Trim());
            return m.Success ? (m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value)) : ((string, int)?)null;
        }
    }
}
