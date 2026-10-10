# Highwind interior navigation implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make the Highwind's ordinary room navigation and crew services identifiable and usable between Story events in both runtimes.

**Architecture:** Keep native gateway, LINE, NPC and walkmesh readers. Add reviewed labels to the shared exit resolver and NPC reader; the existing native state gates remain authoritative.

**Tech stack:** C#/.NET 8, native FFVII field archives, PowerShell, Ghidra.

**Spec:** ../specs/2026-10-10-highwind-interior-navigation.md

## Global constraints

- Version 0.9.1; preserve released 0.9.0 work.
- Both legacy x86/7th Heaven and Steam x64.
- Existing controls and native interaction buttons.
- Native visible actors and available exits only; preserve settings, saves, description recordings and history on deployment.

## Review focus

- Identical map names must not leave different room exits indistinguishable.
- The native cockpit return LINE aliases [OK] and Move; no new Confirm automation or invented script transition.
- Rooms must remain navigable when Story is empty, and when Story is active.
- Hidden/non-talkable crew and disabled native routes must stay absent.
- Corridor stairs and return paths must work from actual native arrival triangles.

## Task 1: Ordinary Highwind map and services

**Files:** `FieldExitLabelResolver.cs`, `FieldNavigationNpcReader.cs`, `FieldScriptExitBranchPolicy.cs`, `HighwindInteriorNavigationTests.cs`, both test entry points and x64 linked test project.

Ruling: native tests showed the existing generic exit guards do not resolve fship_4's GameMoment word branches. Resolve only its reviewed forward door in the shared branch policy (already wired in both runtimes). Boundaries and destinations are checked against the installed script; preserve the stable identity and every other exit.

**Interfaces:** Existing `FieldExitLabelResolver.Resolve`, `FieldNavigationNpcReader.ReadTargets`, native archive readers and `FieldNavigationTargetSource.GetTargets` remain unchanged.

- [ ] Add focused tests for room labels, NPC identities, empty/active Story categories, visibility/talkability gates, native exits and arrival routes.
- [ ] Run the focused suite before production changes; retain the expected failing label assertions.
- [ ] Add only native door and crew labels to the existing shared readers.
- [ ] Run the focused suite against both compiled runtime assemblies and their installed archives.
- [ ] Record native proof and limitations in the validation document; update 0.9.1 metadata, release notes and usage guidance.

## Task 2: Review, verify and release

- [ ] Request one fresh-context review of the entire branch; resolve material findings.
- [ ] Run the full Research dual-runtime gate with required installed game data.
- [ ] Check exact portable/AMM archives and full suites against their retained DLLs.
- [ ] Back up changed installed files, deploy both mods and Steam launcher, and verify protected files unchanged.
- [ ] Publish PR/tag/release and both AMM entries using the verified release workflow; preserve concurrent catalog changes.
- [ ] Archive evidence and write the handoff, separating automated evidence from unperformed live gameplay.
