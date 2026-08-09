# Blind Soldier 2013 x86 Portable Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build, verify, and publish a deterministic 2013/x86-only Blind Soldier portable ZIP that supports direct launch and stock 7th Heaven/FFNx without shipping 2026 or x64 files.

**Architecture:** Derive the x86 archive from the already verified dual-runtime ZIP, retaining byte-identical shared and x86 files. A dedicated builder writes a profile-specific manifest and deterministic archive; a dedicated verifier enforces the exact x86 boundary and binds copied native binaries to the Ghidra-verified dual source archive.

**Tech Stack:** PowerShell 5.1, Pester 4.10.1, .NET `System.IO.Compression`, PE header inspection, SHA-256 manifests, GitHub Actions, GitHub CLI.

## Global Constraints

- The archive name is `Blind-Soldier-2013-x86-Portable.zip`.
- The version remains `0.2.1-beta.1` for the current release asset.
- Direct Steam 2013 and unmodified 7th Heaven/FFNx launches are both supported.
- No x64, Steam 2026 launcher, nested `ff7/workingdir`, FFNx, 7th Heaven, game executable, or game-data file may be present.
- Shared accessibility assets and all required x86 dependencies remain present.
- The source dual archive is never modified and existing output files are never overwritten.
- Release assets include both ZIPs and both SHA-256 sidecars.

---

### Task 1: Define the x86 archive contract with failing tests

**Files:**
- Create: `Build-BlindSoldier2013PortablePackage.Tests.ps1`
- Test: `Build-BlindSoldier2013PortablePackage.Tests.ps1`

**Interfaces:**
- Consumes: a small dual-package fixture ZIP and a source-verifier scriptblock test seam.
- Produces: executable expectations for `Build-BlindSoldier2013PortablePackage.ps1` and `Verify-BlindSoldier2013PortablePackage.ps1`.

- [ ] **Step 1: Write the failing package-boundary tests**

Create fixture helpers that write x86 and x64 PE samples, shared mod files,
both architecture trees, all eight dual-package Version proxy paths, licenses,
policy, tools, launcher files, and a minimal source manifest. Require these six
target proxy paths:

```powershell
@(
  'version.dll',
  'ff7_en.exe.local/version.dll',
  'ff7.exe.local/version.dll',
  'workingdir/version.dll',
  'workingdir/ff7_en.exe.local/version.dll',
  'workingdir/ff7.exe.local/version.dll'
)
```

Assert x86/common trees are retained, every x64/launcher/nested-2026 tree is
absent, the manifest profile is `legacy-x86`, the source SHA-256 is recorded,
two identical inputs yield byte-identical ZIPs, and the sidecar matches.

- [ ] **Step 2: Write failing rejection tests**

Require rejection of an unsafe ZIP member, injected x64 path, changed manifest
hash, version mismatch, existing output, and source changed after verification.

