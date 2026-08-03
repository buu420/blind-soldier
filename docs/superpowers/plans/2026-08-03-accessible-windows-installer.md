# Blind Swordsman Accessible Windows Installer Implementation Plan

> **Execution:** Implement inline in this isolated worktree. Use test-first
> iterations and do not pause for additional design approvals; the user
> explicitly approved the design and continuous execution.

**Goal:** Publish a conventional, screen-reader-accessible Windows setup EXE
that discovers supported FFVII installs, validates Reloaded-II, downloads and
verifies a GitHub release payload, performs install/update/repair/uninstall,
and checks for future updates.

**Architecture:** A self-contained .NET 8 WinForms front end uses a testable
installer-core library. It embeds the repository's hardened PowerShell
preflight/deployment scripts, downloads only release artifacts, verifies
manifests and SHA-256 values, and records per-user install state. Existing
PowerShell identity/collision/rollback functions remain authoritative. A
deterministic PowerShell release builder produces the setup, runtime ZIP,
channel manifest, and hashes for GitHub Releases.

**Tech Stack:** C#/.NET 8, WinForms, Windows UI Automation, PowerShell 5.1,
Pester, GitHub Actions, GitHub CLI, GitHub Releases API.

---

## Task 1: Create installer projects and release contracts

**Files:**

- Create: `installer/BlindSwordsman.Setup.Core/BlindSwordsman.Setup.Core.csproj`
- Create: `installer/BlindSwordsman.Setup.Core/ReleaseChannel.cs`
- Create: `installer/BlindSwordsman.Setup.Core/ReleaseManifestParser.cs`
- Create: `installer/BlindSwordsman.Setup.Core/SemanticVersion.cs`
- Create: `installer/BlindSwordsman.Setup.Tests/BlindSwordsman.Setup.Tests.csproj`
- Create: `installer/BlindSwordsman.Setup.Tests/Program.cs`
- Modify: `Run-DualRuntimeVerification.ps1`

- [ ] Write tests that reject missing/unknown manifest fields, malformed
  semantic versions, non-HTTPS URLs, non-GitHub asset hosts, invalid hashes,
  duplicate asset names, and stable/prerelease channel mismatches.
- [ ] Run the installer test project and confirm the new assertions fail.
- [ ] Implement immutable channel/asset contracts, strict JSON parsing, semantic
  version comparison, and channel filtering.
- [ ] Re-run the installer tests and add them to the dual-runtime verification
  command list.
- [ ] Commit: `test: establish installer release contracts`.

## Task 2: Secure release discovery, download, and extraction

**Files:**

- Create: `installer/BlindSwordsman.Setup.Core/GitHubReleaseClient.cs`
- Create: `installer/BlindSwordsman.Setup.Core/ArtifactDownloader.cs`
- Create: `installer/BlindSwordsman.Setup.Core/HashVerifier.cs`
- Create: `installer/BlindSwordsman.Setup.Core/SafeZipExtractor.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/Program.cs`

- [ ] Add failing tests for GitHub API release selection, drafts, stable versus
  prerelease behavior, cancellation, partial downloads, hash mismatch, absolute
  ZIP entries, traversal entries, reparse points, duplicate paths, and payload
  manifest disagreement.
- [ ] Implement an injectable HTTP client, progress reporting, cancellation,
  atomic download completion, fixed-time SHA-256 comparison, and safe extraction
  into a unique temporary directory.
- [ ] Validate every extracted file against `payload-manifest.json` and reject
  unlisted or missing files.
- [ ] Run tests green and commit: `feat: verify GitHub release payloads`.

## Task 3: Add exact preflight and dependency reporting

**Files:**

- Create: `Invoke-BlindSwordsmanPreflight.ps1`
- Create: `Invoke-BlindSwordsmanPreflight.Tests.ps1`
- Create: `installer/BlindSwordsman.Setup.Core/InstallerPaths.cs`
- Create: `installer/BlindSwordsman.Setup.Core/PreflightClient.cs`
- Create: `installer/BlindSwordsman.Setup.Core/DependencyReport.cs`
- Modify: `FF7SteamInstall.psm1`
- Modify: `installer/BlindSwordsman.Setup.Tests/Program.cs`

