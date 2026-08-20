# Midgar Zolom accessibility design

## Goal

Keep world-map footsteps audible while the player crosses the Midgar Zolom marsh, and announce the same crossing opportunity a sighted player gets by watching the Zolom move to the far side.

## Evidence

- The live x64 log records normal player movement on native world terrain 7, followed by `World-map footstep suppressed: no explicit Cosmo mapping for model 0, terrain 7.` The movement tracker does not stop; only sound selection fails.
- The shipped Cosmo configuration has world-map mappings for the other traversable surfaces but omits terrain 7. Its shallow-water sequence is `5130` through `5135`, which is the appropriate existing game-derived surface sound for the marsh.
- Ghidra analysis of the stock x86 executable identifies the native Zolom enable flag at `0x00E29F40`, the active position-record pointer at `0x00E2A18C`, and its history ring at `0x00E29F80` through `0x00E2A100` with eight-byte records. The record stores relative X and Z; world coordinates add `0x34000` and `0x20000` respectively.
- The native initializer places the Zolom at one of two far-side anchors based on the player's approach side: `(221192, 156472)` when player Z is below `0x23A98`, otherwise `(220492, 135672)`. This matches the visible strategy of waiting at the marsh edge until the Zolom reaches the far side.
- The native collision path starts the encounter only when the Zolom gets close to the player. Blind Soldier will not predict or suppress that collision; it will only expose the visible far-side window.

## Design

### Marsh footsteps

Add exact `wm_footsteps_{model}_7_159` entries for the walking and Chocobo world models already supported by Blind Soldier. Reuse Cosmo's existing shallow-water sequence `5130` through `5135`. The current post-collision movement tracker and cadence remain unchanged.

### Native Zolom observation

Add a shared `MidgarZolomStateReader` over `ILegacyAddressSpace`. It will:

- require world module 3 and overworld map 0;
- require the native Zolom enable flag;
- validate the active record pointer is aligned and inside the native history ring;
- translate the current relative X/Z record into world coordinates;
- double-read the complete frame and reject torn or unreadable state.

Both x86 and x64 hosts use this same legacy-layout reader, with x64 reading through the existing translated guest address space.

### Shoreline and notification policy

Use the already loaded native world mesh to establish that the player is on the edge of terrain 7: either the current triangle is non-marsh and borders marsh, or it is marsh and borders non-marsh. This prevents a late announcement after the player is deep in the swamp.

For an on-foot player at the shoreline, choose the native far-side anchor from the player's Z coordinate. When the Zolom enters a conservative Manhattan-distance radius around that anchor, speak once:

`Midgar Zolom is at the far side. Run now.`

The tracker rearms only after the Zolom leaves the window. It resets on unreadable state, map/module changes, foreground loss, battle, or runtime suspension. It remains silent while mounted on a Chocobo because the requested run timing is not needed there.

## Accessibility boundary

The notification communicates a visible position and present opportunity. It does not guarantee success, reveal hidden collision calculations, automate movement, alter the enemy, or provide advance information unavailable to a sighted player.

## Verification

- Exercise the shipped Cosmo file through the real parser and sequencer for terrain 7.
- Unit-test valid, invalid, misaligned, and torn native Zolom reads.
- Unit-test both approach anchors, shoreline gating, one-shot behavior, rearming, and mounted suppression.
- Run the focused world-map suite, complete x86 and x64 suites, and release builds.
- Deploy both runtime outputs to the installed mod without changing user configuration or other mods.
