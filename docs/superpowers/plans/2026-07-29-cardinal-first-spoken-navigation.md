# Cardinal-First Spoken Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove false diagonal speech caused by lateral drift and short funnel corners while retaining genuine sustained diagonal guidance.

**Architecture:** Change only `FieldNavigationSpokenCueFormatter`'s direction classification. Pass the existing per-field spoken-distance scale into segment resolution so diagonal eligibility can use both an angular ratio and a meaningful-distance floor; leave all route and collision structures intact.

**Tech Stack:** C# 12, .NET 8, the existing console regression suites, Reloaded-II dual-runtime packaging.

## Global Constraints

- Preserve native field control rotation.
- Do not alter walkmesh routing, obstacle recovery, beacons, progress, or ladders.
- Apply identically to x86 and Steam 2026 x64 through the shared implementation.
- Install the verified package without launching the game.

---

### Task 1: Lock cardinal-first behavior with regressions

**Files:**
- Modify: `reloaded/Ff7.Accessibility.Reloaded.Tests/Program.cs`

**Interfaces:**
- Consumes: `FieldNavigationSpokenCueFormatter.Format` and `FieldNavigationConnectedRunFormatter.Format`.
- Produces: failing behavioral coverage for the captured stair chord and a sub-count diagonal correction.

- [ ] **Step 1: Add the captured stair regression**

Add a test using position `(-137,257,-10)`, immediate waypoint
`(-86,167,0)`, and the first three literal `blinst_2` stable points. Assert
that connected guidance contains no diagonal hyphen and reports direction
`down`.

- [ ] **Step 2: Add the meaningful-distance regression**

Format a `55,55` vector at a scale of `60` and assert that the result contains
no hyphen, while retaining the existing literal `up-right 4` expectation for
`240,240` at scale `80`.

- [ ] **Step 3: Run the Reloaded suite and verify RED**

Run:

```powershell
dotnet run --project .\reloaded\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj --configuration Release
```

Expected: failure showing the captured stair chord is diagonal rather than
cardinal `down`.

### Task 2: Implement meaningful diagonal classification

**Files:**
- Modify: `reloaded/Ff7.Accessibility.Reloaded/FieldNavigationSpokenCue.cs`
- Modify: `reloaded/Ff7.Accessibility.Reloaded/FieldNavigationAssistant.cs`

**Interfaces:**
- Consumes: world delta, native `FieldNavigationControlTransform`, and the existing spoken-distance count scale.
- Produces: `TryResolveSegment(..., int distanceUnitsPerCount, out FieldNavigationSpokenSegment)` with cardinal-first diagonal eligibility.

- [ ] **Step 1: Pass the count scale into every spoken segment resolution**

Update direct, connected-run, and fallback speech call sites to provide their
already-resolved `distanceUnitsPerCount`.

- [ ] **Step 2: Apply the two diagonal gates**

Use a minor-to-dominant ratio of `0.75` and require the minor magnitude to be
at least `Math.Max(1, distanceUnitsPerCount)`. Preserve the existing dominant
cardinal fallback and Euclidean distance only for accepted diagonals.

- [ ] **Step 3: Run the Reloaded suite and verify GREEN**

Run the Task 1 command. Expected: `Reloaded accessibility tests passed.`

### Task 3: Verify and deploy both runtimes

**Files:**
- Verify: `reloaded/Ff7.Accessibility.Shared.Tests`
- Verify: `reloaded/Ff7.Accessibility.Steam2026X64.Tests`
- Verify: `reloaded/Ff7.Accessibility.Parity.Tests`
- Deploy: `C:\Users\buu42\AccessXI\external\Reloaded-II\Mods\ff7.accessibility.reloaded`

**Interfaces:**
- Consumes: the green shared implementation.
- Produces: one installed, validated x86/x64 Reloaded-II package.

- [ ] **Step 1: Run all remaining suites**

Run the shared-layout, Steam 2026 x64, and parity projects in Release. Each
must exit zero with its pass message.

- [ ] **Step 2: Confirm FFVII and Reloaded-II are not running**

Check `FFVII`, `ff7_en`, `FFVII_LAUNCHER`, and `Reloaded-II` processes. Do not
install over a running game.

- [ ] **Step 3: Install and validate**

Run `Install-FF7ReloadedMod.ps1` with the current game root,
`-SkipFfnx -SkipSeventhHeavenSettings -AllowResearchNativeProfile`, then run
`Assert-Ff7DualRuntimePackage` against the installed mod directory. Record
the backup path and resulting fingerprint.
