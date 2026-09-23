using System;

namespace CAHelper
{
    // Counting: waiting for the 2nd CA boss.  BossUp: boss spawned, clock running on the 1st CA's re-entry window.
    public enum Phase { Off, Counting, BossUp, Expired }

    public struct AlertState
    {
        public Phase Phase;
        public TimeSpan ToBoss;    // time until the 2nd CA boss spawns
        public TimeSpan Left;      // time left to re-enter the 1st CA (negative once expired)
        public bool LastCall;      // inside the final warning window
        public bool Flash;         // show the full-screen flash
        public bool Beep;          // play the alert sound this tick
    }

    /// Pure timing rules, kept separate from the UI so they can be tested on any OS.
    public sealed class AlertTracker
    {
        public static readonly TimeSpan FlashAfterExpiry = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan BeepEvery = TimeSpan.FromSeconds(2);

        bool bossBeepDone, lastCallBeepDone;
        DateTime lastBeep = DateTime.MinValue;

        public void Reset() { bossBeepDone = lastCallBeepDone = false; lastBeep = DateTime.MinValue; }

        /// elapsed: time since the hotkey was pressed at the channel switch (null = stopped).
        /// idle: time since the last mouse/keyboard input.  gameFocused: the game window is in front.
        public AlertState Evaluate(TimeSpan? elapsed, TimeSpan idle, bool gameFocused, Settings s, DateTime now)
        {
            var st = new AlertState();
            if (elapsed == null) { st.Phase = Phase.Off; Reset(); return st; }

            st.ToBoss = s.BossSpawnAfter - elapsed.Value;
            st.Left = s.ReentryWindow - elapsed.Value;
            if (st.Left <= TimeSpan.Zero) st.Phase = Phase.Expired;
            else if (st.ToBoss <= TimeSpan.Zero) st.Phase = Phase.BossUp;
            else st.Phase = Phase.Counting;
            if (st.Phase == Phase.Counting) return st;

            st.LastCall = st.Left <= s.FinalWarning;
            bool distracted = idle >= s.IdleAfter || (s.FlashWhenGameNotFocused && !gameFocused);
            st.Flash = (distracted || s.FlashWhenActive || st.LastCall) && st.Left > -FlashAfterExpiry;

            if (s.Sound)
            {
                if (!bossBeepDone) { st.Beep = true; bossBeepDone = true; lastBeep = now; }
                else if (st.LastCall && !lastCallBeepDone) { st.Beep = true; lastCallBeepDone = true; lastBeep = now; }
                else if (st.Flash && now - lastBeep >= BeepEvery) { st.Beep = true; lastBeep = now; }
            }
            return st;
        }

        public static string Format(TimeSpan t)
        {
            var a = t.Duration();
            return $"{(int)a.TotalMinutes}:{a.Seconds:00}";
        }
    }
}
