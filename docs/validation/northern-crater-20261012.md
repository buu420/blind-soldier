# Northern Crater navigation: 0.9.6

## Supplied evidence

Downloads log `ff7_accessibility_steam2026_x64 (16).log` contains an exact
prefix copy of `(15)` and 7,443,759 additional bytes / 38,312 lines. Its loaded
assembly is 0.9.4, MVID `bbe665cb-6bb3-43e6-b21a-9d07f8efd574`.
The logged 749/750 loop and movie suppression also occur in the 0.9.5 source.

## Ledge and cave route

Field 749 has eighteen disconnected walkmesh components. The former catalog
offered sixteen descent LINEs everywhere. A reachable ledge could therefore
receive guidance for an unrelated ledge; choosing the lower exit from the main
side cave also returned to a west ledge with no onward descent.

The reviewed Story route now selects one native trigger on the current playable
component. The left upper ledge uses entity16's native `[OK]` climb back to the
branching ledge; navigation uses the existing facing/Confirm handoff. The west
dead end uses gateway5 into field750, then its gateway0 back to the upper ledge
and the right-hand descent. The normal Exits reader retains both cave doors.
Other components retain their native downward LINEs and the lower side passage.
Disabled LINEs remain unavailable, without falling back to another ledge.

All fourteen retained/new field749 LINE geometries independently match the
licensed script operands. The native selected jump/gateway graph reaches
field749's terminal component4, whose movement script maps to field751, without
cycles. Decorative component1 and the terminal component have no unrelated
player-controlled Story step.

## Playable movies

The log records `MovieActive=1` with `UserControl=0` in fields765/766. Both
licensed scripts set BGMOVIE(1), looping playback, and then release control.
The shared reader previously suppressed every movie before publishing targets.

Fresh Ghidra analysis of the licensed x86 executable proves:

- `0060BACF` binds script context `00CC0D88`.
- BGMOVIE handler `00614023` writes byte context+`0x3A`, address `00CC0DC2`.
- Field initialization `0060BCFA` clears that byte.
- Movement function `006342C6` and active field rendering `00644075` consume it
  during playback; rendering permits no active movie or background mode.

The checked shared reader samples that byte only during an active field movie,
bookends it with the rest of field ownership, and permits only the known
MovieActive=1/BG=1 combination. Script locks, modal windows, unknown values,
unreadable state and torn frames still suppress input. Native movie identity,
frame tracking and narration continue using the unchanged movie state.

Primary references: [BGMOVIE](https://wiki.ffrtt.ru/index.php/FF7/Field/Script/Opcodes/27_BGMOVIE)
and [FFNx field opcode handling](https://github.com/julianxhokaxhiu/FFNx/blob/master/src/ff7/field/opcode.cpp).
The implementation uses independently verified native facts; no FFNx code is copied.

## Verification boundary

The regression initially fails with sixteen targets on a single ledge and
navigation suppressed during background playback. Both focused runtime suites
pass after the repair. Native-data checks cover every 749/750 triangle, local
trigger reachability, exact gateway operands and cycle-free onward routes.
They verify final 765/766 platform paths using the native jump links; field766's
isolated scenery component6 has no native arrival or jump and is excluded.

Ownership checks cover normal movies, background playback, native control locks,
dialogue, stale counts, permanent windows, unknown modes, failed reads and changing
modes. The real Steam navigation coordinator speaks category selection during
background playback with a completed nonmodal window.

Release delivery additionally requires the full dual-runtime release gate,
exact-package suites, package/archive checks and protected deployment hashes.
Their completed results belong to the release evidence record. No live endgame
save, hardware controller response or screen-reader listening was exercised;
these offline checks do not establish player confirmation.
