# Kalm through Lower Junon audio-description design

## Goal

Give a blind player the visual story information a sighted player receives from the beginning of the Kalm flashback through the party's arrival and first scripted events in Lower Junon. The descriptions must work identically in the legacy x86 and Steam 2026 x64 runtimes.

## Scope

Cover scripted visual events in:

- Kalm and the complete Nibelheim flashback told at the inn.
- The Nibelheim arrival, Mt. Nibel bridge and cave, reactor, Shinra Mansion, town fire, and reactor confrontation portions of the memory.
- Chocobo Farm story scenes and the chocobo dance.
- The marsh aftermath and impaled Midgar Zolom reveal.
- Mythril Mine, including the Turks encounter.
- The first arrival in Lower Junon and Priscilla's introductory story scenes.

Cover both pre-rendered FMVs and in-engine locked-camera scenes. Exclude ordinary player-controlled traversal, routine battles, menu operation, and dialogue whose spoken text already conveys the same information. Fort Condor is optional and outside this release slice unless its story scene is encountered in the selected source footage.

## Description standard

Each cue must communicate only visually available information that matters to story, character action, setting, spatial relationships, or a silent reaction. It must not add strategy advice, hidden state, character motives not shown on screen, or information the sighted player has not yet received.

Descriptions should:

- Name established characters directly.
- Prefer short present-tense sentences that fit natural gaps.
- Describe an action rather than restating the dialogue that accompanies it.
- Preserve important visual reveals, including the scale and condition of the impaled Zolom and Sephiroth framed by Nibelheim's flames.
- Avoid interrupting dialogue when a later safe speech window conveys the same visual information.

## Vidscribe workflow

Use an original Final Fantasy VII no-commentary playthrough as the visual source. Extract only the relevant scripted sequences into clips no longer than five minutes, matching the current Vidscribe individual-plan limit. Keep total submitted footage inside the available monthly allowance.

For every clip:

1. Generate a first-pass audio description with Vidscribe.
2. Capture its timestamped descriptions or transcript from the result page.
3. Inspect representative and transition frames directly.
4. Compare the sequence with the game guide and native field script.
5. Rewrite the usable observations into concise Blind Soldier cues.

Vidscribe output is evidence, not authoritative text. Unsupported names, inferred emotions, redundant dialogue, missed visual beats, and inaccurate timing must be corrected before implementation.

## Runtime architecture

### In-engine scenes

Extend the shared `FieldCutsceneDescriptionCatalog` with a Kalm-through-Lower-Junon catalog section. Each cue is keyed to the native field, entity, script, opcode, and byte index already observed by both runtimes. This keeps playback deterministic and prevents descriptions from firing during ordinary gameplay.

### FMVs

Add a shared field-movie description tracker for FMVs that need more than one visual cue. A native movie-start observation selects a catalog entry and starts its relative timeline. The tracker emits each cue once, stops when movie playback or the owning field ends, and resets safely after skips or torn native reads.

Single-beat or very short FMVs may use one native field-script cue when that preserves the complete visual information. Multi-beat FMVs use the timed tracker rather than one oversized paragraph.

### Speech ownership

Descriptions use the existing Prism speech path and cutscene speech priority. Dialogue remains the primary owner when text is active. Movie narration may use a safe audio-only interval, but must never cause stale dialogue to play after control returns to the player.

### Dual-runtime and Echo S behavior

The catalog and trackers remain shared source compiled into both runtime assemblies. Native vanilla game files and scripts are the canonical source. Existing Echo S compatibility can translate a cue only when its manifest has a verified equivalent; no Echo-derived scene or dialogue becomes canonical data.

## Native cue research

Use the decompiled field script data and the existing Ghidra-backed opcode/address tooling to identify:

- Exact field IDs and source field names.
- The native script entity and script index owning each scene.
- Movie-start and movie-stop opcodes or checked movie-state signals.
- Safe byte indices for in-engine actions immediately before or after dialogue.

Every implemented cue must have both a visual source timestamp and a native trigger identity recorded in the research notes.

## Testing

- Catalog tests assert unique native cue keys and expected coverage for each story segment.
- Tracker tests prove chronological emission, one-shot behavior, skip handling, movie-end reset, field-change reset, and no delayed cue after control returns.
- Shared parity tests require identical cue text and ordering in x86 and x64.
- Focused builds and existing cutscene/dialogue regression suites must remain green.
- Deploy both runtime assemblies without overwriting user configuration, then live-test from a Kalm save through Lower Junon.

## Success criteria

- Every visually meaningful scripted beat in the approved slice has a verified cue or an explicit documented reason for exclusion.
- No description repeats dialogue, gives a gameplay advantage, or fires during ordinary movement.
- FMV cues stop immediately when a movie is skipped.
- The same save produces equivalent narration in both supported game versions.
