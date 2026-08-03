# Blind Swordsman GitHub Repository Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the existing dual-runtime FFVII accessibility source tree into a clean, self-contained private GitHub repository named `buu420/blind-swordsman` with an accurate player-focused README.

**Architecture:** Keep the existing Reloaded-II project layout and stable mod ID. Add only the three small external build inputs beneath the repository root, repoint existing consumers, exclude generated and machine-local data, and publish a branded root README. Initialize `main`, commit the reviewed contents, create the private repository through GitHub's authenticated REST endpoint, and push with the existing Windows Git credential.

**Tech Stack:** Git, GitHub REST API, GitHub CLI credential store, PowerShell, .NET 8, Reloaded-II, C#.

## Global Constraints

- The public-facing name is `Blind Swordsman`; `ff7.accessibility.reloaded` remains the stable internal ModId.
- Repository visibility is private for the initial push.
- No game executable, game archive, save, credential, log, generated build output, downloaded toolchain, or Ghidra database may be committed.
- README controls and navigation behavior must match the shared x86/x64 implementation.
- The README is a quick start and navigation manual, not a gameplay walkthrough.

---

### Task 1: Repository hygiene and portable build inputs

**Files:**
- Create: `.gitignore`
- Create: `.gitattributes`
- Create: `analysis/dual_runtime/parity-matrix.json`
- Create: `external/kujata/field-id-to-world-map-coords.json`
- Create: `external/kujata/wm-field-menu-names.txt`
- Modify: `Build-DualRuntimePackage.ps1`
- Modify: `Install-FF7ReloadedMod.ps1`
- Modify: `Launch-FF7Reloaded.ps1`
- Modify: `Run-DualRuntimeVerification.ps1`
- Modify: `Run-DualRuntimeVerification.Tests.ps1`
- Modify: `FF7SteamInstall.Tests.ps1`
- Modify: `Ff7.Accessibility.Parity.Tests/Ff7.Accessibility.Parity.Tests.csproj`
- Modify: `Ff7.Accessibility.Reloaded/Ff7.Accessibility.Reloaded.csproj`
- Modify: `Ff7.Accessibility.Steam2026X64/Ff7.Accessibility.Steam2026X64.csproj`

**Interfaces:**
- Consumes: the verified parity matrix and two Kujata metadata files from the current parent workspace.
- Produces: a repository whose build and installer paths resolve entirely beneath the repository root.

- [x] **Step 1: Add Git ignore and text-normalization rules**

Ignore `bin`, `obj`, `dist`, test results, logs, crash dumps, IDE state, local backups, and `tools/ghidra`. Normalize text to LF while retaining CRLF for Windows command files.

- [x] **Step 2: Add the three verified external inputs**

Copy the current parity matrix and two Kujata metadata inputs with identical
parsed content to `analysis/dual_runtime/` and `external/kujata/`. Repository
line-ending normalization may change their byte hashes without changing data.

- [x] **Step 3: Repoint every production and test consumer**

Use repository-local paths:

```powershell
Join-Path $scriptRoot 'analysis\dual_runtime\parity-matrix.json'
Join-Path $scriptRoot 'external\kujata\field-id-to-world-map-coords.json'
Join-Path $scriptRoot 'external\kujata\wm-field-menu-names.txt'
```

Project files beneath a project directory use `..\analysis\...` or
`..\external\...` as appropriate.

- [x] **Step 4: Verify copied inputs and path closure**

Run SHA-256 comparisons against the current parent-workspace files, then run:

```powershell
rg -n "\.\.\\analysis|\.\.\\tools\\kujata" -g '*.ps1' -g '*.csproj' .
```

Expected: no consumer reaches outside the new repository for these inputs.

- [x] **Step 5: Commit the portable repository foundation**

```powershell
git add .gitignore .gitattributes analysis external Build-DualRuntimePackage.ps1 Install-FF7ReloadedMod.ps1 Launch-FF7Reloaded.ps1 Run-DualRuntimeVerification.ps1 Run-DualRuntimeVerification.Tests.ps1 FF7SteamInstall.Tests.ps1 Ff7.Accessibility.Parity.Tests/Ff7.Accessibility.Parity.Tests.csproj Ff7.Accessibility.Reloaded/Ff7.Accessibility.Reloaded.csproj Ff7.Accessibility.Steam2026X64/Ff7.Accessibility.Steam2026X64.csproj
git commit -m "build: make repository inputs self-contained"
```

