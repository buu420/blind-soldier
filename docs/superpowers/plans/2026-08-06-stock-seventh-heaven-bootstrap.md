# Blind Soldier Stock 7th Heaven Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the x86 Blind Soldier portable runtime start after an unmodified 7th Heaven 4.5.2 AppLoader is ready, without shipping or modifying 7th Heaven/FFNx, while preserving direct x86, Prism accessibility, and the unchanged Steam 2026 x64 path.

**Architecture:** The existing x86 `version.dll` continues forwarding the 17 Windows Version APIs, but its worker classifies the launch and waits for the current process's ordered AppLoader log markers before starting the existing broker. The broker then attaches Reloaded-II after stock AppProxy/AppWrapper owns .NET; managed Blind Soldier starts normally through Prism and later chains FFNx hooks. Package, staging, verification, and Ghidra gates remove the obsolete WinMM/private-7H assumptions and prove that 7th Heaven and FFNx remain externally owned.

**Tech Stack:** C++20 and Win32, MSVC v143 x86/x64, .NET 8 build tooling with the existing x86 Reloaded mod, Prism, Reloaded-II 1.30.3, Shared Hooks 1.16.3, PowerShell 5.1, Pester 4.10.1, Ghidra 12.1.2, SHA-256, GitHub Actions.

## Global Constraints

- Stock 7th Heaven 4.5.2, AppLoader, AppProxy, AppWrapper, and FFNx files/configuration must remain byte-for-byte untouched.
- Do not ship `dinput.dll`, `AF3DN.P`, `AF4DN.P`, `FFNx.toml`, `steam_api.dll`, an ASI, or a Blind Soldier WinMM proxy.
- Keep the four byte-identical x86 `.local\version.dll` paths; do not place an x86 proxy at the universal archive root.
- Preserve the existing x86 C# accessibility feature code and Prism backend.
- Preserve Steam 2026 x64 code and payloads except shared release metadata.
- Detect current stock AppLoader from the loaded sibling `dinput.dll` and sibling `AppProxy.runtimeconfig.json`, `AppProxy.dll`, `AppWrapper.dll`, and `nethost.dll`.
- Accept 7th Heaven readiness only from the last current-process `AppLoader init log` followed by `AppLoader started successfully`, with `.7thWrapperProfile` absent.
- Use a 120000 ms AppLoader timeout and fail closed with an accessible error and absolute log path.
- Do not gate initial Prism/title/menu startup on FFNx field opcode readiness.
- Remove the obsolete `BlindSoldier.ManagedReady.<pid>` contract completely.
- Do not copy GPL-3.0 reference-project source.

---

### Task 1: Add a pure, testable stock-AppLoader readiness model

**Files:**
- Create: `native/BlindSoldier.VersionProxy/app_loader_readiness.h`
- Create: `native/BlindSoldier.VersionProxy/app_loader_readiness.cpp`
- Modify: `native/BlindSoldier.VersionProxy/BlindSoldier.VersionProxy.vcxproj`
- Modify: `native/BlindSoldier.WinMMProxy.Tests/BlindSoldier.WinMMProxy.Tests.vcxproj`
- Modify: `native/BlindSoldier.WinMMProxy.Tests/proxy_tests.cpp`

**Interfaces:**
- Consumes: `SupportedHostKind` from `native/BlindSoldier.Common/supported_hosts.h` and Win32 `FILETIME`.
- Produces:

```cpp
enum class AppLoaderGateState {
    Discovering,
    WaitingForCurrentLog,
    WaitingForSuccess,
    WaitingForProfileConsumption,
    ReadyDirect,
    ReadySeventhHeaven,
    Failed
};

struct AppLoaderObservation {
    SupportedHostKind hostKind = SupportedHostKind::None;
    bool stockLoaderSignaturePresent = false;
    bool recognizedFfnxModulePresent = false;
    bool processAlive = true;
    ULONGLONG elapsedMilliseconds = 0;
    std::string appLoaderLog;
    FILETIME processCreation{};
    bool wrapperProfilePresent = false;
};

struct AppLoaderGateDecision {
    AppLoaderGateState state = AppLoaderGateState::Discovering;
    bool ready = false;
    bool seventhHeaven = false;
    std::wstring diagnostic;
};

class AppLoaderReadinessTracker {
public:
    explicit AppLoaderReadinessTracker(
        ULONGLONG directDiscoveryMilliseconds = 3000,
        ULONGLONG timeoutMilliseconds = 120000);
    AppLoaderGateDecision Observe(const AppLoaderObservation& observation);
};
```

