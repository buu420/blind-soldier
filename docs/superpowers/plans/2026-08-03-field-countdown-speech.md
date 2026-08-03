# Native Field Countdown Speech Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Announce every active FFVII field countdown from the game's own remaining-time state in both the legacy x86 and Steam 2026 x64 runtimes, using the agreed minute, half-minute, fifteen-second, and final-ten schedule.

**Architecture:** A runtime-neutral tracker converts native remaining seconds into at most one due spoken announcement per observation. Each runtime supplies checked native timer observations and suppresses the changing timer window from ordinary dialogue speech, while continuing to acknowledge it so dialogue stability machinery cannot stall. The implementation follows the game timer rather than a synthetic wall clock, so pauses, frame stalls, and script resets remain accurate.

**Tech Stack:** C#/.NET 8 test harnesses, Reloaded-II x86/x64 modules, native FFVII field opcode/state evidence from Ghidra, PowerShell build/deployment scripts.

---

## Task 1: Confirm the native countdown contract

**Files:**
- Create: `analysis/ghidra/InspectFf7FieldCountdown.java`
- Create: `analysis/ghidra/ff7-field-countdown-contract.txt`
- Inspect: `Ff7.Accessibility.Reloaded/EchoSReactorTimerOverrideTracker.cs`
- Inspect: `Ff7.Accessibility.LegacyLayout/FieldMessageReader.cs`

1. Add a read-only Ghidra script that decompiles the x86 `WSPCL` and `STTIM` handlers and all useful references to the known remaining-seconds address.
2. Run the script against the existing legacy FFVII Ghidra project without modifying the project.
3. Record the verified timer value, display/active-state signal, reset behavior, and any version-relative address contract needed by the two runtimes.
4. Cross-check the opcode meanings against Makou Reactor's official opcode definitions and reject any state field that is not evidenced.

## Task 2: Build the shared threshold tracker with TDD

**Files:**
- Create: `Ff7.Accessibility.Core/FieldCountdownSpeechTracker.cs`
- Create: `Ff7.Accessibility.Reloaded.Tests/FieldCountdownSpeechTrackerTests.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

1. Add literal, table-driven tests for 10:00 through 2:00 whole-minute announcements; 1:30, 1:00, 0:30, 0:15; and the numbers 10 through 0.
2. Add behavioral tests for skipped frames, duplicate observations, pause/repeated values, timer resets, timer disappearance, and observing a timer after it has already crossed earlier thresholds.
3. Run the focused shared test harness and confirm the new tests fail because the tracker does not exist.
4. Implement the smallest state machine that emits at most the most urgent crossed threshold and resets only on a new or meaningfully increased countdown.
5. Re-run the focused tests and confirm they pass.

## Task 3: Add checked native observations and x86 integration

**Files:**
- Create or Modify: `Ff7.Accessibility.LegacyLayout/FieldCountdownReader.cs`
- Create: `Ff7.Accessibility.Reloaded/FieldCountdownSpeechCoordinator.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Create: `Ff7.Accessibility.Reloaded.Tests/FieldCountdownIntegrationTests.cs`

1. Add failing tests that prove an inactive/stale native value is ignored, a visible timer yields the native remaining seconds, timer pages do not leak through ordinary dialogue speech, and normal dialogue beside a timer still speaks.
2. Implement the checked reader from the Ghidra-established state contract.
3. Wire the tracker into the x86 field polling path and send due alerts through Prism with timely interruption; use short number-only speech for the final ten seconds.
4. Filter only positively identified timer output from normal dialogue processing.
5. Run the Reloaded test harness and fix only failures caused by this feature.

## Task 4: Wire the same behavior into x64

**Files:**
- Modify: `Ff7.Accessibility.Steam2026X64/Ff7.Accessibility.Steam2026X64.csproj`
- Modify: `Ff7.Accessibility.Steam2026X64/Steam2026ResearchObservationPump.cs`
- Modify: `Ff7.Accessibility.Core/RuntimeEventDispatcher.cs` if needed for explicit timer acknowledgement
- Create: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026FieldCountdownSpeechTests.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Program.cs`

1. Add failing x64 tests for the shared schedule, ordinary dialogue beside a timer, timer-page acknowledgement, and no duplicate speech from the timer's rendered text.
2. Reuse the same checked observation contract and shared tracker in the x64 observation pump.
3. Ensure suppressed timer pages are acknowledged so the stability gate continues advancing.
4. Run the x64 test harness and confirm parity with the x86 behavior.

## Task 5: Verify and deploy safely

**Files:**
- Modify only if needed: `docs/runtime-parity-matrix.md`
- Deploy to: `C:/Users/buu42/AccessXI/external/Reloaded-II/Mods/ff7.accessibility.reloaded`

1. Run focused shared, x86, and x64 test harnesses.
2. Run `Run-DualRuntimeVerification.ps1 -Mode Research` and retain the exact result; do not describe the broader research build as release-ready.
3. Confirm FFVII, Reloaded-II, and 7th Heaven are not running. If any are running, stop deployment rather than terminating them.
4. Record the installed configuration hash, deploy with `Install-FF7ReloadedMod.ps1` using `-SkipFfnx`, `-SkipSeventhHeavenSettings`, and `-AllowResearchNativeProfile`, then confirm the configuration hash is unchanged.
5. Verify both architecture-specific assemblies were installed and report the exact live-test behavior the user should hear.
