# Stock 7th Heaven Bootstrap Design Amendment

## Status and precedence

Approved design correction discovered during pre-implementation source review on
2026-08-06. This amendment is part of the approved stock-compatibility design
and takes precedence over the initial-readiness and FFNx-packaging sections of
`2026-08-06-stock-seventh-heaven-bootstrap-design.md`.

## Correct initial readiness boundary

The initial design proposed delaying the complete Blind Soldier managed runtime
until FFNx replaced field opcode-table entry `0x40`. That is too late. The
reference native accessibility mod starts title/menu polling before its field
hooks become installable, and Blind Soldier must not leave New Game or another
title choice silent while waiting for field state.

Stock 7th Heaven 4.5.2 provides an earlier, exact, source-backed boundary in
`AppLoader/dllmain.cpp`:

1. On process attach, AppLoader deletes the previous `AppLoader.log`, opens a
   new log, and writes `AppLoader init log`.
2. Its `GameWinMain` detour loads hostfxr and AppProxy, calls
   `HostInitialize(&exports)`, and commits all Win32 file/API detours.
3. It writes `AppLoader started successfully`.
4. The very next operation calls FF7's original `GameWinMain`.

For a launch classified as 7th Heaven, Blind Soldier's native Version-proxy
worker will therefore wait for those two ordered markers in the current
launch's `AppLoader.log`. It will require the log's file time to belong to the
current FF7 process lifetime, so a stale marker can never authorize injection.
The worker tolerates normal sharing and a partially written final line; an
incomplete marker remains a wait state.

Launch classification is sticky. Any of the following locks the process into
the 7th Heaven branch:

- Blind Soldier's verified host validator returns `SevenHeavenX86`;
- a `dinput.dll` module loaded from the FF7 executable directory is observed;
- `AF3DN.P`, `7H_GameDriver.dll`, or `FFNx.dll` is observed.

An exact stock x86 host remains in discovery for a bounded interval so 7th
Heaven's local AppLoader can appear. Only when the entire interval expires with
none of that evidence can direct legacy startup proceed without an AppLoader
log.

The later FFNx MESSAGE-handler replacement remains a field-hook diagnostic and
managed hook-chaining input. It does not gate initial Prism, title, menu, or
input accessibility startup.

## Startup sequence amendment

The corrected x86 7th Heaven sequence is:

1. 7th Heaven starts FF7 through its stock local `dinput.dll` AppLoader.
2. Blind Soldier's executable-local `version.dll` forwards all Version APIs and
   starts a native worker.
3. The worker validates the host, portable root, and current process lifetime.
4. It classifies the launch and, for 7th Heaven, waits for the ordered current
   AppLoader markers.
5. AppLoader finishes AppProxy/AppWrapper initialization and commits its API
   detours, then writes `AppLoader started successfully` immediately before
   entering FF7's original WinMain.
6. Blind Soldier starts its broker, which attaches Reloaded-II to the already
   initialized stock .NET host and loads Shared Hooks followed by Blind Soldier.
7. Blind Soldier acquires its one-instance runtime lease and initializes Prism,
   title/menu readers, input, and resilient hook retries.
8. Field hooks resolve and chain the already installed FFNx handlers when field
   state becomes available.

The obsolete `BlindSoldier.ManagedReady.<pid>` event is removed from native and
managed code. The existing proxy-to-broker ready event remains internal to Blind
Soldier and continues to report successful Reloaded attachment.

## Stock FFNx ownership amendment

“Stock 7th Heaven and FFNx remain untouched” is literal. The portable Blind
Soldier ZIP will no longer copy `AF3DN.P`, `AF4DN.P`, `FFNx.toml`,
`steam_api.dll`, or any other FFNx file. Players install or update 7th Heaven and
FFNx through their normal official flow. Blind Soldier supplies only its own
Version proxy, broker, Reloaded runtime, Shared Hooks, Prism, managed mod,
assets, launcher components, and documentation.

Package tests must reject every FFNx, 7th Heaven, WinMM-proxy, ASI, or dinput
artifact. Live validation records hashes for the existing 7th Heaven and FFNx
files before extraction, after extraction, and after game exit; all hashes must
match.

This amendment supersedes the earlier separate “bundle FFNx” design for this
release.

## Version-library cache amendment

If Windows redirects the absolute System32 Version-library request back to the
already-loading proxy, the private cache fallback must not trust file length
alone. It will:

- reject reparse points;
- copy to a uniquely named temporary file in the same cache directory;
- verify SHA-256 equality with the current system `version.dll`;
- validate that the candidate is an x86 PE library;
- publish it atomically under a content-addressed name; and
- tolerate another process winning the identical atomic publication race.

A corrupt same-size cache, stale source version, reparse collision, or wrong PE
machine must be rejected and rebuilt or fail closed with an accessible error.

## Additional acceptance checks

- A current AppLoader log with ordered markers starts the broker.
- A stale, reversed, missing, truncated, or wrong-path AppLoader log does not.
- Detection of 7th Heaven remains sticky even while FFNx is temporarily absent.
- Direct legacy FF7 starts only after its detection interval proves that no 7th
  Heaven loader appeared.
- Title options speak on the first stock 7th Heaven launch before field opcode
  readiness exists.
- Field dialogue and Echo-S hooks later chain FFNx without duplicate speech or
  duplicate audio descriptions.
- Production source, built native/managed binaries, and package contents contain
  no `BlindSoldier.ManagedReady` contract.
- Final Ghidra verification audits `version.dll`, not the obsolete WinMM proxy,
  and verifies the AppLoader marker/state logic plus the hardened system-Version
  cache.
- Staging, release-gate, version metadata, and live-test scripts consistently use
  the Version proxy and release version `0.1.6`.
- x64 binaries and behavior remain unchanged except shared release metadata.
