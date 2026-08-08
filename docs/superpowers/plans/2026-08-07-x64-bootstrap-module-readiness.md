# X64 Bootstrap Module Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate intermittent Blind Soldier 2026 bootstrap exit code 17 by waiting for the resumed target process module list to become ready.

**Architecture:** The bootstrap will use a bounded `WaitForRemoteModuleBase` helper that recreates and enumerates module snapshots until the requested module appears, the target exits, a non-retryable error occurs, or five seconds elapse. A native child-process test will delay loading a DLL so the regression is deterministic and exercises real Windows module enumeration.

**Tech Stack:** C++20, Win32 Tool Help APIs, Visual Studio 2022 MSBuild, Pester native verification, PowerShell packaging, GitHub releases, Accessibility Mod Manager author CLI.

## Global Constraints

- Preserve fail-closed startup: never continue into an unmodified game after bootstrap failure.
- Retry interval is 10 milliseconds and the production timeout is 5 seconds.
- Stop retrying immediately when the target process exits.
- Do not change Reloaded-II, private .NET runtime, 7th Heaven, FFNx, or game-detection behavior.
- Publish both game entries as version 0.1.7 on the beta channel.

---

### Task 1: Reproduce delayed module readiness in the native behavior test

**Files:**
- Modify: `native/BlindSoldier.Bootstrap/process_bootstrap.h`
- Modify: `native/BlindSoldier.Bootstrap.Tests/bootstrap_tests.cpp`

**Interfaces:**
- Produces: `LPVOID WaitForRemoteModuleBase(HANDLE process, DWORD processId, const std::wstring& moduleName, DWORD timeoutMilliseconds, Logger& log)`.
- Consumes: Windows process handles, process IDs, event handles, and `Logger`.

- [ ] **Step 1: Expose the current lookup through the wished-for readiness helper**

Add the exact signature above to `process_bootstrap.h` and move the existing one-shot lookup behind it without changing its behavior. Rebuild and run the existing behavior test first to verify this is a behavior-preserving refactor. This gives the delayed-load test a real production seam while ensuring its RED result is a behavioral failure rather than an unresolved-symbol build error.

- [ ] **Step 2: Write the delayed-load child-process test**

Add a child mode that waits on a named event, sleeps briefly, calls `LoadLibraryW` for a module not linked by the test executable, and remains alive until a release event. In the parent, create the events, start the child, begin `WaitForRemoteModuleBase`, signal the load event from a short-delay thread, assert a non-null base, then release and join the child.

- [ ] **Step 3: Run the x64 behavior test and verify RED**

Run a sanitized-path MSBuild rebuild of `native/BlindSoldier.Bootstrap.Tests/BlindSoldier.Bootstrap.Tests.vcxproj` for `Release|x64`, then run the executable.

Expected: the new delayed-module assertion fails because the current implementation returns as soon as the first valid snapshot omits the module.

### Task 2: Implement bounded fresh-snapshot retries

**Files:**
- Modify: `native/BlindSoldier.Bootstrap/process_bootstrap.cpp`
- Modify: `native/BlindSoldier.Native.Tests.ps1`

**Interfaces:**
- Consumes: the `WaitForRemoteModuleBase` declaration from Task 1.
- Produces: a lookup that returns the remote base when found and `nullptr` only on process exit, non-retryable failure, or timeout.

- [ ] **Step 1: Move lookup into the declared helper**

For each attempt, check `WaitForSingleObject(process, 0)`, create a new `TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32` snapshot, enumerate with `Module32FirstW` and `Module32NextW`, close the snapshot, and return immediately when `_wcsicmp` matches.

- [ ] **Step 2: Add bounded retry and diagnostics**

Use `GetTickCount64` to enforce `timeoutMilliseconds`; sleep 10 milliseconds between attempts. Retry absent modules plus `ERROR_BAD_LENGTH` and `ERROR_PARTIAL_COPY`, while logging and returning immediately for other snapshot errors.

- [ ] **Step 3: Pass the process handle through resolution**

Change `ResolveRemoteLoadLibraryW` to accept `HANDLE process` and call `WaitForRemoteModuleBase(process, processId, owner.filename().wstring(), 5000, log)`. Update `InjectDll` to pass its existing process handle.

- [ ] **Step 4: Strengthen the source contract**

Assert in `BlindSoldier.Native.Tests.ps1` that the source includes the readiness helper, target-exit check, fresh-snapshot loop, 10-millisecond retry interval, and 5000-millisecond production timeout.

- [ ] **Step 5: Run GREEN verification**

Rebuild and run the behavior tests in Win32 and x64, then run the native Pester suite. Expected: bootstrap behavior and build tests pass in both architectures; the pre-existing protected-registry installer test may require elevation and must be reported separately if the environment denies it.

- [ ] **Step 6: Commit the focused fix**

Stage only the bootstrap source, header, native behavior test, source contract, design, and plan. Commit with `fix: wait for target module readiness`.

### Task 3: Build, validate, deploy, and publish 0.1.7 beta

**Files:**
- Generated: portable and mod-manager ZIP archives under the existing release staging directories.
- Modify through author CLI: Buu Mods `index.json` release records for `ffviiold` and `ffviinew`.

**Interfaces:**
- Consumes: verified bootstrap binaries and the existing self-contained package builder.
- Produces: version 0.1.7 beta assets and catalog records with exact SHA-256 hashes.

- [ ] **Step 1: Run repository release verification**

Run the focused native suite, portable package tests, package verifier, and relevant launcher/runtime checks using the repository's established scripts.

- [ ] **Step 2: Build fresh packages**

Build the portable package and derive the architecture-specific 2013 and full 2026 Accessibility Mod Manager archives. Confirm the x64 bootstrap hash differs from 0.1.6 and that the expected x86 bootstrap remains architecture-correct.

- [ ] **Step 3: Deploy the corrected local runtime**

Replace only Blind Soldier-owned files in the local game installation using the established deployment script, preserving game and third-party mod files.

- [ ] **Step 4: Publish release assets**

Create or update GitHub release `v0.1.7`, upload the two mod-manager ZIPs and portable archive, and record their SHA-256 hashes.

- [ ] **Step 5: Publish catalog entries through the author CLI**

Add `0.1.7` beta releases for `ffviiold` and `ffviinew` with the exact asset URLs and hashes, validate the index, run `tests/Verify-Ff7Catalog.ps1`, then publish to `buu420/buu-s-mods` main.

- [ ] **Step 6: Verify live delivery**

Fetch the public catalog through GitHub's API, confirm both entries select `0.1.7` on beta, download each release asset, recompute its SHA-256, and compare it with the catalog. Verify the source and catalog repositories are synchronized with their remotes.
