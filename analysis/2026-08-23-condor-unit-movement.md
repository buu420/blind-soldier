# Fort Condor ordered-unit movement speech, 2026-08-23

## Scope and preserved state

This investigation targets the ordinary battlefield cursor being carried with an allied unit after the player confirms an Action destination. It does not target the separate `I`-key cursor steering path. The existing dirty navigation-beacon, configuration, packaging, and prior Condor changes remain in place and are not committed or deployed by this work.

## Finding 1: the reported chatter is the ordinary cursor readout

The supplied live session enters destination mode, settles the independent destination cursor at `(256,895)`, returns to ordinary cursor mode, and then speaks the ordinary cursor readout at roughly two-world-unit intervals as the allied Attacker walks. `CondorBattleSpeechTracker.ObserveCursor` is the producer: after two identical samples it emits `"{CursorX}, {CursorY}. {DescribeUnderCursor(...)}."`. Because module 9 carries the ordinary cursor with the selected moving unit, every short pause between simulation updates looks like another settled manual position.

This is distinct from the prior `I`-jump chatter. That path moved the independent destination cursor while interaction mode 3 was open. The present chatter occurs after the command is accepted, in interaction mode 1, and comes from `ObserveCursor` rather than `ObserveInterface`'s Destination branch.

## Finding 2: HeldDirectionMask distinguishes player motion, but not the reason for game motion

`CondorBattleStateReader` reads module 9's own held-input word at `0x00C72E80` and exposes only bits `0xF000` as `CondorBattleSnapshot.HeldDirectionMask`. Native `FUN_005FD958` proves the proposed negative test is reliable for **input ownership**:

- with any direction bit held, it calls `FUN_005FE771` and moves the cursor through the player's configured direction mapping;
- with no direction bit held, it resets the repeat counter and, when `0x00C6097C != -1`, repeatedly copies the selected live unit's `+0x48/+0x4A` position directly into `0x00CBCCC0/2` (writes at `0x005FE0FE` and `0x005FE119` in the analyzed executable).

Therefore a changing ordinary cursor with `HeldDirectionMask == 0` and no Blind Soldier steering in progress is game-driven movement, not manual movement. The suggested signal is sound for preserving manual surveying.

It is **not yet sufficient to call the motion an ordered move**. The same native branch follows whichever live unit remains selected; it does not test why that unit moved. A selected unit chasing, fighting, or otherwise moving under simulation can carry the cursor through the same path. The exact ordered-movement state and its completion path must be established before the final design is fixed.

## Finding 3: Action has a durable native order state

The repeated Ghidra pass in `analysis/ghidra/DumpCondorOrderedMovementEvidence.java` follows the Ally Unit command from the menu into the live unit record:

- `FUN_00603230` maps command cell 3 (`Action`) to interaction mode 3, copies the independent destination cursor into `0x00C75268/6A`, and stores command id 3 in `0x00C752B0`.
- `FUN_006033CD` calls `FUN_006034E2` for that Action row after the destination is confirmed.
- `FUN_006034E2` writes the command id to live-unit byte `+0x03`. Its Action case writes primary action state 1 to `+0x02`, writes 1 to the secondary state at `+0x04`, and calls `FUN_00603441` to install the chosen destination in the unit's motion state. In the analyzed executable the relevant stores are at `0x006035D6` (`+0x03`) and `0x006035DD` (`+0x02`).

The primary action byte is not a constant "walking" flag. Native combat/update paths can move it to another nonzero state while the order remains Action; for example `FUN_005FF6DA` writes state 3 at `0x00600063`. The command byte at `+0x03` remains 3 through that transition. The useful order predicate is therefore:

```text
unit[+0x03] == 3             // Action command
&& unit[+0x02] != 0          // command still active
&& unit is not dying/removing
```

This closes the ambiguity left by `HeldDirectionMask`: a selected unit can move for other simulation reasons, but only a live ally carrying command 3 with a nonzero action state is executing the player's directed Action order.

