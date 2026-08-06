# Registry-Free Direct-Extract Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Blind Soldier 0.1.4 as one copy-and-play ZIP that starts automatically from Steam 2026 launcher Play, a direct legacy x86 launch, or 7th Heaven Play without an installer, administrator access, persistent registry changes, or a separate bootstrap action.

**Architecture:** The accessible Square Enix launcher starts an architecture-matched x64 broker in launch mode. Executable-specific x86 `.local` directories load a complete WinMM forwarding proxy that starts the same broker implementation in attach mode. Both paths validate the game and portable payload, provide a private .NET 9.0.8 runtime to Reloaded, lease and restore Reloaded's per-user pointer, load Shared Hooks before Blind Soldier, and fail closed with a screen-reader-accessible error if accessibility cannot start.

**Tech Stack:** C++20 and Win32, MSVC v143 x86/x64, .NET Framework 4.8 Windows Forms, .NET 8 SDK build tooling, Reloaded-II 1.30.3, Shared Hooks 1.16.3, .NET Windows Desktop Runtime 9.0.8, PowerShell 5.1, Pester, Ghidra 12.1.2, SHA-256/SHA-512, GitHub Actions, GitHub CLI.

## Global Constraints

- The approved design is `docs/superpowers/specs/2026-08-06-registry-free-direct-extract-bootstrap-design.md`; implementation must not reintroduce IFEO, an installer, a required command, an updater, or persistent registry state.
- The user has already approved implementation. Do not stop for routine confirmations. Stop only for a genuinely unsafe collision, a missing copyrighted game fixture that cannot be recovered locally, or a live result that changes the design.
- Do not use GitHub Copilot during this implementation; the user said its allowance is exhausted.
- Version the release as `0.1.4`; keep the separate mod-manager distribution behavior unchanged.
- Support these game identities: stock legacy x86 SHA-256 `4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`, observed 7th Heaven x86 inputs SHA-256 `C1437392C5E4178765FBD238DCC9B33D86D2B97337310131C874F302236E4B6F` and `68CF1B8C1D732CC00A1DDB02CED161F7C94B06680D9E8641A11C7361417375C2`, and Steam 2026 x64 SHA-256 `57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B`.
- Names alone never authorize injection. Stock hosts require an exact digest; generated 7th Heaven hosts require the checked-in structural fingerprint and game-code signatures derived in Ghidra.
- The four packaged x86 proxy DLLs must be byte-identical and exist only at the four approved executable-specific `.local` paths. Do not place a loose `winmm.dll` at either game root.
- Preserve 7th Heaven's `dinput.dll`, FFNx files, settings, and launch flow. A pre-existing unknown `<executable>.local/winmm.dll` is a release-blocking collision; do not overwrite or chain it.
- Bundle private x86 and x64 .NET Windows Desktop 9.0.8 runtimes. A clean machine must not need to execute a runtime installer.
- The native broker and proxy must be statically linked to the MSVC runtime and must not depend on a development-machine PATH.
- Startup is fail-closed. Missing or invalid accessibility files, a wrong architecture, an unsupported host, a pointer conflict, or injection failure must prevent FFVII from becoming interactive.
- Errors must include the cause, a corrective action, and an absolute log path in a standard top-level Windows dialog. The accessible launcher must also send the same error through Prism.
- One launch must produce exactly one Blind Soldier initialization and one audio-description stream.
- Logs are UTF-8, append-safe, stored under `<game root>/Blind-Soldier/Logs`, and correlated by a per-launch GUID.
- No archive member or generated JSON may contain `C:\Users\buu42`, `X:\`, a build staging path, or another absolute development path.
- Pin Ghidra to official release `Ghidra_12.1.2_build`, asset `ghidra_12.1.2_PUBLIC_20260605.zip`, SHA-256 `B62E81A0390618466C019C60D8C2F796CED2509C4C1AEA4A37644A77272CF99D`.
- Treat missing speech or navigation as a failed live test, not a cosmetic defect.

---

### Task 1: Version and reproducibly build the accessible launcher source

**Files:**
- Create: `launcher/Ff7.Launcher.Accessible/` from the verified source tree at `C:\Users\buu42\Documents\FFVII Mod Backups\Blind Soldier development disabled 20260805-032240\x86-game-root\accessibility_prototype\launcher\Ff7.Launcher.Accessible`, excluding `bin/`, `obj/`, and the duplicate `LauncherDependencies/native/x86/FFVII_LAUNCHER.prism.x86.dll`.
- Create: `launcher/Ff7.Launcher.Accessible.Tests/` from the adjacent verified test project, excluding `bin/` and `obj/`.
- Create: `launcher/launcher-bundle.template.json`
- Create: `Build-AccessibleLauncherBundle.ps1`
- Create: `Build-AccessibleLauncherBundle.Tests.ps1`
- Modify: `launcher/Ff7.Launcher.Accessible/FFVII_LAUNCHER.csproj`
- Modify: `Run-DualRuntimeVerification.ps1`

**Interfaces:**
- Consumes: versioned launcher source, NuGet compile-time references, and `Ff7.Accessibility.Reloaded/Native/win-x86/prism.dll`.
- Produces: `FFVII_LAUNCHER.exe`, `FFVII_LAUNCHER.exe.config`, `native/x86/FFVII_LAUNCHER.prism.x86.dll`, and `launcher-bundle.json` beneath a caller-supplied output directory.
- Produces: `Build-AccessibleLauncherBundle.ps1 -OutputPath <absolute directory> [-Configuration Release]`.

- [ ] **Step 1: Import only source inputs and add a failing portability test**

Copy the source/resource files but not compiled output. In `Build-AccessibleLauncherBundle.Tests.ps1`, assert that the project contains no absolute `HintPath`, builds as x86, and emits the four exact bundle files:

```powershell
$projectText = [IO.File]::ReadAllText($project)
$projectText | Should -Not -Match '(?i)<HintPath>[A-Z]:\\'
$result = & $builder -OutputPath $output
$LASTEXITCODE | Should -Be 0
@(
    'FFVII_LAUNCHER.exe',
    'FFVII_LAUNCHER.exe.config',
    'native\x86\FFVII_LAUNCHER.prism.x86.dll',
    'launcher-bundle.json'
) | ForEach-Object { Test-Path -LiteralPath (Join-Path $output $_) | Should -BeTrue }
```

- [ ] **Step 2: Run the imported launcher tests and bundle test RED**

Run:

```powershell
dotnet build .\launcher\Ff7.Launcher.Accessible.Tests\FFVII_LAUNCHER.Accessibility.Tests.csproj -c Release
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-AccessibleLauncherBundle.Tests.ps1
```

Expected: build failure naming the old absolute `X:\...` reference paths and bundle-test failure because the builder does not exist.

- [ ] **Step 3: Replace development paths with pinned compile-time packages**

Replace all absolute assembly references in `FFVII_LAUNCHER.csproj` with these exact package references while retaining x86, .NET Framework 4.8, deterministic output, and original embedded resources:

```xml
<ItemGroup>
  <PackageReference Include="SharpDX" Version="4.2.0" GeneratePathProperty="true" />
  <PackageReference Include="SharpDX.DirectInput" Version="4.2.0" GeneratePathProperty="true" />
  <PackageReference Include="SharpDX.DirectSound" Version="4.2.0" GeneratePathProperty="true" />
  <PackageReference Include="SharpDX.XInput" Version="4.2.0" GeneratePathProperty="true" />
  <PackageReference Include="SharpDX.Direct3D11" Version="4.2.0" GeneratePathProperty="true" />
  <PackageReference Include="SharpDX.DXGI" Version="4.2.0" GeneratePathProperty="true" />
  <PackageReference Include="SharpDX.Desktop" Version="4.2.0" GeneratePathProperty="true" />
  <PackageReference Include="NAudio.Core" Version="2.2.1" GeneratePathProperty="true" />
  <PackageReference Include="NAudio.WinMM" Version="2.2.1" GeneratePathProperty="true" />
  <PackageReference Include="Steamworks.NET" Version="2024.8.0" GeneratePathProperty="true" />
