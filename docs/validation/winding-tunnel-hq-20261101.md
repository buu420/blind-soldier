# Winding Tunnel HQ entrance, 1 November 2026

Scope: the approved 0.10.7 repair of the late Midgar HQ entrance, both runtimes and Echo-S, native climbing controls, deployment and publication.

## Evidence and cause

The supplied Steam x64 log is from 0.10.6; its module identity matches the installed released DLL. The player repeatedly selects a blocked "Way up the tunnel", follows ladder cues in sections without a ladder, then eventually climbs from field 736 to 225 and continues through 227 to the HQ lobby (234). The menu does refresh and includes the ladder when available. Licensed archives, extracted scripts and the tester's log remain private.

Native tunnel_4 (736) init tests bank 15[129]. Section zero hides PATH and locks triangles 12, 13, 72 and 73; section eighteen locks the opposite end's ten triangles. The exit LINE scripts themselves have no such test. Even with those actual IDLCK bits loaded, the route planner returns a centre-line path through the remaining narrow mesh slivers, reproducing the reported stalls. The initializer's exact section test and lock set now add the missing availability guard to the matching LINE exit. Other fields' narrow-route behavior is unchanged.

The lad_u LINE requests Cloud's climb only while bank 15[131] bit 6 is set. The initializer uses the same flag to show the ladder. Field 737's section-one upper-left branch sets it; returning via 736's downward exit clears it. Previously this guarded only the exit row, while the traversal and proximity cue checked only LINE enablement. Both runtime traversal providers now also apply the bound exit's live guards after LINE grace resolution. Only the two reviewed tunnel fields suppress the incoming top landing as an additional proximity entrance. Field 778's unconditional ladder and other automatic ladder entrances remain available.

Ghidra 12.1.2 decompiled the fingerprinted x86 field-bank byte reader (0060F750) and writer (0060FA7D). Both confirm bank 15 maps to 00DC08DC + 0400, corroborating the existing shared guard reader. Engine SHA256: 4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225. Steam host SHA256: 57a23d166d69e46b9e3339f779d4a3c4feb402a989fa7291d0d9b4a1953abb4b. The encrypted x64 host is not claimed as directly decompiled; its evidence is translated native state, the supplied runtime log and runtime/shared-code tests.

The [field-format parser](https://github.com/cebix/ff7tools/blob/master/ff7/field.py) and [original Midgar walkthrough and screenshots](https://almarsguides.com/Retro/Walkthroughs/Ps1/Games/FinalFantasyVII/FullPlaythrough/Disc2/Midgar/) supplement the native evidence. The HQ detour is labelled by its verified 736 -> 225 -> 227 -> 234 chain; no optional objective replaces the main-story route.

## Regression and release coverage

Expected assertion failures were captured for the section-zero exit, the section-eighteen exit and two ladder cues while the ladder is hidden. Focused tests pass in both compiled hosts against the C: and D: archives and all five exact loaded Echo-S layouts. A replay of the reported section/flag journey checks current exits, real triangle locks, menu browsing to the HQ ladder, and a route from the native arrival with the manual mount and Up climb. Existing mount/climb input holds, native junction destinations and the route onward to HQ remain covered. Unknown checked native guards cannot authorize the ladder; field 778 and other fields' automatic entrances have positive controls.

Release requires all 19 dual-runtime gates, the release contracts, installed native data, five Echo-S variants, existing Postscript fixtures, four suites against exact packaged DLL/dependency copies, fresh launcher version 2.0.0.0+blind-soldier.0.10.7, archive checksums, preservation verification, source/CI/tag binding, public downloads and both Mod Manager entries. Receipts are retained separately.

No live gameplay, physical input, audible speech or recipient-PC test is claimed. No Confirm, position, flags, scripts, timers or combat state are written by this repair. Claude assistance is restricted to Opus 5.5 at max effort.
