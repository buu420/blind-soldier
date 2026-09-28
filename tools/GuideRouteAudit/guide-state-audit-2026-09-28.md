# Guide state audit, 2026-09-28

This covers all 43 chronological walkthrough chapters, checked against the current catalogs and the installed field scripts. It replaces the free-text "Read in full" notes in `guide-chapters.json` as the evidence of what was checked. Those notes are not bound to any catalog row or test.

## What each column means

- **Read**: the chapter body (walkthrough text, treasure and key-item tables) was read end to end. Nothing from the guide is reproduced here.
- **Check**: the result of cross-checking that chapter's required and optional interactions, including revisit branches, against the catalogs and native scripts.
- **Bound**: the number of player-triggerable native interactions in the chapter's fields that have a Story, Object, Exit, native transition or NPC row bound to them.
  - Player-triggerable means Talk, touch, LINE Confirm and walk-over. Party-member entities are excluded.
  - A binding can be by entity, `requiredEnabledLineEntityId`, source entity name, or a `FieldNavigationNpcReader.VerifiedInteractionLines` counter.
  - **A binding is not proof of reachability, live publication or gameplay.**
- **Unbound**: the interactions with no row, grouped by disposition:
  - **covered elsewhere**: the same interaction is reached through a named published entity, such as the other side of a counter, the shopkeeper's own Talk, or another face of the same spot.
  - **automatic**: fires when the leader walks over or into its LINE. These are story scenes, boundary refusals, hazards and remarks on the way.
  - **door**: see the door table below.
- **Nothing player-triggered (Confirm or Talk) is unbound without a named disposition.** Automatic entries with any effect beyond text were read one by one. The walk-over remarks with text only (179 across the game) are classified by rule.
- The per-interaction evidence is private derived data kept with the investigation (`vincent-autowalk-20260928/work`): `fulltrace.json`, `guide-state-review.md` and `.json`, and `manual_pass3.py`. It is not committed, per the README.

