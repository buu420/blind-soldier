# Blind Soldier Registry-Free Direct-Extract Bootstrap Design

## Decision

Blind Soldier will publish one direct-extract ZIP for the supported Final
Fantasy VII layouts. Extracting the ZIP into the game root is the complete
installation procedure. The player does not run an installer, register an
Image File Execution Options debugger, edit Steam launch options, configure
7th Heaven, or start a separate Blind Soldier program.

The ZIP still contains automatic bootstrap components. "Portable" means the
package requires no persistent registry state or installation step; it does
not mean the accessibility mod can run without being loaded into the game.

The approved architecture is hybrid:

- Steam 2026 x64 starts through the existing accessible
  `FFVII_LAUNCHER.exe`, whose Play action invokes the packaged x64 Blind
  Soldier bootstrap.
- The legacy x86 game, including a launch initiated by 7th Heaven, loads an
  x86 `winmm.dll` forwarding proxy from its executable-specific `.local`
  directory. The proxy invokes the packaged x86 Blind Soldier bootstrap
  automatically.
- Both bootstrap paths activate the same portable `Reloaded-II` tree and the
  same dual-runtime Blind Soldier release.

This replaces the current portable package's installer and IFEO redirect. It
does not change the separate mod-manager distribution.

## Goals

1. A player can extract one ZIP into either supported game root and then use
   the Play action they already use.
2. Steam Play for the 2026 edition opens the accessible launcher; activating
   Play starts x64 FFVII with Blind Soldier already loaded.
3. Direct legacy x86 launches and 7th Heaven Play start x86 FFVII with Blind
   Soldier already loaded.
4. 7th Heaven keeps ownership of `dinput.dll`, and FFNx keeps its existing
   files and startup behavior.
5. The package writes no persistent registry entries and requires no
   administrator elevation.
6. A startup failure is announced through an accessible native Windows error
   and stops the game before inaccessible play begins.
7. The package remains architecture-correct, dependency-closed,
   deterministic, and independently verifiable.

## Non-goals

- Double-clicking the native 2026 `FFVII.exe` directly is not a supported
  launch path. That bypasses the Square Enix launcher by design. Steam Play
  and the accessible launcher's Play action are the supported x64 paths.
- The ZIP does not install or configure 7th Heaven or FFNx.
- The ZIP does not replace 7th Heaven's `dinput.dll` or package Blind Soldier
  as an IRO.
- The ZIP does not add an updater, uninstall wizard, or persistent install
  database.
- The ZIP does not support an arbitrary 7th Heaven working directory outside
  the extracted game tree. The bridge and portable runtime must be present in
  the tree containing the executable 7th Heaven launches.

## Verified constraints

The current accessible 2026 launcher ultimately starts `FFVII.exe` directly.
The current native Blind Soldier launcher then relies on IFEO to receive that
launch, writes a temporary Reloaded pointer, starts the game suspended,
injects the architecture-matched Reloaded bootstrapper, resumes the game, and
restores the prior pointer after exit.

Reloaded-II 1.30.3 reads its top-level `ReloadedII.json` from the current
Windows user's roaming AppData directory. Its `portable.txt` behavior moves
the Apps, Mods, Plugins, and User configuration directories beside the
configured launcher path, but does not relocate that initial pointer file.
The new bootstrap therefore preserves the current mutex, durable backup,
ownership check, crash recovery, and restore logic instead of leaving a
global pointer behind.

7th Heaven 4.5.2 writes and removes its own `dinput.dll` in the selected FFVII
executable directory around each launch. Its launch flow starts the configured
game executable directly. Blind Soldier cannot safely use `dinput.dll` in
that directory.

The supported x86 game imports WinMM. The current 7th Heaven and FFNx launch
paths do not claim `winmm.dll`, making a forwarding WinMM proxy the least
colliding local bootstrap point among the game's imported libraries. Windows'
documented unpackaged-app DLL redirection supports an
`<executable>.local/winmm.dll` directory, allowing the x86 proxy to be scoped
to `ff7_en.exe` or `ff7.exe` instead of placing a wrong-architecture DLL where
the x64 `FFVII.exe` could see it. The supported x86 fingerprints must have no
embedded application manifest that disables `.local` redirection without the
machine-wide `DevOverrideEnable` registry value; Blind Soldier will not set
that value. The proxy must preserve every WinMM function used by the supported
game and any code loaded into it.

