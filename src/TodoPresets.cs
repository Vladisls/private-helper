using System.Linq;

namespace CAHelper
{
    /// Built-in to-do lists per CP bracket (from PLAN-force-gunner-solo.md §4.2 and live findings).
    /// Format per line: name ; count ; daily|weekly ; why (shown as the row's tooltip)
    public static class TodoPresets
    {
        const string Login =
@"Vote + Voter Sigil ; 1 ; daily ; Free daily rewards and CP buff. Voter Sigil comes from Event Girl Yul.
GM buff ; 3 ; daily ; GM Blessing lv5, 3 per day. Pop it before hard runs: one failed CA run loses that tier's reward for the day.
Buy dungeon entries (Yul) ; 1 ; daily ; Event Girl Yul sells premium/DX entries for 1 alz each, no limit. Stock up before the dungeon block.
Chloe token quest ; 1 ; daily ; New Material Development Support at Chloe: craft it from 3x Upgrade Core (Highest), hand in with the Remote NPC button. 15 tokens pay Tempus' Ring registration instead of 300M. Never buy tokens for DP.
Guild treasure claim ; 1 ; daily ; Guild window > Guild Treasure > Receive all (guild main character must be set). Elixir material buys honor potions in the Guild Shop.
";
        const string Honor =
@"White Gold Fruits (Troglodyte) ; 1 ; daily ; Troglodyte secret shop (Lv180+) restocks daily. Trade fruits for honor potions. Pay with stain clones to keep Force Gems for the pass.
Mission War ; 1 ; daily ; War XP -> honor at the Morrison officer (5,000 WEXP = 45M honor, ~20% of class 16). Merit tickets -> evaluate and report medals for more honor. Honor class 20 unlocks the Honor Medal.
";
        const string CA =
@"CA1 runs ; 5 ; daily ; Only for the fragment/DF milestone: 5 clean runs = 30 fragments. Go as fast as possible, use the two-channel trick with CA Runner. On return press Exit, never Challenge.
CA2 runs ; 5 ; daily ; Fragment milestone only: 5 clean runs = 30 fragments. A single failed run loses the tier for the day, so re-buff first.
CA3 runs ; 5 ; daily ; Fragment milestone only: 5 clean runs = 30 fragments. Fast runs, two-channel trick.
CA4 runs ; 5 ; daily ; Fragment milestone only: 5 clean runs = 42 fragments, the best tier.
CA5 runs ; 5 ; daily ; Fragment milestone only. Easy from ~420k CP. Skip CA6/CA7, they are nerfed for fragments.
";
        const string Wind =
@"Wing dungeon ; 9 ; daily ; 3 free + 2 resets of 3. Kill the mysterious boy in 17-20 s for the secret chest (Potion of Alz 1M-100M). Gives myth XP too.
";
        const string Weekly =
@"Faded / Weakened weeklies ; 1 ; weekly ; Yul's Limited tab, resets weekly. Free Enchant Safeguards (not Chaos Safeguards) and Elixir of Myth.
Weak Map Part ; 3 ; weekly ; Peddler in Bloody Ice, resets Tuesday. Honor medals and war tickets.
Weak Epaulet of Dead ; 3 ; weekly ; Fri-Sun, 1 per day. Luminous Souls, superior cores, divine converters: 'the most important dungeon in the game'.
Event pass weekly missions ; 1 ; weekly ; Pass missions reset Tuesday. Check which ones need Heroic Holy Water (Arena Shop: 15 for 3 Baldus Tokens).
";

        public static readonly (string Name, string Text)[] All =
        {
            ("0-200k CP", Header("0-200k CP", "CA daily with the channel trick is the #1 farm: fragments + DP + cores. Goal: 500k fast.") + Login + CA +
@"Lake in Dusk ; 90 ; daily ; 90 DP and slot extender rolls, 25-35 s runs. Fragments from it are only a trickle.
Steamer Crazy ; 40 ; daily ; Best alz per minute. Fill the CA 6-minute waits with it.
" + Wind + Weekly),

            ("200k-500k CP", Header("200k-500k CP", "~2.5 h/day, ~400M alz + ~370 DP.") + Login + Honor + CA +
@"Steamer Crazy ; 30 ; daily ; 10 Normal + 20 Hard, skip the 10 Easy. Best alz per minute, fills CA waits.
Hazardous Valley ; 40 ; daily ; Include Easy: Upgrade Core Medium is the 2nd most valuable core. 2-4 DP per run.
Altar of Sienna B1F ; 30 ; daily ; Low-CP friendly, 6 DP per run, ~230M/hr: 'if you're lower CP, this dungeon all day, every day'.
" + Wind + Weekly),

            ("500k-1M CP", Header("500k-1M CP", "3-4 h/day. The AIC1/AIC2/EOP block is 630 DP/day.") + Login + Honor + CA +
@"Awakened IC1 ; 30 ; daily ; 7 DP per run (~600k CP recommended, test from 500k). Slot Extender High drops sell 350-400M.
Awakened IC2 ; 30 ; daily ; 7 DP per run, more AP and XP than IC1. Gives myth XP.
EOP (Edge of Phantom) ; 30 ; daily ; Best of the three: 7 DP, ~85M floor. Extract only the blue Palladium drops; green/violet give worthless Astral Core.
Steamer Crazy ; 30 ; daily ; Morning DX block: 10 Normal + 20 Hard.
Hazardous Valley ; 40 ; daily ; Morning DX block, include Easy for Upgrade Core Medium.
World Boss ; 2 ; daily ; 09:30 and 21:30. Force Gunner never misses, ideal content.
" + Wind + Weekly),

            ("1M+ CP", Header("1M+ CP", "Entry stock, not time, is the limiter. Buy weekly entries every Tuesday.") + Login + Honor +
@"Frozen Canyon (UDX) ; 30 ; daily ; First thing every day: quickest, most profitable Ultimate DX. 1-2 Faded Violet Jewels (60-80M) per ~30 runs.
Awakened IC1 ; 30 ; daily ; Keep for the DP: AIC1 + AIC2 + EOP = 630 DP/day.
Awakened IC2 ; 30 ; daily ; Keep for the DP and AP.
EOP (Edge of Phantom) ; 30 ; daily ; Keep for the DP. Extract only blue Palladium drops.
World Boss ; 2 ; daily ; 09:30 and 21:30.
Legend Arena (Baldus) ; 1 ; daily ; Guard Sebius, Bloody Ice. Top 30 Force Gunners get Baldus Tokens (~5M each on the AH).
" + Wind + Weekly),
        };

        static string Header(string name, string about) =>
            "# preset: " + name + "\n# " + about + "\n# Format: name ; how many ; daily or weekly ; why (hover a task to see it)\n\n";

        public const string DefaultName = "500k-1M CP";
        public static string Default => All.First(p => p.Name == DefaultName).Text;

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
    }
}
