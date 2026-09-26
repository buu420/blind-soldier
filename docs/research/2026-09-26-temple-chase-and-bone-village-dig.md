# Temple of the Ancients doorway chase and Bone Village dig

A tester reached two rooms that offered almost nothing to navigate to. In the Temple's doorway
chase (field 610, kuro_7) the only Exits were the way back to the clock room and the locked
doorway, although the room has nine doorways on three floors. In the Bone Village dig (field
772, bonevil2) every navigation category was empty, and the readout repeated "5 of 5 diggers
placed" before any digger had been sent.

## The doorway chase (610)

### Doors, ledges and floors
- **Upper floor (about z 370):** one door, the ledge down to the middle floor, the locked doorway out, and the doorway from the clock room.
- **Middle floor (about z 0):** four doors and the ledge down to the lower floor.
- **Lower floor (about z −390):** four doors.
- **Numbering:** doors are numbered from the left on each floor. The camera's x axis is the field's x, so field x order is screen left to right.
- **Exits:** every door and both ledges are now Exits. Routes use the field's own door and jump traversals, so a door on another floor can be reached through the doors that lead there.

### How a door works (native scripts)
Crossing a door's LINE (border1..border9, entities 6..14) runs one of Cloud's scripts 6..14.

**Doors in pairs.** Each door leads to one other door's mouth:

| Door (entity) | Comes out at |
|---|---|
| Upper floor door (6) | Middle floor door 3 |
| Middle floor door 1 (7) | Lower floor door 2 |
| Middle floor door 2 (8) | Lower floor door 4 |
| Middle floor door 3 (9) | Middle floor door 1 |
| Middle floor door 4 (10) | Lower floor door 1 |
| Lower floor door 1 (11) | Middle floor door 2 |
| Lower floor door 2 (12) | Lower floor door 3 |
| Lower floor door 3 (13) | Middle floor door 4 |
| Lower floor door 4 (14) | Upper floor door |

**What happens on crossing:**
- **Guard's door:** if the guard is about to come out of the door the party entered, he is caught there. The game shows "Door Unlocked!!" and the locked doorway opens.
- **Any other door:** the guard's run plays, and then the party comes out of the paired door's mouth.
- **Ledges:** these wait for OK and only go down a floor.

### What is said
- **Automatically, when it happens:**
  - "The guard came out of lower floor door 4."
  - "The guard dropped to the lower floor."
  - "The guard went into the upper floor door."
  - "You came out of middle floor door 3."
  - On a ledge, said once as you step on: "At the ledge. Press Confirm to jump down to the middle floor." A ledge is optional, so it never holds navigation. You can pick another target and walk on.
- **Never said:** where the guard will come out next. The game keeps that in Bank[5][18]; it is the puzzle, and it is not read. The game's own hint is to memorise the doors he enters and exits.
- **Repeat key:** R says your floor and door, and the guard's floor and door when he is in sight.

### The routing fix
- **Symptom:** in this field, a route to a middle-floor door could end on the upper floor directly above it.
- **Cause:** the planner took the upper triangle whenever the approach point fell on the edge of its own triangle.
- **Fix:** a route to a trigger line must now end within the game's own vertical reach of that line. This applies everywhere.

## The Bone Village dig (772)

### Phases
The phase is the field's own Bank[5][12]:
- **Placing diggers (7 down to 3):**
  - Stand where you want a digger and press Switch.
  - Choose "Order a search (100 gil)", or "Done" to go to the blast.
  - Each order sends the next digger to where you stand, climbing the ladder if needed.
  - If you cannot pay, that digger's turn is used up anyway.
- **The blast (2):** press Switch. Every digger turns to face the buried item.
- **The dig point (1):** stand where the diggers' lines of sight meet and press Switch.
- **The dig:** the scene plays out, and the result waits in the village's treasure box overnight.

### When a digger counts as placed
- **How the game sends one:** in phase p (7 down to 3), the foreman's script sends digger entity 21 − p (digger 1 at phase 7, digger 5 at phase 3). It runs the digger's own script with `entityExecuteSync`, which only returns once he has walked there, climbed the ladder if needed, jumped to where the party stood and waited 20 frames. Only then does it take the phase down.
- **Placed means both:**
  - the phase is below the one that sends him, so the game has finished sending him;
  - and he is standing, stopped in x, y and height, away from the waiting spot.
- **Not a count from the phase:** the phase alone can't say how many are placed. With too little gil the phase goes down and nobody moves, and choosing Done jumps straight to 2. The phase only says which digger may still be on his way.
- **Repeat key:** while the game is sending a digger, R says "Digger 1 is on his way." and does not count him as placed.
- **Navigation targets differ:** the Objects list gives a digger at his live position as soon as he leaves the waiting spot, because that is where a sighted player sees him.

### Navigation targets (both levels)
- **The ladder:** both ends, "Ladder down to the lower level; press OK" and "Ladder up to the upper level; press OK".
- **Placed diggers:** each digger actually placed, "Digger 1" to "Digger 5" in the order they are sent, at his live position.
- **Waiting diggers:** a digger still standing at the spot where diggers wait is not a target.

### What is said
- **Automatically:**
  - "Digger 2 placed.", said once the game has finished sending him and he has stopped. It is not said while he walks, climbs the ladder or pauses on the way. A digger the reader briefly loses is not announced again.
  - On a ladder, said once as you step on: "At the ladder. Press Confirm to climb down to the lower level." (or up). Like the chase ledges, it never holds navigation.
  - "Blast next." when the game moves to the blast.
  - "The diggers have turned towards the dig point." after it.
  - "The dig is under way." once you have chosen.
  - After the blast, walking onto or off a digger's line of sight: "In line with diggers 1 and 2." or "Out of line with digger 1."
  - The game's own windows already give the controls, so nothing is added on arrival, and no step of the dig holds navigation.
- **Repeat key:** R gives the phase and its control, and each placed digger with his direction, distance and level. It also says which level you are on.
  - After the blast it also gives which way each digger faces and where his line of sight passes you, for example "his line of sight passes 100 ahead of you". This is worked out only from where he stands and faces.
  - R is available while the game's instruction windows are open.
- **Targets:** the ladders and placed diggers are ordinary targets, so navigation and auto walk go to them, through the ladder when a digger is on the other level. The ladder's own OK is left to you.
- **Never read or announced:** the buried item's models (luna and box0..box6) and which item the foreman was asked for.

## Limits
- **Waiting spot:** a digger deliberately sent to the exact spot where diggers wait cannot be told from one who is waiting. The field keeps no record of who was hired, only where each model stands.
- **Button name:** the dig's control is named "Switch", as the game names it; the mod does not know which key it is bound to.
- **No Story step before the catch:** the chase has no Story step until the guard is caught. The game's own dialogue explains the rule, and the doors are in Exits.
- **Not played live:** neither room has been played through with this build. The tests replay the native scripts, the installed walkmesh and archives, and both runtimes' readers.

## Read recovery regression

The final review reproduced a false "Out of line" cue when one worker could not be read, followed by a false "In line" cue on recovery without player movement. Sightline tracking now retains its last coherent observation until the phase and workers can be read again. A shared regression exercises failed worker and phase reads, recovery, and subsequent real movement. The new case failed before the correction and passed afterward; it is included in both runtime suites.