</ItemGroup>
```

Point the Prism content item at `../../Ff7.Accessibility.Reloaded/Native/win-x86/prism.dll` and link it to `launcher_accessibility/native/x86/FFVII_LAUNCHER.prism.x86.dll`. NuGet dependencies may exist in the intermediate build output so the tests can execute; the strict bundle builder copies only the launcher, config, Prism, and manifest and therefore does not redistribute those compile-time packages.

- [ ] **Step 4: Implement the deterministic launcher-bundle builder**

Make the script build the project, validate the launcher and Prism PE machine as `0x014C`, validate managed identity `FFVII_LAUNCHER, Version=2.0.0.0`, copy only the exact four files, and write a strict schema-two manifest from measured lengths and SHA-256 values:

```powershell
$manifest = [ordered]@{
    schemaVersion = 2
    stockLauncherSha256 = 'B9CDAD3629703883EFC9D5C7427425CF6A8105746E674E4DD3DF783B4F044AEE'
    launcher = Get-FileDescriptor $launcherTarget
    config = Get-FileDescriptor $configTarget
    prism = Get-FileDescriptor $prismTarget
    assemblyName = 'FFVII_LAUNCHER'
    assemblyVersion = '2.0.0.0'
}
```

The template contains only `schemaVersion`, `stockLauncherSha256`, `assemblyName`, and `assemblyVersion`; measured file data always comes from the current source build.

- [ ] **Step 5: Keep all current launcher accessibility coverage green**

Run the Release-built test executable, not only `dotnet build`:

```powershell
& .\launcher\Ff7.Launcher.Accessible.Tests\bin\Release\net48\FFVII_LAUNCHER.Accessibility.Tests.exe
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-AccessibleLauncherBundle.Tests.ps1
```

Expected: localized main choices, initial focus, arrow behavior, combo boxes, sliders, Enter, UI Automation, Prism retry, UTF-8, and absolute Prism-path tests all pass.

- [ ] **Step 6: Register the launcher checks and commit**

Add the two commands to `Run-DualRuntimeVerification.ps1`, then commit:

```powershell
git add launcher Build-AccessibleLauncherBundle.ps1 Build-AccessibleLauncherBundle.Tests.ps1 Run-DualRuntimeVerification.ps1
git commit -m "build: version accessible launcher source"
```

### Task 2: Define evidence-backed supported-host identities

**Files:**
- Create: `analysis/native-bootstrap/supported-hosts.json`
- Create: `analysis/native-bootstrap/README.md`
- Create: `analysis/ghidra/BlindSoldierHostEvidence.java`
- Create: `tools/Generate-BlindSoldierHostManifest.ps1`
- Create: `native/BlindSoldier.Common/pe_image.h`
- Create: `native/BlindSoldier.Common/pe_image.cpp`
- Create: `native/BlindSoldier.Common/supported_hosts.h`
- Create: `native/BlindSoldier.Common/supported_hosts.cpp`
- Create: `native/BlindSoldier.Host.Tests/BlindSoldier.Host.Tests.vcxproj`
- Create: `native/BlindSoldier.Host.Tests/host_tests.cpp`
- Modify: `Ff7.Accessibility.Reloaded/Runtime/LegacyX86Fingerprint.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Reloaded/ModConfig.json`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `PeImageInfo InspectPeImage(const fs::path&)` with machine, sections, imports, resources, image base, and bounded file-backed ranges.
- Produces: `HostValidationResult ValidateSupportedHost(const fs::path&, ExpectedHostArchitecture)`.
- Produces: generated C++ constants from `supported-hosts.json`; JSON is the single source of truth for native and managed validators.

- [ ] **Step 1: Write failing native and managed host tests**

Define the public native contract:

```cpp
enum class SupportedHostKind { None, LegacyStockX86, SevenHeavenX86, Steam2026X64 };
enum class ExpectedHostArchitecture { X86, X64 };

struct HostValidationResult {
    SupportedHostKind kind = SupportedHostKind::None;
    bool supported = false;
    std::wstring diagnostic;
    std::wstring sha256;
};

HostValidationResult ValidateSupportedHost(
    const fs::path& executable,
    ExpectedHostArchitecture expectedArchitecture);
```

Cover exact stock acceptance, both allowed 7th Heaven names, x64 acceptance, wrong name, wrong machine, altered exact hash, missing WinMM import, altered game-code signature, malformed PE range, and an embedded manifest that disables `.local` redirection. In the managed tests, require `ff7.exe` and `ff7_en.exe` to resolve to the same legacy runtime only after validation.

- [ ] **Step 2: Run tests RED**

Run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.Native.Tests.ps1
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release
```

Expected: missing host-test project, `ff7.exe` unsupported, and the current single-hash validator failing the new cases.

- [ ] **Step 3: Install pinned Ghidra and derive the structural evidence**

Use the installed 64-bit Microsoft OpenJDK 21.0.11. Download only the official NSA release, verify the pinned SHA-256 before extraction, recover the exact stock x86 executable from the user's licensed Steam installation or verified backup into an untracked local-fixtures directory, and analyze these local binaries:

```text
analysis/native-bootstrap/local-fixtures/ff7_en.exe (must hash to 4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225)
C:\Users\buu42\Tools\7thHeaven\Resources\FF7_1.02_Eng_Patch\ff7.exe
C:\Users\buu42\ff7_accessibility_analysis\input\ff7_en.exe
C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII Steam Edition\FFVII.exe
```

`BlindSoldierHostEvidence.java` must emit machine, image base, section names/RVAs/sizes/flags, imported modules and symbols, manifest-resource presence, and at least three invariant game-code signatures at addresses already used by the legacy accessibility runtime. `Generate-BlindSoldierHostManifest.ps1` writes one exact stock profile, separate compatible structural profiles for the 7th Heaven patch input and converted output, and one exact x64 profile to `supported-hosts.json`; no game executable is committed. Add `analysis/native-bootstrap/local-fixtures/` to `.gitignore` in this task.