- Exact accepted log records use the stock formatter shape:

```text
YYYY-MM-DD HH:MM:SS.mmm INFO  AppLoader init log
YYYY-MM-DD HH:MM:SS.mmm INFO  AppLoader started successfully
```

- Timestamp conversion parses local time with `SystemTimeToTzSpecificLocalTimeEx`/`TzSpecificLocalTimeToSystemTimeEx` where available and falls back to `TzSpecificLocalTimeToSystemTime`, then compares the resulting `FILETIME` to `GetProcessTimes` creation time.

- [ ] **Step 1: Write failing state-machine tests**

Add tests that construct observations directly and assert:

```cpp
AppLoaderReadinessTracker gate(3000, 120000);
CHECK(gate.Observe(Observation(SevenHeavenX86, true, 10,
    CurrentLine("AppLoader init log"), false)).state ==
    AppLoaderGateState::WaitingForSuccess);
CHECK(gate.Observe(Observation(SevenHeavenX86, true, 20,
    CurrentLines({"AppLoader init log",
                  "AppLoader started successfully"}), false)).state ==
    AppLoaderGateState::ReadySeventhHeaven);
```

Cover all of these cases in the same test executable:

- exact stock host before 3000 ms remains `Discovering`;
- exact stock host after 3000 ms without loader evidence becomes `ReadyDirect`;
- `SevenHeavenX86`, sibling stock-loader signature, or recognized FFNx evidence permanently locks the tracker into the 7th Heaven branch;
- current init without success waits;
- success without a current init is rejected;
- stale prior-process init/success is rejected;
- multiple appended launches use only the last current init section;
- success before the last init is rejected;
- a truncated success line waits;
- success with `.7thWrapperProfile` still present waits and then fails at timeout;
- success after profile removal becomes ready;
- process exit and 120000 ms timeout become failed with nonempty diagnostics.

- [ ] **Step 2: Run the native proxy test and verify it fails**

Run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.WinMMProxy.Tests.ps1
```

Expected: FAIL because `AppLoaderReadinessTracker` and its observation types do not exist.

- [ ] **Step 3: Implement the pure parser and sticky state machine**

Implement line splitting without regex backtracking, require the exact message suffix, parse exactly 23 timestamp characters, and search from the last valid current-process init record. Store the sticky 7th Heaven classification in the tracker. Do not touch the filesystem in this layer.

- [ ] **Step 4: Add the new source files to both native projects**

The Version proxy project compiles the implementation. The behavior-test project compiles the same implementation so its tests exercise production code.

- [ ] **Step 5: Run the native proxy tests and verify they pass**

Run the command from Step 2. Expected: all existing forwarding/root tests and all new readiness cases pass.

- [ ] **Step 6: Commit the readiness model**

```powershell
git add native/BlindSoldier.VersionProxy/app_loader_readiness.h native/BlindSoldier.VersionProxy/app_loader_readiness.cpp native/BlindSoldier.VersionProxy/BlindSoldier.VersionProxy.vcxproj native/BlindSoldier.WinMMProxy.Tests/BlindSoldier.WinMMProxy.Tests.vcxproj native/BlindSoldier.WinMMProxy.Tests/proxy_tests.cpp
git commit -m "feat: model stock 7th Heaven readiness"
```

---

### Task 2: Gate Version-proxy broker startup on the current AppLoader launch

**Files:**
- Modify: `native/BlindSoldier.VersionProxy/app_loader_readiness.h`
- Modify: `native/BlindSoldier.VersionProxy/app_loader_readiness.cpp`
- Modify: `native/BlindSoldier.VersionProxy/version_proxy.cpp`
- Modify: `native/BlindSoldier.WinMMProxy/proxy_state.h`
- Modify: `native/BlindSoldier.WinMMProxy/proxy_state.cpp`
- Modify: `native/BlindSoldier.WinMMProxy.Tests/proxy_tests.cpp`
- Modify: `native/BlindSoldier.WinMMProxy.Tests/version_forwarding_smoke.cpp`

**Interfaces:**
- Produces:

```cpp
struct StockRuntimeReadinessResult {
    bool ready = false;
    bool seventhHeaven = false;
    std::wstring diagnostic;
};

