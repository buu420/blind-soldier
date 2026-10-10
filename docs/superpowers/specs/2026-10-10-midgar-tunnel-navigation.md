# Midgar tunnel navigation repair

The user confirmed native Story guidance through the late Midgar tunnels leading
to the Turks, visible uncollected pickups in Objects, both runtimes and existing
navigation controls. The next release is **0.9.0**. Keep the published 0.8.11
release and its history intact.

The cumulative tester log's new 32,621,743 bytes show empty Story lists in fields
735, 736 and 737 at game moment 1601. Field 735 is `sbwy4_22`, the entry ladder;
736 and 737 reuse `tunnel_4` and `tunnel_5` according to native bank15[129]. The
current curated rows cover only selected sections after moment 1602. Native
scripts from both installed archives agree. The native crossing in field 778
settles the encounter and advances the chapter; combat and the decision remain
under player control.

Use the existing state-backed Story reader and navigation contracts. Select the
onward line from the actual section and chapter; preserve line-enable checks.
Give the ladder a verified manual instruction because it runs an input-waiting
LADER routine without a selectable gateway. Do not invent a ground route there.
Audit existing pickup models against native visibility and collection flags;
repair a demonstrated omission rather than add duplicates or reveal pickups
from another reused section.

Preserve both x86/7th Heaven and Steam x64, settings, saves, description history,
audio assets and existing control bindings. Do not write game progress, item
state or actor coordinates. Verify native scripts, Ghidra evidence and ordinary
player-facing behavior separately from unperformed live gameplay/controller
tests. Deploy and publish after the required automated and exact-package checks.