- [ ] **Step 3: Run tests and confirm RED**

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Import-Module Pester -RequiredVersion 4.10.1 -Force; Invoke-Pester -Script '.\Build-BlindSoldier2013PortablePackage.Tests.ps1' -EnableExit"
```

Expected: FAIL because the builder and verifier scripts do not exist.

- [ ] **Step 4: Commit the failing contract**

```powershell
git add Build-BlindSoldier2013PortablePackage.Tests.ps1
git commit -m "test: define legacy x86 portable package"
```

### Task 2: Implement the deterministic x86 builder and verifier

**Files:**
- Create: `Build-BlindSoldier2013PortablePackage.ps1`
- Create: `Verify-BlindSoldier2013PortablePackage.ps1`
- Modify: `Build-BlindSoldier2013PortablePackage.Tests.ps1`

**Interfaces:**
- Consumes: `Build-BlindSoldier2013PortablePackage.ps1 -SourceArchivePath <zip> -OutputPath <zip> -Version <semver>`.
- Produces: the x86 ZIP, its `.sha256` sidecar, and a verification result containing version, profile, source hash, archive hash, file count, and native hashes.

- [ ] **Step 1: Implement safe source verification and extraction**

Invoke `Verify-BlindSoldierPortablePackage.ps1` by default, bookend verification
and extraction with the same source SHA-256, reject rooted, traversing,
duplicate, alternate-stream, trailing-dot/space, directory, and reparse ZIP
entries, and extract only into GUID-named temporary storage. Expose a hidden
`SourceVerifier` scriptblock only for fixture tests.

- [ ] **Step 2: Implement the exact copy boundary**

Copy only the x86/common roots from the specification. Copy the verified
source `ff7_en.exe.local/version.dll` to all six target proxy paths. Never
remove a source file or enumerate outside the extracted source root.

- [ ] **Step 3: Write the 2013 README and manifest**

Write direct and 7th Heaven extraction instructions, collision warning, log
location, language support, and an explicit statement that the archive has no
FFNx or 7th Heaven files. Write a sorted schema-1 manifest containing
`profile`, `version`, `sourceArchiveSha256`, and per-file length/hash records.

- [ ] **Step 4: Create the deterministic ZIP and checksum**

Sort entries ordinally, set timestamp `2000-01-01T00:00:00Z`, clear external
attributes, use optimal compression, create with `FileMode.CreateNew`, and
write the uppercase SHA-256 sidecar.

- [ ] **Step 5: Implement independent archive verification**

Safely extract the ZIP, validate checksum and manifest, require all six proxy
paths and runtime files, forbid excluded paths, check proxy equality, and
parse the required PE machines as x86 `0x014C`.

- [ ] **Step 6: Run tests and confirm GREEN**

Run the Task 1 Pester command. Expected: all tests pass with zero failures.

- [ ] **Step 7: Commit the implementation**

```powershell
git add Build-BlindSoldier2013PortablePackage.ps1 Verify-BlindSoldier2013PortablePackage.ps1 Build-BlindSoldier2013PortablePackage.Tests.ps1
git commit -m "feat: add legacy x86 portable package"
```

### Task 3: Add both profiles to the release workflow and documentation

**Files:**
- Modify: `.github/workflows/release.yml`
- Modify: `.github/workflows/release.Tests.ps1`
- Modify: `Run-DualRuntimeVerification.ps1`
- Modify: `Run-DualRuntimeVerification.Tests.ps1`
- Modify: `README.md`
- Modify: `docs/releases/v0.2.1-beta.1.md`

**Interfaces:**
- Consumes: verified `Blind-Soldier-Portable.zip`.
- Produces: a verified x86 derivative and both checksum sidecars in future releases.

- [ ] **Step 1: Extend workflow tests first**

Require the workflow to build and verify the derivative after the dual ZIP and
Ghidra gate, then upload these exact assets:

```text
Blind-Soldier-Portable.zip
Blind-Soldier-Portable.zip.sha256
Blind-Soldier-2013-x86-Portable.zip
Blind-Soldier-2013-x86-Portable.zip.sha256
```

- [ ] **Step 2: Run workflow tests and confirm RED**

Run Pester 4.10.1 against `.github/workflows/release.Tests.ps1` and
`Run-DualRuntimeVerification.Tests.ps1`. Expected: the new assertions fail.

- [ ] **Step 3: Wire build and verification commands**

Add the x86 builder/verifier to the aggregate gate and tagged workflow. Keep
the dual-source Ghidra gate before derivative creation.

- [ ] **Step 4: Update tester-facing documentation**

Explain which ZIP to choose, extraction roots, x86-only exclusions, and that
both packages carry the same accessibility behavior and language support.

- [ ] **Step 5: Run workflow tests and confirm GREEN**

Expected: every workflow and aggregate-gate test passes with zero failures.

- [ ] **Step 6: Commit release integration**

```powershell
git add .github/workflows/release.yml .github/workflows/release.Tests.ps1 Run-DualRuntimeVerification.ps1 Run-DualRuntimeVerification.Tests.ps1 README.md docs/releases/v0.2.1-beta.1.md
git commit -m "build: publish dual and legacy x86 archives"
```

### Task 4: Build, verify, publish, and hand off both links

**Files:**
- Generate: `artifacts/release/Blind-Soldier-2013-x86-Portable.zip`
- Generate: `artifacts/release/Blind-Soldier-2013-x86-Portable.zip.sha256`

**Interfaces:**
- Consumes: released `Blind-Soldier-Portable.zip` version `0.2.1-beta.1`.
- Produces: a second release asset and two shareable ZIP links.

- [ ] **Step 1: Run the full relevant verification matrix**

Run new package tests, existing portable tests, mod-manager staging tests,
workflow tests, aggregate gate tests, `git diff --check`, the real x86 builder,
the real x86 verifier, and Ghidra verification of the source dual ZIP.

- [ ] **Step 2: Compare real native hashes**

Confirm x86 proxy and bootstrap hashes equal the Ghidra summary and no x64 PE
or forbidden path is present.

- [ ] **Step 3: Push the implementation branch**

```powershell
git push origin agent/multilingual-support
```

- [ ] **Step 4: Upload without replacing existing assets**

Use `gh release upload v0.2.1-beta.1` without `--clobber`; abort if either
target asset name already exists.

- [ ] **Step 5: Verify GitHub release state**

Require both ZIP assets to report `state=uploaded`; record GitHub digest, size,
and URL; confirm the release remains a public prerelease rather than a draft.

- [ ] **Step 6: Deliver both links**

Provide both direct ZIP links, both SHA-256 values, and one sentence explaining
which archive each player should choose.
