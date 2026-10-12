# Northern Crater creature description validation

## Native trigger

Fresh Ghidra exports of both installed engines identify the creature as entity12,
model8, HYFD.HRC, in field767 (`las4_4`). OFST sets the native offsets; OFSTW
holds the entity's own cursor while its interpolation runs. The first update
still displays the initial position. Capture therefore requires changed native
coordinates, an active interpolation, and the exact reviewed OFST/OFSTW bytes.
The two stock anchors are `767:12:3:12` and `767:12:4:22`; Echo inserts VISI before
the first approach, moving its anchor to byte14. Both installed stock archives
and all five Echo field variants were checked directly.

Observation runs at native `00660D6E`, after the bone pose is bound and immediately
around the original polygon dispatch. Steam's translated physical callback is
`void(void)` at host RVA `00DBDA70`; guest arguments come from verified ESP,
not physical host parameters. Every callback forwards Original exactly once.
Native image identity, function mappings, prefixes and executable ownership are
verified before installing a hook. The legacy culling helper can be patched by
FFNx uncrop; it is not a required callable or visibility witness.

Visibility follows the owned HRC/bone/RSD chain to the polygon primitive lists.
Bounded, opaque triangle lists are transformed using the native world matrix
and effective projection, including the field owner's inherited projection for
world-only HRC sets. Homogeneous clipping tests all six planes, positive W and
nondegenerate screen area. Both windings are present throughout the actual
reviewed HYFD approaches, so no guessed cull mode is needed.

Capture requires the frame's draw scope. Worker delivery instead revalidates the
owned geometry and scene after EndScene closes that scope. It retains the native
field, actor, movie, mask, fade, pointer and matrix-ownership checks. The exact
movie-only branch suppresses field tile layers, excluding foreground tile
occlusion in this scene; this rule is not generalized to other fields.

## Speech and lifetime

The creature uses a bounded, separate narration lane ahead of waiting room and
action descriptions. Output refusal retains the cue; only actual acceptance
deduplicates it. Repeated per-bone callbacks do not renew its original lifetime.
Unknown script prefixes, hidden/replaced geometry and expired episodes remain
silent. A dedicated audio owner stops only the creature recording at its native
presentation and host lifecycle boundaries.

The retained MESSAGE44 warning is identified through Cloud's own script cursor
and the current native window, rather than the interpreter's globally current
entity. Prism completion requires successful delivery and observed backend
speech ending. For the verified FFNx 1.24.3 module, Ghidra and matching native
symbols identify its window voice map, message status and SoLoud handle
generation. The reader performs bounded coherent reads; it never calls the
inserting `isVoicePlaying` query, and a valid paused handle remains unavailable
for narration.

Two locally generated recordings use the same approved Brice reference and
voice workflow as 0.9.7. Independent medium.en transcription exactly matches
both sentences. Export preserves natural tempo and pitch, trims only outer
silence, applies the existing loudness target and checks the decoded waveform.
The append-only manifest contains 1,514 entries; all earlier assets are protected.

## Verification boundaries

Focused reader, original-forwarding/ABI, queue, warning-completion and audio
lifecycle regressions run in both test hosts. The release uses the full dual
runtime gate, native archive integration, exact packaged assembly suites and
deployment checksum/preservation checks. Detailed native exports, positive and
negative regression logs and waveform reports are retained with the release
evidence. No live final-scene, controller, screen-reader or in-game listening
verification is claimed by these offline checks.
