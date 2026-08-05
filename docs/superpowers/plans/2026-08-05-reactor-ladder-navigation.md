# Reactor Ladder Navigation Repair Plan

**Goal:** Restore the Reactor 1 vertical route and add route-aware ladder
entry/direction feedback in both runtimes.

**Tech stack:** C#/.NET 8 and 9, native FFVII field scripts and walkmeshes,
Reloaded-II, NAudio/Steam Audio, Ghidra 12.0.4, PowerShell.

1. Add diagnostics and failing tests for `nmkin_3`/`nmkin_4` exits, ladder
   pairings, controller direction precedence, and ladder cue lifetime.
2. Fix missing-Z native ladder pairing without weakening boundary or
   reachability checks.
3. Add correctly gated Reactor 1 story targets for the save-room/core route and
   escape return.
4. Package `214.wav`, make the active route's next ladder the priority cue, and
   stop it on mount/reset/suppression.
5. Run focused and project verification, build x86/x64, stage the mod, deploy
   it to the active game, and inspect resulting package/log evidence.
