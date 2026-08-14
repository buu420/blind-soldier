# Nibelheim Flashback Completeness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Story navigation, cutscene descriptions, and native NPC labels complete throughout Cloud's Nibelheim flashback in both Blind Soldier runtimes.

**Architecture:** Extend the generated shared Story catalog with exact GameMoment, bank-bit, walkmesh, model, and gateway targets. Extend the shared cutscene catalog only at verified native opcodes, and make the NPC reader prefer a reviewed native-model label table across the flashback fields while preserving live visibility and interactibility gates.

**Tech Stack:** C#/.NET 8, PowerShell catalog generator, Reloaded-II x86, Steam 2026 x64 shared runtime, Prism, FFVII `flevel.lgp`, Ghidra, existing console regression harnesses.

## Global Constraints

- Communicate only what a sighted player can see; do not expose hidden rewards, future events, or strategy.
- Vanilla native game scripts and model resources are canonical; dialogue-heading guesses and Echo-S text are not identity sources.
- Every Story target must exist only in its exact native progression state and route to the exact interaction line, live model, or gateway.
- Director entities, line proxies, beds, letters, furniture, boxes, Materia, keys, and other props must never appear in NPCs.
- x86 and x64 must consume the same Story, NPC-label, and cutscene-description behavior.
- Preserve user configuration and every unrelated mod during deployment.

---

### Task 1: Complete the five missing Story states

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/KalmRanchNavigationTests.cs`
- Modify: `tools/Generate-FieldStoryEvents.ps1`
- Regenerate: `Ff7.Accessibility.Reloaded/Assets/navigation/field_story_events.json`

**Interfaces:**
- Consumes: `FieldStoryTargetReader`, `FieldStoryEventCatalog.CreateAllFields()`, native Bank 3 byte 19, GameMoment, player triangle, and live model state.
- Produces: five target definitions using existing `FieldStoryEventDefinition` fields; no new runtime API.

- [ ] **Step 1: Add failing Story regressions**

Extend `AssertNibelheimFlashbackStoryProgression()` with literal expectations for:

```csharp
// Bank 3 byte 19 bit 7 clear.
AssertStoryTarget(memory, 290, 376,
    "Approach Zangan in the burning square", 196, 746, 51);

// Bank 3 byte 19 bit 7 set and entity 7 visible.
AssertStoryTarget(memory, 290, 376,
    "Enter the only unblocked house and follow Sephiroth", 616, 475, 0);

AssertStoryTarget(memory, 316, 376,
    "Enter the Nibel Reactor", -118, 163, 325);

AssertStoryTargetAtTriangle(memory, 322, 381, 24,
    "Follow Tifa deeper into the reactor", -6, -912, 191,
    new FieldNavigationTriggerLine(62, -937, 191, -74, -887, 191));

AssertStoryTarget(memory, 323, 383,
    "Follow Sephiroth into Jenova's chamber", -4, -1141, 709);
```

Also assert that the first burning-square target retires when bit 7 is set and that the old backward gateway is never selected at moments 381 or 383.

- [ ] **Step 2: Run the focused navigation suite and verify RED**

Run:

```powershell
$env:FF7_ACCESSIBILITY_RUNTIME='C:\Games\Final Fantasy VII\workingdir'
dotnet run --project Ff7.Accessibility.Reloaded.Tests/Ff7.Accessibility.Reloaded.Tests.csproj -c Release -- --nibelheim-flashback-only
```

Expected: fail because all five labels are absent from `FieldStoryEventCatalog.CreateAllFields()`.

- [ ] **Step 3: Add the minimal generator definitions**

Add two field-290 definitions gated by `(Bank 3, byte 19, mask 0x80)`, a field-316 moment-376 forward gateway, a field-322 moment-381 lower-walkmesh forward gateway, and a field-323 moment-383 forward gateway. Use the literal lines and coordinates from the design document.

- [ ] **Step 4: Regenerate the catalog**

Run the repository's existing Story generator command used by `Generate-FieldStoryEvents.ps1`, producing only the embedded JSON catalog from the reviewed definitions.

- [ ] **Step 5: Run the focused navigation suite and verify GREEN**

Run the command from Step 2 and confirm every state transition, trigger line, and coordinate assertion passes.

### Task 2: Replace flashback NPC guesses with native model labels

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/KalmRanchNavigationTests.cs`
- Modify: `Ff7.Accessibility.Reloaded/FieldNavigationNpcReader.cs`

**Interfaces:**
- Consumes: existing `VerifiedLabels`, `ReviewedLabelFields`, native entity-to-model mapping, `VISI`, `TLKON`, and model coordinates.
- Produces: reviewed labels for legitimate flashback people and silence for every unlisted proxy or prop.

- [ ] **Step 1: Add failing model-identity tests**

Add `AssertNibelheimFlashbackNpcLabels()` and call it from both `Run()` and `RunNibelheimFlashbackOnly()`. Use real `FieldNavigationNpcReader` behavior with one visible model per case. At minimum assert these literal identities:

```csharp
(273, 17, "Old man"), (273, 18, "Zangan"),
(273, 19, "Innkeeper"), (273, 20, "Man in black cape"),
(274, 8, "Sephiroth"), (274, 9, "Shinra infantryman"),
(276, 11, "Cloud's mother"),
(282, 11, "Photographer"), (282, 12, "Tifa's father"),
(290, 7, "Sephiroth"), (290, 8, "Shinra infantryman"),
(290, 9, "Zangan"), (290, 10, "Photographer"),
(307, 3, "Sephiroth"),
(312, 6, "Tifa"), (312, 8, "Shinra infantryman"),
(312, 9, "Shinra infantryman"),
(323, 6, "Tifa"), (323, 7, "Tifa"), (323, 8, "Sephiroth")
```

