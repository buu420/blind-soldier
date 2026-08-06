# Bundled FFNx for 7th Heaven Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a verified FFNx runtime in the portable 7th Heaven layout and repair the x86 ready-event contract so accessibility attaches to that runtime.

**Architecture:** Extend the existing locked prerequisite bundle with an FFNx Steam subtree, then let the portable builder copy that subtree only into `ff7/workingdir`. Define the ready-event prefix once in the native common layer and consume it from both bootstrap and proxy.

**Tech Stack:** PowerShell 7, Pester 4, deterministic ZIP packaging, native C++/MSBuild, GitHub release assets, Ghidra binary inspection.

## Global Constraints

- FFNx version is pinned to 1.24.3.0 from official tag `1.24.3`.
- The official archive SHA-256 is `2BE45F486974F0979B849D0525EB66427DF62483EC99E9339E9773E9E52AFC0D`.
- FFNx is installed only beneath `ff7/workingdir` in the universal archive.
- `FFNx.pdb` must not ship.
- Native Steam 2026 root files must not be overwritten by the FFNx Steam payload.
- The direct-extract and no-registry behavior remains unchanged.

---

### Task 1: Lock and build the FFNx prerequisite

**Files:**
- Modify: `installer-dependencies/dependency-lock.json`
- Modify: `installer-dependencies/THIRD-PARTY-NOTICES.md`
- Modify: `Build-BlindSwordsmanPrerequisiteBundle.Tests.ps1`
- Modify: `Build-BlindSwordsmanPrerequisiteBundle.ps1`

**Interfaces:**
- Consumes: official `FFNx-Steam-v1.24.3.0.zip` and `COPYING.TXT` metadata.
- Produces: `prerequisites/ffnx/**`, `notices/FFNx-GPL-3.0.txt`, and `dependency-bundle.json.ffnx` provenance.

- [ ] Add fixture metadata and a failing assertion for `ffnx/AF3DN.P`, `ffnx/AF4DN.P`, `ffnx/FFNx.toml`, the FFNx license, and absence of `ffnx/FFNx.pdb`.
- [ ] Run `Invoke-Pester ./Build-BlindSwordsmanPrerequisiteBundle.Tests.ps1` and confirm the new assertion fails because no FFNx subtree exists.
- [ ] Add strict FFNx lock validation, safe ZIP extraction, x86 PE validation, PDB removal, license publication, and provenance output.
- [ ] Rerun the focused Pester file and confirm all tests pass.

### Task 2: Put FFNx in the 7th Heaven portable layout

**Files:**
- Modify: `Build-BlindSoldierPortablePackage.Tests.ps1`
- Modify: `Build-BlindSoldierPortablePackage.ps1`
- Modify: `Verify-BlindSoldierPortablePackage.ps1`

**Interfaces:**
- Consumes: the Task 1 `prerequisites/ffnx` tree.
- Produces: `ff7/workingdir/AF3DN.P`, `AF4DN.P`, `FFNx.toml`, supporting assets, and `LICENSES/FFNx-GPL-3.0.txt`.

- [ ] Add fixture FFNx files and failing assertions for their exact nested placement, architecture, license, and absence at archive root.
- [ ] Run `Invoke-Pester ./Build-BlindSoldierPortablePackage.Tests.ps1` and confirm the new assertion fails.
- [ ] Copy the validated FFNx tree into `ff7/workingdir`, validate the drivers, include its license, and update the portable instructions.
- [ ] Extend the verifier to reject archives missing the required nested FFNx runtime or containing the excluded PDB.
- [ ] Rerun focused portable tests and confirm they pass.

### Task 3: Repair the x86 ready-event contract

**Files:**
- Modify: `native/BlindSoldier.Common/common.h`
- Modify: `native/BlindSoldier.Bootstrap/bootstrap_contract.cpp`
- Modify: `native/BlindSoldier.WinMMProxy/proxy_state.h`
- Modify: `native/BlindSoldier.WinMMProxy/proxy_state.cpp`
- Modify: `native/BlindSoldier.WinMMProxy.Tests/proxy_tests.cpp`

**Interfaces:**
- Produces: `BuildReadyEventName(launchId)` using `Local\\BlindSoldier.Ready.`.
- Consumes: the same shared prefix from bootstrap validation.

- [ ] Add a proxy test that calls `BuildReadyEventName` and expects `Local\\BlindSoldier.Ready.<guid>`.
- [ ] Build/run the proxy tests and confirm the test fails before the helper exists or returns the old prefix.
- [ ] Add the shared prefix and helper, then route proxy creation and bootstrap validation through it.
- [ ] Rebuild and run both native bootstrap and proxy test executables.

### Task 4: Build, launch-test, and publish

**Files:**
- Modify: `README.md` if the public download/install section needs the new bundled dependency called out.
- Create: release artifacts under `artifacts/portable/` or the repository's current release output convention.

**Interfaces:**
- Produces: versioned portable ZIP, `.sha256` sidecar, Git commit, GitHub release asset, and download URL.

- [ ] Run the full PowerShell and .NET/native test suite used by the current release workflow.
- [ ] Build the real package against the pinned FFNx asset and run `Verify-BlindSoldierPortablePackage.ps1`.
- [ ] Extract to a clean fixture and verify file hashes, PE architectures, and absence of `FFNx.pdb`.
- [ ] Launch the x86 converted executable and verify `FFNx.log` plus a successful Blind Soldier ready signal.
- [ ] Launch through 7th Heaven and inspect its application log and Blind Soldier logs for a successful combined startup.
- [ ] Commit, push, publish the new ZIP and checksum, then verify the remote asset hashes.