- [ ] Add Pester fixtures for Steam detection, manual Browse paths, unknown
  executables, absent Reloaded-II, wrong-machine loader DLLs, missing shared
  hooks, and optional 7th Heaven/FFNx states.
- [ ] Add C# tests for command argument quoting, hidden process execution,
  cancellation, structured JSON parsing, and dependency severity ordering.
- [ ] Implement the preflight script using the existing exact FFVII runtime
  validators and read-only Reloaded checks.
- [ ] Embed preflight resources in setup staging and expose a structured report
  without changing the machine.
- [ ] Run both suites green and commit: `feat: add installer preflight checks`.

## Task 4: Support a prebuilt payload in the hardened deployment path

**Files:**

- Modify: `Install-FF7ReloadedMod.ps1`
- Modify: `FF7SteamInstall.psm1`
- Modify: `FF7SteamInstall.Tests.ps1`
- Create: `Uninstall-FF7ReloadedMod.ps1`
- Create: `Uninstall-FF7ReloadedMod.Tests.ps1`

- [ ] Add failing Pester tests proving `-PackagePath` skips source compilation,
  still validates the dual-runtime package, preserves configuration, and writes
  an atomic structured result file.
- [ ] Add failing uninstall tests for created versus pre-existing loaders,
  changed-file preservation, profile ownership, prior-package restoration,
  missing backups, reparse points, and idempotent repeat execution.
- [ ] Refactor deployment into reusable functions without weakening existing PE
  identity, collision, candidate-directory, fingerprint, or rollback checks.
- [ ] Record exact changed files, hashes, profiles, package fingerprint, and
  backup paths in the structured result.
- [ ] Implement cautious state-backed uninstall and run all installer Pester
  tests twice.
- [ ] Commit: `feat: deploy and remove prebuilt release payloads`.

## Task 5: Implement orchestration, state, repair, and Windows registration

**Files:**

- Create: `installer/BlindSwordsman.Setup.Core/InstallState.cs`
- Create: `installer/BlindSwordsman.Setup.Core/InstallStateStore.cs`
- Create: `installer/BlindSwordsman.Setup.Core/SetupMode.cs`
- Create: `installer/BlindSwordsman.Setup.Core/SetupOrchestrator.cs`
- Create: `installer/BlindSwordsman.Setup.Core/WindowsRegistration.cs`
- Create: `installer/BlindSwordsman.Setup.Core/SetupLog.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/Program.cs`

- [ ] Add failing tests for new install, same-version repair, newer-version
  update, downgrade rejection, corrupted state, atomic replacement, rollback,
  update handoff, cautious uninstall, registry values, and Start Menu paths.
- [ ] Implement orchestration behind injected filesystem/process/registry/HTTP
  interfaces so tests use temporary roots only.
- [ ] Persist state beneath local application data, copy the managed setup EXE,
  create the per-user Add or Remove Programs entry and update shortcut, and
  maintain a readable per-run log.
- [ ] Re-run tests green and commit: `feat: orchestrate setup repair and updates`.

## Task 6: Build the accessible WinForms setup application

**Files:**

- Create: `installer/BlindSwordsman.Setup/BlindSwordsman.Setup.csproj`
- Create: `installer/BlindSwordsman.Setup/Program.cs`
- Create: `installer/BlindSwordsman.Setup/SetupApplicationContext.cs`
- Create: `installer/BlindSwordsman.Setup/SetupForm.cs`
- Create: `installer/BlindSwordsman.Setup/SetupPage.cs`
- Create: `installer/BlindSwordsman.Setup/AccessibleNotifier.cs`
- Create: `installer/BlindSwordsman.Setup/Properties/AssemblyInfo.cs`
- Modify: `installer/BlindSwordsman.Setup.Tests/BlindSwordsman.Setup.Tests.csproj`
- Modify: `installer/BlindSwordsman.Setup.Tests/Program.cs`

