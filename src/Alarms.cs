using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CAHelper
{
    public sealed class Alarm
    {
        public string Title = "";
        public TimeSpan Time;                 // your PC's local time of day
        public bool[] Days = AllDays();       // index = (int)DayOfWeek, Sunday = 0
        public int WarnMinutes = 5;
        public bool Enabled = true;

        public static bool[] AllDays() => new[] { true, true, true, true, true, true, true };
        public bool EveryDay => Days.All(d => d);
        public string Key => Title.Trim().ToLowerInvariant() + "|" + Time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        /// Next time this alarm happens at or after `from` (the minute it rings counts as "now").
        public DateTime? Next(DateTime from)
        {
            if (!Days.Any(d => d)) return null;
            for (int i = 0; i <= 7; i++)
            {
                var day = from.Date.AddDays(i);
                var at = day + Time;
                if (Days[(int)day.DayOfWeek] && at >= from.AddMinutes(-1)) return at;
            }
            return null;
        }

        public string DaysText()
        {
            if (EveryDay) return "every day";
            if (Days.SequenceEqual(new[] { false, true, true, true, true, true, false })) return "Mon-Fri";
            if (Days.SequenceEqual(new[] { true, false, false, false, false, false, true })) return "weekends";
            var names = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            var order = new[] { 1, 2, 3, 4, 5, 6, 0 };
            return string.Join(",", order.Where(i => Days[i]).Select(i => names[i]));
        }
    }

    /// Alarm list file, parsing and the ring/warn rules. No UI, so it can be tested anywhere.
    public static class Alarms
    {
        public static string ListPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper-alarms.txt");

        public const string DefaultList =
@"# Cabal Helper alarms. One per line:  title ; hh:mm (your PC's time) ; days ; warn minutes ; on/off
# days: daily, weekdays, weekends, or a list like Mon,Wed,Fri
# Easier: use the Alarms panel (+ Add, click a row to edit).
GDG ; 19:30 ; daily ; 5 ; on
";

        static readonly string[] DayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

        public static List<Alarm> Parse(string text, List<string> problems)
        {
            var list = new List<Alarm>();
            int n = 0;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                n++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split(';').Select(x => x.Trim()).ToArray();
                var a = new Alarm { Title = p[0] };
                if (a.Title.Length == 0) continue;
                if (p.Length < 2 || !TryParseTime(p[1], out a.Time)) { problems.Add($"Line {n}: time should look like 19:30"); continue; }
                if (p.Length > 2 && p[2].Length > 0 && !TryParseDays(p[2], out a.Days)) problems.Add($"Line {n}: '{p[2]}' is not daily, weekdays, weekends or Mon,Tue,...");
                if (p.Length > 3 && p[3].Length > 0)
                {
                    if (int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && w >= 0 && w <= 240) a.WarnMinutes = w;
                    else problems.Add($"Line {n}: warn minutes '{p[3]}' should be 0-240");
                }
                if (p.Length > 4) a.Enabled = !(p[4].Equals("off", StringComparison.OrdinalIgnoreCase) || p[4] == "0" || p[4].Equals("false", StringComparison.OrdinalIgnoreCase));
                list.Add(a);
            }
            return list;
        }

        public static string Serialize(IEnumerable<Alarm> alarms)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in DefaultList.Split('\n').Where(l => l.StartsWith("#"))) sb.Append(l.TrimEnd()).Append("\r\n");
            foreach (var a in alarms)
                sb.Append(a.Title.Replace(";", ",")).Append(" ; ").Append(a.Time.ToString(@"hh\:mm", CultureInfo.InvariantCulture))
                  .Append(" ; ").Append(DaysSpec(a.Days)).Append(" ; ").Append(a.WarnMinutes).Append(" ; ").Append(a.Enabled ? "on" : "off").Append("\r\n");
            return sb.ToString();
        }

        public static bool TryParseTime(string v, out TimeSpan t)
            => TimeSpan.TryParseExact((v ?? "").Trim().Replace('.', ':'), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out t)
               && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1);

        public static bool TryParseDays(string v, out bool[] days)
        {
            days = new bool[7];
            var s = v.Trim().ToLowerInvariant();
            if (s == "daily" || s == "every day" || s == "all") { days = Alarm.AllDays(); return true; }
            if (s == "weekdays" || s == "mon-fri") { for (int i = 1; i <= 5; i++) days[i] = true; return true; }
            if (s == "weekends") { days[0] = days[6] = true; return true; }
            foreach (var part in s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = part.Length >= 3 ? part.Substring(0, 3) : part;
                int i = Array.IndexOf(DayNames, key);
                if (i < 0) { days = Alarm.AllDays(); return false; }
                days[i] = true;
            }
            return days.Any(d => d);
        }

        static string DaysSpec(bool[] d)
        {
            if (d.All(x => x)) return "daily";
            var order = new[] { 1, 2, 3, 4, 5, 6, 0 };
            return string.Join(",", order.Where(i => d[i]).Select(i => char.ToUpper(DayNames[i][0]) + DayNames[i].Substring(1)));
        }

        /// Server time of day for a local time, given "server = PC time + offset".
        public static TimeSpan ToServer(TimeSpan local, TimeSpan offset) => Wrap(local + offset);
        public static TimeSpan FromServer(TimeSpan server, TimeSpan offset) => Wrap(server - offset);
        static TimeSpan Wrap(TimeSpan t) { var m = ((int)t.TotalMinutes % 1440 + 1440) % 1440; return TimeSpan.FromMinutes(m); }

        public static string Countdown(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        }
    }

    public enum AlarmEvent { Warn, Ring }

    /// Decides when each alarm warns and rings. Each fires once per occurrence.
    public sealed class AlarmEngine
    {
        readonly HashSet<string> fired = new HashSet<string>();

        public List<(Alarm alarm, AlarmEvent ev, DateTime at)> Tick(IEnumerable<Alarm> alarms, DateTime now)
        {
            var events = new List<(Alarm, AlarmEvent, DateTime)>();
            foreach (var a in alarms)
            {
                if (!a.Enabled) continue;
                var at = a.Next(now);
                if (at == null) continue;
                var at0 = at.Value;
                string id = a.Key + "|" + at0.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                // Ring during the alarm's minute (catch-up window of 60 s, so a restart can't replay old alarms).
                if (now >= at0 && now < at0.AddMinutes(1))
                { if (fired.Add(id + "|ring")) events.Add((a, AlarmEvent.Ring, at0)); }
                else if (a.WarnMinutes > 0 && now >= at0.AddMinutes(-a.WarnMinutes) && now < at0)
                { if (fired.Add(id + "|warn")) events.Add((a, AlarmEvent.Warn, at0)); }
            }
            return events;
        }

        /// True while `now` is inside the alarm's warning window (for the blinking row).
        public static bool InWarning(Alarm a, DateTime now)
        {
            if (!a.Enabled || a.WarnMinutes <= 0) return false;
            var at = a.Next(now);
            return at != null && now >= at.Value.AddMinutes(-a.WarnMinutes) && now < at.Value.AddMinutes(1);
        }
    }
}
