using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CAHelper
{
    public sealed class TodoItem
    {
        public string Name; public int Target; public bool Weekly; public int Count; public string Tip = "";
        public bool Done => Count >= Target;
        public string Key => (Weekly ? "W|" : "D|") + Name.Trim().ToLowerInvariant();
    }

    /// The to-do list file, progress file and daily/weekly reset rules. No UI, so it can be tested anywhere.
    public static class Todo
    {
        public static string ListPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-todo.txt");
        public static string BackupPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-todo.backup.txt");
        public static string ProgressPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-progress.txt");

        public static string DefaultList => TodoPresets.Default;

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
                    else if (period.Length > 0 && period != "daily" && period != "day" && period != "d")
                        problems.Add($"Line {lineNo}: '{p[2].Trim()}' should be daily or weekly");
                }
                if (p.Length > 3) item.Tip = string.Join(";", p, 3, p.Length - 3).Trim();
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
