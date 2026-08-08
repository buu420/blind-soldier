# Wall Market Story and Battle Status Hotkeys Implementation Plan

## Task 1: Lock the desired behavior with focused tests

**Files:**
- Add `Ff7.Accessibility.Reloaded.Tests/BattleStatusHotkeyTests.cs`
- Add `Ff7.Accessibility.Reloaded.Tests/WallMarketStoryRoutingTests.cs`
- Modify `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Add `Ff7.Accessibility.Steam2026X64.Tests/Steam2026BattleStatusHotkeyTests.cs`
- Modify `Ff7.Accessibility.Steam2026X64.Tests/Program.cs`

1. Add a focused test entry point that runs before the historical full-suite source-root lookup.
2. Test all six Wall Market plot phases, including the current save state.
3. Test party selection, empty-slot retention, HP/MP formatting, buff/debuff separation, explicit empty status speech, and limit percentage edges.
4. Test battle/foreground key gating for the x64 input path.
5. Run the focused tests and confirm they fail because the new catalog targets and controller do not yet exist.

## Task 2: Add native limit gauge and shared hotkey behavior

**Files:**
- Modify `Ff7.Accessibility.LegacyLayout/SavemapPartyReader.cs`
- Add `Ff7.Accessibility.LegacyLayout/BattleStatusCatalog.cs`
- Add `Ff7.Accessibility.LegacyLayout/BattleStatusHotkeyController.cs`
- Modify `Ff7.Accessibility.Reloaded/BattleStatusSpeechTracker.cs`

1. Add coherent party-slot reading for the native character limit byte at offset `0x0F`.
2. Add one shared native status-name catalog and beneficial-status classification.
3. Implement the shared key map, party selection state, live snapshot formatting, and limit conversion.
4. Replace the automatic tracker’s private status-name table with the shared catalog.
5. Run focused shared tests until green.

## Task 3: Restore the Wall Market gateway chain

**Files:**
- Modify `tools/Generate-FieldStoryEvents.ps1`
- Regenerate `Ff7.Accessibility.Reloaded/Assets/navigation/field_story_events.json`

1. Add lower/upper Wall Market gateway targets for each conditional plot phase.
2. Add the post-change route to Corneo Hall.
3. Regenerate the embedded catalog.
4. Run focused Wall Market tests until green.

## Task 4: Wire the x86 runtime

**Files:**
- Modify `Ff7.Accessibility.Reloaded/Mod.cs`

1. Poll the eight physical keys through the existing foreground rising-edge tracker.
2. Gate speech to live battle module ownership and suppress it during results.
3. Build snapshots from `BattleStateReader` plus the coherent native limit byte.
4. Speak through Prism with immediate interruption and reset held state across ownership changes.
5. Run x86 focused tests.

## Task 5: Wire the x64 runtime

**Files:**
- Modify `Ff7.Accessibility.Steam2026X64/Ff7.Accessibility.Steam2026X64.csproj`
- Modify `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchSession.cs`
- Modify x64 battle/input coordinator files only if the session seam requires it.

1. Link the shared controller into the x64 build if it is not in LegacyLayout.
2. Poll the same key map through `Steam2026ForegroundInputAdapter`.
3. Use the same battle ownership and live reader instances as existing battle speech.
4. Speak through the x64 runtime output and log the selected query.
5. Run x64 focused and module tests.

## Task 6: Parity, packaging, and deployment

**Files:**
- Modify parity tests if needed to assert both runtime seams.
- Use the existing dual-runtime packaging and deployment scripts.

1. Run shared tests, focused x86 tests, x64 module tests, and parity tests.
2. Build Release x86 and x64 artifacts.
3. Package the dual-runtime mod without changing third-party loaders.
4. Deploy the correct build into the active x86/7th Heaven and x64 game environments.
5. Record exact artifact paths and remaining live checks for the handoff.
