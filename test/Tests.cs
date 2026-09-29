using System; using System.Collections.Generic; using System.Linq; using CAHelper;
static class T {
  static int fail = 0;
  static void Check(bool ok, string name){ Console.WriteLine((ok?"PASS ":"FAIL ")+name); if(!ok) fail++; }
  static TimeSpan S(double s)=>TimeSpan.FromSeconds(s);
  static bool FarrmSafe() => FarmCheck.TrailingCount("")==null && FarmCheck.TrailingCount("2 34")==34;
  /// Dark (grey under 128) 8-connected components of a picture, and the same merged where they overlap in x (CountGlyphs' glyphs).
  static (int comps, int glyphs) BlackComponents(Img p){ var seen = new bool[p.W * p.H]; var st = new Stack<int>(); var spans = new List<(int l, int r)>();
    for (int i = 0; i < seen.Length; i++) { if (seen[i] || p.Px[i*4] >= 128) continue; int l = i % p.W, r = l; seen[i] = true; st.Push(i);
      while (st.Count > 0) { int k = st.Pop(), x = k % p.W, y = k / p.W; l = Math.Min(l, x); r = Math.Max(r, x);
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int nx = x + dx, ny = y + dy; if (nx < 0 || ny < 0 || nx >= p.W || ny >= p.H) continue; int j = ny * p.W + nx; if (!seen[j] && p.Px[j*4] < 128) { seen[j] = true; st.Push(j); } } }
      spans.Add((l, r)); }
    int glyphs = 0, right = -1; foreach (var sp in spans.OrderBy(q => q.l)) { if (sp.l > right) glyphs++; right = Math.Max(right, sp.r); }
    return (spans.Count, glyphs); }
  /// Rows holding dark (under 128) pixels in a picture: the glyph height; and the smallest distance of a dark pixel to the edge.
  static (int h, int margin) DarkRows(Img p){ int y0 = p.H, y1 = -1, m = int.MaxValue;
    for (int y = 0; y < p.H; y++) for (int x = 0; x < p.W; x++) if (p.Px[(y * p.W + x) * 4] < 128) { y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); m = Math.Min(m, Math.Min(Math.Min(x, p.W - 1 - x), Math.Min(y, p.H - 1 - y))); }
    return (y1 < 0 ? 0 : y1 - y0 + 1, m); }
  /// Grey levels used in a picture (a smooth enlargement has many, a pixel-repeated binary mask two).
  static int GreyLevels(Img p){ var seen = new HashSet<byte>(); for (int i = 0; i < p.W * p.H; i++) seen.Add(p.Px[i * 4]); return seen.Count; }
  /// Bicubic resampling (Keys, a = -0.5) with the kernel widened by the shrink factor (a low-pass filter, like an
  /// image editor's bicubic): simulates the game drawn at a smaller UI scale.
  static Img ResizeBicubic(Img src, double f){
    int W = (int)Math.Round(src.W * f), H = (int)Math.Round(src.H * f);
    double K(double t){ t = Math.Abs(t); return t <= 1 ? (1.5 * t - 2.5) * t * t + 1 : t < 2 ? ((-0.5 * t + 2.5) * t - 4) * t + 2 : 0; }
    (int[] idx, double[] w, int taps) Wt(int n, int m){ double sc = (double)n / m, ks = Math.Max(1, sc); int taps = (int)Math.Ceiling(4 * ks) + 1; var idx = new int[m * taps]; var w = new double[m * taps];
      for (int x = 0; x < m; x++) { double u = (x + 0.5) * sc - 0.5; int i0 = (int)Math.Floor(u - 2 * ks) + 1; double sum = 0;
        for (int t = 0; t < taps; t++) { double ww = K((i0 + t - u) / ks); idx[x * taps + t] = Math.Min(n - 1, Math.Max(0, i0 + t)); w[x * taps + t] = ww; sum += ww; }
        for (int t = 0; t < taps; t++) w[x * taps + t] /= sum; }
      return (idx, w, taps); }
    var (hx, hw, ht) = Wt(src.W, W); var (vy, vw, vt) = Wt(src.H, H);
    var tmp = new double[src.H * W * 3];
    for (int y = 0; y < src.H; y++) for (int x = 0; x < W; x++) { double b = 0, g = 0, r = 0; for (int t = 0; t < ht; t++) { int si = (y * src.W + hx[x * ht + t]) * 4; double ww = hw[x * ht + t]; b += ww * src.Px[si]; g += ww * src.Px[si + 1]; r += ww * src.Px[si + 2]; } int o = (y * W + x) * 3; tmp[o] = b; tmp[o + 1] = g; tmp[o + 2] = r; }
    var px = new byte[W * H * 4]; byte C(double v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)Math.Round(v);
    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { double b = 0, g = 0, r = 0; for (int t = 0; t < vt; t++) { int o = (vy[y * vt + t] * W + x) * 3; double ww = vw[y * vt + t]; b += ww * tmp[o]; g += ww * tmp[o + 1]; r += ww * tmp[o + 2]; } int d = (y * W + x) * 4; px[d] = C(b); px[d + 1] = C(g); px[d + 2] = C(r); px[d + 3] = 255; }
    return new Img(W, H, px); }
#if DEBUG
  const bool DebugBuild = true;
#else
  const bool DebugBuild = false;