The reader already fetches each live unit as one contiguous `0x78`-byte record (`CondorBattleStateReader.cs:631-664`). Adding `+0x02` and `+0x03` to `CondorBattleUnit` therefore does not add separately timed memory reads or create a new translated-page-table coherence window.

## Finding 4: native completion and interruption paths are distinguishable

`FUN_00600247` is the successful destination-arrival path. Once the unit reaches its target, it clears the primary action byte at `0x0060040A`, clears movement state, zeroes its motion vectors, then publishes report cell 3 through `FUN_006027C2` at `0x00600480`. The existing speech tracker already maps that exact report to `Arrived at the directed position.` (`CondorBattleSpeechTracker.cs:28`). That report is the correct single successful stop announcement; adding a second `Moving stopped.` would duplicate it.

Not every temporary pause is completion. `FUN_00600577` is path recovery and can redirect the unit while leaving the Action command active. A speech tracker must not declare failure merely because the coordinate remains unchanged for one or several 100 ms samples.

The native state does expose real non-arrival endings: the command may be replaced, the primary action state may clear without report cell 3, the unit may enter its dying/removal state, or the slot may disappear. `FUN_0060080B`, for example, clears the primary state when a tracked target is gone. Those paths need one explicit spoken ending rather than silence.

There is a read-window complication visible in Brice's supplied session. After the last carried-cursor line at 13:04:17, the reader reports incoherent state repeatedly before `Arrived at the directed position.` at 13:04:20. A missing snapshot must therefore preserve the pending movement rather than end it. For an apparently inactive unit without the arrival report, two consecutive coherent inactive readings are the minimum safe confirmation: one translated-memory reading can straddle the native stores that clear movement and publish the report.

## Finding 5: the suppression predicate can preserve manual battlefield surveying

The tracker should not suppress merely because the direction mask is zero. A short physical tap can be over before the next 100 ms sample, and zero also describes a resting cursor. The safe conjunction is narrower:

1. the tracked allied slot carries the native active-Action predicate above;
2. the ordinary cursor is still on that same slot and equals that unit's live `+0x48/+0x4A` position;
3. module 9's held-direction mask is zero; and
4. Blind Soldier's own `I`-key cursor steering is idle.

That describes the game's `FUN_005FD958` unit-follow branch, not merely "something moved without a key." Once this state begins, say `Moving.` once and suppress `ObserveCursor` while the conjunction remains true, including brief pathfinding or combat pauses. If the player holds a direction, moves the cursor away from the tracked unit, or the cursor no longer equals the unit position, the existing settled `X, Y. <unit or placement answer>.` path runs unchanged. Consequently `Cannot place` remains reachable during manual placement.

On success, report cell 3 says `Arrived at the directed position.` and closes the tracked order without an extra line. On an interruption confirmed by two coherent inactive snapshots, the tracker should say one truthful ending with the last known position, such as `Movement stopped at 256, 880.` If the slot died or disappeared, the ending should say that the unit is no longer on the field rather than inventing a coordinate. Null/incoherent snapshots retain the state and say nothing until the reader is coherent again.

## Finding 6: the `I`-jump wording should be restored, but its chatter suppression should stay

Plain `Moving.` should identify allied-unit travel. Using the same word for an `I`-key cursor jump makes two different actors indistinguishable. The last-round wording changes addressed the wrong complaint:

- restore the jump's accepted-start line to `Going to <selected target>, at <x>, <y>.`;
- restore successful jump completion to its former silence, because the existing settled cursor readout names the actual final coordinate and what is there;
- retain every steering failure line; and
- retain the destination-readout suppression while steering, because deliberate deceleration can still make an in-progress jump look settled between pulses.

This restores the specific information that was removed without bringing back any intermediate-coordinate stream. It also leaves `Moving.` and the native `Arrived at the directed position.` pair available for the unit order Brice was actually describing.

