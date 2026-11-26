# Local audio buffer compatibility test

The report describes skipping or stuttering on an unidentified external sound
card. There is no tester log, and the user's own audio is smooth. The hardware
failure has not been reproduced. This is a local compatibility trial on top of
1.0.3, identified in runtime logs as `1.0.3-audio-buffer-test.1`.

Blind Soldier uses NAudio.WinMM 2.3.0. Its `DesiredLatency` is divided across
`NumberOfBuffers` when allocating the output; it is not a per-buffer duration.
See the [pinned WaveOutEvent source](https://github.com/naudio/NAudio/blob/v2.3.0/NAudio.WinMM/WaveOutEvent.cs).

| Output | Previous queue | Test queue | Block duration |
| --- | --- | --- | --- |
| Recorded descriptions | 80 ms, 2 blocks | 160 ms, 4 blocks | 40 ms |
| Navigation beacons and proximity cues | 80 ms, 2 blocks | 160 ms, 4 blocks | 40 ms |
| Footsteps | 80 ms, 2 blocks | 160 ms, 4 blocks | 40 ms |
| Immediate WAV cues | 40 ms, 2 blocks | 80 ms, 4 blocks | 20 ms |
| Junon timing cue | 40 ms, 2 blocks | 80 ms, 4 blocks | 20 ms |

Doubling the number of blocks provides additional queued audio during a short
decoder or worker scheduling stall. The duration of each refill is unchanged.
Persistent mixed outputs can take longer to sound a newly added cue: their
configured queue grows by 80 ms for navigation and 40 ms for Junon timing. The
timing output therefore keeps the smaller 20 ms blocks and 80 ms total queue.
Physical listening and minigame response on the affected card remain unverified.

A separate native-device experiment used the pinned NAudio DLLs and inspected
WinMM's actual queued headers while deliberately delaying the source's Read:

| Experiment | Injected delay | Old remaining queued blocks | Test remaining queued blocks |
| --- | --- | --- | --- |
| General output | 55 ms | 0 in all 5 observations | 2 in all 5 observations |
| Timing output | 25 ms | 0 in 7 of 8 observations | 2 or 3 in all 11 observations |

Intentional output shutdowns are excluded from these observations. Queue
exhaustion demonstrates susceptibility to these injected delays; it is neither
an audible loopback measurement nor evidence of the tester's particular cause.
An exploratory 60 ms/3-block timing candidate still exhausted its queue in some
observations, so the final timing candidate uses four 20 ms blocks.

Ghidra inspected the local x86 Windows WinMM DLL's waveOutWrite, waveOutOpen and
waveOutPrepareHeader export thunks. These forward to the system backend; this
does not inspect the tester's driver. Buffer allocation and refill behavior are
established by the pinned managed source and the runtime experiment.

The same five source files are compiled into the x86 and x64 runtimes. The
navigation device remains persistent and its mixer remains bounded. Description
decoding, gain, limiting, cancellation and normal completion retain their
existing behavior. This change concerns Blind Soldier's own WaveOut outputs.

Both complete x86 and x64 executable test suites passed using the published
ReadyToRun runtime DLLs with the installed dependency DLLs. Seven focused checks
also passed: Junon audio, navigation output reuse and description volume on both
runtimes, plus opening movie startup and cancellation. Compilation completed
with existing nullable/unused-code warnings (20 for x86 tests, 22 for x64 tests)
and no errors. The isolated host did not exercise controller-hook installation;
some private Postscript fixture checks were unavailable. These checks do not
establish live gameplay or audible listening on the external sound card.

Initial test launches needed their repository ancestry, executable apphost and
game-data environment corrected. The retained runner-failure logs describe
those setup failures; the final exact-test logs contain the complete passing
runs. No production changes were made to resolve the test setup.

Build scripts, native queue observations, Ghidra output, complete regression
logs and deployment receipts are retained in
`C:\Users\buu42\Dropbox\FFVII\FFVII-ActiveBuild\audio-buffer-20261126`.
Disposable build products and byte-exact rollback DLLs are in
`D:\BlindSoldier-audio-buffer-20261126`.
