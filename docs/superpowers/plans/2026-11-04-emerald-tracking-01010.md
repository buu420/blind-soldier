# Emerald Tracking Implementation Plan

> **For agentic workers:** Execute the approved change with test-first implementation, bounded Claude Teammate ownership and one independent whole-branch review. Root owns integration, publication and deployment.

**Goal:** Make Emerald's current native position trackable before first sighting, with consistent deliberate approach and ordinary-route avoidance.

**Architecture:** Separate live native tracking from render visibility. The shared world-map runtime and target/controller pipeline consume the same coherent native observation on both runtimes. Render visibility remains an independent fact used for truthful sighting announcements.

**Tech Stack:** C#/.NET, Reloaded-II, shared legacy address-space abstractions, x86/x64 native-data regression harnesses.

**Spec:** `../specs/2026-11-04-emerald-key-story-01010.md`.

## Global constraints

- Version 0.10.10; canonical Dropbox checkout only; no duplicate source worktree or Documents tree.
- Both runtimes and the existing Echo-S/Postscript regressions remain supported.
- Root owns version/docs/Program registrations, commits, deployment and publication. The Emerald implementer owns only WorldMap* production/tests and any required world-map host read path.
- Root does not build while the Emerald implementer uses the canonical test-output lane. Audit work may use existing compiled tools read-only.
- No game execution, automatic Confirm or native quest-state writes.

## Review focus

- First underwater entry without any render sample must still offer a usable current Emerald target.
- Moving hidden Emerald must update both active pursuit and avoidance consistently.
- Duplicate/replaced/retired entities and torn reads must not drive toward stale or invented coordinates.
- Camera loss must not stop a coherent live pursuit or falsely announce a sighting.
- Existing discoveries, normal underwater destinations and navigation around wrap/unsafe depths must retain their behavior.

## Task 1: Live tracking, pursuit and avoidance

**Files:** `Ff7.Accessibility.Reloaded/WorldMapEmeraldMemory.cs`, `WorldMapRuntimeContext.cs`, `WorldMapTargetCatalog.cs`, `WorldMapSubmarineNavigationController.cs`, `WorldMapNavigationController.cs`, any required shared world-map entity read/route policy, and corresponding `Ff7.Accessibility.Reloaded.Tests/WorldMap*Tests.cs`.

**Interfaces:** Consume `WorldMapEntitySnapshot` from checked `WorldMapRuntimeContext.UpdateEntities` calls; publish through its existing Events target provider and `WorldMapNavigationController`. Keep `IsVisibleUnderwater` truthful and independent of the tracking decision.

- [ ] Add regressions for a never-visible live contact, hidden patrol movement, continued selected pursuit and ordinary-route avoidance; observe the current implementation fail for the intended policy gap.
- [ ] Implement the live native tracking policy in the shared pipeline without marking hidden entities visible. Ensure the source/read path provides coherent current coordinates independently of render sampling.
- [ ] Preserve immediate input release on invalid state and retirement/new-world handling. Add lifecycle and duplicate/identity regressions where the old sighting tests no longer express the authorized policy.
- [ ] Run the existing underwater/world-map selectors against native data; verify fresh current positions, actual controller input and safe ordinary-route behavior, alongside unchanged visibility-discovery tests.
- [ ] Root reviews the diff and retained red/green evidence, then frees the test-output lane for the Key work.

## Task 2: Release integration

- [ ] Integrate the separately audited Key Story change and bump all owned version/launcher/package metadata to 0.10.10.
- [ ] Freeze the source manifest, obtain independent Opus 5.5/max review and run the required dual-runtime release gates plus exact-package suites.
- [ ] Deploy the exact verified payloads with preservation receipts and backups while both runtimes are closed.
- [ ] Publish merged/tagged source, eight verified public assets and both Manager records; verify CI and complete anonymous downloads.
- [ ] Update the canonical handoff/pointers, remove only allowlisted owned scratch and verify installed/public artifacts remain exact.
