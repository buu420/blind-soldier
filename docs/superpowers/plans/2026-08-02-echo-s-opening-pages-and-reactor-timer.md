# Echo-S Opening Pages and Reactor Timer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan.

**Goal:** Speak all four Echo-S pre-movie disclaimer pages with an actionable confirm prompt and restore the supported Echo-S first-reactor escape timer from five to ten minutes.

**Architecture:** Route both native message signals through a bounded fingerprint-gated disclaimer tracker, and add a dedicated native `STTIM` hook that defers exact script validation and the timer write to the monitor thread. Both paths fail closed for unknown scripts and leave vanilla FF7 unchanged.

**Tech Stack:** C#/.NET, Reloaded.Hooks x86 cdecl hooks, native FF7 field-script state, `ReadProcessMemory`/`WriteProcessMemory`, existing console test harnesses, PowerShell packaging/deployment.

## Global Constraints

- Do not launch, stop, or focus FF7, 7th Heaven, or Reloaded.
- Do not alter the user's installed configuration.
- Call every native original exactly once.
- Keep detours bounded and defer hashing, speech, logging, and writes to the monitor thread.
- Accept only exact supported Echo-S fingerprints and verified script coordinates.
- Use `apply_patch` for source and documentation edits.
- This workspace is not a Git repository, so commit steps are intentionally omitted.

### Task 1: Add failing disclaimer lifecycle tests

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/EchoSCompatibilityTests.cs`
- Modify: `Ff7.Accessibility.Reloaded/EchoSDisclaimerSpeechTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/EchoSCompatibilityManifest.cs`

**Step 1: Write failing tests**

Add assertions that:

- `ResolveDisclaimerSpeechText` appends `Press confirm to continue.` to each supported page.
- IDs 1 through 4 queue before identity is available and resolve after the exact field 109 identity arrives.
- unsupported identities do not resolve;
- a failed delivery remains pending;
- a delivered page does not repeat;
- reset permits a new loaded-script lifecycle.

**Step 2: Run the focused test harness and confirm failure**

Run:

```powershell
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release
```

Expected: compilation or assertion failure because the queued disclaimer API and prompt-bearing resolver do not exist yet.

**Step 3: Implement the smallest passing tracker API**

Add:

```csharp
public static string? ResolveDisclaimerSpeechText(LoadedFieldScriptIdentity identity, int messageId);
public bool Queue(FieldScriptContext context, int messageId);
public EchoSDisclaimerSpeechCandidate? TryResolve(LoadedFieldScriptIdentity identity);
public void Acknowledge(EchoSDisclaimerSpeechCandidate candidate, bool delivered);
```

Keep the queue bounded to IDs 1 through 4, keyed to field 109 and script pointer, and retain failed candidates for retry.

**Step 4: Run the focused tests and confirm pass**

Run the same `dotnet run` command and require exit code zero.

### Task 2: Route live disclaimer events through the tracker

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`

**Step 1: Add a shared queue/flush helper**

Create helpers equivalent to:

```csharp
private void QueueEchoSDisclaimerSpeech(FieldScriptContext? context, int messageId);
private void TickEchoSDisclaimerSpeech();
```

Queue from `HandleFieldOpcodeMessageObservation` on the first native lifecycle observation and from `HandleFieldMessageOpen` as a secondary source. Flush after deferred native field events on the monitor thread. Use the exact loaded identity, call `Speak` once per attempt, acknowledge only on success, and suppress generic visible-window ownership only after exact compatibility ownership is established.

**Step 2: Remove duplicate special-case behavior**

Replace the old standalone disclaimer branch in `HandleFieldMessageOpen` with the shared path. Ensure resets clear the tracker.

**Step 3: Run focused tests**

Run:

```powershell
dotnet run --project .\Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Release
```

Expected: exit code zero.

### Task 3: Add failing timer-policy, event-queue, and delegate tests