## Proposed test-first implementation, pending confirmation

1. Add the `+0x02` primary-action and `+0x03` command bytes to the shared unit snapshot and cover their extraction from the existing `0x78` record.
2. Add identity-based ordered-movement state to `CondorBattleSpeechTracker`, keyed by unit slot and retaining the last coherent unit position.
3. Emit `Moving.` exactly once when the selected allied unit enters an active Action order and the ordinary cursor is following it.
4. Suppress only the ordinary cursor readout under the four-part predicate in Finding 5. Leave manual settled-coordinate behaviour byte-for-byte in meaning.
5. Let report cell 3 provide the one successful ending. Confirm non-arrival endings across two coherent samples and speak one explicit interruption line. Keep the pending state through failed/incoherent reads.
6. Restore the specific `I`-jump wording described in Finding 6 while keeping its intermediate-destination suppression and all failure speech.
7. Start with regressions for: one start line; no coordinate or `Cannot place` stream while the ordered unit moves; unchanged manual surveying; a manually moved cursor leaving the followed unit; arrival without a duplicate stop line; interruption, death and disappearance with speech; and a read gap before arrival. Mutation checks must prove that removing the native command predicate, the player-input escape, or the interruption speech makes a named test fail.
8. Because the reader and tracker are shared sources, the behaviour lands in both x86 and x64. Both runtime hosts must pass the same steering-in-progress value, and the four executable suites remain the completion gate.

## Repeatability

The native evidence is reproduced by:

```text
analysis\ghidra\RunCondorOrderedMovementEvidence.cmd
```

That invokes `analysis/ghidra/DumpCondorOrderedMovementEvidence.java` against the existing x86 Ghidra project and records the command-selection, action-state, cursor-follow, arrival/report and interruption call paths by native address. No file under the game or 7th Heaven directories is written by the pass.

## Brice's wording refinement and implementation reading

Brice's later specification supersedes Finding 6's wording recommendation. The `I` jump keeps its standalone `Moving.` and successful `Moving stopped.` lines. An ordered allied move also says standalone `Moving.` once. This is not ambiguous to the player because both standalone uses immediately answer an action the player just took. The old `Going to ...` line will not be restored.

There are two related but different native facts to present:

- **The player's Action order remains active:** command byte `+0x03 == 3`, primary state `+0x02 != 0`, and the allied unit is alive. This durable predicate owns the one standalone `Moving.` and keeps coordinate-only cursor dragging quiet through pathfinding or combat state changes.
- **A unit is currently in the walking/advance state:** primary state `+0x02 == 1` and the unit is alive. `FUN_005FFF45` dispatches state 1 through its directed-advance path; an encounter moves it to state 3. This presentation state can be used for any unit under the cursor, including one the player did not just command.

The cursor readout will therefore retain its complete existing shape and add one state word:

```text
256, 872. Attacker, 180 of 180. Moving.
```

Coordinate-only changes caused by `FUN_005FD958` carrying the cursor with the selected ordered unit will silently advance the readout baseline, preventing the stream in the supplied log. A real direction press, or the cursor leaving that unit, re-enables the unchanged settled manual readout. Motion-state changes are not suppressed: when a unit under a resting cursor leaves native advance state without the ordered-arrival report, the chosen line is the same complete readout ending in `Stopped.`, for example `256, 895. Attacker, 180 of 180. Stopped.` This names the unit before its state and also covers a different unit the player merely surveyed.

For the ordered unit's successful destination, `Arrived at the directed position.` remains the only stop line because it is more specific and the game publishes it from the exact arrival path. A failed, replaced or interrupted order still gets the separate confirmed non-arrival ending described above. Thus neither success nor failure can end silently, while an ordinary unit stopping under the cursor is no longer dependent on an Action report it will never generate.

## Test-first implementation checkpoint

