# Cabal Helper

Small always-on-top helpers that float over Cabal (PlayCabal), for Windows 10/11:

- **CA Runner**: timer for the two-channel Chaos Arena trick. Flashes the screen when the 2nd boss is up and you are not looking.
- **Daily To-do**: daily/weekly checklist with + / - counters, filtered by your CP, most valuable first, with a "why" tooltip per task.
- **Alarms**: your own alarms (title + time + days), with a warning before. E.g. GDG every day at 19:30.
- **Party Check**: reads the party member names from the screen. Fix the party once, press Validate after each re-form to spot snipers.

It never reads, hooks or sends anything to the game.

## PlayCabal rules

Checked against [playcabal.to/tos](https://playcabal.to/tos) (2026-09-23): the rules don't mention overlays or timers.
They ban cheating, **macros for active grinding** and **AFK DP farming**. This helper sends no keys or clicks to the
game and reads nothing from it, so it is not a macro. You still play every run yourself: stay at the PC during the
CA wait. When in doubt, ask a GA on the PlayCabal Discord.

## Download (once)

1. Download [`release/CabalHelper.exe`](https://github.com/Vladisls/private-helper/raw/main/release/CabalHelper.exe).
2. Put it in its own folder, e.g. `C:\Games\CabalHelper\`, and double-click it.
   SmartScreen may say "unrecognized app": **More info > Run anyway** (the exe is not code-signed).

## Updates

On every start the helper checks `release/version.txt` on the `main` branch. If a newer version is there it downloads
`release/CabalHelper.exe`, checks its SHA-256 and restarts itself. Offline, it just starts normally.
Tray icon > **Check for updates** does the same on demand. Turn it off with `AutoUpdate=false` in `cabal-helper.ini`.
Settings, the to-do list and progress live next to the exe and are kept across updates.

## Publishing a new version

```
./build.sh          # runs tests, bumps the patch version, builds release/CabalHelper.exe + version.txt
git add -A && git commit -m "..." && git push
```

Builds on macOS or Linux with the .NET SDK (targets .NET Framework 4.8, which every Windows 10/11 has).

## Usage

```
- CA Runner: timer for the two-channel Chaos Arena trick, flashes the screen when the boss is up.
- Daily To-do: your daily/weekly checklist with + / - counters; finished rows fold into "Done".

INSTALL (on the gaming PC, Windows 10/11, nothing else needed)
1. Put CabalHelper.exe in its own folder, e.g. C:\Games\CabalHelper\
   (it saves its settings, list and progress next to the exe).
2. Double-click it. SmartScreen may say "unrecognized app": "More info" > "Run anyway"
   (the exe is not code-signed).
3. A round blue "+" button appears near the top right of the screen, and a "+" icon in the tray.
4. Run Cabal in WINDOWED or BORDERLESS mode. In exclusive fullscreen nothing can be drawn over
   the game.

THE "+" BUTTON
- Click it to pick a helper. Drag it anywhere. Hide it from its menu; the tray icon brings it back.
- Helpers are dragged by their title bar and closed with the x. Positions and open helpers are
  remembered.
- Clicking helper buttons never takes focus away from the game: your keyboard stays in Cabal.

CA RUNNER (two-channel Chaos Arena)
1. Channel 2: open CA, break the gates.
2. Change server to channel 3. When the game says you have 9 minutes to re-enter:
   click Start (or press Ctrl+F9). The panel shows "2nd CA boss spawns in 6:00".
3. Open the 2nd CA and wait at the PC. Alt-tabbing is fine, walking away is not: PlayCabal's
   rules ban staying AFK in a dungeon ("all characters must be actively engaged").
4. At 6:00 (2nd CA clock 14:00) the boss is up: beep, the panel blinks red with the time left
   to re-enter the 1st CA. If you have not touched mouse/keyboard for 15 s or another window is in front,
   the whole screen flashes red and beeps until you are back in the game.
5. Kill the boss, change server to channel 2, re-enter the 1st CA (press Exit, not Challenge),
   then click Reset (Ctrl+F10).
6. At 1:00 left it flashes even while you fight (last call).
Next pair of instances: Start again.

DAILY TO-DO
- Set your CP once (click "Your CP" at the top of the panel, or tray > Settings).
  The panel then shows only tasks you can do at that CP, most valuable first:
  quick dailies, then Chaos Arena (while you still need fragments), then farms by
  alz per hour (DP counted at 250k alz each).
- Tasks that need more CP fold into "Needs more CP", with the CP each one needs,
  so you can see what unlocks next. Update your CP as it grows.
- Hover a task to see why you do it, the CP it needs and its value.
- Click + or - on a row. Right-click a task name to set it straight to done (or back to 0).
- Finished rows move into "Done (n)" at the bottom; click it to fold/unfold.
- "List > Edit list in Notepad" to change tasks. One task per line:
      name ; how many ; daily or weekly ; min CP ; value ; why
      Awakened IC1 ; 30 ; daily ; 550k ; 214 ; 7 DP per run
  Save and the panel updates by itself. Some minimum CPs are estimates (marked "est."
  in the tooltip): fix them in the list when the game tells you otherwise.
- The built-in list updates with the helper. To keep your own edits, change the first
  line of the list file to "# preset: Custom".
- Daily tasks reset at the daily reset time, weekly ones on the weekly reset day (Settings).

ALARMS
- "+ Add alarm": title, time, days, how many minutes before to warn.
  Type the time in your PC's time or in server time; the dialog shows both.
- Starts with GDG at 19:30 (server 18:30), every day, warning 5 minutes before.
- Warning: beep, tray message and the row blinks red. At the time: beeps and a short
  click-through red flash over the game.
- Alarms ring even when the Alarms panel is closed, as long as the helper runs.
- Click an alarm to edit, switch off or delete it.
- Server time offset (Settings): server clock minus your PC clock. Default -1:00, because the
  server shows 18:30 when an Estonian PC shows 19:30. Check it again when clocks change for
  summer/winter time if the server doesn't follow the same change.

PARTY CHECK (GDG party re-forms)
- Area & roster > Set area: drag a box around the member list, including the
  "member (19/25)" line. Done once; the list stays in the same place.
- Make every name visible. If the chat box covers the bottom of the list, make it smaller.
- Fix party: while the party is right. The names are read and saved as the roster.
- Validate: after each re-form. Shows "All 16 are the fixed party", or NEW: <name> (a
  sniper) and Missing: <name>. The order of names doesn't matter.
- If the window says more members than were read, it warns you instead of saying OK.
- Look-alike letters (I/l, O/0) count as the same name. Close misreads are listed as
  "close matches" instead of alarms.
- Misread a name? Area & roster > Edit roster, one name per line.
- "Show what was read last time" and "Open last capture image" show exactly what the
  reader saw. Only the last capture is kept, in cabal-helper-party-last.png next to the exe.
- Reading uses Windows' built-in text recognition: offline, free, nothing is sent anywhere.
  It needs the English language (Settings > Time & language > Language).
- It only reads on your click. It never kicks, invites or presses anything in the game.

SETTINGS WINDOW (tray > Settings, or the "+" menu)
Your CP, CA Runner timings, flash and sound options, hotkeys, to-do reset times, auto-update.
Saved to cabal-helper.ini next to the exe.

TRAY ICON (right-click)
Open helper, Show "+" button, Test flash, Settings, Edit settings file, Reload settings,
Check for updates, Restart (fresh start, also checks for updates), Exit.

SETTINGS FILE (cabal-helper.ini: the Settings window writes it; hand edits need tray > Reload settings)
- ReentryWindow   1st CA re-entry time shown when you change channel. Default 9:00
- BossSpawnAfter  Start -> 2nd CA boss spawn. Default 6:00. Adjust after one run if you
                  press Start a bit before/after the gate break.
- FinalWarning    flash even while fighting when this much is left. Default 1:00
- IdleAfter       no input this long counts as AFK. Default 0:15
- FlashWhenGameNotFocused / GameMatch
                  flash when another window is in front. GameMatch = part of the game's process
                  name or window title (default "cabal"). If it flashes while you are in the game,
                  put the real exe name here (Task Manager > Details).
- FlashWhenActive true = flash from boss spawn even while you play. Default false
- RestartHotkey / StopHotkey  default Ctrl+F9 / Ctrl+F10 (e.g. Alt+Q, Pause, Ctrl+Shift+1)
- DailyResetTime / WeeklyResetDay  default 00:00 / Tuesday

TROUBLESHOOTING
- Hotkeys or clicks do nothing while in the game: Cabal probably runs as administrator.
  Run CabalHelper.exe as administrator too (right-click > Run as administrator).
- Safe by design: it never reads, hooks or sends anything to the game. It only uses a registered
  hotkey (like Discord/OBS), Windows' "time since last input" counter and the name of the
  window in front.
```