## Package layout

The same archive is extracted at the root of either game layout:

```text
FFVII_LAUNCHER.exe
FFVII_LAUNCHER.exe.config
ff7_en.exe.local/
  winmm.dll
ff7.exe.local/
  winmm.dll
ff7/
  workingdir/
    ff7_en.exe.local/
      winmm.dll
    ff7.exe.local/
      winmm.dll
launcher_accessibility/
  native/x86/FFVII_LAUNCHER.prism.x86.dll
Blind-Soldier/
  Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe
  Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe
Reloaded-II/
  portable.txt
  Loader/X86/...
  Loader/X64/...
  Apps/...
  Mods/ff7.accessibility.reloaded/...
  Mods/reloaded.sharedlib.hooks/...
  User/...
LICENSES/...
README-PORTABLE.txt
```

The four `winmm.dll` entries are byte-identical x86 proxies. The two root
`.local` directories serve the supported 2013/x86 executable names. The two
nested directories serve those names in the x86 compatibility working
directory beneath the 2026 tree. Executable-specific redirection keeps the
proxy out of `FFVII_LAUNCHER.exe` and the x64 `FFVII.exe`; files irrelevant to
the selected runtime stay dormant.

The native bootstrap executables live below `Blind-Soldier/Bootstrap` instead
of at the archive root so they are implementation components, not alternative
programs the player is expected to run.

## Component boundaries

### Accessible 2026 launcher integration

The maintained accessible launcher source becomes a versioned build input in
the Blind Soldier repository rather than only a prebuilt release asset. Its
existing speech, keyboard interaction, UI Automation tree, settings behavior,
resources, and Prism output remain unchanged.

Only its game-start boundary changes. The Play action resolves paths relative
to `FFVII_LAUNCHER.exe`, validates
`Blind-Soldier/Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe` and `FFVII.exe`,
and starts the bootstrap with an explicit launch request. It never silently
falls back to `Process.Start("FFVII.exe")` if the bootstrap is unavailable.
Failure leaves the accessible launcher open and presents a standard accessible
Windows error describing the missing or failed component.

The launcher does not write registry state, modify Steam, or contain Reloaded
injection code. It delegates that responsibility to the x64 bootstrap.

### x86 WinMM forwarding proxy

The proxy exports the complete WinMM surface required by the supported Windows
and FFVII environment and forwards calls to the real system WinMM library
loaded from an explicit Windows system-directory path. It never resolves the
real library through an unqualified search that could recurse into itself.

`DllMain` performs only loader-safe initialization: it records its own module
path, disables thread notifications, creates the minimum synchronization
state, and schedules bootstrap work without waiting under the loader lock.
The worker validates the host before doing anything else. Accepted hosts are
known x86 FFVII executable names and verified executable fingerprints. A host
such as `FFVII_LAUNCHER.exe`, a setup tool, or an unknown executable receives
normal WinMM forwarding only; Blind Soldier is not started in that process.

For a supported FFVII host, the proxy finds the package root using a bounded
relative search from its own `.local` directory. The search accepts only a
root that contains the expected x86 bootstrap, Reloaded loader, Blind Soldier
mod, and Shared Hooks identities. It searches only the small number of parents
required to leave `<executable>.local` and, when applicable,
`ff7/workingdir`; it never searches a drive or uses the registry.

The proxy starts the x86 bootstrap in attach mode and passes the current
process ID, executable path, package root, and a per-launch synchronization
identity. The first WinMM call made after process initialization waits for a
bounded bootstrap result. Success releases the call to the real WinMM
function. Failure displays an accessible native error and terminates FFVII
rather than allowing an inaccessible session to continue.

### Architecture-matched bootstrap broker

The existing native launcher is refactored into one shared implementation with
two modes:

- `launch`: create the named game executable suspended, inject the matching
  Reloaded bootstrapper, resume it, wait for exit, and return the game result;
- `attach`: open the already-starting supported FFVII process, inject the
  matching Reloaded bootstrapper, signal the proxy that accessibility is
  active, wait for the game to exit, and return.

The x64 executable is used only in launch mode by the accessible 2026
launcher. The x86 executable is used in attach mode by the WinMM proxy. Both
validate their own PE architecture, the target architecture, executable
identity, runtime files, mod entry assembly, Shared Hooks assembly, and
application configuration before injection.

The broker owns the Reloaded pointer lease for the complete game lifetime. It
serializes access with the existing per-pointer mutex, recovers a durable
backup left by an interrupted prior launch, writes the portable pointer only
after validation, and restores the exact prior state after normal exit or a
target-process crash. It refuses to overwrite or restore a pointer whose
contents changed externally.

### Portable Reloaded configuration

The archive includes `Reloaded-II/portable.txt`. Each bootstrap writes or
validates an application configuration for the actual executable path and
enables mods in this order:

1. `reloaded.sharedlib.hooks`
2. `ff7.accessibility.reloaded`

The x86 runtime accepts `ff7_en.exe` for the supported 2013 Steam executable
and `ff7.exe` for a supported 7th-Heaven-converted executable. Names alone are
insufficient: stock builds use a SHA-256 allowlist, while a generated 7th
Heaven executable must match an x86 PE structural fingerprint that includes
the expected FFVII sections, WinMM imports, and verified game-code signatures.
The x64 runtime accepts only the verified Steam 2026 `FFVII.exe`. Blind
Soldier's `SupportedAppId` metadata and generated application configurations
must agree with those identities.

No absolute development-machine path is stored in the archive. Configurations
are resolved and, when necessary, regenerated from the extraction root at
launch.

## Startup flows

### Steam 2026 x64

1. Steam starts `FFVII_LAUNCHER.exe` normally.
2. The accessible launcher speaks and exposes its existing controls.
3. The player activates Play.
4. The launcher starts the x64 bootstrap in launch mode for adjacent
   `FFVII.exe`.
5. The bootstrap validates the portable tree, acquires the Reloaded pointer
   lease, creates FFVII suspended, injects the x64 Reloaded bootstrapper, and
   resumes FFVII.
6. Reloaded enables Shared Hooks and Blind Soldier before game interaction.
7. When FFVII exits, the broker restores the prior Reloaded pointer and exits.

### Legacy 2013 x86 direct launch

1. Steam or the player starts the x86 FFVII executable normally.
2. Windows applies the executable-specific `.local` redirection and resolves
   its forwarding `winmm.dll` while loading FFVII.
3. The proxy verifies that the host is supported and starts the x86 bootstrap
   in attach mode.
4. The bootstrap validates the portable tree, acquires the Reloaded pointer
   lease, injects the x86 Reloaded bootstrapper, and signals success.
5. WinMM forwarding continues and the game starts with Shared Hooks and Blind
   Soldier active.
6. The broker waits outside the game process and restores the prior pointer
   when the game exits.

### 7th Heaven x86 launch

1. The player activates Play in 7th Heaven.
2. 7th Heaven places its own `dinput.dll` and starts its configured x86 FFVII
   executable.
3. FFVII loads both 7th Heaven's `dinput.dll` and Blind Soldier's independent,
   executable-specific `.local` WinMM proxy.
4. The x86 proxy and bootstrap complete the same attach flow used by a direct
   legacy launch.
5. 7th Heaven, FFNx, Shared Hooks, and Blind Soldier coexist without either
   project replacing the other's loader file.
6. On exit, 7th Heaven performs its normal wrapper cleanup while the Blind
   Soldier broker restores only the Reloaded pointer state it owns.

## Error handling and accessibility contract

Startup is fail-closed. The new package removes the current behavior that can
announce missing files and then launch the game without Blind Soldier.

Before FFVII becomes interactive, the relevant launcher, proxy, or broker must
reject and clearly identify:

- an absent or unsupported game executable;
- a wrong x86/x64 pairing;
- a missing or changed bootstrapper, loader, mod, Shared Hooks, or Prism file;
- an unknown host fingerprint;
- a package root that cannot be resolved unambiguously;
- an application-configuration write failure;
- a Reloaded pointer conflict that cannot be recovered safely;
- an injection timeout or `LoadLibraryW` failure; and
- a second launch that cannot safely obtain the pointer lease.

Errors use a standard top-level Windows error dialog with the Blind Soldier
name, a concise cause, the corrective action, and the absolute log path. The
dialog must be screen-reader accessible. The accessible 2026 launcher also
speaks the failure through its existing Prism path. Every native component
writes an append-safe log beneath the extraction tree and includes a launch
identity so one startup can be traced across launcher, proxy, and broker.

## Compatibility and ownership rules

- Blind Soldier never creates, changes, or deletes a 7th Heaven `dinput.dll`.
- Blind Soldier never changes FFNx configuration or graphics/audio driver
  files.
- The WinMM proxy loads the real system library by absolute path and forwards
  normal behavior even in an unsupported host.
- A pre-existing `winmm.dll` inside either supported `<executable>.local`
  directory is a collision. The README must warn the player not to overwrite
  an unknown proxy there. Supporting proxy chaining is outside this release
  because extraction cannot safely preserve and rename an unknown existing
  file.
- The accessible launcher replacement is intentional. Steam's Verify Files
  action is the documented rollback for restoring the stock 2026 launcher.
- Removing the four packaged `<executable>.local/winmm.dll` files, restoring
  the stock launcher, and deleting the added Blind Soldier/Reloaded files
  disables this distribution; no registry cleanup is required.

## Build and verification strategy

Implementation follows test-first changes around each boundary.

### Native unit and fixture coverage

- Host validation accepts only `ff7_en.exe` and `ff7.exe` plus their defined
  cryptographic or structural fingerprints, and rejects the accessible
  launcher and arbitrary x86 hosts.
- PE-resource tests prove every supported x86 fingerprint is compatible with
  `.local` redirection without `DevOverrideEnable`.
- Root discovery succeeds from each root and nested `.local` directory,
  rejects incomplete and ambiguous trees, and never searches beyond its
  bound.
- WinMM export tests compare the proxy's named and ordinal exports with the
  supported system WinMM contract and exercise representative forwarded calls.
- The proxy loads the real library from the system directory and cannot recurse
  into itself.
- Launch and attach modes reject cross-architecture targets.
- The proxy/broker synchronization covers success, timeout, helper crash, game
  exit during startup, and duplicate launch attempts.
- Pointer tests cover no prior file, prior user file, stale owned pointer,
  durable-backup recovery, externally changed pointer, crash restoration, and
  concurrent launch serialization.
- All accessibility-critical failures prove that FFVII does not continue.

### Launcher coverage

- The Play action invokes the adjacent x64 bootstrap with an exactly quoted
  `FFVII.exe` path and does not invoke FFVII directly.
- Missing bootstrap, missing game, nonzero bootstrap exit, and start failure
  keep the launcher accessible and report the error.
- Existing launcher speech, keyboard, UI Automation, combo-box, slider, Enter,
  and Prism tests remain green.

### Package coverage

- The archive contains all four byte-identical x86 proxies at the exact
  executable-specific `.local` paths, both architecture-matched brokers, the
  accessible launcher, Prism, complete loader closures, Shared Hooks, both mod
  assemblies, assets, licenses, `portable.txt`, and documentation.
- The archive contains no installer executable and no IFEO setup instructions.
- No archive file or JSON configuration contains a development-machine path.
- PE-machine checks cover every executable, native DLL, bootstrapper, and
  ReadyToRun assembly.
- Two clean builds produce identical member lists, hashes, and ZIP hashes.

### Binary and live validation

Ghidra analysis of the supported x86 hosts, final proxy, and both brokers
confirms `.local` compatibility, the intended host guard, bounded root
discovery, absolute system-WinMM load, export forwarding, architecture checks,
remote `LoadLibraryW`, synchronization, and absence of registry-writing code.

Live validation uses isolated copies of each supported layout:

