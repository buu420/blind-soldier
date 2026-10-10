# Highwind interior navigation: native evidence and validation

## Report and approved scope

The user reported that ordinary Highwind exploration depended on Story guidance
and confirmed normal category navigation for available rooms, doors and crew
services, with Story retained, both runtimes and version 0.9.1.

## Native investigation

Both installed flevel archives provide identical opcode streams for fields
66..80 (1,265 script listings). The playable Highwind rooms are fship_1 (66,
outside deck), fship_23 (70, earlier bridge), fship_25 (72, later bridge),
fship_3 (73, operations), fship_4 (74, corridor) and fship_5 (76, Chocobo hold).
fship_12 (67), fship_24 (71) and fship_42 (75) are scripted scene variants;
ordinary control-lock suppression stays in place. The directory includes zz1..3
after the Highwind fields; those were inspected but are outside this repair.

The corridor's gateways lead to 66, 73 and 76. Its forward jump LINE maps to
68 at GameMoment 1614, to 70 below 1199, and to 72 otherwise. The existing
generic exit guards do not resolve those word comparisons, so the shared
branch policy now resolves this one native door. Its identity and geometry
stay fixed. The separate Northern Crater LINE still waits for bank13[91] bit7.

The earlier bridge disables its gateway with MPJPO. Entity6's [OK] and Move
slots share a native pointer, and its MAPJUMP returns to the corridor. The
existing script walker already discovers this alias; no opcode-walker or
Confirm-input change is needed. Ghidra confirms MENU at 0061F04A, MAPJUMP at
006131C4, LINE at 006111D8, LINON at 006115AD, MPJPO at 0061A4D4 and native
LINE dispatch at 0060C94D/00637D35. Analysis used the original x86 engine
project read-only and discarded all temporary analysis changes.

Pilot identities are 70:17 and 72:16: their own Talk scripts can MAPJUMP to the
world map. Operations crew 73:12 has native PHS (MENU7), Save (MENU14), and
HP/MP restoration choices; earlier chapters also use that crew for required
party formation. Hold crew 76:6 is the native Chocobo handler. Labels identify
these roles without selecting a menu answer or exposing a hidden actor. The
existing visibility, model mapping and talk-disable checks still apply.

Primary research references:

- [ff7tools native field format/opcode source](https://github.com/cebix/ff7tools/blob/master/ff7/field.py)
- [Fenrir's field opcode implementation](https://github.com/dangarfield/ff7-fenrir/blob/master/OPS_CODES_FIELD_README.md)

These provide orientation; the installed archive bytes and Ghidra are the
binding evidence. Initial suspicion that the cockpit return was excluded by
the [OK] filter was disproved by its aliased Move entry and was not implemented.

## Checks

`HighwindInteriorNavigationTests` reproduced indistinguishable ordinary room
labels before the change. A second failing test reproduced unresolved bridge
destinations. The focused checks pass against both compiled runtime assemblies
and their respective installed archives. They cover fifteen native door labels,
crew identification and hidden/non-talkable/absent model cases, ordinary room
selection with empty and active Story, eight bridge chapter boundary cases,
the crater access flag, native service opcodes, and routes from nine native
arrival positions, including both levels of the corridor.

The original native corridor walkmesh is connected; no synthetic room, route,
party menu, camera write or automatic interaction was added. An exploratory
route sweep from every corridor triangle reached all five native exits with
no dynamic boundaries configured. That does not prove every live boundary
state or collision against every standing actor. Full release verification
and exact-package evidence are retained in the release archive/handoff.

Live gameplay, SDL/controller delivery and speech listening have not been
performed for this repair. Automated native-archive and memory-frame tests
must not be presented as a completed Highwind playthrough.