Pass misleading delegated lines beginning with `Cloud` for field 290 entity 7 and assert that the result is still `Sephiroth`. Provide synthetic talk definitions for representative proxies/props in reviewed fields 276, 297, 299, 317, and 322 and assert that no NPC target is returned.

- [ ] **Step 2: Run the focused suite and verify RED**

Run the Task 1 focused command. Expected: fail because flashback fields are not reviewed and have no verified labels.

- [ ] **Step 3: Add the reviewed native-model label set**

Add every legitimate talkable person identified in the design audit to `VerifiedLabels`. Add fields 273 through 327 to `ReviewedLabelFields`; unlisted native definitions in that range intentionally remain silent. Do not add verified definitions for action-line-only Tifa in field 322 or for any entity whose model is a prop or whose script is a director proxy.

- [ ] **Step 4: Run the focused suite and verify GREEN**

Run the Task 1 focused command and confirm identity, visibility, interactibility, and proxy-suppression assertions pass.

### Task 3: Add the five missing visual descriptions

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Reloaded/FieldCutsceneDescriptionTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/EchoSCompatibilityManifest.cs` only when an exact known fingerprint requires a mapping entry

**Interfaces:**
- Consumes: `FieldCutsceneDescriptionCatalog.CreateKalmThroughLowerJunonDescriptions()` and existing native opcode ingress.
- Produces: five additional `FieldCutsceneDescriptionCue` records with unique keys and shared x86/x64 text.

- [ ] **Step 1: Add failing exact-anchor tests**

Change the Kalm-through-Lower-Junon expectation from 25 to 30 cues and require these literal keys/opcodes:

```text
101:0:0:15:F9
322:6:0:100:BC
323:7:3:13:01
323:7:4:0:BA
323:5:15:27:BA
```

Assert text contains `Mt. Nibel`, `injured father`, `charges Sephiroth`, `slashes Tifa`, and `kneels beside her`. Keep the existing installed-script audit so each expected byte must exist with the exact opcode. Update the x64 focused test to require the same count/order and to include `OpcodeCanm2Index` and `OpcodeAnimHoldIndex` in the accepted ingress set.

- [ ] **Step 2: Run both description suites and verify RED**

Run:

```powershell
$env:FF7_ACCESSIBILITY_RUNTIME='C:\Games\Final Fantasy VII\workingdir'
dotnet run --project Ff7.Accessibility.Reloaded.Tests/Ff7.Accessibility.Reloaded.Tests.csproj -c Release -- --kalm-junon-descriptions-only
dotnet run --project Ff7.Accessibility.Steam2026X64.Tests/Ff7.Accessibility.Steam2026X64.Tests.csproj -c Release -- --kalm-junon-descriptions-only
```

Expected: both fail because the shared catalog still contains 25 cues.

- [ ] **Step 3: Add the minimal shared cues**

Insert the five cue records in chronological flashback order using the exact anchors from Step 1 and concise present-tense narration from the design document. Do not add a timer, OCR fallback, field-entry guess, or duplicate of the existing fire/Jenova cues.

- [ ] **Step 4: Authorize exact field identity**

Add the installed vanilla field-101 and field-322 fingerprints to the exact compatibility manifest if they are not already authorized. Preserve fail-closed behavior for unknown hashes. Add an Echo-S mapping only if an exact known Echo script contains the same action and its opcode has been independently located.

- [ ] **Step 5: Run both description suites and verify GREEN**

Run both commands from Step 2 and confirm cue uniqueness, installed opcode matching, ordering, and x64 ingress support.

### Task 4: Verify shared behavior and deploy the test build

**Files:**
- Modify only if required by an existing deploy manifest: repository deployment scripts
- Do not modify: live `Config.json`, 7th Heaven, FFNx, or unrelated mods

**Interfaces:**
- Consumes: completed shared catalogs and reader implementation.
- Produces: verified x86/x64 Release assemblies in the active Blind Soldier mod directories.

- [ ] **Step 1: Run full verification**

Run:

```powershell
$env:FF7_ACCESSIBILITY_RUNTIME='C:\Games\Final Fantasy VII\workingdir'
dotnet run --project Ff7.Accessibility.Reloaded.Tests/Ff7.Accessibility.Reloaded.Tests.csproj -c Release
dotnet run --project Ff7.Accessibility.Steam2026X64.Tests/Ff7.Accessibility.Steam2026X64.Tests.csproj -c Release
dotnet run --project Ff7.Accessibility.Parity.Tests/Ff7.Accessibility.Parity.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\Run-DualRuntimeVerification.ps1
git diff --check
```

- [ ] **Step 2: Inspect the change boundary**

Run `git status --short` and `git diff --stat`. Confirm only the two plan documents, focused tests, shared production files, and generated Story catalog changed. Leave `.artifacts/` untracked.

- [ ] **Step 3: Build both Release assemblies**

Build the x86 Reloaded project and x64 Steam2026 project in Release. Record each DLL's full path, architecture, file version, and SHA-256.

- [ ] **Step 4: Deploy without overwriting configuration**

Use the repository's established dual-runtime deployment flow or copy only verified binaries and required shared catalog assets into the active Blind Soldier directories under the x86 working game and the Steam 2026 install. Preserve all JSON configuration and third-party mod files.

- [ ] **Step 5: Verify deployed bytes**

Hash the deployed assemblies and compare them to the built outputs. Inspect the next load log for architecture, dependency, duplicate-load, and compatibility-variant errors.
