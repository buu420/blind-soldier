# Echo-S Compatibility Implementation Plan

> **Execution note:** Implement sequentially in the shared checkout. The user
> explicitly waived additional approval/review pauses. Use red-green tests for
> each behavior change and do not launch FFVII, 7th Heaven, or Reloaded-II.

**Goal:** Make Echo-S dialogue, startup disclaimer, and modified field scripts
coexist with the full Prism accessibility layer without duplicate voiced lines
or missing audio descriptions.

**Architecture:** Identify the exact Echo-S script variant from loaded field
memory; map canonical descriptions to exact alternate script keys; hook FFNx's
verified `play_voice` routine so dialogue ownership follows its real boolean
playback result; preserve Prism as the fail-open speech path.

**Tech stack:** C# 12, .NET 8 Windows x86, Reloaded.Hooks 4.3.3, Prism,
PowerShell, FFNx official source/PDB, Ghidra 12.0.4.

## Global constraints

- Preserve every vanilla cue and non-Echo speech path.
- Never use nearby-offset matching, OCR, input inference, or guessed text.
- Restrict the FFNx voice hook to the installed module SHA-256 and a verified
  pristine function prefix.
- Native detours call their original exactly once and only publish bounded
  immutable observations.
- Choices, the disclaimer, menus, navigation, battle output, and descriptions
  are never suppressed as voiced dialogue.
- Deployment preserves the installed configuration and does not start or focus
  any game or launcher process.
- The source directory has no Git metadata, so commit steps are omitted.

---

### Task 1: Freeze Echo-S and FFNx identities

**Files:**
- Create: `tools/EchoSCompatibilityAnalyzer/EchoSCompatibilityAnalyzer.csproj`
- Create: `tools/EchoSCompatibilityAnalyzer/Program.cs`
- Create: `docs/research/echo-s-1.24-compatibility-atlas.md`
- Evidence: `C:\FF7A11Y\data\field\flevel`
- Evidence: installed `AF3DN.P`, `FFNx.pdb`, and official FFNx source

- [ ] Add an offline analyzer that loads vanilla and extracted Echo-S field
  scripts through `FieldScriptNavigationCatalog`, aligns exact opcode sequences,
  and reports every canonical description cue as mapped, ambiguous, or missing.
- [ ] Extract the four field-109 disclaimer messages with the existing FFVII
  decoder and record their script-section fingerprints.
- [ ] Finish the Ghidra/PDB analysis of `play_voice(char*, byte, byte, byte)`;
  record its RVA, ABI, pristine bytes, callers, and returned ownership signal.
- [ ] Run the analyzer and manually reject every ambiguous cue. Store only
  unambiguous alternate keys and exact field fingerprints in the atlas.

### Task 2: Preserve opening detection and speak the disclaimer

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/OpeningMovieProbeLifetime.cs`
- Create: `Ff7.Accessibility.Reloaded/EchoSStartupTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Write failing tests for opening 116 -> validated disclaimer 109 -> return
  116 -> movie close, ordinary direct-field abandonment, timeout, and reset.
- [ ] Implement the minimal explicit startup state machine; do not exempt an
  unrelated field-109 visit.
- [ ] Write failing tests that disclaimer message ids resolve to exact decoded
  Echo-S text, speak once, bypass voiced-dialogue suppression, and fall back to
  fingerprint-bound packaged text only when the runtime window is unavailable.
- [ ] Integrate the disclaimer through the existing native message queue and
  make the opening probe consume the startup tracker's exemption.
- [ ] Run the Reloaded tests and require green.

### Task 3: Add exact Echo-S description variants

**Files:**
- Create: `Ff7.Accessibility.Reloaded/EchoSCompatibilityManifest.cs`
- Create: `Ff7.Accessibility.Reloaded/LoadedFieldScriptIdentityReader.cs`
- Modify: `Ff7.Accessibility.Reloaded/FieldCutsceneDescriptionTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Write failing tests for loaded-script fingerprint calculation, exact
  Echo-S key selection, vanilla key preservation, unsupported-hash rejection,
  no nearby-offset fallback, and once-per-field-entry behavior.
- [ ] Implement coherent reads of the loaded field script section and its
  SHA-256 fingerprint with structural bounds.
- [ ] Add the reviewed Echo-S 1.24 alternate-key manifest while keeping cue text
  single-sourced from `FieldCutsceneDescriptionCatalog`.
- [ ] Select the variant only after the current field fingerprint validates;
  otherwise use vanilla matching and log a bounded compatibility diagnostic.
- [ ] Run the Reloaded tests and require green.

### Task 4: Capture actual FFNx voice ownership

**Files:**
- Create: `Ff7.Accessibility.Reloaded/FfnxVoicePlaybackHook.cs`
- Create: `Ff7.Accessibility.Reloaded/EchoSDialoguePolicy.cs`
- Modify: `Ff7.Accessibility.Reloaded/NativeFieldHookEventQueue.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Write failing hook-contract tests for exact module hash, checked RVA,
  pristine prefix, ABI, original-once behavior, successful/failed playback
  publication, queue bounds, and idempotent teardown.
- [ ] Implement the verified FFNx hook. Publish field name, window id, dialog
  id, page, and original return value; never speak in the detour.
- [ ] Write failing policy tests: suppress only matching successful ordinary
  dialogue; retain unvoiced lines, choices, disclaimer, mismatches, stale
  events, unsupported FFNx, and all accessibility-only channels.
- [ ] Implement bounded correlation between native message observations and
  FFNx voice events. Missing or incoherent correlation speaks through Prism.
- [ ] Run the Reloaded tests and require green.

### Task 5: Coordinate descriptions and lifecycle

**Files:**
- Create: `Ff7.Accessibility.Reloaded/EchoSDescriptionSpeechCoordinator.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Write failing tests for immediate delivery without active voice, bounded
  deferral during voice playback, first-safe-gap delivery, replacement rules,
  field-change cancellation, suspend/reset, and non-Echo pass-through.
- [ ] Implement the coordinator and route only audio descriptions through it;
  normal menu/navigation/battle speech remains unchanged.
- [ ] Reset startup, compatibility, voice-correlation, and description state on
  suspend, field lifecycle reset, and unload.
- [ ] Run the Reloaded tests and require green.

### Task 6: Full verification and deployment

**Files:**
- Modify only if required: `Build-DualRuntimePackage.ps1`
- Deploy: installed `ff7.accessibility.reloaded` package

- [ ] Run focused Reloaded tests, then the complete dual-runtime verification.
- [ ] Build release packages for x86 and x64 and verify PE architecture, hashes,
  expected assets, and exact compatibility manifest contents.
- [ ] Run `Install-FF7ReloadedMod.ps1` through its supported non-launching path,
  preserving `Configuration/config.json`.
- [ ] Inspect installed files and logs without launching the game.
- [ ] Hand off a short live-test sequence: disclaimer pages, opening narration,
  one voiced line, one choice, and two early-game description cues.
