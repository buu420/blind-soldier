# Kalm Through Lower Junon Audio Description Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add verified, sighted-equivalent audio descriptions for every meaningful FMV and locked-camera story beat from the Kalm flashback through the first Lower Junon scenes in both Blind Soldier runtimes.

**Architecture:** Native vanilla field scripts remain the source of truth. In-engine actions and the audited single-beat FMVs extend the shared `FieldCutsceneDescriptionCatalog`; FMVs fire once from their exact native `MOVIE` opcode. Both runtime adapters enqueue the same shared cue records and speak them through the existing Prism cutscene-priority path.

**Tech Stack:** C#/.NET, Reloaded-II x86, Steam 2026 x64 native ingress, Prism, Ghidra, FFVII `flevel.lgp` field scripts, ffmpeg/yt-dlp, Vidscribe, existing console regression harnesses.

## Global Constraints

- Cover Kalm, the complete Nibelheim flashback, Chocobo Farm, marsh aftermath, Mythril Mine Turks encounter, and first Lower Junon/Priscilla scenes.
- Communicate only information a sighted player can see; do not add strategy, hidden state, inferred motives, or dialogue duplication.
- Native vanilla game files and scripts are canonical; Echo S data is never a cue source.
- Use short present-tense Prism narration and yield to readable dialogue.
- A skipped or ended movie must never leave delayed narration queued after player control returns.
- Shared catalog text and ordering must be identical in x86 and x64.
- Deploy without overwriting user configuration.

---

### Task 1: Build the visual and native cue evidence table

**Files:**
- Create: `analysis/kalm-through-lower-junon-description-cues.md`
- Create locally only: `.artifacts/vidscribe-source/*.mp4`
- Create locally only: `.artifacts/vidscribe-clips/*.mp4`

**Interfaces:**
- Consumes: original-game no-commentary footage, the GameFAQs guide, `tools/FieldRouteDump`, `tools/Generate-FieldStoryEvents.ps1`, and Ghidra field-opcode evidence.
- Produces: one chronological table whose implemented rows each contain source video and timestamp, sighted-equivalent narration, field name and ID, entity ID, script ID, byte index, opcode, and whether the cue is an in-engine action or native movie start.

- [ ] **Step 1: Download the five original-game source segments**

Run yt-dlp against SourceSpy91 parts 13, 14, 15, 16, and 19 at 720p or lower and save them below `.artifacts/vidscribe-source/`.

- [ ] **Step 2: Isolate description-worthy footage**

Use `ffprobe` and `ffmpeg -ss <start> -to <end> -c:v libx264 -c:a aac` to create clips no longer than five minutes under `.artifacts/vidscribe-clips/`. Exclude ordinary traversal, routine battles, menus, and dialogue-only stretches; keep the submitted total at or below 50 minutes.

- [ ] **Step 3: Run every clip through Vidscribe**

Upload each clip using the signed-in Chrome session. Capture Vidscribe's timestamped output in the evidence table, then directly inspect the opening, transition, reaction, and closing frames of each clip. Rewrite rather than copy any output that infers emotion, repeats dialogue, misidentifies a character, or misses a silent action.

- [ ] **Step 4: Map each narration to native script evidence**

For every retained cue, decode the vanilla field script and record the exact native trigger. Verify the trigger exists at the stated byte index and opcode; use Ghidra to confirm movie lifecycle state and any ambiguous opcode semantics. Rows without a safe native trigger must be marked excluded with the specific reason and must not enter the production catalog.

- [ ] **Step 5: Self-audit the slice**

Compare the table against the guide's complete sequence and explicitly account for Kalm framing, truck/dragon arrival, Nibelheim/Mt. Nibel/reactor/mansion/fire/confrontation, chocobo dance, impaled Zolom, Turks, Lower Junon arrival, Priscilla, Bottomswell aftermath, and CPR. Remove dialogue-only rows and verify every retained sentence is present tense and spoiler-safe.

