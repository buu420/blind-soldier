# Mansion flashback bookcase implementation plan

> For agentic workers: use superpowers:executing-plans to execute and review this bounded fix.

**Goal:** Make the visible mansion bookcase navigable and guide the flashback through its passage.

**Architecture:** Use the existing Objects and Story readers. Field 300 entity 7 has no native progression gate: its OK handler opens the background panel and releases triangles 143, 67 and 120 when temporary Bank 5 byte 7 is zero. Story at moments 370 and 373 must approach that line before offering gateway 1.

**Tech stack:** C#, PowerShell, licensed stock/Echo-S field archives, Ghidra 12.1.2.

**Spec:** User-confirmed scope in this chat, 2026-10-13: bookcase in Objects, correct flashback Story, both runtimes and Echo-S, deploy and publish. Normal game interaction opens it.

## Global constraints

- Preserve saves, settings, recorded descriptions and unrelated mods.
- Use native line availability, player radius, facing and panel state; never automate Confirm or change game locks.
- Keep the other optional mansion objects restricted to their existing native chapters.
- Release 0.10.0 after 0.9.9, following the requested version rollover convention.

## Review focus

- Closed panel: reach the bookcase without crossing native locked triangles.
- Open panel and return from stairs: retire the interaction, offer the forward passage or the return route as appropriate.
- Disabled line or unreadable geometry: no false interaction target/readiness.
- Present-day visits: preserve the bookcase interaction without duplicating it.
- Echo-S: verify native LINE, condition, locks and background animation across all five reviewed variants.

## Task 1: Repair the native bookcase targets

Files: NibelheimObjectCatalog.cs; Generate-FieldStoryEvents.ps1; generated field_story_events.json; NibelheimStoryTests.cs; KalmRanchNavigationTests.cs; Steam2026MansionSecretDoorExitTests.cs; both test entrypoints.

Interfaces: existing FieldNavigationObjectDefinition, FieldStoryEventDefinition, FieldStoryTargetReader, FieldNavigationObjectReader, FieldWalkmeshRoutePlanner and Steam2026FailClosedFieldRoutePlanner.

- [ ] Add a failing regression for closed/open flashback targets and normal Confirm readiness, then run it.
- [ ] Remove only the bookcase's artificial present-day bound, identify it as the visible bookcase, mark its native OK interaction.
- [ ] Author closed-bookcase Story at moments 370/373, followed by gateway 1 only when Bank 5 byte 7 is one; regenerate the catalog.
- [ ] Verify closed-bookcase routing and navigation activation with native locks in both planners; verify open-passage routing and the return path.
- [ ] Verify exact script witnesses against stock and all reviewed Echo-S variants.

## Task 2: Verify, deploy and release

Files: release notes and validation documentation; runtime metadata; private release evidence and scripts; handoff.

- [ ] Run the dual-runtime release gate, release contracts, exact packaged suites and final review.
- [ ] Deploy verified payloads with backups and hash-check protected files.
- [ ] Merge reviewed source, publish matching portable and manager assets, verify public hashes and catalog entries.
- [ ] Save an accurate handoff and state the unperformed live gameplay/speech validation.