| Ch | Read | Check | Bound | Unbound (by disposition) | Doors |
| --- | --- | --- | --- | --- | --- |
| 02 | Read | Checked | 21 | 1 (1 automatic) | - |
| 03 | Read | Checked | 77 | 7 (4 automatic, 3 covered elsewhere) | - |
| 04 | Read | Checked | 71 | 5 (5 automatic) | - |
| 05 | Read | Checked | 48 | 8 (8 automatic) | - |
| 06 | Read | Repaired (Honey Bee room doors); the rest was checked | 95 | 13 (12 automatic, 1 covered elsewhere) | - |
| 07 | Read | Checked | 60 | 7 (7 automatic) | - |
| 08 | Read | Checked | 73 | 8 (8 automatic) | - |
| 09 | Read | Checked | 156 | 21 (19 automatic, 2 covered elsewhere) | - |
| 10 | Read | Checked | 155 | 6 (6 automatic) | - |
| 11 | Read | Checked (static trace) | 26 | 2 (2 automatic) | - |
| 12 | Read | Checked (static trace) | 141 | 18 (18 automatic) | - |
| 13 | Read | Repaired (Costa del Sol materia stall); the rest was checked | 73 | 3 (2 automatic, 1 covered elsewhere) | - |
| 14 | Read | Checked | 63 | 7 (3 covered elsewhere, 4 automatic) | - |
| 15 | Read | Repaired (Wonder Square machines, Battle Square prize windows) | 162 | 10 (10 automatic) | - |
| 16 | Read | Checked (static trace; fields 77-85 re-read after the q.py fix) | 15 | 10 (10 covered elsewhere) | - |
| 17 | Read | Checked (static trace) | 21 | 4 (3 automatic, 1 covered elsewhere) | - |
| 18 | Read | Checked | 70 | 8 (8 automatic) | - |
| 19 | Read | Repaired in code (the coffin doorway Exit by the main coder; the piano by this audit); native replay only | 85 | 3 (3 automatic) | - |
| 20 | Read | Checked (static trace) | 29 | 1 (1 automatic) | - |
| 21 | Read | Checked (static trace); the 558 automatic door is repaired in code by the main coder (controller replay, not live play) | 65 | 5 (2 door, 3 automatic) | 558/e9, 560/e8 |
| 22 | Read | Checked | 98 | 16 (10 covered elsewhere, 6 automatic) | - |
| 23 | Read | Checked (static trace) | 60 | 2 (2 automatic) | - |
| 24 | Read | Checked (static trace) | 39 | 4 (4 automatic) | - |
| 25 | Read | Repaired (Gold Saucer) and checked (static trace) | 119 | 7 (7 automatic) | - |
| 26 | Read | Checked | 61 | 8 (7 automatic, 1 covered elsewhere) | - |
| 27 | Read | Checked; the shell-house rest labels are repaired (636) | 61 | 6 (1 covered elsewhere, 5 automatic) | - |
| 28 | Read | Checked | 63 | 7 (6 covered elsewhere, 1 automatic) | - |
| 29 | Read | Checked (earlier work in this session) | 159 | 3 (3 covered elsewhere) | - |
| 30 | Read | Checked (static trace) | 69 | 6 (4 automatic, 2 covered elsewhere) | - |
| 31 | Read | Checked (static trace) | 18 | 5 (5 automatic) | - |
| 32 | Read | Checked (static trace) | 42 | 8 (8 automatic) | - |
| 33 | Read | Repaired (piano); 539 unchanged | 183 | 12 (12 automatic) | - |
| 34 | Read | Repaired (back door, key spot) | 42 | 3 (3 covered elsewhere) | - |
| 35 | Read | Checked (static trace) | 72 | 5 (5 automatic) | - |
| 36 | Read | Repaired (piano, Gold Saucer) | 216 | 17 (3 covered elsewhere, 14 automatic) | - |
| 37 | Read | Checked (static trace) | 54 | 8 (8 automatic) | - |
| 38 | Read | Checked (static trace; fields 81 and 88-91 re-read after the q.py fix) | 24 | 9 (1 covered elsewhere, 8 automatic) | - |
| 39 | Read | Repaired (control panel) | 66 | 5 (2 door, 3 automatic) | 558/e9, 560/e8 |
| 40 | Read | Checked (static trace); the shell-house rest labels are repaired (636) | 33 | 6 (1 covered elsewhere, 5 automatic) | - |
| 41 | Read | Checked | 208 | 26 (24 automatic, 2 covered elsewhere) | - |
| 42 | Read | Checked (static trace). The 747 line7/line17 switches are bound to native transitions | 132 | 5 (5 automatic) | - |
| 43 | Read | Repaired in part (Battle Square prize windows) | 146 | 18 (8 automatic, 10 covered elsewhere) | - |
| 44 | Read | Checked (static trace) | 64 | 1 (1 automatic) | - |

## Doors released by their own LINE

| Field | Status |
| --- | --- |
| 558 | Automatic; walking into door1 unlocks triangles 102 and 106 together. **Repaired in code** by the runtime coder (grouped door-opening row, `CidHouseDoorApproachTests`); root fixed the secondary-lock lookup. Controller replay only, from root's entry position; there is no matched user log and no live verification. |
| 560 | Automatic one-way door. **No current defect**: the closet behind lock 45 has no published target (root's rocket-door final review). The direction mapping (temp 5[6] against the leader's direction) is a residual assumption, not a confirmed gap. |
| 333, 238, 245, 578, 582, 589, 653 | Need Confirm; absence from the automatic-opening table is not a defect. |
| 207 | A story door that opens on walking once the story bit is set; the Story rows route through it. |

## Vincent (303)

- The coffin-room doorway is now an Exit, gated by the live lock on triangle 34 (`FieldRoomDoorExitCatalog`, runtime coder).
- Focused native replays pass on both architectures. That is native replay, not live play.
- The two "Enter the basement library" entries remain a confirmed duplicate label. That one of them is unreachable is unproven.

## Innkeeper (273)

