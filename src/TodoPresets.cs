using System;

namespace CAHelper
{
    /// Built-in task catalog. Every task has a minimum CP and a value score; the panel hides tasks above
    /// your CP and sorts the rest by value. Numbers come from PLAN-force-gunner-solo.md §4.1/§4.2 and live findings.
    /// Line format: name ; count ; daily|weekly ; min CP ; value ; why
    public static class TodoPresets
    {
        public const int CatalogVersion = 8;
        public static string CatalogName => "Catalog v" + CatalogVersion;

        public static readonly string Catalog =
"# preset: " + CatalogName + @"
# One list for every CP. The panel hides tasks above your CP and sorts the rest by value.
# Format: name ; how many ; daily, action or weekly ; min CP ; value ; why (tooltip)
# 'action' = quick daily action (vote, claims, cash shop): shown in its own 'Daily actions' section.
# Value = alz per hour + DP at 250k alz each, in millions (from the plan's dungeon table).
# Quick dailies use 900+ so they come first. Min CP marked 'est.' is a guess: fix it if the game says otherwise.
# To keep your own edits when the helper updates, change the first line to: # preset: Custom

Vote + Voter Sigil ; 1 ; action ; 0 ; 990 ; Free daily rewards and CP buff. Voter Sigil comes from Event Girl Yul.
Guild treasure claim ; 1 ; action ; 0 ; 985 ; Guild window > Guild Treasure > Receive all (guild main character must be set). Elixir material buys honor potions in the Guild Shop.
Daily cash shop (GM buff) ; 3 ; action ; 0 ; 975 ; GM Blessing lv5 from the daily cash shop, 3 per day. Pop it before hard runs: one failed CA run loses that tier's reward for the day.
Chloe token quest ; 1 ; action ; 0 ; 970 ; New Material Development Support at Chloe: craft from 3x Upgrade Core (Highest), hand in with the Remote NPC button. 15 tokens pay Tempus' Ring registration instead of 300M. Never buy tokens for DP.
Daily dungeons (next to minimap) ; 3 ; action ; 0 ; 965 ; The 3 dungeons that change every day, opened from the button next to the minimap.
Mission War ; 1 ; daily ; 0 ; 900 ; War XP -> honor at the Morrison officer (5,000 WEXP = 45M honor). Merit tickets -> evaluate and report medals for more honor. Honor class 20 unlocks the Honor Medal.

CA1 runs ; 5 ; daily ; 0 ; 505 ; Only for the fragment/DF milestone: 5 clean runs = 30 fragments. Go as fast as possible with the two-channel trick and CA Runner. On return press Exit, never Challenge. Delete the CA rows once your DF set is done.
CA2 runs ; 5 ; daily ; 250k ; 504 ; Fragment milestone only: 5 clean runs = 30 fragments. Real difficulty wall around 250k CP. One failed run loses the tier for the day, so re-buff first.
CA3 runs ; 5 ; daily ; 300k ; 503 ; Fragment milestone only: 5 clean runs = 30 fragments. Min CP est.
CA4 runs ; 5 ; daily ; 350k ; 502 ; Fragment milestone only: 5 clean runs = 42 fragments, the best tier. Min CP est.
CA5 runs ; 5 ; daily ; 400k ; 501 ; Fragment milestone only. Easy from ~420k CP. Skip CA6/CA7, they are nerfed for fragments.

Frozen Canyon (UDX) ; 30 ; daily ; 900k ; 250 ; First thing every day at 1.1M+: ~160M/h + 8 DP per run, plus 1-2 Faded Violet Jewels (60-80M) per ~30 runs. Menu requirement 900k CP, comfortable at 1.1-1.2M.
Altar of Sienna B1F ; 30 ; daily ; 200k ; 180 ; B tier (Chunky): after the S and A tier dungeons, 'if you still want to grind some more'. Slower than the others; 6 DP per run, secret room chest drops Prettiest Bracelets (extract and sell the pieces, ~0.8M each).
Altar of Sienna B2F ; 30 ; daily ; 400k ; 175 ; B tier (Chunky): 'quite valuable as well', same tier as B1F. Separate entries, so it doesn't use up B1F runs. Time 5 runs and note DP + drops to rate it properly. Min CP est.
Steamer Crazy ; 30 ; daily ; 141k ; 260 ; S tier (Chunky, Sep 2026): one of the 'very first dungeons that you do'. Run only Normal (10) + Hard (20) = 30; skip the 10 Easy. Best alz per minute, Upgrade Core Medium sells best. Fills the CA 6-minute waits.
Nearly Hatching Egg ; 40 ; daily ; 270k ; 255 ; S tier (Chunky, Sep 2026): same structure as Steamer Crazy (Easy/Normal/Hard, 40 runs) but drops upgrade cores; Upgrade Core Medium sells best. Menu requirement 270k CP.
Ever-heated Lava Stone (Awakened) ; 30 ; daily ; 390k ; 240 ; A tier (Chunky): 'the very second dungeons that you do'. 5 DP per run, under a minute when strong, only two bosses, drops highest-grade upgrade cores. His favourite Awakened DX. Min CP from the plan (~390k).
Frozen Clue (Awakened) ; 30 ; daily ; 390k ; 235 ; A tier (Chunky): Awakened frozen DX, played like Ever-heated Lava Stone: 5 DP per run, highest-grade upgrade cores. Not the same as Frozen Canyon. Name as in Chunky's video; min CP est.
EOP (Edge of Phantom) ; 30 ; daily ; 550k ; 220 ; Best of the Awakened three: 7 DP per run, ~85M floor, Faded Violet Jewel 60M. Extract only blue Palladium drops; green/violet give worthless Astral Core.
Awakened IC1 ; 30 ; daily ; 550k ; 214 ; ~130M/h + 7 DP per run. Slot Extender High drops sell 350-400M. Recommended ~600k.
Awakened IC2 ; 30 ; daily ; 550k ; 205 ; 7 DP per run, more AP and XP than IC1, gives myth XP.
EOD B1F ; 50 ; daily ; 300k ; 182 ; ~170M/h, 30 free + 20 buyable entries, 2 DP per run. Min CP est.
World Boss ; 2 ; daily ; 500k ; 170 ; 09:30 and 21:30. Force Gunner never misses, ideal content. Min CP est.
Seal of Darkness ; 90 ; daily ; 300k ; 157 ; ~597M + 270 DP but takes ~4 hours. Only on long sessions. Min CP est.
Hazardous Valley ; 40 ; daily ; 200k ; 190 ; ~120M/h + 2-4 DP per run, 40 runs. Include Easy: Upgrade Core Medium is the 2nd most valuable core. Chunky: 'an incredibly good dungeon'.
Forgotten Temple B1F ; 30 ; daily ; 300k ; 139 ; ~116M per 50 min. Rotation filler. Min CP est.
Holy Windmill ; 9 ; daily ; 0 ; 130 ; Wing dungeon (wiki: Holia Windhill), Lv130, any wing rank. 3 per day + 2 resets of 3 (resets cost Force Gems). Force wing XP, Essence of Wing (Rare), myth XP. SECRET CHEST: in the 3rd room a Mysterious Boy appears on the left while you clear the wisps (sometimes in a corner). Kill him at once, he vanishes after a few seconds. Then a secret chest waits after the final boss: Potion of Luck, random alz up to ~100M (Chunky got 1-10M). Check its AH price first: selling may beat drinking it.
Holy Kedrasil ; 6 ; daily ; 650k ; 150 ; Wing dungeon (wiki: Holia Keldrasil), Lv150 + wing rank Rare level 100. 2 per day + 2 resets. More force wing XP, Essence of Wing (Unique). Min CP est.: set the real one in Edit list.
Holy Shrine ; 6 ; daily ; 850k ; 170 ; Wing dungeon (wiki: Holia Tristy), Lv170 + wing rank Unique level 100. 2 per day + 2 resets. The last wing dungeon gives the most force wing XP, Essence of Wing (Epic). Min CP est.: set the real one in Edit list.
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
