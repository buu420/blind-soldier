# Submarine selection and automatic pursuit

The user confirmed automatic pursuit and firing guidance on October 5, 2026. Work starts from released v0.8.3 and targets both legacy x86 and native Steam x64.

## Behavior

- J/L cycle submarine sightings, P toggles automatic pursuit, I starts it and K repeats status.
- R3 opens the controller target list; D-pad Up/Down selects, A or X pursues and B or R3 from the list stops.
- The unique red leader hull identifies the Huge Materia submarine in the story mission. Ordinary red arcade submarines retain separate names.
- Only currently projected native contacts update positions. Remembered contacts carry a fixed last-seen point and an age limit; speech identifies them as last seen.
- Pursuit issues ordinary yaw, pitch and throttle controls. Up lowers the nose; Down raises it. Menu accelerates and Cancel brakes. Switch remains manual.
- Fire guidance requires the selected currently visible contact to hold the native lock and a loaded torpedo lamp. The game can lock a different contact; speech states that explicitly.
- Overview sightings are selectable, but that camera cannot lock. The player returns to normal view with PageDown.
- At a stale overview point, hold the current course briefly for the next blinking sweep; normal-view searches and expired sightings end pursuit. No hidden movement prediction is used.
- Pause, quit prompt, unreadable state, focus loss, result, module exit, suspension and disposal release owned inputs. Manual steering or throttle stops pursuit.

## Implementation ownership

Claude owns the shared reader, readout, pure pursuit tracker and their tests. Codex owns native input delivery, controller ownership, the shared host coordinator, both runtime integrations, packaging and deployment. Both use the same tracker and live control-table resolver. No physics, lock, damage, script, save or hidden-target state is written.

## Verification and release

1. Preserve failing and passing evidence for throttle delivery, native-poll retirement, selection ownership and host boundaries.
2. Run shared reader/tracker/coordinator tests through both test hosts.
3. Review the final change against native Ghidra evidence and the new tester-log append.
4. Build retained portable packages with the dual-runtime release gate; verify all four full suites against packaged dependencies and audit both AMM packages.
5. Publish v0.8.4 after checks, deploy with backups and preserve existing settings, assets, histories and saves. Archive source and release evidence to Dropbox.

Static native evidence and automated tests do not establish live gameplay behavior. The installed native Steam executable and SDL2.dll are currently absent; use the licensed fingerprinted executable only as a temporary static-test fixture and remove it afterwards.
