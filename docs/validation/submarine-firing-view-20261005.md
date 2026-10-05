# Submarine firing-view correction

Scope: the user's new log and explicit confirmation, "Yes, switch to firing view
when pursuing." Version 0.8.4 is the baseline; its release work is complete.

## Evidence and cause

The Downloads file `ff7_accessibility_steam2026_x64 (9).log` contains the entire
463,734,488-byte `(8)` prefix and a new 2,824,622-byte append. Only the append was
used for the new failure investigation. Its six October 5 mission sessions
contain 61 pursuit starts, 19 overview warnings, 61 PageDown-return instructions,
53 hull-contact stops and no selected-lock firing prompts. The log's `Z` suffix
comes from formatting local `DateTime.Now`; it does not make those labels UTC.

The controller reports overview accurately. The defect was that pursuit did not
request a return to normal view and the guidance confused a logical game action
with a physical key. Legacy defaults bind the overview action to DIK 0x51
(numpad 3) and Target to DIK 0x4F (numpad 1). A dedicated Page Down key has a
different token. This does not prove which physical input the tester pressed.

Ghidra evidence for legacy `FUN_00798580` and translated x64
`FUN_7ff702b44180` agrees: overview is 0x9873A8, the overview action toggles it,
and Target (slot 1, mask 0x2) clears it while changing normal camera. New locks
cannot form in overview; a previously acquired lock can survive there. Fire
requires the game's lock and loaded lamp state, plus an unshown free torpedo
object, so speech describes visible readiness without promising a launch.

The Steam host's guest keyboard table is synthesized logical output. Its guest
tokens do not establish the host's physical keyboard/controller bindings. General
input contracts were checked against [Microsoft DirectInput documentation](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee418271(v=vs.85)),
[KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput)
and [SDL scancodes](https://wiki.libsdl.org/SDL2/SDL_Scancode).

The saved x64 executable's code is encrypted on disk. A bounded fresh headless
Ghidra run records that limitation; code meaning uses cached decompilation of the
decrypted image. Plain static data in the exact saved file matches that cache's
handler addresses, vtables and string tables. Together they verify submarine
host mode 0xB: defaults are R1/RB or E/Right Ctrl for
overview, R2/RT or T for Target, and Square/X or Z for Fire. These are defaults,
not a player's remapped settings. P is native Start/Pause; O is absent from the
default host table. The mod now uses O for pursuit toggle on both runtimes, with
I still starting pursuit. A coordinator regression first failed when O did not
start pursuit; the Win32 input guard also rejects injected O hotkey aliases.

## Implementation and checks

Close overview pursuit requests only the configured Target action, resolves all
three live mapping banks and refuses aliases with Fire, pause, quit/menu, depth,
overview or unrequested movement. The 100 ms press is followed by a bounded wait
for a coherent normal-view acknowledgement. No acknowledgement within two
seconds stops pursuit and explains the failure. There is no direct view-state
write. The native x64 keyboard poll also checks overview before each delivery,
retiring Target as soon as normal view appears, even before the worker observes
it. Pause/focus/unreadable boundaries release owned input.

The range used for the request comes only from a visible sighting or a bounded
fixed last-seen point. Distant overview pursuit and all visibility/expiry rules
remain. Fire guidance follows the currently visible selected native lock and
loaded lamps, including a lock retained in overview. The instrument readout
announces coherent view changes independently of pursuit.

Two coordinator regressions first failed against the baseline: no view action
was delivered, and six read-failure recoveries repeated the warning seven times.
Separate red tests reproduced missing manual-view speech and suppressed existing
overview-lock guidance. The focused x86 and x64 submarine suites then passed.
The x64 host test uses the real resolver, controller, coordinator, input sink
and injected native keyboard overlay: it observes Target in the game's buffer,
observes release on the first normal-view poll, preserves manual Fire and
requires the real selected lock before firing guidance. Additional cases cover
timeout, boundaries, remapped controls and cross-bank aliases.

Feature evidence is retained in
`C:\Users\buu42\Documents\FFVII-ActiveBuild\submarine-view-fix-20261005`.
Final release verification and exact package checks are retained separately in
`C:\Users\buu42\Documents\FFVII-ActiveBuild\release-0.8.5-20261005`.

## Limits

No live gameplay, physical controller or real torpedo launch was exercised.
Injected-overlay checks do not establish live native hook installation; the x64
test host explicitly reports the unavailable Reloaded.Hooks initializer. The
native Steam install lacks FFVII.exe and SDL2.dll. Full static tests use the
fingerprinted licensed saved executable as a temporary fixture and remove it
afterwards. No game executable is shipped. The older post-mission freeze is not
claimed fixed by this change.