- [ ] Add Windows-only UI tests that inspect every interactive control for a
  non-empty accessible name, unique mnemonic, logical tab order, keyboard
  activation, focus placement, and text equivalent for progress/errors.
- [ ] Implement the five-page wizard with standard WinForms controls only,
  scalable fonts, high-contrast-compatible system colors, Browse dialogs,
  Back/Next/Install/Cancel buttons, and no visual-only state.
- [ ] Raise native UI Automation notifications for meaningful progress and
  completion changes while keeping the visible status log available.
- [ ] Implement `--uninstall`, `--check-for-updates`, `--local-manifest`, and
  verified update-continuation command-line modes.
- [ ] Publish self-contained `win-x64` single-file output and confirm it runs on
  a machine without a separately installed .NET runtime.
- [ ] Commit: `feat: add accessible Windows setup application`.

## Task 7: Produce deterministic release artifacts and automation

**Files:**

- Create: `Build-BlindSwordsmanRelease.ps1`
- Create: `Build-BlindSwordsmanRelease.Tests.ps1`
- Create: `Publish-BlindSwordsmanRelease.ps1`
- Create: `.github/workflows/release.yml`
- Modify: `.gitignore`
- Modify: `Run-DualRuntimeVerification.ps1`

- [ ] Add failing Pester tests for deterministic staging, payload member hashes,
  asset names, channel schema, setup/payload hash agreement, path safety,
  prerelease flags, and cleanup after failure.
- [ ] Build `Blind-Swordsman-Setup.exe`, `Blind-Swordsman-Runtime.zip`, sidecar
  hashes, and `blind-swordsman-channel.json` from one version/tag input.
- [ ] Validate produced artifacts by parsing the manifest and extracting the
  payload with the same installer core used by setup.
- [ ] Add a Windows GitHub Actions tag workflow and an equivalent local `gh`
  publication script with no embedded credential.
- [ ] Run release-builder tests green and commit:
  `build: add reproducible installer releases`.

## Task 8: Replace manual-first installation documentation

**Files:**

- Modify: `README.md`
- Modify: `Ff7.Accessibility.Reloaded/README.md`
- Create: `docs/installer.md`

- [ ] Make the primary installation action a direct download link to
  `v0.1.0-pre.1/Blind-Swordsman-Setup.exe` and retain the Releases page as a
  fallback.
- [ ] Document detection, Browse fallback, dependency statuses, update/repair,
  Add or Remove Programs, the update shortcut, local/offline payload selection,
  log location, prerelease x64 status, and the unsigned SmartScreen warning.
- [ ] Move PowerShell/source installation into a clearly labeled developer
  section and remove any implication that end users need the SDK.
- [ ] Validate every path/link and commit: `docs: make setup the primary install path`.

## Task 9: Verify, publish, and inspect the public prerelease

**Files:**

- Modify: release notes and generated artifacts only.

- [ ] Run the complete dual-runtime verification in Research mode, including
  installer C# tests and both repeats of all installer Pester tests.
- [ ] Build release `v0.1.0-pre.1` twice and compare content manifests and hashes
  where deterministic inputs permit; inspect PE architecture and single-file
  metadata.
- [ ] Run preflight against this machine and a controlled repair install without
  launching FFVII. Confirm existing 7th Heaven and FFNx settings are unchanged.
- [ ] Exercise keyboard-only setup flow and inspect controls with Windows UI
  Automation; verify screen-reader notification calls do not block setup.
- [ ] Merge the feature branch into `main`, push it, change
  `buu420/blind-swordsman` to public, create the GitHub prerelease with `gh`, and
  upload all verified assets.
- [ ] Query the unauthenticated public GitHub API, download the installer and
  runtime assets to a new temporary directory, verify their hashes, and check
  the README's direct installer link.
- [ ] Report the public installer URL, release URL, verification results, and the
  expected unsigned-publisher warning.
