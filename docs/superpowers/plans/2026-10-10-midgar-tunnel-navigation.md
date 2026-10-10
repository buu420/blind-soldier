# Midgar tunnel navigation implementation plan

> **For agentic workers:** Use superpowers:executing-plans for the root's implementation; Claude owns the bounded pickup audit. Perform a fresh whole-branch review before release.

**Goal:** Restore native Story and visible Objects coverage for the late Midgar tunnels in release 0.9.0.

**Architecture:** Keep the existing shared field readers and navigation controls. Correct the curated Midgar rows using native section/encounter state, and add a manual ladder target. Change pickup handling only for a reproduced omission.

**Tech stack:** C#, .NET, PowerShell generators, installed flevel archives, Ghidra, AMM Author CLI 0.30.0.

**Spec:** `docs/superpowers/specs/2026-10-10-midgar-tunnel-navigation.md`.

## Global constraints

- Both x86/7th Heaven and Steam x64; release 0.9.0, preserving older releases.
- Native visible state; no inventory, progression, coordinate or combat writes.
- Existing controls, protected settings, saves, narration and history preserved.
- Local playable installations are C:\Games\Final Fantasy VII and the Steam installation on D:; the UNC cwd is development scratch.
- Scope is already confirmed; execute without another approval checkpoint.

## Review focus

- Before and after the encounter, north and south tunnel visits need different lines.
- Section 18's pickups must remain discoverable without exposing them in other sections.
- A disabled native line or invalid section cannot produce a guessed target.
- The ladder instruction must preserve manual input and start no automatic walking.
- Regeneration must preserve unrelated fields and existing controller behavior.

## Task 1: Story continuity and ladder

**Files:** `tools/story-regions/MidgarRaid.ps1`, generated `Assets/navigation/field_story_events.json`, new `MidgarTunnelStoryTests.cs`, both test hosts and x64 test links.

- [x] Write and run failing regressions against 0.8.11 for pre-encounter sections, return route, disabled/invalid state and manual ladder guidance.
- [x] Bind rows to installed LINE/LADER/MAPJUMP operands and verify real walkmesh routes on both archives.
- [x] Correct section/chapter coverage, regenerate the catalog and verify only intended records changed.
- [x] Run focused green checks and shared native Story coverage.

## Task 2: Pickup coverage

**Files:** Claude owns the object generator/reader/catalog and a narrow new test file if required; root owns test registration.

- [x] Verify native model visibility, section and collected flags for each tunnel pickup.
- [x] Reproduce any real omission before implementing a minimal correction; otherwise retain the correct production behavior and pin it with coverage tests.
- [x] Independently inspect Claude's evidence and run both-runtime checks.

## Task 3: Verify, deploy and release

**Files:** ModConfig, launcher version, release workflow default, README, release/validation notes and external release evidence.

- [x] Set 0.9.0 and document concrete behavior and verification limits.
- [ ] Review the complete change with fresh context; resolve material findings.
- [ ] Run required release gates, full suites and exact packaged DLL tests serially.
- [ ] Deploy both verified installations and accessible launcher with backups and protected-data checks.
- [ ] Merge reviewed source, publish 0.9.0 downloads and both AMM beta records; verify public hashes and preserved catalog history.
- [ ] Archive evidence and an updated handoff in Development; do not write memory.

Ruling: use inline root implementation and the already authorized Claude pickup
subtask. The user confirmed this scope and previously directed autonomous fixes
and release; another plan-approval pause would repeat that confirmation.

Ruling: section 0 is a reachable native tunnel_4 boundary, established by 737's
section-one DEC/MAPJUMP and 736's Init. Expand its downward return, rather than
classifying zero as invalid. The new regression failed before this correction;
both focused and broader Story suites now pass against both installed archives.

Pickup audit found no production omission; retain the correct catalog and reader.
Root verified the native grants, collected flags and both-runtime coverage.
Integration uses the existing authorized merge/release flow; no new menu choice
is required from the finishing-a-development-branch skill.