StockRuntimeReadinessResult WaitForStockRuntimeReadiness(
    const fs::path& processImage,
    const HostValidationResult& host,
    Logger& log,
    DWORD pollMilliseconds = 25,
    ULONGLONG timeoutMilliseconds = 120000);
```

- `ProxyBootstrapHooks` gains:

```cpp
std::function<StockRuntimeReadinessResult(
    const fs::path&, const HostValidationResult&, Logger&)>
    waitForStockRuntime;
```

- `ProxyBootstrapContext` gains `bool requireStockRuntimeReadiness = false;`.

- [ ] **Step 1: Write failing integration-boundary tests**

Extend `CoordinateProxyBootstrap` fixtures so a Version context:

- calls `waitForStockRuntime` after host validation;
- does not apply the private runtime or start the broker while readiness fails;
- starts the broker exactly once after readiness succeeds;
- returns the readiness diagnostic on timeout;
- leaves the historical WinMM test context ungated because it is no longer shipped.

Update the forwarding smoke test so it verifies all 17 APIs and proves no event named `Local\BlindSoldier.ManagedReady.<pid>` is created.

- [ ] **Step 2: Run the native proxy suite and verify failure**

Run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.WinMMProxy.Tests.ps1
```

Expected: FAIL because the coordination hook and real readiness waiter are absent.

- [ ] **Step 3: Implement stock-loader detection and log polling**

Detection must require:

```text
<game-dir>\dinput.dll                 loaded from this exact path
<game-dir>\AppProxy.runtimeconfig.json
<game-dir>\AppProxy.dll
<game-dir>\AppWrapper.dll
<game-dir>\nethost.dll
```

Use `GetModuleHandleW`, `GetModuleFileNameW`, canonical same-directory comparison, ordinary-file checks that reject reparse points, `GetProcessTimes`, and `CreateFileW` with `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE`. Read at most 4 MiB from the log, because only the tail containing the last init section is relevant. Use the executable parent, never the process current directory.

Log only state changes. Do not log every 25 ms poll.

- [ ] **Step 4: Call the gate before private-runtime setup and broker launch**

`BootstrapMonitor` passes a Version-only context. `CoordinateProxyBootstrap` validates root and host first, then calls the stock-runtime gate, then applies the private .NET environment and starts the broker. Direct legacy readiness continues immediately after the 3000 ms discovery interval.

- [ ] **Step 5: Remove the unused native ManagedReady contract**

Delete:

- `g_managedReadyEvent`;
- `CreateManagedReadyEvent` and `CloseManagedReadyEvent`;
- `BuildManagedReadyEventName`;
- all DllMain creation/detach handling; and
- all event-specific test code.

Preserve `Local\BlindSoldier.Ready.<launchId>`, which is the functioning proxy-to-broker event.

- [ ] **Step 6: Run native tests and verify success**

Run the command from Step 2. Expected: all tests pass and forwarding smoke confirms the proxy plus a distinct system implementation are loaded.

- [ ] **Step 7: Commit the integrated gate**

```powershell
git add native/BlindSoldier.VersionProxy native/BlindSoldier.WinMMProxy/proxy_state.h native/BlindSoldier.WinMMProxy/proxy_state.cpp native/BlindSoldier.WinMMProxy.Tests/proxy_tests.cpp native/BlindSoldier.WinMMProxy.Tests/version_forwarding_smoke.cpp
git commit -m "fix: wait for stock AppLoader before Reloaded"
```

---

### Task 3: Remove the obsolete managed 7th Heaven workaround and add startup evidence

