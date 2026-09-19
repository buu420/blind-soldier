# Gold Saucer follow-up evidence, 2026-09-19

The friend log ends at 13:50Z, before v0.5.7 publication at 19:02Z. It is not evidence of a regression in that release.

## Missing Battle Square story step

At 12:34:42Z the player is in field 505 (games / Wonder Square), GameMoment 442, with zero story targets. Story reports none at 12:34:59 and 12:35:17. The terminal-floor platform already existed in field 497; no objective existed in the room where Cait Sith actually joins.

Installed field 505 entity 18 (jp3) init byte 0 is LINE `D01E00ACFC0000F8FFD1FC0000`: (30,-852,0) to (-8,-815,0). Contact script 2 sets selector 5[8]=3 and requests cloud script 4 (`030144` at byte 8). The installed exit parser resolves this to field 499, Battle Square. The new row matches that line and its live enabled state at GameMoment 442..444. Generator and embedded catalog both contain it.

## Racing waiting-room occupants

Installed field 512 (crcin_2) entities 4..8 load the gold_dirver1, gold_driver2/3 models; entity 9 loads sub_esto. Entity 4's Talk MESSAGE 14 identifies Joe; entity 9's messages identify Ester. Entities 5..8 have anonymous Talk text, including ellipses, so the old speaker-name heuristic drops them. Verified labels now supplement the catalog without bypassing live model, visibility or Talk-enabled checks. Ramuh (entity 10) remains an object rather than an NPC.

Ester leaving and returning is native behavior: the existing Story target is already restricted to her visible model at GameMoment 467. No timer shortcut or early race start is added.

Ghidra headless read-only inspection of the supported legacy executable confirms TLKON opcode 0x7E at 0x618A80 writes event +0x61; VISI 0xA4 at 0x618A01 writes +0x62. LINE and LINEON handlers establish and enable the actual contact boundaries. Native scripts/model-loader evidence were extracted from the licensed local game; they are not included in the release.

## Regression scope

GoldSaucerFollowupTests drives the actual target readers at the reported stage, checks disabled/wrong-stage exits are withheld, checks visible/talkable versus hidden/unloaded occupants, and compares the route and model/Talk anchors against the installed game. It runs in both architectures; pure state tests run in the x64 portable module suite.

Joe is introduced by Ester script 4 MESSAGE 5, synchronously requested by director byte 251, during the entry sequence whose init disables player control. The playable room follows that sequence; the named Talk also identifies him. This is not an undisclosed story role inferred from an internal entity name.
