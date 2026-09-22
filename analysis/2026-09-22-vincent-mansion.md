# Vincent recruitment and mansion navigation

Request: missing Shinra Mansion Exits and a complete check of the optional Vincent recruitment route, on both runtimes.

## Sources and reproduction

- [Absolute Steve's Nibelheim and Shinra Mansion walkthrough](https://www.supercheats.com/guides/final-fantasy-vii/nibelheim-shinra-mansion) supplies the quest checklist: note and clues, safe, Lost Number, key and Odin, basement coffin conversations, then departure toward the stairs. The guide is a checklist; native scripts and walkmeshes determine coordinates and availability.
- The reported x64 capture is `Downloads/ff7_accessibility_steam2026_x64 (7).log`, copied into `FFVII-ActiveBuild/vincent-mansion-20260922/reported-x64.log`. On September 22 it reports field 300 with two native exits but only one reachable exit. Field 297 also varies between seven, five, one and zero reachable exits. Thus neither a static gateway count nor a Story row proves the live Exits list works.
- Installed `sinin1_1` through `sininb52` fields were decoded with `FieldRouteDump` and the production field-script decoder. Native x86 executable handlers were independently decompiled with Ghidra: LINE `0x6111D8`, LINON `0x6115AD`, IDLCK `0x61E29F`, KEY `0x61220D`, KEYON `0x61225D`. IDLCK writes the triangle bit at field-global +0xB2; a nonzero argument locks and zero clears it. LINE creates an enabled interaction record; LINON updates that record's enabled state.

## Quest interaction gaps

- The x64 checked planner omitted `IFieldNavigationNativeBoundaryStatus`. Its inner planner correctly found the panel's locks, but the reachable-exit provider could not see that status and removed the stairs exit. The wrapper now preserves the coherent boundary result, including a captured result during prepared action replay; failed native reads do not publish a lock or an exit. A regression using the installed field 300 mesh, native locks, real wrapper and real provider reproduced two exits on x86 versus one on x64 before the fix, and two on both afterward. No route crosses the locked panel.

- Field 299, entity 7 `box0`: collecting Enemy Launcher sets Bank 15 address 35 bit 3. The existing treasure target then disappears, but Talk still displays the chest-lid inscription (MESSAGE 178 at byte 88). An opened-chest Objects target now appears under that same collection flag, using the live visible/talkable model. It does not announce the solution.
- Field 300, entity 8 `fl0`: the native floor-search line was absent from Objects. Its midpoint is (814,328,339). Entity 9's separate floor-writing interaction remains intact.
- Field 300, entity 7 `lin0`: the hidden panel needs OK before its exit is walkable. Director Init locks triangles 143, 67 and 120; Talk unlocks them and sets temporary Bank 5 address 7 to 1. A door target at the LINE midpoint (906,623,339), with the native player interaction radius, remains until that flag changes. Normal interaction-arrival speech prompts the player to interact; after opening it, the passage exit is walkable.
- The existing safe and piano rows incorrectly used `ManualNavigationGuidance`, which stops the navigation controller from starting a beacon at all. Removed that manual-only marker from these walkable interactions. An actual `FieldNavigationController.ToggleBeacon` regression reproduced the safe failure before the fix; it now checks safe, panel and piano approaches, not merely the planner's ability to return a route.
- Mansion exit labels now distinguish the two upstairs wings, three ground-floor doors, spiral stairs, corridor and deeper library rooms. Identical broad map names no longer obscure those choices.

## Quest steps already supported, now checked explicitly

- The safe target remains after opening/Lost Number (Bank 1 address 232 bit 0), until the key is taken (bit 1). The safe dial's existing spoken numbers are preserved. The Odin model uses the existing materia target and its live visibility/collection state.
- The basement door is native triangle 34, locked while the key-collected bit is clear. Tests prohibit routing through it before the key and prove the coffin route after unlocking.
- The coffin remains available across conversation bits in Bank 1 address 231. Its retirement uses Bank 13 address 80 bit 2, set by field 302 entity 11's departure scene. Native dialogue and choices remain under the player's control.

## Verification scope

`NibelheimStoryTests` now checks the additional interaction state changes, installed opcode bindings and actual line midpoints, native lock transitions, and routes to all optional interactions (including the opened chest). The missing floor interaction was reproduced as a failing test before its catalog entry was added. `NibelheimMansionExitTests` separately audits gateways and script exits; Story coverage is not treated as Exits coverage.

These are native-data and host regressions, not a claim that a new save has been played through Lost Number and Vincent's recruitment. Live controller traversal remains the final gameplay check.

Full x86 and x64 executable suites, Shared and Parity suites, and the dual-runtime shared-source check passed. The separate mansion audit checks 92 gateway routes and samples 908 triangle positions; the new x64 regression exercises the missing-exit failure rather than relying on those static route counts. Two additional autowalk stalls in the capture (hall at 268,280 and library at -34,-1) have not been reproduced as native movement failures; these fixes do not claim to resolve those stalls. Automated path existence alone cannot prove a complete live playthrough.

The validated dual-runtime package was deployed locally to both installed mod directories. Each deployment replaced 16 changed files and verified all 4,038 package payload hashes; Configuration was excluded and user settings were unchanged. Backups and `deployment-report.json` are in the investigation directory. Public version metadata remains 0.6.3; this task did not publish a release or commit source changes.
