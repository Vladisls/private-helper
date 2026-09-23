using System; using System.Collections.Generic; using System.Linq; using CAHelper;
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
    Check(TodoPresets.NameOf(TodoPresets.Catalog)=="Catalog v3" && !cat.Exists(i => i.Name.StartsWith("Buy dungeon entries") || i.Name.StartsWith("White Gold")), "catalog header");
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
          && TodoPresets.IsOutdated("# preset: Catalog v2\nx;1"), "old bracket lists are replaced, custom lists kept");
    // ---- Settings save ----
    var sv = Settings.Parse("", new List<string>()); sv.BossSpawnAfter = S(330); sv.Sound = false; sv.WeeklyResetDay = DayOfWeek.Friday; sv.DailyResetTime = new TimeSpan(6,30,0);
    var rp = new List<string>(); var back = Settings.Parse(sv.ToIni(), rp);
    Check(rp.Count==0 && back.BossSpawnAfter==S(330) && !back.Sound && back.WeeklyResetDay==DayOfWeek.Friday && back.DailyResetTime==new TimeSpan(6,30,0), "settings save and load round-trip");
    // ---- Alarms ----
    var ap = new List<string>(); var al = Alarms.Parse(Alarms.DefaultList, ap);
    Check(ap.Count==0 && al.Count==1 && al[0].Title=="GDG" && al[0].Time==new TimeSpan(19,30,0) && al[0].EveryDay && al[0].WarnMinutes==5, "default alarm: GDG 19:30 daily, warn 5");
    var gdg = al[0]; var wed = new DateTime(2026,9,23);   // a Wednesday
    Check(gdg.Next(wed.AddHours(12))==wed.AddHours(19.5) && gdg.Next(wed.AddHours(20))==wed.AddDays(1).AddHours(19.5), "next GDG: today before 19:30, tomorrow after");
    var fri = Alarms.Parse("Weak EoD ; 20:00 ; Fri,Sat,Sun ; 10 ; on", new List<string>())[0];
    Check(fri.Next(wed.AddHours(21))==wed.AddDays(2).AddHours(20) && fri.DaysText()=="Fri,Sat,Sun", "Fri-Sun alarm skips to Friday");
    Check(Alarms.ToServer(new TimeSpan(19,30,0), TimeSpan.FromHours(-1))==new TimeSpan(18,30,0)
          && Alarms.FromServer(new TimeSpan(18,30,0), TimeSpan.FromHours(-1))==new TimeSpan(19,30,0)
          && Alarms.ToServer(new TimeSpan(0,30,0), TimeSpan.FromHours(-1))==new TimeSpan(23,30,0), "PC 19:30 = server 18:30, wraps past midnight");
    var eng = new AlarmEngine(); var evs = new List<string>();
    foreach (var t in new[]{ "19:20","19:25:00","19:25:30","19:29:59","19:30:00","19:30:40","19:31:05" }) {
      var tt = TimeSpan.Parse(t.Length==5 ? t+":00" : t);
      foreach (var e in eng.Tick(al, wed + tt)) evs.Add(t+" "+e.ev);
    }
    Check(string.Join(", ", evs)=="19:25:00 Warn, 19:30:00 Ring", "warns once at 19:25, rings once at 19:30 ("+string.Join(", ", evs)+")");
    var eng2 = new AlarmEngine();
    Check(eng2.Tick(al, wed + new TimeSpan(19,31,10)).Count==0, "restart after 19:31 does not replay the alarm");
    Check(AlarmEngine.InWarning(gdg, wed + new TimeSpan(19,27,0)) && !AlarmEngine.InWarning(gdg, wed + new TimeSpan(19,20,0)), "row blinks only inside the warning window");
    var off = Alarms.Parse("X ; 07:00 ; weekdays ; 0 ; off", new List<string>())[0];
    Check(!off.Enabled && off.WarnMinutes==0 && off.DaysText()=="Mon-Fri" && new AlarmEngine().Tick(new[]{off}, wed.AddHours(7)).Count==0, "disabled alarm never rings");
    var badA = new List<string>(); Alarms.Parse("A ; 25:00\nB ; 9:00 ; someday\nC ; 9:00 ; daily ; 999", badA);
    Check(badA.Count==3, "bad time, days and warn are reported ("+string.Join(" | ",badA)+")");
    var round = Alarms.Parse(Alarms.Serialize(new[]{ gdg, fri, off }), new List<string>());
    Check(round.Count==3 && round[1].Title=="Weak EoD" && round[1].Days[5] && !round[1].Days[1] && !round[2].Enabled, "alarms save and load");
    Check(Settings.TryParseOffset("-1:00", out var o1) && o1==TimeSpan.FromHours(-1) && Settings.TryParseOffset("+2", out var o2) && o2==TimeSpan.FromHours(2)
          && !Settings.TryParseOffset("abc", out _) && Settings.FormatOffset(TimeSpan.FromHours(-1))=="-1:00", "server offset parses -1:00 / +2");
    var so = Settings.Parse("", new List<string>()); so.ServerTimeOffset = TimeSpan.FromMinutes(90);
    Check(Settings.Parse(so.ToIni(), new List<string>()).ServerTimeOffset==TimeSpan.FromMinutes(90) && Settings.Parse("", new List<string>()).ServerTimeOffset==TimeSpan.FromHours(-1), "offset saved; default -1:00");

    // ---- Party check (names from the 2026-09-23 GDG screenshot) ----
    OcrWord Wd(string t, double x, double y, double w=60, double h=14) => new OcrWord(t, x, y, w, h);
    var lines = new List<IList<OcrWord>> {
      new List<OcrWord>{ Wd("Flame",10,0,40), Wd("Dimension",55,0,70), Wd("/",130,0,5), Wd("member",140,0,50), Wd("(19/25)",195,0,40) },
      new List<OcrWord>{ Wd("Regnitiel",30,20), Wd("Conzine",230,20) },           // two columns on one line
      new List<OcrWord>{ Wd("14795/14795",40,36), Wd("14581/14581",240,36) },     // HP
      new List<OcrWord>{ Wd("X",5,60,8), Wd("netrl",30,60,35) },                  // class icon read as X
      new List<OcrWord>{ Wd("Bir",230,80,20), Wd("dTreeCircle",253,80,80) },       // one name split in two
      new List<OcrWord>{ Wd("DynaxWI",30,100) }, new List<OcrWord>{ Wd("CarTyTa",30,120) }, new List<OcrWord>{ Wd("GooFy994",30,140) },
      new List<OcrWord>{ Wd("5499/5543",40,156), Wd("1479514795",240,156) },
    };
    var pr = PartyCheck.Extract(lines);
    Check(string.Join(",", pr.Names)=="Regnitiel,Conzine,netrl,BirdTreeCircle,DynaxWI,CarTyTa,GooFy994" && pr.Count==19 && pr.Capacity==25,
          "names extracted, HP/icon dropped, split name glued, count 19/25 ("+string.Join(",", pr.Names)+")");
    // Real Windows-reader failures from the first GDG test (2026-09-23)
    var winLines = new List<IList<OcrWord>> {
      new List<OcrWord>{ Wd("GOOF",30,140,34), Wd("y994",68,140,30) },            // "GooFy994" split, digits kept
      new List<OcrWord>{ Wd("Re",30,20,18), Wd("nitiel",60,20,40) },              // gap ~0.85x text height
      new List<OcrWord>{ Wd("•",5,60,6), Wd("Csiribi",30,60,50), Wd("%",200,60,8), Wd("Conzine",230,60,55) },
      new List<OcrWord>{ Wd("11187;11191",40,76,80), Wd("229/11229",240,76,70) },
    };
    var wr = PartyCheck.Extract(winLines);
    Check(string.Join(",", wr.Names)=="GOOFy994,Renitiel,Csiribi,Conzine", "split names re-joined, icon specks and HP dropped ("+string.Join(",", wr.Names)+")");
    var fixA = new PartyRead{ Names = new List<string>{"A1","B2","C3"}, Count=4 };
    var fixB = new PartyRead{ Names = new List<string>{"Aaa","Bbb","Ccc","Ddd"}, Count=4 };
    var fixC = new PartyRead{ Names = new List<string>{"Aaa","Bbb","Ccc","Ddd","Junk"}, Count=4 };
    Check(PartyCheck.BestForFix(new[]{fixA, fixC, fixB}) == fixB, "Fix picks the read whose name count matches the window");

    var roster = new List<string>{ "Regnitiel","Conzine","DynaxWI","Doern","GooFy994","Butcher1","Melantha","BirdTreeCircle","netrl","CarTyTa" };
    var now1 = new PartyRead{ Names = new List<string>{ "CarTyTa","Dynaxwl","Regnitiel","GooFy994","netrl","Conzine","BirdTreeCircle","Doern","Butcher1","Melantha" }, Count=10 };
    var r1 = PartyCheck.Compare(roster, now1);
    Check(r1.AllGood && r1.Probable.Count()==0, "reshuffled party with I/l mixed up: all good");
    var now2 = new PartyRead{ Names = new List<string>{ "CarTyTa","DynaxWI","Regnitiel","GooFy994","netrl","Conzine","BirdTreeCirc1e","Doern","Butcher1","XyzSniper" }, Count=10 };
    var r2 = PartyCheck.Compare(roster, now2);
    Check(!r2.AllGood && r2.NewNames.SequenceEqual(new[]{"XyzSniper"}) && r2.Missing.SequenceEqual(new[]{"Melantha"}), "sniper found, Melantha missing");
    var now3 = new PartyRead{ Names = new List<string>{ "CarTyTa","DynaxWI","Regnitiel","GooFy994","netrl","Conzine","BirdTreeCirde","Doern","Butcher1","Melantha" }, Count=10 };
    var r3 = PartyCheck.Compare(roster, now3);
    Check(r3.AllGood && r3.Probable.Count()==1 && r3.Probable.First().roster=="BirdTreeCircle", "misread 'BirdTreeCirde' counts as probable BirdTreeCircle");
    var now4 = new PartyRead{ Names = roster.Take(7).ToList(), Count=10 };
    var r4 = PartyCheck.Compare(roster, now4);
    Check(!r4.AllGood && r4.Hidden==3, "3 names hidden (count 10, read 7): not validated");
    Check(PartyCheck.Compare(new List<string>{"Doern"}, new PartyRead{ Names=new List<string>{"Doen"} }).NewNames.Count()==1, "short names need an exact match");
    var badRead = new PartyRead{ Names = new List<string>{ "Re","nitiel","EConzine","VD","axW1","Doern","FGButherl","GOOF","rxBirdTreeCircle","Melantha" }, Count=10 };
    Check(PartyCheck.BestForValidate(roster, new[]{ badRead, now1 }) == now1, "Validate picks the read that agrees with the roster");

    // ---- Updater ----
    string sha = new string('a', 64);
    Check(Updater.TryParseInfo("2.1.0\r\n"+sha+"\r\n", out var v1, out var h1) && v1==new Version(2,1,0) && h1==sha, "version.txt parses (CRLF ok)");
    Check(!Updater.TryParseInfo("2.1.0", out _, out _) && !Updater.TryParseInfo("abc\n"+sha, out _, out _) && !Updater.TryParseInfo("2.1.0\nshort", out _, out _), "bad version.txt is rejected");
    Check(new Version(2,1,0) > new Version(2,0,9) && !(new Version(2,1,0) > new Version(2,1,0)), "only newer versions update");
    Check(Updater.Short(new Version(2,1,3,0))=="2.1.3", "version shown as 2.1.3");
    Console.WriteLine(fail==0?"ALL PASS":fail+" FAILED"); return fail;
  }
}
