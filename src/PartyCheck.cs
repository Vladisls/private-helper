using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CAHelper
{
    /// A word the text reader found, with its box in image pixels.
    public struct OcrWord
    {
        public string Text; public double X, Y, W, H;
        public OcrWord(string t, double x, double y, double w, double h) { Text = t; X = x; Y = y; W = w; H = h; }
    }

    public sealed class PartyRead
    {
        public List<string> Names = new List<string>();
        public int? Count;        // "member (19/25)" -> 19, if the header was inside the box
        public int? Capacity;     // -> 25
    }

    public enum MatchKind { Exact, Probable, New }

    public sealed class PartyResult
    {
        public List<(string read, string roster, MatchKind kind)> Seen = new List<(string, string, MatchKind)>();
        public List<string> Missing = new List<string>();
        public int? Count;
        public IEnumerable<string> NewNames => Seen.Where(s => s.kind == MatchKind.New).Select(s => s.read);
        public IEnumerable<(string read, string roster)> Probable => Seen.Where(s => s.kind == MatchKind.Probable).Select(s => (s.read, s.roster));
        /// Names that could not be read because they were hidden (e.g. behind the chat box).
        public int Hidden => Count.HasValue ? Math.Max(0, Count.Value - Seen.Count) : 0;
        public bool AllGood => !NewNames.Any() && Missing.Count == 0 && Hidden == 0;
    }

    /// Turns text-reader output into player names and compares them with the fixed roster.
    public static class PartyCheck
    {
        static readonly Regex CountRx = new Regex(@"\(\s*(\d{1,2})\s*[/l|I1]\s*(\d{1,2})\s*\)");

        /// lines: the reader's lines, each a list of words (any order).
        public static PartyRead Extract(IEnumerable<IList<OcrWord>> lines)
        {
            var read = new PartyRead();
            foreach (var line in lines)
            {
                string lineText = string.Join(" ", line.Select(w => w.Text));
                var m = CountRx.Match(lineText.Replace(" ", ""));
                if (m.Success) { read.Count = int.Parse(m.Groups[1].Value); read.Capacity = int.Parse(m.Groups[2].Value); continue; }
                if (lineText.IndexOf("member", StringComparison.OrdinalIgnoreCase) >= 0) continue;   // header without a readable count

                // Drop HP numbers and class-icon specks, then glue words that sit close together
                // (the reader sometimes splits one name, e.g. "Bir dTreeCircle").
                var words = line.Where(w => !IsJunk(w.Text)).OrderBy(w => w.X).ToList();
                var token = new StringBuilder(); OcrWord? prev = null;
                foreach (var w in words)
                {
                    if (prev != null)
                    {
                        double gap = w.X - (prev.Value.X + prev.Value.W);
                        double h = Math.Max(1, (w.H + prev.Value.H) / 2);
                        if (gap > h * 0.6) { Flush(token, read.Names); }
                    }
                    token.Append(w.Text);
                    prev = w;
                }
                Flush(token, read.Names);
            }
            return read;
        }

        static bool IsJunk(string t)
        {
            var s = t.Trim();
            if (s.Length <= 1) return true;                                   // icon read as "X"
            if (s.Contains("/")) return true;                                 // 14795/14795
            int digits = s.Count(char.IsDigit);
            return digits * 2 > s.Length;                                     // mostly digits: HP pieces
        }

        static void Flush(StringBuilder token, List<string> names)
        {
            var clean = new string(token.ToString().Where(char.IsLetterOrDigit).ToArray());
            token.Clear();
            if (clean.Length >= 2 && !IsJunk(clean) && !names.Contains(clean)) names.Add(clean);
        }

        /// Folds letters the reader confuses in this font: I/l/1/i, O/0, rn/m, vv/w, 5/S.
        public static string Key(string name)
        {
            var s = (name ?? "").ToLowerInvariant().Replace("rn", "m").Replace("vv", "w");
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                switch (c)
                {
                    case 'i': case '1': case '|': case '!': sb.Append('l'); break;
                    case '0': sb.Append('o'); break;
                    case '5': sb.Append('s'); break;
                    default: if (char.IsLetterOrDigit(c)) sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        public static PartyResult Compare(IList<string> roster, PartyRead read)
        {
            var result = new PartyResult { Count = read.Count };
            var left = roster.ToList();
            var pending = new List<string>();
            // Exact (after folding look-alike letters) first, so a near-match can't steal someone's slot.
            foreach (var n in read.Names)
            {
                var hit = left.FirstOrDefault(r => Key(r) == Key(n));
                if (hit != null) { result.Seen.Add((n, hit, MatchKind.Exact)); left.Remove(hit); }
                else pending.Add(n);
            }
            foreach (var n in pending)
            {
                var k = Key(n);
                var best = left.Select(r => (r, d: Distance(Key(r), k))).OrderBy(x => x.d).FirstOrDefault();
                int allowed = k.Length >= 8 ? 2 : k.Length >= 5 ? 1 : 0;
                if (best.r != null && best.d <= allowed) { result.Seen.Add((n, best.r, MatchKind.Probable)); left.Remove(best.r); }
                else result.Seen.Add((n, null, MatchKind.New));
            }
            result.Missing = left;
            return result;
        }
    }
}