**Files:**
- Delete: `Ff7.Accessibility.Reloaded/LegacySeventhHeavenRuntimeCompatibility.cs`
- Delete: `Ff7.Accessibility.Reloaded.Tests/LegacySeventhHeavenRuntimeCompatibilityTests.cs`
- Create: `Ff7.Accessibility.Reloaded/Runtime/LegacyStartupDiagnostics.cs`
- Create: `Ff7.Accessibility.Reloaded.Tests/LegacyStartupDiagnosticsTests.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/NavigationProgressControlTests.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

**Interfaces:**
- Produces:

```csharp
internal readonly record struct LegacyStartupSnapshot(
    bool Is64Bit,
    IReadOnlyList<string> NativeModules,
    IReadOnlyList<string> ManagedAssemblies);

internal static class LegacyStartupDiagnostics
{
    internal static string Classify(LegacyStartupSnapshot snapshot);
    internal static LegacyStartupSnapshot Capture();
}
```

- Classifications are exactly `stock-7h-ffnx-late-attach`, `direct-reloaded`, and `partial-unexpected`.

- [ ] **Step 1: Write failing managed tests**

Test pure snapshots for all three classifications. Add a source/artifact assertion that constructing the mod does not set `System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization` and production source contains no `BlindSoldier.ManagedReady`.

- [ ] **Step 2: Run the focused managed test and verify it fails**

Run:

```powershell
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release -- --7h-compatibility-only
```

Expected: FAIL until the new diagnostic-only test mode and class exist.

- [ ] **Step 3: Remove the workaround**

Delete the constructor-time BinaryFormatter switch and ManagedReady signal. Stock 7th Heaven already hosts .NET 9 using `AppProxy.runtimeconfig.json`, which enables the required compatibility property before AppWrapper deserializes its profile; Blind Soldier cannot improve that after the fact.

- [ ] **Step 4: Log startup order after Reloaded supplies its logger**

In `Start` and `StartEx`, after assigning `logger` and before `StartWithRuntimeOwnership`, capture and log:

- PID and x86/x64;
- module paths/versions for local dinput/AppLoader, recognized FFNx, coreclr, hostfxr, and Reloaded;
- loaded AppProxy/AppWrapper managed assemblies;
- startup classification;
- runtime-lease acquisition result; and
- a final `hooks and Prism speech backend initialized` record after `StartCore` returns.

Do not enumerate unrelated modules into the log.

- [ ] **Step 5: Run focused and full managed tests**

Run:

```powershell
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release -- --7h-compatibility-only
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release
```

Expected: both exit 0; the runtime-lease tests still prove only one active speech pipeline.

- [ ] **Step 6: Commit managed startup cleanup**

```powershell
git add Ff7.Accessibility.Reloaded Ff7.Accessibility.Reloaded.Tests
git commit -m "fix: let stock 7th Heaven own managed startup"
```

---

### Task 4: Harden the real Windows Version-library fallback

**Files:**
- Modify: `native/BlindSoldier.VersionProxy/version_proxy.cpp`
- Modify: `native/BlindSoldier.WinMMProxy.Tests/version_forwarding_smoke.cpp`
- Modify: `native/BlindSoldier.WinMMProxy.Tests.ps1`

**Interfaces:**
- The cache filename becomes `version-system-x86-<UPPERCASE_SHA256>.dll`.
- Production validation reuses `InspectPeImage` and `ComputeSha256` from `native/BlindSoldier.Common`.

- [ ] **Step 1: Write failing cache-integrity tests**

Add test seams or a fixture executable covering:

- same-size corrupt cached file;
- wrong-machine PE candidate;
- reparse-point collision;
- source system DLL change resulting in a new content-addressed name;
- concurrent identical publication; and
- successful fallback that loads both the proxy and a distinct validated implementation.

- [ ] **Step 2: Run native proxy tests and verify failure**

Run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.WinMMProxy.Tests.ps1
```

Expected: the same-size corruption case fails because current code checks only length.

- [ ] **Step 3: Implement atomic content-addressed caching**

Read and validate the source x86 system DLL, compute SHA-256, reject reparse points, copy to a unique same-directory temporary name, verify byte hash and PE machine, then publish with `MoveFileExW(..., MOVEFILE_WRITE_THROUGH)`. If another process wins with `ERROR_ALREADY_EXISTS`, delete the temporary file and validate the winner before loading it. Never ship a Windows DLL.