The shared reader now carries the two native bytes out of the already-coherent `0x78`-byte unit record: primary action state at `+0x02` and command id at `+0x03`. Both runtime test hosts compile and execute the same new movement-speech regression file.

The behavior tests were run before the speech implementation. After only the reader contract existed, the focused x86 suite failed on the first user-visible assertion: an Action transition produced zero lines where exactly one `Moving.` was required. After implementing the shared tracker state, the focused suites pass on both hosts:

```text
Ff7.Accessibility.Reloaded.Tests --condor-battle-only
  FFVII Fort Condor battle reader tests passed.

Ff7.Accessibility.Steam2026X64.Tests --condor-battle-only
  Steam 2026 x64 Fort Condor initialization tests passed.
```

The implementation treats two native concepts separately:

- `command == 3 && primary state != 0` tracks the lifetime of a player-issued Action order. Its rising edge, while the ordered ally remains selected, speaks standalone `Moving.` once. Coordinate-only cursor motion in lockstep with that ally advances the speech baseline silently.
- `primary state == 1` is what decorates any surveyed live unit as `Moving.`. A transition out of that visible walking state is confirmed over two coherent samples while the cursor stays on the unit, then the same full survey line ends in `Stopped.`.

An arrival report for the same slot clears both pending endings and primes the cursor baseline, leaving `Arrived at the directed position.` as the only successful completion. An Action order which becomes inactive without that native arrival report is also confirmed over two coherent samples and ends explicitly with `Movement stopped at X, Y.` (or names that the unit left the field). A failed or torn read never enters `Observe`, so it cannot falsely complete or erase the retained order state.

A second red-green cycle closed a translated-poll ordering edge. The command/action bytes can become visible before `FUN_005FD958` has copied the selected unit's position back to the cursor. The first implementation registered the order in that sample but permanently missed its acknowledgement because the cursor was not on the unit yet. A regression now presents the Action transition with the cursor still at the chosen destination, then presents the game's cursor-follow sample. It failed with zero lines where `Moving.` was required. The tracker now retains an unannounced order until the cursor follows that unit, says `Moving.` once at that point, and marks battle-entry orders as already announced so attaching mid-battle cannot invent a player action. Both focused runtime suites are green after this correction.

## Mutation evidence

Three independent production mutations were made one at a time, each focused suite was executed, and each source edit was restored with a patch rather than with Git so none of the other uncommitted work could be overwritten:

1. Replacing the exact `command == 3` Action predicate with an always-true command comparison made `AnUncommandedMovingUnitUsesTheFullSurveyReadout` fail: it got standalone `Moving.` instead of `256, 870. Attacker, 180 of 180. Moving.`.
2. Reversing the previous-cursor/unit-position equality in the game-carried-cursor predicate made `OrderedMoveSpeaksOnceAndSilencesTheCarriedCursor` fail: a paused carried step produced one unwanted line instead of none.
3. Raising the surveyed-stop confirmation threshold from two to three coherent samples made `ManualSurveyKeepsCoordinatesAndSaysWhenAUnitStops` fail with no stop line at the required sample.

After restoring all three mutants, the focused x86 and x64 suites both returned green again. The standalone movement acknowledgement is also marked as superseding stale interface state, so it replaces the just-obsolete destination-coordinate utterance rather than waiting behind it; if a one-shot event occurs in the same sample, the tracker's existing finalizer combines the event and current state before interrupting.

## Full verification

With `FF7_ACCESSIBILITY_RUNTIME` and `FF7_ACCESSIBILITY_DATA_ROOT` pointed at the read-only real runtime data and `FF7_ACCESSIBILITY_SOURCE_ROOT` pointed at this worktree, all four required executable suites passed after the mutations were restored:

```text
Reloaded accessibility tests passed.
Steam 2026 x64 accessibility skeleton tests passed.
FFVII shared layout tests passed.
FFVII dual-runtime parity matrix tests passed.
```

No file under the game directory or 7th Heaven was changed, and the work remains uncommitted for review.
