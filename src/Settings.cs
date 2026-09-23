using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CAHelper
{
    public class Settings
    {
        public TimeSpan ReentryWindow = TimeSpan.FromMinutes(9);
        public TimeSpan BossSpawnAfter = TimeSpan.FromMinutes(6);
        public TimeSpan FinalWarning = TimeSpan.FromMinutes(1);
        public TimeSpan IdleAfter = TimeSpan.FromSeconds(15);
        public bool FlashWhenGameNotFocused = true;
        public string GameMatch = "cabal";
        public TimeSpan DailyResetTime = TimeSpan.Zero;
        public DayOfWeek WeeklyResetDay = DayOfWeek.Tuesday;
        public bool AutoUpdate = true;
        public TimeSpan ServerTimeOffset = TimeSpan.FromHours(-1);   // server clock = PC clock + this
        public string UpdateUrl = "";
        public bool FlashWhenActive = false;
        public bool Sound = true;
        public string RestartHotkey = "Ctrl+F9";
        public string StopHotkey = "Ctrl+F10";

        public static string IniPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cabal-helper.ini");

        const string DefaultIni =
@"; Cabal Helper settings. Edit, save, then tray icon > Reload settings.
; Times are minutes:seconds.
;
; CA Runner (two-channel Chaos Arena): open CA on channel 2, change server to channel 3 and press
; Start (or the hotkey) right when the game says you have N minutes to re-enter. Open the 2nd CA there.

; Re-entry window of the 1st CA, as shown by the game when you change channel
ReentryWindow=9:00
; When the 2nd CA boss spawns, counted from the hotkey press (its dungeon clock hits 14:00)
BossSpawnAfter=6:00
; With this much re-entry time left, flash even while you are fighting
FinalWarning=1:00

; You count as ""not moving"" after this long without mouse or keyboard input
IdleAfter=0:15
; Also flash when another window (browser, Discord...) is in front of the game
FlashWhenGameNotFocused=true
; Text that identifies the game window: part of its process name or window title
GameMatch=cabal
; true = flash from boss spawn on even while you are playing
FlashWhenActive=false
; Alert sound
Sound=true

; Start / restart the helper you used last (opens a CA Runner if none is open)
RestartHotkey=Ctrl+F9
; Reset it (e.g. once you are back in the 1st CA)
StopHotkey=Ctrl+F10

; Daily To-do: when daily tasks reset (24h clock, your PC's time) and which day weekly tasks reset
DailyResetTime=00:00
WeeklyResetDay=Tuesday

; Alarms: server clock minus your PC clock, e.g. -1:00 when the server shows 18:30 while your PC shows 19:30
ServerTimeOffset=-1:00

; Update from GitHub (Vladisls/private-helper, main branch) every time the helper starts
AutoUpdate=true
";

        /// "-1:00", "+2:30", "0", "-1"
        public static bool TryParseOffset(string v, out TimeSpan off)
        {
            off = TimeSpan.Zero;
            var t = (v ?? "").Trim().Replace(" ", "");
            if (t.Length == 0) return false;
            int sign = 1;
            if (t[0] == '-' || t[0] == '+') { if (t[0] == '-') sign = -1; t = t.Substring(1); }
            var p = t.Split(':');
            if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) || h > 14) return false;
            int m = 0;
            if (p.Length == 2 && (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m) || m < 0 || m >= 60)) return false;
            if (p.Length > 2) return false;
            off = TimeSpan.FromMinutes(sign * (h * 60 + m));
            return true;
        }
        public static string FormatOffset(TimeSpan o) => (o < TimeSpan.Zero ? "-" : "+") + $"{(int)Math.Abs(o.TotalHours)}:{Math.Abs(o.Minutes):00}";

        static string T(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        static string B(bool b) => b ? "true" : "false";

        /// The settings as ini text (same layout and comments as the default file).
        public string ToIni()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ReentryWindow"] = T(ReentryWindow), ["BossSpawnAfter"] = T(BossSpawnAfter), ["FinalWarning"] = T(FinalWarning),
                ["IdleAfter"] = T(IdleAfter), ["FlashWhenGameNotFocused"] = B(FlashWhenGameNotFocused), ["GameMatch"] = GameMatch,
                ["FlashWhenActive"] = B(FlashWhenActive), ["Sound"] = B(Sound), ["RestartHotkey"] = RestartHotkey, ["StopHotkey"] = StopHotkey,
                ["DailyResetTime"] = DailyResetTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture), ["WeeklyResetDay"] = WeeklyResetDay.ToString(),
                ["AutoUpdate"] = B(AutoUpdate), ["ServerTimeOffset"] = FormatOffset(ServerTimeOffset),
            };
            var sb = new System.Text.StringBuilder();
            foreach (var raw in DefaultIni.Replace("\r", "").Split('\n'))
            {
                int eq = raw.IndexOf('=');
                string key = eq > 0 && !raw.StartsWith(";") ? raw.Substring(0, eq) : null;
                sb.Append(key != null && values.TryGetValue(key, out var v) ? key + "=" + v : raw).Append("\r\n");
            }
            if (!string.IsNullOrWhiteSpace(UpdateUrl)) sb.Append("UpdateUrl=").Append(UpdateUrl).Append("\r\n");
            return sb.ToString().TrimEnd() + "\r\n";
        }

        public void Save() => File.WriteAllText(IniPath, ToIni());

        public static Settings Load(out List<string> problems)
        {
            problems = new List<string>();
            if (!File.Exists(IniPath))
            {
                try { File.WriteAllText(IniPath, DefaultIni); }
                catch (Exception e) { problems.Add("Could not create ca-helper.ini: " + e.Message); }
            }
            string text = File.Exists(IniPath) ? File.ReadAllText(IniPath) : DefaultIni;
            return Parse(text, problems);
        }

        public static Settings Parse(string text, List<string> problems)
        {
            var s = new Settings();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "reentrywindow": Time(v, ref s.ReentryWindow, k, problems); break;
                    case "bossspawnafter": Time(v, ref s.BossSpawnAfter, k, problems); break;
                    case "finalwarning": Time(v, ref s.FinalWarning, k, problems); break;
                    case "flashwhengamenotfocused": Bool(v, ref s.FlashWhenGameNotFocused, k, problems); break;
                    case "gamematch": s.GameMatch = v; break;
                    case "autoupdate": Bool(v, ref s.AutoUpdate, k, problems); break;
                    case "servertimeoffset":
                        if (TryParseOffset(v, out var off)) s.ServerTimeOffset = off;
                        else problems.Add($"{k}: '{v}' should look like -1:00, +2:00 or 0");
                        break;
                    case "updateurl": s.UpdateUrl = v; break;
                    case "dailyresettime":
                        if (TimeSpan.TryParseExact(v, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var rt) && rt < TimeSpan.FromDays(1)) s.DailyResetTime = rt;
                        else problems.Add($"{k}: '{v}' is not a time like 00:00 or 06:30");
                        break;
                    case "weeklyresetday":
                        if (Enum.TryParse(v, true, out DayOfWeek dw) && Enum.IsDefined(typeof(DayOfWeek), dw)) s.WeeklyResetDay = dw;
                        else problems.Add($"{k}: '{v}' is not a weekday like Tuesday");
                        break;
                    case "idleafter": Time(v, ref s.IdleAfter, k, problems); break;
                    case "flashwhenactive": Bool(v, ref s.FlashWhenActive, k, problems); break;
                    case "sound": Bool(v, ref s.Sound, k, problems); break;
                    case "restarthotkey": s.RestartHotkey = v; break;
                    case "stophotkey": s.StopHotkey = v; break;
                    case "hudposition": break;   // no longer used; helpers are dragged into place
                    default: problems.Add("Unknown setting: " + k); break;
                }
            }
            if (s.BossSpawnAfter > s.ReentryWindow) { problems.Add("BossSpawnAfter is later than ReentryWindow; using ReentryWindow."); s.BossSpawnAfter = s.ReentryWindow; }
            if (s.FinalWarning > s.ReentryWindow) { problems.Add("FinalWarning is longer than ReentryWindow; using ReentryWindow."); s.FinalWarning = s.ReentryWindow; }
            return s;
        }

        public static bool TryParseTime(string v, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            var p = v.Split(':');
            if (p.Length == 2 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)
                              && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sec)
                              && m >= 0 && sec >= 0 && sec < 60)
            { t = new TimeSpan(0, m, sec); return true; }
            if (p.Length == 1 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int onlySec) && onlySec >= 0)
            { t = TimeSpan.FromSeconds(onlySec); return true; }
            return false;
        }

        static void Time(string v, ref TimeSpan field, string k, List<string> problems)
        {
            if (TryParseTime(v, out var t)) field = t; else problems.Add($"{k}: '{v}' is not mm:ss");
        }
        static void Bool(string v, ref bool field, string k, List<string> problems)
        {
            var l = v.ToLowerInvariant();
            if (l == "true" || l == "1" || l == "yes") field = true;
            else if (l == "false" || l == "0" || l == "no") field = false;
            else problems.Add($"{k}: '{v}' is not true/false");
        }
    }
}
