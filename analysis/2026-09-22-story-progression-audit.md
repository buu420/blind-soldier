# Story progression and native route audit

The Rocket Town report exposed a gap in the earlier verification: checking authored
rows against native data did not detect required rooms that had no authored row.
The captured first visit was field557 at GameMoment523, while the arrival objective
was attached to field551. The house also had a valid objective whose route failed
with Shera and Palmer standing near the player.

## Evidence

- The new user capture is `ff7_accessibility_steam2026_x64 (8).log`, ending
  2026-09-22 16:14:30Z. It contains manual progress past the failures; it is not
  evidence of a permanent softlock.
- Both installed `flevel.lgp` archives hash to
  `AF695CCF7C681BE222F5F716758160E39469EC02BC0AF3DD6F20A5E9C2A34807`.
  Fresh extraction read 702 fields, including native gateways, opcodes, NPCs,
  LINEs, scripted exits, and in-field traversals.
- Ghidra rechecked legacy LINE, LINON, TLKON, VISI and IDLCK handlers. These
  establish native visibility/activation semantics; they are not a new x64
  executable analysis or evidence of a full playthrough.
- [Absolute Steve's walkthrough](https://www.supercheats.com/guides/final-fantasy-vii)
  provides the chapter order. Native scripts determine field variants, local
  conditions, actor ownership, and which steps are automatic.

## Reviewed connections added outside Rocket Town

`ReviewedStoryTransit.ps1` adds 85 explicit state-bounded rows in 59 fields.
Its companion 203 checkpoints include lower/upper phase boundaries, disabled
objectives outside their phase, native outgoing destinations, and static routes
from independently recorded incoming gateways/MAPJUMPs.

| Chapter or return | Native chain or gate |
| --- | --- |
| Sleeping Forest to the Capital | 619 releases the loop at652;625 gateway1 ->626 gateway0 ->world57; world58 enters630. |
| Forgotten Capital exploration | Side houses and the blue building return to the resting-house route, then the opened-floor route, and later to634's northern departure. |
| Highwind departures | 73 ->74 ->70 after the first party selection; after1199 the same forward line enters72. Pilot Talk is a manual flight interaction. |
| Huge Materia: Corel | Either458 ->459 ->460 or450 ->467 ->464 ->462 ->461 ->460 while15[144] bit0 is clear. The failed-train town variant451 also has an exit after completion. |
| Huge Materia: Condor | Visible entrance NPC,353 ->354 native climb ->355; after success or failure, the native completion bit13[82] bit5 selects the return. |
| Mideel | Native shop/house Go lines return712 during the clinic chapter. |
| Underwater reactor approach | 386 ->360 ->361 ->390;388 returns control after the ride and maps392, which writes1256. |
| Underwater reactor corridors | 393 ->394 ->391/395 ->417 ->418 ->419 ->420 ->421 ->422 ->423 ->424 ->425 ->426 ->427 ->408. Existing switch/battle/boarding steps retain their native conditions. |
| Temple detours | Treasure/service rooms return to the maze or clock. The isolated clock-doorway-V balcony uses604 line18; the main maze must go through606 instead. |
| Bugenhagen return | Lower525 ->529 ->upper525 ->531 Confirm ladder ->540 ->544 ->541 at1389..1390. First-visit Red/party flags do not gate this visit. |
| Key of the Ancients | At1396..1398 the actual key flag selects outward versus inward travel. After the cannon scene,635 ->630 returns to the world. |

The world Story destination also changes at1391, when bugin1a AD Script7 writes
the counter and returns the party to the Highwind. Keeping Cosmo Canyon selected
until1392 repeated the completed handoff. The new target is native location58,
the Capital entrance, rather than routing back through Bone Village.

Two route failures were caught while authoring these connections: the Temple's
isolated balcony, and a direct western-to-eastern Capital gateway inaccessible
from the house arrivals. Both now follow the reachable native components; the
tests retain those arrivals.

## Movement and NPC corrections

The first-visit Rocket Town objective now uses557, matching the side-room scripts'
native return destination. Shops, the inn bedroom and Mount Nibel detours have
phase-bounded return targets. The rocket gantry keeps one climb active through
brief native movement-mode gaps, while a real landing or field change ends it.

In Cid's house, the captured position is blocked from the direct exit route by
Shera and Palmer. The replacement planner first finds an alternate triangle path,
then admits only legs that clear both native model cylinders and wall probes.
Its candidate search is bounded. Independent replay checked all emitted legs at
four bracketed Shera collision widths and verified that the route tracker advances
each required corner in order. Arrival also retains the native vertical constraint
and the controller's effective line reach.

The x64 NPC reader now allows bounded movement between two otherwise matching
actor observations. Entity identity, interaction properties and field/table
ownership still have to agree. The shared player-position reader's exact
fixed-point confirmation contract is unchanged.

## Other chapters and interactions

The existing opening, Junon, Costa/Corel, prison, Cosmo/Gi, Nibelheim, Glacier,
Gaea/Whirlwind, Junon escape, Lifestream, rocket escape, Midgar raid and Northern
Crater suites remain required verification. The native inventory also screens
every authored object and NPC reference. A separate arrival-route screen covered
all previously authored navigable Location rows; in-field landing and traversal
candidates require individual classification, not automatic deletion or broad
fallback objectives.

The three conservative manual-slot candidates remain callbacks/debug content:
765/766 batkun has no visible model and is invoked after the native jumps;
93 blackbg1 is an unentered test field. They must not become fabricated NPCs.
Ordinary NPC discovery continues to require native visibility and Talk eligibility;
moving actors must not cause the entire x64 list to disappear.

The no-arrival-route candidates were checked against native in-field landings.
The Corel train places the party on its first carriage with a scripted JUMP; the
Junon pole is reached by the dolphin sequence. Midgar ducts are separate traversal
stages. The Great Glacier screen enters from the world module and has no incoming
field MAPJUMP. These are limitations of a doorway-only screen, not evidence that
their objectives are absent.

A suspected final-ladder problem in the Midgar wall climb was also rejected after
replaying the production Story reader. The ladder row is already restricted to
the successful swing landing. Constructing a target directly from the catalog
had bypassed that gate; it did not reproduce an incorrect ladder objective offered
to a player. Replaying the actually offered targets then found a separate problem:
isolated return components offered the swinging bar without a route to it. Their
native line02 return leads to224 triangle91, whose left ladder returns to the bar.
The regression requires this connection in Story, rather than accepting an empty
Story list merely because an Exit remains available.

An additional labeling screen invoked the production label resolver for 1,427
native NPC candidates not owned by the object catalog, using the extracted native
model names and dialogue. It resolved 1,039 names; the 388 blank candidates were
then screened for ordinary dialogue-bearing models. The remaining named-model
candidates were debug rooms, player models, scenery, and scripted actors without
ordinary dialogue. This checks label loss, not live visibility at every story
state. The confirmed x64 defect was rejecting an otherwise stable cast whenever
an actor moved between its two samples.

## Verification contract

Run `tools/Test-StoryCoverage.ps1` for both installed archives and both runtimes,
then the full executable regression hosts against the actual packaged DLLs.
Reports must record their actual results before deployment. Preserve user
configuration and room-description history when copying the verified package.

## Final verification and local deployment

Completed on2026-09-22:

- All four runtime/archive combinations passed: x86 and x64 against each installed
  archive. Each run read702 native fields and passed258 checkpoints (55 core,
  203 reviewed transit), plus its Story regression suite.
- The full default x86 and x64 executable test suites passed while bound to the
  actual packaged DLLs. Their SHA-256 identities were checked before deployment.
- All136 definitions emitted by the changed region scripts and wall-climb block
  matched the embedded catalog. The catalog has1340 entries; existing entry order
  was retained rather than accepting regeneration's unrelated reordering.
- The combined checks also corrected the old Rocket Town audit fixture, which
  repeated the wrong551 assumption. It now pins557's independently extracted
  gateway2 line and destination558, and rejects the first-visit row on551.
- The packaged suites retain the short-turn and ladder-flicker behavior checks;
  their older speech/diagnostic expectations were updated for the intended wording
  and settling behavior.
- The tested package was installed into both local mod folders. Each installation
  changed17 files and verified all4,038 payload-file hashes. The complete
  Configuration tree, including room-description history, was unchanged. Replaced
  files were backed up. This is a local build using0.6.4 metadata, not a new public
  release.

Machine-local evidence is in the `story-completion-20260922` investigation folder:
`final-story-checks/results.json`, `packaged-test-results.json`, the two
`packaged-full-*.log` files, `generator-match-report.json`, and
`deployment-report.json`.

These checks establish the recorded states and route witnesses. They do not
execute every field script or reproduce every moving actor, IDLCK change, puzzle
choice, battle return, and controller input in a live playthrough. No claim that
the complete game has been played through is supported by this audit alone.