- [ ] **Step 4: Run native tests and forwarding smoke**

Run the command from Step 2. Expected: all cases pass and the release proxy remains static-CRT x86 with exactly 17 exports.

- [ ] **Step 5: Commit cache hardening**

```powershell
git add native/BlindSoldier.VersionProxy/version_proxy.cpp native/BlindSoldier.WinMMProxy.Tests/version_forwarding_smoke.cpp native/BlindSoldier.WinMMProxy.Tests.ps1
git commit -m "fix: verify system Version proxy cache"
```

---

### Task 5: Make the portable ZIP own only Blind Soldier files

**Files:**
- Modify: `Build-BlindSoldierPortablePackage.Tests.ps1`
- Modify: `Build-BlindSoldierPortablePackage.ps1`
- Modify: `Verify-BlindSoldierPortablePackage.ps1`
- Modify: `installer-dependencies/THIRD-PARTY-NOTICES.md`
- Modify: `README.md`
- Create: `docs/releases/v0.1.6.md`
- Modify: `docs/superpowers/specs/2026-08-06-bundle-ffnx-for-7th-heaven-design.md`

**Interfaces:**
- `Build-BlindSoldierPortablePackage.ps1` no longer consumes or copies the `ffnx` prerequisite subtree.
- `portable-manifest.json` contains no 7th Heaven/FFNx-owned member.

- [ ] **Step 1: Change package tests first**

Replace positive FFNx assertions with negative assertions that reject these anywhere in the ZIP:

```powershell
$forbiddenExternal = @(
    'AF3DN.P','AF4DN.P','FFNx.toml','steam_api.dll','dinput.dll',
    'AppProxy.dll','AppProxy.runtimeconfig.json','AppWrapper.dll','nethost.dll',
    'winmm.dll'
)
```

Retain positive checks for four identical `.local/version.dll` copies, both Blind Soldier brokers, Reloaded loaders, Shared Hooks, Prism, mod assemblies, assets, portable mode, and launcher files.

- [ ] **Step 2: Run package tests and verify they fail**

Run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSoldierPortablePackage.Tests.ps1
```

Expected: FAIL because the builder still copies FFNx into `ff7\workingdir`.

- [ ] **Step 3: Remove FFNx copying and ownership claims**

Delete the `prerequisites\ffnx` copy/verification block. Update generated instructions and README to state that official 7th Heaven manages FFNx, Blind Soldier works beside that stock installation, and extracting Blind Soldier never overwrites it. Mark the bundle-FFNx design `Superseded by the stock 7th Heaven bootstrap design on 2026-08-06`.

- [ ] **Step 4: Update notices and v0.1.6 release notes**

Remove notices that apply only to no-longer-shipped FFNx payloads. Document the Version proxy, stock AppLoader boundary, no-special-7H-build requirement, Prism preservation, and rollback by removing only manifest-owned files.

- [ ] **Step 5: Run build and verifier tests**

Run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSoldierPortablePackage.Tests.ps1
```

Expected: exit 0 and two deterministic fixture builds have identical member lists and hashes.

- [ ] **Step 6: Commit package ownership correction**

```powershell
git add Build-BlindSoldierPortablePackage.ps1 Build-BlindSoldierPortablePackage.Tests.ps1 Verify-BlindSoldierPortablePackage.ps1 installer-dependencies/THIRD-PARTY-NOTICES.md README.md docs/releases/v0.1.6.md docs/superpowers/specs/2026-08-06-bundle-ffnx-for-7th-heaven-design.md
git commit -m "build: preserve stock 7th Heaven and FFNx"
```

---

### Task 6: Align staging, release, and Ghidra gates with Version startup

**Files:**
- Modify: `tools/Stage-BlindSoldierPortableLiveTest.Tests.ps1`
- Modify: `tools/Stage-BlindSoldierPortableLiveTest.ps1`
- Modify: `tools/Invoke-BlindSoldierGhidraVerification.Tests.ps1`
- Modify: `tools/Invoke-BlindSoldierGhidraVerification.ps1`
- Modify: `Run-DualRuntimeVerification.Tests.ps1`
- Modify: `Run-DualRuntimeVerification.ps1`
- Modify: `native/BlindSoldier.Native.Tests.ps1`
- Modify: `.github/workflows/release.yml`
- Modify: `launcher/Ff7.Launcher.Accessible/Properties/AssemblyInfo.cs`

