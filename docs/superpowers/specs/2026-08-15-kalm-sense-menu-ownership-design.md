# Kalm, Sense, and Menu Ownership Design

## Goal

Restore sighted-equivalent information in three places without adding gameplay advantages:

1. Kalm exposes the correct storefronts, useful building names, one logical world-map exit, and every real treasure.
2. Sense speaks the complete native result as one uninterrupted utterance: target, level, HP, MP, and any native weakness line.
3. Main-menu speech cannot leak stale selections into battle results or shop screens.

Both the Reloaded-II x86 runtime and the Steam 2026 x64 runtime must use the same shared interpretation wherever the game data is equivalent.

## Evidence

### Kalm

Kujata field data and native field scripts identify Kalm town as field 335. Its two doors that both enter field 328 are distinct storefronts: gateway 2 is the Materia Store and gateway 3 is the Weapon Store. Field 328's return gateways preserve the same distinction. The two gateways to field 2 are adjoining line segments of one world-map opening, not two player-facing exits.

The guide's six ordinary Kalm treasures already exist in the generated catalog: Megalixir, three Ethers, Guard Source, and Peacemaker. The repair must verify and preserve those records instead of creating duplicates.

### Sense

Ghidra confirms that the native Sense presentation is:

- target and level;
- current/max HP and MP;
- a weakness line when applicable;
- or the native failure message.

The current decoder misclassifies control bytes `0xEF` and `0xF0` and advances one byte too far after three-byte controls, which removes separators from HP/MP text. The current tracker also speaks the individual Sense lines separately, allowing later Prism speech to interrupt the level line. Native enemy level and elemental rates are available from the scene enemy record, while the persistent sensed flag is bit `0x40` in the parallel actor record.

### Menu speech ownership

Runtime logs show the legacy main-menu reader speaking stale `Magic` or `Save` values while the current module is battle results, and stale `Save` while the exact shop reader owns the Kalm shop. The raw main-menu globals remain plausible after ownership changes, so timing or deduplication cannot solve this. Module and exact shop ownership must gate the reader.

## Architecture

### 1. Kalm exit presentation policy

Use stable field/gateway IDs for semantic naming. Do not infer the two shared-interior storefronts from destination names alone.

Add a shared exit-presentation layer after native exit discovery and before reachability/navigation presentation. It will:

- relabel the known Kalm stable IDs;
- collapse only `gateway:335:9:2` and `gateway:335:10:2` into one `Leave Kalm for the World Map` target;
- hide that world-map target until Kalm's native completion bit is set;
- leave other same-destination gateways independent;
- preserve a navigable position for the one logical exit;
- run identically in both runtimes.

The user-facing Kalm town labels are:

| Stable ID | Label |
| --- | --- |
| `gateway:335:0:329` | `Enter Item Store` |
| `gateway:335:1:330` | `Enter Bar` |
| `gateway:335:2:328` | `Enter Materia Store` |
| `gateway:335:3:328` | `Enter Weapon Store` |
| `gateway:335:4:341` | `Enter Kalm Traveler's house` |
| `gateway:335:5:338` | `Enter house with rear tower` |
| `gateway:335:6:336` | `Enter west house` |
| `gateway:335:7:333` | `Enter house beside the inn` |
| `gateway:335:8:331` | `Enter Kalm Inn` |
| `gateway:335:9:2` and `gateway:335:10:2` | one `Leave Kalm for the World Map` |

Interior labels distinguish returning from the Materia Store and Weapon Store and name the rear-tower transitions.

The treasure catalog receives regression coverage for all six real pickups, their collection flags, and runtime publication. If an existing line target is proven unavailable while its treasure is collectible, convert only that record to a static location while retaining its collected-state gate.

### 2. Native Sense result and atomic speech

Correct the shared battle runtime text decoder:

- `0xEF` resolves the native target ID/letter;
- `0xF0` resolves the localized element name;
- control tokens consume exactly their encoded byte length so punctuation remains intact.

Extend the shared native battle reader with a coherent, privacy-preserving Sense result snapshot. Enemy level and weakness data are read from the actor's scene enemy record. Values remain unavailable until the native sensed bit proves the player has earned access.

Introduce a small Sense speech coordinator around the battle-message path. When the native Sense header begins, it produces one utterance in visual order, for example:

`Guard Hound. Level 3. HP 42 of 42. MP 0 of 0. Weak against Fire.`

It suppresses only the immediately following native fragments belonging to that same Sense sequence. `Couldn't sense.` remains available. Ordinary target help after Sense remains the sighted-equivalent name and HP information rather than repeating the full Sense panel.

No English string parsing is used to discover game state. Typed decoder results and native actor identity own the sequence.

### 3. Exact main-menu ownership

For x86, poll the legacy main-menu reader only when:

- current module is 5;
- exact shop ownership is coherently readable and false;
- no stronger save-menu owner is active.

For x64, pass the lifecycle module into the observation pump's main-menu read and apply the same exact shop exclusion. On ownership loss or incoherent ownership, return a closed update and reset the main-menu dedupe state. Reacquiring the real menu must speak its current selection again.

Battle results remain owned by the results reader, and shops remain owned by the exact shop reader.

## Failure and Safety Behavior

- Ambiguous or incoherent native reads fail closed; they do not guess or expose hidden enemy information.
- Kalm-specific filtering applies only to the documented stable IDs.
- A target is never marked collected or unavailable merely to reduce clutter.
- Speech ownership changes reset stale reader state so a valid later screen is announced normally.

## Verification

Automated coverage must prove:

- exact Kalm storefront labels and one gated world-map exit in both runtimes;
- all six Kalm treasures exist exactly once and publish under their legitimate conditions;
- Sense controls preserve punctuation and localize element names;
- an unsensed enemy cannot leak level, MP, or weakness;
- a successful Sense produces one complete utterance, and failure still speaks;
- stale main-menu bytes are silent during module 17 and exact shop ownership;
- returning to the real menu speaks again;
- both runtime suites and the shared suite pass.

After builds pass, deploy both runtime outputs into the local game installation for live testing. Publishing a public release is outside this change unless separately requested.
