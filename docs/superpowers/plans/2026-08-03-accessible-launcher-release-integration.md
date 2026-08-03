# Accessible Launcher Release Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish Blind Swordsman `v0.1.0-pre.2` with the verified accessible Steam 2026 launcher installed, repaired, and safely restored by the accessible setup program.

**Architecture:** A strict launcher bundle becomes part of the authenticated runtime ZIP. A focused PowerShell module owns launcher validation, transactional installation, persistent backup migration, repair, and uninstall restoration. Existing C# setup orchestration passes the bundle into deployment and stores a backward-compatible nullable launcher ownership record.

**Tech Stack:** PowerShell 5.1, Pester 5, C#/.NET 8 Windows Forms, JSON manifests, SHA-256, PE/managed assembly inspection, GitHub Actions, GitHub Releases.

## Global Constraints

- The native launcher bundle contains `FFVII_LAUNCHER.exe` version `2.0.0.0`, its configuration, and only the launcher-scoped x86 Prism DLL.
- The verified stock Steam 2026 launcher SHA-256 is `B9CDAD3629703883EFC9D5C7427425CF6A8105746E674E4DD3DF783B4F044AEE`.
- Unknown launcher identities and unsafe paths must fail before mutation.
- Persistent backups live beneath `<Reloaded-II>/AccessibilityBackups`; no development-machine path may enter setup defaults or release metadata.
- Legacy-only installs skip launcher deployment and retain a null launcher state.
- Existing schema-one install state from `v0.1.0-pre.1` must remain readable and uninstallable.
- 7th Heaven and FFNx remain optional and setup must not modify them.
- Release artifacts must be deterministic and the installer remains a keyboard- and screen-reader-accessible Windows executable.

---

### Task 1: Authenticate and package the launcher bundle

**Files:**
- Create: `installer-assets/launcher/FFVII_LAUNCHER.exe`
- Create: `installer-assets/launcher/FFVII_LAUNCHER.exe.config`
- Create: `installer-assets/launcher/launcher-bundle.json`
- Modify: `Build-BlindSwordsmanRelease.ps1`
- Modify: `Build-BlindSwordsmanRelease.Tests.ps1`
- Modify: `installer/BlindSwordsman.Setup.Tests/ReleaseValidationCommand.cs`

**Interfaces:**
- Consumes: current verified launcher build and `Ff7.Accessibility.Reloaded/Native/win-x86/prism.dll`.
- Produces: runtime ZIP directory `launcher/` with a strict bundle manifest and three authenticated files.

- [ ] **Step 1: Add a failing release-builder assertion**

Extend the release test to require exactly these entries:

```powershell
$launcherEntries = @(
    'launcher/FFVII_LAUNCHER.exe',
    'launcher/FFVII_LAUNCHER.exe.config',
    'launcher/launcher-bundle.json',
    'launcher/native/x86/FFVII_LAUNCHER.prism.x86.dll'
)
foreach ($entry in $launcherEntries) {
    Assert-True ($archiveEntries -contains $entry) "Missing launcher entry $entry"
}
```

- [ ] **Step 2: Run the release-builder test and verify RED**