### Task 2: Product branding and player README

**Files:**
- Create: `README.md`
- Modify: `Ff7.Accessibility.Reloaded/README.md`
- Modify: `Ff7.Accessibility.Reloaded/ModConfig.json`

**Interfaces:**
- Consumes: verified keys and category order from `Mod.cs`, `NavigationProgressControls.cs`, `FieldNavigationContracts.cs`, and `WorldMapTargetCatalog.cs`.
- Produces: the GitHub landing page and Reloaded-II display metadata for Blind Swordsman.

- [x] **Step 1: Write the GitHub landing page**

Cover overview, status, requirements, release and source installation, launch paths, mod-specific keys, detailed field/world navigation, progress indicators, troubleshooting, reporting issues, credits, and non-affiliation. Recommend keeping progress enabled and state that `F5` toggles it, while `F6` and `F7` cycle 5, 10, 15, and 20 percent.

- [x] **Step 2: Replace stale nested documentation**

Replace the obsolete x86-only project README with a short pointer to the root README so GitHub users cannot encounter contradictory instructions.

- [x] **Step 3: Apply visible Blind Swordsman branding**

Set `ModName` to `Blind Swordsman` and replace the development-only description with an accurate pre-release dual-runtime accessibility description. Preserve `ModId`, author, version, DLL names, dependencies, and supported app IDs.

- [x] **Step 4: Validate all documented controls against source**

Run targeted `rg` checks for `VirtualKeyU/O/J/K/L/I`, `VirtualKeyF5/F6/F7/F8`, the field category enum, and the world-map category enum. Review every README key row against those results.

- [x] **Step 5: Commit branding and documentation**

```powershell
git add README.md Ff7.Accessibility.Reloaded/README.md Ff7.Accessibility.Reloaded/ModConfig.json
git commit -m "docs: introduce Blind Swordsman"
```

### Task 3: Validate and commit the complete source tree

**Files:**
- Add: all intended source, test, asset, template, tool-source, and focused documentation files not excluded by `.gitignore`.

**Interfaces:**
- Consumes: the cleaned source tree and self-contained inputs from Tasks 1 and 2.
- Produces: a tested initial `main` history containing the complete intended mod source.

- [x] **Step 1: Run source and credential audits**

List ignored and untracked files, reject any file of 100 MB or more, and scan intended files for token, private-key, password, and credential patterns. Confirm that local paths found in fixtures or research notes contain no credential values.

- [x] **Step 2: Run the relevant validation suites**

Run the shared, x86, x64, parity, PowerShell installer, packaging, and dual-runtime verification commands already provided by the repository. Record any release-gate status separately from actual test failures.

- [x] **Step 3: Stage the intended tree and inspect it**

```powershell
git add -A
git status --short
git diff --cached --stat
```

Expected: no `bin`, `obj`, `dist`, log, dump, Ghidra database, or downloaded toolchain files.

- [ ] **Step 4: Commit the source snapshot**

```powershell
git commit -m "feat: publish Blind Swordsman source"
```

### Task 4: Create and verify the private GitHub repository

**Files:**
- Modify: local Git remote configuration only.

**Interfaces:**
- Consumes: the authenticated `buu420` Windows Git credential and clean local `main` branch.
- Produces: private repository `https://github.com/buu420/blind-swordsman` with `main` tracking `origin/main`.

- [ ] **Step 1: Confirm the remote name is still available**

Query `GET /repos/buu420/blind-swordsman`. Continue only when GitHub returns not found for the authenticated account.

- [ ] **Step 2: Create the private repository**

Call `POST /user/repos` with name `blind-swordsman`, private visibility, issues enabled, wiki disabled, and description `Screen-reader, audio-description, spatial-audio, and navigation accessibility for Final Fantasy VII on Windows.` Do not initialize remote files.

- [ ] **Step 3: Add origin and push main**

```powershell
git remote add origin https://github.com/buu420/blind-swordsman.git
git push -u origin main
```

- [ ] **Step 4: Set topics and verify remote state**

Set topics for Final Fantasy VII, accessibility, blind gamers, screen readers,
Reloaded-II, and FFNx. Query the repository and compare local `HEAD` to
`refs/heads/main`; verify private visibility and default branch `main`.
