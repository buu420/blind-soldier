# Fort Condor cursor steering input

Date: 2026-08-22

Scope: static, read-only analysis of the original x86 `ff7_en.exe` used by the
2013/FFNx runtime. No production code or deployed game files were changed.

Analyzed executable (the existing read-only Ghidra project):

- MD5: `72DF0999B2FAD9AE2AA721CE67D8C3AB`
- SHA-256: `4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`

## Finding 1 — module 9 consumes DirectInput immediate keyboard state

**Proved from native code.** The input chain is:

```text
FUN_005F4A47  module 9 main loop
  -> FUN_005FD958  Condor input/UI update
       -> FUN_005FADD4  collect Condor controls
            -> FUN_0041A21E  rebuild FFVII logical input
                 -> FUN_0041F55E  poll keyboard
                      -> IDirectInputDevice::GetDeviceState(0x100, 0x009ADAE4)
```

`FUN_0041F55E` loads the keyboard-device interface from `0x009ADA90` and calls
vtable slot `+0x24` with a 256-byte destination at `0x009ADAE4`. On
`DIERR_INPUTLOST` (`0x8007001E`) it reacquires through `FUN_0041F4F0` (vtable
slot `+0x1C`) and retries the same state read. These are
`IDirectInputDevice::GetDeviceState` and `Acquire`, not a Windows-message,
`GetKeyboardState`, or Raw Input path.

The initialization path independently identifies the interface. `FUN_0041F39C`
creates the system-keyboard device through the DirectInput interface at
`0x009ADED4`, installs the 256-byte keyboard data format, sets foreground and
nonexclusive cooperative level (`6`), and acquires the device.

## Finding 2 — FFVII's live key mapping sits between DirectInput and Condor

**Proved from native code.** `FUN_0041A21E` does not copy DirectInput bytes into
the Condor mask. It loops through 25 logical actions in each of three mapping
banks held at `0x009A85E8` (bank stride `0x64`). A mapping token below `0xDE`
indexes the 256-byte DirectInput state and is down when bit `0x80` is set.
Tokens `0xDF..0xE1` represent mouse buttons and `0xE3..0xF4` represent joystick
controls. Active mappings are ORed into the shared logical-held mask at
`0x009A85D4`; transitions are recorded at `0x009A85E0`.

`FUN_006CE6EC` reads a version byte and then all 300 mapping bytes from
`ff7input.cfg` into `0x009A85E8`. `FUN_006CE665` writes the same table. Therefore
FFVII's Config keyboard assignments are authoritative. Fixed arrow scan codes
work only when the player still has the corresponding arrow bindings.

The default keyboard mappings installed by `FUN_0041A96D` through
`FUN_0041A7EF` are:

| Logical direction | Condor bit | Default DirectInput token | Default physical key | Bank-0 mapping address |
| --- | ---: | ---: | --- | ---: |
| Up | `0x1000` | `0x48` | Numpad 8 | `0x009A8618` |
| Right | `0x2000` | `0x4D` | Numpad 6 | `0x009A861C` |
| Down | `0x4000` | `0x50` | Numpad 2 | `0x009A8620` |
| Left | `0x8000` | `0x4B` | Numpad 4 | `0x009A8624` |

The same logical-action slots in banks 1 and 2 are at `+0x64` and `+0xC8`.
`FUN_0041A21E` accepts any active bank. The values above are DirectInput's
physical-key identifiers, not Windows virtual-key codes. In particular they are
the **nonextended numeric-keypad keys**, not the dedicated arrows. DirectInput's
dedicated-arrow identifiers are `0xC8`, `0xCD`, `0xD0`, and `0xCB`.

This matters to the existing sender. `Win32HighwayKeyboardInputSink` supplies
base scan codes `0x48/0x4D/0x50/0x4B` together with
`KEYEVENTF_EXTENDEDKEY` (`HighwayAutoSteeringController.cs:32-35,303-350`).
That synthesizes the dedicated arrows and therefore corresponds to live
DirectInput tokens `0xC8/0xCD/0xD0/0xCB`, **not** the default tokens in the
table above. It is correct for a player who remapped FFVII's directions to the
arrow keys; it is wrong for an untouched default mapping.

A safe steering implementation must resolve the current live keyboard token
for the requested logical direction; it must not assume either the keypad
defaults or the arrow remap. For the common directional tokens, translate a
DirectInput token below `0x80` to a nonextended `SendInput` scan-code event and
an extended token such as `0xC8` to base scan `0x48` plus
`KEYEVENTF_EXTENDEDKEY`. If no bank gives that direction a supported keyboard
token below `0xDE`, keyboard synthesis has no proven route to the requested
logical bit and steering should refuse audibly.

