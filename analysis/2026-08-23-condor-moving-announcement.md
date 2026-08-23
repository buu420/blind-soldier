# Fort Condor cursor-jump speech, 2026-08-23

## Scope and preserved state

- Worktree: `fix/condor-probe-silence` at `82761f1` when this work began.
- The existing uncommitted navigation-beacon, configuration, test, and packaging changes are being preserved in place. This change will not be committed or deployed.
- No file under the game installation or 7th Heaven will be modified.

## Finding 1: root cause confirmed from the managed sequencing

The supplied diagnosis is correct. `CondorBattleSpeechTracker.ObserveInterface` treats the destination cursor as settled when the same `(DestinationX, DestinationY)` is present in two consecutive observations. `CondorCursorSteering.Step` deliberately releases every direction for a pass while `slowingDown` so FFVII resets its native repeat ramp. The steering remains active during that release, but two equal destination samples can reach the tracker and produce an intermediate `Destination X, Y.` line.

Both hosts step steering before asking the shared tracker for speech:

- x86: `Mod.TickCondorBattleReader` calls `StepCondorCursorSteering(snapshot)` and then `condorBattleSpeechTracker.Observe(snapshot)`.
- x64: `Steam2026ResearchObservationPump.ObserveCondorBattle` does the same and collects steering speech before tracker speech.

That ordering is sufficient to preserve the final coordinate. While steering is active, the tracker can remember destination samples without publishing them. On the terminal pass, `Step` clears `IsSteering` before `Observe` runs. If the destination is already settled, its final coordinate is spoken in that pass; otherwise the next equal sample produces the existing settled `Destination X, Y.` line. The stop announcement therefore does not need to invent or duplicate a coordinate.

## Design under test

1. An accepted `I` jump returns exactly `Moving.` once from `CondorFieldNavigator`.
2. A successful terminal steering step returns exactly `Moving stopped.`. Existing failure sentences remain the terminal announcement for failed jumps.
3. `CondorBattleSpeechTracker.Observe` receives the live post-step steering state. Only the destination-position line is suppressed while that state is true; its sample is still updated so ordinary settling resumes correctly when steering ends.
4. Manual destination movement continues through the existing two-sample settled readout unchanged.

## Red test, before production changes

The first focused x86 run failed before any production edit. The new integration regression could not compile because `CondorBattleSpeechTracker.Observe` had no `cursorJumpInProgress` input (`CS1739` at all four synthetic/manual sequencing calls). That is the missing contract: the tracker had no way to distinguish a native/manual pause from the steering controller deliberately releasing the direction keys. The same test sources are linked into the x64 test host so the contract is exercised under both target architectures.

The same red set also changes the already-existing start and arrival expectations to exact `Moving.` and `Moving stopped.` sentences and pins the four existing failure reasons so a general stop line cannot erase them.

## Green implementation and focused runs

- `CondorFieldNavigator` now returns exactly `Moving.` after the steering callback accepts a jump. A refused jump still gives the selected target coordinate and the unchanged cursor coordinate.
- A successful `CondorCursorSteering` terminal step returns exactly `Moving stopped.`. `Could not get closer.`, `Could not get there.`, `Lost track of the cursor.`, and `The game is not taking the direction keys.` remain distinct failure results.
- `CondorBattleSpeechTracker.Observe` accepts `cursorJumpInProgress`. In the Destination view it updates the sampled coordinate but refuses to publish a settled line while the flag is true. It deliberately does not update the last *spoken* selection, so the terminal coordinate is still eligible once steering ends.
- x86 and x64 pass the post-step `IsSteering` value. This is important: passing the pre-step value would suppress the terminal sample for an extra pass and make final speech timing depend on another poll.
- `CondorFieldNavigatorTests` and `CondorNavigationIntegrationTests` are now linked into the x64 test executable, closing the previous architecture-specific test gap around the navigator/tracker behavior.

Focused results after implementation:

- x86 `--condor-probe-silence-only`: passed.
- x64 `--condor-battle-only`: passed.

The synthetic sequence used in both targets is the reported shape: destination opens at `(256,795)`, a repeated `(256,980)` during steering stays silent, the post-step state becomes false at `(256,1008)`, and the existing settled readout then says `Destination 256, 1008.`. A separate manual sequence still says `Destination 240, 640.` after two stable samples.

## Mutation checks

Each mutation was applied alone, the focused executable was run, and the exact inverse patch was applied before the next mutation:

1. Disabling the `cursorJumpInProgress` branch failed `repeat-ramp pause stays silent`: the repeated intermediate coordinate produced one line instead of zero.
2. Making destination suppression unconditional failed the destination interface/readout assertions, proving that the tests do not accept silence as the cure for chatter.
3. Restoring the old `Going to Fighter ... at 428, 706.` start line failed the exact `Moving.` assertion.
4. Restoring silent arrival failed the exact `Moving stopped.` assertion.

After all four inverse patches, both focused executables passed again. The failure-message assertions remained active throughout, pinning the existing specific outcomes rather than merely requiring non-empty speech.

## Final verification

With all mutations restored and the original newline convention restored per file, the four requested executable test hosts were freshly built and run with:

- `FF7_ACCESSIBILITY_RUNTIME=C:\Games\Final Fantasy VII\workingdir`
- `FF7_ACCESSIBILITY_DATA_ROOT=C:\Games\Final Fantasy VII\workingdir`
- `FF7_ACCESSIBILITY_SOURCE_ROOT=<this worktree>`

Results:

- Reloaded x86: `Reloaded accessibility tests passed.`
- Steam 2026 x64: `Steam 2026 x64 accessibility skeleton tests passed.`
- Shared: `FFVII shared layout tests passed.`
- Parity: `FFVII dual-runtime parity matrix tests passed.`

The x86 build still emits the pre-existing nullable warning at `FortCondorLadderReachabilityTests.cs:156` (`CS8625`); it is outside this change and the test host exits successfully. `git diff --check` passes. All edited CRLF files are uniformly CRLF and both LF files remain uniformly LF.

The prior dirty work remains present: the six unused configuration keys are still absent, the `real_samples` package exclusion remains in `Build-DualRuntimePackage.ps1`, and the uncommitted field/world navigation-beacon work remains untouched except for the two deliberate Condor host call-site additions in `Mod.cs` and the x64 observation pump. No commit, deployment, game-folder write, or 7th Heaven write was made.
