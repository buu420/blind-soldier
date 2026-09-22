# Shinra Mansion safe dial speech

The reported x64 session opens the safe in field 299 (`sinin2_1`). Its log repeatedly
records window 0/dialog 23 and window 3/dialog 20, but only the clock is spoken.
These are blank text placeholders beneath native numeric displays.

The installed field scripts agree in both game archives:

- Entity 1 `disn`, script 3 opens window 0 as `WSPCL` type 2 and supplies its
  two-digit value with `WNUMB`, from bank 6/address 7.
- Entity 2 `time`, script 3 opens window 3 as a clock (type 1).
- The party leader's script 3 increments on held Right (`0x2000`), decrements on
  held Left (`0x8000`), wraps within 0–99, and confirms on fresh OK (`0x220`).
  It starts a twenty-second timer. Closing the dial resets its display type.
- The target combination, required direction, and correctness flags are never
  inputs to the speech readout. Only the currently displayed number is exposed.

Fresh read-only Ghidra inspection of `ff7_en.exe` confirms `WSPCL` at `0061FD5C`
writes the display type at `CFF5D3 + window*0x30`; `WNUMB` at `0061FE26` writes
the value at `CFF5D8` and digit count at `CFF5D5`, using the same stride.
`00631586` claims the window owner at `CC0960 + window`. The shared native
numeric-window reader uses these fields, and x64 resolves them through its
validated guest address space.

The community opcode references independently document the numeric display:
[WSPCL](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes/36_WSPCL)
and [WNUMB](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes/37_WNUMB).

Both runtimes use `ShinraMansionSafeDialReadout`. A held turn receives the latest
number about every 250 ms; a final settled number can be spoken after 120 ms.
There is no queue of intermediate dial values. Waiting for release before giving
any feedback was rejected in review: the player needs information to decide when
to stop turning. Repeating requests the current number rather than an old utterance.

The dial owns speech while open so the clock's interrupting final countdown cannot
cut numbers off. Those timer announcements are discarded rather than replayed
after closing. Ordinary Open/Don't Open and Success/Fail text remains readable.
An unreadable sample is not evidence of closure, and cannot supply a repeat or
retry value. Coherent closure, leaving the field, or disabling the feature releases
ownership. Native inputs, timer, and puzzle rules are unchanged.

The report's clock advances faster than wall time. Normal game speed gives more
time to hear and act on numbers; the mod does not change the game speed.

Final verification: both full Release executable test suites passed with their
installed game data, as did the focused safe tests and dual-runtime source check.
The held-turn regression was observed failing against the release-only draft and
passing after bounded live feedback was implemented. Numeric-window tests cover
coherent closure, wrong fields/modules, failed and torn reads, old-reader behavior,
and x64 guest-page translation. Delivery tests cover stale pending values and
ordinary dialogue ownership. Host construction still requires the game/Reloaded;
host wiring was reviewed rather than claimed as an end-to-end runtime test.

The dual-runtime package was built and installed into both local mod directories.
All 4,038 payload files matched their built hashes in both destinations, and
configuration hashes were preserved. Only rebuilt DLL/PDB files were added or
replaced; narration assets were unchanged. Previous binaries were backed up under
the local `safe-dial-20260921` repair folder. No GitHub release was made in this pass.

No live safe playthrough is claimed; these checks verify visible state, delivery
and native script evidence, rather than automatically solving the puzzle.
