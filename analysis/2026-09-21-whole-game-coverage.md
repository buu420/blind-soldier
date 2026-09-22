# Whole-game navigation coverage, 2026-09-21

## Scope and evidence

The user requested the remaining main story, towns, NPCs and side interactions. This pass uses the licensed installed field archives, community format documentation, Ghidra and AbsoluteSteve's walkthrough. It does not claim a completed live playthrough, solve optional puzzles, or add new narration.

`tools/FieldInteractionAudit` exports all 702 readable fields: native entities, every decoded opcode, LINE geometry, dialogue references, gateways, models and the shipping catalog's discoveries. Raw extracted dialogue stays in the private working directory, not this repository. The 85 unavailable maplist entries include world/debug identifiers and are not counted as missing accessibility content.

Local evidence: `C:/Users/buu42/Documents/FFVII-ActiveBuild/whole-game-coverage-20260921`.

## NPC and optional-interaction audit

Existing town NPC discovery already handles the bulk of visible, talkable actors, including staff behind counters. Inspection covered the full field inventory, including early Midgar, Kalm, Junon, Costa del Sol, Corel, Gold Saucer, Gongaga, Cosmo Canyon, Nibelheim, Rocket Town, Wutai, Bone Village, Icicle, Mideel and late-game returns. Unlabelled models were separated from automatic actors, player avatars, props and minigame participants; a model's presence is not proof of a player action.

Fifteen explicit NPC labels repair actual unheaded Talk handlers: Reno in the church (183/7), the creature blocking Mount Nibel (317/21), the Temple keeper (606/31), cloaked figures (700/12, 709/18-19), and Cloud's versions in the memory scenes (280/3, 725/2,3,6, 726/2,4, 727/3,4,5). Native model mapping, visibility, Talk-disabled state and player exclusion still control availability. No unknown-model catch-all was introduced.

`TownInteractionObjectCatalog` adds 65 visible interactions to Objects:

- Sector 7's shop front, sign and television; Shinra library notices; Tifa's desk letter; Kalm's door and locked chest.
- The Gold Saucer museum's ten displays and the previously omitted Turtle's Paradise flyers at the hotel, Cosmo Canyon and Wutai.
- The Cosmo Canyon pub sign; Rocket Town's gun display, bathroom and locked door.
- Wutai's folding screen, levers, publicity notice, sliding and revolving doors, bell, hanging scroll and resting room.
- Icicle's window, Costa del Sol's beach ball, two Corel Prison chests, and 17 Ancient Forest insects, frogs and beehives.

Native LINE rows use live LINON plus the player's collision radius, not a generic stopping distance. Model rows use the live position, visibility, Talk gate and native interaction radius. The bell (587/KANE) polls triangles 138,139,140,86; the scroll (588/JIKU) polls triangles 53,26,54 on the public side. Their authored points are inside native triangle 138 and 53 respectively, with a 12-unit approach radius so Confirm does not stop outside the activation area.

Wutai screen availability is controlled by its own LINE init/main gates; opening it disables those lines. The lever line disables itself during its native sequence, and the sliding doors disable Talk and visibility after opening. Kalm's opened door is removed at temporary bank 5[7] != 0; the Rocket Town backyard lock is offered only after GameMoment 566, matching its internal handler test. The mod does not choose a lever or provide a puzzle solution. Ancient Forest pickup instructions expose the game's OK action; carrying and placing remain native game controls.

## Native verification

Ghidra read-only import of the installed legacy executable inspected TLKON (0x618A80), VISI (0x618A01), LINON (0x6115AD), and LINE activation helpers (0x637ABB, 0x637D35). The shared reader's visibility, Talk flag and strict collision-radius rules follow those handlers. The same reviewed source is compiled into both architectures; this evidence is not a fresh x64 executable decompilation.

`TownInteractionCoverageTests` runs in both runtime hosts, checking required optional actions, line enable/disable, missing player geometry, model visibility/Talk gates, and new NPC labels. With installed data it independently checks entity names, native LINE midpoints and CHAR/Confirm handler presence.

`TownInteractionRouteAudit` replays native gateway/MAPJUMP arrivals through the production planner and script transitions. The 65 targets have a reachable native arrival state in both archives (209 attempts each). Two Ancient Forest beehives require their native player jump onto a puzzle ledge; those are separately reported as `puzzleLandingsOnly`, not claimed reachable on foot from an entrance. A frog hidden at its Init coordinates is checked at its first visible native landing. Eight other cross-component entrance pairs in fields 151 and 525 do not produce static routes; all their targets are reachable from the appropriate component. The report preserves those failed pairs rather than declaring universal reachability. Dynamic IDLCK, live actors, timed puzzle controls and actual player input are outside this static replay.

## Story continuity

The reviewed private `story-continuity-ledger.md` records area-by-area dispositions and evidence limits. This pass adds the Gold Saucer Keystone route from North Corel, the later Highwind deck/takeoff steps, ten restored Nibelheim flashback gateways, and corrected Temple and Lifestream targets. Automatic date, Cait report and final-departure scenes do not acquire false manual objectives. Existing later chapter definitions and preceding native-binding repairs are preserved. A TargetGameMoment already retires a row at its milestone; absent explicit bounds do not imply availability until the credits.

