# Nibelheim Flashback Completeness Design

## Outcome

Blind Soldier will provide uninterrupted, sighted-equivalent accessibility from the Kalm framing scene through the end of Cloud's Nibelheim flashback. Every point where the original game returns player control will expose the next mandatory Story target, silent visual actions will be described from exact native script events, and the NPC category will identify visible talkable people by their native models instead of guessing from delegated dialogue.

The behavior is shared by the x86 and x64 runtimes. No route, label, or description may depend on Echo-S dialogue or on a translated field name.

## Evidence

The 2026-08-13 x86 trace shows Story becoming empty while control is available in five places:

1. The burning Nibelheim square before Cloud approaches Zangan.
2. The same square after Zangan tells Cloud to inspect the other house.
3. The exterior reactor approach after the fire FMV.
4. `nvmkin1` after Tifa takes Sephiroth's sword and runs deeper inside.
5. `nvmkin21` after Cloud talks to wounded Tifa.

Vanilla field data identifies the exact native progression state and target for each gap. Ghidra confirms that Blind Soldier's live field ID, player model, model table, event table, and entity-to-model array addresses are native runtime state, so target availability and NPC visibility can remain live-state-backed.

Recorded original-game footage also shows five unspoken visual beats that are absent from the current cue catalog: the reactor approach FMV, Tifa kneeling beside her father, Tifa charging Sephiroth, Sephiroth striking Tifa, and Cloud kneeling beside her.

## Story Progression

| Field | Native state | Story target | Native target |
|---|---|---|---|
| 290 `nivl_b1` | GameMoment 376, Bank 3 byte 19 bit 7 clear | Approach Zangan in the burning square | LINE from `(116,733,51)` to `(275,759,51)` |
| 290 `nivl_b1` | GameMoment 376, Bank 3 byte 19 bit 7 set | Enter the only unblocked house and follow Sephiroth | Sephiroth entity 7 at its live model position |
| 316 `mtnvl6b` | GameMoment 376 | Enter the Nibel Reactor | Gateway to field 322 from `(-166,165,325)` to `(-70,160,325)` |
| 322 `nvmkin1` | GameMoment 381, lower walkmesh | Follow Tifa deeper into the reactor | Gateway to field 323 from `(62,-937,191)` to `(-74,-887,191)` |
| 323 `nvmkin21` | GameMoment 383 | Follow Sephiroth into Jenova's chamber | Gateway to field 326 from `(56,-1141,709)` to `(-64,-1141,709)` |

The first burning-square target retires only when the native bit is set. The second disappears through the native field transition. The two `nvmkin1` levels remain separated by the existing walkmesh-triangle gates so Story never routes to a target on the wrong elevation.

## NPC Identity

Fields 273 through 327 become a reviewed label range. Only native model entities that represent visible, talkable people receive labels. Director entities, line proxies, beds, letters, drawers, desks, pianos, boxes, Materia, keys, camera-control entities, and event-only proxies remain absent from NPCs.

The reviewed labels cover:

- Nibelheim residents and visitors: Old man, Innkeeper, Cloud's mother, Photographer, Tifa's father, Zangan, and men in black capes.
- Flashback party and expedition characters when their native models are visible and talk-enabled: Tifa, Barret, Red XIII, Yuffie, Cait Sith, Vincent, Cid, Sephiroth, and Shinra infantrymen.
- Reactor characters: Tifa and Sephiroth.

Visibility, `TLKON`, model identity, and live position remain mandatory. A verified label does not force a hidden or noninteractive model into the category.

This specifically prevents field 290 entity 7 from being called `Cloud`: that Sephiroth model delegates its interaction to young Cloud's script, and the old dialogue-heading fallback followed the delegated speaker instead of the visible model.

## Audio Descriptions

The shared catalog adds the following exact native cues:

| Field | Entity/script/byte | Opcode | Description beat |
|---|---|---|---|
| 101 `blackbg9` | 0/0/15 | `MOVIE` | The view sweeps across Mt. Nibel to the reactor built into the mountainside. |
| 322 `nvmkin1` | 6/0/100 | `CANM!2` | Tifa kneels beside her gravely injured father. |
| 323 `nvmkin21` | 7/3/13 | `REQ` | Tifa charges Sephiroth with his sword. |
| 323 `nvmkin21` | 7/4/0 | `ANIM!1` | Sephiroth slashes Tifa and sends her tumbling down the steps. |
| 323 `nvmkin21` | 5/15/27 | `ANIM!1` | Cloud rushes to Tifa and kneels beside her. |

Each trigger runs only in the corresponding native branch. Descriptions remain one-shot per field entry and continue to yield to readable dialogue through the existing Prism priority queue.

## Compatibility and Failure Policy

- Vanilla field scripts are the canonical implementation target.
- Exact known Echo-S field fingerprints may map a cue only after the corresponding opcode is verified; unknown variants remain silent instead of accepting a broad fallback.
- Story and NPC data are shared by x86 and x64.
- Missing information is treated as a release-blocking accessibility regression.
- No user configuration, 7th Heaven files, FFNx files, or unrelated mods are modified during deployment.

## Verification

Focused tests will first fail against the current catalog, then pass after implementation. They will assert exact state gates, coordinates, trigger lines, arrival behavior, native opcode locations, shared x64 ingress support, correct labels, and suppression of misleading proxies. Full x86, x64, parity, dual-runtime, and whitespace verification follows before deployment.
