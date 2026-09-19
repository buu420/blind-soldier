# World-map menus and description delivery, 2026-09-19

## PHS and Status

The report log shows root-menu labels and the Status party selector speaking on module 3, followed by silent Status/PHS screens. PHS is driven by title/name/cursor draws rather than ActiveMenuWidget. Status uses title/detail draws. Both need their existing bounded native evidence accepted by the world-map ownership gate.

Both layers now use HasCurrentWorldMapMenuEvidence: the worker calls HasWorldMapMenuOwnership before Poll and resets an unowned reader. Fixing Poll alone was insufficient; the host-order regression reproduced that failure. Closed/expired screens and ordinary world-map text remain unable to hold menu ownership.

## Recorded descriptions and dialogue

The log contains 18 action-description stops caused by native dialogue opening and two caused by field changes. Recorded action clips now finish when a text box opens. Native films retain their separate lifetime and dialogue handoff policy.

FieldCutsceneSpeechPriority reads the actual independent output device. It does not infer completion from Prism, and it releases a cancelled or finished recording even if no tick observed the clip playing. Text-only narration retains the bounded word-count estimate. A stale recording indication is bounded by the longer of 30 seconds or the declared clip duration plus five seconds; field-state resets cannot extend that bound.

The x64 dispatcher combines this device signal with its existing Prism narration-completion tracker. An idle screen reader cannot override an active recording. The legacy native queue and draw reader defer before consuming their pending lines; visible-window polling rejects delivery through the existing acknowledgement path and retries the same line when the recording ends. New action cues, including ones missing a recording and requiring TTS fallback, wait behind the current clip.

Host suspension, scene changes, and native film lifetime rules remain. In particular, this patch does not promise to finish an old field description after leaving that field.

## Verification

Focused tests exercise PHS and Status through the worker's pre-Poll ownership order, menu expiry, actual clip completion/cancellation, queued visible-window retry, recording priority across Prism active-to-idle transitions, bounded stale playback, unrecorded fallback queueing, and existing film/foreground boundaries. The old PHS host gate and unrecorded-cue overlap each failed their new regression before correction. Tests run in both architecture hosts where applicable; no live friend-session reproduction is claimed.
