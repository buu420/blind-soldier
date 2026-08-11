# World-map combat resume and story catalog design

## Goal

Keep an active world-map route intact through the complete native battle lifecycle, and expose the current main-story overworld destination from Kalm through the Northern Crater in both supported runtimes.

## Evidence

- The live legacy log enters module `0x17`, then battle module `2`, then post-battle module `0x11`, before returning to world module `3`.
- Ghidra decompilation of the legacy executable confirms the battle-completion path writes `0x11` to the current-module global. The shared lifecycle classifier currently preserves routes only for `0x17` and `2`, so it destroys the route during `0x11`.
- The native Game Moment table confirms the main progression milestones. The current catalog only maps Game Moment 341 and later to Kalm, so Kalm remains the sole Story target forever.

## Design

### Combat lifecycle

Treat `0x11` as a combat interruption alongside `0x17` and `2`. Both runtime hosts already delegate to the shared classifier, so this change preserves the route and progress control without runtime-specific behavior. Ordinary field module `1` and quit module `0x13` remain permanent world-map exits.

### Story progression

Store the overworld progression as ordered inclusive Game Moment ranges. Each range references native world-map entrance labels already loaded by the catalog. Returned copies use the Story category and stable `world-story:` identifiers.

Most stages have one destination. A few game segments intentionally share one Game Moment across multiple consecutive overworld stops. For those stages, expose the ordered candidates and let the existing native terrain planner hide entrances the current player model and connected region cannot reach. This avoids fabricated save flags while still allowing J/L target selection when two valid objectives are genuinely available.

Dynamic story objectives remain native-state backed:

- Game Moment 1396: expose the live Key of the Ancients entity while underwater.
- Game Moments 1400 through 1569: expose the live Diamond Weapon entity.

Optional destinations such as Gongaga and Wutai remain in Locations rather than being presented as mandatory Story objectives.

## Progression baseline

| Game Moment | Story destination |
| --- | --- |
| 341-384 | Kalm |
| 385-386 | Chocobo Farm; Mythril Mine, Midgar side |
| 387-414 | Junon |
| 415-426 | Mt. Corel |
| 427-468 | North Corel |
| 469-522 | Cosmo Canyon |
| 523-534 | Nibelheim; Mt. Nibel; Rocket Town |
| 535-565 | Rocket Town |
| 566-582 | North Corel |
| 583-637 | Temple of the Ancients |
| 638-676 | Bone Village |
| 677-769 | Icicle Inn |
| 1033-1099 | Mideel |
| 1110-1115 | North Corel; Fort Condor |
| 1116-1117 | Fort Condor; Mideel |
| 1118-1198 | Mideel |
| 1199-1298 | Junon |
| 1299-1307 | Rocket Town |
| 1389-1391 | Cosmo Canyon |
| 1392-1395 | Bone Village / City of the Ancients approach |
| 1396 | live Key of the Ancients |
| 1397-1399 | Bone Village / City of the Ancients approach |
| 1400-1569 | live Diamond Weapon |
| 1570-1597 | Midgar |
| 1620-1997 | Northern Crater |

Ranges without free overworld control deliberately return no Story target.

## Verification

- Regression-test `0x17 -> 2 -> 0x11 -> 3` preservation and reject ordinary field/quit modules.
- Table-test representative Game Moments across all discs, including empty gaps.
- Verify every static story label resolves to a native entrance.
- Verify dynamic Key and Diamond targets appear only from matching live entities and progression windows.
- Run the shared test harness, both runtime builds, and deploy the resulting package to the installed mod copies.
