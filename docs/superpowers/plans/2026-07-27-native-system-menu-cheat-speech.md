# Native System Menu and Cheat Speech Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Steam 2026 x64 Escape menu and the FFNx x86 visible popup channel speak verified native state through Prism.

**Architecture:** Exact-fingerprint native adapters publish bounded observations to pure speech trackers. The x64 adapter follows the MUI scene/control state identified in Ghidra; the x86 adapter reads FFNx's own popup buffer and TTL. Both fail closed and reuse the existing Prism outputs and runtime loops.

**Tech Stack:** C# 12, .NET 8 Windows, Reloaded.Hooks 4.3.3, Prism native DLL, Ghidra 12.0.4, PowerShell.

## Global Constraints

- Do not launch FFVII, 7th Heaven, or Reloaded-II; the user performs live game validation.
- Steam x64 support is restricted to SHA-256 `57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B`.
- FFNx x86 support is restricted to module SHA-256 `7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A`.
- Runtime OCR, screen scraping, input inference, and invented state are prohibited.
- Native detours call the original exactly once and perform no speech or blocking work.
- Focus speech is immediate and interrupting; help speech is non-interrupting after 500 milliseconds of stable focus.
- The source directory has no Git metadata, so commit steps are intentionally omitted.
- Deployment must preserve the installed `Configuration/config.json`.

---

### Task 1: Freeze the native identity atlas

**Files:**
- Modify: `C:\FF7A11Y\accessibility_prototype\analysis\InspectSteam2026NativeSystemMenu.java`
- Create: `C:\FF7A11Y\accessibility_prototype\analysis\DumpSteam2026MenuVtables.java`
- Create: `C:\FF7A11Y\accessibility_prototype\analysis\steam2026-native-system-menu-atlas.md`
- Evidence: `C:\FF7A11Y\accessibility_prototype\analysis\ghidra_outputs\steam2026_system_menu_20260727_stdout.txt`
- Evidence: `C:\FF7A11Y\accessibility_prototype\analysis\ffnx-1.24.3-functions.json`

**Interfaces:**
- Consumes: the unpacked x64 image at image base `0x140000000` and installed FFNx 1.24.3 with its matching PDB.
- Produces: constructor/destructor or signal callback RVAs, pristine entry prefixes, Microsoft x64 signatures, object layout bounds, FFNx popup RVAs, and the exact evidence chain used by Tasks 3 and 5.

- [ ] **Step 1: Complete the read-only Ghidra extraction**

Run the existing headless menu script against
`unpacked-menus-dialogue-20260719-1645`, then dump the vtables assigned by the
PCSettingMenu, GameSettingMenu, SystemSettingMenu, BoostSettingMenu,
AutosaveSettingMenu, KeyBindMenu, KeyboardMenu, and GamePadMenu constructors.
The dump script must report every executable target, pristine entry bytes, all
callers, and decompilation.

- [ ] **Step 2: Cross-check the installed binaries**

Run:

```powershell
Get-FileHash 'X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\FFVII.exe' -Algorithm SHA256
Get-FileHash 'X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir\AF3DN.P' -Algorithm SHA256
```

Expected hashes are the two values in Global Constraints.

- [ ] **Step 3: Write the atlas**

Record, for each selected hook or polled object:

```text
runtime, class/control, RVA, ABI, parameters, return, pristine prefix,
object/value offsets, activation evidence, teardown evidence
```

Include the MUI node identifiers, English `Sys_*.png` label/help identities,
and FFNx `popup_msg`, `popup_ttl`, and `popup_color` RVAs.

- [ ] **Step 4: Audit the atlas**

Reject any target without a unique function boundary, coherent object
ownership, readable bounds, and an entry prefix long enough to validate before
hook activation.

---

### Task 2: Add pure x64 speech semantics

**Files:**
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/SystemMenu/Steam2026SystemMenuObservation.cs`
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/SystemMenu/Steam2026SystemMenuCatalog.cs`
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/SystemMenu/Steam2026SystemMenuSpeechCoordinator.cs`
- Create: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026SystemMenuSpeechTests.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Program.cs`

**Interfaces:**
- Consumes:
  `Steam2026SystemMenuObservation(SceneId, ControlId, Kind, Value, Position,
  Count, PrimaryBinding, SecondaryBinding, ModalText, IsFocused, Generation)`.
- Produces:
  `Steam2026SystemMenuSpeechRequest(Text, Interrupt)` from
  `Observe(observation, nowUtc)` and `Poll(nowUtc)`, plus `Reset()`.

- [ ] **Step 1: Write failing formatting tests**

Cover:

```csharp
Button:       "Game Options"
Toggle:       "Speed Boost (x3), On"
List:         "Display Mode, Borderless Windowed, 2 of 3"
Slider:       "Brightness, 52 percent"
Binding:      "Move Up. Primary, W. Secondary, Up Arrow"
Modal:        warning once, then the focused "Yes" or "No"
```

