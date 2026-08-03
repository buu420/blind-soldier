# Echo-S Compatibility Design

## Goal

Make the FFVII accessibility mod coexist natively with an enabled Echo-S
installation. Echo-S owns dialogue that it actually voices. Prism remains the
fallback for unvoiced dialogue and always owns accessibility-only information,
including menus, choices, navigation, battle state, the Echo-S disclaimer, and
audio descriptions.

The vanilla game and games without Echo-S must retain their current behavior.

## Verified incompatibilities

The active Echo-S 1.24 package inserts field `blackbgh` (field id 109) between
the opening field and the opening movie. The current opening probe treats that
temporary field as abandonment of the opening and stops before the movie
starts. The disclaimer's four native `MESSAGE` operations also appear before a
stable visible-window snapshot is available, so the polling reader does not
speak them.

Echo-S replaces much of `flevel.lgp` to add voice and auto-text behavior. These
changes preserve story actions but move their script byte offsets. The current
audio-description catalog deliberately matches field, entity, script, byte
offset, and opcode exactly, so vanilla cue keys do not match Echo-S's modified
scripts. Nearby-offset matching is not acceptable because a description at the
wrong story action is worse than silence.

## Compatibility identity

`EchoSCompatibilityDetector` recognizes a supported Echo-S installation from
the loaded field-script content, not merely from the presence of an installed
IRO. The supported compatibility manifest records the relevant field name,
script fingerprint, Echo-S version label, and exact alternate cue keys.

Detection is automatic and session scoped. A matching disclaimer field can
establish Echo-S startup mode immediately. Exact per-field script fingerprints
validate description mappings as those fields load. Unknown or changed scripts
do not receive guessed alternate cue locations; vanilla keys and unrelated
accessibility features remain operational.

## Disclaimer and opening movie

The opening probe lifetime recognizes this supported sequence:

1. opening field 116;
2. Echo-S disclaimer field 109;
3. temporary non-field/loading modules;
4. return to opening field 116;
5. opening movie starts and later closes.

Only a validated Echo-S disclaimer transition is exempt from the existing
direct-field-abandonment rule. An ordinary move from field 116 to another field
still stops probing.

Each disclaimer `MESSAGE` is resolved from the currently loaded field's native
dialog table by field id and message id, normalized through the existing FFVII
text decoder, and queued through the native field speech ownership path. The
implementation does not hardcode guessed disclaimer wording. Each page speaks
once, and normal input advances the native screen unchanged.

## Audio-description cues

The canonical description text remains single-sourced. A compatibility
manifest may attach one or more exact keys to the same logical cue:

- the existing vanilla key;
- a verified Echo-S 1.24 key;
- future keys only after their scripts are independently aligned and checked.

An offline alignment utility compares decoded script structure around each
canonical cue and emits candidate alternate keys. Candidates are accepted into
the manifest only when field, entity, script, opcode, and surrounding opcode
signature identify one unambiguous action. Runtime matching remains exact; it
does not search nearby offsets.

Opening-movie narration uses the existing described audio track. Keeping the
probe alive across the disclaimer restores its normal start and stop behavior.
Field descriptions use the exact compatibility keys and retain their current
once-per-field-entry semantics.

## Echo-S dialogue ownership

`EchoSDialoguePolicy` decides whether a native dialogue observation should be
spoken by Prism. It suppresses Prism only when all of the following are true:

- Echo-S compatibility mode is validated;
- Echo-S voice playback is enabled for the current installation;
- the current field/message has a matching Echo-S voice asset or a verified
  runtime voice event;
- the observation is ordinary dialogue, not a choice, menu, disclaimer, battle
  message, navigation cue, or audio description.

If any condition is unknown or false, Prism speaks the line. This fail-open
speech policy prevents missing information while avoiding duplicate narration
for known voiced lines. Choices remain spoken even when their surrounding
dialogue is voiced.

Description speech retains priority over queued ordinary dialogue. When an
Echo-S voice line is active at a description cue, the description is held for
the first bounded safe gap; it is not discarded. A stale description is never
carried into unrelated gameplay.

## Runtime and data boundaries

- Support both the Reloaded x86 and x64 launch paths used by the dual-runtime
  installation.
- Do not modify Echo-S, its IRO, FFNx, or 7th Heaven profile data.
- Do not disable Echo-S auto-text or voice settings.
- Do not use OCR, input-based guessing, or loose script offsets.
- Log compatibility activation, field fingerprint rejection, voice ownership,
  disclaimer fallback, and description-key selection with bounded diagnostics.

## Failure handling

Unsupported Echo-S field scripts fail closed for alternate description cues and
fail open for dialogue speech: Prism continues reading rather than risking a
missing line. Failure to resolve a disclaimer message through the loaded script
falls back to the packaged, fingerprint-bound text for that exact supported
field version; no text is inferred from message order alone.

The opening exemption is revoked after the opening movie closes, the player
loads an unrelated field, or the startup sequence exceeds its bounded lifetime.
Reset, suspend, and unload clear all compatibility and queued-speech state.

## Verification and deployment

Automated tests cover:

- vanilla opening behavior and direct-field abandonment;
- field 116 to Echo-S disclaimer 109 to field 116 to movie completion;
- rejection of an unrelated field 109 visit or unsupported script identity;
- exact vanilla and Echo-S cue matching with no nearby-offset fallback;
- once-per-entry behavior for alternate keys;
- voiced dialogue suppression, unvoiced fallback, choice/disclaimer exemption,
  and reset behavior;
- bounded description deferral and stale-cue cancellation;
- preservation of non-Echo behavior.

Release verification builds and runs the x86, x64, shared, and parity tests,
publishes both runtime assemblies, verifies PE architecture and package
contents, and deploys into the installed Reloaded-II mod without launching or
focusing FFVII, 7th Heaven, or Reloaded-II. The user performs live game testing.