## Finding 3 — the live Condor acknowledgement mask is `0x00C72E80`

**Proved from native code.** `FUN_005FADD4` tests the shared logical masks via
`FUN_005FADB6 -> FUN_0041AB67` (held) and
`FUN_005FAFBB -> FUN_0041AB74` (pressed). `FUN_005FD958` then writes the
Condor-specific states:

| Address | Type | Meaning |
| ---: | --- | --- |
| `0x009A85D4` | `uint32` | shared FFVII logical-held mask |
| `0x009A85E0` | `uint32` | shared FFVII logical pressed/edge mask |
| `0x00C72E80` | `uint32` | current Condor held mask consumed this update |
| `0x00C74C4C` | `uint32` | previous Condor held mask |
| `0x00C74C54` | `uint32` | Condor rising edges: `(current ^ previous) & current` |
| `0x00C74C48` | `uint32` | Condor menu-repeat events |

For cursor steering, `0x00C72E80` is the primary acknowledgement: after
synthesizing the player's configured scan code, require the intended direction
bit there before allowing the steering loop to continue. `0x009A85D4` is a
useful upstream diagnostic. Absence of the intended bit after one or two module
updates must release the synthetic key and abort rather than continuing open
loop.

## Finding 4 — cursor repeat is driven by the held mask, not key transitions

**Proved from native code.** `FUN_005FD958` tests
`0x00C72E80 & 0xF000`. While no direction bit is held, it resets the repeat
counter at `0x00CBC7BC`. While any direction remains held it calls
`FUN_005FE771`; it does not require a new edge in `0x00C74C54`.

`FUN_005FE771` implements the acceleration itself:

- `0x005FE774..0x005FE792`: increment the counter, capped at 16;
- `0x005FE79B..0x005FE7A2`: dispatch one move on every update;
- `0x005FE7AC..0x005FE7C3`: dispatch a second move once the counter is at
  least 3;
- `0x005FE7CD..0x005FE7F3`: dispatch a third and fourth move once the counter
  is at least 4.

The resulting ramp is 1, 1, 2, 4, then 4 movement dispatches per module update
for as long as the logical direction bit stays set. This is FFVII's own hold
acceleration. A synthetic key which reaches the logical held mask therefore
behaves like a physical held key; Windows keyboard-repeat messages are not part
of this path.

## Finding 5 — `SendInput` is compatible with the path, but the game must acknowledge it

There is **no module-9-specific input source which rejects synthetic events**.
It reads the ordinary system keyboard through a foreground, nonexclusive
DirectInput device. `SendInput` with `KEYEVENTF_SCANCODE` inserts a hardware
scan-code event into the Windows keyboard input stream, and DirectInput's
keyboard state is indexed by physical scan code. The mechanisms therefore
match.

There are still two reasons not to call this an unconditional static proof:

1. Microsoft documents that `SendInput` is subject to UIPI and can inject only
   into a process at equal or lower integrity. Its documentation also explicitly
   says accessibility injection outside shell shortcuts is not guaranteed for
   arbitrary applications.
2. The proprietary Windows DirectInput implementation is not present in
   `ff7_en.exe`, so Ghidra can prove what FFVII requests from DirectInput but
   cannot prove how that OS component treats an injected event on every host.

As supporting implementation evidence, Wine's DirectInput keyboard backend
feeds its state from Raw Input or a low-level keyboard hook, converts the E0
extended flag into the high DIK bit, and does not filter `LLKHF_INJECTED` events.
That is consistent with `SendInput` reaching immediate DirectInput state, but it
is not a substitute for observing Microsoft's implementation in the running
game.

A small non-game probe in this worktree successfully created and acquired a
DirectInput keyboard and read its 256-byte immediate state. The managed terminal
host could not perform the injection: `SendInput` returned zero with error 5.
That run is therefore **inconclusive**, not evidence that FFVII rejects the
event. It did not touch or launch the game.

**Verdict:** steering is viable, but only as an acknowledged closed loop. Do
not treat a successful `SendInput` return as proof. The intended logical bit in
`0x00C72E80` must appear before the controller regards the key as held. If it
does not appear within one or two module updates, release immediately and say
that the game did not accept the direction. One marked live pulse supplies the
remaining host-specific proof without risking an open-loop movement.

## Finding 6 — required fail-safe pulse and hold protocol

For each requested direction:

1. Read its three live mapping slots at `0x009A85E8 + bank*0x64 +
   logicalIndex*4` and choose a supported keyboard token. Do not hardcode the
   default or the dedicated arrows.