**Files:**
- Create: `Ff7.Accessibility.Reloaded/EchoSReactorTimerOverrideTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/EchoSCompatibilityTests.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Reloaded/NativeFieldHookEventQueue.cs`
- Modify: `Ff7.Accessibility.Reloaded/FieldOpcodeParameterReader.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`

**Step 1: Write failing policy tests**

Assert that exact field 125 contexts `(1, 0, 0x89, 0x38)` and `(1, 0, 0x91, 0x38)` resolve to 600 seconds for the base and alternate supported Echo-S identities. Assert rejection for vanilla offset `0x11E`, wrong field/entity/script/opcode, unknown hashes, and nearby offsets. Assert pending identity retry, at-most-once per script pointer, failed-write retry, and reset/new-pointer behavior.

**Step 2: Write failing plumbing tests**

Add a `TimerSet` queue round-trip assertion, assert `FieldOpcodeAddressResolver.OpcodeTimerIndex == 0x38`, and assert `FieldOpcodeTimerDelegate` has the Reloaded x86 cdecl `FunctionAttribute`.

**Step 3: Run tests and confirm failure**

Run the focused test harness. Expected: compile/assertion failure for missing policy, event kind, opcode constant, and delegate.

### Task 4: Implement the native timer override

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/EchoSReactorTimerOverrideTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/NativeFieldHookEventQueue.cs`
- Modify: `Ff7.Accessibility.Reloaded/FieldOpcodeParameterReader.cs`
- Modify: `Ff7.Accessibility.Reloaded/CurrentProcessLegacyAddressSpace.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`

**Step 1: Implement the pure tracker**

Create a bounded tracker accepting only the two verified Echo-S contexts. It must return an apply decision containing script pointer and `Seconds = 600` only for an exact supported field identity. Keep an unavailable identity pending briefly and acknowledge a decision only after a successful write.

**Step 2: Extend deferred native events**

Add `NativeFieldHookEventKind.TimerSet` and `TryCaptureTimerSet(FieldScriptContext context, int result)`, preserving existing fixed-slot queue behavior.

**Step 3: Add checked native writing**

Extend `CurrentProcessLegacyAddressSpace` with:

```csharp
public bool TryWriteInt32(uint address, int value);
```

Use `WriteProcessMemory` against the current process and require all four bytes to be written.

**Step 4: Install and handle opcode `0x38`**

Add the x86 cdecl delegate, hook fields, installer, and detour. Resolve the handler through `FieldOpcodeAddressResolver`, capture context before the original call, invoke the original exactly once, and enqueue afterward. Install the hook independently of cutscene-description settings on supported x86 runtime.

Process `TimerSet` events on the monitor thread, validate the exact loaded identity, and write 600 to `0x00DC08BC`. Log success or terminal rejection without speaking an extra gameplay message. Reset the tracker with field compatibility state.

**Step 5: Run focused tests**

Run the Reloaded test harness and require exit code zero.

### Task 5: Verify, package, and deploy

**Files:**
- Verify all modified projects and package scripts.
- Deploy to `C:/Users/buu42/AccessXI/external/Reloaded-II/Mods/ff7.accessibility.reloaded`.

**Step 1: Run targeted and broader verification**

Inspect the repository's package scripts, then run the focused Reloaded tests and the existing dual-runtime package verification commands. Require fresh exit code zero for every command.

**Step 2: Build the release package**

Run the existing dual-runtime packaging script with its documented arguments. Verify both x86 and x64 artifacts are present and architecture-correct.

**Step 3: Deploy while preserving configuration**

Use the existing installer/deployment script. Compare the installed configuration hash before and after deployment and require it to remain unchanged. Do not launch any game component.

**Step 4: Verify installed artifacts**

Compare packaged and installed DLL hashes, inspect deployment output for the Echo-S compatibility files/dependencies, and record the exact installed paths.

**Step 5: Hand off two live checks**

Tell the user the build is installed and ask them to verify only:

1. each pre-movie Echo-S page speaks its text plus the confirm instruction;
2. the first reactor escape countdown begins at ten minutes.
