# Auto Walk and Train Graveyard Navigation Design

## Outcome

Blind Soldier will let a player highlight any field or world-map navigation target and press `P` to start or stop automatic directional movement along the same route used by spoken navigation. Auto walk will never press an interaction button. It will release every owned direction when movement is unsafe or navigation is unavailable.

The Train Graveyard story catalog will expose the actual three-stage puzzle progression instead of routing directly to the blocked Sector 7 Station exit:

1. Move the first train by crossing its native trigger line.
2. Move the upper train by crossing its native trigger line.
3. Leave for Sector 7 Station after both train-state changes are complete.

## Controls and speech

- `J` and `L` continue to change the highlighted destination; `K` continues to repeat it.
- `P` starts a route to the current selection when navigation is off, then begins auto walk.
- Pressing `P` again stops auto walk and releases all direction keys.
- Changing category or destination stops auto walk; the player presses `P` for the newly selected target.
- Speech is limited to concise state changes such as `Auto walk on.` and `Auto walk off.` Existing navigation speech continues to describe directions, progress, ladders, and arrival.

## Safety and parity

- The implementation shares route-decision and scan-code output logic between x86 and x64.
- Auto walk owns only arrow-key directions. It does not press confirm, cancel, run, or action.
- Focus loss, an unreadable frame, a non-navigation module, an incoherent route, a disabled beacon, or a completed route immediately releases owned directions.
- Battles and field transitions pause directional output. If the same route remains valid after normal navigation recovers, auto walk resumes; it never holds a direction through the transition.
- At a ladder mount point, auto walk releases directions so the existing ladder prompt can tell the player to press action. Once mounted, it follows the navigation controller's live climb direction and releases again on dismount or completion.
- World-map auto walk uses the existing connected route and progress model and does not add an audio beacon.

## Train Graveyard evidence

Field 145 (`mds7st2`) stores moving-train state in field variable bank 1, byte `0xA4` (164). Native field scripts produce the normal state sequence `0 -> 3 -> 7`. The first train trigger is native line 20 near `(1740, 3094, 0)`, the upper train trigger is native line 30 near `(823, 3482, 0)`, and the final Sector 7 Station exit is valid only at state 7. Story targets will be conditioned on those native states.

## Failure handling

Input injection is fail-closed. Partial `SendInput` failure triggers repeated key-up cleanup and disables further output until cleanup succeeds. Diagnostics identify the stop reason without repeatedly announcing route updates.
