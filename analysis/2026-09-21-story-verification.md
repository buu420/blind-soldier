# Story verification and Nibelheim repair — 2026-09-21

The reported Steam 2026 x64 session was at GameMoment 523. Story was empty in present-day Nibelheim and the mansion even though normal exits and objects were present. The catalog covered the flashback but omitted this present-day interval. Progress through this area does not write a new GameMoment at every interaction, so extracting milestone writes alone did not establish chapter coverage.

## Changes

- Added 23 Story route definitions for present-day Nibelheim, its buildings, and mansion return paths. They carry native crossing lines so navigation reaches the exit rather than stopping near it. The required path through town points toward Mount Nibel; the optional mansion is not a required detour.
- Added 12 mansion interactions to Objects: the entrance note, piano, safe, two floor writings, coffin, two specimen tanks, and four reports. Native enabled-line state and collection/availability flags govern them. They remain available on later visits. All eleven LINE interactions use the native player collision radius. Piano and safe guidance explains how to begin their existing game interactions.
- The Sephiroth scene in field 308 is automatic. Its director tests and sets bank 1[231] bit 1. It is reached through field 305's ordinary exit; no invented Talk target was added.
- Corrected 12 Story LINE bindings against native instructions: Reactor 5, the Shinra cell-block blood trail, four Corel train triggers, and six Northern Crater triggers. Both the referenced entity and enabled-line gate are checked together.
- Corrected nine object entity references: six chests and three save points in Northern Crater fields 747, 749 and 757. Native item IDs and collected flags were retained and verified.
- Corrected 17 provenance-name errors and removed one impossible milestone row (field 154, minimum 78 but already completed at 72).
- Fixed story regeneration losing carried-forward definitions from unreadable source files. Merging now precedes deduplication. A coverage ledger records unreadable sources, unresolved/automatic writes, retained unverified definitions and rejected impossible rows; rejection is also warned on the console.
- Added `tools/StoryCoverageAudit`, a dual-runtime runner, and independently specified checkpoints. The audit checks field identity, native entities, LINE geometry, triangle bounds, moment intervals and supported state flags. Named-entity checks also handle duplicate names when the referenced entity is not one of them.
- Registered the existing main story regression suite in x64 and added both portable Story suites to CI. Updated two older test assumptions: the Ether is no longer the only independently reviewed native-radius object, and Reactor 5's frozen fixture now uses the actual native line.

## Verification

Investigation artifacts: `C:\Users\buu42\Documents\FFVII-ActiveBuild\story-verification-20260921`.

- Both shipping assemblies audited against both installed archives: all four combinations passed. Each report inventories 702 native fields, 1,134 Story definitions and 464 object definitions. The decoded fields match between the two installed archives.
- 38 selected Story checkpoints passed in each combination. Thirteen carry exact native opcode anchors; the others replay reviewed state/flag expectations. These are selected states, not every possible progression state.
- Nibelheim native route replay passed in all four combinations: 41 Story routes over 22 fields and 25 optional-object routes. Story endpoints reach their authored native crossing; object endpoints fall inside the required interaction radius. Availability tests include first arrival, later returns, disabled lines and completed flags.
- Full x86 suite against the legacy archive: passed. Full x64 suite against the Steam archive: passed. The test host reports that installation of the native keyboard overlay is not exercised there; this task makes no new live-hook claim.
- Three regeneration tests and eight CI-contract tests passed. PowerShell scripts parsed successfully and `git diff --check` passed.
- Negative audit probes rejected a missing expected target, missing expectations and an altered native anchor.
- Ghidra headless inspection of the native bank read/write functions corroborated bank-address mapping used by checkpoint replays. Logs are `ghidra-story-banks.log` and `ghidra-console.log`.
- Claude implemented the bounded Nibelheim work and reviewed the other changes. Root independently verified the native evidence, reviewed the edits and ran final checks. Final review found no blockers; applicable audit/generator observations were addressed.

The three manual-slot audit candidates were inspected rather than turned into targets: field 93 `blackbg1` contains developer/debug actions; fields 765/766 entity 3 `batkun` are battle-controller callbacks called by the party scripts, not visible interactable NPCs. Their raw evidence remains in `manual-review-*.txt`.

## Installation

The verified dual-runtime package is installed in:

- `C:\Games\Final Fantasy VII\Reloaded-II\Mods\ff7.accessibility.reloaded`
- `C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII Steam Edition\Reloaded-II\Mods\ff7.accessibility.reloaded`

Each installation changed 26 files, verified all 4,038 payload files against the staged package, and preserved user configuration byte-for-byte. Changed files were backed up under the investigation directory. The 846 recorded voice clips are unchanged. No new GitHub release, version bump, tag, push or commit was made; the local build retains version 0.6.1.

Evidence files: `final-audit/results.json`, the four archive reports and replay logs, `full-test-results.json`, `pester-final-results.json`, `payload-audit.json`, and `deployment-report.json`.

## Limits

No complete live playthrough was performed. Passing the inventory and selected checkpoints does not establish that every chapter, side interaction, world-map route or minigame can be completed. Nibelheim field 306 has an authored return matching its variant geometry but no independently recorded incoming route, so it is explicitly absent from route-replay coverage. Existing controls and screen-reader output still need gameplay confirmation at the reported save.

Reference reading: [AbsoluteSteve's Nibelheim/Shinra Mansion guide](https://www.supercheats.com/guides/final-fantasy-vii/nibelheim-shinra-mansion) and the [FFVII native field opcode reference](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes.html). Installed native scripts, local state and walkmeshes are the binding evidence; guide prose does not define trigger geometry.
