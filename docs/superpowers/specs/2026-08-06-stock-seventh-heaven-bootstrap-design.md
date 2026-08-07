# Blind Soldier Stock 7th Heaven Bootstrap Design

## Status

Approved by the user on 2026-08-06.

## Decision

Blind Soldier's x86 runtime will work with an unmodified source or release build
of 7th Heaven and its stock FFNx driver. Blind Soldier will not patch, replace,
or require a private build of 7th Heaven, AppLoader, AppProxy, AppWrapper, or
FFNx.

The existing x86 C# accessibility implementation and Prism speech backend will
remain intact. A native x86 `version.dll` forwarding proxy will delay the
existing Blind Soldier broker and Reloaded-II injection until the stock 7th
Heaven and FFNx startup path has reached an observable, stable game-runtime
boundary. Steam 2026 x64 behavior is outside this change and must remain
unchanged.

## Evidence behind the design

The independently maintained `Final_Fantasy_VII_Accessibility` release uses a
native x86 `version.dll` proxy, not Reloaded-II. It forwards the Windows Version
APIs, waits until FFNx has replaced the FF7 field MESSAGE opcode handler, and
then chains its own hooks after the handler already installed by FFNx. Its
runtime reads semantic FF7 state directly and sends speech through Tolk; 7th
Heaven itself supplies no accessibility-state API.

Ghidra analysis of that release confirmed that its proxy is PE32 x86, imports
no managed host, and contains no remote-process injection APIs. Source analysis
of stock 7th Heaven 4.5.2 confirmed that AppLoader hosts AppProxy/AppWrapper and
installs its virtual-file hooks before it invokes FF7's real `GameWinMain`.
Consequently, an FFNx field-opcode handler observed after that startup sequence
is a useful stock-owned readiness boundary.

Blind Soldier currently starts Reloaded-II from its native proxy before that
boundary and contains a PID-scoped `BlindSoldier.ManagedReady` event introduced
for a private 7th Heaven patch. A clean 7th Heaven installation neither creates
nor consumes that contract. Starting Reloaded first can also make its CoreCLR
configuration win over 7th Heaven's runtime configuration. The fix is to change
startup ownership and timing, not to add another 7th Heaven patch.

## Goals

1. A clean, unmodified 7th Heaven 4.5.2 installation can launch supported x86
   FF7 with FFNx, selected 7th Heaven mods, and Blind Soldier all active.
2. Blind Soldier continues to use its existing C# feature implementation and
   Prism output.
3. Blind Soldier never creates, modifies, replaces, or deletes 7th Heaven or
   FFNx program files or configuration as part of startup.
4. The x86 proxy starts Blind Soldier exactly once and only after stock runtime
   readiness has been proven.
5. Direct legacy x86 startup without 7th Heaven remains supported.
6. Steam 2026 x64 startup, packaging, behavior, and binaries remain unchanged.
7. Failure to establish accessibility remains fail-closed and produces an
   accessible error with a precise log path.

## Non-goals

- Rewriting Blind Soldier's x86 accessibility features in native C++.
- Replacing Prism with Tolk.
- Copying GPL-3.0 implementation code from the reference project.
- Shipping or maintaining a fork of 7th Heaven or FFNx.
- Changing 7th Heaven's mod-discovery, IRO, profile, or update behavior.
- Changing Blind Soldier's x64 startup path.

## Component boundaries

### Version API forwarding

The x86 `version.dll` remains a complete proxy for the 17 Version APIs expected
by the supported environment. It loads a distinct implementation of the real
Windows Version library, resolves every export before allowing forwarded calls,
and never redirects through `winmm.dll`. The prior WinMM bootstrap must not be
packaged because FFNx inspects WinMM machine code during address discovery.

`DllMain` performs only the minimum required proxy initialization: disable
thread notifications, establish forwarding, and schedule a worker. Runtime
probing, logging, package validation, broker launch, and waits occur on the
worker outside loader lock.

### Stock runtime readiness probe

A focused native readiness component will derive the field opcode dispatch
table using Blind Soldier's already verified legacy FF7 address evidence:

- field-init function virtual address `0x0060BACF` for the supported image;
- execute-opcode call at field-init plus `0x80`;
- opcode-table operand at execute-opcode plus `0x10D`; and
- MESSAGE entry index `0x40`.

The implementation must validate every instruction, pointer, memory page, and
module range before dereferencing it. It must use the actual loaded host-module
base and PE image size rather than assuming an unchecked address range.

Readiness has two supported outcomes:

1. **Stock 7th Heaven/FFNx:** when 7th Heaven or FFNx startup is observed, the
   proxy waits until an FFNx module is loaded and the MESSAGE opcode-table entry
   points to executable memory outside the FF7 host image. The handler must be
   stable across consecutive samples before readiness is accepted.
2. **Direct legacy FF7:** when neither 7th Heaven nor FFNx appears during a
   bounded discovery interval, the proxy accepts the validated original FF7
   opcode table after it remains stable across consecutive samples.

The probe must never classify a temporarily absent FFNx module during 7th
Heaven startup as a direct-game launch. A locally loaded 7th Heaven `dinput.dll`
or appearance of any recognized FFNx module (`AF3DN.P`, `7H_GameDriver.dll`, or
`FFNx.dll`) locks the launch into the FFNx-required branch.

The worker polls at a short bounded interval and stops immediately if the game
process is exiting. A timeout is a startup failure, not permission to inject
early.

### Existing broker and Reloaded-II

Only after readiness succeeds does the proxy perform the existing portable-root
validation, private-runtime setup, broker launch, and attach-mode Reloaded-II
injection. The broker continues to validate the x86 host and payload, manage the
Reloaded pointer lease, write the application configuration, inject the
architecture-matched Reloaded bootstrapper, and remain alive until FF7 exits.

The broker-ready event remains an internal Blind Soldier proxy-to-broker
contract. It proves that Reloaded was injected; it is unrelated to 7th Heaven.

### Managed compatibility startup

`LegacySeventhHeavenRuntimeCompatibility` will retain only the early
`System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization`
compatibility switch as a defensive measure for direct launches. It will no
longer build, open, or signal `BlindSoldier.ManagedReady.<pid>`.

The existing process-wide Blind Soldier runtime lease remains the one-instance
authority. A stock 7th Heaven launch must produce exactly one Blind Soldier
initialization and one owner for audio descriptions, speech, tones, and input.

## Startup sequence

1. The player activates Play in an unmodified 7th Heaven installation.
2. 7th Heaven performs its normal `dinput.dll` AppLoader startup and loads FFNx.
3. FF7 loads Blind Soldier's executable-local `version.dll` proxy.
4. The proxy forwards Windows Version APIs immediately and starts its worker.
5. The worker validates the host and package, detects the 7th Heaven/FFNx path,
   and polls the validated field opcode table.
6. FFNx finishes address discovery and replaces the MESSAGE handler.
7. After the replacement handler is stable, the proxy starts the existing x86
   broker.
8. The broker injects Reloaded-II and loads Shared Hooks followed by Blind
   Soldier.
9. Blind Soldier acquires its runtime lease, initializes Prism, and installs or
   chains its accessibility hooks against the already initialized game.
10. On game exit, the broker restores only Reloaded pointer state that it owns;
    7th Heaven performs its ordinary cleanup without Blind Soldier modifying it.

## Logging and failure behavior

The x86 proxy log will record, without repeatedly spamming the same state:

- validated host identity and module range;
- whether the launch was classified as direct FF7 or 7th Heaven/FFNx;
- detection of each relevant loader/driver module;
- resolved opcode-table address;
- original and observed MESSAGE handler addresses;
- the stable-sample count and final readiness reason;
- elapsed readiness time;
- broker start, broker-ready result, and any timeout or validation failure.

No log may contain credentials or unrelated user data. Failure text must name
the failed stage, state that stock 7th Heaven/FFNx files were not changed, give
the absolute Blind Soldier log path, and stop FF7 before inaccessible gameplay.

## Packaging contract

The portable package retains the architecture-scoped x86 Version proxies at
the supported executable-local paths:

```text
ff7_en.exe.local/version.dll
ff7.exe.local/version.dll
ff7/workingdir/ff7_en.exe.local/version.dll
ff7/workingdir/ff7.exe.local/version.dll
```

The four copies are byte-identical. No Blind Soldier `winmm.dll`, `dinput.dll`,
7th Heaven executable, AppLoader/AppProxy/AppWrapper binary, FFNx driver, or
private patched dependency is included. Required Blind Soldier broker,
Reloaded-II, Shared Hooks, dual-runtime mod assemblies, Prism, assets, and the
private architecture-matched .NET runtime remain included as before.

Documentation must say that 7th Heaven and FFNx can be installed from their
normal official source/release flow and that Blind Soldier does not require a
special build. It must not instruct players to patch 7th Heaven or manually run
the broker.

## Verification strategy

### Unit and fixture tests

- Resolve the opcode table from valid fixture bytes and reject malformed call,
  table, page-permission, module-range, and handler evidence.
- Keep a detected 7th Heaven launch in the FFNx-required branch while FFNx is
  temporarily absent.
- Require a stable out-of-host MESSAGE handler before declaring FFNx ready.
- Permit the stable original handler only after the bounded direct-launch
  discovery interval.
- Cover readiness timeout, process exit, delayed FFNx appearance, handler
  instability, and repeated observations.
- Prove the old `BlindSoldier.ManagedReady` string and contract are absent from
  native and managed production binaries and package contents.
- Preserve all 17 Version exports and representative forwarding behavior.
- Preserve host validation, root discovery, one-shot broker start, pointer
  lease, ordered Shared Hooks/mod configuration, and x64 tests.

### Binary verification

Ghidra and PE inspection of the final x86 `version.dll` must confirm:

- PE32 x86 architecture;
- exactly the intended Version exports;
- no WinMM forwarding surface;
- no embedded Reloaded, CoreCLR, or 7th Heaven code;
- readiness references to validated FF7 structures and recognized FFNx module
  names;
- no code that writes to 7th Heaven or FFNx files; and
- the existing broker remains the only component using remote-process
  injection APIs.

### Live acceptance matrix

Testing uses a clean stock 7th Heaven 4.5.2 build or release, not the previously
patched local copy:

1. Direct supported 2013 x86 FF7 without 7th Heaven.
2. Supported x86 FF7 launched by stock 7th Heaven with FFNx and no optional
   gameplay mods.
3. The same stock 7th Heaven launch with at least one visible IRO mod enabled.
4. Echo-S enabled, verifying its pre-movie pages and opening movie plus exactly
   one Blind Soldier audio-description stream.
5. Missing FFNx during a 7th Heaven-classified launch, which must fail closed
   with an accessible diagnostic rather than inject early.
6. Normal exit and forced FF7 termination, proving pointer restoration and
   ordinary 7th Heaven cleanup.
7. Steam 2026 x64 launcher and gameplay smoke test, proving no regression.

Each successful x86 launch must prove menu sounds, Prism speech, navigation,
selected 7th Heaven mods, FFNx rendering/audio, and audio descriptions. The
test evidence must include the stock 7th Heaven build identity and hashes of
its relevant binaries before and after launch; those hashes must be unchanged.

## Acceptance criteria

- Blind Soldier works when launched through an unmodified official/source build
  of 7th Heaven and its normal FFNx dependency.
- No private 7th Heaven handshake or patched 7th Heaven binary is required.
- FFNx and selected 7th Heaven mods load normally.
- Blind Soldier initializes once, speaks through Prism, and produces no
  duplicate descriptions.
- Direct x86 remains functional.
- x64 behavior is unchanged.
- The release package contains no Blind Soldier WinMM proxy and no modified 7th
  Heaven or FFNx artifact.
- Every failure before accessibility initialization is accessible and
  fail-closed.

## Licensing boundary

The reference accessibility project is GPL-3.0. Blind Soldier may use its
observable architecture and independently verified runtime behavior as design
evidence, but this implementation will not copy its source. Any future decision
to incorporate code from that project requires an explicit project-wide
licensing decision before the code is imported.
