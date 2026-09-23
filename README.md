# Cabal Helper

Small always-on-top helpers that float over Cabal (PlayCabal), for Windows 10/11:

- **CA Runner**: timer for the two-channel Chaos Arena trick. Flashes the screen when the 2nd boss is up and you are not looking.
- **Daily To-do**: daily/weekly checklist with + / - counters, CP-bracket presets and a "why" tooltip per task.

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
- Starts with the 500k-1M CP list. Click "List" at the bottom of the panel to load another
  CP bracket: 0-200k, 200k-500k, 500k-1M, 1M+. Your previous list is saved to
  cabal-helper-todo.backup.txt first, and counts carry over for tasks with the same name.
- Hover a task (the ones marked with the info sign) to see WHY you do it, e.g. CA runs are only
  for the 5-run fragment milestone, so do them as fast as possible.
- Click + or - on a row. Right-click a task name to set it straight to done (or back to 0).
- Finished rows move into "Done (n)" at the bottom; click it to fold/unfold.
- "List > Edit list in Notepad" to change tasks. One task per line:
      name ; how many ; daily or weekly ; why (optional tooltip)
      CA1 runs ; 5 ; daily ; Only for the fragment milestone, go fast
  Save and the panel updates by itself.
- Daily tasks reset at DailyResetTime, weekly ones on WeeklyResetDay (settings file).
  Default: 00:00 your PC's time, weekly on Tuesday.

TRAY ICON (right-click)
Open helper, Show "+" button, Test flash, Edit settings, Reload settings, Exit.

SETTINGS (cabal-helper.ini, then tray > Reload settings)
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
