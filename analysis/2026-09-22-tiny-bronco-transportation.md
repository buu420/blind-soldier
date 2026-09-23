# Tiny Bronco Transportation and shore approach

## Report and reproduction

The final session in `ff7_accessibility_steam2026_x64 (8).log` contains the report.
At 2026-09-22 16:09:28Z the player changed from Tiny Bronco (model 5) at
109072,102,162804 to Cloud (model 0) at 109923,258,162872. Repeated Transportation
requests subsequently said none available. Valid 10-entity reads immediately
preceded several failures, so rejected reads alone do not explain the symptom.

The replay uses the last mounted coordinates as a **parked-position proxy**; the
log does not record each parked entity's coordinates. This is a reproducible
shore geometry regression, not an exact post-disembark memory capture.

The installed world map resolves the proxy boat to water triangle 89732 and Cloud
to shore 89734. The old catalog offers only the immediately adjacent walkable
triangle 89731, which belongs to a different island component. The controller
then silently removes the boat because that arrival is unreachable. Triangle 89734
is two edges from the boat's triangle and contains valid native contact points.

Separately, the entity reader compares whole snapshots twice. An entity moving
between reads invalidates every parked vehicle. The log alternates rejected and
accepted entity snapshots hundreds of times during this attempt.

## Native evidence

Read-only Ghidra analysis of the installed legacy executable, with follow-up
bounded decompilation, established:

- `007610B3` allocates a node and links it at `00E39A00`.
- `007611AE` attaches the party to the boarded vehicle; `00761313` restores party
  ownership when detached. `007667B2` handles native disembark transitions.
- `00762A21` tests the two models' collision masks at `0096DDB0 + 8*model`.
  These are 8x8 masks over 256-unit cells, bounded by strict absolute X/Z deltas
  below 1024. Tiny Bronco's bytes are `00 18 3c 7e 7e 3c 18 00`; party models 0/1/2
  use `00 00 00 18 18 00 00 00`. Closing contact rejects movement; moving away is
  allowed.
- `00762993` stores the touched entity at current entity+4 even when closing
  movement is rejected. `0076420A` dispatches that entity's model action based on
  Confirm (`0x20`). Geometric proximity alone does not prove native contact.
- Private independent geometry sampling finds native contact positions on 89734,
  including 109840,230,162868, only 83 X units / 4 Z units from the logged dismount.
  The old arrival triangle 89731 has no contact positions for this proxy.

The repeatable script is
`analysis/ghidra/DumpTinyBroncoTransportationEvidence.java`. Native dumps and
personal logs remain outside the repository.

The primary ABI reference is [FFNx world_event_data](https://github.com/julianxhokaxhiu/FFNx/blob/master/src/ff7.h),
which corroborates linked-list, position, model and animation-flag offsets.

Input hashes (SHA256):

- Installed `ff7_en.exe`: `4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`.
- Both installed runtimes' `data/wm/wm0.map`: `43E72295212311FCEC3A513051A96CA4475185A72211EBC3A5E4F89A50ECA78C`.

## Movement replay findings

A private independent replay applies the existing raw-triangle movement oracle,
then the executable's raw party/boat masks and movement rollback. Four camera
headings (0,1152,2048,3550) at step lengths 30,60,120 all reached contact and then
stalled with a geometric-only arrival predicate. The game rejected the closing
step, leaving the accepted player position outside the mask. This requires the
native contact pointer, not just testing the route endpoint or current geometry.

The same review reproduced continued automatic input after an active boat target
was removed, and after the player remounted it. These are controller-level checks;
verifying only the catalog's parked-entity filter does not cover an existing route.

## Verification

The independent native movement replay now passes all 12 speed/camera combinations.
Each includes a rejected closing step and ends navigation using the simulated native
contact pointer, even though the accepted position is outside the collision mask.
Removing or remounting the selected vehicle also stops automatic movement.

The initial full x64 suite caught an exported native pointer on the new vehicle-contact
type. The type and the target's contact property are now internal; the existing trust
surface test was preserved.

Final verification, all exit 0:

- Release builds for both runtime test hosts and shared/parity tests.
- Dual-runtime package build and package validation.
- Full legacy and Steam x64 suites against the exact packaged runtime DLLs and each
  installed game's native data. Test-host DLL hashes matched the package.
- Shared and parity suites, and the dual-runtime shared-source check.

No live boarding test has been performed. The replay supplies the contact-pointer
write established by Ghidra; it does not observe that write in a running game.

The verified package was deployed to both local mod installations. Each installation
changed 16 files and verified all 4,038 payload files by SHA256. The entire Configuration
directory, including room-description history, was preserved. Changed files were backed
up outside the repository. That initial local deployment used the 0.6.5 baseline.
The user subsequently requested publication; this fix is included in the 0.6.6
release preparation, with both portable and Mod Manager packages.

