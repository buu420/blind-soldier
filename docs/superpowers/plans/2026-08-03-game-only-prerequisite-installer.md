# Game-Only Prerequisite Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a supported Final Fantasy VII installation the only external product prerequisite by provisioning Reloaded-II, Shared Hooks, and the required .NET desktop runtimes from Blind Swordsman's verified release payload.

**Architecture:** A locked release-build stage creates `prerequisites/` from exact upstream artifacts. Setup validates that layout and passes it to a focused PowerShell prerequisite module, which installs only the detected architectures before the existing mod/launcher transaction. Preflight reports bundled components as setup-managed, while install state adds a separately tracked legacy profile for safe uninstall.

**Tech Stack:** Windows PowerShell 5.1, Pester, .NET 8 WinForms/C#, deterministic ZIP packaging, GitHub Releases, Ghidra 12 PE validation.

## Global Constraints

- The only user-supplied product prerequisite is a supported Final Fantasy VII installation.
- Reloaded-II is pinned to `1.30.3` and Shared Hooks to `1.16.3`.
- Microsoft .NET Desktop Runtime is pinned to `9.0.8` and installed only for detected x86/x64 runtimes that need it.
- 7th Heaven and FFNx remain optional and their settings/files are not installed or replaced.
- Existing Reloaded `Apps`, `Mods`, `User`, and `Plugins` content must be preserved outside explicitly owned/merged targets.
- Missing or invalid shared prerequisites are repaired transactionally; reparse points and unrelated ownership are rejected.
- Tests must observe the intended failure before each production behavior is added.

---

### Task 1: Locked prerequisite release bundle

**Files:**
- Create: `installer-dependencies/dependency-lock.json`
- Create: `installer-dependencies/THIRD-PARTY-NOTICES.md`
- Create: `installer-dependencies/licenses/Reloaded-II-GPL-3.0.txt`
- Create: `installer-dependencies/licenses/Reloaded-Shared-Hooks-LGPL-3.0.txt`
- Modify: `Build-BlindSwordsmanRelease.Tests.ps1`
- Modify: `Build-BlindSwordsmanRelease.ps1`

**Interfaces:**
- Consumes: exact upstream URL, length, SHA-256/SHA-512, version, and architecture records from `dependency-lock.json`.
- Produces: `<runtime>/prerequisites/dependency-bundle.json`, `reloaded/`, `shared-hooks/`, `dotnet/`, and `notices/`.

- [ ] **Step 1: Add a failing release-builder fixture test**

Add a test-only `PrerequisiteBundleBuilder` that emits marker files, then assert the runtime archive contains the strict prerequisite tree and that omission of any required member fails:

```powershell
$entries | Should Contain 'prerequisites/dependency-bundle.json'
$entries | Should Contain 'prerequisites/reloaded/Reloaded-II.exe'
$entries | Should Contain 'prerequisites/reloaded/_asi_extract/ASILoader32.dll'
$entries | Should Contain 'prerequisites/reloaded/_asi_extract/ASILoader64.dll'
$entries | Should Contain 'prerequisites/shared-hooks/ModConfig.json'
$entries | Should Contain 'prerequisites/dotnet/windowsdesktop-runtime-9.0.8-win-x86.exe'
$entries | Should Contain 'prerequisites/dotnet/windowsdesktop-runtime-9.0.8-win-x64.exe'
```

- [ ] **Step 2: Run the release-builder test and verify RED**

Run:

```powershell
powershell.exe -NoProfile -Command "Invoke-Pester -Script '.\Build-BlindSwordsmanRelease.Tests.ps1' -EnableExit"
```

Expected: FAIL because `Build-BlindSwordsmanRelease.ps1` has no prerequisite builder parameter or payload directory.

- [ ] **Step 3: Add the immutable lock and minimal staging implementation**

Add the parameter and production builder boundary:

```powershell
[Parameter(DontShow=$true)] [scriptblock] $PrerequisiteBundleBuilder

$prerequisiteRoot = Join-Path $runtimeRoot 'prerequisites'
if ($null -ne $PrerequisiteBundleBuilder) {
    & $PrerequisiteBundleBuilder $prerequisiteRoot
}
else {
    New-BlindSwordsmanPrerequisiteBundle `
        -LockPath (Join-Path $scriptRoot 'installer-dependencies\dependency-lock.json') `
        -Destination $prerequisiteRoot
}
Assert-BlindSwordsmanPrerequisiteBundleLayout -Root $prerequisiteRoot
```

The production function downloads exact URLs to a unique staging directory, checks the locked digest before extraction, rejects absolute/parent archive members, extracts Reloaded's nested ASI loader archive and Shared Hooks archive, validates x86/x64 PE machines, copies notices, and moves the coherent tree into place.

- [ ] **Step 4: Run tests and verify GREEN**

Run the command from Step 2. Expected: all release-builder tests pass.

- [ ] **Step 5: Commit**

```powershell
git add installer-dependencies Build-BlindSwordsmanRelease.ps1 Build-BlindSwordsmanRelease.Tests.ps1
git commit -m "build: bundle pinned Reloaded prerequisites"
```

### Task 2: Setup payload validation and deployment plumbing

**Files:**
- Modify: `installer/BlindSwordsman.Setup.Core/ReleasePayloadLayoutValidator.cs`
- Modify: `installer/BlindSwordsman.Setup.Core/SetupOrchestrator.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/ArtifactSecurityTests.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/SetupOrchestratorTests.cs`

**Interfaces:**
- Consumes: the extracted `prerequisites/` tree from Task 1.
- Produces: `ReleasePayloadLayout.PrerequisiteBundlePath` and deployment argument `-PrerequisiteBundlePath <absolute path>`.

- [ ] **Step 1: Write failing C# contract tests**

Assert that a valid layout returns the prerequisite path, a missing manifest fails, and deployment arguments contain the exact path:

```csharp
var layout = ReleasePayloadLayoutValidator.Validate(root);
AssertSamePath(layout.PrerequisiteBundlePath, Path.Combine(root, "prerequisites"));
var args = SetupOrchestrator.BuildDeploymentArguments(report, release, package, result, launcher, layout.PrerequisiteBundlePath);
AssertArgumentPair(args, "-PrerequisiteBundlePath", layout.PrerequisiteBundlePath);
```

- [ ] **Step 2: Run setup tests and verify RED**

```powershell
dotnet run --project .\installer\BlindSwordsman.Setup.Tests\BlindSwordsman.Setup.Tests.csproj -c Release
```

Expected: compile failure because the layout record and method signature do not expose the prerequisite path.

- [ ] **Step 3: Implement the narrow contract**

Change the record to:

```csharp
public sealed record ReleasePayloadLayout(
    string ModPackagePath,
    string LauncherBundlePath,
    string PrerequisiteBundlePath);
```

Require `dependency-bundle.json`, both ASI loaders and bootstrappers, Shared Hooks `ModConfig.json` plus both entry assemblies, both .NET installers, and notices. Pass the validated path through `InstallAsync` and `BuildDeploymentArguments`.

- [ ] **Step 4: Run setup tests and verify GREEN**

Run the Step 2 command. Expected: all setup tests pass.

- [ ] **Step 5: Commit**

```powershell
git add installer/BlindSwordsman.Setup.Core installer/BlindSwordsman.Setup.Tests
git commit -m "feat: validate bundled installer prerequisites"
```

### Task 3: Nonblocking setup-managed preflight

**Files:**
- Modify: `Invoke-BlindSwordsmanPreflight.Tests.ps1`
- Modify: `Invoke-BlindSwordsmanPreflight.ps1`

**Interfaces:**
- Consumes: resolved game architectures and the selected/recommended Reloaded root.
- Produces: `canInstall=true` for a valid game even when Reloaded, loaders, or Shared Hooks are absent; dependency messages say setup will install/repair them.

