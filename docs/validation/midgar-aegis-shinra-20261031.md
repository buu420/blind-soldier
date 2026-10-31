# Late Midgar ladder navigation, 31 October 2026

Scope: missing ladder targets and the routes to the Aegis Armlet and Shinra HQ during the Disc 2 return to Midgar. Release 0.10.6 covers the shared field-navigation implementation and both runtime hosts.

## Evidence

Three supplied Steam x64 logs identify build 0.10.4. The clearest witness transfers from 734 to 733, arrives on native triangle 14, previews “Aegis Armlet. climb up,” then reports no native chain to the item. An independent installed-script and walkmesh audit confirms that this position lies beyond the one-way walkway drop. The armlet remains reachable from the earlier connected region and unavailable from the logged later platforms. Existing positive and negative route cases are retained.

Both installed flevel archives match. An independent parser checked native LADER, LINE, MAPJUMP, arrival scripts, connected walkmesh components and archive-wide inbound edges. The Aegis region has no ordinary return entrance after the one-way drop. The [original walkthrough and screenshots](https://almarsguides.com/Retro/Walkthroughs/Ps1/Games/FinalFantasyVII/FullPlaythrough/Disc2/Midgar/) corroborate the visible layout and optional Shinra detour; native state decides availability.

Ghidra 12.1.2 decompiled the fingerprinted x86 LADER handler 00615EC6. It stores movement mode 4/5, reverse direction, progress, target XYZ and triangle in the 0x88-byte event record, returns while the native climb is active, then clears its state on completion. Engine SHA256: 4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225. Steam host SHA256: 57a23d166d69e46b9e3339f779d4a3c4feb402a989fa7291d0d9b4a1953abb4b. The encrypted x64 host is not claimed as directly decompiled; its evidence is translated native state, supplied runtime logs and shared-code/runtime tests.

## Changes and regressions

Steam omitted the ladder snapshot and mounted-state eligibility from both periodic speech paths. It now uses the same existing shared guidance as the legacy host. Empty menus still identify an actual mounted ladder; item previews no longer assign its direction to the item.

Native scaffold platform ladders join Exits from currently available, controlled-player transitions and enabled LINEs. Each row routes through its actual traversal and completes at its native endpoint after dismount. A replay covers both the raw ladder triangle hint and the adjacent landing triangle the walkmesh resolves; it cannot complete at the source platform. Mounted ladders in 733–736 and 778 offer manual up/down choices even when the raw arrival pad has no walking route. Unusable/dismounted state creates no manual rows. No Confirm, flags, positions, scripts, timers or combat state are written by these changes.

Ghidra also resolves the per-frame movement function 006342C6, called with input bits by 0063C17F. For modes 4/5 it reads direction keys only for the current leader with UC0; UC1 advances automatically toward the scripted endpoint. UC handler 00614155 mirrors context+0x32 to 00CC040C, the existing cue reader's control byte. ASK/JOIN/SPLIT and an interpreter return can change only the actual gate, so the ladder reader captures 00CC0DBA before and after the movement state and fails closed if it changes. The snapshot identifies player-driven movement. Automatic/hidden climbs retain script ownership; a real manual climb can remain navigable when the cue mirror is stale. Native 0DBA and existing cue 040C use the same translated guest page on x64; a mapping regression checks both divergent values. This avoids offering a false way back during 733's hidden duct arrival. The native evidence is exported separately; no global control flag is written.

The HQ entrance is native field 736, entity 18: its LINE requests Cloud script 5, climbs to triangle 2 and transfers to 225. The game requires 15[131] bit 6, set by field 737, section 1's upper-left passage. That gate and section-dependent junction destinations are retained. The corresponding ladder in 778 is covered too. Native 225 -> 227 -> 234 gateways continue to the HQ lobby.

Expected assertion failures were captured for empty-category ladder silence and for field 736, entity 18 missing the ladder portal. Focused tests then passed in both runtimes against installed data. Additional checks cover both manual ends, no automatic input, unusable state, disabled lines, all eight local platform ladders, mount and manual climb holds, raw/resolved endpoint completion, legitimate Aegis chains and native HQ unlock/junctions.

The reviewed Story generator extends only 11 existing maximum moment ranges through 1603. Native writes establish 1601 at the manhole, 1602 after the Turks, 1603 after the bridge battle and 1604 at Hojo. All 1,447 definitions, duplicate counts, header and other parameters are preserved; collapse, enabled-line, triangle, section and encounter gates remain. Exceptions tied to earlier encounters or the standing walkway retain their original ranges. The canonical JSON is regenerated from the pinned Kujata revision; array/property ordering accounts for the larger textual diff.

## Release criteria and limits

The release pipeline requires all 19 dual-runtime gates, 13 release contracts, the installed native data and five available Echo-S variants, existing Postscript fixtures, and four full suites bound to the exact packaged dependencies. Launcher informational version must be 2.0.0.0+blind-soldier.0.10.6 before packaging. Archive hashes, bundled recordings, deployment preservation, source/CI/tag binding, public assets and both Mod Manager records are independently checked and retained in the private release receipts.

Live traversal, physical controller use, audible screen-reader speech and recipient-PC tests are not performed here. Automated and native evidence does not claim those outcomes. Licensed archives, tester logs and extracted scripts remain private. Claude assistance is restricted to Opus 5.5/max.