### Task 2: Specify the shared catalog behavior with failing tests

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Parity.Tests/Program.cs`

**Interfaces:**
- Consumes: exact trigger rows from `analysis/kalm-through-lower-junon-description-cues.md`.
- Produces: regression assertions for `FieldCutsceneDescriptionCatalog.CreateKalmThroughLowerJunonDescriptions()` and inclusion in `CreateEarlyGameDescriptions()`.

- [ ] **Step 1: Write catalog coverage tests**

Add a test that expects at least one verified cue for every scoped segment, rejects duplicate `FieldCutsceneDescriptionKey` values, rejects blank text, and checks each cue's exact native `(field, entity, script, byte index, opcode)` against the decoded vanilla field script.

- [ ] **Step 2: Write parity assertions**

Load the shared catalog from both built runtime assemblies and assert the Kalm-through-Lower-Junon cue sequence has identical keys, opcodes, text, and ordering.

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```powershell
dotnet run --project Ff7.Accessibility.Reloaded.Tests/Ff7.Accessibility.Reloaded.Tests.csproj -c Release
dotnet run --project Ff7.Accessibility.Parity.Tests/Ff7.Accessibility.Parity.Tests.csproj -c Release
```

Expected: failure because `CreateKalmThroughLowerJunonDescriptions()` and its catalog rows do not yet exist.

### Task 3: Implement the native in-engine cue catalog

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/FieldCutsceneDescriptionTracker.cs`

**Interfaces:**
- Consumes: verified in-engine rows from the evidence table.
- Produces: `public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateKalmThroughLowerJunonDescriptions()` and inclusion in `CreateEarlyGameDescriptions()`.

- [ ] **Step 1: Add the minimal shared catalog method**

Create one cue per verified native trigger using the exact field, entity, script, byte index, opcode, and final narration from the evidence table. Append the method once to `CreateEarlyGameDescriptions()` so both linked runtimes consume it.

- [ ] **Step 2: Run the focused suites and verify GREEN**

Run the two commands from Task 2. Confirm all native-trigger checks and parity assertions pass.

- [ ] **Step 3: Refactor only catalog organization**

If the method is too large to scan, split it into private chronological region methods while preserving the public method's exact output order. Re-run both focused suites.

### Task 4: Prove the audited FMVs need no delayed timeline

**Files:**
- Modify: `analysis/kalm-through-lower-junon-description-cues.md`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Program.cs`

- [x] **Step 1: Review every scoped FMV for independent visual beats**

The audit retained one concise beat per FMV. Record that delayed narration would add skip risk without conveying additional equivalent information.

- [x] **Step 2: Verify native movie ownership in Ghidra**

Confirm the installed x86 movie-active flag has native start, stop, and loop references, and record the evidence count.

- [x] **Step 3: Verify both runtimes support every selected cue opcode**

Assert each shared cue uses a native ingress opcode owned by x64, while the x86 native-script audit confirms the exact byte and opcode.

### Task 5: Verify shared Prism delivery

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Program.cs`

- [x] **Step 1: Verify catalog inclusion and global uniqueness**

Construct the complete shared tracker and reject any duplicate native key.

- [x] **Step 2: Verify dual-runtime catalog compilation**

Run the focused x86 native-script suite and the x64 linked-catalog suite. Existing Prism dialogue-priority and one-shot tracker behavior remains unchanged.

### Task 6: Verify, deploy, and prepare the playable build

**Files:**
- Modify only if needed: `tools/Deploy-*.ps1` or existing packaging manifests
- Do not modify: live user configuration files

**Interfaces:**
- Consumes: all production code and tests from Tasks 1-5.
- Produces: updated x86 and x64 mod assemblies in every active game install.

- [ ] **Step 1: Run focused and full verification**

Run:

```powershell
dotnet run --project Ff7.Accessibility.Reloaded.Tests/Ff7.Accessibility.Reloaded.Tests.csproj -c Release
dotnet run --project Ff7.Accessibility.Steam2026X64.Tests/Ff7.Accessibility.Steam2026X64.Tests.csproj -c Release
dotnet run --project Ff7.Accessibility.Parity.Tests/Ff7.Accessibility.Parity.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\Run-DualRuntimeVerification.ps1
git diff --check
```

- [ ] **Step 2: Build release assemblies**

Build the x86 and x64 projects in Release and record output paths, file versions, and SHA-256 hashes.

- [ ] **Step 3: Deploy without configuration overwrite**

Copy only new binaries and required shared assets into the three active Reloaded mod directories. Preserve `Config.json`, user speech settings, navigation settings, and all unrelated mods.

- [ ] **Step 4: Smoke-test native loading**

Launch each available runtime far enough to confirm Reloaded loads the correct architecture assembly and the mod log contains no duplicate-load, missing-dependency, or architecture errors. Use a Kalm save when available to confirm the first cue is produced by the native trigger.

- [ ] **Step 5: Commit the implementation**

Stage only the evidence note, plan, tests, production source, and required project files. Preserve unrelated working-tree edits. Commit with a message describing Kalm-through-Lower-Junon descriptions.
