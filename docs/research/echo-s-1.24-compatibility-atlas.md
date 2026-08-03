# Echo-S 1.24 compatibility atlas

## Frozen inputs

- Active IRO: `Tsunamods__Echo_S_1.24.iro`
- Extracted field root: `C:\FF7A11Y\research\echo-s-1.24-root\data\field\flevel`
- Vanilla field root: `X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir\data\field\flevel`
- Installed FFNx module: `AF3DN.P`
- FFNx SHA-256: `7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A`
- Matching debug database: `FFNx.pdb`, RSDS identity
  `{B921745C-7439-4227-88F6-2E2C0AB83956}`, age 1

The field identity is SHA-256 over section-one bytes `[0, textOffset)`. This
is the exact prefix available from the live `field_script_ptr`; text content is
excluded so runtime and offline hashes have the same structural boundary.

## Cue alignment result

The reviewed analyzer result is:

```text
Cues: 127; mapped: 124; reviewed: 2; discarded: 1; ambiguous: 0; missing: 0.
```

The complete per-cue mapping, both vanilla and Echo-S field fingerprints, and
the surrounding-anchor counts are in
[`echo-s-1.24-cue-alignment.tsv`](echo-s-1.24-cue-alignment.tsv).

Echo-S 1.24 also packages complete `flevel.lgp` variants for Aavock UI,
Retouch, Finishing Touch, and ESUI. Each 706-field variant was independently
extracted and analyzed. All four produced the same reviewed result above and
the same exact 126 retained cue keys; only their script-prefix fingerprints
differ. Their complete evidence is stored in:

- `echo-s-1.24-ui-aavock-cue-alignment.tsv`
- `echo-s-1.24-ui-retouch-cue-alignment.tsv`
- `echo-s-1.24-ui-ft-cue-alignment.tsv`
- `echo-s-1.24-ui-esui-cue-alignment.tsv`

The runtime manifest contains all 144 distinct alternate field fingerprints.
It accepts the shared cue map only after the current field matches one of
those exact reviewed fingerprints.

Two exact semantic substitutions required manual script review:

1. Field 116, entity 0, script 0, vanilla byte 204 (`WAIT`) becomes Echo-S
   byte 246 (`REQEW`, opcode `0x03`). Echo-S replaces the vanilla `REQ` plus
   `WAIT` sequence with the single blocking request at the same story action.
2. Field 182, entity 1, script 1, vanilla byte 20 (`REQ`, opcode `0x01`)
   remains byte 20 but becomes `REQSW` (`0x02`). The target script and church
   door action are unchanged.

Field 322, entity 0, script 0, byte 444 was discarded. It is not an opcode
boundary in vanilla and duplicates the valid byte-446 cue. The valid cue maps
to Echo-S byte 456.

Runtime matching remains exact. These reviewed substitutions do not enable a
nearby-offset or fuzzy fallback.

## Echo-S startup field

Echo-S inserts `blackbgh`, field 109, between opening field 116 and the opening
movie. Its supported script fingerprint is:

```text
95D109B176FB1A007A076D5CBFE74BA9E00396A87194BBA552699898E841C57D
```

The four UI flevel packages use the same messages with three additional exact
script fingerprints (Aavock and ESUI share one):

```text
0EE70B724A9F19F675688EB616BA0A24345CB74C81EDD82A6FB03F56AFB9B6C2
B51CFFC242CAA043D9FF8BD0FEB55401AA456C5A1CF6C465C538B8F5034BC03D
FBFB6AC70976DD48B647976AF6A51BB9D4AE5DE0B30940DA7DE70CA7CA6E8367
```

Its four decoded native messages are:

1. `Welcome to Project Echo-S.`
2. `This mod has a few optional features that you may have missed inside 7th Heaven. If you haven't checked them before now, please be sure to do so. Double-click Echo-S in your mod list and select the options you would like. These options are on by default, but you may turn them on/off at any time.`
3. `This mod was created by the entire community. Thank you to everyone that voiced NPCs for us! The quality may differ and vary from time to time as the NPCs are not professional VAs,and they do not all have amazing microphones. But we all did our best. So thank you all! I hope you enjoy the experience!`
4. `If you find ANY bugs at all, PLEASE report them in our Discord server. A link can be found in the Mod Info column of the Echo-S mod inside 7th Heaven.`

These strings are packaged only as a fallback tied to those exact field hashes.
An unrelated field 109 or changed Echo-S script cannot claim them.

## FFNx voice ownership

Official FFNx source defines:

```cpp
bool play_voice(char* field_name, byte window_id, byte dialog_id, byte page_count)
```

It probes the window-specific and field-specific voice paths and returns the
actual result of `nxAudioEngine.playVoice`. FFNx stores that return in
`current_opcode_message_status[window_id].is_voice_acting` for both `MESSAGE`
and `ASK`.

Ghidra 12.0.4 imported the installed module and recognized its matching PDB.
Because full type recovery on the 133 MB PDB was prohibitively slow, the exact
public symbol was independently resolved through the same PDB with Microsoft's
DIA SDK:

```text
?play_voice@@YA_NPADEEE@Z
bool __cdecl play_voice(char *, unsigned char, unsigned char, unsigned char)
RVA 0x004187E0
section 1 offset 0x004177E0
```

The installed image's pristine entry bytes are:

```text
55 8B EC 81 EC 0C 01 00 00 A1 40 CD 08 12 33 C5
```

The runtime guard checks the exact module hash and the first nine stable bytes.
It deliberately stops before the following ASLR-relocated absolute address.
If either check fails, no FFNx detour is installed and Prism remains the
dialogue fallback.