**Interfaces:**
- Every release/staging default uses version `0.1.6`.
- Ghidra verification accepts `native/BlindSoldier.VersionProxy/bin/Release/Win32/version.dll`.

- [ ] **Step 1: Write failing orchestration tests**

Require:

- staging collision checks for the four `.local\version.dll` files;
- no staging action against FFNx/7H files;
- Ghidra script references Version proxy and exact 17 exports, not WinMM export evidence;
- release gate runs the focused stock-startup managed tests even without licensed game data;
- build/verify use `0.1.6`; and
- x64 payload paths remain the existing approved paths.

- [ ] **Step 2: Run orchestration tests and verify failure**

Run:

```powershell
Import-Module Pester -RequiredVersion 4.10.1 -Force
Invoke-Pester .\tools\Stage-BlindSoldierPortableLiveTest.Tests.ps1 -EnableExit
Invoke-Pester .\tools\Invoke-BlindSoldierGhidraVerification.Tests.ps1 -EnableExit
Invoke-Pester .\Run-DualRuntimeVerification.Tests.ps1 -EnableExit
```

Expected: failures name stale WinMM paths and version `0.1.5`.

- [ ] **Step 3: Update staging and release orchestration**

Stage only manifest-owned Blind Soldier files. Refuse to overwrite an unknown Version proxy. Snapshot 7th Heaven/FFNx hashes before staging and compare them afterward. Add `--7h-compatibility-only` to portable managed verification. Rename user-facing gate labels from `WinMMProxy.Tests` to `NativeProxy.Tests` while retaining the existing script filename for compatibility.

- [ ] **Step 4: Update the Ghidra gate**

The final headless analysis must verify:

- x86 PE and exact 17 Version exports;
- system Version load plus hardened private-cache fallback;
- AppLoader module/file/marker strings and 120000 ms timeout;
- host/root validation and broker thread;
- no registry mutation;
- no WinMM forwarding surface;
- no embedded 7th Heaven/FFNx implementation; and
- remote-injection imports remain absent from `version.dll` and present only in the broker.

- [ ] **Step 5: Run the three orchestration suites again**

Run the commands from Step 2. Expected: all pass.

- [ ] **Step 6: Commit verification alignment**

```powershell
git add tools Run-DualRuntimeVerification.ps1 Run-DualRuntimeVerification.Tests.ps1 native/BlindSoldier.Native.Tests.ps1 .github/workflows/release.yml launcher/Ff7.Launcher.Accessible/Properties/AssemblyInfo.cs
git commit -m "test: verify stock 7th Heaven bootstrap"
```

---

### Task 7: Run the complete release gate and build the candidate ZIP

**Files:**
- Modify only if a test exposes an in-scope defect in Tasks 1–6.
- Create through build tooling: `artifacts/release/Blind-Soldier-Portable.zip`
- Create through build tooling: `artifacts/release/Blind-Soldier-Portable.zip.sha256`
- Create: `docs/validation/v0.1.6-stock-7h-live-matrix.md`

**Interfaces:**
- Candidate version is `0.1.6`.

- [ ] **Step 1: Run whitespace and focused suites**

```powershell
git diff --check
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.WinMMProxy.Tests.ps1
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release -- --7h-compatibility-only
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSoldierPortablePackage.Tests.ps1
```

Expected: all exit 0.

- [ ] **Step 2: Run the complete dual-runtime gate**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Run-DualRuntimeVerification.ps1
```

Expected: every managed, native, launcher, package, and Ghidra stage passes.

- [ ] **Step 3: Build and verify the release candidate**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSoldierPortablePackage.ps1 -OutputPath .\artifacts\release\Blind-Soldier-Portable.zip -Version 0.1.6
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Verify-BlindSoldierPortablePackage.ps1 -ArchivePath .\artifacts\release\Blind-Soldier-Portable.zip -ExpectedVersion 0.1.6
```