- [ ] **Step 2: Run the x64 test executable and verify RED**

Run:

```powershell
dotnet run --project .\Ff7.Accessibility.Steam2026X64.Tests\Ff7.Accessibility.Steam2026X64.Tests.csproj -c Debug
```

Expected: failure because the new observation/catalog/coordinator types do not
exist.

- [ ] **Step 3: Implement the minimal catalog and formatter**

Use immutable catalog entries:

```csharp
internal sealed record Steam2026SystemMenuCatalogEntry(
    string SceneId,
    string ControlId,
    string Label,
    string? HelpText,
    Steam2026SystemMenuControlKind Kind);
```

The catalog must contain every verified English entry from the atlas and return
no entry for an unknown language, scene, or control.

- [ ] **Step 4: Write failing timing and lifecycle tests**

Test immediate focus, 499-millisecond silence, help at 500 milliseconds,
movement cancellation, value-change speech, repeated-frame deduplication,
modal replacement, scene close, suspend/reset, and a repeated identical value
after a new native generation.

- [ ] **Step 5: Run and verify RED**

Use the command from Step 2. Expected: formatting tests pass and at least one
timing/lifecycle assertion fails.

- [ ] **Step 6: Implement the coordinator and verify GREEN**

Keep one focus generation, one acknowledged immediate key, and one pending-help
deadline. `Reset()` clears all three. Run the x64 test command and require exit
code 0.

---

### Task 3: Capture verified x64 MUI state

**Files:**
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/SystemMenu/Steam2026SystemMenuCallbackContract.cs`
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/SystemMenu/Steam2026SystemMenuIngress.cs`
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/SystemMenu/Steam2026SystemMenuObservationReader.cs`
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/SystemMenu/Steam2026SystemMenuHookSet.cs`
- Create: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026SystemMenuNativeTests.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Program.cs`

**Interfaces:**
- Consumes: exact RVAs, prefixes, ABIs, and object offsets from the Task 1 atlas.
- Produces:
  `TryDequeue(out Steam2026SystemMenuIngressSnapshot)`,
  `TryRead(snapshot, out Steam2026SystemMenuObservation)`,
  `IsFatallyDegraded`, and idempotent `Dispose()`.

- [ ] **Step 1: Write failing contract tests**

Use `CountingNativeMemoryReader` to prove exact fingerprint rejection, entry
prefix validation, executable main-image ownership, coherent double reads,
wrong-vtable rejection, invalid pointer rejection, and unknown scene rejection.

- [ ] **Step 2: Run the x64 tests and verify RED**

Expected: failure because the callback contract and reader do not exist.

- [ ] **Step 3: Implement the identity contract and bounded reader**

Use checked RVA addition, two matching reads around mutable values, the exact
class vtable, bounded UTF-8/ASCII node identifiers, and range validation through
`INativeMemoryReader.TryQueryRegion`.

- [ ] **Step 4: Write failing ingress and hook-lifecycle tests**

Prove original-once behavior when capture succeeds, when capture fails, after
stop, and during publication failure. Prove all-targets-before-activation,
rollback on partial construction, fixed queue capacity, lease degradation, and
idempotent reverse-order disable.

- [ ] **Step 5: Run and verify RED**

Expected: contract/reader tests pass and hook/ingress tests fail for missing
implementation.

- [ ] **Step 6: Implement the capture coordinator and hook set**

Detours copy only the instance/signal pointers, callback kind, native
generation, and timestamp to `BoundedNativeIngressQueue<T>`. Original delegates
remain rooted after stop so a late detour can still call its original once.

- [ ] **Step 7: Run and verify GREEN**

Run the full x64 test executable and require exit code 0.

---

### Task 4: Integrate x64 speech into the research session

**Files:**
- Modify: `Ff7.Accessibility.Core/AccessibilityConfig.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchSession.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Mod.cs`
- Create: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026SystemMenuIntegrationTests.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Program.cs`

**Interfaces:**
- Consumes: `Steam2026SystemMenuHookSet`,
  `Steam2026SystemMenuObservationReader`, and
  `Steam2026SystemMenuSpeechCoordinator`.
- Produces: Prism speech from verified native menu observations while the FFVII
  process is foreground.

- [ ] **Step 1: Write failing integration tests**

Assert config defaults (`true`, `500`), help-delay clamp (`0..5000`), hook
installation only when speech and hooks are enabled, foreground-only dispatch,
reset on suspend/scene close, cohort disable on fatal degradation, and
non-interrupting help delivery.

- [ ] **Step 2: Run and verify RED**

Run the x64 test command. Expected: failure because config and session wiring
are absent.

- [ ] **Step 3: Implement session wiring**

Create the hook set beside the existing translated menu hook set, drain it on
the worker thread, resolve observations, send coordinator requests through
`Steam2026ResearchAccessibilityOutput`, and include it in reset and teardown.