- [ ] **Step 1: Replace old blocking expectations with failing game-only tests**

Add x86-only, x64-only, and dual-runtime cases with an absent Reloaded root:

```powershell
$report.canInstall | Should Be $true
($report.dependencies | Where-Object id -eq 'reloaded').severity | Should Be 'required'
($report.dependencies | Where-Object id -eq 'reloaded').satisfied | Should Be $true
($report.dependencies | Where-Object id -eq 'reloaded-loaders').message | Should Match 'setup will install'
($report.dependencies | Where-Object id -eq 'shared-hooks').message | Should Match 'setup will install'
```

- [ ] **Step 2: Run preflight tests and verify RED**

```powershell
powershell.exe -NoProfile -Command "Invoke-Pester -Script '.\Invoke-BlindSwordsmanPreflight.Tests.ps1' -EnableExit"
```

Expected: FAIL because the current report makes all three dependencies blocking.

- [ ] **Step 3: Implement setup-managed status**

Keep validation results when components exist, but make missing/repairable components required and satisfied by the bundled setup contract. A present wrong-machine or unrelated-owned target remains blocking because setup must not overwrite it blindly. Compute `canInstall` from the game plus true collision blockers only.

- [ ] **Step 4: Run preflight tests and verify GREEN**

Run the Step 2 command. Expected: all preflight tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Invoke-BlindSwordsmanPreflight.ps1 Invoke-BlindSwordsmanPreflight.Tests.ps1
git commit -m "fix: make Reloaded prerequisites setup-managed"
```

### Task 4: Transactional Reloaded and .NET provisioning module

**Files:**
- Create: `ReloadedPrerequisiteInstall.psm1`
- Create: `ReloadedPrerequisiteInstall.Tests.ps1`
- Modify: `installer/BlindSwordsman.Setup/EmbeddedResourceBundle.cs`
- Modify: `installer/BlindSwordsman.Setup/BlindSwordsman.Setup.csproj`
- Modify: `Run-DualRuntimeVerification.ps1`
- Modify: `Run-DualRuntimeVerification.Tests.ps1`

**Interfaces:**
- Produces: `Install-BlindSwordsmanReloadedPrerequisites -BundlePath -ReloadedRoot -RequiredArchitectures -SettingsPath` returning `ReloadedRoot`, `SettingsPath`, `SharedHooksPath`, `InstalledDotNetArchitectures`, and recovery backups.
- Produces: `Test-BlindSwordsmanDesktopRuntime -Architecture x86|x64 -MinimumVersion 9.0.8`.

- [ ] **Step 1: Write failing module tests**

Cover fresh x86, fresh x64, dual, idempotent repair, existing preference preservation, wrong ModId refusal, reparse-point refusal, forced mid-overlay rollback, .NET skip, and injected .NET installer result validation. Use test-only scriptblocks rather than launching installers:

```powershell
$result = Install-BlindSwordsmanReloadedPrerequisites `
    -BundlePath $bundle -ReloadedRoot $target -RequiredArchitectures @('x86') `
    -SettingsPath $settings -RuntimeProbe $missingProbe -RuntimeInstaller $successfulInstaller
