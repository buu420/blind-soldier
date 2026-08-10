# Auto Walk and Train Graveyard Implementation Plan

1. Record native evidence for field 145 train state and the game's directional input path with the existing field-script dumper and Ghidra.
2. Replace the Train Graveyard stable-exit regression with tests for state `0`, `3`, and `7`, then update the generated story catalog source and regenerate it.
3. Add focused failing tests for route-driven directional recommendations, `P` toggle semantics, key release, arrival, ladders, focus/frame loss, battle/transition recovery, and held-key cadence.
4. Generalize the proven motorcycle scan-code sender into a shared directional input owner without changing motorcycle behavior.
5. Integrate the auto-walk state machine with x86 and x64 field navigation, then the world-map navigation coordinators.
6. Update the hotkey documentation and local packages, run focused and broad regression suites, build both runtimes, inspect the diff, and deploy to the installed test game only after verification succeeds.