`GreatGlacier.ps1` adds 96 native state-dependent decisions across 26 walkable fields, with exact bank1[184] gates on reused corridors. A bounded interpreter follows the selected native exit handlers to snowfield entrances61..64. All96 chains terminate without cycles;88 ordinary arrival routes are checked with production walking/script traversal. One optional ice-floe arrival is explicitly manual because the islands require puzzle controls. The main selected route avoids that detour. Existing world navigation continues north; three native snowfield mesh starts pass. These checks do not execute every dynamic lock, fatigue/temperature mechanic or live input sequence.

Four runtime/archive combinations passed the702-field inventory and54 explicit progression checkpoints, plus the focused Story/native route suites. Minigame controls and puzzle automation remain outside this pass, and no end-to-end live playthrough is claimed. The conservative inventory keeps three review candidates: two unmodelled Northern Crater battle callbacks called by Cloud after jumps, and a debug/test-field reward actor with no incoming MAPJUMP in the inventory. They are not exposed as invented town NPCs.

## Gold Saucer exploration

`GoldSaucerPlatformExitCatalog` adds all seven Terminal Floor platforms to Exits independently of Story priority. Each entry preserves its two native activation triangles and destination: Ghost 24/25 to 491, Battle 26/27 to 499, Wonder 28/29 to 505, Chocobo 30/31 to 509, Event 32/33 to 484, Speed 34/35 to 486, and Round 36/37 to 488. These are triangle-polled platforms, so they do not incorrectly require an enabled LINE entity. The legacy runtime now accepts the negative trigger-entity sentinel already supported by x64.

`GoldSaucerPlatformExitTests` verifies the actual conditional and MAPJUMP bytes in Cloud's Script 4 and runs seven routes from the native Ropeway Station arrival to the activation triangles in each test host. Distinct labels survive normal exit naming and deduplication. Battle Square's existing visible CHAR/Talk attendants already cover registration; no duplicate NPCs were added.

## Autowalk regressions

The arrival-pause check measured distance along the route to a LINE interaction's reachable approach, but its resume check discarded that guidance and measured to the authored representative point. The same unchanged position could therefore alternate between arrived and resumed. LINE resume checks now retain route guidance; model interactions still use their live positions so a moving actor can leave reach before route replanning completes. The existing reported regression reproduces the LINE failure and passes with the change.

North Corel's alternate-corridor search could find a valid route and then discard it if optional path smoothing exhausted its budget. It now retains the already validated graph edges when shortening runs out of time. Full x64 runs also reproduced an earlier graph-search timeout at about41ms. The last-resort small-room ceiling is now80ms, with the existing640-node and16,384-edge limits. Route selection, native walkmesh tracing, model clearance, executable entry and portal-order checks remain. The native doorway witnesses, blocked-passage cases and controller replays verify the change. The original failing full-run log is retained in the private evidence folder, and witness diagnostics report expanded nodes, elapsed time and GC pause time. This remains a bounded fallback, not a promise that every dynamic room configuration has a route.

Two older regression fixtures were corrected to match native behavior: Kalm leaves the inn after the PHS handover, and the Chocobo Lure is optional; Floor 63's duct uses successive C2/LADER segments, with a dismount between native climb directions. These fixture corrections do not change those gameplay features. Progression-only fixtures now explicitly enable their action lines; separate reader tests exercise disabled lines.

Reader-to-controller checks cover the Wutai bell and scroll and all 17 Ancient Forest objects. A development mistake placed interaction instructions in the manual-traversal field, preventing navigation from starting; the new check reproduced it before those flags were removed. The native approach is navigable when reachable, and pickup, placement and puzzle jumps continue to use ordinary game controls.

## Final verification and deployment

Both full Release test hosts passed with their installed game data and the final shared source. All four runtime/archive combinations passed the702-field inventory,54 explicitly authored native-state checkpoints and the focused Story suites. The regeneration/CI Pester checks passed11/11. The full x64 native doorway witness completed in42.6ms with the final bounded fallback; the old40ms limit rejected this same case. No completed live game run is claimed.

`Build-DualRuntimePackage.ps1` produced the verified dual-runtime package. All846 recorded narration clips and their baseline metadata were checked across source and the three package copies, allowing only text line-ending differences. Both local mod installations received the final files as a development update to0.6.1. The deployment verified4,038 payload files per installation, changed five files in each, preserved user settings and kept backups under the private evidence folder's `deployment-backups`. No GitHub release, tag or version bump was made.

The machine-readable results are `final-verification/results.json`, `full-test-results.json`, `pester-results.json`, `payload-audit.json` and `deployment-report.json` in the private evidence folder. The reviewed `story-continuity-ledger.md` and `glacier-continuity-ledger.md` distinguish tested states from live-play limitations.

## References

- [AbsoluteSteve walkthrough](https://www.supercheats.com/guides/final-fantasy-vii/)
- [Wutai walkthrough](https://www.supercheats.com/guides/final-fantasy-vii/wutai-sidequest)
- [Community field opcode reference](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes.html)
- [Kujata native field data project](https://github.com/dangarfield/kujata-data)
- [Ancient Forest interaction reference](https://www.cavesofnarshe.com/ff7/forest.php)
