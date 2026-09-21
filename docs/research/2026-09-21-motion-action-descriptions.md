# In-engine action descriptions, September 21, 2026

Eight short descriptions fill gaps in the Cosmo Canyon campfire, Shinra Mansion,
Rocket Town flashback, and Temple of the Ancients scenes. They supplement the
existing dialogue reader and use Brice's approved narration voice.

| Action | Field / entity / script / byte | Opcode bytes |
| --- | --- | --- |
| Vincent leaps into the passage | 302 / 8 / 4 / 25 | BA0301 |
| The red-cloaked man leaps onto the coffin lid | 303 / 5 / 12 / 60 | A20001 |
| The man lowers himself into the coffin | 303 / 5 / 15 / 0 | AF0501 |
| Sephiroth throws an orb and Cloud drops to one knee | 308 / 4 / 6 / 6 | 0105C3 |
| Barret stands and spreads his arms at the bonfire | 526 / 7 / 1 / 202 | BA0501 |
| The rocket technicians salute Cid | 564 / 8 / 3 / 22 | 010BC3 |
| Cait Sith tumbles sideways in the mural hall | 612 / 16 / 12 / 47 | AF0901 |
| Cait Sith falls forward beside the altar | 613 / 13 / 10 / 36 | A30601 |

The actions were visually checked in footage using the original field models:

- [Shinra Mansion footage](https://www.youtube.com/watch?v=CDA4AcA_obs):
  1168-1170, 1629-1632, 1654-1655, and 1730-1732 seconds.
- [Cosmo Canyon footage](https://www.youtube.com/watch?v=3BzMLN79QLk):
  Barret rises and spreads his arms at 107-108 seconds.
- [Rocket Town footage](https://www.youtube.com/watch?v=7q133TbItVk):
  the technicians salute at 1280-1283 seconds.
- [Temple footage](https://www.youtube.com/watch?v=-kXmadZJ6tw):
  the two falls occur at 1468-1470 and 1503-1506 seconds.

The native scripts establish the timing and identities; animation numbers alone
were not used to infer visual actions. Both installed game archives contain the
same bytes at all eight instruction boundaries. Cait Sith's mural-hall cue uses
script 12's fall. Script 13's animation at byte 17 is his recovery and carries no
fall cue. The altar cue follows his approach rather than his initial visibility.

Ghidra 12.1.2 decompiled the installed legacy executable's animation handlers at
0x006149A5, 0x00614424, and 0x0061484A. The executable SHA-256 is
`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`.
These handlers use the native actor and script cursor. Blocking animations can
revisit the same instruction, so the existing once-per-visit suppression matters.
The Steam x64 callback catalog maps the same guest handler identities. No new
hooks are added. Explicit WAIT counts omit animation and movement durations and
are not measurements of the available narration time.

Descript drafted the scripts from the reviewed observations; it did not inspect
video frames. The narration uses the previously approved local Qwen3-TTS voice
reference, natural tempo and pitch, fixed gain, and outer-silence trimming.
All eight clips passed independent medium.en transcription with exact spoken
words and decoded 44.1 kHz mono Vorbis checks. The batch adds 25.05 seconds of
narration. It preserves the existing 838 short recordings and movie assets.

Live playback through these scenes remains to be tested. This batch does not
claim complete coverage of every in-engine cutscene.
