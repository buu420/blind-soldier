# Mansion flashback bookcase validation

The report concerns the upstairs bookcase in Shinra Mansion during the Kalm
flashback. Objects restricted its interaction to GameMoment 386 and later,
although that bound does not exist in the bookcase's native script. Story at
moments 370 and 373 targeted gateway 1, behind a closed panel.

## Native evidence

Field 300, `sinin2_2`, entity 7 `lin0` declares LINE endpoints
`(855,658,339)` and `(958,589,339)`. Its OK slot tests temporary Bank 5 byte 7
against zero, sets it to one, releases triangles 143, 67 and 120, and requests
entity 10 slot 1. That slot runs the background animation opening the bookcase.
The Director opens the same panel on arrival from field 301, the staircase.

Ghidra 12.1.2 analysis of the licensed legacy executable confirms
`FUN_00637ABB` and `FUN_00637D35` use the leader's event `+0x72` radius, the
line's ready byte/angle and the leader's heading before the normal Confirm
edge triggers the OK slot. This is native pseudocode, not recovered original
source. No game state or interaction input is written by the fix.

## Regression and release checks

`--mansion-bookcase-only` runs the real Objects and Story readers with closed,
open and disabled-line states. It checks that the bookcase target does not complete
on arrival and changes to the passage when the native open flag changes. Unrelated
present-day hints stay hidden during the flashback. The initial regression failed because Objects omitted
the bookcase at moment 370.

Licensed route replays use the native field walkmesh and panel locks. Both
Objects and Story must start navigation and finish within the native contact
region without crossing locked triangles. Opening enables the staircase route;
moment 376 retains the hall return. The x64 tests also exercise
`Steam2026FailClosedFieldRoutePlanner` around the same reader and boundary state.

Exact opcode witnesses are checked against stock and all five reviewed Echo-S
data roots: base, Aavock, ESUI, FT and Retouch. These checks establish the native
interaction contract; they do not establish a live Echo-S playthrough on Steam.

Release verification includes the dual-runtime gate, packaged x86/x64/shared/
parity suites, release contracts, binary/package hash checks and deployment
preservation manifests. CI runs portable/source/Prism and release asset checks;
licensed native route replays run locally. All unrelated Story definitions are
preserved from the released catalog, including previously authored native guards.

## Limits

No live flashback traversal, controller play, keyboard interaction or audible
Prism output was performed for this fix. Offline native routes do not substitute
for player validation of facing, opening the bookcase or traversing the passage.
