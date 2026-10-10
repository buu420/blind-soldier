# World tracking and piano repairs implementation plan

> **For agentic workers:** Use superpowers:executing-plans for the root implementation. Claude owns the explicitly delegated piano work in the same checkout with separate build output.

**Goal:** Repair the tester's Weapon tracking, Northern Crater navigation, and piano accessibility on legacy x86 and Steam 2026 x64, then deploy and release the verified result.

**Architecture:** Follow the native world entity/script lifetime and real coordinate/arrival conditions. Preserve current target identity across fresh movement without retaining stale entities. Decode native visible button instructions and give active piano input back to the game, preserving existing controller safety elsewhere.

**Tech Stack:** C#/.NET 8, shared legacy layout readers, translated x64 address space, Ghidra, native field/world archives, PowerShell packaging, Prism and existing Reloaded hooks.

**Spec:** The user's October 9 tester report and explicit instruction to fix everything; the native evidence and exact-release reproductions in `C:\Users\buu42\Documents\FFVII-ActiveBuild\tester-world-piano-20261009`.

## Global constraints

- Both FFVII runtimes are required; no save/settings/history/narration loss.
- Do not write game stages, enemy health, native entity coordinates, or piano notes.
- Track native available entities only; do not invent a hidden Weapon location.
- Native game interaction, combat, piano playing and Highwind landing remain manual.
- Current branch is `fix/world-weapon-crater-piano-20261009`, baseline `71c089e52e1d7e4cb2d5875d2ce4617735af774b` (0.8.10 source tree).
- No repeated scope or release approval is needed; both are authorized.
- Research and build evidence stay outside packaged product data. Do not publish licensed extracted resources.

## Review focus

- Weapon list lifetime during flight, battle escape and field/world re-entry; distinguish transient reader failure from native absence.
- Dynamic target coordinates over wrapped map edges, midair terrain and active route refresh.
- Crater point arrival includes height; final landing uses its own terrain condition and story state.
- Piano protection applies to active native note/chord threads, not the whole room or every busy dialog.
- Japanese/unknown text, settings ownership and Steam R3 release suppression remain correct.

### Task 1: Native Weapon tracking

**Files:** `WorldMapEntityReader.cs`, `Steam2026WorldMapAddressSpace.cs`/related native world translation, `WorldMapTargetCatalog.cs`, `WorldMapNavigationController.cs`, focused world reader/catalog/controller tests as evidence warrants.

- [x] Trace actual native creation, visibility, escape, movement and destruction; inspect x64 translation and catalog exclusions.
- [x] Reproduce the identified tracking fault with native-backed input fixtures and a failing regression.
- [x] Repair the responsible reader/catalog/controller layer; preserve the absence of a truly removed entity.
- [x] Verify fresh moving target tracking, escape/re-entry and disappearance handling on both runtime paths.

### Task 2: Northern Crater navigation

**Files:** `WorldMapTargetCatalog.cs`, `WorldMapRoutePlanner.cs` if needed, world catalog/highwind tests.

- [x] Add a failing regression from the logged remote flight position and native point 14.
- [x] Use absolute wrapped goal distances and aim point objectives near the native point, not an arbitrary outer boundary.
- [x] Keep altitude-aware native arrival and terrain-based manual crater landing.
- [x] Verify route completion at the recorded height, no local false destination, and state-appropriate selection.

### Task 3: Piano speech and native controls (Claude ownership)

**Files:** shared encoded-text decoder and tests, focused piano-state reader, controller context/policy and both field hosts as required by native phase evidence.

- [x] Reproduce incorrect native Start and shoulder/direction glyph speech with failing regressions.
- [x] Correct verified paired glyphs; retain unknown/Japanese decoding behavior.
- [x] Identify active native piano phase and protect its normal note/chord/end controls with scoped tests.
- [x] Root review the diff and run the focused checks independently.

### Task 4: Integration, deployment and release

- [ ] Run focused dual-runtime checks and fresh whole-branch review; fix material findings.
- [ ] Verify next release version, bump package/launcher labels, run required release gates and exact-package suites.
- [ ] Back up installed payload and hash protected files; deploy both runtimes and launcher; verify payload and preservation.
- [ ] Create/attach PR, finish required CI/merge, publish beta assets and Author CLI 0.30.0 catalog entries with public hash verification.
- [ ] Save release handoff and local archive; state precisely what was tested and any missing live gameplay validation.