- [ ] **Step 4: Implement bounded PE parsing and host validation**

Use checked offset arithmetic for DOS, NT, optional, section, import, and resource tables. The JSON schema has the exact top-level records `schemaVersion`, `legacyStockX86`, `sevenHeavenX86`, and `steam2026X64`. The stock and x64 records contain the fixed name, machine, and digest values from Global Constraints. The 7th Heaven record contains the two names, machine `332`, required `WINMM.DLL` import, `forbidEmbeddedManifest: true`, and a nonempty `profiles` array; each generated profile has its exact section constraints and at least three exact masked code signatures emitted in Step 3.

The generator emits `supported_hosts.generated.h`; production code must not parse mutable JSON at startup.

- [ ] **Step 5: Align Reloaded metadata and managed validation**

Change `SupportedAppId` to the exact ordered set:

```json
"SupportedAppId": ["ff7_en.exe", "ff7.exe", "FFVII.exe"]
```

Replace the managed single-hash implementation with a reader for the generated host constants or a generated C# source file emitted by the same script. Keep diagnostics explicit and fail closed.

- [ ] **Step 6: Run host tests GREEN and commit**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.Native.Tests.ps1
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release
git add analysis/native-bootstrap analysis/ghidra/BlindSoldierHostEvidence.java tools/Generate-BlindSoldierHostManifest.ps1 native/BlindSoldier.Common native/BlindSoldier.Host.Tests Ff7.Accessibility.Reloaded Ff7.Accessibility.Reloaded.Tests .gitignore
git commit -m "feat: validate portable FFVII host identities"
```

### Task 3: Refactor the IFEO launcher into the launch/attach bootstrap broker

**Files:**
- Rename: `native/BlindSoldier.Launcher/` to `native/BlindSoldier.Bootstrap/`
- Rename: `native/BlindSoldier.Launcher.Tests/` to `native/BlindSoldier.Bootstrap.Tests/`
- Create: `native/BlindSoldier.Bootstrap/bootstrap_contract.h`
- Create: `native/BlindSoldier.Bootstrap/bootstrap_contract.cpp`
- Create: `native/BlindSoldier.Bootstrap/reloaded_session.h`
- Create: `native/BlindSoldier.Bootstrap/reloaded_session.cpp`
- Create: `native/BlindSoldier.Bootstrap/process_bootstrap.h`
- Create: `native/BlindSoldier.Bootstrap/process_bootstrap.cpp`
- Create: `native/BlindSoldier.Bootstrap/main.cpp`
- Modify: `native/BlindSoldier.Common/common.h`
- Modify: `native/BlindSoldier.Bootstrap/BlindSoldier.Bootstrap.vcxproj`
- Modify: `native/BlindSoldier.Bootstrap.Tests/bootstrap_tests.cpp`
- Modify: `native/BlindSoldier.Native.Tests.ps1`

**Interfaces:**
- Consumes launch CLI: `--launch --root <root> --game <FFVII.exe> [--game-arguments jp] --launch-id <GUID>`.
- Consumes attach CLI: `--attach --root <root> --game <ff7 path> --pid <PID> --ready-event <Local name> --launch-id <GUID>`.
- Produces x86 and x64 `Blind-Soldier-Bootstrap-<arch>.exe` with a shared exit-code contract.

- [ ] **Step 1: Add failing broker-contract tests**

Define and test these exact types:

```cpp
enum class BootstrapMode { Launch, Attach };
enum class BootstrapExitCode : int {
    Success = 0,
    InvalidArguments = 10,
    UnsupportedHost = 11,
    MissingPayload = 12,
    PointerLeaseUnavailable = 13,
    TargetUnavailable = 14,
    ArchitectureMismatch = 15,
    AppConfigFailed = 16,
    InjectionFailed = 17,
    ResumeFailed = 18,
    RuntimeUnavailable = 19,
    ReadySignalFailed = 20
};

struct BootstrapRequest {
    BootstrapMode mode;
    fs::path packageRoot;
    fs::path gameExecutable;
    DWORD processId = 0;
    std::wstring gameArguments;
    std::wstring readyEventName;
    std::wstring launchId;
};

bool TryParseBootstrapRequest(
    const std::vector<std::wstring>& arguments,
    BootstrapRequest& request,
    std::wstring& error);
BootstrapExitCode RunBootstrap(const BootstrapRequest& request, Logger& log);
```

Test quoting, duplicate switches, missing values, invalid PID/GUID/event name, x64 attach rejection, x86 launch rejection, package root escape, PID/path disagreement, wrong target machine, and explicit absence of an unmodded fallback.

- [ ] **Step 2: Run native tests RED**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.Native.Tests.ps1
```

Expected: missing renamed project and undefined contract types.

- [ ] **Step 3: Extract pointer ownership into a bounded lease**

Rename `AppDataSwap` to `ReloadedPointerLease`, preserve durable backup recovery and external-change ownership checks, and replace the infinite mutex wait with an explicit 20-second timeout:

```cpp
class ReloadedPointerLease {
public:
    ReloadedPointerLease(
        const fs::path& reloadedRoot,
        Logger& log,
        DWORD waitMilliseconds = 20000,
        const fs::path& pointerOverride = {});
    bool Ready() const;
    const std::wstring& Diagnostic() const;
};
```

Open logs with append mode and write a BOM only to a new empty file. Extend tests for no prior pointer, prior pointer, stale owned pointer, stale backup, externally changed pointer, acquisition after release, timeout, normal target exit, and simulated target crash.

- [ ] **Step 4: Implement one strict payload validator**

`ValidatePortablePayload` must require the architecture-matched Reloaded bootstrapper and loader, both mod configs, both architecture-matched assemblies, `portable.txt`, private runtime `host/fxr/9.0.8/hostfxr.dll`, and Prism. It writes `Apps/<lowercase executable>/AppConfig.json` with this exact order:

```json
"EnabledMods": ["reloaded.sharedlib.hooks", "ff7.accessibility.reloaded"],
"SortedMods": ["reloaded.sharedlib.hooks", "ff7.accessibility.reloaded"]
```

It accepts only a package root whose canonical paths contain every required file and never follows a reparse point outside the root.

- [ ] **Step 5: Implement launch mode**

For x64 only: validate first, acquire the pointer lease, set the private runtime environment for the child, create `FFVII.exe` with `CREATE_SUSPENDED`, inject `Reloaded.Mod.Loader.Bootstrapper.dll`, resume, wait for game exit, restore the pointer, and return `Success`. Any failure before resume terminates the suspended child.