Write the SHA-256 file from the verified archive hash. Record archive path, size, hash, Version-proxy hash, 17-export count, and x64 non-regression hashes in the validation matrix.

- [ ] **Step 4: Run final Ghidra analysis**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\tools\Invoke-BlindSoldierGhidraVerification.ps1
```

Expected: a current Ghidra report for `version.dll` and both brokers, with no obsolete WinMM target.

- [ ] **Step 5: Commit build-gate evidence**

```powershell
git add docs/validation/v0.1.6-stock-7h-live-matrix.md
git commit -m "test: record v0.1.6 stock loader evidence"
```

Do not commit generated ZIPs or build output unless repository policy explicitly tracks them.

---

### Task 8: Stage and live-test against untouched stock 7th Heaven

**Files:**
- Runtime deployment only under the user-selected FF7 game root.
- Update: `docs/validation/v0.1.6-stock-7h-live-matrix.md`

**Interfaces:**
- Input: verified `artifacts/release/Blind-Soldier-Portable.zip`.
- Output: deployed Blind Soldier files plus captured logs/hashes; no 7th Heaven/FFNx mutation.

- [ ] **Step 1: Establish a stock 7th Heaven baseline**

Record the 7th Heaven tag/release identity and SHA-256 for `dinput.dll`, `AppProxy.dll`, `AppProxy.runtimeconfig.json`, `AppWrapper.dll`, `nethost.dll`, `AF3DN.P`, `AF4DN.P`, `FFNx.toml`, and `steam_api.dll`. Confirm none are Blind Soldier private builds.

- [ ] **Step 2: Stage the verified ZIP without touching external ownership**

Run the repository stager against the actual game root. It must back up only an earlier Blind Soldier-owned Version proxy, refuse unknown collisions, and copy only files listed in the new portable manifest.

- [ ] **Step 3: Re-hash 7th Heaven and FFNx immediately after staging**

Expected: every baseline hash is identical.

- [ ] **Step 4: Launch stock 7th Heaven with no optional IRO gameplay mod**

Verify from logs and live behavior:

- current-run AppLoader init then success marker;
- `.7thWrapperProfile` consumed;
- Version proxy starts broker after that marker;
- one `coreclr.dll` runtime is present;
- Reloaded loads Shared Hooks and Blind Soldier once;
- Prism speaks title choices on the initial window;
- menu sounds, speech, navigation, field dialogue, battle, and audio descriptions work.

- [ ] **Step 5: Launch with an enabled IRO and Echo-S**

Verify an IRO override is visibly/semantically active, Echo-S pre-movie pages speak, the opening movie description plays once, later dialogue remains active, and no description or speech is duplicated.

- [ ] **Step 6: Test direct x86 and x64 non-regression**

Direct x86 must start after the 3000 ms discovery interval without waiting for AppLoader. Steam 2026 x64 launcher Play must retain its approved loading, speech, descriptions, and navigation behavior; no x86 Version proxy may load into x64.

- [ ] **Step 7: Test fail-closed and cleanup paths**

In an isolated copy, test missing broker and an intentionally unavailable FFNx/AppLoader completion. The accessible error must name the stage and log, and FF7 must not continue interactively. Test normal exit and forced termination, then prove the prior Reloaded pointer and all 7th Heaven/FFNx hashes are unchanged.

- [ ] **Step 8: Record results and commit live evidence**

Update every row in the validation matrix with timestamp, executable identity, result, log paths, and hashes. Commit only after the behavior was actually observed:

```powershell
git add docs/validation/v0.1.6-stock-7h-live-matrix.md
git commit -m "test: validate stock 7th Heaven integration"
```

---

## Final self-review checklist

- Every requirement in the design and precedence amendment maps to a task above.
- No task asks for a patched 7th Heaven binary or a bundled FFNx file.
- AppLoader readiness is earlier than FFNx field readiness, preserving title speech.
- Direct x86 and x64 have explicit non-regression tests.
- The obsolete ManagedReady contract is removed while the functioning broker-ready event remains.
- Production and package tests prove exactly one active Blind Soldier runtime.
- Ghidra examines the final Version proxy and brokers.
- Live success is not claimed until the user-visible stock launch is observed.