2. Translate that DIK token to a scan-code `SendInput` event. The verified
   defaults `0x48/0x4D/0x50/0x4B` are nonextended keypad keys. The verified
   dedicated arrows `0xC8/0xCD/0xD0/0xCB` use base scans
   `0x48/0x4D/0x50/0x4B` plus `KEYEVENTF_EXTENDEDKEY`.
3. Send one key-down, then wait for a later module update. Require the intended
   bit in `0x00C72E80`; `0x009A85D4` can distinguish "Windows/DirectInput did
   not see it" from "FFVII did not copy it into Condor."
4. If the bit is absent after at most two module updates, send key-up, cancel
   the jump, and speak the failure. Never continue merely because `SendInput`
   returned one.
5. Once acknowledged, use cursor read-back for convergence. Abort and release
   on loss of focus, leaving module 9, leaving ordinary cursor control, a failed
   mask/cursor read, an unexpected or opposing direction bit, target loss,
   timeout, stall, divergence, or arrival.
6. Key-up belongs in every exit path. After releasing, require the owned bit to
   clear from `0x00C72E80` on a later update; a failure to clear is a diagnostic
   worthy of the strongest log message because it may mean a key remains held.

For a live confirmation capture, log one pulse in this order: mapping token,
mask before, `SendInput` result, `0x009A85D4`, `0x00C72E80`, cursor delta,
key-up result, and both masks after release. That single trace settles the
Windows-host question and validates the token translation.

## Finding 7 — consequence for the concurrent steering implementation

At current HEAD `3d1803b`, `CondorCursorSteering` calls the shared fixed-key
sender at `CondorCursorSteering.cs:207` and decides success from cursor
convergence, stall and divergence. It does not receive or verify either input
mask. `Win32HighwayKeyboardInputSink` still defines the fixed base scans at
`HighwayAutoSteeringController.cs:32-35` and always adds the extended flag at
`HighwayAutoSteeringController.cs:348-350`.

That implementation is not yet safe to deploy for Fort Condor: it targets the
dedicated arrows even when FFVII is using its default keypad bindings, and it
cannot distinguish an accepted hold from a refused injection until the much
later stall limit. The live mapping lookup and immediate mask acknowledgement
above close both defects.

## Direct answers

| Question | Answer |
| --- | --- |
| What populates the mask? | `FUN_0041F55E` calls `IDirectInputDevice::GetDeviceState(256, 0x009ADAE4)`; `FUN_0041A21E` applies `ff7input.cfg`; `FUN_005FADD4` and `FUN_005FD958` build the Condor mask. |
| Does `SendInput` reach it? | The scan-code mechanisms are compatible and no module-9 filter exists, but Microsoft does not guarantee arbitrary-app injection. Require the live mask acknowledgement; a one-pulse game capture is the final host proof. This is not a static "no." |
| Which keys? | Logical bits are Up `0x1000`, Right `0x2000`, Down `0x4000`, Left `0x8000`. Defaults are Numpad 8/6/2/4 (`DIK 48/4D/50/4B`), not arrows. `ff7input.cfg` remapping is authoritative. |
| Held or edge? | Held. `FUN_005FE771` accelerates from 1 to 4 dispatches per update while the bit remains set. |
| Live address? | Primary acknowledgement: `0x00C72E80`. Upstream diagnostic: `0x009A85D4`. Rising-edge masks: `0x00C74C54` and `0x009A85E0`. |

## Repeatable Ghidra pass

Run from the worktree root:

```cmd
analysis\ghidra\RunCondorSteeringInputEvidence.cmd
```

The runner reopens the existing read-only `CondorPlacement` project and runs
`analysis/ghidra/DumpCondorSteeringInputEvidence.java`. The script prints the
module chain, all global references, full instructions and decompilation for
the polling, mapping, config-load, Condor packing and repeat functions, plus
all three mapping addresses for each direction. It does not alter the analyzed
program.

## External API/source cross-checks

- [Microsoft `SendInput`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput): inserted input-stream events, UIPI limitation, and the explicit arbitrary-application caveat.
- [Microsoft `KEYBDINPUT`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput): scan-code and E0 extended-key semantics.
- [Microsoft `IDirectInputDevice8::GetDeviceState`](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee417897(v=vs.85)): immediate device state and the 256-byte keyboard format.
- [Microsoft, Interpreting Keyboard Data](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee418271(v=vs.85)): DirectInput uses physical scan codes rather than virtual keys.
- [Wine DirectInput keyboard backend](https://github.com/wine-mirror/wine/blob/master/dlls/dinput/keyboard.c): supporting open implementation showing Raw Input/low-level-hook ingestion and E0-to-DIK translation without an injected-event filter.
- [FFNx control-mapping answer](https://github.com/julianxhokaxhiou/FFNx/discussions/308): confirms that FF7 uses `ff7input.cfg` under FFNx.
