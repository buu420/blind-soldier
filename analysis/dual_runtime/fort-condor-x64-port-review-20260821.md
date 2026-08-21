# Fort Condor x64 port review — 2026-08-21

Reviewed commit: `b8404ee` (`feat: give the fort battle to both runtimes, and name what is on it`)

Scope:

- `Ff7.Accessibility.Steam2026X64/Ff7.Accessibility.Steam2026X64.csproj`
- `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchObservationPump.cs`
- `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchSession.cs`
- the shared input and translated-address-space implementations those changes depend on

## Finding

### Medium: the x64 suspend/resume reset omitted Fort Condor state

Status: fixed in this worktree. `ResetCondorBattle` now resets the observation
epoch, read throttle, speech tracker, and terrain cache, and the session reset
path calls it. `DualRuntimeSharedSourceTests` guards the reset wiring.

`Steam2026ResearchSession.Run` handles `resetRequested` by resetting every long-lived runtime
coordinator that can retain observations across a suspend: field navigation, world map,
highway, normal battle, footsteps, menus, and cutscenes. The new Fort Condor reader and speech
tracker live inside `Steam2026ResearchObservationPump`, but that reset block does not reset
them.

Consequences:

- `CondorBattleSpeechTracker` can compare the first post-resume snapshot with a pre-suspend
  snapshot and announce casualties, menu changes, or a result that occurred while the mod was
  suspended.
- `CondorBattleStateReader` can retain its cached terrain across the reset. The terrain is safe
  if this is the same battle, but keeping it is contrary to the reset contract and unsafe if a
  reset spans a guest-runtime reinitialization without an observed non-module-9 frame.
- `inCondorBattle` can remain true, so the post-resume session is not treated as a fresh
  observation epoch.

The normal module-exit path and `BeginShutdown` do reset all three pieces. The missing path is
specifically `Steam2026ResearchSession`'s `resetRequested` block.

The implemented fix exposes a pump-level Condor reset that clears `inCondorBattle`, the speech
tracker, the terrain cache, and the read throttle, and calls it alongside the highway and normal
battle resets.

## Requested concern checks

### Shared K key: safe for the two present owners

`Steam2026ForegroundInputAdapter.ObserveRisingEdge` delegates to
`NavigationKeyPressTracker.Observe`, which records one `wasDown` bit per virtual key. A second
call for K in the same worker iteration would consume nothing because the first call has already
stored `isDown=true`.

The current call sites do not make that second call:

- highway is module 6;
- Fort Condor is module 9;
- each expression tests its module and foreground predicates before the call;
- C# `&&` evaluates left to right and short-circuits.

Therefore the active module is the only one that samples K in that block. The comment is
accurate. This would cease to be safe if either caller moved the input call before its module
predicate, so the ordering should remain covered by a focused ownership test.

There is a pre-existing, smaller input caveat: an owner that does not sample K while inactive
cannot learn that K became held during a module with no K owner. Entering the module while K is
already held can therefore look like a rising edge. This is not introduced by the Condor port,
and field/world-map owners sample K in their own modules, so it is not a release blocker for
this change.

### Speech gate: no `EnableSpeech` divergence

The x64 call site checks `config.EnableSpeech` before `output.Speak`. The x86 Condor loop calls
the common `Mod.Speak` helper, and that helper itself checks `config.EnableSpeech` before calling
Prism. Both runtimes still advance `CondorBattleSpeechTracker` while speech is disabled, so
neither replays old transitions when speech is later enabled.

The only behavioral difference is foreground ownership: x64 suppresses Condor output while the
game is backgrounded, whereas the x86 common `Speak` helper does not apply a foreground test.
The x64 behavior matches the ownership policy used by its normal battle pipeline and prevents
game speech from leaking over another foreground application. Closing that broader x86 policy
difference should be a separate, cross-feature change rather than weakening the new x64 port.

### Read cost and thread

The code runs on the named background research worker, not a game callback or render thread.
That worker loops every 35 ms in the ordinary case. `ObserveCondorBattle` throttles coherent
state reads to 100 ms unless the status hotkey is pressed.

- The 40 live-unit records are 0x78 bytes each: 4,800 bytes per snapshot, ten times per second.
- Collision terrain is read only when first entering a battle or when its record count changes.
  The normal 333 records total 25,308 bytes and are cached for the battle.
- `TranslatedX86AddressSpace` adds page-table and mapping-stability checks to each read. The
  terrain implementation currently performs one translated read per record, so its first load
  is more syscall-heavy than a single bulk read, but it happens off the game thread and only
  once per battle.

This is acceptable for correctness. A bulk terrain read would be a reasonable optimization only
if profiling shows the first Condor snapshot delaying other accessibility output; it is not
needed speculatively.

### `TranslatedX86AddressSpace` lifetime across module changes

The object is tied to the supported x64 process image, not to one FFVII guest module. It stores
the host module base and fixed page-table location. Every `TryRead` resolves the current guest
pages, rejects unmapped/sentinel entries, reads the data, then re-reads the page-table entries
and rejects the snapshot if any mapping changed during the read.

That makes the address-space object valid across guest module changes. It does not make a set of
separate reads atomic; the Condor snapshot can still cross a game tick, and the native placement
flag is known to be frame-local. That coherence issue belongs to the placement comparison, not
to the x64 translation lifetime.

The Condor terrain cache is separate from the translated address space. It is correctly cleared
when a non-module-9 frame is observed and on shutdown; the reset omission above is the only
port-specific lifetime defect found.

## Project file review

The five linked sources are the complete direct Condor dependency set:

1. `CondorUnitCatalog.cs`
2. `CondorPlacementRegion.cs`
3. `CondorBattleSnapshot.cs`
4. `CondorBattleStateReader.cs`
5. `CondorBattleSpeechTracker.cs`

They all compile from the legacy project as their canonical source and are linked into x64, so
the implementation and wording cannot drift between copies. The new dual-runtime shared-source
guard is the correct structural protection for this class of omission.
