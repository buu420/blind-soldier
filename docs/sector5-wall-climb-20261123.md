# Aeris's bed and the wall-climb report

The November 23 tester log contains older sessions and a current session with the
1,515-entry description manifest. Its final player position is field 224,
`wcrimb_2`, around triangle 69. Selecting the optional battery repeatedly produces
`Route unavailable`. Game-update logging then ends after rendering a nearby ladder
cue, before its playback-start record; the separate keyboard observer continues.
There is no exception stack or native fault-module record in this input.

## Native route evidence

Stock and all five reviewed Echo-S variants have the same relevant traversal.
Field 224's line30 (entity 12) requests Cloud script 13. It descends to triangle 62,
then the controlled character automatically jumps through triangle 76 to
`(-229,961)` on triangle 81. That ledge reaches line90, the optional battery socket.
The native battery guards and normal Confirm input remain unchanged.

The planner's missing-Z ladder-pair heuristic changed the explicit triangle-81
cleanup landing to triangle 99, diverting the route to the other ledge. It now keeps
a script-specified landing when its coordinates lie on that triangle on a different
walkmesh component. Collapsed endpoints that still name their own component retain
the existing pairing path, as verified by the older reactor-ladder regressions.

Ghidra on executable SHA256
`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`
confirms JUMP at `00615CA3`, LADER at `00615EC6`, LINE at `006111D8`, LINON at
`006115AD`, and the native contact/OK checks at `00637ABB`/`00637D35`.

## Bed availability

Field 190's bedsen LINE is part of the visible bed background. Stock disables it
at GameMoment 170 while the wake-up scene runs and reenables it afterward. Echo-S's
director Main disables it before that moment check, so later returns leave the
bed visible while its rest interaction is unavailable. Bedsen's native Move script
still owns interaction whenever the LINE is enabled. No game script or enable bit
is changed here. An explicit catalogue opt-in keeps only this disabled LINE as
labelled scenery; other disabled interactions remain unavailable.

## Audio evidence and limits

The old cue player rendered a cue, then constructed and initialized a fresh
`WaveOutEvent` synchronously in the game update. Completion callbacks and proximity
resets disposed those outputs. The log stops within that setup interval. NAudio's
[WaveOutEvent implementation](https://github.com/naudio/NAudio/blob/master/NAudio.WinMM/WaveOutEvent.cs)
performs native WinMM device operations in initialization and disposal.

The replacement follows the existing Junon cue player's ready-output pattern:
one initialized output, a continuously ready mixer, bounded queued samples, and
sample-only clearing when proximity ends. Tests exercise repeated cues, clearing,
failure and idempotent disposal without an audio device. This establishes the
removed synchronous device operations; it does not establish the tester's exact
crash cause or an audible in-game result.

Private input hashes, native exports, Ghidra output and red/green logs are retained
under `FFVII-ActiveBuild/sector5-wall-climb-20261123`. Decoded game content and the
tester log are not included in release packages.