- [ ] **Step 4: Run and verify GREEN**

Run the full x64 test executable and require exit code 0.

---

### Task 5: Read and speak FFNx's visible popup channel

**Files:**
- Create: `Ff7.Accessibility.Reloaded/FfnxPopupIdentity.cs`
- Create: `Ff7.Accessibility.Reloaded/FfnxPopupReader.cs`
- Create: `Ff7.Accessibility.Reloaded/FfnxPopupSpeechTracker.cs`
- Create: `Ff7.Accessibility.Reloaded.Tests/FfnxPopupSpeechTests.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Core/AccessibilityConfig.cs`

**Interfaces:**
- Consumes:
  `FfnxPopupSnapshot(Text, Ttl, Color)` read from the exact FFNx globals.
- Produces:
  `string? Observe(FfnxPopupSnapshot? snapshot)` and `Reset()`.

- [ ] **Step 1: Write failing tracker tests**

Cover first popup, unchanged frame deduplication, expiry, same-text TTL restart,
different text, empty text, invalid UTF-8/termination, and reset.

- [ ] **Step 2: Run the x86 tests and verify RED**

Run:

```powershell
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Debug
```

Expected: failure because popup types do not exist.

- [ ] **Step 3: Implement the pure tracker and verify GREEN**

Normalize embedded line breaks and whitespace but preserve the popup's words,
punctuation, values, and enabled/disabled state.

- [ ] **Step 4: Write failing identity/reader tests**

Assert exact module SHA, x86 architecture, bounded NUL-terminated buffer,
coherent message/TTL/message reads, readable committed module/data pages, and
failure on any mismatch.

- [ ] **Step 5: Implement the reader and verify GREEN**

Resolve the current process module once, validate the exact Task 1 RVAs, and
read at most the native `popup_msg` capacity. Do not scan memory or infer a
shortcut.

- [ ] **Step 6: Write failing x86 loop-integration tests**

Assert the default `EnableFfnxPopupSpeech=true`, creation only for validated
FFNx, foreground-only Prism speech, reset on suspend/unload, and rate-limited
identity diagnostics.

- [ ] **Step 7: Integrate and verify GREEN**

Poll the reader from `MonitorLoop` before other menu speech, call the existing
`Speak(text, true)`, and preserve all existing FFNx opening-movie behavior.
Run the full x86 test executable and require exit code 0.

---

### Task 6: Package, verify, and deploy

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Configuration/config.json`
- Modify: `dist/ff7.accessibility.reloaded/**`
- Deploy: `C:\Users\buu42\AccessXI\external\Reloaded-II\Mods\ff7.accessibility.reloaded\**`

**Interfaces:**
- Consumes: passing source projects and the installed user configuration.
- Produces: architecture-correct x86 and x64 Reloaded-II payloads with Prism and
  dependencies.

- [ ] **Step 1: Run all test projects**

Run:

```powershell
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release
dotnet run --project .\Ff7.Accessibility.Steam2026X64.Tests\Ff7.Accessibility.Steam2026X64.Tests.csproj -c Release
dotnet run --project .\Ff7.Accessibility.Shared.Tests\Ff7.Accessibility.Shared.Tests.csproj -c Release
dotnet run --project .\Ff7.Accessibility.Parity.Tests\Ff7.Accessibility.Parity.Tests.csproj -c Release
```

Require four exit codes of 0.

- [ ] **Step 2: Publish both architectures**

Run:

```powershell
dotnet publish .\Ff7.Accessibility.Reloaded\Ff7.Accessibility.Reloaded.csproj -c Release -r win-x86 --self-contained false
dotnet publish .\Ff7.Accessibility.Steam2026X64\Ff7.Accessibility.Steam2026X64.csproj -c Release -r win-x64 --self-contained false
```

- [ ] **Step 3: Stage without overwriting user configuration**

Copy published x86 output to `dist/ff7.accessibility.reloaded/x86`, x64 output
to `dist/ff7.accessibility.reloaded/x64`, shared assets to the package root,
and update the template configuration only. Save the installed
`Configuration/config.json` bytes before deployment and restore those exact
bytes afterward.

- [ ] **Step 4: Deploy**

Copy the staged package into
`C:\Users\buu42\AccessXI\external\Reloaded-II\Mods\ff7.accessibility.reloaded`
without deleting unrelated files.

- [ ] **Step 5: Verify the installed payload**

Check:

```text
x86/Ff7.Accessibility.Reloaded.dll       PE32 managed x86
x64/Ff7.Accessibility.Steam2026X64.dll   PE32+ managed x64
x86/prism.dll                            x86
x64/prism.dll                            x64
ModConfig.json                           selects both runtime DLLs
Configuration/config.json                byte-identical to pre-deployment copy
```

Re-run both installed-assembly dependency inspections and record SHA-256 hashes.
Do not launch any game or loader process.
