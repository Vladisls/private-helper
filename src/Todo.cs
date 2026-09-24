using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CAHelper
{
    public sealed class TodoItem
    {
        public string Name; public int Target; public bool Weekly; public int Count; public string Tip = "";
        public long MinCp; public int Value;
        public bool Action;      // quick daily action (vote, claims...): own collapsible section, resets daily
        public bool Done => Count >= Target;
        public string Key => (Weekly ? "W|" : "D|") + Name.Trim().ToLowerInvariant();
    }

    /// The to-do list file, progress file and daily/weekly reset rules. No UI, so it can be tested anywhere.
    public static class Todo
    {
        public static string ListPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-todo.txt");
        public static string BackupPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-todo.backup.txt");
        public static string ProfilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-profile.txt");

        /// Your CP as last entered in the panel; -1 when never set.
        public static long LoadCp()
        {
            try
            {
                if (File.Exists(ProfilePath))
                    foreach (var l in File.ReadAllLines(ProfilePath))
                        if (l.StartsWith("CP=") && long.TryParse(l.Substring(3), out long v)) return v;
            }
            catch { }
            return -1;
        }
        public static void SaveCp(long cp) { try { File.WriteAllText(ProfilePath, "CP=" + cp + "\n"); } catch { } }

        public static string ProgressPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-progress.txt");

        public static string DefaultList => TodoPresets.Catalog;

        /// Accepts 518000, 518,000, 518k, 1.1m, 0 (empty = not a number).
        public static bool TryParseCp(string text, out long cp)
        {
            cp = 0;
            var t = (text ?? "").Trim().ToLowerInvariant().Replace(",", "").Replace(" ", "").Replace("_", "");
            if (t.Length == 0) return false;
            double mult = 1;
            if (t.EndsWith("k")) { mult = 1e3; t = t.Substring(0, t.Length - 1); }
            else if (t.EndsWith("m")) { mult = 1e6; t = t.Substring(0, t.Length - 1); }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || d < 0 || d * mult > 1e9) return false;
            cp = (long)Math.Round(d * mult);
            return true;
        }

        public static string FormatCp(long cp) =>
            cp >= 1_000_000 ? (cp / 1e6).ToString(cp % 1_000_000 == 0 ? "0" : "0.0#", CultureInfo.InvariantCulture) + "M"
          : cp >= 1000 ? (cp / 1000).ToString(CultureInfo.InvariantCulture) + "k" : cp.ToString(CultureInfo.InvariantCulture);

        /// Open tasks you can do at `cp`, most valuable first (list order breaks ties). cp < 0 = CP not set, show all.
        public static List<TodoItem> Available(List<TodoItem> items, long cp, bool weekly, bool action = false) =>
            items.Select((it, i) => (it, i))
                 .Where(x => !x.it.Done && x.it.Weekly == weekly && x.it.Action == action && (cp < 0 || x.it.MinCp <= cp))
                 .OrderByDescending(x => x.it.Value).ThenBy(x => x.i)
                 .Select(x => x.it).ToList();

        /// Not done yet and above your CP, lowest requirement first.
        public static List<TodoItem> Locked(List<TodoItem> items, long cp) =>
            cp < 0 ? new List<TodoItem>() :
            items.Where(it => !it.Done && it.MinCp > cp).OrderBy(it => it.MinCp).ThenByDescending(it => it.Value).ToList();

        public static List<TodoItem> ParseList(string text, List<string> problems)
        {
            var list = new List<TodoItem>();
            var seen = new HashSet<string>();
            int lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                var p = line.Split(';');
                var item = new TodoItem { Name = p[0].Trim(), Target = 1 };
                if (item.Name.Length == 0) continue;
                if (p.Length > 1 && p[1].Trim().Length > 0)
                {
                    if (!int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) || t < 1 || t > 999)
                    { problems.Add($"Line {lineNo}: '{p[1].Trim()}' is not a count from 1 to 999"); t = 1; }
                    item.Target = t;
                }
                if (p.Length > 2)
                {
                    var period = p[2].Trim().ToLowerInvariant();
                    if (period == "weekly" || period == "week" || period == "w") item.Weekly = true;
                    else if (period == "action" || period == "daily action") item.Action = true;
                    else if (period.Length > 0 && period != "daily" && period != "day" && period != "d")
                        problems.Add($"Line {lineNo}: '{p[2].Trim()}' should be daily, action or weekly");
                }
                // New format: ; min CP ; value ; tip.  Old format: ; tip
                int next = 3;
                if (p.Length > 3 && TryParseCp(p[3], out long minCp))
                {
                    item.MinCp = minCp; next = 4;
                    if (p.Length > 4 && int.TryParse(p[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val)) { item.Value = val; next = 5; }
                }
                if (p.Length > next) item.Tip = string.Join(";", p, next, p.Length - next).Trim();
                if (!seen.Add(item.Key)) { problems.Add($"Line {lineNo}: '{item.Name}' is listed twice"); continue; }
                list.Add(item);
            }
            return list;
        }

        /// Id of the daily period `now` falls in, e.g. "2026-09-23" (the day whose reset time has passed most recently).
        public static string DailyId(DateTime now, TimeSpan resetTime)
            => (now - resetTime).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// Id of the weekly period: date of the most recent reset day at reset time.
        public static string WeeklyId(DateTime now, DayOfWeek resetDay, TimeSpan resetTime)
        {
            var shifted = (now - resetTime).Date;
            int back = ((int)shifted.DayOfWeek - (int)resetDay + 7) % 7;
            return "W" + shifted.AddDays(-back).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// Progress lines: key|periodId|count. Counts from an older period are dropped.
        public static void ApplyProgress(List<TodoItem> items, string progressText, string dailyId, string weeklyId)
        {
            var map = new Dictionary<string, (string period, int count)>();
            foreach (var raw in (progressText ?? "").Split('\n'))
            {
                var p = raw.Trim().Split('|');
                if (p.Length != 4) continue;                 // D|name|period|count  or W|name|period|count
                if (int.TryParse(p[3], out int c)) map[p[0] + "|" + p[1]] = (p[2], c);
            }
            foreach (var it in items)
            {
                it.Count = 0;
                if (map.TryGetValue(it.Key, out var v) && v.period == (it.Weekly ? weeklyId : dailyId))
                    it.Count = Math.Max(0, Math.Min(it.Target, v.count));
            }
        }

        public static string SerializeProgress(List<TodoItem> items, string dailyId, string weeklyId)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var it in items)
                if (it.Count > 0) sb.Append(it.Key).Append('|').Append(it.Weekly ? weeklyId : dailyId).Append('|').Append(it.Count).Append('\n');
            return sb.ToString();
        }
    }
}
