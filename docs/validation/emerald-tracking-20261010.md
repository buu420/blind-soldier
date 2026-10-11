# Emerald Weapon tracking: native evidence and validation

## Report and approved scope

The user reported that Emerald could not be tracked when deliberately sought.
They confirmed a selectable underwater target only when the current game view
shows it, automatic approach on explicit selection, continued avoidance on
other submarine routes and both runtime implementations. This is version 0.9.2.

## Native cause and correction

The Events catalog and explicit Emerald pursuit already existed. Their
positive-visibility gate was never satisfied by ordinary native camera memory:
`WorldMapUnderwaterVisibilityReader` read `D_00DE6A20.t` at offset 0x14,
assuming four-byte alignment. Native MATRIX is packed, with nine int16
rotation entries followed immediately by three int32 translations at 0x12.
The wrong address combined halves of adjacent components and rejected a
genuine drawn position as belonging to another camera or entity.

Fresh read-only Ghidra analysis of `ff7_en.exe` (SHA-256
`4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225`)
confirms `FUN_0074D50E` reads `DE6A32`, `DE6A36`, `DE6A3A`.
`FUN_0076328F`, `FUN_0075E0BA`, the camera and per-frame draw routines
were re-examined in the same run. The retained Steam engine decompile
`FUN_7ff7029e1950` reads these identical guest addresses through
`FUN_7ff7016cf0a0`. Steam's host uses the shared reader over that translated
guest address space; the correction therefore applies to both runtimes.
The original Steam executable fingerprint is unchanged:
`57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B`.

Primary web orientation agrees with the executable evidence:

- [Packed MATRIX definition](https://github.com/ergonomy-joe/ff7-worldmap/blob/master/NEWFF7/ff7_structs.h)
- [Native world camera and draw loop reconstruction](https://github.com/ergonomy-joe/ff7-worldmap/blob/master/NEWFF7/C_0074C9A0.cpp)

The installed map archives remain the authority for occlusion and routes.
Their `wm2.map` bytes match across the two installations. No entity position,
camera, visibility flag, encounter state or native archive is written. Existing
on-screen projection, fog, seabed occlusion, coherent capture and stale-position
checks remain in force. Off-screen, hidden or unconfirmed Emerald locations
are never substituted from the patrol script.

## Regression checks

The previous test camera fixture repeated the alignment error. It now writes
literal independently verified native addresses. Before correcting product
code, eleven visibility scenarios failed, including the complete native-frame
to Events/pursuit check. After the correction all 25 visibility scenarios pass.
New coverage exercises all three nonzero packed translation components and
torn translation reads, without deriving fixture addresses from product
constants.

The integration regression feeds a coherent native camera/entity frame through
`WorldMapEntityReader`, `WorldMapRuntimeContext`, Events, selection and automatic
input against installed underwater geometry. It verifies sighting speech,
selection, explicit approach depth, a fresh moving endpoint, immediate input
release on loss of sight, sustained-loss cancellation, hidden-model rejection
and ordinary-destination depth recovery. Both hosts run the same regression
against their respective installed maps. The focused switch is
`--emerald-tracking-only`; full suites also include these checks.

Full release verification, exact packaged-assembly suites, independent review,
protected-data hashes and public publication evidence are retained in the
release archive/handoff. These checks must not be described as live play.
Live Emerald encounters, hardware-controller delivery and speech listening
have not been performed for this repair.