```cpp
BootstrapExitCode RunLaunch(const BootstrapRequest& request, Logger& log) {
    ValidatedPayload payload;
    if (!ValidatePortablePayload(request, payload, log))
        return BootstrapExitCode::MissingPayload;
    ReloadedPointerLease lease(payload.reloadedRoot, log);
    if (!lease.Ready()) return BootstrapExitCode::PointerLeaseUnavailable;
    // Create suspended, inject the verified x64 bootstrapper, resume once,
    // wait for the target, and let lease restore exact prior state.
}
```

- [ ] **Step 6: Implement attach mode and proxy handshake**

For x86 only: open the supplied PID with the minimum injection/query/synchronize rights, verify `QueryFullProcessImageNameW` equals `--game`, validate machine and host identity, acquire the pointer lease, inject the x86 bootstrapper, open and set the supplied event only after `LoadLibraryW` succeeds, then wait for target exit before restoring the pointer.

Do not resume, debug, or recreate the already-running target. If the target exits during startup, return `TargetUnavailable` without signaling success.

- [ ] **Step 7: Build architecture-specific statically linked brokers**

Set project output names exactly:

```xml
<PropertyGroup Condition="'$(Platform)'=='Win32'">
  <TargetName>Blind-Soldier-Bootstrap-x86</TargetName>
</PropertyGroup>
<PropertyGroup Condition="'$(Platform)'=='x64'">
  <TargetName>Blind-Soldier-Bootstrap-x64</TargetName>
</PropertyGroup>
```

Keep `/MT` and `/MTd`; remove every IFEO recursion environment variable and every message instructing the user to run an installer.

- [ ] **Step 8: Run native tests GREEN and commit**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.Native.Tests.ps1
git add native
git commit -m "feat: add portable launch and attach broker"
```

### Task 4: Bundle and select private architecture-matched .NET runtimes

**Files:**
- Modify: `installer-dependencies/dependency-lock.json`
- Modify: `Build-BlindSwordsmanPrerequisiteBundle.ps1`
- Modify: `Build-BlindSwordsmanPrerequisiteBundle.Tests.ps1`
- Create: `PortableDotNetRuntime.psm1`
- Create: `PortableDotNetRuntime.Tests.ps1`
- Modify: `native/BlindSoldier.Bootstrap/reloaded_session.h`
- Modify: `native/BlindSoldier.Bootstrap/reloaded_session.cpp`
- Modify: `native/BlindSoldier.Bootstrap.Tests/bootstrap_tests.cpp`

**Interfaces:**
- Produces: `Expand-VerifiedPortableDotNetRuntime -Architecture x86|x64 -Destination <path> -CachePath <path> -LockPath <path>`.
- Produces: `ApplyPrivateDotNetEnvironment(ExpectedHostArchitecture, runtimeRoot)` for the broker and equivalent process-local setup in the proxy.

- [ ] **Step 1: Add failing lock and runtime-closure tests**

Require the dependency lock to contain these official portable archives and require safe extraction to reject absolute paths, `..`, duplicate case-insensitive paths, and reparse-point entries:

```json
"portableArchives": [
  {
    "architecture": "x86",
    "name": "windowsdesktop-runtime-9.0.8-win-x86.zip",
    "url": "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.8/windowsdesktop-runtime-9.0.8-win-x86.zip",
    "sha512": "09A6D9A8AA4BA944C59D8A57703CF1C42CCC86263B7FB07D1D21848E67254623A079CC5599EB5C7E03BA04FACCC3A0E9452706151AF6B7C0A2E75F725BEFA2DC"
  },
  {
    "architecture": "x64",
    "name": "windowsdesktop-runtime-9.0.8-win-x64.zip",
    "url": "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.8/windowsdesktop-runtime-9.0.8-win-x64.zip",
    "sha512": "FFE3055F50F5E57ABA41AD7790044E32D9D73F526A0A0310664E8D936BBBB60CB84C90E4FF0EC12CB726BFC157DF105769A768306B4191FC1D6CC22173F20771"
  }
]
```

- [ ] **Step 2: Run runtime tests RED**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\PortableDotNetRuntime.Tests.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSwordsmanPrerequisiteBundle.Tests.ps1
```

Expected: strict-lock-schema and missing-module failures.

- [ ] **Step 3: Implement verified safe extraction**

Download to a caller-supplied cache only when absent, verify SHA-512 before opening, enumerate every `ZipArchiveEntry`, reject unsafe names before writing anything, then extract into a staging directory and atomically move it into place. Require these files:

```text
dotnet.exe
host/fxr/9.0.8/hostfxr.dll
shared/Microsoft.NETCore.App/9.0.8/coreclr.dll
shared/Microsoft.WindowsDesktop.App/9.0.8/PresentationFramework.dll
LICENSE.txt
ThirdPartyNotices.txt
```

The existing prerequisite installer bundle continues to package its installer EXEs; it accepts but does not copy `portableArchives`, preserving the mod-manager flow.

- [ ] **Step 4: Direct Reloaded to the private runtime without persistent state**

For x64 launch mode, temporarily set `DOTNET_ROOT_X64` and `DOTNET_ROOT` in the broker before `CreateProcessW`, let the child inherit them, then restore the broker's prior values. For x86 attach mode, the proxy sets `DOTNET_ROOT_X86`, `DOTNET_ROOT(x86)`, and `DOTNET_ROOT` inside the FFVII process before the broker injects Reloaded. Never call `SetEnvironmentVariable` with a machine- or user-persistent registry target.

```cpp
bool ApplyPrivateDotNetEnvironment(
    ExpectedHostArchitecture architecture,
    const fs::path& runtimeRoot,
    Logger& log);
```

- [ ] **Step 5: Prove Reloaded's pinned bootstrapper uses environment discovery**

Compile a small native fixture against the pinned `nethost` API, clear inherited global runtime lookup inside that fixture process, set only the package-private architecture variable, and require `get_hostfxr_path(..., nullptr)` to resolve to the staged `host/fxr/9.0.8/hostfxr.dll`. Add the fixture to `PortableDotNetRuntime.Tests.ps1`.