(Test-Path "$target\Loader\X86\Reloaded.Mod.Loader.dll") | Should Be $true
(Test-Path "$target\Loader\X64\Reloaded.Mod.Loader.dll") | Should Be $false
```

- [ ] **Step 2: Run module tests and verify RED**

```powershell
powershell.exe -NoProfile -Command "Invoke-Pester -Script '.\ReloadedPrerequisiteInstall.Tests.ps1' -EnableExit"
```

Expected: FAIL because the module does not exist.

- [ ] **Step 3: Implement validation, provisioning, and rollback**

The exported entry point must:

```powershell
Assert-BlindSwordsmanPrerequisiteBundle -Path $BundlePath
Install-RequiredDesktopRuntimes -Architectures $RequiredArchitectures
Install-ReloadedCoreOverlay -Source "$BundlePath\reloaded" -Target $ReloadedRoot
Install-SharedHooksPackage -Source "$BundlePath\shared-hooks" -ReloadedRoot $ReloadedRoot
Update-ReloadedGlobalSettings -ReloadedRoot $ReloadedRoot -SettingsPath $SettingsPath
```

Preflight every source/destination and digest before mutation. Replace files atomically, write recovery manifests under `AccessibilityBackups`, and restore prior bytes when a test-injected write fails. Accept .NET exit codes `0`, `1641`, and `3010`, then require the post-install runtime probe to pass.

- [ ] **Step 4: Embed the module and gate it**

Add `ReloadedPrerequisiteInstall.psm1` to setup embedded resources and add a `Reloaded prerequisite Pester` command immediately after preflight tests in the Research gate.

- [ ] **Step 5: Run module and setup tests and verify GREEN**

Run:

```powershell
powershell.exe -NoProfile -Command "Invoke-Pester -Script '.\ReloadedPrerequisiteInstall.Tests.ps1' -EnableExit"
dotnet run --project .\installer\BlindSwordsman.Setup.Tests\BlindSwordsman.Setup.Tests.csproj -c Release
```

Expected: both pass.

- [ ] **Step 6: Commit**

```powershell
git add ReloadedPrerequisiteInstall.psm1 ReloadedPrerequisiteInstall.Tests.ps1 Run-DualRuntimeVerification.ps1 Run-DualRuntimeVerification.Tests.ps1 installer/BlindSwordsman.Setup
git commit -m "feat: provision Reloaded and dotnet prerequisites"
```

### Task 5: Fresh legacy profile and install-state lifecycle

**Files:**
- Create: `templates/Ff7.Legacy.Steam.AppConfig.json`
- Modify: `FF7SteamInstall.Tests.ps1`
- Modify: `FF7SteamInstall.psm1`
- Modify: `InstallerEntrypoint.Tests.ps1`
- Modify: `Install-FF7ReloadedMod.ps1`
- Modify: `Uninstall-FF7ReloadedMod.ps1`
- Modify: `installer/BlindSwordsman.Setup.Core/InstallState.cs`
- Modify: `installer/BlindSwordsman.Setup.Core/SetupOrchestrator.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/InstallStateTests.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/SetupOrchestratorTests.cs`
- Modify: `installer/BlindSwordsman.Setup/EmbeddedResourceBundle.cs`
- Modify: `installer/BlindSwordsman.Setup/BlindSwordsman.Setup.csproj`

**Interfaces:**
- Produces: `Install-Ff7LegacyReloadedProfile -ReloadedRoot -LegacyRuntime -TemplatePath [-ValidateOnly]`.
- Produces: install-state schema 2 property `legacyProfile`, while parser remains compatible with schema 1 states.

- [ ] **Step 1: Write failing legacy profile tests**

Assert fresh creation, preservation/ordered insertion into an existing profile, idempotence, wrong-executable refusal, and uninstall restoration:

```powershell
$result = Install-Ff7LegacyReloadedProfile -ReloadedRoot $root -LegacyRuntime $runtime -TemplatePath $template
@($profile.EnabledMods) | Should Be @('existing.mod', 'reloaded.sharedlib.hooks', 'ff7.accessibility.reloaded')
$result.BackupPath | Should Not BeNullOrEmpty
```

- [ ] **Step 2: Run Pester and setup tests and verify RED**

```powershell
powershell.exe -NoProfile -Command "Invoke-Pester -Script '.\FF7SteamInstall.Tests.ps1' -EnableExit"
dotnet run --project .\installer\BlindSwordsman.Setup.Tests\BlindSwordsman.Setup.Tests.csproj -c Release
```

Expected: FAIL because the legacy profile function and schema property do not exist.

- [ ] **Step 3: Implement the profile and schema**

Create the profile from the validated `LegacyRuntime.GameExe` and `RuntimeRoot`. Preserve unrelated enabled/sorted mods and ensure Shared Hooks precedes Blind Swordsman. Add nullable `LegacyProfile` state, serialize schema 2, parse schema 1 as `LegacyProfile=null`, and validate the expected `Apps\Ff7.En.Steam\AppConfig.json` path.

- [ ] **Step 4: Integrate provisioning before existing deployment**

Import `ReloadedPrerequisiteInstall.psm1`, require `-PrerequisiteBundlePath`, provision detected architectures, validate/create the legacy profile before copying x86 loaders, record its backup/hash, and add rollback/uninstall handling equivalent to the native profile.

- [ ] **Step 5: Run targeted suites and verify GREEN**

Run the Step 2 commands plus:

```powershell
powershell.exe -NoProfile -Command "Invoke-Pester -Script '.\InstallerEntrypoint.Tests.ps1' -EnableExit"
```

Expected: all pass for x86-only, x64-only, and dual fixtures.

- [ ] **Step 6: Commit**

```powershell
git add templates FF7SteamInstall.psm1 FF7SteamInstall.Tests.ps1 Install-FF7ReloadedMod.ps1 Uninstall-FF7ReloadedMod.ps1 InstallerEntrypoint.Tests.ps1 installer
git commit -m "feat: configure fresh Reloaded game profiles"
```

### Task 6: Documentation, full verification, and replacement prerelease

**Files:**
- Modify: `README.md`
- Modify: `docs/installer.md`
- Create: `docs/releases/v0.1.0-pre.4.md`
- Modify: `.github/workflows/release.yml` if the locked prerequisite cache/build inputs require it.

**Interfaces:**
- Produces: public `v0.1.0-pre.4` setup, runtime, channel, and hash sidecars.

- [ ] **Step 1: Update user-facing requirements**

State that users install FFVII, run setup, and choose/browse the game when needed. State that setup supplies Reloaded-II, Shared Hooks, and .NET; 7th Heaven/FFNx are optional. Document the portable default path and the larger download.

- [ ] **Step 2: Run Ghidra and PE evidence checks**

Analyze the final staged `ASILoader32.dll`, `ASILoader64.dll`, and both bootstrapper DLLs with Ghidra headless and record x86/x64 processor identities in the release notes.

- [ ] **Step 3: Run the full Research gate**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-DualRuntimeVerification.ps1 -Mode Research
```

