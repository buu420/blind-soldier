# Whole-game engine and interaction audit

> Execution: Codex owns native engine exports and integration. The existing Claude teammate owns the bounded field-state audit and subsequent reviewed fixes. Preserve that method; do not create additional agents.

**Goal:** Replace selected-checkpoint confidence with a reproducible inventory of the complete installed engine and all native script entry points, then repair supported accessibility omissions in both runtimes.

**Architecture:** Keep approximate native decompilation, decoded game resources, shipping accessibility behavior, and live verification as separate evidence layers. Every field and script has a recorded disposition. Unknown conditions remain review findings rather than silently becoming an available NPC or a passing route.

**Tech stack:** Ghidra 12.1.2, Java 21, .NET 8, PowerShell, Python standard library, licensed English PC game data.

**Specification:** The user requested the entire game engine and all story/NPC coverage, including previously unreported problems. Ordinary visible player information is the accessibility boundary. The existing v0.6.8 install is the deployment baseline.

## Constraints and review focus

- Do not distribute game binaries, decompiled game code, raw dialogue, saves, or extracted game assets in Git.
- Keep generated research in a private directory outside this repository; bind outputs to source hashes.
- Ghidra output is approximate, not restored original source. Failed or degraded functions and unassigned executable ranges must be counted.
- Shared script entry addresses must preserve every caller's identity. A return on one conditional branch must not hide another branch.
- Independent quest flags, party membership, Talk/VISI/LINE gates, dynamic locks and field revisits must not be approximated by GameMoment alone.
- World and battle VMs have different instruction formats; field decoding is not proof of coverage of either.
- A static arrival route is not proof of a complete live playthrough. Preserve blocked and dynamic cases in the final ledger.
- Keep settings, room-description history, saves, narration and voice ownership intact when deploying.

## Native executable inventory

Files: `analysis/ghidra/ExportWholeGameEngine.java`, private `engine/*`, private `inputs/manifest.json`.

- [x] Hash both actual installed executable versions and copy only those inputs privately.
- [x] Implement exports for function C, assembly, references, symbols, memory blocks, failures, and unassigned executable ranges.
- [x] Compile and run the exporter on the project's own native DLL before trusting game exports.
- [x] Finish both game exports; reconcile internal functions versus external imports and validate artifact counts and hashes.
- [x] Record decompiler warnings separately from hard failures, and inspect script/interaction dispatchers and runtime translation boundaries.

## Complete field-state inventory

Files: `tools/WholeGameStateAudit/`, its synthetic tests, and private `fields/` reports.

- [x] Compare the shipping parser with independently bounded script tables and control-flow traversal.
- [x] Exercise shared entries, early returns, conditional branches, cross-entity requests and banked conditions with synthetic fixtures.
- [x] Extract every readable field from both installed archives, including all entry aliases, condition dependencies and interaction effects.
- [x] Compare native evidence with Story, NPC, Object and Exit discovery. Preserve unknown or dynamic reachability as distinct results.
- [x] Repair systemic parser/reader omissions only after independently reviewing the baseline and native examples.
- [x] Re-run the complete inventory after fixes; add regression evidence to both runtime hosts.

## Other engine resources

Files: `tools/WholeGameResourceAudit/`, private `resources/`.

- [x] Inventory and validate world event call tables; follow branches across entry boundaries and retain duplicate/aliased entries.
- [x] Inventory all compressed battle scenes and kernel sections, preserving script offsets and explicitly unresolved instructions.
- [x] Bind resources to both installations; distinguish identical shared data from runtime-specific engine behavior.

## Integration and verification

- [x] Create a field-by-field ledger with native findings, supported fixes, reviewed exclusions and remaining unknowns.
- [x] Re-run the focused native route/state checks for prior failures: Junon, Corel, Gold Saucer, Cosmo Canyon, Nibelheim, Rocket Town, Wutai and world vehicles.
- [x] Build and run both full runtime test hosts with their installed data; inspect any failures before packaging.
- [x] Independently review the final runtime changes with the existing Claude teammate.
- [x] Build and deploy verified development payloads to both local game installations, preserving user data and verifying copied hashes.
- [x] Report exact coverage and remaining limits. Do not label the entire game playable without end-to-end live evidence.

Final integration evidence is recorded in `analysis/2026-09-24-whole-game-engine-audit.md`.
Both local installs were updated at 2026-09-25 08:02 UTC after stable-source full
suites, the 280-checkpoint four-way Story matrix and independent tests of the
packaged DLLs. Backups and narration/configuration preservation are hash verified.
Claude's closing report was synchronized at 08:07 UTC with no active work remaining.
The final ledger preserves all 890 candidates, including 160 repaired findings and
no undisposed original candidates. Root independently verified the exact package
and deployment; the remaining static/live limits are retained in the report.