- [ ] **Step 6: Run runtime and broker tests GREEN and commit**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\PortableDotNetRuntime.Tests.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSwordsmanPrerequisiteBundle.Tests.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.Native.Tests.ps1
git add installer-dependencies Build-BlindSwordsmanPrerequisiteBundle.ps1 Build-BlindSwordsmanPrerequisiteBundle.Tests.ps1 PortableDotNetRuntime.psm1 PortableDotNetRuntime.Tests.ps1 native
git commit -m "build: bundle private dotnet runtimes"
```

### Task 5: Implement the guarded full-surface x86 WinMM proxy

**Files:**
- Create: `analysis/native-bootstrap/winmm-exports-10.0.26100.8737.json`
- Create: `tools/Generate-WinmmForwarders.ps1`
- Create: `native/BlindSoldier.WinMMProxy/BlindSoldier.WinMMProxy.vcxproj`
- Create: `native/BlindSoldier.WinMMProxy/proxy.cpp`
- Create: `native/BlindSoldier.WinMMProxy/proxy_state.h`
- Create: `native/BlindSoldier.WinMMProxy/proxy_state.cpp`
- Create: `native/BlindSoldier.WinMMProxy/winmm_exports.inc`
- Create: `native/BlindSoldier.WinMMProxy/winmm.def`
- Create: `native/BlindSoldier.WinMMProxy.Tests/BlindSoldier.WinMMProxy.Tests.vcxproj`
- Create: `native/BlindSoldier.WinMMProxy.Tests/proxy_tests.cpp`
- Create: `native/BlindSoldier.WinMMProxy.Tests/forwarding_smoke.cpp`
- Create: `native/BlindSoldier.WinMMProxy.Tests.ps1`
- Modify: `native/BlindSoldier.Native.Tests.ps1`

**Interfaces:**
- Exports: the 192 named/ordinal exports of `C:\Windows\SysWOW64\winmm.dll` version `10.0.26100.8737`, without extra public exports.
- Starts: `Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe --attach ...` only in a validated supported FFVII host.
- Produces: bounded root discovery and a named ready-event handshake.

- [ ] **Step 1: Generate the locked export manifest and failing export tests**

Use MSVC `dumpbin /exports` against the explicit SysWOW64 library and record file SHA-256 `761E7285BDCA295F82E9EC88FE73D7CF23FBDCB1757F0E043DC701BB3ECD3A51`, file version, ordinal, name, and NONAME status. The test must compare the built proxy's full export table to the checked-in 192-entry manifest and fail on any missing, extra, renamed, or renumbered export.

- [ ] **Step 2: Add failing proxy behavior tests**

Cover:

```text
root .local directory -> package root
ff7/workingdir .local directory -> package root
incomplete root -> rejected
ambiguous complete roots -> rejected
search beyond four parents -> rejected
unsupported host -> forwarding only, no broker
supported synthetic host -> one broker launch
ready event -> first WinMM call continues
broker exit, timeout, or target exit -> accessible error and fail closed
duplicate WinMM calls -> no duplicate broker
```

Use test seams around process start and termination; production has no environment-variable bypass for host validation. An unrelated process name is forwarding-only. A process named `ff7_en.exe` or `ff7.exe` with an invalid fingerprint is a supported-name integrity failure and must show the accessible error and terminate before play.

- [ ] **Step 3: Run proxy tests RED**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.WinMMProxy.Tests.ps1
```

Expected: missing project and proxy output.

- [ ] **Step 4: Generate x86 forwarding stubs for the entire manifest**

`Generate-WinmmForwarders.ps1` writes the `.def` and macro invocations deterministically. Each x86 naked stub preserves flags and all general registers, calls the common readiness gate, restores state, and tail-jumps to its resolved system function:

```cpp
#define BS_WINMM_FORWARD(name, index)               \
extern "C" __declspec(naked) void name() {          \
    __asm {                                          \
        pushfd                                       \
        pushad                                       \
        call EnsureWinmmAndBootstrapReady            \
        popad                                        \
        popfd                                        \
        jmp dword ptr [g_winmmExports + index * 4]   \
    }                                                \
}
```

Resolve every real export with `GetProcAddress` after loading `%WINDIR%\SysWOW64\winmm.dll` via an absolute path built with `GetSystemWow64DirectoryW`; never call unqualified `LoadLibraryW(L"winmm.dll")`.

- [ ] **Step 5: Keep `DllMain` loader-safe and move work to one worker**

`DllMain` stores the module handle, calls `DisableThreadLibraryCalls`, creates synchronization primitives, and creates one worker thread without waiting. The worker validates the host, discovers the root, applies x86 private-runtime environment variables, creates the named event, starts the broker, and waits for either ready or broker exit. The first forwarded call waits at most 30 seconds on the worker result outside the loader lock.

```cpp
enum class ProxyBootstrapState : LONG {
    Pending, ForwardOnly, Ready, Unsupported, Failed, TimedOut
};
```

For an unrelated process, set `ForwardOnly` and preserve normal WinMM behavior. For a supported FFVII name, any invalid fingerprint or non-ready result shows one `MessageBoxW` containing cause/action/log and calls `TerminateProcess` after dismissal. Establish the diagnostic root directly from the bounded `.local` path first so even an incomplete package can write its error under `<candidate root>/Blind-Soldier/Logs`.

- [ ] **Step 6: Prove representative forwarding and no recursion**

Load the proxy into `forwarding_smoke.exe` as an unsupported host and compare `timeGetTime`, `waveOutGetNumDevs`, `midiOutGetNumDevs`, and `mciGetErrorStringW` behavior with the system DLL. Require the proxy log to record the canonical SysWOW64 path and require Process Explorer/module enumeration to show distinct proxy and system WinMM modules.

- [ ] **Step 7: Run proxy tests GREEN and commit**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.WinMMProxy.Tests.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\native\BlindSoldier.Native.Tests.ps1
git add analysis/native-bootstrap tools/Generate-WinmmForwarders.ps1 native
git commit -m "feat: add guarded x86 WinMM bootstrap proxy"
```

### Task 6: Route accessible launcher Play through the x64 broker

**Files:**
- Create: `launcher/Ff7.Launcher.Accessible/FF7_Launcher/BlindSoldierGameLauncher.cs`
- Modify: `launcher/Ff7.Launcher.Accessible/FF7_Launcher/FF7Launcher.cs`
- Modify: `launcher/Ff7.Launcher.Accessible.Tests/Program.cs`
- Modify: `launcher/Ff7.Launcher.Accessible/Properties/AssemblyInfo.cs`

**Interfaces:**
- Produces: `BlindSoldierGameLauncher.TryLaunch(string root, string language, out string accessibleError)`.
- Consumes: adjacent `FFVII.exe` and `Blind-Soldier/Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe`.
- Never directly starts `FFVII.exe`.

- [ ] **Step 1: Add failing launch-boundary tests**

Define a fake process runner and cover exact bootstrap path, root/game quoting, Japanese `jp` forwarding, generated launch GUID, missing bootstrap, missing game, start exception, nonzero bootstrap exit, launcher staying usable, and no direct `FFVII.exe` process start.

```csharp
internal interface IGameProcessRunner
{
    int Run(ProcessStartInfo startInfo);
}

internal sealed class BlindSoldierGameLauncher
{
    internal const string BootstrapRelativePath =
        @"Blind-Soldier\Bootstrap\x64\Blind-Soldier-Bootstrap-x64.exe";