1. 2013 x86 direct launch;
2. 2013 x86 launched by 7th Heaven;
3. x86 compatibility working directory beneath the 2026 root launched by 7th
   Heaven;
4. Steam 2026 launcher Play into x64 FFVII;
5. startup with a missing required accessibility file; and
6. restoration of a pre-existing Reloaded pointer after normal exit and a
   forced game crash.

Each successful launch must produce exactly one Blind Soldier initialization,
one set of audio descriptions, and working speech/navigation. The 7th Heaven
tests must also prove that its selected mods and FFNx still load.

## Release and documentation contract

The release publishes:

- `Blind-Soldier-Portable.zip`
- `Blind-Soldier-Portable.zip.sha256`

`README-PORTABLE.txt` begins with the two-step user flow:

1. Extract the ZIP into the Final Fantasy VII game folder, allowing the
   packaged files to reach their listed relative paths.
2. Start the game normally from Steam or 7th Heaven.

The README describes the supported roots, notes that no installer or
  administrator access is required, warns about a pre-existing unknown
`<executable>.local/winmm.dll`, explains the supported x64 launch boundary,
and gives rollback instructions. It does not tell the player to run a
bootstrap executable.

## Acceptance criteria

- A clean 2013 installation loads Blind Soldier from an ordinary launch after
  extraction only.
- The same 2013 installation loads Blind Soldier when Play is activated in 7th
  Heaven, with 7th Heaven and FFNx still functional.
- A supported x86 compatibility working directory beneath the 2026 root loads
  Blind Soldier through 7th Heaven after the same archive is extracted at the
  2026 root.
- Steam Play opens the accessible 2026 launcher, and its Play action starts
  x64 FFVII with Blind Soldier active.
- No supported path requires the player to run an installer or bootstrap,
  change a registry value, enter an administrator password, or configure a
  separate launch command.
- The package creates no persistent registry entry.
- A required accessibility startup failure stops FFVII and provides an
  accessible explanation and log path.
- 7th Heaven retains its own `dinput.dll`; Blind Soldier uses only the guarded,
  executable-specific `.local` x86 WinMM proxy for that runtime.
- One game launch produces one Blind Soldier initialization and never duplicate
  audio descriptions.
- The portable Reloaded pointer is restored exactly after normal exit and a
  target-process crash.
- The public ZIP matches its SHA-256 sidecar and passes the full deterministic
  package, PE, dependency, Ghidra, and live-runtime verification gates.

## Rejected alternatives

### Separate Blind Soldier launcher

This would avoid DLL proxy work but would require the player to bypass Steam
or 7th Heaven Play. It fails the central requirement.

### `dinput.dll` proxy

7th Heaven owns and rewrites `dinput.dll` during its normal launch flow. Using
the same filename would create an avoidable loader conflict.

### x64 game DLL proxy

A second DLL proxy beside the 2026 executable could catch direct
`FFVII.exe` launches, but likely proxy candidates overlap graphics, input, or
overlay mods. The accessible launcher already provides a controlled and
accessible x64 start boundary, so the added collision surface is unjustified.

### 7th Heaven IRO-only integration

An IRO could request a native bridge through 7th Heaven, but the player would
still need to import and activate that IRO. It would not provide one
copy-and-play ZIP for both runtimes.

### Forking Reloaded's configuration library

Changing Reloaded-II to read a Blind Soldier-specific environment variable
would remove the transient pointer lease, but would create a permanent fork of
an otherwise pinned third-party runtime. The existing ownership-aware pointer
swap is already implemented and tested, so extending the broker is the smaller
and more maintainable change.

## Research references

- [Microsoft: Dynamic-link library redirection](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-redirection)
- [Microsoft: Dynamic-link library security](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security)
- [Microsoft: File System Redirector](https://learn.microsoft.com/en-us/windows/win32/winprog64/file-system-redirector)
- [Reloaded-II: Injection Methods](https://reloaded-project.github.io/Reloaded-II/InjectionMethods/)
- [Reloaded-II: Project Structure](https://reloaded-project.github.io/Reloaded-II/ProjectStructure/)