#endif
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
    Check(TodoPresets.NameOf(TodoPresets.Catalog)=="Catalog v15" && cat.Find(i => i.Name=="Steamer Crazy").Target==30 && cat.Find(i => i.Name=="Holy Windmill").Tip.StartsWith("Secret chest: a boy spawns") && !cat.Exists(i => i.Name.StartsWith("Buy dungeon entries") || i.Name.StartsWith("White Gold") || i.Name=="Wing dungeon") && cat.Exists(i => i.Name=="Holy Windmill" && i.MinCp==0) && cat.Exists(i => i.Name=="Holy Shrine" && i.MinCp == 640000) && cat.Exists(i => i.Name=="Holy Keldrasil" && i.MinCp == 560000), "catalog header");
    Check(Todo.TryParseCp("518k", out var c1) && c1==518000 && Todo.TryParseCp("1.1m", out var c2) && c2==1100000
          && Todo.TryParseCp("518,000", out var c3) && c3==518000 && !Todo.TryParseCp("abc", out _), "CP parses 518k / 1.1m / 518,000");
    Check(Todo.FormatCp(518000)=="518k" && Todo.FormatCp(1100000)=="1.1M" && Todo.FormatCp(1000000)=="1M", "CP formats");
    var at518 = Todo.Available(cat, 518000, weekly:false);
    Check(!at518.Exists(i => i.MinCp > 518000) && !at518.Exists(i => i.Name.StartsWith("Awakened IC") || i.Name.StartsWith("EOP") || i.Name.StartsWith("Frozen Canyon")), "518k: no 550k+ dungeons in the list");
    var acts = Todo.Available(cat, 518000, weekly:false, action:true);
    Check(acts.Count==6 && acts[0].Name=="Vote" && acts.TrueForAll(i => i.Action && !i.Weekly) && !at518.Exists(i => i.Action),
          "daily actions in their own list: " + string.Join(", ", acts.ConvertAll(i => i.Name)));
    Check(Todo.ParseList("X ; 1 ; action", new List<string>())[0].Action && Todo.ParseList("X ; 1 ; action", new List<string>())[0].Key=="D|x", "'action' parses and keeps the daily progress key");
    Check(at518[0].Name=="Mission War" && at518.FindIndex(i=>i.Name=="CA1 runs") < at518.FindIndex(i=>i.Name=="Steamer Crazy")
          && at518.FindIndex(i=>i.Name=="Steamer Crazy") < at518.FindIndex(i=>i.Name=="Nearly Hatching Egg")
          && at518.FindIndex(i=>i.Name=="Nearly Hatching Egg") < at518.FindIndex(i=>i.Name=="Ever-heated Lava Stone (Awakened)")
          && at518.FindIndex(i=>i.Name=="Hazardous Valley") < at518.FindIndex(i=>i.Name=="Altar of Sienna B1F")
          && at518.FindIndex(i=>i.Name=="Altar of Sienna B1F") < at518.FindIndex(i=>i.Name=="Altar of Sienna B2F"),
          "518k dungeons follow Chunky's tiers: S (Steamer, Egg) > A (Awakened DX) > Hazardous > B (Sienna): " + string.Join(" > ", at518.ConvertAll(i => i.Name)));
    var at590 = Todo.Available(cat, 590000, weekly:false);
    int P(string n) => at590.FindIndex(i => i.Name==n);
    Check(P("Ever-heated Lava Stone (Awakened)") < P("Frozen Clue (Awakened)") && P("Frozen Clue (Awakened)") < P("Panic Cave (Awakened)")
          && P("Panic Cave (Awakened)") < P("Hazardous Valley") && P("Altar of Sienna B2F") < P("Steamer Crazy (Awakened)")
          && P("Steamer Crazy (Awakened)") < P("Nearly Hatching Egg (Awakened)"), "ADX order: Ever-heated > Frozen Clue > Panic Cave > ... > Steamer (A) > Egg (A)");
    var locked518 = Todo.Locked(cat, 518000);
    Check(locked518.Count>0 && locked518[0].MinCp==550000 && locked518[locked518.Count-1].MinCp>=900000, "locked list: 550k first, 900k+ last");
    var at600 = Todo.Available(cat, 600000, weekly:false);
    Check(at600.FindIndex(i=>i.Name.StartsWith("EOP")) >= 0 && at600.FindIndex(i=>i.Name.StartsWith("EOP")) < at600.FindIndex(i=>i.Name=="Hazardous Valley"), "600k: EOP unlocked, ranked above Hazardous Valley");
    Check(Todo.Available(cat, -1, false).Count == cat.FindAll(i=>!i.Weekly && !i.Action).Count, "CP not set: show everything");
    var old = Todo.ParseList("CA1 runs ; 5 ; daily ; Only for the milestone", new List<string>())[0];
    Check(old.MinCp==0 && old.Tip=="Only for the milestone", "old 4-field lines still read the tip");
    Check(TodoPresets.IsOutdated("# preset: 500k-1M CP\nx;1") && !TodoPresets.IsOutdated(TodoPresets.Catalog) && !TodoPresets.IsOutdated("# preset: Custom\nx;1")
          && TodoPresets.IsOutdated("# preset: Catalog v14\nx;1"), "old bracket lists are replaced, custom lists kept");
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

    // ---- Farm Tracker (real 2026-09-28 screenshots; skipped if test/data is missing) ----
    Img Load(string f) { var b = System.IO.File.ReadAllBytes(f); int w = BitConverter.ToInt32(b, 0), h = BitConverter.ToInt32(b, 4); var px = new byte[b.Length - 8]; Buffer.BlockCopy(b, 8, px, 0, px.Length); return new Img(w, h, px); }
    Img Crop(Img im, int x, int y, int w, int h) { var px = new byte[w*h*4]; for (int r = 0; r < h; r++) Buffer.BlockCopy(im.Px, ((y+r)*im.W + x)*4, px, r*w*4, w*4); return new Img(w, h, px); }
    string dataDir = System.IO.Path.Combine(AppContext.BaseDirectory, "../../../data");
    if (System.IO.File.Exists(System.IO.Path.Combine(dataDir, "inv.bin"))) {
      var inv = Load(System.IO.Path.Combine(dataDir, "inv.bin"));
      var end = Load(System.IO.Path.Combine(dataDir, "end.bin"));
      var tmr = Load(System.IO.Path.Combine(dataDir, "timer.bin"));
      var icons = FarmCheck.Icons.ToList();
      // counts come from the text reader only (not run here): check the recognised cores, in order, and that every
      // core slot has count glyphs for the reader
      string expect = "Upgrade Core (Ultimate),Upgrade Core (Highest),Upgrade Core (High),Upgrade Core (Medium),Upgrade Core (Low),Force Core (Ultimate),Force Core (Highest),Force Core (High),Force Core (Medium),Force Core (Low)";
      foreach (var (bx,by,bw,bh) in new[]{ (1912,248,612,612), (1905,240,620,615), (1918,255,608,608), (1900,245,616,612) }) {
        var g = FarmCheck.FindGrid(inv, bx, by, bw, bh);
        var rd0 = FarmCheck.BestRead(inv, g, icons).slots;
        var got = string.Join(",", rd0.Select(x => x.Item));
        Check(got == expect && rd0.All(x => x.GlyphCount > 0), $"inventory read from a rough box {bx},{by}: grid {g.X},{g.Y} pitch {g.PitchX:0.00} -> {got}; glyphs " + string.Join(",", rd0.Select(x => x.GlyphCount)));
      }
      // boxes around the WHOLE inventory window, as a player might drag them
      foreach (var (bx,by,bw,bh) in new[]{ (1890,130,660,900), (1880,180,670,720), (1895,230,640,640) }) {
        var gw = FarmCheck.FindGrid(inv, bx, by, bw, bh);
        var rdw = FarmCheck.BestRead(inv, gw, icons).slots;
        var gotw = string.Join(",", rdw.Select(x => x.Item));
        Check(gotw == expect && rdw.All(x => x.GlyphCount > 0) && Math.Abs(gw.X - 1919) <= 3 && Math.Abs(gw.Y - 253) <= 3, $"inventory read from a whole-window box {bx},{by},{bw}x{bh}: grid {gw.X:0},{gw.Y:0} pitch {gw.PitchX:0.00}");
      }
      // search the whole screen, as after a reset when the saved area is wrong
      var sw = System.Diagnostics.Stopwatch.StartNew();
      var found = FarmCheck.LocateInventory(inv); long ms = sw.ElapsedMilliseconds;
      var none1 = FarmCheck.LocateInventory(end); var none2 = FarmCheck.LocateInventory(tmr);
      Console.WriteLine($"INFO locate: inventory shot -> {(found.HasValue ? $"{found.Value.X:0},{found.Value.Y:0} weakest {FarmCheck.WeakestLine(inv, found.Value):0.0}" : "none")} in {ms} ms; end shot -> {(none1.HasValue ? "FOUND" : "none")}; timer shot -> {(none2.HasValue ? "FOUND" : "none")}");
      Check(found.HasValue && Math.Abs(found.Value.X - 1919) <= 3 && Math.Abs(found.Value.Y - 253) <= 3 && !none1.HasValue && !none2.HasValue, "whole-screen search finds the inventory, and nothing when it's closed");
      // find the inventory by its "Inventory" title (the box the text reader would return; its height varies)
      foreach (var (tx,ty,tw,th) in new[]{ (2162.0,147.0,129.0,29.0), (2162.0,147.0,129.0,21.0), (2170.0,158.0,110.0,23.0) }) {
        var title = new OcrWord("Inventory", tx, ty, tw, th);
        var gtl = FarmCheck.FindGridBelowTitle(inv, title);
        Check(gtl.HasValue && Math.Abs(gtl.Value.X - 1919) <= 3 && Math.Abs(gtl.Value.Y - 253) <= 3 && Math.Abs(gtl.Value.PitchX - 76.9) < 1 && Math.Abs(gtl.Value.PitchY - 76.9) < 1
              && !FarmCheck.FindGridBelowTitle(end, title).HasValue && !FarmCheck.FindGridBelowTitle(tmr, title).HasValue,
              $"grid found below a {tw}x{th} title at {tx},{ty}: {(gtl.HasValue ? $"{gtl.Value.X:0},{gtl.Value.Y:0} pitch {gtl.Value.PitchX:0.00}" : "none")}; nothing on the closed shots");
      }
      // a wrong cached grid is searched again instead of reporting "closed"
      var right = FarmCheck.FindGrid(inv, 1905, 240, 620, 615);
      int searches = 0;
      var keep = FarmCheck.CurrentGrid(inv, right, () => { searches++; return right; }, out bool rep1);
      var wrong = new Grid{ X = 1880, Y = 300, PitchX = 68, PitchY = 68 };
      var fixedG = FarmCheck.CurrentGrid(inv, wrong, () => FarmCheck.FindGridNear(inv, 1905, 240, 620, 615, 68), out bool rep2);
      var closedG = FarmCheck.CurrentGrid(end, right, () => FarmCheck.FindGridNear(end, 1905, 240, 620, 615, 76.9), out bool rep3);
      Check(keep.HasValue && searches == 0 && !rep1, "cached grid still open: kept, no new search");
      Check(fixedG.HasValue && rep2 && Math.Abs(fixedG.Value.X - 1919) <= 3 && Math.Abs(fixedG.Value.Y - 253) <= 3 && Math.Abs(fixedG.Value.PitchX - 76.9) < 1,
            $"wrong cached grid (68 px): searched again and replaced ({(fixedG.HasValue ? $"{fixedG.Value.X:0},{fixedG.Value.Y:0} pitch {fixedG.Value.PitchX:0.00}" : "none")})");
      Check(!closedG.HasValue && !rep3, "cached grid on a closed inventory: closed after searching again");
      var gi = FarmCheck.FindGrid(inv, 1912, 248, 612, 612);
      Check(FarmCheck.InventoryOpen(inv, gi) && !FarmCheck.InventoryOpen(end, gi) && !FarmCheck.InventoryOpen(tmr, gi), "inventory-open check: yes on the inventory shot, no on the others");
      Check(FarmCheck.EndWindowLikely(Crop(end, 209, 284, 630, 745)) && !FarmCheck.EndWindowLikely(Crop(tmr, 209, 284, 630, 745)) && !FarmCheck.EndWindowLikely(Crop(inv, 209, 284, 630, 745)),
            "end-window trigger: yes on the end window, no in dungeon or with inventory");
      {
        // the trigger runs on the area inflated by 15% (clipped to the monitor), with a window-sized sliding check
        var mon = new System.Drawing.Rectangle(0, 0, end.W, end.H);
        var endA = new System.Drawing.Rectangle(209, 284, 630, 745);
        var inf = FarmCheck.InflateArea(endA, 0.15, mon);
        Img C(Img im, System.Drawing.Rectangle r) => Crop(im, r.X, r.Y, r.Width, r.Height);
        var offA = FarmCheck.InflateArea(new System.Drawing.Rectangle(209 + 40, 284 - 30, 630, 745), 0.15, mon);   // area a little off the window
        Console.WriteLine($"INFO end window, inflated capture {inf}: plain check {FarmCheck.EndWindowLikely(C(end, inf))}, window-sized check {FarmCheck.EndWindowLikely(C(end, inf), 630, 745)}");
        Check(FarmCheck.EndWindowLikely(C(end, inf), 630, 745) && FarmCheck.EndWindowLikely(C(end, offA), 630, 745)
              && !FarmCheck.EndWindowLikely(C(tmr, inf), 630, 745) && !FarmCheck.EndWindowLikely(C(inv, inf), 630, 745),
              "end-window trigger on the 15% larger capture: yes on the end window (also 40 px off its area), no in dungeon or with inventory");
        Check(FarmCheck.EndWindowLikely(C(end, endA), 630, 745) == FarmCheck.EndWindowLikely(C(end, endA)), "window-sized check without a margin = the plain check");
      }
      var ft = System.IO.Path.Combine(dataDir, "tab1.bin");
      if (System.IO.File.Exists(ft)) {
        var t1 = Load(ft);
        var gt = FarmCheck.FindGrid(t1, 1415, 245, 615, 385, 8, 5);
        var coreTab = FarmCheck.TabPrint(inv, gi);
        double same = FarmCheck.Dist(coreTab, FarmCheck.TabPrint(inv, new Grid{ X = gi.X + 1, Y = gi.Y + 1, PitchX = gi.PitchX, PitchY = gi.PitchY }));
        double other = FarmCheck.Dist(coreTab, FarmCheck.TabPrint(t1, gt));
        Console.WriteLine($"INFO tab strip: core tab vs itself (1 px shift) {same:0.0}, core tab vs tab I {other:0.0}, grid on tab-I shot {gt.X},{gt.Y} pitch {gt.PitchX:0.00}");
        Check(same < FarmCheck.TabMatchMax && other > FarmCheck.TabMatchMax, "tab check: core tab (VI) matches itself, tab I does not");
      }
      var fl = System.IO.Path.Combine(dataDir, "live172.bin");
      if (System.IO.File.Exists(fl)) {
        var live = Load(fl);
        // the helper drew its grid in pure green: take the grid from those lines
        var gx = new List<int>(); var gy = new List<int>();
        for (int x = 0; x < live.W; x++) { int n = 0; for (int y = 0; y < live.H; y++) { live.Rgb(x, y, out int r, out int g, out int b); if (g > 200 && r < 60 && b < 60) n++; } if (n > 300) gx.Add(x); }
        for (int y = 0; y < live.H; y++) { int n = 0; for (int x = 0; x < live.W; x++) { live.Rgb(x, y, out int r, out int g, out int b); if (g > 200 && r < 60 && b < 60) n++; } if (n > 300) gy.Add(y); }
        Console.WriteLine("INFO green lines x: " + string.Join(" ", gx) + " | y: " + string.Join(" ", gy));
        var glb = new Grid { X = gx.First(), Y = gy.First(), PitchX = (gx.Last() - gx.First()) / 8.0, PitchY = (gy.Last() - gy.First()) / 8.0 };
        var lr = FarmCheck.ReadInventory(live, glb, icons);
        Console.WriteLine("INFO live capture: grid " + $"{glb.X:0},{glb.Y:0} pitch {glb.PitchX:0.00}: " + string.Join(",", lr.Select(x => x.Item + " glyphs " + x.GlyphCount)));
        var hi = lr.FirstOrDefault(x => x.Item == "Upgrade Core (High)");
        Check(hi != null && hi.GlyphCount > 0, $"live capture: Upgrade Core (High) (172) found with count glyphs (got {hi?.GlyphCount.ToString() ?? "none"})");
      }
      var ffc = System.IO.Path.Combine(dataDir, "livefc.bin");
      if (System.IO.File.Exists(ffc)) {
        var lv = Load(ffc);
        var lx = new List<int>(); var ly = new List<int>();
        for (int x = 0; x < lv.W; x++) { int n = 0; for (int y = 0; y < lv.H; y++) { lv.Rgb(x, y, out int r, out int g, out int b); if (g > 200 && r < 60 && b < 60) n++; } if (n > 300) lx.Add(x); }
        for (int y = 0; y < lv.H; y++) { int n = 0; for (int x = 0; x < lv.W; x++) { lv.Rgb(x, y, out int r, out int g, out int b); if (g > 200 && r < 60 && b < 60) n++; } if (n > 300) ly.Add(y); }
        var lg = new Grid { X = lx.First(), Y = ly.First(), PitchX = (lx.Last() - lx.First()) / 8.0, PitchY = (ly.Last() - ly.First()) / 8.0 };
        foreach (var (name, c) in new[]{ ("Force Core (Medium)", 3), ("Force Core (Low)", 4) }) {
          var f = FarmCheck.IconFeature(lv, lg, 1, c);
          var ds = FarmCheck.Icons.Select(t => (t.name, d: FarmCheck.Dist(f, t.f))).OrderBy(t => t.d).ToList();
          Console.WriteLine($"LIVE {name}: nearest {ds[0].name} {ds[0].d:0.00}, 2nd {ds[1].name} {ds[1].d:0.00}; empty {FarmCheck.Dist(f, FarmCheck.EmptySlot):0.00}");
        }
        var liveRead = FarmCheck.ReadInventory(lv, lg, FarmCheck.DefaultIcons());
        Check(liveRead.Any(x => x.Item == "Force Core (Medium)" && x.GlyphCount > 0) && liveRead.Any(x => x.Item == "Force Core (Low)" && x.GlyphCount > 0), "live capture: FC Medium (16) and FC Low (2) found with the live icons, with their count glyphs");
      }
      var fmis = System.IO.Path.Combine(dataDir, "mis.bin");
      if (System.IO.File.Exists(fmis)) {
        var mi = Load(fmis);
        foreach (var ep in new[]{ 0.0 }) {
          var gm = FarmCheck.FindGrid(mi, 815, 160, 620, 620, 8, 8, ep);
          var rm = FarmCheck.BestRead(mi, gm, FarmCheck.DefaultIcons());
          Check(Math.Abs(gm.PitchX - 76.9) < 1 && Math.Abs(gm.X - 820) <= 3 && Math.Abs(gm.Y - 166) <= 3, "live screenshot: grid found without a resolution guess");
          Console.WriteLine($"MIS expected {ep}: grid {gm.X:0},{gm.Y:0} pitch {gm.PitchX:0.00}x{gm.PitchY:0.00} contrast {gm.Score:0.00} weakest {FarmCheck.WeakestLine(mi, gm):0.0} -> " + string.Join(",", rm.slots.Select(x => x.Item.Replace("Upgrade Core","UC").Replace("Force Core","FC") + " g" + x.GlyphCount)));
        }
      }
      // Shine / similarity study on the two live captures (all icons clean on mis.bin, FC Medium+Low clean on livefc.bin)
      if (System.IO.File.Exists(fmis) && System.IO.File.Exists(ffc)) {
        var mi2 = Load(fmis); var gm2 = FarmCheck.FindGrid(mi2, 815, 160, 620, 620);
        var names = FarmCheck.Icons.Select(t => t.name).ToList();
        var live = new List<(string name, double[] f)>();
        for (int i = 0; i < 10; i++) live.Add((names[i], FarmCheck.IconFeature(mi2, gm2, i / 5, i % 5)));
        var lv2 = Load(ffc); var lx2 = new List<int>(); var ly2 = new List<int>();
        for (int x = 0; x < lv2.W; x++) { int n = 0; for (int y = 0; y < lv2.H; y++) { lv2.Rgb(x, y, out int r, out int g, out int b); if (g > 200 && r < 60 && b < 60) n++; } if (n > 300) lx2.Add(x); }
        for (int y = 0; y < lv2.H; y++) { int n = 0; for (int x = 0; x < lv2.W; x++) { lv2.Rgb(x, y, out int r, out int g, out int b); if (g > 200 && r < 60 && b < 60) n++; } if (n > 300) ly2.Add(y); }
        var lg2 = new Grid { X = lx2.First(), Y = ly2.First(), PitchX = (lx2.Last() - lx2.First()) / 8.0, PitchY = (ly2.Last() - ly2.First()) / 8.0 };
        double shineMed = FarmCheck.Dist(live[8].f, FarmCheck.IconFeature(lv2, lg2, 1, 3)), shineLow = FarmCheck.Dist(live[9].f, FarmCheck.IconFeature(lv2, lg2, 1, 4));
        Console.WriteLine($"STUDY same icon, two live moments: FC Medium {shineMed:0.00}, FC Low {shineLow:0.00}");
        Console.WriteLine($"STUDY live vs JPG template, same core: " + string.Join(", ", live.Select((l, i) => $"{l.name.Replace("Upgrade Core","UC").Replace("Force Core","FC")} {FarmCheck.Dist(l.f, FarmCheck.Icons[i].f):0.00}")));
        double minDiff = 99; string pair = "";
        for (int i = 0; i < 10; i++) for (int j = i + 1; j < 10; j++) { double d = FarmCheck.Dist(live[i].f, live[j].f); if (d < minDiff) { minDiff = d; pair = live[i].name + " vs " + live[j].name; } }
        Console.WriteLine($"STUDY closest two different cores (live): {minDiff:0.00} ({pair}); UC Ult vs UC Highest {FarmCheck.Dist(live[0].f, live[1].f):0.00}; FC Ult vs FC Highest {FarmCheck.Dist(live[5].f, live[6].f):0.00}; UC Ult vs FC Ult {FarmCheck.Dist(live[0].f, live[5].f):0.00}; UC Highest vs FC Highest {FarmCheck.Dist(live[1].f, live[6].f):0.00}");
        var liveRead2 = FarmCheck.BestRead(mi2, gm2, FarmCheck.DefaultIcons()).slots.OrderBy(x => x.Row * 8 + x.Col).ToList();
        string gotLive = string.Join(",", liveRead2.Select(x => x.Item));
        Check(gotLive == expect && liveRead2.All(x => x.GlyphCount > 0), "live Debug capture: all 10 cores with count glyphs, with the default icons -> " + gotLive);
        foreach (var l in live) { var m = FarmCheck.MatchIcon(l.f, FarmCheck.DefaultIcons()); Check(m != null && m.Value.name == l.name && m.Value.d < 0.01, "live icon matches its own core unambiguously: " + l.name); }
      }
      // grid picker must prefer the grid where every core slot has count glyphs
      { var gOk = FarmCheck.FindGrid(inv, 1912, 248, 612, 612); var (gPick, sPick) = FarmCheck.BestRead(inv, gOk, icons);
        Check(sPick.Count == 10 && sPick.All(x => x.GlyphCount > 0), "grid picker keeps the alignment where every core slot has count glyphs"); }
      // hysteresis: the grid used last time is kept unless another position reads clearly better
      { var g0 = FarmCheck.FindGrid(inv, 1912, 248, 612, 612); var first = FarmCheck.BestRead(inv, g0, FarmCheck.DefaultIcons());
        var nudged = g0; nudged.X += 3; nudged.Y -= 2;                       // a slightly different snap on the next read
        var again = FarmCheck.BestRead(inv, nudged, FarmCheck.DefaultIcons(), keep: first.grid);
        Check(again.grid.X == first.grid.X && again.grid.Y == first.grid.Y && again.slots.Count == 10, "grid is kept between reads when the new snap differs slightly");
        var badKeep = first.grid; badKeep.X -= 8 * badKeep.Scale; badKeep.Y -= 8 * badKeep.Scale;   // a previously kept grid that reads badly
        var fixedUp = FarmCheck.BestRead(inv, g0, FarmCheck.DefaultIcons(), keep: badKeep);
        Check(fixedUp.slots.Count(x => x.GlyphCount > 0) >= 9, "a kept grid that reads badly is replaced by a clearly better one"); }
      // button/cross anchor on every capture
      foreach (var (nm, file, box, expect10) in new[]{ ("inv", "inv.bin", (1912,248,612,612), expect), ("mis", "mis.bin", (815,160,620,620), expect), ("off", "off.bin", (487,143,658,642), null) }) {
        var fpath = System.IO.Path.Combine(dataDir, file); if (!System.IO.File.Exists(fpath)) continue;
        var cim = nm == "inv" ? inv : Load(fpath);
        var snap = FarmCheck.FindGrid(cim, box.Item1, box.Item2, box.Item3, box.Item4);
        var wrongSnap = snap; wrongSnap.X += 9; wrongSnap.Y -= 11;               // a wrong snap, like the twitching one
        var anchored = FarmCheck.AnchorByButtons(cim, wrongSnap, out bool okA, out string infoA);
        var swB = System.Diagnostics.Stopwatch.StartNew(); var byBtn = FarmCheck.LocateByButton(cim, out double dBtn, out double sBtn); long msBtn = swB.ElapsedMilliseconds;
        Console.WriteLine($"ANCHOR {nm}: snap {snap.X:0},{snap.Y:0}; from a wrong snap -> {infoA}; whole-image locate by button -> {(byBtn.HasValue ? $"{byBtn.Value.X:0},{byBtn.Value.Y:0}" : "none")} (diff {dBtn:0.0}, {msBtn} ms)");
        if (expect10 != null) {
          var read = FarmCheck.ReadInventory(cim, anchored, FarmCheck.DefaultIcons()).OrderBy(x => x.Row * 8 + x.Col).ToList();
          string got = string.Join(",", read.Select(x => x.Item));
          Check(okA && got == expect10 && read.All(x => x.GlyphCount > 0), $"{nm}: grid anchored on the button finds all 10 cores with count glyphs ({got})");
          Check(byBtn.HasValue && Math.Abs(byBtn.Value.X - anchored.X) <= 2 && Math.Abs(byBtn.Value.Y - anchored.Y) <= 2, $"{nm}: locating by the button alone finds the same grid");
        } else Check(okA && Math.Abs(anchored.X - 506) <= 2 && Math.Abs(anchored.Y - 158) <= 2, $"{nm}: anchored grid matches the frame snap 506,158 ({anchored.X:0},{anchored.Y:0})");
      }
      // sticky identity: a slot keeps its core when the icon drifts past the strict limit but still resembles it
      { var gS2 = FarmCheck.BestRead(inv, FarmCheck.FindGrid(inv, 1912, 248, 612, 612), FarmCheck.DefaultIcons());
        var prior = gS2.slots.ToDictionary(x => (x.Row, x.Col), x => x.Item);
        var strictIcons = FarmCheck.DefaultIcons().Where(t => t.name != "Force Core (Low)").Concat(new[]{ ("Force Core (Low)", FarmCheck.Icons.First(t => t.name == "Force Core (Low)").f) }).ToList();   // only the JPG sample: live FC Low sits ~2.0 away
        var without = FarmCheck.ReadInventory(inv, gS2.grid, strictIcons);
        var withPrior = FarmCheck.ReadInventory(inv, gS2.grid, strictIcons, 8, 8, prior);
        Console.WriteLine($"STICKY without prior: {without.Count} slots, with prior: {withPrior.Count} slots");
        Check(withPrior.Count >= without.Count && withPrior.Any(x => x.Item == "Force Core (Low)"), "a previously identified slot stays identified when its icon drifts"); }
      // live 2.9.8 frame where most counts showed "?": reproduce the read
      var fq = System.IO.Path.Combine(dataDir, "q.bin");
      if (System.IO.File.Exists(fq)) {
        var q = Load(fq);
        var gq = FarmCheck.LocateByButton(q, out double qd, out double qs);
        Console.WriteLine($"Q locate by button: {(gq.HasValue ? $"{gq.Value.X:0},{gq.Value.Y:0}" : "none")} diff {qd:0.0}");
        if (gq.HasValue) {
          var rq = FarmCheck.ReadInventory(q, gq.Value, FarmCheck.DefaultIcons()).OrderBy(x => x.Row * 8 + x.Col).ToList();
          Console.WriteLine("Q read: " + string.Join(" | ", rq.Select(x => $"{x.Item.Replace("Upgrade Core","UC").Replace("Force Core","FC")} g{x.GlyphCount} d{x.IconDist:0.00}")));
          // the bright-shine slots (9 and 209): the count region must cover the whole glyph chain
          int okQ = 0; var infoQ = new List<string>();
          foreach (var (nmq, digitsQ) in new[]{ ("Upgrade Core (Low)", 1), ("Upgrade Core (High)", 3) }) {
            var sl = rq.FirstOrDefault(x => x.Item == nmq); if (sl == null) { infoQ.Add(nmq + ": not found"); continue; }
            var br = FarmCheck.FindCountBand(q, gq.Value, sl.Row, sl.Col, out var glyphs);
            var reg = br.HasValue ? FarmCheck.CountRegion(glyphs, br.Value.Width, br.Value.Height) : null;
            bool ok = glyphs.Count > 0 && reg.HasValue && reg.Value.X <= glyphs[glyphs.Count - 1].Right - digitsQ * 11 * gq.Value.Scale && reg.Value.X <= glyphs[0].X && reg.Value.Right >= glyphs[glyphs.Count - 1].Right && reg.Value.Y <= glyphs.Min(r => r.Y) && reg.Value.Bottom >= glyphs.Max(r => r.Bottom);
            if (ok) okQ++;
            infoQ.Add($"{nmq}: {glyphs.Count} glyphs " + string.Join(" ", glyphs.Select(r => $"[{r.X}-{r.Right - 1} y{r.Y}-{r.Bottom - 1}]")) + $", region {(reg.HasValue ? $"{reg.Value.X}-{reg.Value.Right} y{reg.Value.Y}-{reg.Value.Bottom}" : "none")}");
          }
          Check(okQ == 2, "live frame with bright shine: the count region covers the glyph chain and all digits of 9 and 209 (" + string.Join("; ", infoQ) + ")");
        }
      }
      // generic count region: anchored on the last glyph, covers the whole count, on every capture
      foreach (var (nm3, im3, box3, digits3) in new[]{ ("inv", inv, (1912,248,612,612), "3,208,144,14,9,5,34,158,16,2"), ("mis", Load(fmis), (815,160,620,620), "3,223,172,14,9,5,34,158,16,2") }) {
        var gr = FarmCheck.BestRead(im3, FarmCheck.FindGrid(im3, box3.Item1, box3.Item2, box3.Item3, box3.Item4), FarmCheck.DefaultIcons());
        var lens = digits3.Split(',').Select(x => x.Length).ToList();
        int okR = 0, k3 = 0; var info = new List<string>();
        foreach (var sl in gr.slots.OrderBy(x => x.Row * 8 + x.Col)) {
          var br = FarmCheck.FindCountBand(im3, gr.grid, sl.Row, sl.Col, out var glyphs);
          var reg = br.HasValue ? FarmCheck.CountRegion(glyphs, br.Value.Width, br.Value.Height) : null;
          bool covers = reg.HasValue && glyphs.Count > 0 && reg.Value.X <= glyphs[0].X && reg.Value.Right >= glyphs[glyphs.Count - 1].Right
                        && reg.Value.Y <= glyphs.Min(r => r.Y) && reg.Value.Bottom >= glyphs.Max(r => r.Bottom)
                        && k3 < lens.Count && reg.Value.X <= glyphs[glyphs.Count - 1].Right - lens[k3] * 11 * gr.grid.Scale;   // wide enough for all digits of the known count
          if (covers) okR++; k3++;
          info.Add($"{sl.Item.Replace("Upgrade Core","UC").Replace("Force Core","FC")}: region {(reg.HasValue ? $"{reg.Value.X}-{reg.Value.Right} y{reg.Value.Y}-{reg.Value.Bottom}" : "none")} glyphs {(glyphs.Count > 0 ? $"{glyphs[0].X}-{glyphs[glyphs.Count-1].Right} y{glyphs.Min(q => q.Y)}-{glyphs.Max(q => q.Bottom)} ({glyphs.Count})" : "none")}{(covers ? "" : " MISSED")}");
        }
        Console.WriteLine($"REGION {nm3}: " + string.Join(" | ", info));
        Check(gr.slots.Count == 10 && okR == 10, $"{nm3}: the generic count region covers every count's glyph chain and all its digits ({okR}/10)");
      }
      // OCR input treatments: 5 generic pictures per real count region
      {
        FarmCheck.TextPrinter fakePrint = (t, dh) => { int w = Math.Max(1, t.Length * dh * 6 / 10), h = dh + dh / 3; var px = new byte[w * h * 4]; for (int q = 0; q < px.Length; q++) px[q] = 255; return (new Img(w, h, px), dh); };
        int regionsSeen = 0, okV = 0; var infoV = new List<string>();
        foreach (var (nmV, imV, boxV) in new[]{ ("inv", inv, (1912,248,612,612)), ("mis", Load(fmis), (815,160,620,620)) }) {
          var grV = FarmCheck.BestRead(imV, FarmCheck.FindGrid(imV, boxV.Item1, boxV.Item2, boxV.Item3, boxV.Item4), FarmCheck.DefaultIcons());
          foreach (var sl in grV.slots) {
            var reg = FarmCheck.CountRegionImage(imV, grV.grid, sl.Row, sl.Col, out int nG);
            if (reg == null) continue;
            regionsSeen++;
            var vs = FarmCheck.MakeCountVariants(reg); var vp = FarmCheck.MakeCountVariants(reg, fakePrint);
            bool ok = vs.Count == 5 && vs.Select(v => v.name).SequenceEqual(FarmCheck.CountVariantNames) && vp.Count == 5
                      && vs.Concat(vp).All(v => v.picture != null && v.picture.W > reg.W && v.picture.H > reg.H && v.picture.Px.Length == v.picture.W * v.picture.H * 4
                                            && v.number.X >= 0 && v.number.Y >= 0 && v.number.Right <= v.picture.W && v.number.Bottom <= v.picture.H && v.number.Width >= reg.W * 2);
            var iso = vs[3].picture; int black = 0;
            for (int q = 0; q < iso.W * iso.H; q++) if ((iso.Px[q*4] + iso.Px[q*4+1] + iso.Px[q*4+2]) / 3 < 128) black++;
            double frac = (double)black / (iso.W * iso.H);
            ok = ok && black > 0 && frac < 0.30;
            // printed words widen the prefix / context pictures and push the count to the right; the others are unchanged
            ok = ok && vp[1].picture.W > vs[1].picture.W && vp[1].number.X > vs[1].number.X && vp[4].picture.W > vs[4].picture.W && vp[4].number.X > vs[4].number.X
                    && vp[0].picture.W == vs[0].picture.W && vp[2].picture.W == vs[2].picture.W && vp[3].picture.W == vs[3].picture.W;
            if (ok) okV++;
            infoV.Add($"{nmV} {sl.Item.Replace("Upgrade Core","UC").Replace("Force Core","FC")} {reg.W}x{reg.H} iso {frac:P0}");
          }
        }
        Console.WriteLine("VARIANTS: " + string.Join(" | ", infoV));
        Check(regionsSeen == 20 && okV == regionsSeen, $"5 OCR treatments per real count region: non-empty, larger than the region, white-isolated 0-30% black ({okV}/{regionsSeen})");
      }
      // Tesseract input B: exactly the count's digits and nothing else, black on white, 3x, white margin. On every
      // count region of the three captures (first two rows, 5 cores each) the picture must hold one black component
      // per digit of the known count, and CountRegionImage must report that many digits.
      {
        int seenD = 0, okD = 0; var infoD = new List<string>(); var badD = new List<string>();
        var capsD = new List<(string nm, Img im, Grid g, string counts)>();
        { var gI = FarmCheck.BestRead(inv, FarmCheck.FindGrid(inv, 1912, 248, 612, 612), FarmCheck.DefaultIcons()).grid; capsD.Add(("inv", inv, gI, "3,208,144,14,9,5,34,158,16,2")); }
        { var mD = Load(fmis); var gM = FarmCheck.BestRead(mD, FarmCheck.FindGrid(mD, 815, 160, 620, 620), FarmCheck.DefaultIcons()).grid; capsD.Add(("mis", mD, gM, "3,223,172,14,9,5,34,158,16,2")); }
        var fqD = System.IO.Path.Combine(dataDir, "q.bin");
        if (System.IO.File.Exists(fqD)) { var qD = Load(fqD); var gQ = FarmCheck.LocateByButton(qD, out double _, out double _); if (gQ.HasValue) capsD.Add(("q", qD, gQ.Value, "3,262,209,14,9,5,34,158,16,2")); }
        foreach (var (nmD, imD, gD, countsD) in capsD) {
          var cnt = countsD.Split(',');
          for (int k = 0; k < 10; k++) {
            int rD = k / 5, cD = k % 5;
            var reg = FarmCheck.CountRegionImage(imD, gD, rD, cD, out int nD, out Img mask);
            seenD++;
            if (reg == null) { badD.Add($"{nmD} {rD},{cD}: no region"); continue; }
            var pic = FarmCheck.DigitsIsolatedPicture(mask);
            int black = 0;
            for (int q2 = 0; q2 < pic.W * pic.H; q2++) if (pic.Px[q2*4] < 128) black++;
            double frac = (double)black / (pic.W * pic.H);
            var (comps, _) = BlackComponents(pic); var (gh, gm) = DarkRows(pic); int levels = GreyLevels(pic);
            bool ok = mask.W == reg.W && mask.H == reg.H && frac > 0 && frac < 0.40 && comps == cnt[k].Length && nD == comps
                      && Math.Abs(gh - FarmCheck.DigitsGlyphHeight) <= 4 && gm >= 0.2 * gh && levels >= 16;       // ~48 px glyphs, 25% margin, smooth
            if (ok) okD++; else badD.Add($"{nmD} {rD},{cD} ({cnt[k]}): {comps} components, {nD} digits, glyphs {gh} px, margin {gm}, {levels} grey levels");
            infoD.Add($"{nmD} {rD},{cD} {cnt[k]}: {comps}/{cnt[k].Length} h{gh}");
          }
        }
        Console.WriteLine("DIGITS-ISOLATED (slot count: components/digits, glyph height): " + string.Join(" | ", infoD));
        Check(capsD.Count == 3 && seenD == 30 && okD == seenD, $"digits picture B holds exactly the count's digits, one dark component each, smooth, glyphs ~48 px with a 25% margin, on inv, mis and q ({okD}/{seenD})" + (badD.Count > 0 ? ": " + string.Join("; ", badD) : ""));
        // the same count regions with the leading digit touching something: a bright bar joining it to the next digit
        // (a merged pair, split at the fixed advance), a solid white shine touching it, a dim tinted icon rim touching it
        int seenS = 0, okS = 0; var badS = new List<string>();
        foreach (var (nmD, imD, gD, countsD) in capsD) {
          var cnt = countsD.Split(',');
          for (int k = 0; k < 10; k++) {
            if (cnt[k].Length < 2) continue;
            var bandR = FarmCheck.FindCountBand(imD, gD, k / 5, k % 5, out var chain);
            if (bandR == null) { badS.Add($"{nmD} {k / 5},{k % 5}: no count band"); continue; }
            var bandS = FarmCheck.CropImg(imD, bandR.Value); var region = FarmCheck.CountRegion(chain, bandS.W, bandS.H).Value;
            FarmCheck.DigitsMask(bandS, chain, region, out var clean);
            if (clean.Count < 2) { badS.Add($"{nmD} {k / 5},{k % 5}: clean picture has {clean.Count} digits"); continue; }
            var d0 = clean[0]; var d1 = clean[1]; int hD = d0.Height, mid = d0.Y + hD / 2;
            foreach (var kind in new[]{ "bridge", "shine", "rim" }) {
              var b2 = new Img(bandS.W, bandS.H, (byte[])bandS.Px.Clone());
              void Paint(int x0, int x1, int y0, int y1, int v, int sat) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { int X = region.X + x, Y = region.Y + y; if (X < 0 || Y < 0 || X >= b2.W || Y >= b2.H) continue; int i = (Y * b2.W + X) * 4; b2.Px[i] = (byte)(v - sat); b2.Px[i+1] = (byte)(v - sat / 2); b2.Px[i+2] = (byte)v; } }
              if (kind == "bridge") Paint(d0.Right - 1, d1.X, mid - 1, mid, 234, 0);
              if (kind == "shine") Paint(d0.X - hD / 2, d0.X, mid - hD / 3, mid + hD / 3, 234, 0);
              if (kind == "rim") Paint(d0.X - hD / 2, d0.X, d0.Y, d0.Bottom - 1, 200, 30);
              var mk = FarmCheck.DigitsMask(b2, chain, region, out var got);
              var (compsS, _) = BlackComponents(FarmCheck.DigitsIsolatedPicture(mk));
              seenS++;
              if (got.Count == cnt[k].Length && compsS == got.Count) okS++; else badS.Add($"{nmD} {k / 5},{k % 5} ({cnt[k]}) {kind}: {got.Count} digits, {compsS} components");
            }
          }
        }
        Check(seenS == 54 && okS == seenS, $"digits picture B with the leading digit joined to its neighbour, to a white shine or to an icon rim: still one component per digit ({okS}/{seenS})" + (badS.Count > 0 ? ": " + string.Join("; ", badS) : ""));
        // the lone-digit picture for Tesseract: the digit three times, as black components, same height
        { var g9 = capsD[0].g; FarmCheck.CountRegionImage(inv, g9, 0, 4, out int n9, out Img m9);
          var rep = FarmCheck.RepeatedDigitPicture(m9); var one = FarmCheck.DigitsIsolatedPicture(m9);
          var (c9, _) = BlackComponents(rep); int bl1 = 0, bl3 = 0;
          for (int q2 = 0; q2 < one.W * one.H; q2++) if (one.Px[q2*4] < 128) bl1++;
          for (int q2 = 0; q2 < rep.W * rep.H; q2++) if (rep.Px[q2*4] < 128) bl3++;
          Check(n9 == 1 && c9 == 3 && Math.Abs(bl3 - 3 * bl1) <= 0.03 * bl3 && Math.Abs(DarkRows(rep).h - DarkRows(one).h) <= 1 && FarmCheck.RepeatedDigitPicture(new Img(2, 2, Enumerable.Repeat((byte)255, 16).ToArray())) == null,
                $"repeated lone digit picture: the 9 three times ({c9} components, {bl3} = 3 x {bl1} black pixels), nothing for an empty mask"); }
      }
      // ---- UI scale change: mis and q shrunk to 75% and 60% (bicubic), as if the game's UI size were set smaller ----
      {
        var table = new List<string>(); int casesS = 0, okLoc = 0, okAnc = 0, okRows = 0, okPics = 0; var badU = new List<string>();
        foreach (var (nmU, fileU, countsU) in new[]{ ("mis", "mis.bin", "3,223,172,14,9,5,34,158,16,2"), ("q", "q.bin", "3,262,209,14,9,5,34,158,16,2") }) {
          var fU = System.IO.Path.Combine(dataDir, fileU); if (!System.IO.File.Exists(fU)) continue;
          var full = Load(fU); var g1 = FarmCheck.LocateByButton(full, out double _, out double s1);
          if (g1 == null) { badU.Add(nmU + ": not located at full size"); continue; }
          var cntU = countsU.Split(',');
          foreach (var f in new[]{ 0.75, 0.6 }) {
            casesS++;
            var small = ResizeBicubic(full, f);
            var swU = System.Diagnostics.Stopwatch.StartNew();
            var gU = FarmCheck.LocateByButton(small, out double dU, out double sU); long msU = swU.ElapsedMilliseconds;
            // located: scale within 5%, slot size RefPitch x scale, origin where the full-size grid lands after shrinking
            bool loc = gU.HasValue && Math.Abs(sU / (s1 * f) - 1) <= 0.05 && Math.Abs(gU.Value.PitchX / (g1.Value.PitchX * f) - 1) <= 0.03
                       && Math.Abs(gU.Value.X - g1.Value.X * f) <= Math.Max(2, 0.05 * gU.Value.PitchX) && Math.Abs(gU.Value.Y - g1.Value.Y * f) <= Math.Max(2, 0.05 * gU.Value.PitchX);
            if (loc) okLoc++; else badU.Add($"{nmU} {f}: locate {(gU.HasValue ? $"{gU.Value.X:0.0},{gU.Value.Y:0.0} pitch {gU.Value.PitchX:0.00}" : "none")} scale {sU:0.000} (expected ~{g1.Value.X * f:0},{g1.Value.Y * f:0} pitch {g1.Value.PitchX * f:0.00})");
            string rowInfo = "-", picInfo = "-"; int fullDigits = 0, goodPics = 0;
            if (gU.HasValue) {
              var gl = gU.Value;
              // anchor from a wrong start: a snap offU by a sixth of a slot at the right scale, and the old full-size grid (stale scale)
              var offU = gl; offU.X += gl.PitchX / 6; offU.Y -= gl.PitchY / 7;
              var aU1 = FarmCheck.AnchorByButtons(small, offU, out FarmCheck.AnchorResult rU1, sU);
              var stale = g1.Value;
              var aU2 = FarmCheck.AnchorByButtons(small, stale, out FarmCheck.AnchorResult rU2, s1);
              bool anc = rU1.Ok && Math.Abs(aU1.X - gl.X) <= 2 && Math.Abs(aU1.Y - gl.Y) <= 2
                         && rU2.Ok && rU2.ScaleSearched && Math.Abs(aU2.X - gl.X) <= 2 && Math.Abs(aU2.Y - gl.Y) <= 2 && Math.Abs(rU2.Scale / sU - 1) <= 0.01 && Math.Abs(aU2.PitchX / gl.PitchX - 1) <= 0.01;
              if (anc) okAnc++; else badU.Add($"{nmU} {f}: anchor from a wrong snap -> {rU1.Info}; from the full-size grid -> {rU2.Info}");
              // count rows from the pixels: every digit whole (no glyph touching the search areaU's top/bottom), one component per digit
              var rowsBad = new List<string>();
              for (int k = 0; k < 10; k++) {
                int rU = k / 5, cU = k % 5;
                var areaU = FarmCheck.CountSearchArea(gl, rU, cU);
                var bandU = FarmCheck.FindCountBand(small, gl, rU, cU, out var chainU);
                var regU = FarmCheck.CountRegionImage(small, gl, rU, cU, out int nU, out Img maskU);
                bool whole = bandU.HasValue && chainU.Count > 0 && chainU.All(b => bandU.Value.Y + b.Y > areaU.Y && bandU.Value.Y + b.Bottom < areaU.Bottom && b.Y >= 1 && b.Bottom <= bandU.Value.Height - 1);
                bool digitsOk = whole && regU != null && maskU != null && nU == cntU[k].Length;
                if (digitsOk) fullDigits++; else rowsBad.Add($"{rU},{cU} ({cntU[k]}): {(bandU.HasValue ? $"bandU y{bandU.Value.Y - areaU.Y}-{bandU.Value.Bottom - areaU.Y} of {areaU.Height}, {chainU.Count} glyphs, {nU} digits" : "no bandU")}");
                if (!digitsOk) continue;
                var picU = FarmCheck.DigitsIsolatedPicture(maskU);
                var (compsU, _) = BlackComponents(picU); var (ghU, gmU) = DarkRows(picU);
                if (compsU == cntU[k].Length && Math.Abs(ghU - FarmCheck.DigitsGlyphHeight) <= 4 && gmU >= 0.2 * ghU) goodPics++;
                else rowsBad.Add($"{rU},{cU} ({cntU[k]}) picture B: {compsU} components, glyphs {ghU} px, margin {gmU}");
              }
              if (fullDigits == 10) okRows++;
              if (goodPics == 10) okPics++;
              if (rowsBad.Count > 0) badU.Add($"{nmU} {f}: " + string.Join("; ", rowsBad));
              rowInfo = $"{fullDigits}/10"; picInfo = $"{goodPics}/10";
            }
            table.Add($"{nmU} x {f:0.00}: {(loc ? "located" : "NOT located")} (scale {sU:0.000}, slot {(gU.HasValue ? gU.Value.PitchX : 0):0.00} px, {msU} ms), slots with full digits {rowInfo}, B pictures ok {picInfo}");
          }
        }
        foreach (var t in table) Console.WriteLine("UI-SCALE " + t);
        foreach (var t in badU) Console.WriteLine("UI-SCALE problem: " + t);
        Check(casesS == 4 && okLoc == casesS, "UI scale 60%/75%: the sword button finds the inventory, scale within 5%, slot size to match" + (badU.Count > 0 ? ": " + string.Join(" | ", badU) : ""));
        Check(casesS == 4 && okAnc == casesS, "UI scale 60%/75%: the anchor pins the grid from a wrong snap and from the stale full-size grid (scale searched)");
        Check(casesS == 4 && okRows == casesS, "UI scale 60%/75%: the count rows found from the pixels hold every count's digits whole (10/10 slots each)");
        Check(casesS == 4 && okPics == casesS, "UI scale 60%/75%: picture B has ~48 px glyphs and one dark component per digit (10/10 slots each)");
      }
      // whole-screen multi-scale locate on the full 2560x1440 capture
      { var swT = System.Diagnostics.Stopwatch.StartNew(); var gT = FarmCheck.LocateByButton(inv, out double dT, out double sT); long msT = swT.ElapsedMilliseconds;
        Console.WriteLine($"INFO multi-scale locate by button ({FarmCheck.MinUiScale}-{FarmCheck.MaxUiScale}, {inv.W}x{inv.H}, {(DebugBuild ? "Debug build" : "Release build")}): {msT} ms -> {(gT.HasValue ? $"{gT.Value.X:0},{gT.Value.Y:0} scale {sT:0.000}" : "none")}");
        Check(gT.HasValue && Math.Abs(sT - 1) < 0.01 && msT < 3000, $"multi-scale locate on the whole 2560x1440 screen: found at scale {sT:0.000} in {msT} ms (under 3 s even in a Debug build)"); }
      var foff = System.IO.Path.Combine(dataDir, "off.bin");
      if (System.IO.File.Exists(foff)) {
        var ofi = Load(foff);   // 1146x1018 screenshot; inventory grid slots start ~x505,y150, pitch ~76.9; blue area box ~487,143 658x642
        foreach (var (label, gfn) in new (string, Func<Grid>)[] {
            ("FindGrid area, pitch 0", () => FarmCheck.FindGrid(ofi, 487, 143, 658, 642)),
            ("FindGrid area, pitch 76.9", () => FarmCheck.FindGrid(ofi, 487, 143, 658, 642, 8, 8, 76.9)),
            ("FindGridNear area, 76.9", () => FarmCheck.FindGridNear(ofi, 487, 143, 658, 642, 76.9)),
            ("FindGrid whole image, pitch 0", () => FarmCheck.FindGrid(ofi, 0, 0, ofi.W, ofi.H)),
            ("LocateInventory", () => FarmCheck.LocateInventory(ofi) ?? new Grid()) }) {
          var g = gfn();
          var rd = FarmCheck.BestRead(ofi, g, FarmCheck.DefaultIcons());
          Console.WriteLine($"OFF {label}: grid {g.X:0},{g.Y:0} pitch {g.PitchX:0.00}x{g.PitchY:0.00} contrast {g.Score:0.00} weakest {FarmCheck.WeakestLine(ofi, g):0.0} -> read grid {rd.grid.X:0},{rd.grid.Y:0}: " + string.Join(",", rd.slots.Select(x => x.Item.Replace("Upgrade Core","UC").Replace("Force Core","FC") + " g" + x.GlyphCount)));
        }
      }
      var f1080 = System.IO.Path.Combine(dataDir, "inv-1080.bin");
      if (System.IO.File.Exists(f1080)) {
        var small = Load(f1080); var g2 = FarmCheck.FindGrid(small, 1434, 186, 459, 459);
        var got2 = string.Join(",", FarmCheck.BestRead(small, g2, icons).slots.Select(x => x.Item + " g" + x.GlyphCount));
        Console.WriteLine($"INFO 1920x1080 (resized screenshot): grid pitch {g2.PitchX:0.00} -> {got2}");
        Console.WriteLine($"INFO 1080p-sized (resized, blurry) inventory: pitch {g2.PitchX:0.00} at {g2.X:0},{g2.Y:0} (true grid at 1439,190)");
      }
    } else Console.WriteLine("SKIP farm screenshot tests (no test/data)");
    // ---- Farm Tracker: title text, OCR tiles, saved slot size ----
    Check(new[]{ "Inventory", "lnventory", "INVENTORY", "Inventorv", "|nventory", "Inventory:" }.All(FarmCheck.IsInventoryTitle)
          && !new[]{ "Warehouse", "Invite", "Event", "Inventory Full Warning", "" }.Any(FarmCheck.IsInventoryTitle), "\"Inventory\" title recognised with reader noise, other words not");
    var tl = FarmCheck.InventoryTitles(new List<IList<OcrWord>>{
      new List<OcrWord>{ new OcrWord("Obtain", 10, 10, 50, 12), new OcrWord("Inventory", 70, 10, 60, 12) },
      new List<OcrWord>{ new OcrWord("Inven", 200, 300, 40, 20), new OcrWord("tory", 244, 301, 30, 20) },
      new List<OcrWord>{ new OcrWord("Character", 500, 50, 80, 20) } });
    Check(tl.Count == 2 && tl[0].X == 70 && tl[1].X == 200 && tl[1].W == 74 && tl[1].H == 21, "title boxes: the word itself, or a split word joined");
    var tiles = FarmCheck.Tiles(5120, 1440, 2600, 200);
    Check(FarmCheck.Tiles(2560, 1440, 2600, 200).Count == 1 && tiles.All(t => t.w <= 2600 && t.h <= 2600) && tiles.Max(t => t.x + t.w) == 5120 && tiles.Max(t => t.y + t.h) == 1440
          && Enumerable.Range(0, 5120).All(x => tiles.Any(t => x >= t.x && x < t.x + t.w)), $"OCR tiles cover two monitors in {tiles.Count} pieces of at most 2600 px");
    Check(FarmCheck.ParseSlotSize("76.90") == 76.9 && FarmCheck.ParseSlotSize(FarmCheck.FormatSlotSize(57.625)) == 57.63 && FarmCheck.ParseSlotSize("abc") == null
          && FarmCheck.ParseSlotSize("5") == null && FarmCheck.ParseSlotSize("") == null && FarmCheck.ParseSlotSize(null) == null, "slot=76.90 saved/parsed, nonsense ignored");
    Check(FarmCheck.ParseSlotSize("38.40") == 38.4 && FarmCheck.ParseUiScale(FarmCheck.FormatUiScale(0.6123)) == 0.6123 && FarmCheck.ParseUiScale("abc") == null
          && FarmCheck.ParseUiScale("0.2") == null && FarmCheck.ParseUiScale("3") == null && FarmCheck.ParseUiScale(null) == null, "scale=0.6123 saved/parsed, a 50% UI's 38.4 px slot is a valid size, nonsense ignored");
    var exp = new[]{ "UC High", "UC Low", "FC Low" };
    var start = new Dictionary<string,int>{ ["UC High"]=144, ["UC Low"]=9 };
    Check(string.Join(",", FarmCheck.MissingToConfirm(exp, start, null, new HashSet<string>()))=="FC Low", "start: ask about every core not found");
    var confirmed = new HashSet<string>{ "FC Low" };
    Check(FarmCheck.MissingToConfirm(exp, start, null, confirmed).Count==0, "start: confirmed-empty cores aren't asked again");
    var baseL = new Dictionary<string,int>{ ["UC High"]=144, ["UC Low"]=9, ["FC Low"]=0 };
    var endR = new Dictionary<string,int>{ ["UC High"]=156, ["FC Low"]=1 };
    Check(string.Join(",", FarmCheck.MissingToConfirm(exp, endR, baseL, confirmed))=="UC Low", "end: ask only about cores that were there before and are gone now");
    var endC = new Dictionary<string,int>{ ["UC High"]=156, ["FC Low"]=1, ["UC Low"]=0 };
    var gains = FarmCheck.Gains(baseL, endC);
    Check(gains["UC High"]==12 && gains["FC Low"]==1 && gains["UC Low"]==-9, "gains: new core from 0 counts +1, used-up stack counts -9");
    { var smc = new FarmCheck.CountSmoother();
      foreach (var v in new[]{ 158, 158, 158 }) smc.Add(new Dictionary<string,int>{ ["FC High"]=v });
      smc.Add(new Dictionary<string,int>{ ["FC High"]=170 }, new HashSet<string>{ "FC High" });
      Check(smc.Stable()["FC High"]==170, "a confident count (every stack read by the text reader) shows at once");
      smc.Add(new Dictionary<string,int>{ ["FC High"]=70 });
      Check(smc.Stable()["FC High"]==170, "an unconfirmed truncated read doesn't override it"); }
    var sm = new FarmCheck.CountSmoother();
    foreach (var v in new[]{ 158, 58, 158, 158, 58 }) sm.Add(new Dictionary<string,int>{ ["FC High"]=v, ["FC Med"]=16 });
    Check(sm.Stable()["FC High"]==158 && sm.Stable()["FC Med"]==16, "smoother: 158 wins over an occasional 58");
    Check(FarmCheck.CountSmoother.Consensus(new[]{ 58, 58, 58, 158, 58 })==158 && FarmCheck.CountSmoother.Consensus(new[]{ 223, 23, 23, 3 })==223
          && FarmCheck.CountSmoother.Consensus(new[]{ 14, 14, 15 })==14 && FarmCheck.CountSmoother.Consensus(new[]{ 9, 9, 8 })==9, "a truncated count (58) always yields to the full one (158); unrelated values vote normally");
    sm.Add(new Dictionary<string,int>{ ["FC High"]=158 }); sm.Add(new Dictionary<string,int>{ ["FC High"]=158 });
    Check(sm.Stable().ContainsKey("FC Med") && sm.Gone().Count==0, "a core missing in 2 reads is not gone yet");
    sm.Add(new Dictionary<string,int>{ ["FC High"]=158 });
    Check(!sm.Stable().ContainsKey("FC Med") && sm.Gone().SequenceEqual(new[]{"FC Med"}), "missing in 3 reads in a row: gone");
    sm.Add(new Dictionary<string,int>{ ["FC High"]=158, ["FC Med"]=16 });
    Check(sm.Stable().ContainsKey("FC Med") && sm.Gone().Count==0, "seen again: back, question would clear");
    // digit strip mapping: 3 bands of 28 px, gap 6, enlarged 3x
    var stripWords = new List<OcrWord>{ new OcrWord("158", 40, 3*3, 60, 45), new OcrWord("l4", 50, 3*(34+3), 30, 45), new OcrWord("2", 60, 3*(68+5), 15, 40), new OcrWord("x", 0, 3*(200), 10, 10) };
    var mapped = FarmCheck.MapStripWords(stripWords, 3, 28, 6, 3);
    Check(mapped[0]==158 && mapped[1]==14 && mapped[2]==2, $"strip words map back to their bands (got {mapped[0]},{mapped[1]},{mapped[2]})");
    var split = FarmCheck.MapStripWords(new[]{ new OcrWord("1", 30, 6, 15, 45), new OcrWord("58", 48, 6, 40, 45) }, 1, 28, 6, 3);
    Check(split[0]==158, "a count the reader splits into two words is joined in x order");
    var gi0 = new Grid { X = 1919, Y = 253, PitchX = 76.75, PitchY = 76.75 }; var band = FarmCheck.DigitBand(gi0, 0, 1);
    Check(band.Width >= 55 && band.Height >= 15 && band.X > gi0.X + gi0.PitchX && band.X < gi0.X + 2 * gi0.PitchX, "digit band sits inside its slot");
    Check(FarmCheck.TrailingCount("158")==158 && FarmCheck.TrailingCount("x.158")==158 && FarmCheck.TrailingCount("l58")==158 && FarmCheck.TrailingCount("1158")==1158
          && FarmCheck.TrailingCount("abc")==null && FarrmSafe(), "trailing-digit rule: junk on the left is dropped");
    // count from the reader's words on a treatment picture: printed words and words off the count are dropped
    var numBox = new System.Drawing.Rectangle(100, 30, 60, 50);
    Check(FarmCheck.CountFromWords(new[]{ new OcrWord("Have", 20, 35, 60, 40), new OcrWord("3", 120, 35, 20, 40), new OcrWord("pcs", 180, 40, 50, 35) }, numBox) == 3
          && FarmCheck.CountFromWords(new[]{ new OcrWord("Have", 20, 35, 60, 40), new OcrWord("pcs", 180, 40, 50, 35) }, numBox) == null
          && FarmCheck.CountFromWords(new[]{ new OcrWord("Qty9", 20, 35, 100, 40) }, numBox) == 9
          && FarmCheck.CountFromWords(new[]{ new OcrWord("Havepcs", 20, 35, 200, 40) }, numBox) == null
          && FarmCheck.CountFromWords(new[]{ new OcrWord("7", 10, 35, 20, 40), new OcrWord("226", 105, 35, 50, 40) }, numBox) == 226, "treatment words: printed words and words off the count are dropped");
    var flat = new Img(3, 2, Enumerable.Range(0, 24).Select(q => (byte)(q % 4 == 3 ? 255 : 90)).ToArray()); var flat3 = FarmCheck.Enlarge(flat, 3);
    Check(flat3.W == 9 && flat3.H == 6 && flat3.Px.All(b => b == 90 || b == 255), "bicubic enlarge keeps a flat picture flat");
    Check(FarmCheck.TesseractCount("209\n", 0.91f, true) == 209 && FarmCheck.TesseractCount("2 09\n", 0.8f, true) == 209 && FarmCheck.TesseractCount("12 34\n", 0.8f, false) == 34
          && FarmCheck.TesseractCount("209", 0.39f, true) == null && FarmCheck.TesseractCount("", 0.95f, true) == null && FarmCheck.TesseractCount("123456", 0.95f, true) == null && FarmCheck.TesseractCount(null, 1f, false) == null,
          "Tesseract result: trailing 1-5 digits, spaces dropped on the digits-only picture, under 40% confidence is not read");
    Check(FarmCheck.AcceptIsolatedCount("208", 3) && !FarmCheck.AcceptIsolatedCount("08", 2) && !FarmCheck.AcceptIsolatedCount("44", 3) && FarmCheck.AcceptIsolatedCount("0", 1) && FarmCheck.AcceptIsolatedCount("2 08", 3),
          "digits-only read must match the digits picture: same number of digits, no leading zero");
    {
      var onlyA = FarmCheck.DecideTesseract("208", 0.9f, null, 0f, 2); var onlyA2 = FarmCheck.DecideTesseract("208", 0.9f, "08", 0.3f, 2);
      var onlyB = FarmCheck.DecideTesseract("", 0.1f, "14", 0.8f, 2); var onlyBOff = FarmCheck.DecideTesseract(null, 0f, "08", 0.8f, 2);
      var same = FarmCheck.DecideTesseract("144", 0.7f, "1 44", 0.9f, 3);
      var longer = FarmCheck.DecideTesseract("208", 0.8f, "08", 0.9f, 2); var junkA = FarmCheck.DecideTesseract("201", 0.73f, "291", 0.96f, 3);
      var none = FarmCheck.DecideTesseract("", 0f, "9", 0.2f, 1);
      Check(onlyA == (208, FarmCheck.TessA, false) && onlyA2 == (208, FarmCheck.TessA, false), $"A+B decision: only A reads (B missing or under 40%) -> A, never confident ({onlyA}, {onlyA2})");
      Check(onlyB == (14, FarmCheck.TessB, true) && onlyBOff == (8, FarmCheck.TessB, false), $"A+B decision: only B reads -> B, not confident when it doesn't match its digits ({onlyB}, {onlyBOff})");
      Check(same == (144, FarmCheck.TessAgree, true), $"A+B decision: equal -> agree ({same})");
      Check(longer == (8, FarmCheck.TessBOverA, false) && junkA == (291, FarmCheck.TessBOverA, true), $"A+B decision: A never overrides B, not even when longer ({longer}, {junkA})");
      Check(none == (null, "none", false), $"A+B decision: nothing read ({none})");
    }
    {
      Check(FarmCheck.TesseractBNeedsRetry("", 0f) && FarmCheck.TesseractBNeedsRetry("36", 0.39f) && !FarmCheck.TesseractBNeedsRetry("36", 0.84f) && FarmCheck.TesseractBNeedsRetry(null, 0.9f),
            "B read in PSM 7: retried in PSM 8 / 13 when empty or under 40%, not otherwise");
      Check(FarmCheck.RepeatedMajority("999") == "9" && FarmCheck.RepeatedMajority("9 9 9") == "9" && FarmCheck.RepeatedMajority("99") == "9" && FarmCheck.RepeatedMajority("989") == "9"
            && FarmCheck.RepeatedMajority("98") == null && FarmCheck.RepeatedMajority("987") == null && FarmCheck.RepeatedMajority("9") == null && FarmCheck.RepeatedMajority("9999") == null
            && FarmCheck.RepeatedMajority("") == null && FarmCheck.RepeatedMajority(null) == null, "repeated lone digit: the majority of 2-3 digits, nothing when inconsistent");
      var pick1 = FarmCheck.PickTesseractB(new List<(string, string, float)>{ (FarmCheck.PsmLine, "", 0f), (FarmCheck.PsmWord, "36", 0.55f), (FarmCheck.PsmRawLine, "36", 0.84f) }, 2);
      var pick2 = FarmCheck.PickTesseractB(new List<(string, string, float)>{ (FarmCheck.PsmLine, "", 0f), (FarmCheck.PsmWord, "", 0f), (FarmCheck.PsmRawLine, "", 0f), (FarmCheck.PsmRepeated, "9 9 9", 0.91f) }, 1);
      var pick3 = FarmCheck.PickTesseractB(new List<(string, string, float)>{ (FarmCheck.PsmLine, "158", 0.96f) }, 3);
      var pick4 = FarmCheck.PickTesseractB(new List<(string, string, float)>{ (FarmCheck.PsmLine, "", 0f), (FarmCheck.PsmRepeated, "987", 0.95f) }, 1);
      var pick5 = FarmCheck.PickTesseractB(new List<(string, string, float)>{ (FarmCheck.PsmLine, "58", 0.3f), (FarmCheck.PsmRepeated, "888", 0.95f) }, 2);
      var pick6 = FarmCheck.PickTesseractB(new List<(string, string, float)>{ (FarmCheck.PsmLine, "7", 0.9f), (FarmCheck.PsmRepeated, "777", 0.9f) }, 1);
      Check(pick1 == ("36", 0.84f, FarmCheck.PsmRawLine) && pick2 == ("9", 0.91f, FarmCheck.PsmRepeated) && pick3 == ("158", 0.96f, FarmCheck.PsmLine)
            && pick4 == (null, 0f, null) && pick5 == (null, 0f, null) && pick6 == ("7", 0.9f, FarmCheck.PsmLine),
            $"B picks the most confident page mode; the repeated digit counts as its majority, only for a lone digit and when consistent ({pick1}, {pick2}, {pick3}, {pick4}, {pick5}, {pick6})");
    }
    var rawPic = FarmCheck.RawCountPicture(new Img(4, 3, Enumerable.Repeat((byte)200, 48).ToArray()));
    Check(rawPic.W == 4 * 3 + 60 && rawPic.H == 3 * 3 + 60 && rawPic.Px[0] == 30, "raw 3x picture for Tesseract: region 3x with a dark margin");
    bool threw = false; try { FarmCheck.MakeCountVariants(new Img(0, 0, new byte[0])); } catch (ArgumentException) { threw = true; }
    Check(threw, "an empty count region is refused with a clear error");
    var endLines = new List<string>{ "Screenshot in", "Dungeon", "Steamer Crazy (Awakened)", "Quest Dungeon Cleared!", "Time :7 min(s) 44 sec(s)", "You successfully stopped the locomotive.", "Dungeon Point Gained: 5", "Dungeon Point Accumulated: 325" };
    var rr = FarmCheck.ParseEndWindow(endLines);
    Check(rr != null && rr.Dungeon=="Steamer Crazy (Awakened)" && rr.Seconds==464 && rr.Dp==5, "end window parsed: Steamer Crazy (Awakened), 7:44, 5 DP");
    var merged = FarmCheck.ParseEndWindow(new List<string>{ "Dungeon", "Frozen Clue (Awakened)", "Quest Dungeon Cleared! Time :1 min(s) 5 sec(s)", "Dungeon Point Gained: 5" });
    Check(merged != null && merged.Seconds==65 && merged.Dungeon=="Frozen Clue (Awakened)", "end window parsed when the reader merges lines");
    Check(FarmCheck.ParseEndWindow(new List<string>{ "Dungeon", "Enter Guild Dungeon 6" }) == null, "other windows are ignored");
    var p1 = new List<string>{ "Obtain Upgrade Core Set (High) x 1", "Obtain Upgrade Core Set (Highest) x 1", "Obtain Faded Orange Jewel x 1" };
    var p2 = new List<string>{ "Obtain Upgrade Core Set (Highest) x 1", "Obtain Faded Orange Jewel x 1", "Obtain Fire Stone x 1", "Obtain Slot Extender (High) x 1" };
    Check(string.Join("|", FarmCheck.NewLines(p1, p2))=="Obtain Fire Stone x 1|Obtain Slot Extender (High) x 1", "loot feed: only lines added since last read");
    Check(FarmCheck.ParseLoot("Obtain Faded Orange Jewel x 2")?.item=="Faded Orange Jewel" && FarmCheck.ParseLoot("Obtain Faded Orange Jewel x 2")?.qty==2, "loot line parsed");
    {
      // end window and loot feed follow the UI scale (FarmCheck.ScaleArea)
      var scr = new System.Drawing.Rectangle(0, 0, 2560, 1440);
      var endA = new System.Drawing.Rectangle(209, 284, 630, 745); var lootA = new System.Drawing.Rectangle(2105, 1195, 395, 160);
      var e6 = FarmCheck.ScaleArea(endA, 1.0, 0.6, scr, AreaAnchor.ScreenCentre);
      double ecx = e6.X + e6.Width / 2.0, ecy = e6.Y + e6.Height / 2.0;
      Check(e6.Width == 378 && e6.Height == 447 && Math.Abs(ecx - (1280 + (524 - 1280) * 0.6)) <= 1 && Math.Abs(ecy - (720 + (656.5 - 720) * 0.6)) <= 1,
            $"end window at UI scale 0.6: 378x447, its centre's offset from the screen centre x0.6 ({e6})");
      var cen = FarmCheck.ScaleArea(new System.Drawing.Rectangle(965, 348, 630, 744), 1.0, 0.6, scr, AreaAnchor.ScreenCentre);
      Check(Math.Abs(cen.X + cen.Width / 2.0 - 1280) <= 1 && Math.Abs(cen.Y + cen.Height / 2.0 - 720) <= 1 && cen.Width == 378, $"a centred end window stays centred on the screen at 0.6 ({cen})");
      var l6 = FarmCheck.ScaleArea(lootA, 1.0, 0.6, scr, AreaAnchor.BottomRight);
      Check(l6.Width == 237 && l6.Height == 96 && 2560 - l6.Right == 36 && 1440 - l6.Bottom == 51,
            $"loot feed at UI scale 0.6: 237x96, bottom-right 60,85 px from the corner -> 36,51 ({l6})");
      var mon2 = new System.Drawing.Rectangle(2560, -200, 1920, 1080);
      var l2 = FarmCheck.ScaleArea(new System.Drawing.Rectangle(4000, 700, 400, 160), 0.8, 0.4, mon2, AreaAnchor.BottomRight);
      Check(l2.Width == 200 && l2.Height == 80 && mon2.Right - l2.Right == 40 && mon2.Bottom - l2.Bottom == 10, $"bottom-right anchor on a second monitor, from scale 0.8 to 0.4 ({l2})");
      var eBack = FarmCheck.ScaleArea(e6, 0.6, 1.0, scr, AreaAnchor.ScreenCentre); var lback = FarmCheck.ScaleArea(l6, 0.6, 1.0, scr, AreaAnchor.BottomRight);
      Check(Math.Abs(eBack.X - endA.X) <= 2 && Math.Abs(eBack.Y - endA.Y) <= 2 && Math.Abs(eBack.Width - endA.Width) <= 2 && Math.Abs(eBack.Height - endA.Height) <= 2
            && Math.Abs(lback.Right - lootA.Right) <= 2 && Math.Abs(lback.Bottom - lootA.Bottom) <= 2 && Math.Abs(lback.Width - lootA.Width) <= 2,
            "scaling back to 1.0 gives the areas again (within rounding)");
      Check(FarmCheck.ScaleArea(endA, 1.0, 1.0, scr, AreaAnchor.ScreenCentre) == endA && FarmCheck.ScaleArea(lootA, 0.6, 0, scr, AreaAnchor.BottomRight) == lootA,
            "same scale or unknown scale: area unchanged");
      var mons = new[] { scr, new System.Drawing.Rectangle(2560, 0, 1920, 1080) };
      Check(FarmCheck.MonitorFor(new System.Drawing.Rectangle(2900, 400, 300, 200), mons, new System.Drawing.Rectangle(0, 0, 4480, 1440)) == mons[1]
            && FarmCheck.MonitorFor(lootA, mons, System.Drawing.Rectangle.Empty) == scr
            && FarmCheck.MonitorFor(new System.Drawing.Rectangle(5000, 5000, 10, 10), mons, new System.Drawing.Rectangle(0, 0, 4480, 1440)) == new System.Drawing.Rectangle(0, 0, 4480, 1440),
            "monitor holding an area: the one with its centre, else all screens");
      // 15% larger captures, clipped to the monitor
      var li = FarmCheck.InflateArea(lootA, 0.15, scr);
      Check(li == new System.Drawing.Rectangle(2075, 1183, 455, 184), $"loot capture 15% larger: 2075,1183 455x184 ({li})");
      var edge = FarmCheck.InflateArea(new System.Drawing.Rectangle(2400, 1350, 160, 90), 0.15, scr);
      Check(edge == System.Drawing.Rectangle.FromLTRB(2388, 1343, 2560, 1440), $"inflated at the monitor corner: clipped to it ({edge})");
      var neg = FarmCheck.InflateArea(new System.Drawing.Rectangle(-1920, 0, 100, 100), 0.15, new System.Drawing.Rectangle(-1920, 0, 1920, 1080));
      Check(neg == System.Drawing.Rectangle.FromLTRB(-1920, 0, -1812, 108), $"inflated at a left monitor's top-left edge: clipped ({neg})");
      Check(FarmCheck.InflateArea(new System.Drawing.Rectangle(9000, 9000, 50, 50), 0.15, scr) == new System.Drawing.Rectangle(9000, 9000, 50, 50), "area off the monitor: left as it is (never empty)");
      // config: end=x,y,w,h@scale, old lines without @scale = scale 1
      var pa = FarmCheck.ParseArea("209,284,630,745", out double as0);
      var pr2 = FarmCheck.ParseArea(FarmCheck.FormatArea(new System.Drawing.Rectangle(-50, 10, 378, 447), 0.6), out double as1);
      var pr3 = FarmCheck.ParseArea("1,2,3,4@abc", out double as2);
      Check(pa == endA && as0 == 1.0 && FarmCheck.FormatArea(endA, 1.0) == "209,284,630,745@1.0000", "area line without @scale reads as scale 1.0; written as 209,284,630,745@1.0000");
      Check(pr2 == new System.Drawing.Rectangle(-50, 10, 378, 447) && as1 == 0.6 && pr3 == new System.Drawing.Rectangle(1, 2, 3, 4) && as2 == 1.0
            && FarmCheck.ParseArea("1,2,3", out _) == null && FarmCheck.ParseArea("a,b,c,d@0.6", out _) == null && FarmCheck.ParseArea(null, out _) == null,
            "area@0.6 round-trips (negative x ok); bad scale = 1.0; bad rectangles ignored");
      // OCR of the larger capture: other text and cut lines around the feed / window don't break the parsing
      var lootRead = new List<string>{ "Obtain Fire St", "[Party] Bob: go", "Obtain Fire Stone x 1.", "Obtain Slot Extender (High) x 1", "HP 12345" };
      var ll = FarmCheck.LootLines(lootRead);
      Check(ll.Count == 2 && FarmCheck.ParseLoot(ll[0])?.item == "Fire Stone" && FarmCheck.ParseLoot(ll[0])?.qty == 1
            && string.Join("|", FarmCheck.NewLines(FarmCheck.LootLines(new List<string>{ "Obtain Fire Sto", "Obtain Fire Stone x 1" }), ll)) == "Obtain Slot Extender (High) x 1",
            "loot capture with a margin: only complete loot lines kept, the feed diff still finds only the new drop");
      var endMargin = new List<string>{ "Lv. 180 Somebody", "Channel 3", "Screenshot in", "Dungeon", "Steamer Crazy (Awakened)", "Quest Dungeon Cleared!", "Time :7 min(s) 44 sec(s)", "Dungeon Point Gained: 5", "Dungeon Point Accumulated: 325", "Exit", "12:34" };
      var rm = FarmCheck.ParseEndWindow(endMargin);
      Check(rm != null && rm.Dungeon == "Steamer Crazy (Awakened)" && rm.Seconds == 464 && rm.Dp == 5, "end window with other text around it (15% margin) still parsed");
    }

    // ---- Updater ----
    string sha = new string('a', 64);
    Check(Updater.TryParseInfo("2.1.0\r\n"+sha+"\r\n", out var v1, out var h1) && v1==new Version(2,1,0) && h1==sha, "version.txt parses (CRLF ok)");
    Check(!Updater.TryParseInfo("2.1.0", out _, out _) && !Updater.TryParseInfo("abc\n"+sha, out _, out _) && !Updater.TryParseInfo("2.1.0\nshort", out _, out _), "bad version.txt is rejected");
    string osha = new string('b', 32) + "0123456789abcdefABCDEF0123456789";
    Check(Updater.TryParseInfo("2.1.0\n"+sha+"\n"+osha+"\n", out var v3, out var h3, out var o3) && v3==new Version(2,1,0) && h3==sha && o3==osha
          && Updater.TryParseInfo("2.1.0\n"+sha, out _, out _, out var oc2) && oc2==null && Updater.TryParseInfo("2.1.0\n"+sha+"\n"+osha, out _, out _), "version.txt with 2 or 3 lines parses (3rd line: OCR package SHA-256)");
    Check(!Updater.TryParseInfo("2.1.0\n"+sha+"\nshort", out _, out _, out _) && !Updater.TryParseInfo("2.1.0\n"+sha+"\n"+new string('g', 64), out _, out _, out _)
          && !Updater.TryParseInfo("2.1.0\n"+new string('z', 64), out _, out _), "a 3rd line that isn't 64 hex characters (or a bad exe SHA) is rejected");
    Check(Updater.OcrPackageNeeded(osha, null) && Updater.OcrPackageNeeded(osha, new string('c', 64)) && !Updater.OcrPackageNeeded(osha, osha.ToUpperInvariant() + "\r\n") && !Updater.OcrPackageNeeded(null, null),
          "OCR package: fetched when ocr.sha is missing or differs, not when it matches or version.txt has no 3rd line");
    {
      byte[] Zip(params (string name, byte[] data)[] entries) { using (var ms = new System.IO.MemoryStream()) { using (var za = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true)) foreach (var (n, d) in entries) using (var es = za.CreateEntry(n).Open()) es.Write(d, 0, d.Length); return ms.ToArray(); } }
      var good = Zip(("tessdata/eng.traineddata", new byte[] { 1, 2, 3 }), ("x64/tesseract50.dll", new byte[] { 4 }), ("Tesseract.dll", new byte[] { 5 }));
      string goodSha = Updater.Sha256(good);
      Check(Updater.ValidateOcrZip(good, goodSha, out string e1) && e1 == null, "OCR package zip with the right SHA-256 and eng.traineddata is accepted");
      Check(!Updater.ValidateOcrZip(good, new string('0', 64), out string e2) && e2.Contains("SHA-256"), "OCR package zip with another SHA-256 is refused (" + e2 + ")");
      var noData = Zip(("x64/tesseract50.dll", new byte[] { 4 }));
      Check(!Updater.ValidateOcrZip(noData, Updater.Sha256(noData), out string e3) && e3.Contains("eng.traineddata"), "OCR package zip without tessdata/eng.traineddata is refused (" + e3 + ")");
      var slip = Zip(("tessdata/eng.traineddata", new byte[] { 1 }), ("../evil.dll", new byte[] { 6 }));
      Check(!Updater.ValidateOcrZip(slip, Updater.Sha256(slip), out string e4) && e4.Contains("outside"), "OCR package zip with a path outside ocr/ is refused (" + e4 + ")");
      Check(!Updater.ValidateOcrZip(new byte[] { 1, 2, 3 }, Updater.Sha256(new byte[] { 1, 2, 3 }), out string e5) && e5 != null, "a broken OCR package zip is refused (" + e5 + ")");
      string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cah-ocr-test-" + Guid.NewGuid().ToString("N"));
      try {
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(tmpDir, "ocr", "x64"));
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(tmpDir, "ocr", "x64", "tesseract50.dll"), new byte[] { 4 });   // same bytes: left alone
        string ocrDir = System.IO.Path.Combine(tmpDir, "ocr");
        int written = Updater.InstallOcrPackage(good, goodSha, ocrDir);
        Check(written == 2 && System.IO.File.ReadAllBytes(System.IO.Path.Combine(ocrDir, "tessdata", "eng.traineddata")).SequenceEqual(new byte[] { 1, 2, 3 })
              && System.IO.File.Exists(System.IO.Path.Combine(ocrDir, "Tesseract.dll")) && System.IO.File.ReadAllText(System.IO.Path.Combine(ocrDir, "ocr.sha")).Trim() == goodSha
              && !Updater.OcrPackageNeeded(goodSha, Updater.ReadLocalOcrSha(ocrDir)), "OCR package installs into ocr/ (unchanged files skipped), then ocr.sha matches");
        bool refused = false; try { Updater.InstallOcrPackage(good, new string('0', 64), ocrDir); } catch (System.IO.InvalidDataException) { refused = true; }
        Check(refused && Updater.ReadLocalOcrSha(ocrDir) == goodSha, "a mismatching OCR package is never extracted");
      } finally { try { System.IO.Directory.Delete(tmpDir, true); } catch { } }
    }
    {
      // Debug overlay: a fresh icon read (Count null) keeps the last reader answer as Pending instead of flashing red
      var known = new Dictionary<(int r, int c, string item), int?>();
      var done = new List<SlotRead> { new SlotRead { Row = 0, Col = 0, Item = "A", Count = 12 }, new SlotRead { Row = 0, Col = 1, Item = "B", Count = null } };
      FarmCheck.RecordCounts(done, known);
      var m0 = FarmCheck.MergeCountsForDisplay(done, known);
      Check(known[(0,0,"A")] == 12 && known.ContainsKey((0,1,"B")) && known[(0,1,"B")] == null
            && m0[0].State == SlotCountState.Read && m0[0].Count == 12 && m0[1].State == SlotCountState.Failed, "overlay: reader answer = Read (map updated), reader \"not read\" = Failed");
      var fresh = new List<SlotRead> { new SlotRead { Row = 0, Col = 0, Item = "A" }, new SlotRead { Row = 0, Col = 1, Item = "B" }, new SlotRead { Row = 1, Col = 0, Item = "C" } };
      var m1 = FarmCheck.MergeCountsForDisplay(fresh, known);
      Check(m1[0].State == SlotCountState.Pending && m1[0].Count == 12 && m1[1].State == SlotCountState.Failed
            && m1[2].State == SlotCountState.Pending && m1[2].Count == null, "overlay: fresh read keeps the known count as Pending, a failed slot stays Failed, a new slot waits");
      var moved = new List<SlotRead> { new SlotRead { Row = 0, Col = 0, Item = "D" } };
      FarmCheck.ForgetMissingCounts(moved, known);
      var m2 = FarmCheck.MergeCountsForDisplay(moved, known);
      Check(known.Count == 0 && m2[0].State == SlotCountState.Pending && m2[0].Count == null, "overlay: a slot that empties or changes item forgets its last count");
      moved[0].Count = 3; FarmCheck.RecordCounts(moved, known);
      Check(known.Count == 1 && known[(0,0,"D")] == 3 && FarmCheck.MergeCountsForDisplay(moved, known)[0].State == SlotCountState.Read, "overlay: the next reader answer is Read and remembered");
    }
    Check(new Version(2,1,0) > new Version(2,0,9) && !(new Version(2,1,0) > new Version(2,1,0)), "only newer versions update");
    Check(Updater.Short(new Version(2,1,3,0))=="2.1.3", "version shown as 2.1.3");
    Console.WriteLine(fail==0?"ALL PASS":fail+" FAILED"); return fail;
  }
}