    internal bool TryLaunch(
        string launcherRoot,
        string language,
        out string accessibleError);
}
```

- [ ] **Step 2: Run launcher tests RED**

```powershell
dotnet build .\launcher\Ff7.Launcher.Accessible.Tests\FFVII_LAUNCHER.Accessibility.Tests.csproj -c Release
& .\launcher\Ff7.Launcher.Accessible.Tests\bin\Release\net48\FFVII_LAUNCHER.Accessibility.Tests.exe
```

Expected: missing type and old direct `ProcessStartInfo("FFVII.exe")` assertion failure.

- [ ] **Step 3: Implement exact argument construction and execution**

Use `AppDomain.CurrentDomain.BaseDirectory`, canonicalize both files, set `UseShellExecute=false`, set the working directory to the launcher root, and pass:

```text
--launch --root "<root>" --game "<root>\FFVII.exe" --launch-id <GUID>
```

Append `--game-arguments jp` only for Japanese. Use one Windows command-line quoting function that correctly escapes embedded quotes and trailing backslashes. Wait for the broker because the existing launcher already remains hidden until the game exits.

- [ ] **Step 4: Speak and display launch failures without closing the launcher**

Replace `launch_FF7Launcher()` with the new boundary. On failure:

```csharp
AccessibilitySpeech.ResetDeduplication();
AccessibilitySpeech.Speak(accessibleError, true);
MessageBox.Show(this, accessibleError, Program.Title,
    MessageBoxButtons.OK, MessageBoxIcon.Error);
```

The error text maps every broker exit code to cause, corrective action, and `Blind-Soldier\Logs\Blind-Soldier-Bootstrap-x64-<GUID>.log`. Do not fall back to launching the game.

- [ ] **Step 5: Run all launcher tests GREEN and commit**

```powershell
dotnet build .\launcher\Ff7.Launcher.Accessible.Tests\FFVII_LAUNCHER.Accessibility.Tests.csproj -c Release
& .\launcher\Ff7.Launcher.Accessible.Tests\bin\Release\net48\FFVII_LAUNCHER.Accessibility.Tests.exe
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-AccessibleLauncherBundle.Tests.ps1
git add launcher
git commit -m "feat: bootstrap x64 game from accessible launcher"
```

### Task 7: Replace the installer ZIP with the complete direct-extract layout

**Files:**
- Modify: `Build-BlindSoldierPortablePackage.ps1`
- Modify: `Build-BlindSoldierPortablePackage.Tests.ps1`
- Modify: `Verify-BlindSoldierPortablePackage.ps1`
- Modify: `Build-DualRuntimePackage.ps1`
- Modify: `Build-BlindSwordsmanRelease.ps1`
- Modify: `Build-BlindSwordsmanRelease.Tests.ps1`
- Modify: `FF7LauncherInstall.psm1`
- Modify: `FF7LauncherInstall.Tests.ps1`
- Modify: `Ff7.Accessibility.Reloaded/ModConfig.json`
- Modify: `README.md`
- Create: `docs/releases/v0.1.4.md`
- Delete: `installer-assets/launcher/FFVII_LAUNCHER.exe`
- Delete: `installer-assets/launcher/FFVII_LAUNCHER.exe.config`
- Delete: `installer-assets/launcher/launcher-bundle.json`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Produces: `Blind-Soldier-Portable.zip` and `Blind-Soldier-Portable.zip.sha256` only.
- Consumes: source-built launcher bundle, x86/x64 brokers, x86 proxy, dual-runtime mod, pinned Reloaded/Shared Hooks, and verified private .NET archives.

- [ ] **Step 1: Rewrite package fixtures for the approved layout and verify RED**

Require these exact top-level/package-critical entries:

```powershell
$required = @(
    'FFVII_LAUNCHER.exe',
    'FFVII_LAUNCHER.exe.config',
    'launcher_accessibility/native/x86/FFVII_LAUNCHER.prism.x86.dll',
    'ff7_en.exe.local/winmm.dll',
    'ff7.exe.local/winmm.dll',
    'ff7/workingdir/ff7_en.exe.local/winmm.dll',
    'ff7/workingdir/ff7.exe.local/winmm.dll',
    'Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe',
    'Blind-Soldier/Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe',
    'Blind-Soldier/Runtime/dotnet/x86/host/fxr/9.0.8/hostfxr.dll',
    'Blind-Soldier/Runtime/dotnet/x64/host/fxr/9.0.8/hostfxr.dll',
    'Reloaded-II/portable.txt',
    'Reloaded-II/Mods/ff7.accessibility.reloaded/ModConfig.json',
    'Reloaded-II/Mods/reloaded.sharedlib.hooks/ModConfig.json',
    'LICENSES/dotnet-LICENSE.txt',
    'LICENSES/dotnet-THIRD-PARTY-NOTICES.txt',
    'README-PORTABLE.txt',
    'portable-manifest.json'
)
```

Also assert the archive omits `Blind-Soldier-Installer.exe`, both old `Blind-Soldier-Launcher-*.exe` names, runtime installer EXEs, IFEO instructions, and loose root `winmm.dll`/`dinput.dll`.

- [ ] **Step 2: Run package tests RED**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSoldierPortablePackage.Tests.ps1
```

Expected: old installer/launcher layout differs from every new assertion.

- [ ] **Step 3: Build and stage only the new native components**

Replace `-NativeBinaryPath` with explicit optional inputs:

```powershell
[string] $BootstrapBinaryPath,
[string] $WinmmProxyPath,
[string] $LauncherBundlePath,
[string] $DependencyCachePath,
[string] $DependencyLockPath
```

When omitted, build both broker architectures, the Win32 proxy, and the versioned launcher source. Validate PE machine for every output. Copy the one proxy binary to all four `.local` destinations and require all four SHA-256 hashes to be equal.

- [ ] **Step 4: Stage the private runtime and portable Reloaded configuration**

Use `PortableDotNetRuntime.psm1` to populate `Blind-Soldier/Runtime/dotnet/x86` and `x64`. Create an empty `Reloaded-II/portable.txt`; do not create `ReloadedII.json` inside the archive. Keep the complete pinned loader closure, Apps root, User root, Shared Hooks, dual-runtime mod, assets, and licenses.

- [ ] **Step 5: Write the two-step accessible README**

`README-PORTABLE.txt` begins exactly with:

```text
Blind Soldier 0.1.4

1. Extract every file in this ZIP into your Final Fantasy VII game folder.
2. Start the game normally from Steam or 7th Heaven.
```

Then document supported root and nested layouts, no installer/admin requirement, accessible x64 launcher Play boundary, direct/7th Heaven x86 behavior, unknown `.local\winmm.dll` collision warning, logs, Steam Verify Files launcher rollback, and deletion-based removal. Never instruct the player to run either broker.

- [ ] **Step 6: Make verification reject leakage and architecture errors**

`Verify-BlindSoldierPortablePackage.ps1` must safely extract to a unique temp directory, validate `portable-manifest.json`, verify the sidecar, inspect every PE/ReadyToRun machine, compare proxy hashes and exports, validate both ModIds and ordered application IDs, validate private runtime versions, reject reparse/unsafe ZIP entries, and scan text/JSON for development paths and IFEO terms.

- [ ] **Step 7: Update version and source-built launcher consumers**