Expected: exit `0`, every suite passes, and only the already-known protected 7th Heaven SharpCompress advisory may be emitted.

- [ ] **Step 4: Build twice and compare deterministic artifacts**

```powershell
.\Build-BlindSwordsmanRelease.ps1 -Version 0.1.0-pre.4 -OutputPath .\artifacts\release\v0.1.0-pre.4-build1
.\Build-BlindSwordsmanRelease.ps1 -Version 0.1.0-pre.4 -OutputPath .\artifacts\release\v0.1.0-pre.4-build2
```

Compare SHA-256 for both runtime ZIPs, setups, and channel manifests. Expected: each pair is identical.

- [ ] **Step 5: Perform a controlled game-only fixture install**

Use a temporary supported-runtime fixture with no Reloaded/settings/hooks tree and injected .NET installer probe. Verify the install result contains the correct game/profile/loader paths and no opposite-architecture deployment.

- [ ] **Step 6: Commit, merge, and publish**

```powershell
git add README.md docs .github/workflows/release.yml
git commit -m "docs: publish game-only installer requirements"
git push -u origin fix/bundle-reloaded
```

Merge the reviewed branch to `main`, tag `v0.1.0-pre.4`, publish all verified assets, and mark `v0.1.0-pre.3` superseded.

- [ ] **Step 7: Verify the public release anonymously**

Download `Blind-Swordsman-Setup.exe`, `Blind-Swordsman-Runtime.zip`, and `blind-swordsman-channel.json` from unauthenticated release URLs. Recompute hashes and compare them to the channel and sidecars. Expected: all match and the README direct installer link resolves to `v0.1.0-pre.4`.