Run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Build-BlindSwordsmanRelease.Tests.ps1
```

Expected: failure naming the first missing `launcher/` entry.

- [ ] **Step 3: Add the verified launcher assets and strict manifest**

Copy the current Release build executable and configuration into
`installer-assets/launcher`. Write `launcher-bundle.json` with exact properties:

```json
{
  "schemaVersion": 1,
  "stockLauncherSha256": "B9CDAD3629703883EFC9D5C7427425CF6A8105746E674E4DD3DF783B4F044AEE",
  "launcher": { "name": "FFVII_LAUNCHER.exe", "size": 6616576, "sha256": "683F704F061D943A976D764233A6B3C290ACF9E5C1B150B7180A03224CA3A912" },
  "config": { "name": "FFVII_LAUNCHER.exe.config", "size": 530, "sha256": "18A078CD4503C948D7A270B3134EB40DF3F1D37D91F74033D2C9CAEFEB04B383" },
  "prism": { "name": "FFVII_LAUNCHER.prism.x86.dll", "size": 687616, "sha256": "BB729FC7E82B2E0CB4A8349E137D97B99A7DCAABEBB26763564DA7CF38DE11CB" },
  "assemblyName": "FFVII_LAUNCHER",
  "assemblyVersion": "2.0.0.0"
}
```

- [ ] **Step 4: Implement build-time launcher validation and staging**

Add a `Copy-ValidatedLauncherBundle` function that rejects unknown manifest
properties, filenames, lengths, hashes, reparse points, extra files, non-x86 PE
images, or incorrect managed identity. Copy Prism from the existing mod source
and require its hash to match the manifest.

- [ ] **Step 5: Require launcher content during setup release validation**

After safe extraction, require `launcher/launcher-bundle.json` and all three
bundle files so an incomplete ZIP cannot reach deployment.

- [ ] **Step 6: Run Task 1 checks GREEN and commit**

Run the release-builder test twice and compare every generated asset hash.
Then commit only Task 1 files:

```powershell
git commit -m "build: package accessible FFVII launcher"
```

### Task 2: Implement launcher lifecycle ownership

**Files:**
- Create: `FF7LauncherInstall.psm1`
- Create: `FF7LauncherInstall.Tests.ps1`

**Interfaces:**
- Produces: `Test-Ff7AccessibleLauncherBundle -BundlePath <string>`.
- Produces: `Install-Ff7AccessibleLauncher -GameRoot <string> -ReloadedRoot <string> -BundlePath <string> [-ValidateOnly]`.
- Produces: `Undo-Ff7AccessibleLauncherTransaction -Result <object>` for same-process deployment rollback.
- Produces: `Restore-Ff7AccessibleLauncherFromState -GameRoot <string> -ReloadedRoot <string> -State <object>` for uninstall.
- Installation result exposes `State`, `Transaction`, and `Changed` properties.

- [ ] **Step 1: Read the test-quality rules**

Read `skills/test-driven-development/writing-good-tests.md` completely before
creating this test file.

- [ ] **Step 2: Write fixture-backed failing lifecycle tests**

Use a temporary game root, temporary Reloaded root, the real packaged launcher
bundle, and a generated minimal x86 PE as the fixture's stock target. Rewrite
only the fixture manifest's stock hash. Cover one behavior per test:

```powershell
It 'installs all launcher files and records verified backups' { }
It 'repairs idempotently while retaining the original backup' { }
It 'refuses an unknown launcher before changing any file' { }
It 'restores stock and removes newly-created files on uninstall' { }
It 'preserves a launcher changed after installation' { }
It 'rolls back every launcher file after a later deployment failure' { }
```

- [ ] **Step 3: Run the module tests and verify RED**

Run:

```powershell
Invoke-Pester .\FF7LauncherInstall.Tests.ps1 -Output Detailed
```

Expected: failure because `FF7LauncherInstall.psm1` and its exported functions do not exist.

- [ ] **Step 4: Implement strict bundle and target validation**

Use exact-property JSON validation, constant-time normalized SHA-256 equality,
PE machine `0x014C` checks, managed assembly identity checks, and
path-containment checks. Validate all bundle and target identities before
creating a backup or directory.

- [ ] **Step 5: Implement transactional install and persistent ownership**

Store persistent backups only under the Reloaded accessibility-backup root.
Write copies through temporary files, verify post-copy hashes, and atomically
replace existing files. Reuse valid ownership from the existing launcher
manifest during repair or update. Keep a transaction snapshot that restores
the exact pre-call state if later deployment fails.

- [ ] **Step 6: Implement state-backed uninstall restoration**

For each owned file, restore an exact verified backup, remove a newly created
file, or preserve a post-install changed file. Remove the launcher manifest
only if its hash matches state. Do not recursively remove the accessibility
directory.

- [ ] **Step 7: Run lifecycle tests GREEN and commit**

Run Pester twice and commit:

```powershell
git commit -m "feat: manage accessible launcher lifecycle"
```

### Task 3: Integrate launcher state with setup and deployment

**Files:**
- Modify: `Install-FF7ReloadedMod.ps1`
- Modify: `Uninstall-FF7ReloadedMod.ps1`
- Modify: `installer/BlindSwordsman.Setup/BlindSwordsman.Setup.csproj`
- Modify: `installer/BlindSwordsman.Setup/EmbeddedResourceBundle.cs`
- Modify: `installer/BlindSwordsman.Setup.Core/SetupOrchestrator.cs`
- Modify: `installer/BlindSwordsman.Setup.Core/InstallState.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/SetupOrchestratorTests.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/InstallStateTests.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/SetupUiTests.cs`
- Modify: `InstallerEntrypoint.Tests.ps1`

**Interfaces:**
- Consumes: extracted runtime `launcher/` path and Task 2 module.
- Produces: nullable `InstalledLauncher` in setup state with generic owned-file records.

- [ ] **Step 1: Add failing setup argument and state tests**

Require x64 deployment arguments to include:

```text
-LauncherBundlePath <absolute extracted launcher directory>
```

Require legacy-only deployment to omit it. Add parsing/serialization tests for
new launcher state and an explicit regression proving the old pre.1 JSON still
parses with `Launcher == null`.

- [ ] **Step 2: Run setup tests and verify RED**

Run:

```powershell
dotnet run --project .\installer\BlindSwordsman.Setup.Tests\BlindSwordsman.Setup.Tests.csproj -c Release
```

Expected: compile or assertion failures naming the missing launcher API/state.

- [ ] **Step 3: Add backward-compatible launcher records**

Add:

```csharp
public sealed record InstalledFileChange(
    string Target, string InstalledSha256, bool Changed,
    string? BackupPath, string? BackupSha256);

