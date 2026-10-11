# Sector 7 navigation evidence, 2026-10-11

The reported missing hideout, bar re-entry and shop service were investigated
against installed stock FLEVEL fields 151 (mds7), 152 (mds7_im), 154 (mds7pb_1)
and 155 (mds7pb_2), plus the actual current Echo-S 1.24 replacements. The
[official catalog](https://raw.githubusercontent.com/tsunamods-codes/7th-Heaven-Catalogs/master/catalogs/qhimm.xml)
identifies the current release, and its
[IRO](https://gp-mc.net/tsunamods/echo-s-7/latest_full/Echo-s.iro)
was read using bounded HTTP ranges and the author's
[archive format](https://raw.githubusercontent.com/tsunamods-codes/7th-Heaven/master/AppWrapper/IrosArc.cs).
Only mod.xml and the four fields in base/Aavock/ESUI/FT/Retouch were extracted:
21 entries totaling 4,255,264 bytes. Licensed field files remain in private
research evidence and are not distributed with the mod.

The old navigation catalog parsed disk FLEVEL scripts, while 7th Heaven/FFNx
loaded override scripts with different entity indices. The current reader
captures the loaded script prefix twice with field/module/pointer checks and
accepts an exact installed stock identity or one of the audited Echo-S prefixes.
Cached metadata is keyed by field and prefix fingerprint; unreadable, torn,
unloaded or unreviewed current scripts cannot reuse stale target identities.
Map names and future cross-field routing retain a separate installed metadata
path and cannot authorize current clickable targets.

| Native target | Stock entity | Echo-S entity | Native evidence |
| --- | --- | --- | --- |
| Seventh Heaven entrance | 151:17 | 151:8 | border4 LINE (290,379,76) to (326,364,76), MAPJUMP 154 |
| Hideout descent | 154:15 | 154:4 | border2 LINE (-52,187,0) to (-8,173,0), Go4 action handler |
| Return upstairs | 155:8 | 155:2 | pinball LINE (155,-14,0) to (129,-65,0), Go4 action handler |
| Item/Materia shopkeeper | 152:4 | 152:4 | border3 152:2 counter LINE; REQEW oldm1 Talk; SHOP 3 |
| Closed shop object | 151:18 | 151:9 | item LINE |
| AVALANCHE sign object | 151:19 | 151:10 | tatekan LINE |
| Hideout television | 155:10 | 155:4 | TV LINE |

The shopkeeper stands at (-91,175,0) with collision disabled, behind a counter
from (70,19,0) to (-65,-11,0). Navigation binds to that counter rather than the
blocked model. The shop sells both items and Materia; separate invented vendor
targets would misrepresent the native service. Availability still requires a
visible current shopkeeper and an enabled counter.

The descent handler requires GameMoment >=72 and temporary bank 5[26] !=1;
the return handler requires bank 5[21] !=1. Stock AD/script2 tests GameMoment
==96 for the flashback branch; every other enabled branch reaches field 155.
Thus only moment 96 is excluded for normal stock descent, rather than all
later moments. Tests include 72,95,96,97,108 and116. Other incidental bar LINEs
that animate story scenes are excluded from ordinary room Exits.

Fresh read-only Ghidra analysis of the retained legacy executable covers
0060C94D (LINE event dispatch), 00637ABB (strict line touch), 00637D35 (OK
heading/ready state) and 0060F750 (bank reads). The executable source SHA-256
is 4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225.
Retained x64 engine decompilation supports the translated native state seam.
The lift and counter use Go4 action checks; no extra OK-heading gate is added.
Their approaches use the current leader's event+0x72 collision radius, with
coherent checked reads for the lift. Confirm and script execution remain under
player control. No camera, script, quest or actor-position writes are added.

Regression checks run in x86 and x64 hosts against native stock walkmeshes and
all five current Echo-S script variants. They exercise bar/hideout re-entry,
native arrival-to-counter and arrival-to-lift routes, enabled/busy states,
current NPC identities (including 151:18,151:19 and155:10 that conflicted with
stock object exclusions), remapped Objects and Story, and rejection of stale
or unreviewed loaded scripts. Independent review identified and corrected
the object-ID exclusions and the overly broad stock flashback guard.

Exact-package x64 checks also caught a public raw-pointer property on the newly
linked script identity. The observation now exposes only field ID and prefix
hash; its pointer and pointer-bearing constructor remain internal for lifecycle
checks. The public API contract is included in the portable module test path,
so this boundary is checked even when licensed game data is unavailable.

This evidence establishes static native behavior and automated reader/router
behavior. No live Echo-S or Steam traversal, controller-hook delivery, purchase,
lift activation or screen-reader listening was performed for this repair.
