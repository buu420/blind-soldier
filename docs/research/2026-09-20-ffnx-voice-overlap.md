# FFNx dialogue overlap after loading a save

The September 19 22:46:36 UTC session in the reporter's downloaded log installs the verified FFNx `play_voice` hook at 22:46:42. It then speaks visible dialogue in fields 214 and 218 while their description-script identities resolve to `Unknown`. Echo-S compatibility is first activated in field 220 at 22:54:35. Subsequent successfully voiced MESSAGE pages are suppressed; the remaining visible-window speech follows failed voice playback for unvoiced choices or an item notification.

The defect was a dependency on `echoSCompatibilityActive` in the polling speech predicate. That flag recognizes reviewed description-script fingerprints, so it cannot establish whether the current dialogue has a voice. The same condition hid voice-event logging before activation, which means the report cannot retrospectively prove the result of each earlier playback call.

The fix keeps the verified-hook and native window/dialog playback checks, removes the description-fingerprint prerequisite, and logs each dequeued voice result. ASK ownership and unvoiced fallback are unchanged. `FfnxVoiceSpeechTests` exercises the real visible-window coordinator and reproduces one Prism dispatch under the old predicate where zero are expected.

Native evidence was rechecked on the installed `AF3DN.P`, SHA-256 `7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A`. Ghidra 12.1.2 confirms the nine-byte entry signature at RVA `0x004187E0` and the window/dialog/page voice-path selection followed by the audio-engine call. The previously resolved PDB signature is recorded in [the Echo-S atlas](echo-s-1.24-compatibility-atlas.md). [Official FFNx voice source](https://github.com/julianxhokaxhiu/FFNx/blob/master/src/voice.cpp) returns the audio engine's playback result; no field-description manifest is involved.

This is a targeted startup-gating correction. It does not change ASK voice policy, tracker expiry, or simultaneous-message ownership. A new in-game reporter log is still needed to confirm the audible result.
