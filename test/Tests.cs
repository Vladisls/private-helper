using System; using System.Collections.Generic; using CAHelper;
static class T {
  static int fail = 0;
  static void Check(bool ok, string name){ Console.WriteLine((ok?"PASS ":"FAIL ")+name); if(!ok) fail++; }
  static TimeSpan S(double s)=>TimeSpan.FromSeconds(s);
  static int Main(){
    var p = new List<string>();
    var s = Settings.Parse("", p);
    Check(s.ReentryWindow==S(540) && s.BossSpawnAfter==S(360) && s.FinalWarning==S(60) && p.Count==0, "defaults 9:00 / 6:00 / 1:00");
    var bad = Settings.Parse("ReentryWindow=9:00\nBossSpawnAfter=12:00\nFoo=1\nIdleAfter=abc", p=new List<string>());
    Check(bad.BossSpawnAfter==bad.ReentryWindow && p.Count==3, "clamps boss>window, flags unknown key and bad time ("+string.Join(" | ",p)+")");

    var tr = new AlertTracker(); var t0 = DateTime.Now;
    AlertState E(double el, double idle, bool focus, double at, Settings st=null) => tr.Evaluate(S(el), S(idle), focus, st??s, t0.AddSeconds(at));

    var st0 = tr.Evaluate(null, S(0), true, s, t0);
    Check(st0.Phase==Phase.Off, "off before hotkey");
    var a = E(120, 100, false, 120);
    Check(a.Phase==Phase.Counting && !a.Flash && !a.Beep && a.ToBoss==S(240), "2:00 in: waiting for boss, AFK is fine, 4:00 to boss");
    a = E(360, 60, true, 360);
    Check(a.Phase==Phase.BossUp && a.Flash && a.Beep && a.Left==S(180), "6:00 boss spawns while AFK: flash + beep, 3:00 to re-enter");
    a = E(361, 61, true, 361);
    Check(a.Flash && !a.Beep, "beeps throttled");
    a = E(362.5, 62, true, 362.5);
    Check(a.Beep, "beeps every 2 s while flashing");
    a = E(370, 1, true, 370);
    Check(!a.Flash && !a.Beep, "you move and the game is in front: flash stops");
    a = E(380, 1, false, 380);
    Check(a.Flash, "moving but browser in front: still flashes");
    var s2 = Settings.Parse("FlashWhenGameNotFocused=false", new List<string>());
    tr.Reset(); E(360,0,true,0,s2);
    a = E(380, 1, false, 20, s2);
    Check(!a.Flash, "browser rule can be switched off");
    tr.Reset();
    a = E(365, 1, true, 500); 
    Check(a.Beep && !a.Flash, "boss up while fighting: one beep, no flash");
    a = E(470, 1, true, 600);
    Check(!a.LastCall && !a.Flash, "7:50 still fighting: no flash yet");
    a = E(480, 1, true, 610);
    Check(a.LastCall && a.Flash && a.Beep, "8:00 last call: flashes and beeps even while fighting");
    a = E(545, 1, true, 680);
    Check(a.Phase==Phase.Expired && a.Flash, "9:05 window closed: still flashing briefly");
    a = E(580, 1, true, 715);
    Check(a.Phase==Phase.Expired && !a.Flash, "30 s after close: flash stops");
    tr.Evaluate(null, S(0), true, s, t0);
    a = E(360, 0, true, 900);
    Check(a.Beep, "after restart the boss beep fires again");
    Check(AlertTracker.Format(S(65))=="1:05" && AlertTracker.Format(S(-7))=="0:07", "format mm:ss");
    // ---- Daily To-do ----
    var tp = new List<string>();
    var items = Todo.ParseList("# c\nCA1 runs ; 5 ; daily\nVote\nWeak Map Part ; 3 ; weekly\nBad ; x ; daily\nOops ; 2 ; monthly\nca1 RUNS ; 5", tp);
    Check(items.Count==5 && items[0].Target==5 && items[1].Target==1 && !items[1].Weekly && items[2].Weekly, "to-do list parses name ; count ; period");
    Check(tp.Count==3, "to-do flags bad count, bad period, duplicate ("+string.Join(" | ",tp)+")");
    var def = Todo.ParseList(Todo.DefaultList, tp=new List<string>());
    Check(def.Count>=10 && tp.Count==0, "default list parses cleanly ("+def.Count+" tasks)");
    var midnight = TimeSpan.Zero;
    Check(Todo.DailyId(new DateTime(2026,9,23,23,59,0), midnight)=="2026-09-23" && Todo.DailyId(new DateTime(2026,9,24,0,0,1), midnight)=="2026-09-24", "daily period flips at midnight");
    var six = new TimeSpan(6,0,0);
    Check(Todo.DailyId(new DateTime(2026,9,24,5,59,0), six)=="2026-09-23", "with 06:00 reset, 05:59 still counts as yesterday");
    // 2026-09-22 is a Tuesday
    Check(Todo.WeeklyId(new DateTime(2026,9,22,0,0,1), DayOfWeek.Tuesday, midnight)=="W2026-09-22", "weekly starts Tuesday");
    Check(Todo.WeeklyId(new DateTime(2026,9,28,23,0,0), DayOfWeek.Tuesday, midnight)=="W2026-09-22", "Monday is still last Tuesday's week");
    Check(Todo.WeeklyId(new DateTime(2026,9,29,0,0,1), DayOfWeek.Tuesday, midnight)=="W2026-09-29", "next Tuesday starts a new week");
    var a1 = Todo.ParseList("CA1 runs ; 5\nWeak Map Part ; 3 ; weekly", new List<string>());
    a1[0].Count=3; a1[1].Count=2;
    var saved = Todo.SerializeProgress(a1, "2026-09-23", "W2026-09-22");
    var a2 = Todo.ParseList("CA1 runs ; 5\nWeak Map Part ; 3 ; weekly", new List<string>());
    Todo.ApplyProgress(a2, saved, "2026-09-23", "W2026-09-22");
    Check(a2[0].Count==3 && a2[1].Count==2, "progress survives a restart");
    Todo.ApplyProgress(a2, saved, "2026-09-24", "W2026-09-22");
    Check(a2[0].Count==0 && a2[1].Count==2, "next day: daily resets, weekly kept");
    var a3 = Todo.ParseList("CA1 runs ; 2", new List<string>());
    Todo.ApplyProgress(a3, saved, "2026-09-23", "W2026-09-22");
    Check(a3[0].Count==2 && a3[0].Done, "lowering a target clamps the count and marks it done");
    // ---- Catalog, CP filter, sorting ----
    var cpp = new List<string>(); var cat = Todo.ParseList(TodoPresets.Catalog, cpp);
    Check(cpp.Count==0 && cat.Count>=25 && cat.TrueForAll(i => i.Tip.Length > 10 && i.Value > 0), $"catalog: {cat.Count} tasks, all with value + tip {string.Join(" | ",cpp)}");
    Check(TodoPresets.NameOf(TodoPresets.Catalog)=="Catalog v1", "catalog header");
    Check(Todo.TryParseCp("518k", out var c1) && c1==518000 && Todo.TryParseCp("1.1m", out var c2) && c2==1100000
          && Todo.TryParseCp("518,000", out var c3) && c3==518000 && !Todo.TryParseCp("abc", out _), "CP parses 518k / 1.1m / 518,000");
    Check(Todo.FormatCp(518000)=="518k" && Todo.FormatCp(1100000)=="1.1M" && Todo.FormatCp(1000000)=="1M", "CP formats");
    var at518 = Todo.Available(cat, 518000, weekly:false);
    Check(!at518.Exists(i => i.Name.StartsWith("Awakened IC") || i.Name.StartsWith("EOP") || i.Name.StartsWith("Frozen")), "518k: no 550k+ dungeons in the list");
    Check(at518[0].Name.StartsWith("Vote") && at518.FindIndex(i=>i.Name=="CA1 runs") < at518.FindIndex(i=>i.Name=="Altar of Sienna B1F")
          && at518.FindIndex(i=>i.Name=="Altar of Sienna B1F") < at518.FindIndex(i=>i.Name=="Hazardous Valley"), "518k: quick dailies, then CA, then farms by value");
    var locked518 = Todo.Locked(cat, 518000);
    Check(locked518.Count>0 && locked518[0].MinCp==550000 && locked518[locked518.Count-1].MinCp==1100000, "locked list: 550k first, Frozen Canyon 1.1M last");
    var at600 = Todo.Available(cat, 600000, weekly:false);
    Check(at600.FindIndex(i=>i.Name.StartsWith("EOP")) >= 0 && at600.FindIndex(i=>i.Name.StartsWith("EOP")) < at600.FindIndex(i=>i.Name=="Hazardous Valley"), "600k: EOP unlocked, ranked above Hazardous Valley");
    Check(Todo.Available(cat, -1, false).Count == cat.FindAll(i=>!i.Weekly).Count, "CP not set: show everything");
    var old = Todo.ParseList("CA1 runs ; 5 ; daily ; Only for the milestone", new List<string>())[0];
    Check(old.MinCp==0 && old.Tip=="Only for the milestone", "old 4-field lines still read the tip");
    Check(TodoPresets.IsOutdated("# preset: 500k-1M CP\nx;1") && !TodoPresets.IsOutdated(TodoPresets.Catalog) && !TodoPresets.IsOutdated("# preset: Custom\nx;1")
          && TodoPresets.IsOutdated("# preset: Catalog v0\nx;1"), "old bracket lists are replaced, custom lists kept");
    // ---- Settings save ----
    var sv = Settings.Parse("", new List<string>()); sv.BossSpawnAfter = S(330); sv.Sound = false; sv.WeeklyResetDay = DayOfWeek.Friday; sv.DailyResetTime = new TimeSpan(6,30,0);
    var rp = new List<string>(); var back = Settings.Parse(sv.ToIni(), rp);
    Check(rp.Count==0 && back.BossSpawnAfter==S(330) && !back.Sound && back.WeeklyResetDay==DayOfWeek.Friday && back.DailyResetTime==new TimeSpan(6,30,0), "settings save and load round-trip");
    // ---- Updater ----
    string sha = new string('a', 64);
    Check(Updater.TryParseInfo("2.1.0\r\n"+sha+"\r\n", out var v1, out var h1) && v1==new Version(2,1,0) && h1==sha, "version.txt parses (CRLF ok)");
    Check(!Updater.TryParseInfo("2.1.0", out _, out _) && !Updater.TryParseInfo("abc\n"+sha, out _, out _) && !Updater.TryParseInfo("2.1.0\nshort", out _, out _), "bad version.txt is rejected");
    Check(new Version(2,1,0) > new Version(2,0,9) && !(new Version(2,1,0) > new Version(2,1,0)), "only newer versions update");
    Check(Updater.Short(new Version(2,1,3,0))=="2.1.3", "version shown as 2.1.3");
    Console.WriteLine(fail==0?"ALL PASS":fail+" FAILED"); return fail;
  }
}