public sealed record InstalledLauncher(
    string StockLauncherSha256,
    InstalledFileChange Executable,
    InstalledFileChange Configuration,
    InstalledFileChange Prism,
    string ManifestPath,
    string ManifestSha256);
```

Accept schema-one roots with or without `launcher`; always serialize the
property for new state.

- [ ] **Step 4: Pass and validate the extracted launcher bundle**

Extend `BuildDeploymentArguments` with an optional launcher-bundle path. The
orchestrator requires it for a preflight containing x64, passes it to the
deployment script, and validates that returned launcher targets remain beneath
the selected game root.

- [ ] **Step 5: Embed and invoke the lifecycle module**

Embed `FF7LauncherInstall.psm1` beside the install and uninstall scripts. The
install script performs validate-only before any mutation, installs the
launcher as the final managed component, adds rollback to its existing rollback
list, and emits launcher state. The uninstall script calls the state-backed
restore function when launcher state is present.

- [ ] **Step 6: Run focused C# and PowerShell tests GREEN and commit**

Run setup tests, lifecycle Pester tests, and installer-entrypoint tests. Commit:

```powershell
git commit -m "feat: install accessible launcher with Blind Swordsman"
```

### Task 4: Update setup messaging, documentation, and release version

**Files:**
- Modify: `installer/BlindSwordsman.Setup/SetupForm.cs`
- Modify: `README.md`
- Modify: `docs/installer.md`
- Modify: release tests containing `v0.1.0-pre.1` expectations

**Interfaces:**
- Produces: user-facing `v0.1.0-pre.2` installer and documentation links.

- [ ] **Step 1: Add a failing UI text assertion**

Require the x64 review text to identify the accessible FFVII launcher as a
setup-managed component.

- [ ] **Step 2: Run setup UI tests and verify RED**

Run the setup test project and confirm the missing launcher wording failure.

- [ ] **Step 3: Update UI and documentation**

Explain that setup installs the launcher automatically for Steam 2026, backs
up a recognized prior launcher, and restores it on uninstall. Change the
primary installer URL and release examples to `v0.1.0-pre.2`.

- [ ] **Step 4: Run documentation/static tests GREEN and commit**

Run setup tests, `git diff --check`, and path scans. Commit:

```powershell
git commit -m "docs: document accessible launcher installation"
```

### Task 5: Full verification, deployment, and publication

**Files:**
- Generated: two separate `v0.1.0-pre.2` release artifact directories
- Remote: GitHub branch, main, release, and Actions run

**Interfaces:**
- Consumes: all prior tasks.
- Produces: public `v0.1.0-pre.2` installer and verified update channel.

- [ ] **Step 1: Run the complete repository verification gate**

Run all setup, Pester, dual-runtime, package, launcher, and protected 7th Heaven
checks. Require zero failures.

- [ ] **Step 2: Build twice and prove deterministic artifacts**

Build `v0.1.0-pre.2` into two empty directories and compare all five release
asset hashes and sizes byte-for-byte.

- [ ] **Step 3: Exercise controlled lifecycle behavior**

In fixtures, prove stock install, repair, post-install-change preservation, and
uninstall restoration. On the live installation, run update then repair without
launching the game. Confirm the launcher/config/Prism hashes and confirm 7th
Heaven and FFNx hashes are unchanged.

- [ ] **Step 4: Review, commit, and publish the branch**

Inspect `git status`, staged diff, and verification output. Push
`agent/include-accessible-launcher` and merge the reviewed change into `main`.

- [ ] **Step 5: Publish and verify `v0.1.0-pre.2`**

Create the prerelease, upload all assets, verify the unauthenticated public API,
download setup/runtime/channel assets anonymously, and compare exact hashes.
Wait for the tag-triggered GitHub Actions run to complete successfully.

- [ ] **Step 6: Mark pre.1 superseded and clean up**

Add a short superseded notice and pre.2 link to the pre.1 release notes. After
public verification, remove only the temporary feature worktree and merged
branch.