Set `ModVersion` and script defaults to `0.1.4`. Make both portable and existing mod-manager release builders call `Build-AccessibleLauncherBundle.ps1` instead of using tracked prebuilt EXEs. Update `FF7LauncherInstall.psm1` and its tests to accept only launcher-bundle schema two while preserving its transactional install/repair/uninstall behavior. Delete the obsolete tracked launcher binaries only after both builders and launcher lifecycle tests pass.

- [ ] **Step 8: Publish only ZIP assets from tag builds**

Change `.github/workflows/release.yml` to build and verify `Blind-Soldier-Portable.zip`, install Java/Ghidra for the static gate, and upload only the ZIP and SHA-256 sidecar as explicit release assets. Do not upload native broker/proxy/installer EXEs separately.

- [ ] **Step 9: Run deterministic package tests GREEN and commit**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSoldierPortablePackage.Tests.ps1
$verificationRoot = Join-Path $env:TEMP ('blind-soldier-portable-plan-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verificationRoot | Out-Null
$archive = Join-Path $verificationRoot 'Blind-Soldier-Portable.zip'
& .\Build-BlindSoldierPortablePackage.ps1 -OutputPath $archive -Version 0.1.4
& .\Verify-BlindSoldierPortablePackage.ps1 -ArchivePath $archive -ExpectedVersion 0.1.4
git add Build-BlindSoldierPortablePackage.ps1 Build-BlindSoldierPortablePackage.Tests.ps1 Verify-BlindSoldierPortablePackage.ps1 Build-DualRuntimePackage.ps1 Build-BlindSwordsmanRelease.ps1 Build-BlindSwordsmanRelease.Tests.ps1 FF7LauncherInstall.psm1 FF7LauncherInstall.Tests.ps1 Ff7.Accessibility.Reloaded/ModConfig.json README.md docs/releases/v0.1.4.md installer-assets/launcher .github/workflows/release.yml
git commit -m "build: create direct-extract portable release"
```

Build twice in distinct clean output directories and require identical member lists, per-member SHA-256 values, ZIP SHA-256 values, and sidecars.

### Task 8: Make Ghidra and full verification release gates

**Files:**
- Replace: `analysis/ghidra/BlindSoldierNativeEvidence.java`
- Create: `analysis/ghidra/BlindSoldierWinmmEvidence.java`
- Create: `analysis/ghidra/BlindSoldierBootstrapEvidence.java`
- Create: `tools/Install-PinnedGhidra.ps1`
- Create: `tools/Invoke-BlindSoldierGhidraVerification.ps1`
- Create: `tools/Invoke-BlindSoldierGhidraVerification.Tests.ps1`
- Modify: `Run-DualRuntimeVerification.ps1`
- Modify: `Run-DualRuntimeVerification.Tests.ps1`
- Modify: `.gitignore`

**Interfaces:**
- Produces: a machine-readable Ghidra evidence report tied to SHA-256 for proxy, x86 broker, x64 broker, and locally available supported hosts.
- Produces: one aggregate verification command that is the release gate.

- [ ] **Step 1: Add failing Ghidra-wrapper tests**

Test wrong Ghidra digest, missing Java, missing program, missing evidence marker, registry-writing import, incomplete WinMM export table, and success. Use tiny fixture PEs so CI does not need copyrighted game binaries.

- [ ] **Step 2: Run Ghidra-wrapper tests RED**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\tools\Invoke-BlindSoldierGhidraVerification.Tests.ps1
```

Expected: missing pinned installer/wrapper/scripts.

- [ ] **Step 3: Implement pinned Ghidra acquisition and headless wrapper**

`Install-PinnedGhidra.ps1` downloads `https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_12.1.2_build/ghidra_12.1.2_PUBLIC_20260605.zip` to `.tools/downloads`, verifies the exact SHA-256, rejects reparse/unsafe ZIP entries, and extracts to `.tools/ghidra_12.1.2_PUBLIC`. Require a 64-bit JDK 21 and have GitHub Actions install Temurin 21. Add only `.tools/` to `.gitignore`.

The wrapper creates a unique temp project, imports one binary at a time, runs the appropriate post-script, writes UTF-8 evidence under `artifacts/ghidra`, and deletes only its verified temp project.

- [ ] **Step 4: Encode positive and negative binary evidence**

Require broker evidence for `CreateProcessW` (x64), `OpenProcess`/`QueryFullProcessImageNameW` (x86), `VirtualAllocEx`, `WriteProcessMemory`, `CreateRemoteThread`, remote `LoadLibraryW`, `CreateMutexW`, `MoveFileExW`, `SetEvent`, and private runtime strings. Require proxy evidence for `GetSystemWow64DirectoryW`, absolute-load construction, `CreateThread`, event/process waits, `MessageBoxW`, `TerminateProcess`, host guard strings, bounded parent count, and all exports.

For every native binary packaged by the portable release, fail if symbols or strings indicate `RegCreateKeyEx`, `RegSetValue`, `Image File Execution Options`, `Debugger`, `/install`, or `/uninstall`. The separate historical/mod-manager installer source is outside this portable-binary scan and is never staged into the ZIP.

- [ ] **Step 5: Extend the aggregate verification matrix**

Add these commands to `Run-DualRuntimeVerification.ps1` after existing managed/parity checks:

```text
AccessibleLauncher.Tests
AccessibleLauncherBundle.Tests
NativeHost.Tests
Bootstrap.Tests x86/x64
WinMMProxy.Tests
PortableDotNetRuntime.Tests
PortablePackage.Tests
PortablePackage.Verify
Ghidra.NativeEvidence
```

The verification summary must return nonzero on the first failed accessibility-critical gate and still print the exact failed command and log path.

- [ ] **Step 6: Run all static/automated checks GREEN and commit**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Run-DualRuntimeVerification.ps1
git add analysis/ghidra tools Run-DualRuntimeVerification.ps1 Run-DualRuntimeVerification.Tests.ps1 .gitignore
git commit -m "test: gate portable bootstrap with Ghidra"
```

### Task 9: Validate and deploy the ZIP through every supported live path

**Files:**
- Create: `tools/Stage-BlindSoldierPortableLiveTest.ps1`
- Create: `tools/Stage-BlindSoldierPortableLiveTest.Tests.ps1`
- Create: `docs/validation/v0.1.4-portable-live-matrix.md`
- Runtime deployment only: isolated copies beneath `C:\Users\buu42\Documents\Blind Soldier Runtime Tests\v0.1.4-<GUID>` and the user's selected live FFVII roots after collision checks.

**Interfaces:**
- Produces: safe, non-registry staging with a manifest of copied/overlaid files and pre-existing hashes.
- Produces: live evidence for direct x86, 7th Heaven x86, nested 7th Heaven x86, and accessible-launcher x64.

- [ ] **Step 1: Add failing staging-safety tests**

Require refusal of a root/drive target, reparse target, unknown `.local\winmm.dll`, mismatched stock launcher, unsafe archive member, or nonempty destination without an ownership snapshot. Require that dry-run reports every overlay and never changes the registry.

- [ ] **Step 2: Implement safe isolated staging**

The script verifies the public ZIP and sidecar first, creates an explicit test directory, copies only the selected game fixture, records pre-existing file hashes, and overlays the archive. It never deletes a source game tree or calls an installer.

- [ ] **Step 3: Run direct legacy x86 validation**

Use both an exact stock legacy executable with SHA-256 `4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225` and the structurally validated converted executable with SHA-256 `68CF1B8C1D732CC00A1DDB02CED161F7C94B06680D9E8641A11C7361417375C2`; start each normally. Recover the stock fixture from the user's licensed Steam installation or its verified backup if it is not currently active. Require before accepting:

```text
proxy host accepted once
x86 broker attach succeeded once
Shared Hooks loaded before Blind Soldier
Blind Soldier initialized once
menu speech and navigation work
footsteps and audio descriptions are not duplicated
game exits normally
ReloadedII.json restored byte-for-byte
```

- [ ] **Step 4: Run 7th Heaven x86 validation in both layouts**

Test a conventional 2013 root and `ff7/workingdir` beneath the 2026 root. Record the hash of 7th Heaven's `dinput.dll` and FFNx configuration before launch. Use 7th Heaven Play, verify selected mods/FFNx and Blind Soldier all load, then verify Blind Soldier did not create, replace, or delete `dinput.dll` or alter FFNx configuration.

- [ ] **Step 5: Run accessible-launcher x64 validation**

Start through Steam/`FFVII_LAUNCHER.exe`, confirm all existing launcher controls read, activate Play, and require x64 broker launch, exactly one mod initialization, opening audio description, menu/dialogue speech, footsteps, and navigation. Confirm direct `FFVII.exe` remains documented as unsupported and is not silently hooked.

- [ ] **Step 6: Exercise fail-closed and pointer recovery cases**

In isolated copies only, test missing Prism, missing mod assembly, wrong-architecture broker, altered host byte, unavailable pointer lease, broker timeout, forced game crash, and changed external Reloaded pointer. Snapshot the Blind Soldier/FFVII IFEO keys and .NET install-location keys before and after; require byte-for-byte registry equality. Each case must show an accessible cause/action/log dialog, prevent inaccessible play, and preserve or restore pointer ownership according to the design.

- [ ] **Step 7: Deploy the verified archive to the user's live root**

Before overlay, run the staging script in dry-run mode against:

```text
C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII Steam Edition
C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII
```

Proceed only for roots that pass host/launcher and collision checks. Store the exact overwritten-file snapshot beneath `C:\Users\buu42\Documents\Blind Soldier Runtime Tests\v0.1.4-live-backup-<GUID>`; this backup is for development recovery and is not part of the public ZIP.

- [ ] **Step 8: Record evidence and commit only documentation/tooling**

Fill `v0.1.4-portable-live-matrix.md` with executable hashes, launch path, proxy/broker log names, initialization count, speech/nav result, pointer before/after hashes, 7th Heaven/FFNx result, and tester result. Do not commit game binaries, user configuration, logs containing personal paths, or the live backup.

```powershell
git add tools/Stage-BlindSoldierPortableLiveTest.ps1 tools/Stage-BlindSoldierPortableLiveTest.Tests.ps1 docs/validation/v0.1.4-portable-live-matrix.md
git commit -m "test: validate portable runtime launch matrix"
```

### Task 10: Final verification and GitHub release

**Files:**
- Modify if evidence requires it: `docs/releases/v0.1.4.md`
- Generated, not committed: `artifacts/release/Blind-Soldier-Portable.zip`
- Generated, not committed: `artifacts/release/Blind-Soldier-Portable.zip.sha256`

**Interfaces:**
- Produces: public GitHub release `v0.1.4` with only the portable ZIP and checksum as binary assets.
- Preserves: source archives automatically supplied by GitHub and the separate mod-manager distribution.

- [ ] **Step 1: Run the complete release gate from a clean worktree**

```powershell
git status --short
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Run-DualRuntimeVerification.ps1
```

Require no untracked generated binaries, no skipped critical check, and no modified source after verification.

- [ ] **Step 2: Build and independently verify final assets**

```powershell
New-Item -ItemType Directory -Path .\artifacts\release -Force | Out-Null
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSoldierPortablePackage.ps1 -OutputPath .\artifacts\release\Blind-Soldier-Portable.zip -Version 0.1.4
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Verify-BlindSoldierPortablePackage.ps1 -ArchivePath .\artifacts\release\Blind-Soldier-Portable.zip -ExpectedVersion 0.1.4
```

Open the archive member list once more and prove there is no standalone installer, broker, proxy, or launcher release asset outside the ZIP.

- [ ] **Step 3: Review the release diff and commit any final release-note correction**

```powershell
git diff --check
git status --short
git log --oneline --decorate -10
```

If release notes changed, commit only those notes with `docs: finalize 0.1.4 portable release notes`.

- [ ] **Step 4: Push source and create the release**

Use the `github:yeet` skill for the publishing boundary. Push `main`, create annotated tag `v0.1.4`, push the tag, and let the verified workflow create the release. If manual fallback is required, use:

```powershell
gh release create v0.1.4 `
  .\artifacts\release\Blind-Soldier-Portable.zip `
  .\artifacts\release\Blind-Soldier-Portable.zip.sha256 `
  --repo buu420/blind-soldier `
  --title "Blind Soldier 0.1.4" `
  --notes-file .\docs\releases\v0.1.4.md
```

- [ ] **Step 5: Verify the public release, not only the local build**

Download both assets from GitHub into a fresh temp directory, verify the SHA-256 sidecar, run `Verify-BlindSoldierPortablePackage.ps1` against the downloaded ZIP, and confirm the release has no obsolete installer/launcher EXE assets. Report the direct download URL only after this check passes.

## Final Self-Review Checklist

- [ ] Every design acceptance criterion maps to a task and live check.
- [ ] The x64 launcher path and both x86 `.local` layouts are covered.
- [ ] The four proxy copies are byte-identical and no root proxy exists.
- [ ] Private x86/x64 .NET runtimes make extraction dependency-closed.
- [ ] `ff7.exe`, `ff7_en.exe`, and `FFVII.exe` metadata agree across native validation, managed validation, Reloaded AppConfig, and ModConfig.
- [ ] The broker owns and restores the Reloaded pointer for the entire game lifetime.
- [ ] No failure path launches unmodded FFVII.
- [ ] Errors are spoken/shown and include cause, action, and log path.
- [ ] 7th Heaven `dinput.dll` and FFNx ownership are preserved.
- [ ] Ghidra checks positive bootstrap/proxy behavior and absence of registry-writing behavior.
- [ ] Package builds are deterministic, path-clean, architecture-correct, and sidecar-verified.
- [ ] One launch produces one accessibility initialization and no duplicate descriptions.
- [ ] The release contains only the ZIP and checksum as explicit downloadable binaries.
