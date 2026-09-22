# Remaining game coverage implementation plan

**Goal:** Find and repair missing story routes and visible interactions across the remaining FFVII towns and story areas, using native evidence, then deploy both runtimes.

**Architecture:** Existing native field extraction supplies scripts, actors, exits and traversal geometry. Reviewed story definitions supply progression intent, while live state controls visibility and availability. Extend those layers where evidence identifies a gap; do not substitute unconditional waypoint lists or count entries as proof of coverage.

**Scope:** The user's request includes remaining story elements, towns, NPCs and optional objects throughout the game. Minigame controls and new narration production remain outside this navigation pass. Preserve the preceding verified Nibelheim repairs, settings and voice assets. No public release is requested.

## Work and ownership

- [x] Root: Export every installed field's script entities, native opcodes, dialogue references, exits and discovered interactions into an auditable local inventory. Compare this with existing NPC/object readers; inspect relevant engine semantics with Ghidra and primary community references.
- [x] Claude, then root after the service failed: Review chapter continuity from the existing story regions and native scripts, starting after Nibelheim and proceeding to the ending. Record required steps, automatic scenes, optional branches and explicit gaps. Repair grounded omissions with state gates and native geometry, and add regression cases. Own story-region scripts, Story JSON and new focused story tests; coordinate shared registrations with root.
- [x] Root: Review every town/settlement in the inventory, including optional settlements and return visits. Fix systemic discovery failures when demonstrated and add named unmodelled interactions only with native visibility/activation evidence. Add focused reader/route regressions rather than tests that only assert counts.
- [x] Joint: Reconcile remaining gaps against the guide and native script paths, including silent intervals with no GameMoment writes, scene callbacks, disabled gateways, local flags and disconnected walkmesh components. Explicitly distinguish unavailable content from missing accessibility information.
- [x] Root: Run native audits and selected-state/route suites against both archives and both shipping assemblies; run full suites. Review Claude changes independently. Package and deploy both installations with settings and narration hashes preserved.

## Completion evidence

Maintain an area-by-area ledger tied to native fields and tested states, with unresolved cases visible. Each implemented action needs evidence of a visible interaction or required route, availability gates, and the correct native target. A static match cannot prove a complete live playthrough; report precisely which checks were performed and any remaining limitations.
