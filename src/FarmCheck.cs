using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Text;

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
            // digit cells anchored on the count's last glyph, from a live 2026-09-29 frame (PNG rendering)
            ('9', "...............####.....#######....##....##..###....##..##......##.###.....##..##....###..#########....###..##........###........##.........##........##....#######....#####..............."),   // live 2026-09-29, Upgrade Core (Low)
            ('9', "...............####.....#######....##....##..###....##..##......##.###.....##..##....###..#########....###..##........###........##.........##........##....#######....#####..............."),   // live 2026-09-29, Upgrade Core (High)
            ('0', "...............###......#######...###...##...##.....##.###.....##.##......##.##......##.##......##.##......##.##......##.###.....##..##.....##..###...##....#######.....####..............."),   // live 2026-09-29, Upgrade Core (High)
            ('2', "............#####.....#######....#....###.........##.........##........###........##........###.......###......####.......##......#.##.......#.##.........########...########.............."),   // live 2026-09-29, Upgrade Core (High)
            ('4', "..................##........###........###.......####......##.##......##.##.....##..##....##...##...###...##...##....##..######################.......##.........##.........##............."),   // live 2026-09-29, Upgrade Core (Medium)
            ('1', ".................#........###......#####......##.##.........##.........##.........##.........##.........##.........##.........##.........##.........##.........##.........##..............."),   // live 2026-09-29, Upgrade Core (Medium)
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
            // all ten from a live Debug capture (2026-09-28); the shine moves an icon's mix by ~0.4-0.6, other cores are >= 1.26 away
            ("Upgrade Core (Ultimate)", new[] { 0.00, 0.00, 0.09, 3.55, 13.23, 4.59, 0.55, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 51.23, 10.27, 16.50 }),
            ("Upgrade Core (Highest)", new[] { 0.00, 0.00, 0.09, 0.00, 0.05, 0.00, 0.00, 0.00, 0.00, 1.73, 13.73, 13.27, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 48.18, 6.73, 16.23 }),
            ("Upgrade Core (High)", new[] { 9.32, 0.77, 0.32, 0.36, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 8.95, 65.00, 12.77, 2.50 }),
            ("Upgrade Core (Medium)", new[] { 1.36, 17.86, 0.41, 0.77, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 65.23, 12.91, 1.45 }),
            ("Upgrade Core (Low)", new[] { 0.18, 1.09, 17.36, 1.14, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 62.73, 15.73, 1.77 }),
            ("Force Core (Ultimate)", new[] { 0.00, 0.00, 0.00, 5.36, 4.41, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 46.59, 13.14, 30.50 }),
            ("Force Core (Highest)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.23, 1.23, 11.23, 2.00, 0.00, 0.00, 0.00, 0.00, 0.00, 44.86, 13.82, 26.64 }),
            ("Force Core (High)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 18.59, 13.82, 0.05, 0.00, 0.00, 0.00, 0.00, 0.00, 44.86, 14.50, 8.18 }),
            ("Force Core (Medium)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 3.50, 17.05, 9.32, 0.00, 0.00, 0.00, 0.00, 45.91, 16.77, 7.45 }),
            ("Force Core (Low)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.18, 5.77, 5.14, 1.00, 2.41, 7.14, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 46.05, 19.82, 12.50 }),
            // a second live moment for the two that were reported missing
            ("Force Core (Medium)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 5.55, 15.77, 8.36, 0.00, 0.00, 0.00, 0.00, 46.68, 18.18, 5.45 }),
            ("Force Core (Low)", new[] { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.23, 6.23, 7.91, 2.09, 2.64, 8.32, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 46.86, 17.73, 8.00 }),
        };
        /// Default icon set: live icons first, the screenshot ones as extra samples.
        public static List<(string name, double[] f)> DefaultIcons() => IconsLive.Concat(Icons).ToList();
        public const double IconMatchMax = 1.0;

        /// Best core for an icon mix: closest sample, and clearly closer than any other core's closest sample.
        public static (string name, double d)? MatchIcon(double[] f, IList<(string name, double[] f)> icons)
        {
            var byName = icons.GroupBy(t => t.name).Select(g => (name: g.Key, d: g.Min(t => Dist(f, t.f)))).OrderBy(t => t.d).ToList();
            if (byName.Count == 0 || byName[0].d > IconMatchMax) return null;
            if (byName.Count > 1 && byName[0].d > 0.5 && byName[1].d < byName[0].d * 1.3) return null;   // ambiguous
            return byName[0];
        }

        public static readonly double[] EmptySlot = { 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 0.00, 100.00, 0.00, 0.00 };

        // ---------- inventory grid ----------
        /// Finds the 8x8 slot grid inside a box (the box may be loose, even the whole inventory window).
        /// Builds a brightness profile along each axis once, then searches every slot size and start for the
        /// pattern "bright border line, darker slot interior" repeated for all slots.
        public const double MinPitch = 48, MaxPitch = 130;     // 1080p (~58 px slots) up to 4K (~115 px)
        public const double DefaultTolerance = 0.08;

        /// expectedPitch = 0: search every plausible slot size (no guess from the screen resolution).
        /// expectedPitch > 0: a slot size already known (saved, or estimated from the title text); search only
        /// within ±tolerance of it (0.08 = ±8%).
        public static Grid FindGrid(Img img, int bx, int by, int bw, int bh, int cols = 8, int rows = 8, double expectedPitch = 0, double tolerance = DefaultTolerance)
        {
            double pMin = expectedPitch > 0 ? Math.Max(MinPitch * 0.8, expectedPitch * (1 - tolerance)) : MinPitch;
            double pMax = expectedPitch > 0 ? Math.Min(MaxPitch * 1.2, expectedPitch * (1 + tolerance)) : MaxPitch;
            // Pitch step small enough that the last of n slot borders drifts at most ~0.5 px from a real border
            // (a 0.5 px step drifted up to 2 px over 8 slots and missed the 1-2 px border lines on a whole screen).
            // Pitches come from one fixed lattice, so the result doesn't depend on where the range starts.
            double step = Math.Min(0.25, 1.0 / Math.Max(cols, rows));
            double pFirst = Math.Ceiling(pMin / step) * step;
            // Look a little past the box so a tight or slightly clipped box still finds the whole grid.
            int pad = (int)Math.Round(pMax * 0.6);
            int x0 = Math.Max(0, bx - pad), y0 = Math.Max(0, by - pad);
            int x1 = Math.Min(img.W, bx + bw + pad), y1 = Math.Min(img.H, by + bh + pad);
            int w = x1 - x0, h = y1 - y0;

            (double start, double pitch, double score) Axis(double[] prof, int n)
            {
                double best = double.MinValue, bs = 0, bp = pFirst;
                for (int pi = 0; pFirst + pi * step <= pMax; pi++)
                {
                    double p = pFirst + pi * step;
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
        public static Grid? LocateInventory(Img screen, double expectedPitch = 0)
        {
            // A whole screen has many repeating patterns, so search narrow slot-size bands one at a time
            // (MinPitch..MaxPitch, or only near a size already known), check each band's best grid again in a
            // box around it, and keep the strongest real one.
            const double tol = 0.05;
            var bands = new List<double>();
            if (expectedPitch > 0) bands.Add(expectedPitch);
            else for (double c = MinPitch / (1 - tol); c * (1 - tol) < MaxPitch; c *= (1 + tol) / (1 - tol)) bands.Add(c);
            Grid? best = null; double bestWeak = MinWeakestLine;
            foreach (var p in bands)
            {
                var g = FindGrid(screen, 0, 0, screen.W, screen.H, 8, 8, p, tol);
                if (!Plausible(screen, g)) continue;
                var r = FindGrid(screen, (int)g.X, (int)g.Y, (int)Math.Round(8 * g.PitchX), (int)Math.Round(8 * g.PitchY), 8, 8, g.PitchX);
                if (Plausible(screen, r) && WeakestLine(screen, r) >= WeakestLine(screen, g)) g = r;
                double w = WeakestLine(screen, g);
                if (w > bestWeak) { bestWeak = w; best = g; }
            }
            return best;
        }
        /// Slots are square, the grid lies on the screen and looks like an open inventory.
        static bool Plausible(Img img, Grid g) =>
            Math.Abs(g.PitchX / g.PitchY - 1) < 0.04 && g.X >= 0 && g.Y >= 0 && g.X + 8 * g.PitchX < img.W && g.Y + 8 * g.PitchY < img.H && InventoryOpen(img, g);
        public const double MinWeakestLine = 8.0;

        /// Grid search in the saved inventory area: near the known slot size, checked against a search over every
        /// slot size (the known size can be stale after a resolution change, and a wrong size can still find a
        /// lookalike: 68 px "slots" over a 77 px inventory pass the contrast check). Keeps the stronger grid.
        /// expectedPitch = 0: every size straight away.
        public static Grid FindGridNear(Img img, int bx, int by, int bw, int bh, double expectedPitch)
        {
            var g = FindGrid(img, bx, by, bw, bh, 8, 8, expectedPitch);
            if (expectedPitch <= 0) return g;
            var any = FindGrid(img, bx, by, bw, bh, 8, 8, 0);
            return Strength(img, any) > Strength(img, g) + 1 ? any : g;
        }
        /// How clearly a grid is a real inventory: its weakest row/column border contrast (-inf when implausible).
        static double Strength(Img img, Grid g) => Plausible(img, g) ? WeakestLine(img, g) : double.NegativeInfinity;

        /// The grid to use for this capture. The cached grid while it still shows an open inventory; otherwise a
        /// fresh search (the inventory may have moved or the cached grid may be wrong), which replaces the cache
        /// when it finds an open inventory. null = the inventory really isn't visible in the area.
        public static Grid? CurrentGrid(Img img, Grid? cached, Func<Grid> search, out bool replaced)
        {
            replaced = false;
            if (cached is Grid c && InventoryOpen(img, c)) return c;
            var g = search();
            if (!InventoryOpen(img, g)) return null;
            replaced = true;
            return g;
        }

        // ---------- finding the inventory by its title ----------
        /// Slot size ~ 3.3 x the "Inventory" title's text height (2560x1440: text ~23 px, slots 76.9 px).
        /// The text reader's box height varies a little, so the grid is searched within ±25% of that.
        public const double TitlePitchRatio = 3.3, TitleTolerance = 0.25;

        /// True for the inventory window's title as the text reader returns it ("Inventory", "lnventory", "Inventorv").
        public static bool IsInventoryTitle(string text)
        {
            var t = new string((text ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()
                .Replace('l', 'i').Replace('1', 'i').Replace('0', 'o');
            return t.Length >= 7 && t.Length <= 11 && EditDistance(t, "inventory") <= 2;
        }
        static int EditDistance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        /// Every "Inventory" title in the text reader's lines (a single word, or a whole short line such as
        /// "Inven tory"), as a box in the read image's pixels.
        public static List<OcrWord> InventoryTitles(IEnumerable<IList<OcrWord>> lines)
        {
            var found = new List<OcrWord>();
            foreach (var line in lines)
            {
                var hits = line.Where(w => IsInventoryTitle(w.Text)).ToList();
                if (hits.Count == 0 && line.Count > 1 && line.Count <= 3 && IsInventoryTitle(string.Concat(line.Select(w => w.Text))))
                {
                    double x0 = line.Min(w => w.X), y0 = line.Min(w => w.Y), x1 = line.Max(w => w.X + w.W), y1 = line.Max(w => w.Y + w.H);
                    hits.Add(new OcrWord(string.Concat(line.Select(w => w.Text)), x0, y0, x1 - x0, y1 - y0));
                }
                found.AddRange(hits);
            }
            return found;
        }

        /// Where the grid should be, given the title's box: below the title, about 8 slots wide and centred
        /// under it, slot size ~ 3.3 x the text height. The box is loose on purpose (FindGrid copes with that).
        public static (int x, int y, int w, int h, double pitch) TitleSearchBox(OcrWord title)
        {
            double pitch = TitlePitchRatio * title.H, cx = title.X + title.W / 2;
            return ((int)Math.Round(cx - 5 * pitch), (int)Math.Round(title.Y + title.H), (int)Math.Round(10 * pitch), (int)Math.Round(10 * pitch), pitch);
        }

        /// Finds the inventory grid under an "Inventory" title (title box in the image's pixels). null = no convincing grid there.
        public static Grid? FindGridBelowTitle(Img img, OcrWord title)
        {
            var (x, y, w, h, pitch) = TitleSearchBox(title);
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(img.W, x + w), y1 = Math.Min(img.H, y + h);
            if (x1 - x0 < 8 * pitch * (1 - TitleTolerance) || y1 - y0 < 8 * pitch * (1 - TitleTolerance)) return null;
            var g = FindGrid(img, x0, y0, x1 - x0, y1 - y0, 8, 8, pitch, TitleTolerance);
            return Plausible(img, g) && WeakestLine(img, g) >= MinWeakestLine ? g : (Grid?)null;
        }

        /// Pieces of at most max x max pixels (overlapping, so a title isn't cut in two) covering a w x h image;
        /// the text reader only takes images up to a certain size.
        public static List<(int x, int y, int w, int h)> Tiles(int w, int h, int max, int overlap)
        {
            var list = new List<(int, int, int, int)>();
            int step = Math.Max(1, max - overlap);
            for (int y = 0; y < h; y += step)
            {
                for (int x = 0; x < w; x += step)
                {
                    list.Add((x, y, Math.Min(max, w - x), Math.Min(max, h - y)));
                    if (x + max >= w) break;
                }
                if (y + max >= h) break;
            }
            return list;
        }

        /// Slot size saved in cabal-helper-farm.txt ("slot=76.9"). null when missing or not a plausible size.
        public static double? ParseSlotSize(string v) =>
            double.TryParse((v ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double p) && p >= MinPitch * 0.8 && p <= MaxPitch * 1.2 ? p : (double?)null;
        public static string FormatSlotSize(double p) => p.ToString("0.00", CultureInfo.InvariantCulture);

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
        /// Cleans an (enlarged) count strip for the text reader: dark digits on white. Keeps only bright, uncoloured
        /// pixels that have a dark pixel within reach, i.e. the outlined count text; the icon's shine is bright but
        /// has no outline and is dropped. `enlarge` is the strip's enlargement factor (outline reach scales with it).
        public static Img CleanDigits(Img src, int enlarge)
        {
            int W = src.W, H = src.H, reach = 2 * enlarge;
            var white = new bool[W * H]; var dark = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    src.Rgb(x, y, out int r, out int g, out int b);
                    int mn = Math.Min(r, Math.Min(g, b)), mx = Math.Max(r, Math.Max(g, b));
                    white[y * W + x] = mn >= 150 && mx - mn < 70;
                    dark[y * W + x] = mx < 80;
                }
            var outPx = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool keep = false;
                    if (white[y * W + x])
                        for (int dy = -reach; dy <= reach && !keep; dy++)
                            for (int dx = -reach; dx <= reach; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx >= 0 && ny >= 0 && nx < W && ny < H && dark[ny * W + nx]) { keep = true; break; }
                            }
                    byte v = keep ? (byte)0 : (byte)255; int i = (y * W + x) * 4;
                    outPx[i] = outPx[i + 1] = outPx[i + 2] = v; outPx[i + 3] = 255;
                }
            return new Img(W, H, outPx);
        }

        /// The count's digit glyphs in a slot's digit band: outlined white shapes of digit height, chained from the
        /// right edge leftwards (counts are right-aligned). Icon remains are farther left or the wrong height.
        public static List<Rectangle> LastComponents = new List<Rectangle>();
        public static List<Rectangle> CountGlyphs(Img band, double scale)
        {
            int W = band.W, H = band.H;
            var m = new bool[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) m[y * W + x] = DigitPx(band, x, y);
            // connected components (8-neighbour)
            var lab = new int[W * H]; var boxes = new List<Rectangle>(); int n = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!m[y * W + x] || lab[y * W + x] != 0) continue;
                    n++; int minX = x, maxX = x, minY = y, maxY = y;
                    var st = new Stack<(int, int)>(); st.Push((x, y)); lab[y * W + x] = n;
                    while (st.Count > 0)
                    {
                        var (px, py) = st.Pop();
                        minX = Math.Min(minX, px); maxX = Math.Max(maxX, px); minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx < 0 || ny < 0 || nx >= W || ny >= H || !m[ny * W + nx] || lab[ny * W + nx] != 0) continue;
                            lab[ny * W + nx] = n; st.Push((nx, ny));
                        }
                    }
                    boxes.Add(Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1));
                }
            // merge pieces that overlap in x (a digit can break into two)
            boxes = boxes.OrderBy(b => b.X).ToList();
            var merged = new List<Rectangle>();
            foreach (var b in boxes)
            {
                if (merged.Count > 0 && b.X <= merged[merged.Count - 1].Right) merged[merged.Count - 1] = Rectangle.Union(merged[merged.Count - 1], b);
                else merged.Add(b);
            }
            double digitH = 16 * scale, digitW = 11 * scale;
            LastComponents = merged;
            var digitLike = merged.Where(b => b.Height >= digitH * 0.7 && b.Height <= digitH * 1.35 && b.Width <= digitW * 1.3).OrderByDescending(b => b.Right).ToList();
            var chain = new List<Rectangle>();
            foreach (var b in digitLike)
            {
                if (chain.Count == 0) { chain.Add(b); continue; }
                var last = chain[chain.Count - 1];
                if (last.X - b.Right > digitW * 0.7) break;                         // gap: the count has ended
                if (Math.Abs(b.Bottom - last.Bottom) > digitH * 0.3) break;         // not on the same baseline
                chain.Add(b);
                if (chain.Count == 5) break;
            }
            chain.Reverse();                                                        // left to right
            return chain;
        }

        /// Where the count sits in a digit band: anchored on the rightmost clean digit glyph (the last digit never
        /// touches the icon), then fixed digit cells leftwards while each cell still holds a digit: enough outlined
        /// white pixels, looking like a digit shape, with a near-black outline and no colour tint (icon remains are
        /// tinted, grey-outlined and match no digit). Returns the cells, left to right.
        public static List<Rectangle> CountCells(Img band, double scale, IList<(char d, string cell)> templates)
        {
            var glyphs = CountGlyphs(band, scale);
            if (glyphs.Count == 0) return new List<Rectangle>();
            var last = glyphs[glyphs.Count - 1];
            int cw = (int)Math.Round(CW * scale), ch = (int)Math.Round(CH * scale);
            int right = last.Right, bottom = Math.Max(last.Bottom, ch);
            var cells = new List<Rectangle>();
            for (int k = 0; k < 5; k++)
            {
                var cell = new Rectangle(right - (k + 1) * cw, bottom - ch, cw, ch);
                if (cell.X < -cw / 2) break;
                if (!CellIsDigit(band, cell, templates)) break;
                cells.Insert(0, cell);
            }
            return cells;
        }

        public static bool CellIsDigit(Img band, Rectangle cell, IList<(char d, string cell)> templates)
        {
            int px = 0; double sat = 0, darkSum = 0; int darkN = 0;
            var chars = new char[CW * CH];
            for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int bx = cell.X + x * cell.Width / CW, by = cell.Y + y * cell.Height / CH;
                    bool d = DigitPx(band, bx, by);
                    chars[y * CW + x] = d ? '#' : '.';
                    if (!d) continue;
                    px++;
                    band.Rgb(bx, by, out int r, out int g, out int b);
                    sat += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        band.Rgb(bx + dx, by + dy, out int nr, out int ng, out int nb);
                        int mx = Math.Max(nr, Math.Max(ng, nb));
                        if (mx < 80) { darkSum += mx; darkN++; }
                    }
                }
            if (px < 6) return false;
            // (A colour/outline rule was tried here and rejected real digits over a bright icon shine; the shape match
            // alone tells digits from icon remains.)
            var best = templates.Where(t => t.d != '_').Min(t => CellDist(new string(chars), t.cell));
            return best <= DigitMatchMax;
        }

        /// Reads a count from its cells with the shape templates.
        public static int? ReadCountFromCells(Img band, IList<Rectangle> cells, IList<(char d, string cell)> templates)
        {
            if (cells.Count == 0) return null;
            var digits = new StringBuilder();
            foreach (var cell in cells)
            {
                var chars = new char[CW * CH];
                for (int y = 0; y < CH; y++) for (int x = 0; x < CW; x++) chars[y * CW + x] = DigitPx(band, cell.X + x * cell.Width / CW, cell.Y + y * cell.Height / CH) ? '#' : '.';
                var best = templates.Where(t => t.d != '_').Select(t => (t.d, dist: CellDist(new string(chars), t.cell))).OrderBy(t => t.dist).First();
                if (best.dist > DigitMatchMax) return null;
                digits.Append(best.d);
            }
            return int.Parse(digits.ToString(), CultureInfo.InvariantCulture);
        }

        /// Reads a count from its glyph boxes with the shape templates: each glyph is placed in a cell aligned
        /// to its own bottom-right corner, so the fixed cell positions no longer matter.
        public static int? ReadCountFromGlyphs(Img band, IList<Rectangle> glyphs, IList<(char d, string cell)> templates)
        {
            if (glyphs.Count == 0) return null;
            var digits = new StringBuilder();
            foreach (var gl in glyphs)
            {
                var chars = new char[CW * CH];
                int cx = gl.Right - CW + 1, cy = gl.Bottom - CH + 1;            // glyph's bottom-right in the cell's bottom-right
                for (int y = 0; y < CH; y++) for (int x = 0; x < CW; x++) chars[y * CW + x] = DigitPx(band, cx + x, cy + y) ? '#' : '.';
                string cell = new string(chars);
                var best = templates.Where(t => t.d != '_').Select(t => (t.d, dist: CellDist(cell, t.cell))).OrderBy(t => t.dist).First();
                if (best.dist > DigitMatchMax) return null;
                digits.Append(best.d);
            }
            return int.Parse(digits.ToString(), CultureInfo.InvariantCulture);
        }

        /// The strip of a slot where its stack count is drawn (image coordinates), for the text reader.
        public static Rectangle DigitBand(Grid g, int r, int c)
        {
            var (sx, sy) = SlotOrigin(g, r, c); double s = g.Scale;
            // digits are drawn at slot y 46..63 (2560x1440); the icon's frame ends at ~45, so start just below it
            return new Rectangle(sx + (int)Math.Round(14 * s), sy + (int)Math.Round(47 * s), (int)Math.Round(60 * s), (int)Math.Round(17 * s));
        }

        /// Maps text-reader words back to the digit bands they were cut from. Bands are stacked top to bottom in a
        /// strip, each `bandH` tall with `gap` between, and the strip was enlarged `enlarge` times before reading.
        /// Returns one count per band (null = nothing readable there).
        public static int?[] MapStripWords(IEnumerable<OcrWord> words, int bands, int bandH, int gap, int enlarge)
        {
            var result = new int?[bands];
            var text = new string[bands];
            foreach (var w in words.OrderBy(w => w.X))
            {
                double cy = (w.Y + w.H / 2) / enlarge;
                int i = (int)(cy / (bandH + gap));
                if (i < 0 || i >= bands || cy - i * (bandH + gap) > bandH) continue;
                text[i] = (text[i] ?? "") + w.Text;
            }
            for (int i = 0; i < bands; i++)
            {
                if (text[i] == null) continue;
                // keep digits; readers sometimes see l/I/| for 1 and O for 0
                var digits = new string(text[i].Select(ch => ch == 'l' || ch == 'I' || ch == '|' ? '1' : ch == 'O' || ch == 'o' ? '0' : ch).Where(char.IsDigit).ToArray());
                if (digits.Length >= 1 && digits.Length <= 5) result[i] = int.Parse(digits, CultureInfo.InvariantCulture);
            }
            return result;
        }

        public static bool DigitPxPublic(Img img, int x, int y) => DigitPx(img, x, y);

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

        /// Reads a stack count from the slot's digit band: cells anchored on the last digit, matched by shape.
        /// null = a cell didn't match any digit (e.g. an unlearned glyph); 1 = no count drawn (single item).
        public static int? ReadCount(Img img, Grid g, int r, int c, IList<(char d, string cell)> templates, List<string> cellsOut)
        {
            var band = CropImg(img, DigitBand(g, r, c));
            var cells = CountCells(band, g.Scale, templates);
            if (cells.Count == 0) return CountGlyphs(band, g.Scale).Count == 0 ? 1 : (int?)null;
            foreach (var cell in cells)
            {
                var chars = new char[CW * CH];
                for (int y = 0; y < CH; y++) for (int x = 0; x < CW; x++) chars[y * CW + x] = DigitPx(band, cell.X + x * cell.Width / CW, cell.Y + y * cell.Height / CH) ? '#' : '.';
                cellsOut?.Add(new string(chars));
            }
            return ReadCountFromCells(band, cells, templates);
        }

        /// The count region of a slot for the text reader: only the validated digit cells, as dark digits on white.
        /// Returns null when the band holds no readable count. cellCount = how many digits the reader must return.
        /// raw = true: the original pixels of the digit cells (the reader copes with light text on the icon; the
        /// outline mask would erase digit parts whose outline is lit by the icon). raw = false: outlined pixels only.
        public static Img CountStrip(Img img, Grid g, int r, int c, IList<(char d, string cell)> templates, out int cellCount, bool raw = true)
        {
            var band = CropImg(img, DigitBand(g, r, c));
            var cells = CountCells(band, g.Scale, templates);
            cellCount = cells.Count;
            if (cells.Count == 0) return null;
            var region = cells.Aggregate(Rectangle.Union);
            region.Inflate(2, 2);
            region = Rectangle.Intersect(region, new Rectangle(0, 0, band.W, band.H));
            if (raw) return CropImg(band, region);
            var px = new byte[region.Width * region.Height * 4];
            for (int y = 0; y < region.Height; y++)
                for (int x = 0; x < region.Width; x++)
                {
                    byte v = DigitPx(band, region.X + x, region.Y + y) ? (byte)0 : (byte)255;
                    int i = (y * region.Width + x) * 4; px[i] = px[i + 1] = px[i + 2] = v; px[i + 3] = 255;
                }
            return new Img(region.Width, region.Height, px);
        }

        public static Img CropImg(Img im, Rectangle r)
        {
            r = Rectangle.Intersect(r, new Rectangle(0, 0, im.W, im.H));
            var px = new byte[Math.Max(0, r.Width * r.Height * 4)];
            for (int y = 0; y < r.Height; y++) Buffer.BlockCopy(im.Px, ((r.Y + y) * im.W + r.X) * 4, px, y * r.Width * 4, r.Width * 4);
            return new Img(Math.Max(0, r.Width), Math.Max(0, r.Height), px);
        }

        /// A slot keeps its last identity while its icon still roughly resembles it (the shine moves the colour mix a
        /// little on every read); entering a new identity needs the strict match.
        public const double IconStayMax = 2.2;

        public static List<SlotRead> ReadInventory(Img img, Grid g, IList<(string name, double[] f)> icons, IList<(char d, string cell)> digits, int cols = 8, int rows = 8, IDictionary<(int r, int c), string> prior = null)
        {
            var list = new List<SlotRead>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var f = IconFeature(img, g, r, c);
                    double empty = Dist(f, EmptySlot);
                    var m = MatchIcon(f, icons);
                    if (m == null && prior != null && prior.TryGetValue((r, c), out var kept))
                    {
                        double dKept = icons.Where(t => t.name == kept).Select(t => Dist(f, t.f)).DefaultIfEmpty(double.MaxValue).Min();
                        if (dKept <= IconStayMax && dKept < empty) m = (kept, dKept);   // sticky: still looks like the same core
                    }
                    if (m == null || empty < m.Value.d) continue;                   // empty slot, unknown item, or ambiguous
                    var best = m.Value;
                    var s = new SlotRead { Row = r, Col = c, Item = best.name, IconDist = best.d };
                    s.Count = ReadCount(img, g, r, c, digits, s.Cells);
                    list.Add(s);
                }
            return list;
        }

        /// The slot frame has two bright lines ~8 px apart, so the snap can land on either one. Try the neighbours
        /// and keep the grid that reads best: most recognised cores, fewest unreadable counts, closest icons.
        // ---------- fixed UI anchors: the sword button under the inventory and the window's close cross ----------
        // Grey-level templates cut from a live 2560x1440 capture (2026-09-29). Both never animate and are unique on screen.
        public const int ButtonW = 66, ButtonH = 56, CrossW = 18, CrossH = 18;
        public static readonly byte[] ButtonTemplate = { 102,126,126,126,126,126,126,125,125,124,122,120,118,117,115,114,114,114,114,116,117,119,121,123,124,125,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,126,102,76,54,144,180,180,180,180,180,179,178,177,175,173,170,168,169,169,169,168,168,169,168,168,168,171,174,176,178,179,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,180,144,106,72,129,142,142,142,142,142,141,140,139,138,136,133,134,157,180,184,187,184,182,166,141,128,132,135,138,140,141,142,142,142,142,142,142,142,142,142,142,142,142,142,142,142,142,142,142,142,142,142,142,143,143,144,145,145,146,146,146,146,144,143,141,141,141,128,115,81,114,105,105,105,105,105,104,103,102,100,99,96,100,145,190,199,205,201,194,163,114,87,92,97,100,103,104,104,105,105,105,105,105,105,105,105,105,105,105,105,105,105,105,105,105,105,105,105,105,106,107,108,109,111,111,112,112,111,109,106,103,103,104,114,124,90,108,93,93,92,92,92,92,91,89,88,86,83,88,139,190,200,207,202,194,157,99,68,76,82,86,90,91,92,92,93,93,93,93,93,93,93,93,92,92,92,93,93,93,93,92,94,96,101,109,118,128,139,140,140,132,123,112,102,93,89,86,88,90,106,123,91,104,89,89,88,88,88,87,86,84,82,80,78,83,135,186,195,203,198,190,150,89,57,66,75,80,86,87,88,89,89,89,89,89,89,89,89,89,89,88,89,89,89,89,89,88,91,97,107,126,145,168,190,190,187,170,147,124,100,83,79,76,80,84,102,119,89,101,85,85,84,84,84,83,82,80,78,76,74,79,130,183,192,200,195,185,144,80,47,59,69,75,81,83,84,85,86,86,86,85,86,86,86,86,85,85,85,86,86,86,86,87,94,110,128,153,177,201,224,217,204,179,149,121,96,77,70,65,72,78,96,115,86,97,82,82,81,81,80,79,78,76,74,72,69,75,127,180,190,198,192,182,141,75,42,53,64,71,77,79,80,81,81,82,82,83,83,83,83,83,83,83,83,83,83,83,86,90,107,141,173,195,217,226,235,209,176,146,117,96,86,75,62,53,62,72,91,111,84,94,79,78,78,77,76,75,73,72,70,68,65,70,123,177,187,194,189,179,136,70,36,49,59,66,73,74,76,77,78,78,79,79,80,80,80,80,80,80,80,80,80,80,86,94,120,171,215,235,255,249,243,200,149,114,86,74,77,74,55,41,53,65,86,107,81,90,76,75,74,73,72,70,69,67,65,63,61,67,119,173,182,190,184,175,133,68,34,45,55,62,68,70,71,73,74,74,75,76,77,77,77,77,77,77,77,77,80,83,100,125,157,198,232,243,253,228,203,162,118,95,80,75,79,75,53,35,49,62,83,104,79,87,73,72,70,69,68,66,64,62,61,59,57,63,116,168,178,185,180,171,129,65,32,42,51,57,64,65,67,69,70,71,73,73,74,74,75,75,74,74,74,75,80,86,114,156,194,225,249,252,252,206,162,124,87,75,74,76,81,77,51,30,44,59,80,102,78,84,70,69,67,78,89,92,90,89,88,86,85,89,129,168,176,181,177,171,140,92,67,74,81,85,90,91,93,82,70,66,69,70,71,72,72,73,72,72,75,78,93,112,144,186,219,239,252,241,229,178,128,101,77,73,78,81,82,75,49,28,43,57,78,99,76,82,68,66,65,91,123,132,131,130,129,128,128,130,150,171,175,177,175,172,156,132,119,122,126,128,130,131,132,103,70,61,64,67,68,70,70,71,71,70,78,85,114,148,182,216,239,248,248,222,195,146,99,85,76,79,87,88,83,72,48,29,43,56,76,96,74,79,65,64,63,101,145,159,159,158,158,158,158,159,165,171,172,173,172,171,167,160,156,156,157,158,158,158,159,117,68,56,60,63,66,68,68,69,70,71,86,101,138,182,214,238,251,248,237,199,161,120,81,77,79,84,91,89,78,65,46,31,43,56,75,94,72,78,64,62,61,100,147,161,161,161,161,161,161,161,164,167,168,168,168,167,165,161,160,160,160,160,161,161,161,115,62,49,55,60,64,66,67,68,72,76,107,139,175,212,233,245,245,232,211,168,125,106,87,88,91,89,85,75,61,48,39,34,45,57,74,92,71,76,62,61,59,100,148,163,163,163,163,163,163,163,163,163,163,163,163,163,163,163,163,163,163,163,163,163,163,113,55,42,51,57,62,65,66,67,74,81,128,176,211,241,253,251,240,215,185,136,90,92,93,98,103,95,79,62,42,30,33,37,47,58,74,91,70,75,62,61,60,96,139,152,151,151,150,150,150,151,154,157,157,158,157,157,155,152,150,150,150,150,150,150,150,104,50,40,49,56,61,66,68,73,95,117,160,205,227,242,243,235,216,181,148,120,95,100,105,100,93,78,59,44,36,31,37,43,51,60,74,89,69,74,63,61,60,91,129,140,139,138,137,136,136,138,143,150,151,152,151,150,146,140,136,136,136,136,136,137,137,95,46,37,47,55,61,66,71,80,117,154,194,233,242,242,233,217,190,145,109,105,101,109,117,101,81,60,38,27,30,33,41,49,56,62,74,87,67,74,63,62,61,82,107,112,110,108,106,105,105,107,124,141,143,146,144,142,131,113,103,104,104,105,106,106,106,77,42,38,48,56,63,72,88,107,148,189,216,242,238,225,208,187,161,126,101,105,109,109,108,87,63,45,29,25,31,39,47,54,59,63,75,86,66,74,64,63,61,68,77,75,70,65,62,61,60,64,97,130,135,140,137,133,137,75,55,57,59,60,62,62,63,52,39,41,50,59,67,82,114,147,185,223,231,238,221,196,172,148,129,119,112,115,118,101,85,64,41,32,29,32,39,47,53,59,61,64,75,86,66,75,64,63,62,58,54,47,40,35,31,28,27,32,76,32,128,133,130,133,93,45,19,22,26,28,31,31,32,35,39,45,53,63,76,99,143,186,216,247,236,225,198,167,140,116,106,114,120,119,116,90,63,43,24,23,31,39,47,54,59,63,64,65,76,86,66,75,64,64,63,60,57,52,47,43,39,37,35,40,77,40,77,40,77,40,89,45,22,28,34,38,43,42,42,44,46,51,57,74,101,133,177,216,226,236,210,182,158,135,121,114,112,115,115,99,82,62,42,34,29,32,39,47,53,59,62,64,65,65,76,86,66,75,65,64,64,62,60,57,54,51,48,46,44,47,77,47,77,47,77,47,84,46,26,34,42,49,55,54,52,53,54,57,61,84,126,168,212,248,236,225,183,140,118,102,102,112,117,117,110,78,48,35,22,26,33,40,48,54,59,63,64,66,66,66,76,87,66,76,65,65,64,63,62,60,58,56,54,52,50,52,77,52,77,52,77,52,81,46,30,41,51,61,69,63,58,57,56,64,76,107,155,195,219,234,205,175,145,114,104,101,103,108,106,94,79,57,36,31,27,33,41,48,54,59,62,64,65,66,66,66,76,87,66,76,66,65,65,64,64,63,62,60,58,57,55,56,77,56,77,56,77,56,77,46,33,47,60,73,83,73,63,61,58,72,93,131,186,223,222,215,168,121,105,91,95,104,106,104,93,67,45,36,28,32,36,43,50,55,59,63,64,65,66,66,66,66,76,87,66,76,66,66,65,65,65,65,64,63,61,60,58,59,76,59,76,59,76,59,74,45,35,50,67,83,98,84,70,68,67,87,115,151,192,215,197,176,132,88,86,86,93,103,100,89,72,48,28,28,29,37,45,50,56,60,63,65,65,66,66,66,66,66,76,87,66,76,66,66,66,66,66,65,65,64,62,61,59,60,74,60,74,60,74,60,70,44,35,51,68,92,114,96,79,80,84,108,142,165,176,174,146,120,99,77,86,97,98,97,83,62,45,36,30,35,39,46,52,57,60,63,64,65,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,65,64,63,62,60,61,72,61,72,61,72,61,65,43,36,51,69,98,125,108,91,94,101,127,164,173,156,133,99,70,69,68,86,104,100,88,66,37,22,27,32,40,49,54,60,62,64,65,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,65,64,64,63,61,61,65,69,67,65,62,58,50,40,38,49,63,86,109,114,119,120,120,132,149,146,123,101,80,63,70,77,83,89,78,62,47,34,28,35,41,48,54,58,62,64,65,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,65,65,64,63,63,61,58,55,49,43,39,36,35,37,41,47,57,75,93,121,148,146,139,137,135,119,90,68,61,57,71,86,80,74,57,36,28,30,34,42,50,55,61,62,64,65,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,65,65,65,64,63,62,58,55,49,44,40,38,38,42,46,50,56,66,76,105,133,141,147,136,118,97,72,55,54,55,64,72,64,55,43,31,29,35,42,49,56,60,63,64,65,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,65,65,65,64,63,61,58,55,51,49,47,48,50,52,55,58,58,60,82,105,127,150,134,101,75,59,48,52,55,54,53,44,34,32,32,36,44,50,56,61,63,65,65,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,65,65,65,64,62,61,59,57,56,55,55,57,58,59,59,55,52,68,85,113,143,130,97,73,59,49,48,48,43,38,32,25,29,36,44,51,57,60,64,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,65,65,64,64,63,62,61,61,60,60,61,61,61,60,58,56,67,78,98,119,120,112,98,79,60,44,31,31,32,33,34,39,46,51,57,61,63,65,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,65,65,65,65,65,65,65,65,65,65,65,63,62,61,61,66,71,82,95,109,124,121,97,71,41,16,20,25,34,41,48,55,59,62,64,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,65,65,65,65,65,65,65,66,67,70,72,75,75,75,70,63,67,73,84,98,103,98,86,57,32,33,35,42,48,54,59,62,64,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,68,70,75,82,88,89,90,73,56,53,51,60,72,85,100,100,73,49,46,44,50,55,59,63,64,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,67,67,70,75,83,96,104,98,90,69,48,41,37,42,52,68,89,99,81,65,59,51,54,57,60,63,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,67,69,74,82,95,113,123,104,85,62,39,31,25,28,36,51,74,89,86,83,70,57,56,55,59,63,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,66,66,68,70,71,71,77,87,101,119,127,101,74,52,31,23,18,20,26,39,61,78,85,89,74,58,56,54,57,62,64,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,67,68,76,83,81,77,80,88,96,103,102,77,52,37,22,20,20,24,29,38,50,59,67,72,61,50,50,50,54,60,64,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,67,69,82,96,92,83,83,90,91,86,77,53,30,22,14,17,22,27,33,37,38,42,49,55,48,42,44,46,52,59,63,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,68,71,95,121,112,92,83,80,75,67,57,40,23,21,18,24,30,36,42,45,45,46,48,49,45,41,44,47,53,60,63,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,69,72,109,147,132,102,83,70,59,48,37,27,17,20,23,31,40,46,51,53,53,51,47,45,43,41,45,50,55,61,64,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,69,71,104,139,125,98,77,61,47,35,26,21,17,23,30,38,47,53,57,59,57,55,51,47,46,45,49,54,58,62,64,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,66,66,66,66,66,66,66,66,66,66,66,66,67,67,87,108,100,83,67,51,38,27,19,20,22,30,39,46,54,58,61,62,61,60,57,55,54,53,56,58,61,63,65,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,66,76,87,66,76,68,68,68,68,68,68,68,68,68,68,68,68,67,66,75,83,78,70,58,43,31,21,16,22,29,39,49,56,62,65,66,67,67,66,65,64,63,62,63,64,66,67,68,68,68,68,68,68,68,68,68,68,68,68,68,68,68,68,68,68,68,68,76,84,64,74,78,78,78,78,78,78,78,78,78,78,78,78,77,76,78,80,72,61,50,39,31,26,25,35,44,54,64,69,74,76,77,77,77,77,76,75,75,75,75,76,77,77,78,78,78,78,78,78,78,78,78,78,78,78,78,78,78,78,78,78,78,78,74,70,53,72,88,88,88,88,88,88,88,88,88,88,88,87,87,86,80,76,64,52,42,35,31,31,34,47,59,69,79,82,85,87,87,88,88,87,87,87,87,87,87,87,87,88,88,88,88,88,88,88,88,88,88,88,88,88,88,88,88,88,88,88,88,88,72,57,43,55,65,65,65,65,65,65,65,65,65,65,65,65,65,64,60,57,48,40,33,28,26,27,29,38,47,54,60,63,64,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,65,55,44,36,35,39,39,39,39,39,39,39,39,39,39,39,39,38,38,37,35,32,28,24,21,20,21,22,27,31,34,37,38,38,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,35,32,29,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,45,55,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,63,55,42,31 };
        public static readonly byte[] CrossTemplate = { 21,22,25,30,28,26,22,20,19,19,19,20,22,26,28,26,28,22,21,23,36,53,47,41,29,20,19,19,19,20,27,37,45,37,45,22,20,22,33,53,64,71,50,34,26,20,26,33,46,60,59,60,59,21,18,19,25,38,76,118,93,70,42,21,42,70,85,100,69,100,69,19,17,17,19,28,72,132,126,118,75,41,75,118,114,107,63,107,63,17,16,16,17,22,49,91,131,171,135,102,133,166,113,67,39,67,39,16,16,16,16,18,32,59,126,210,194,179,191,201,108,39,24,39,24,15,15,15,15,17,24,38,94,171,194,216,192,169,87,30,20,30,20,15,16,15,15,16,16,22,67,135,193,255,194,139,68,22,16,22,16,15,15,15,15,17,28,48,102,170,183,196,182,169,95,38,24,38,24,15,15,15,15,19,43,84,143,210,175,143,172,202,125,59,32,59,32,15,16,15,16,22,56,109,133,154,115,79,119,166,132,97,55,97,55,16,16,16,18,27,71,130,110,90,58,32,68,116,133,148,89,148,89,16,16,17,23,37,75,117,79,49,31,17,38,67,99,137,99,137,99,18,16,18,31,53,70,82,48,25,21,16,22,30,52,82,87,82,87,21,17,19,34,55,55,51,29,16,16,16,16,16,27,46,63,46,63,23,17,18,24,31,28,25,20,16,16,16,16,16,20,25,31,25,31,21,17,18,18,19,18,16,16,16,16,16,16,16,16,16,17,16,17,19 };
        // Grid origin relative to the button template's top-left, and the cross relative to the button (2560x1440 px).
        public const double ButtonToGridX = -6, ButtonToGridY = -636, ButtonToCrossX = 585, ButtonToCrossY = -737;

        /// Mean absolute grey difference between a template (scaled) and the image at (x, y); sampled every `step` px.
        public static double TemplateDiff(Img img, byte[] tpl, int tw, int th, int x, int y, double scale, int step = 1)
        {
            double sum = 0; int n = 0;
            for (int ty = 0; ty < th; ty += step)
                for (int tx = 0; tx < tw; tx += step)
                {
                    int px = x + (int)Math.Round(tx * scale), py = y + (int)Math.Round(ty * scale);
                    if (px < 0 || py < 0 || px >= img.W || py >= img.H) return double.MaxValue;
                    sum += Math.Abs(img.Lum(px, py) - tpl[ty * tw + tx]); n++;
                }
            return n == 0 ? double.MaxValue : sum / n;
        }

        /// Best template position inside a search rectangle: coarse pass every 4 px on a subsampled template, then a
        /// fine pass around the best coarse hit. Returns the top-left and the mean difference (lower is better).
        public static (Point at, double diff) FindTemplate(Img img, byte[] tpl, int tw, int th, Rectangle search, double scale)
        {
            search = Rectangle.Intersect(search, new Rectangle(0, 0, img.W, img.H));
            Point best = Point.Empty; double bestD = double.MaxValue;
            for (int y = search.Y; y < search.Bottom; y += 4)
                for (int x = search.X; x < search.Right; x += 4)
                { double d = TemplateDiff(img, tpl, tw, th, x, y, scale, 4); if (d < bestD) { bestD = d; best = new Point(x, y); } }
            if (bestD == double.MaxValue) return (best, bestD);
            Point fine = best; double fineD = double.MaxValue;
            for (int y = best.Y - 4; y <= best.Y + 4; y++)
                for (int x = best.X - 4; x <= best.X + 4; x++)
                { double d = TemplateDiff(img, tpl, tw, th, x, y, scale, 1); if (d < fineD) { fineD = d; fine = new Point(x, y); } }
            return (fine, fineD);
        }
        public const double ButtonGoodMax = 22, CrossGoodMax = 30;

        public sealed class AnchorResult
        {
            public bool Ok; public string Info = "";
            public Rectangle ButtonSearch, CrossSearch, ButtonAt, CrossAt;   // image coordinates
            public double ButtonDiff = double.MaxValue, CrossDiff = double.MaxValue;
            public double ScaleCheck;
        }

        /// Pins the grid to the sword button (and checks the scale with the close cross). Looks within about a slot of
        /// where the button should be for the given grid; if it isn't there, over the whole picture. ok = false when the
        /// button isn't found convincingly (then the given grid is returned unchanged).
        public static Grid AnchorByButtons(Img img, Grid g, out bool ok, out string info) { var r = AnchorByButtons(img, g, out var res); ok = res.Ok; info = res.Info; return r; }
        public static Grid AnchorByButtons(Img img, Grid g, out AnchorResult res)
        {
            res = new AnchorResult();
            double sc = g.Scale;
            int ex = (int)Math.Round(g.X - ButtonToGridX * sc), ey = (int)Math.Round(g.Y - ButtonToGridY * sc);
            int win = (int)Math.Round(g.PitchX);
            res.ButtonSearch = new Rectangle(ex - win, ey - win, 2 * win + (int)(ButtonW * sc), 2 * win + (int)(ButtonH * sc));
            var (bAt, bD) = FindTemplate(img, ButtonTemplate, ButtonW, ButtonH, new Rectangle(ex - win, ey - win, 2 * win + 1, 2 * win + 1), sc);
            if (bD > ButtonGoodMax)
            {
                var (bAt2, bD2) = FindTemplate(img, ButtonTemplate, ButtonW, ButtonH, new Rectangle(0, 0, img.W, img.H), sc);   // anywhere in the picture
                if (bD2 < bD) { bAt = bAt2; bD = bD2; res.ButtonSearch = new Rectangle(0, 0, img.W, img.H); }
            }
            res.ButtonAt = new Rectangle(bAt.X, bAt.Y, (int)(ButtonW * sc), (int)(ButtonH * sc)); res.ButtonDiff = bD;
            if (bD > ButtonGoodMax) { res.Ok = false; res.Info = $"button not found (best diff {bD:0.0}, limit {ButtonGoodMax})"; return g; }
            var r = g; r.X = bAt.X + ButtonToGridX * sc; r.Y = bAt.Y + ButtonToGridY * sc;
            int cx = (int)Math.Round(bAt.X + ButtonToCrossX * sc), cy = (int)Math.Round(bAt.Y + ButtonToCrossY * sc);
            res.CrossSearch = new Rectangle(cx - win, cy - win, 2 * win + (int)(CrossW * sc), 2 * win + (int)(CrossH * sc));
            var (xAt, xD) = FindTemplate(img, CrossTemplate, CrossW, CrossH, new Rectangle(cx - win, cy - win, 2 * win + 1, 2 * win + 1), sc);
            res.CrossAt = new Rectangle(xAt.X, xAt.Y, (int)(CrossW * sc), (int)(CrossH * sc)); res.CrossDiff = xD;
            res.ScaleCheck = (xAt.X - bAt.X) / (ButtonToCrossX * sc);
            string crossInfo = xD <= CrossGoodMax ? $"cross at {xAt.X},{xAt.Y} (diff {xD:0.0}, scale check {res.ScaleCheck:0.000})" : $"cross not found (diff {xD:0.0})";
            res.Ok = true; res.Info = $"button at {bAt.X},{bAt.Y} (diff {bD:0.0}) -> grid {r.X:0},{r.Y:0}; " + crossInfo;
            return r;
        }

        /// Finds the inventory anywhere on screen by its sword button (unique, never animates) at the given UI scale.
        public static Grid? LocateByButton(Img screen, double scale, out double diff)
        {
            var (at, d) = FindTemplate(screen, ButtonTemplate, ButtonW, ButtonH, new Rectangle(0, 0, screen.W, screen.H), scale);
            diff = d;
            if (d > ButtonGoodMax) return null;
            var g = new Grid { X = at.X + ButtonToGridX * scale, Y = at.Y + ButtonToGridY * scale, PitchX = RefPitch * scale, PitchY = RefPitch * scale };
            if (g.X < 0 || g.Y < 0) return null;
            g.Score = BorderContrast(screen, g);
            return g;
        }

        /// Score of a read: recognised cores, counts that came from real digits, icon closeness.
        public static double ReadScore(List<SlotRead> read)
        {
            int digitReads = read.Count(r => r.Count.HasValue && r.Cells.Count > 0);
            return read.Count * 10 + digitReads * 8 - read.Sum(r => r.IconDist) - read.Count(r => r.Count == 1 && r.Cells.Count == 0) * 3;
        }

        /// The slot frame has two bright lines ~8 px apart, so the snap can land on either one. Try the neighbours
        /// and keep the grid that reads best. With `keep` set (the grid used last time), that grid stays unless
        /// another position reads clearly better (SwitchMargin), so the grid doesn't twitch between reads.
        public const double SwitchMargin = 12;
        public static (Grid grid, List<SlotRead> slots) BestRead(Img img, Grid g, IList<(string name, double[] f)> icons, IList<(char d, string cell)> digits, Grid? keep = null)
        {
            (Grid, List<SlotRead>, double) best = (g, new List<SlotRead>(), double.MinValue);
            double step = SlotInset * g.Scale;
            var candidates = new List<Grid>();
            if (keep is Grid k) candidates.Add(k);
            foreach (var dx in new[] { 0.0, -step, step })
                foreach (var dy in new[] { 0.0, -step, step })
                { var cand = g; cand.X += dx; cand.Y += dy; candidates.Add(cand); }
            double keepScore = double.MinValue; List<SlotRead> keepRead = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var read = ReadInventory(img, candidates[i], icons, digits);
                double score = ReadScore(read);
                if (i == 0 && keep != null) { keepScore = score; keepRead = read; }
                if (score > best.Item3) best = (candidates[i], read, score);
            }
            if (keep != null && keepRead != null && best.Item3 < keepScore + SwitchMargin) return (keep.Value, keepRead);
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

        /// Smooths inventory reads over time: a stack count is the most common value of the last few reads,
        /// and a core only counts as missing after it has been absent in several reads in a row.
        public sealed class CountSmoother
        {
            public int Window = 5, MissingAfter = 3;
            readonly Dictionary<string, List<int>> recent = new Dictionary<string, List<int>>();
            readonly Dictionary<string, int> missingStreak = new Dictionary<string, int>();
            readonly HashSet<string> everSeen = new HashSet<string>();

            public void Clear() { recent.Clear(); missingStreak.Clear(); everSeen.Clear(); }

            /// Feed one read (name -> count for the cores found in it). Counts in `confident` (both readers agreed)
            /// replace that core's history, so they show at once; other values still need the recent majority.
            public void Add(IDictionary<string, int> read, ICollection<string> confident = null)
            {
                foreach (var kv in read)
                {
                    if (!recent.TryGetValue(kv.Key, out var l)) recent[kv.Key] = l = new List<int>();
                    if (confident != null && confident.Contains(kv.Key)) l.Clear();
                    l.Add(kv.Value); if (l.Count > Window) l.RemoveAt(0);
                    missingStreak[kv.Key] = 0; everSeen.Add(kv.Key);
                }
                foreach (var name in everSeen) if (!read.ContainsKey(name)) missingStreak[name] = (missingStreak.TryGetValue(name, out int m) ? m : 0) + 1;
            }

            /// Cores seen in the recent reads (not missing for MissingAfter reads) with their most common count.
            public Dictionary<string, int> Stable()
            {
                var d = new Dictionary<string, int>();
                foreach (var kv in recent)
                {
                    if (missingStreak.TryGetValue(kv.Key, out int m) && m >= MissingAfter) continue;
                    d[kv.Key] = Consensus(kv.Value);
                }
                return d;
            }

            /// Most common value, where a value that is the tail of a longer one (58 of 158: the icon shine can hide
            /// the leading digit, never add one) lends its votes to the longer value.
            public static int Consensus(IList<int> values)
            {
                var groups = values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
                int best = -1, bestVotes = -1;
                foreach (var v in groups.Keys)
                {
                    string sv = v.ToString(CultureInfo.InvariantCulture);
                    int votes = groups.Where(g => sv.EndsWith(g.Key.ToString(CultureInfo.InvariantCulture)) && sv.Length >= g.Key.ToString(CultureInfo.InvariantCulture).Length).Sum(g => g.Value);
                    if (votes > bestVotes || (votes == bestVotes && v > best)) { best = v; bestVotes = votes; }
                }
                return best;
            }

            /// Cores that were seen earlier but have been absent for MissingAfter reads or more.
            public List<string> Gone() => missingStreak.Where(kv => kv.Value >= MissingAfter).Select(kv => kv.Key).ToList();

            public int Reads => recent.Values.DefaultIfEmpty(new List<int>()).Max(l => l.Count);
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
