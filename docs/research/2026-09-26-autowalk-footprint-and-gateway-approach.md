# Auto walk: world ground footprint, and field gateway approaches (2026-09-26)

A tester's legacy log from 2026-09-26 showed three kinds of failure:
- **On foot:** auto walk stalled and turned back and forth short of Old Man's House (Mythril).
- **Buggy:** the Buggy stalled three times.
- **Field:** under Junon (field 428), auto walk slid along a wall beside the gateway to field 7 and never crossed it.

## World map

### Ground footprint
The game moves the party on foot, and the Buggy, only where all five contact points are on ground that model may enter. FUN_00751EFC calls 007530B3 with the centre and the points 200 units along each axis (350 for the boat).

The house door is a slot in that footprint about 30 units wide. The mountain faces on each side leave only one row of positions where the party fits.

The old step check looked only at the centre line. It kept pressing a key whose next frame the game refuses, for five seconds, and then gave up.

### What auto walk now does
- **Footprint preference:** on foot, and in the Buggy, it prefers a key whose next native frame fits the footprint.
  - The frame is FUN_0074EA48's own integer vector: 0x1E on foot, 0x2D for the Buggy. Each diagonal axis is `(axis*3)>>2`, turned by the negative camera angle in 4096ths.
  - This is a preference, not a refusal. Where no key fits and closes, the old choice stands.
  - It is never applied on the way to a vehicle, where the refused step into the vehicle's mask is how boarding is recorded. It is also not applied on the bridges.
- **Refused keys:** a key that did not move the party for two samples is not pressed again from that spot.
- **Slot walk:** when the plain aim steps back to an earlier corner, or has no legal key, auto walk plans a short walk and commits to it.
  - The walk is built from the steps the game would accept, each held for as many native frames as the host sample has been taking.
  - It is scored by how much of the route is left, measured in plan view.
  - This replaces the corner-to-corner back-and-forth.
- **Buggy routes** are now planned with the same footprint as walking routes.

### Entry and ownership
- **Old Man's House:** handler A584 enters field 9 only for models 0, 1 and 2. The Buggy on its trigger is now told the way in is on foot, and does not hear "Arrived".
- **Movement ownership:** read on its own, from the control flag DE6B5C and the main-menu session DC12F0.
  - A torn window no longer hides it.
  - The party menu holds auto walk, and menu time is not counted as a stall.

## Field gateways
The route to a gateway used to end at the point of its line nearest the party. For field 428 that point is the line's endpoint, 3 units from a closed edge, where no body of the leader's native radius can stand. The native initial radius is 30 × scale / 512, which is 30 in this field.

Now:
- **Body-clear approach:** when that point leaves no room, the approach moves to the nearest point of the same real line that does leave room.
- **Narrow doorways:** where the doorway is narrower than the body, it moves to the roomiest point of the line.
- **Boundary lines:** a line lying on the walkmesh boundary keeps its old point.
- **Radius source:** the leader's live +0x72, read directly.
- **Unchanged:** arrival is still the real crossing of the line.

**Not claimed:** the field 370 mid-route stalls at −2616,−38 are not reproduced by the replay, and are left open.