- Root reproduced the hold, and the runtime coder repaired it. There were two causes:
  - the interaction approach missed the thin legal band behind the counter (`TryCreateReachBandInteractionApproach`);
  - arrival in that band was nearly impossible while running (`IsInsideTalkReachOnTheApproach`).
- The regression is `InnkeeperCounterApproachTests`: talk reach 110 at scale 512, red before the fix and green after.
- The search now shares a bounded amount of work across triangles and reads the leader's radius even without other models. Automatic arrival checks facing; root also repaired the exact-waypoint branch and the strict native 64-unit angular boundary, using 00636284 and the direction copy in 006392BB.

## Automatic lines checked against their routes (517, 88, 91)

Each of these one-time scenes or comments runs from its LINE's Move slot, and needs no Confirm:
- In the installed pointer tables (both archives), 517 line1 shares slots 1 and 2.
- 88 e3 and 91 line1 share slots 1, 2 and 3.
- The Move slot runs while the leader is within the collision radius. That radius is 30 in all three fields (scale 512).

The static analysis uses a 4-unit lattice over the installed walkmesh:
- The lattice keeps the catalog transitions and excludes triangles closed by permanent Init locks.
- No bypass outside the line's 30-unit activation radius was found for the targets below from the relevant native arrivals. This finite-grid result supports coverage; it is not a continuous-geometry proof or a live movement replay.

| Line (gate) | Way in | Targets with no bypass in the lattice analysis |
| --- | --- | --- |
| 517 gnmk line1, reactor scene (3[129] bit 4, moment < 604) | 516 gateway | The Titan materia (line2 Object) |
| 88 qa e3, Gelnika comment (3[135] bit 0) | Submarine, then the ladder | Every interior target: the item, the save point, and the ways to 89 and 90 (which are reached only through 88) |
| 91 qd line1, Gelnika comment (3[135] bit 3) | From 89 | tre1, tre4 and the materia. tre2 and tre3 do not pass the line, which matches what a sighted player who only visits those would see. |

Nothing was added:
- The static analysis found no missed independently accessible scene among these three.
- The 88 result depends on the native radius of 30; a path of radius 3 could round the segment's ends.

## Repairs made by this audit

**Objects** (Line targets touched inside the leader's radius, with the LINE's live enable state respected):
- **287** Tifa's piano: every visit.
- **287** Drawer in Tifa's room: the playable flashback only.
- **507** Nine Wonder Square machines, gated at moments 445, 790 and 1299.
- **500** Two Battle Square prize windows.
- **566** Rocket control panel.
- **717** Mideel weapon-shop back door, and **712** the key spot (entity 18 plays its clink sound).
- **218** Four Honey Bee Inn room doors.
- **432** and **443** Two shop counters that only their LINE opens.
- **531** Sealed door (from moment 514).
- **699** Healing spot.
- **633** and **636** Forgotten City spots where Cloud hears the Ancients.
- **78** The spot in the sleeping man's cave.

**NPC counter proxies** (`VerifiedInteractionLines`), for people whose own Talk is off or empty:
- **178** the man and the child;
- **443** the tourist guide;
- **507** the bike rider.

**Story:** in **636**, the four generated shell-house rest rows are relabelled to the bed/rest action. Their conditions are unchanged.

**Not a proxy:** **148**'s oyaji2 already reaches across his counter (TalkRange 200), so his counter LINE is not repeated.

## What is and is not proven

- **Proven statically, on both archives:**
  - native LINE geometry and state gates (byte anchors);
  - Object and NPC reader publication by moment, flag, LINE enable, visibility and leader radius;
  - static route connectivity from every native way into each field to every repaired target, reader-published, 82 approaches with no failures;
  - the 517 reactor scene and the 88 and 91 Gelnika comment lines separate the relevant arrivals and targets in the tested lattice at the native radius.
- **Not proven:**
  - live autowalk, body clearance, and live boundary or model state for these targets;
  - that the Mideel counters' facing requirement is met on arrival;
  - camera parity for field 539;
  - the live effect of the 273 innkeeper repair;
  - that one of the two 303 library exits is unreachable;
  - the backgrounds behind the neutral "Something to examine" labels, which were not viewed.
