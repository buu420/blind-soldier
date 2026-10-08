# Temple clock and miniature temple validation - October 8, 2026

The user reported a second hand knocking the player off the Time Guardian clock
without audible guidance, and an unlisted statue required after the Red Dragon.
There was no log. The user confirmed live hazard guidance and a selectable,
navigable Story/Objects destination on both runtimes, with manual avoidance and
interaction. The candidate is version 0.8.9; 0.8.8 was already released.

## Native evidence

The installed legacy archive identifies clock field 607 `kuro_4`, the long hand
as entity 21, short hand as 22 and second hand as 23. The latter's Main decreases
its rendered facing through TURNGEN. LINE entity 18 `line10` is the knock-off
trigger. Its Go calls Cloud entity 20 script 5; script 7 enables it and script 8
disables it. The director disables it on the later GameMoment 627 return. Cloud's
knock-off script moves the party to the lower floor and enters field 608.

Fresh read-only Ghidra resolves DIR 00618062, TURNGEN 00617B0F, LINE 006111D8,
LINON 006115AD, SLINE 006114D0, SIN 0061F48C and COS 0061F516. Native collision
00637ABB uses the leader's collision radius; 00637D35 adds the Confirm facing
condition for applicable line triggers. Rendered direction zero points toward
six at negative Y, 64 toward three at positive X, and 128 toward twelve at
positive Y. SIN/COS additions precede their fixed-point shift, so the readout
uses the rendered model pivot rather than assuming a script operand is its
coordinate. No native game file or Ghidra program was modified.

The installed clock mesh has 132 triangles. Its IDdr scripts unlock two ends of
each hand bridge; the common native neighbour completes each three-triangle
span. The twelve independently derived triples match the readout's 36 hand
triangles. Middle, doorway-side and lower-floor triangles do not warn of a
crossing hazard. The second hand is never a navigation target or exit.

The mural hall is field 612 `kuro_82`. The preceding Cloud script sets GameMoment
621 and enters this field. Its director starts Red Dragon battle 652, then makes
entity 19 `mini` visible, solid and talkable at 1032,18,57 and enables LINE entity
7 `border2`. The model's Talk and Contact slots return without progressing.
The actual interaction is the line 914,35,0 to 922,-46,0 in front of it: its Go
checks native Confirm mask 0x220, sets 624 and enters field 613. At 627 the same
line offers an optional unsuccessful attempt to move the miniature temple; at
630 the collapse is a cutscene. These script bytes are checked from installed
data, rather than inferred from a walkthrough.

## Change and regressions

Both runtimes build the shared field activity observation, now including the
coherent LINON state for entity 18. Clock speech observes only the visible second
hand's current model pose and recent rendered movement. It names position,
approach, crossing and passage; it never reads a future angle or timer, promises
safety, moves the player or owns Confirm. Unreadable hazard state is announced
while on a hand. Hidden models and the disabled native line are silent.

Changed ordinary positions retain the existing 2.5-second speech cadence; an
increased hazard severity is immediate. Urgent current warnings can interrupt a
protected repeat, still behind the delivery's 500-ms retry bound and its focus
and speech checks. A refused or muted warning is replaced by the latest pose,
and leaving the hand drops it. Repeat describes the current pose without
advancing the automatic motion or speech watch, including when either main
clock hand is unreadable.

The statue's priority-zero Story row names the visible miniature temple and
Confirm for moments 621-626. The native enabled line gates availability. Its
KeepActiveOnArrival setting uses native touch arrival and retains the goal until
Confirm progresses the scene. Objects offers the named line at moments 621-629.
Both generated catalogs gain exactly the intended row and updated count; every
prior row and its order are preserved. Both hosts embed the same catalogs.

The red clock regression fails against 0.8.8 for missing automatic knock-off
speech; the actual x64 coordinator likewise fails against the exact released
0.8.8 DLL. The initial statue test fails because the named rows are absent.
An intermediate statue route reproduces walking onto the trigger without an
arrival announcement. The final tests cover native script witnesses, Story and
Objects availability, all twelve clock bridges, unreadable reads, Repeat,
protected speech interruption, refused delivery, native disable, mute and focus.

Four statue routes use the installed walkmesh, real planner and navigation
controller: Story and Objects from each native post-battle start, 277,117 and
305,84. Each announces arrival at 900,-20 on walkable floor, touching the active
line within the native leader radius 34 and 137 units from the solid model.
The runtime still reads the live radius. This establishes route and contact
behavior offline; the player supplies the final Confirm.

## Evidence and limits

Research and red/green logs are retained under
`C:\Users\buu42\Documents\FFVII-ActiveBuild\temple-clock-statue-20261008`.
Release gate, exact-package checks, independent review bindings and deployment
manifests are retained under `release-0.8.9-20261008` in the same ActiveBuild root.
Claude Teammate researched and implemented the statue catalogs and installed
route tests; an independent Codex reviewer checks the complete candidate.

The current Steam installation is on `D:\SteamLibrary`; its executable has the
supported SHA-256 57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B.
The test host accepts its path through FF7_ACCESSIBILITY_NATIVE_EXECUTABLE, so
no executable needs copying into the former C: installation path. Clock native
decompilation uses the installed legacy executable; the x64 coordinator tests
use the translated guest memory readers shared by the actual Steam host.

No live gameplay or hardware/controller hook delivery was exercised. The
newest-only warnings report sampled visible poses and do not predict an exact
impact time. The report cannot establish what a particular tester heard or
which navigation category they tried. The older post-mission freeze and a
separate Keystone altar arrival concern are outside this change.
