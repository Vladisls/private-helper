using System;

namespace CAHelper
{
    /// Built-in task catalog. Every task has a minimum CP and a value score; the panel hides tasks above
    /// your CP and sorts the rest by value. Numbers come from PLAN-force-gunner-solo.md §4.1/§4.2 and live findings.
    /// Line format: name ; count ; daily|weekly ; min CP ; value ; why
    public static class TodoPresets
    {
        public const int CatalogVersion = 2;
        public static string CatalogName => "Catalog v" + CatalogVersion;

        public static readonly string Catalog =
"# preset: " + CatalogName + @"
# One list for every CP. The panel hides tasks above your CP and sorts the rest by value.
# Format: name ; how many ; daily or weekly ; min CP ; value ; why (tooltip)
# Value = alz per hour + DP at 250k alz each, in millions (from the plan's dungeon table).
# Quick dailies use 900+ so they come first. Min CP marked 'est.' is a guess: fix it if the game says otherwise.
# To keep your own edits when the helper updates, change the first line to: # preset: Custom

Vote + Voter Sigil ; 1 ; daily ; 0 ; 990 ; Quick win, 1 min. Free daily rewards and CP buff. Voter Sigil comes from Event Girl Yul.
Guild treasure claim ; 1 ; daily ; 0 ; 985 ; Quick win. Guild window > Guild Treasure > Receive all (guild main character must be set). Elixir material buys honor potions in the Guild Shop.
GM buff ; 3 ; daily ; 0 ; 975 ; GM Blessing lv5, 3 per day. Pop it before hard runs: one failed CA run loses that tier's reward for the day.
Chloe token quest ; 1 ; daily ; 0 ; 970 ; New Material Development Support at Chloe: craft from 3x Upgrade Core (Highest), hand in with the Remote NPC button. 15 tokens pay Tempus' Ring registration instead of 300M. Never buy tokens for DP.
White Gold Fruits (Troglodyte) ; 1 ; daily ; 0 ; 960 ; Lv180+. Secret shop restocks daily. Trade fruits for honor potions; pay with stain clones to keep Force Gems for the pass.
Mission War ; 1 ; daily ; 0 ; 900 ; War XP -> honor at the Morrison officer (5,000 WEXP = 45M honor). Merit tickets -> evaluate and report medals for more honor. Honor class 20 unlocks the Honor Medal.

CA1 runs ; 5 ; daily ; 0 ; 505 ; Only for the fragment/DF milestone: 5 clean runs = 30 fragments. Go as fast as possible with the two-channel trick and CA Runner. On return press Exit, never Challenge. Delete the CA rows once your DF set is done.
CA2 runs ; 5 ; daily ; 250k ; 504 ; Fragment milestone only: 5 clean runs = 30 fragments. Real difficulty wall around 250k CP. One failed run loses the tier for the day, so re-buff first.
CA3 runs ; 5 ; daily ; 300k ; 503 ; Fragment milestone only: 5 clean runs = 30 fragments. Min CP est.
CA4 runs ; 5 ; daily ; 350k ; 502 ; Fragment milestone only: 5 clean runs = 42 fragments, the best tier. Min CP est.
CA5 runs ; 5 ; daily ; 400k ; 501 ; Fragment milestone only. Easy from ~420k CP. Skip CA6/CA7, they are nerfed for fragments.

Frozen Canyon (UDX) ; 30 ; daily ; 1.1m ; 250 ; First thing every day at 1.1M+: ~160M/h + 8 DP per run, plus 1-2 Faded Violet Jewels (60-80M) per ~30 runs. Minimum 800-850k, comfortable at 1.1-1.2M.
Altar of Sienna B1F ; 30 ; daily ; 200k ; 238 ; ~193M + 180 DP per hour. Low-CP friendly: 'if you're lower CP, this dungeon all day, every day'.
Steamer Crazy ; 30 ; daily ; 141k ; 228 ; Best alz per minute: ~200M/h. Do 10 Normal + 20 Hard, skip the 10 Easy. Fills the CA 6-minute waits.
EOP (Edge of Phantom) ; 30 ; daily ; 550k ; 220 ; Best of the Awakened three: 7 DP per run, ~85M floor, Faded Violet Jewel 60M. Extract only blue Palladium drops; green/violet give worthless Astral Core.
Awakened IC1 ; 30 ; daily ; 550k ; 214 ; ~130M/h + 7 DP per run. Slot Extender High drops sell 350-400M. Recommended ~600k.
Awakened IC2 ; 30 ; daily ; 550k ; 205 ; 7 DP per run, more AP and XP than IC1, gives myth XP.
EOD B1F ; 50 ; daily ; 300k ; 182 ; ~170M/h, 30 free + 20 buyable entries, 2 DP per run. Min CP est.
World Boss ; 2 ; daily ; 500k ; 170 ; 09:30 and 21:30. Force Gunner never misses, ideal content. Min CP est.
Seal of Darkness ; 90 ; daily ; 300k ; 157 ; ~597M + 270 DP but takes ~4 hours. Only on long sessions. Min CP est.
Hazardous Valley ; 40 ; daily ; 200k ; 152 ; ~120M/h + 2-4 DP per run. Include Easy: Upgrade Core Medium is the 2nd most valuable core.
Forgotten Temple B1F ; 30 ; daily ; 300k ; 139 ; ~116M per 50 min. Rotation filler. Min CP est.
Wing dungeon ; 9 ; daily ; 0 ; 130 ; 3 free + 2 resets of 3, ~4 min each. Kill the mysterious boy in 17-20 s for the secret chest (Potion of Alz 1M-100M). Force wing XP and myth XP.
Lake in Dusk ; 90 ; daily ; 80k ; 120 ; ~98M/h + 90 DP, 25-35 s runs, slot extender rolls. Fragments from it are only a trickle.
Legend Arena (Baldus) ; 1 ; daily ; 900k ; 60 ; Guard Sebius, Bloody Ice. Only the top 30 Force Gunners get Baldus Tokens (~5M each). At 518k you scored 200k vs a 600k floor. Min CP est.

Weak Epaulet of Dead ; 3 ; weekly ; 0 ; 400 ; Fri-Sun, 1 per day. Luminous Souls, superior cores, divine converters: 'the most important dungeon in the game'.
Faded / Weakened weeklies ; 1 ; weekly ; 0 ; 350 ; Yul's Limited tab, weekly reset. Free Enchant Safeguards (not Chaos Safeguards) and Elixir of Myth.
Event pass weekly missions ; 1 ; weekly ; 0 ; 300 ; Pass missions reset Tuesday. Heroic Holy Water for missions: Arena Shop, 15 for 3 Baldus Tokens.
Weak Map Part ; 3 ; weekly ; 0 ; 200 ; Peddler in Bloody Ice, resets Tuesday. Honor medals and war tickets.
";

        /// Lists written by older versions of the helper; they are replaced by the catalog automatically.
        static readonly string[] Retired = { "0-200k CP", "200k-500k CP", "500k-1M CP", "1M+ CP" };

        /// Reads the "# preset: X" header; returns "Custom" if the list was written by hand.
        public static string NameOf(string listText)
        {
            foreach (var raw in (listText ?? "").Split('\n'))
            {
                var l = raw.Trim();
                if (l.StartsWith("# preset:")) return l.Substring(9).Trim();
                if (l.Length > 0 && !l.StartsWith("#")) break;
            }
            return "Custom";
        }

        /// True when the saved list came from an old built-in list or an older catalog and should be refreshed.
        public static bool IsOutdated(string listText)
        {
            string n = NameOf(listText);
            if (Array.IndexOf(Retired, n) >= 0) return true;
            if (n.StartsWith("Catalog v") && int.TryParse(n.Substring(9), out int v)) return v < CatalogVersion;
            return false;
        }
    }
}
