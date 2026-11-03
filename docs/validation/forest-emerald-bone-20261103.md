# Ancient Forest, Emerald and Bone Village, 3 November 2026

The supplied log is Steam x64 0.10.7 at world progress 4. Its startup explicitly omits Ancient Forest for lack of a terrain-script entrance. It contains no underwater Emerald observation or Bone Village field visit, so those reports are investigated through native scripts and regression cases rather than described as reproduced log events.

## Ancient Forest

The world script enters location 55 through model25 function3 (`0x5903`), after its native test admits party models0,1,2. Confirm dispatches that contact handler; walking onto a terrain-script boundary does not enter it. Model25 function0 places it at world (92960,179731). `field.tbl` sends location55/entry0 to field622 (`anfrst_3`). Its collision mask at executable `0096DDB0 +25*8` is `00 00 18 3C 3C 18 00 00`.

The catalog now builds this Location from native contact ground and uses the live model's contact identity when available. A coherent pool that hides or omits it cannot manufacture an entrance. Static metadata uses its fixed native placement. These are contact arrivals, separate from automatic terrain-script entry cues. The route planner uses the selected vehicle's native terrain permissions; stage4's map opens the basin and legal grass landings. Chocobos receive a normal dismount handoff and entry remains manual Confirm.

Regressions read the installed event handlers, field table and executable mask. They exercise the supplied log position, both terrain stages, all three party leaders, black/gold chocobos, Highwind landings, hidden/removed models, exact contact and normal-entry prompts. Existing terrain entrance-cue coverage remains intact.

## Emerald

The prior catalog removed an undrawn Emerald and pursuit stopped after 500ms without visibility. This made it difficult to finish selecting a moving target. Fresh read-only Ghidra analysis of licensed `ff7_en.exe` (SHA256 `4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225`) confirms `0076315C` clears entity draw bits every frame and `0076328F` requires a fresh draw indication. That transient observation is not persistent existence.

Native `wm2.ev` allocates model30 while defeat bit `03DC` is clear and either submarine access bit `1A83` or `1A92` is set. A coherent entity supplies existence for the persistent Events row. A separate runtime memory stores only positions accepted by the existing renderer, camera, fog and seabed visibility checks. Unknown rows have no usable location; planners and arrival checks reject them. Hidden XYZ updates never change the remembered point.

Selection and guidance distinguish Current, LastSeen and Unknown. A current pursuit silently releases input during a missing render sample and retains its encounter route for 500ms of continuous absence. A positive frame inside that window resumes silently; sustained loss changes to last-seen navigation and announces that transition once. Routes started from a LastSeen row use normal travel depth immediately. Last-seen navigation stops at the recorded approach area and does not claim the creature is present. A positive resighting updates it; an active route's sighting prompt does not ask the player to select it again. Unavailable or replaced native entity pools clear memory; native entity retirement releases input immediately. Both hosts retire inactive-map and battle observations, so reusing a pool address cannot inherit a previous world's sighting or announcement. A torn world-state read releases input while preserving knowledge and the announcement identity; a continuous 500ms state outage clears them. No knowledge is saved across loads.

The native-camera integration regression includes early submarine access, menu delay, moving hidden coordinates, repeated single-frame gaps, the 499/500ms boundary, safe-depth recovery, resighting, last-seen completion and reset/retirement. State-failure cases cover immediate input release, brief and 499ms outages followed by valid hidden frames, resumed remembered movement, no repeated continuous-sighting announcement, and 500ms retirement. Existing visibility and ordinary-route avoidance tests remain in place. Both full runtime harnesses include the Ancient Forest checks; Bone Village success output records the number of Echo-S layouts actually checked.

## Bone Village

The excavation field772 writes a result to bank1 byte234 and returns the party to field617 at (-206,-161), triangle64. Entity13 `box1`, model10, holds the result. It is closed and lit while the result is nonzero; its Talk script pays the reward and clears the result. Key results5,6,7 require game moment greater than1195 and the native one-time key flag. Native opening reveals the reward.

The nine existing object rows already covered pending results, but lacked Talk activation and a Chest cue. The generator and shipped definitions now name the neutral Excavation results chest, use the shared Talk positioning/facing path and supply the Chest cue. No reward is named before opening, and no Confirm is generated. The other451 definitions are unchanged.

Regressions verify the native placement, pose, reward gate, return jump and all five reviewed Echo-S mappings; all nine results, native Talk reach, missing/hidden/empty/collected/disabled models; the installed approach from the dig return; and the controller's facing handoff. Echo-S's unrelated Phoenix result condition is unchanged by this repair.

## Evidence limits

Native scripts, Ghidra evidence, supplied logs, automated navigation/input simulations, exact package checks and publication receipts are separate evidence classes. No live game traversal, physical controller use or audible speech listening was performed for 0.10.8. Private logs and licensed native data remain outside the public source and packages.
