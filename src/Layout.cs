using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace CAHelper
{
    /// Remembers where the launcher and each helper sit, and which helpers were open.
    static class Layout
    {
        public static string PathFile => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-layout.txt");

        public static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(PathFile))
                    foreach (var line in File.ReadAllLines(PathFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
            }
            catch { }
            return d;
        }

        public static void Save(Dictionary<string, string> d)
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in d) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(PathFile, lines);
            }
            catch { }
        }

        public static Point? GetPoint(Dictionary<string, string> d, string key)
        {
            if (!d.TryGetValue(key, out var v)) return null;
            var p = v.Split(',');
            if (p.Length == 2 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                              && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                return new Point(x, y);
            return null;
        }

        public static string Pt(Point p) => p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture);
    }
}
