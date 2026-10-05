# Submarine selection and pursuit evidence

Scope: the user's October 5 report and confirmed request for automatic pursuit and firing guidance. The released v0.8.3 is the baseline. Gold Saucer behavior is unchanged.

## Tester log

The newest Downloads file was `ff7_accessibility_steam2026_x64 (8).log` (463,734,488 bytes). Its first 459,828,115 bytes exactly match the preceding `(7)` download; investigation isolated the new 3,906,373-byte append instead of treating all historical sessions as new evidence.

The append contains the submarine return diagnostics added in v0.8.3. Two recorded failure returns advance Cloud's MESSAGE 85 window from phase 6 to 7, its program counter from 0x157F to 0x1587 and subsequently through 0x166E. Navigation menu and suppression are false in these returns. This does not establish the cause of the older reported freeze or a new fix for it. The read-only trace remains available.

The log also contains many torn-identity and unavailable-view samples. Captures now retry a bounded whole reading and name fields that changed. Neither missing data nor an unreadable projection is treated as an empty sea.

## Native evidence

Fresh Ghidra decompilation of the licensed legacy executable was compared with existing translated x64 evidence. Mission input is ordinary held/pressed game actions: Menu slot 4 accelerates, Cancel slot 6 brakes and Switch slot 7 fires. Up lowers the nose and Down raises it; Right increases yaw. The assist resolves the live three-bank control table, refuses movement keys that also invoke Fire, camera/view changes or native menu actions, and never requests Fire.

The twelve native enemy records carry separate visible marker and hull-model identities. Model 2 is the distinctive red leader and can occupy arcade slot 7; model 3 is the ordinary red hull. Only the story leader is named as carrying the Huge Materia. Current-camera projection and viewport bounds determine which sightings enter the selectable list. Sinking hulls are treated separately from active marker squares. Hidden health, waypoints and unseen motion do not update the tracker.

The native overview blinks contacts and cannot acquire a lock. Normal-view lock bit 0x800 is chosen by the game; selecting a target does not force that lock. Firing guidance requires the selected current visible contact to hold that bit and a loaded torpedo indicator. Another locked contact and reloading lamps are announced separately. An unshown free-torpedo object is also needed by the game, so the guidance describes the visible readiness state without promising a launch.

0xE73F18 is the inner-loop frame completion flag, not mission completion. It can change during normal play and is not an input-delivery gate. Result, active-run and session flags remain authoritative input boundaries.

## Integration and regression coverage

Both legacy x86 and Steam x64 use the same reader, pure pursuit tracker and coordinator. J/L select, P toggles pursuit, I starts and K repeats. R3 opens controller targets, D-pad selects, A/X starts and B/R3 from the list stops. Browsing releases movement. Inactive field and world owners cannot consume submarine commands or close its controller menu.

Tests cover all twelve selectable slots, story versus arcade leader identity, angle wrap and pitch direction, last-seen positions, overview sweeps, bounded expiry, sinking, hull contact, native wrong-target locks, reload-to-loaded guidance and bounded speech. The tracker also has an offline motion simulation based on researched turn and throttle behavior; its output is supporting synthetic evidence.

Host regressions cover pause/quit/result/module exit, unreadable instruments and view, focus loss, disabled speech and disposal. A key held through failed reads cannot become a new press on recovery. Reader faults release owned keys before escaping. Manual takeover stops pursuit; just-released owned keys have a short drain interval. Manual-play lock speech remains available, and simultaneous fire and hull-damage information are both spoken.

Final review regressions also verify that browsing preserves the lock baseline without repeating a fire prompt each tick; a pause between capture and input delivery suspends pursuit and resumes afterwards; a selection command cannot discard a hull-contact stop announcement; and returning focus preserves firing guidance alongside the introduction. These tests failed before the coordinator corrections and passed afterwards.

Steam x64 additionally rechecks the owner on the actual keyboard-state poll, preserves physical player input and blocks a physical Pause press before the translated game has published its pause flag. The ordinary frame flag does not reject valid input. Remapped Fire/menu aliases, partial-send cleanup and controller button draining have separate input tests.

Legacy system keyboard delivery also refuses movement bindings that would press the submarine or repeat-speech hotkeys themselves. The current Windows scan-to-virtual-key mapping determines those conflicts. This follows the actual system keyboard stream used by [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) and the key-down state sampled by [GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate). Steam's guest-buffer overlay does not synthesize those system hotkey events.

Release verification builds retained portable archives, runs four full suites against the exact packaged dependencies, audits both AMM packages and checks source/asset hashes. Detailed results and release hashes are retained in `C:\Users\buu42\Documents\FFVII-ActiveBuild\release-0.8.4-20261005`.

## Limits

No live gameplay was driven. Real native keyboard/SDL hook installation is not established by injected-hook tests; the local x64 test host reports its unavailable Reloaded.Hooks initializer explicitly. The Steam installation currently lacks FFVII.exe and SDL2.dll. Full static x64 checks use the matching licensed saved executable only as a temporary, fingerprinted fixture, with removal recorded afterwards. No game executable is distributed.

Gameplay feedback remains necessary for pursuing each submarine, the Huge Materia leader, controller behavior and actual shots on both runtimes. Automated tests do not establish that the older submarine return freeze is fixed.

Detailed log and Ghidra evidence: `C:\Users\buu42\Documents\FFVII-ActiveBuild\submarine-pursuit-20261005`. Public baseline: [v0.8.3](https://github.com/buu420/blind-soldier/releases/tag/v0.8.3).
