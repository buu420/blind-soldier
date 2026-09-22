# Room history and Cosmo Canyon autowalk

Evidence: the user's local x86 log, last updated September 22 at 09:19:31,
copied to `FFVII-ActiveBuild/cosmo-battle-autowalk-20260922/legacy-session.log`.
The older Downloads logs were not used as this reproduction.

## Room descriptions

Both runtimes keep heard room IDs in `Configuration/room-descriptions.json`,
keyed by native save container and game slot. Successful Continue selects that
slot's history; successful Save carries the current history into its destination.
New Game starts an empty history. Cancelled or failed operations do not bind it.
Existing rooms start being recorded with this build; old logs are not imported.
Save files themselves are not modified.

The delivery gate applies only to MPNAM room cues. It records a room after output
accepts it and drops already-heard queued rooms without reserving dialogue time.
Story actions, movies, and the explicit status command retain their behavior.
Unreadable history storage falls back to session deduplication and logs the error.

Ghidra confirmed Continue readiness 2 follows the native checksum validation.
Successful Save is identified by the result popup, not by returning to page 1
(both success and failure do that). The observer handles readiness before the
first playable frame and accepts title selections only in native module 20.
This prevents stale menu buffers from resetting history after a battle.

## Autowalk after battle

The actual stop was `Mod.PublishControllerNavigationContextFromModule`: hiding
the controller menu in battle also called the unlogged `StopEveryControllerAutoWalk`.
It ran before the ordinary battle suspension path. Transient modules and loss of
foreground now release movement through Suspend; title and explicit controller
Stop remain real stops. The x64 ownership policy already suspends for the swirl,
battle, and results modules and resets at title.

A proposed route-reacquisition timeout was disproved and removed. Position
recovery retains `BeaconEnabled`; the failed walk was cancelled earlier by the
controller-menu publication path.

## Cosmo Canyon upper approach

Three recorded stalls shared field 525, triangle 314, near (-677,-551). The path
through triangles 0 and 1 repeatedly failed while the player's manual recovery
went around through triangle 9. Routes that actually cross that connector now
use the captured checkpoint (-772,-698,-1468) as an explicit required detour.
The existing walkmesh planner verifies both legs and all native locks. Routes
already beyond the connector do not return to the checkpoint.

The movement-clearance function also incorrectly returned true whenever the
dynamic NPC list was empty. It now checks the walkmesh and native locks in that
case, allowing the existing running-distance checks to reject walls.

## Verification boundaries

Regressions cover persistent reload/save isolation, overwrite and New Game,
load failure and cancellation, same-frame load success and field entry, stale
title memory through battle, refused output and duplicate queued rooms, and the
real x86 host's battle/foreground/title controller publication paths.

Installed walkmesh checks cover the upper approach in both directions, the real
story exit trigger with locks 299/300, the triangle-9 checkpoint, blocked movement
without NPCs, and refusal to cross a locked checkpoint. Both installed data sets
are exercised. These are automated and native-data checks; live controller play
and a real save/load cycle still require in-game verification.

Final Release builds and both complete executable test suites passed against
their respective installed archives. The x64 suite reported that native keyboard
hook installation could not be exercised in its test host (Reloaded.Hooks static
initialization); this is not a live hook-installation claim. Test transcripts and
the build/deployment reports are in the investigation directory named above.
