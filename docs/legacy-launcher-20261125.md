# FINAL FANTASY VII (2013) accessible launcher and standalone compatibility

November 25, 2026. This change adds an accessible replacement for the Steam 2013
launcher, `FF7_Launcher.exe`, with a verified install and uninstall module. It
records what was found about standalone 2013 compatibility in the real Steam
files. No game tree, save or setting on this PC was changed, and nothing was
published. The launcher is not yet released.

Evidence lives in `FFVII-ActiveBuild/legacy-launcher-20261125/`. Analysis copies
and scratch builds are under `D:\BlindSoldier-localized-model-launcher-20261125`.

## What the 2013 edition is

- Steam app 39140, "FINAL FANTASY VII (2013)", is still available to owners, but
  Square Enix asked Steam to retire it from sale. Steam runs `FF7_Launcher.exe`
  with no arguments from `installdir = FINAL FANTASY VII`, public build 115956.
  Language depots are 39143 to 39146. There is no Steam Cloud (`ufs`) section.
  Steam's Deck review lists launcher interaction issues.
- The app is now installed at `D:\SteamLibrary\steamapps\common\FINAL FANTASY
  VII` (manifest updated November 25, 2026, build 115956). Its files were only
  read; analysis used copies.

| File | SHA-256 | Bytes | Notes |
|---|---|---|---|
| `ff7_en.exe` | `68CF1B8C1D732CC00A1DDB02CED161F7C94B06680D9E8641A11C7361417375C2` | 6421856 | x86, Authenticode-signed, SteamStub 3.0, external `ff7_en.exe.manifest` (VC90 CRT), not large-address-aware. SHA-1 `1C9A6F4B…` is 7th Heaven's "Steam" entry. |
| `AF3DN.P` | `1763359FFEE6C894EFF8DFEFB6E9A90769942CD30C9F480C471C97AB44271EF0` | 195752 | Original DotEmu driver, x86, signed, not packed. |
| `FF7_Launcher.exe` | `7C686CF42CAB3639EDA3EDF804268A454C5AF2BCB5C931094521315A7B80B055` | 1911992 | Qt 4 with QtWebKit, x86, signed, SteamStub 2.1. |
| `steam_api.dll` | `ABFEDD473B3F4A9597BBDC90D20F4B6F696BB2EBB937A03177461DF695430AD6` | 104872 | Loads `steamclient.dll` with `LOAD_WITH_ALTERED_SEARCH_PATH`. |

- `C:\Games\Final Fantasy VII` is 7th Heaven's copy of the 2026 re-release, not
  the 2013 edition. Its `ff7_en.exe` is the 4 GB copy of the 2026 bundled
  executable (SHA-256 `4274AB2D…`), and its root `FFVII_LAUNCHER.exe` is the stock
  2026 launcher.
- The user's OneDrive Documents folder still holds 2013 launcher data in
  `Square Enix\FINAL FANTASY VII Steam\`, with one `user_<Square Enix ID>`
  folder. That folder has `metadata.xml` but no save files. Each empty block's
  signature equals the MD5 of the ID. Its `ff7input.cfg` is byte-identical to
  the official default controls described below.

## How the original launcher and driver work

Ghidra 12.1.2 decompiled the real `AF3DN.P`, and a Steamless-unpacked scratch
copy of the real `FF7_Launcher.exe`.

Driver start-up:

- The driver initializes from its `new_dll_graphics_driver` export, not from
  `DllMain`. It opens the four semaphores `ff7_{game,launcher}{Can,Did}ReadMsgSem`
  and the mapping `ff7_sharedMemoryWithLauncher`.
- Without them it shows "Please use the launcher 'FF7_Launcher.exe' ..." and
  exits.
- It then calls `SteamAPI_RestartAppIfNecessary(39140)` and `SteamAPI_Init`, so
  Steam must be running.
- The SteamStub stub in `ff7_en.exe` also requires Steam.

Launcher-to-game messages are read until message 24:

| Message | Meaning |
|---|---|
| 10 | Save folder. The driver opens `saveNN.ff7` as `<folder>\saveNN.ff7` and does not create the folder. |
| 11 | Documents folder for `ff7input.cfg`. The default is `Documents\Square Enix\FINAL FANTASY VII Steam`. |
| 12 | Install folder |
| 13 | Locale |
| 14 | Achievement definitions |
| 15 | In-game strings |
| 16 | Key table |
| 18 | Version |
| 21 and 22 | Cloud saving on or off. On is the default. |

Game-to-launcher messages use the same identifier-then-payload layout:

| Message | Meaning |
|---|---|
| 1 | Achievement earned, with its index. |
| 2 | Cloud sync request before the save screen, sent only while cloud saving is on. |
| 3 | `[0, file, slot]` after a save, and `[1, file, slot]` after a load. |
| 4 | Request for the start-up information. |

The original launcher:

- Gets the user ID from the QtWebKit Square Enix sign-in and remembers it in
  `lastLogin.dat`. That file is DPAPI-protected and bound to the Steam ID.
- Creates `user_<id>` with a default `metadata.xml`.
- On message 3 `[0, file, slot]`, sets that slot's timestamp to milliseconds
  since 1970 and the block's signature to lowercase MD5(`saveNN.ff7` + ID in
  UTF-8). It then rewrites the whole file: QDom output, two-space indent, LF line
  endings.
- At every sign-in, deletes any save whose signature differs from
  `metadata.xml`, including a save in a block recorded as empty. If
  `metadata.xml` is unreadable, every block resets first, so every save is
  deleted.
- Is the only component that writes `metadata.xml` for the original driver. The
  driver never reads it.

The other launchers:

- FFNx maintains `metadata.xml` itself.
- FF78Launcher acknowledges game messages without updating `metadata.xml`, so
  with the original driver its saves are exposed to that deletion.
- FF78Launcher and FFNx both take the first `user_*` folder. With none, FF78Launcher
  sends an undefined folder name.

`ff7input.cfg`: the official default controls are byte-identical to FF78Launcher's
builder (SHA-256 `A41E4A1D…`). FF78Launcher leaves the file alone by default, and
so does this launcher.

## Does the original driver load Blind Soldier?

The Blind Soldier x86 loader is `version.dll` beside `ff7_en.exe`. Windows loads
it only if some module imports `VERSION.dll`.

- Neither `ff7_en.exe` nor the original `AF3DN.P` imports `VERSION.dll`. The other
  2013 modules don't either, including `d3dx9_29`, `steam_api`, `avcodec-53`,
  `avformat-53`, `avutil-51` and `libvgmstream`. The exception is the launcher's
  own `QtWebKit4.dll`, where the proxy forwards only.
- FFNx's `AF3DN.P` imports `VERSION.dll` directly, so FFNx always loads the proxy.
- The original driver imports `d3d9.dll`. Microsoft's symbol-server copies
  (indexed by Winbindex) show which x86 `d3d9.dll` builds import `VERSION.dll`.
  Receipt: `d3d9-version-imports.json`.
  - Every sampled Windows 11 24H2 build imports it, from 10.0.26100.1 to
    10.0.26100.9818. This PC's 10.0.26300 copy is the same file as 26100.9818.
  - Windows 10 1507, 1809, 21H2 and 22H2, and Windows 11 22H2 and 23H2, don't.
    They use only the KernelBase version API sets.

So with the original driver, Blind Soldier loads at start-up only on Windows 11
24H2 or later. Elsewhere the game would run silently.

The launcher closes that gap without `.local`, without the registry, and without
touching Direct3D or DirectInput. After the handshake, every module the game
imports is loaded. The launcher then reads the game's module list:

- If the sibling proxy is loaded, it is left alone, so it never starts twice.
- If not, the launcher calls `LoadLibraryW` on a thread in the game process,
  using the proxy's full path. The proxy's own start-up then runs exactly as it
  does for a dependency load.
- If that fails, the launcher closes the game and explains why, the same
  fail-closed rule the proxy follows.
- A game that exits by itself during the check is not a failure. One example is
  a game started outside Steam, which restarts itself through Steam. Its exit is
  reported like any other early exit.
- The module check and the load go through a duplicate of the launch's own
  process handle, never a process ID, so they cannot reach another process.

ReShade, 7th Heaven and FFNx paths are unaffected. 7th Heaven starts the game
itself, and the check runs only for the original driver with `ff7_en.exe` and a
complete Blind Soldier install.

Fixture and native proof:

- `loadprobe.exe` statically imports `d3d9.dll`. On this PC it mapped the
  sibling 1.0.2 proxy (`7AF7FB06…`), and the proxy logged "native Version
  bootstrap active". Without the proxy it mapped System32's copy.
- `sleepprobe.exe` imports only KERNEL32 and had no `version.dll`. The
  final bundle's `RemoteLibraryLoader` (`FF7_Launcher.exe` `CB43EA33…`) loaded the
  real proxy into it in 82 ms, through its owned process handle.
  The proxy then loaded the system copy and logged "native Version bootstrap
  active" and "unrelated process; bootstrap skipped".
  Receipt: `late-version-proxy-load.json`.
  Script: `tools/Prove-LateVersionProxyLoad.ps1`.

## Host identity

The shipped native validator accepts the real `ff7_en.exe` as `SevenHeavenX86`
through the `7th-heaven-converted-output` profile. That profile's sample hash
is the unmodified Steam file, so its name is misleading.

The same bytes named `ff7_fr.exe`, `ff7_de.exe` or `ff7_es.exe` are rejected.
The boundary for the other language executables is unchanged.

## Root causes

Evidenced:

1. **No accessible 2013 launch path existed.** The 2013 ZIP and the manager's
   `ffviiold` entry installed only the x86 loader. The manager plays through
   `steam://run/39140`, so play always passed through the original launcher.
2. **Non-English 2013 executables never load Blind Soldier.** The proxy and host
   validator accept only `ff7_en.exe` or `ff7.exe`. Supporting `ff7_fr`, `ff7_de`
   and `ff7_es` needs engine offsets that have not been researched.
3. **The repository's `legacyStockX86` identity (`4274AB2D…`) is the 2026
   re-release executable.** The real 2013 file passes only through the
   structural profile above.
4. **With the original driver, the loader depends on the Windows version.**
   Without FFNx, Windows 10 and Windows 11 23H2 or earlier never load the proxy.
   This corrects the earlier note that the loader needed FFNx. Fixed by the
   launcher's load check.
5. **Saves could be lost when switching launchers.** Without `metadata.xml`
   updates, the original launcher deletes saves made elsewhere. Fixed by doing
   what the original launcher does after each save.
6. **A first run without Square Enix sign-in had no save folder.** The driver
   would have nowhere to save, and its sign-in page is QtWebKit. Fixed by
   Create local save folder.

Unconfirmed:

- Speech on the late-load path, used on Windows 10 and Windows 11 23H2 or
  earlier. Only the fixture probe above covers it. The parent reports startup
  and speech in an isolated full-game copy of the original 2013 game with
  released 1.0.2, on this Windows 11 26300 PC, where `d3d9.dll` loads the
  proxy. That run also needed the parent's Reloaded `DelayInjector` fix.
- Full proxy start-up in a real `ff7_en.exe` after a late load: host check,
  readiness, broker and Reloaded.
- Which Blind Soldier features work without FFNx. Several readers are bound to
  FFNx builds and report that they are unavailable without it.
- Whether Square Enix sign-in or cloud saving still works in 2026.
- Whether Steam achievements unlock with the original driver. Its
  `SteamAchievements` class and `SteamUserStats` call suggest they do.
- Screen-reader access to the original launcher's window.

## The accessible launcher

`launcher/Ff7.Launcher2013.Accessible` builds `FF7_Launcher.exe`, an x86 .NET
Framework 4.8 Windows Forms launcher. It reuses the 2026 launcher's server-side
UI Automation buttons and sliders.

Main window:

- Play, Settings, Original launcher and Exit come first in the Tab order,
  followed by the read-only Status box (Alt+A). Enter plays and Escape exits.
- Text follows the game executable's language: English, French, German or
  Spanish. Mnemonics are unique per window in each language.
- Status changes are announced through UI Automation notifications. Errors
  appear in message boxes.

First run:

- When no `user_*` folder exists and the driver is present, a **Create local
  save folder** button (Alt+L) comes first and has the focus. The opening
  announcement says "Ready. Press Enter to create a local save folder."
- Pressing it creates `user_local` with the default `metadata.xml` for ID
  `local`, then selects and remembers the folder. Focus moves to Play and the
  launcher announces where the folder is.
- The launcher never replaces existing files. Play without a folder explains
  the button.

Status box:

- Language and executable, driver and FFNx version, and the save folder and
  number of saves.
- "Cloud saving: off. Saves stay on this PC, and this launcher does not sign in
  to Square Enix." Shown for the original driver and FFNx.
- For the original driver, that it updates `metadata.xml` so the original
  launcher keeps the saves.
- Blind Soldier warnings, a start outside Steam, and whether the original
  launcher is available.

Play:

- FFNx or an unrecognized driver starts the game directly.
- The original driver gets the handshake with cloud saving off. The launcher
  then runs the Blind Soldier load check. It updates `metadata.xml` after each
  save the game reports, and signs those saves again after the game closes.
- Missing or unreadable `metadata.xml` is rebuilt with every existing save
  signed. The first replacement keeps `metadata.xml.blind-soldier-original`.
- A launch without a save leaves the folder byte-for-byte unchanged.
- `ff7video.cfg` and `ff7sound.cfg` are created only when missing.
  `ff7input.cfg` is never written.
- If a start fails, times out or is cancelled, the launcher stops the one game
  process it started, through that process's own handle. It waits for that
  process to exit and releases the launcher objects, so Play works again.
  Other running games are never touched. Before this change, a game stuck in
  the handshake kept those objects open, and a retry reported "already
  running".
- Start-up information is checked before any process starts.
- Pressing Exit, or closing the window, while the game starts cancels the start.
  The launcher announces "Stopping the FINAL FANTASY VII start-up. The launcher
  closes when it has stopped." It closes once its game has stopped. A game that
  finished starting at that moment is stopped too, because without the
  launcher it would hang at its first save.
- After a successful start the launcher only waits; a running game is never
  stopped by it.

Settings and Original launcher are unchanged from the first version. Original
launcher keeps Square Enix sign-in, cloud saves and the online profile
available.

`FF7Launcher2013Install.psm1` installs the bundle only into a closed 2013 folder.
It keeps `FF7_Launcher.original.exe` and `.config.original` with hashes in
`Blind-Soldier\Launcher2013\install-manifest.json`. Uninstall restores only
unchanged files.

## Verification

C# suite, Release: 113 of 113 tests pass (`csharp-tests-lifecycle.log`), up from 70.
New tests cover:

- The exact original `metadata.xml` text, signing and parsing rules.
- Updating one block only, the one-time backup, and rebuilding without losing
  saves.
- The final re-sign. A mutation that removed it failed.
- Game-message decoding and dispatch after acknowledgement.
- A cross-process fake game that writes a save and reports it.
- Byte-for-byte preservation of the save folder.
- First-run creation, refusal and UI Automation Invoke of the new button.
- The loader rules, including a game that closes by itself, and a real remote
  load into a running process.
- Ownership: a game wrapper that reports another running process's ID still has
  its modules read from, and the proxy loaded into, only the started game. The
  previous ID-based code failed this test by loading into the other process.
- Start-up lifecycle:
  - A handshake timeout stops only the started game, and Play works again.
  - A real stuck game that holds the launcher objects open is stopped, while a
    second copy of the game keeps running.
  - Cancellation before, during and right after the process starts stops only
    the started game.
  - An unexpected failure after the start stops the started game.
  - Invalid start-up information never starts a process.
  - Exit during start-up waits, announces once, and then closes.
  - A start that finished as Exit was pressed is stopped.
  - Exit when idle closes at once.
  - A successful game is never stopped.
  - Mutations that removed the timeout stop or the race stop failed.

Native host test:

- Unchanged without `BLIND_SOLDIER_STOCK_2013_FF7_EN`.
- With a private copy of the real `ff7_en.exe`, it passes as `SevenHeavenX86`
  through `7th-heaven-converted-output`, and rejects the French, German and
  Spanish names.
- With one signature byte changed, it fails.

Pester 4.10.1: the two bundle tests and the 13 install tests pass, 15 of 15, in
both Windows PowerShell 5.1 and PowerShell 7.6.9.

- The bundle architecture check reads the PE machine (0x14C, as the builder
  checks) and the CLR `32BITREQUIRED` flag. `AssemblyName.ProcessorArchitecture`
  is None under PowerShell 7, and machine 0x14C alone also matches AnyCPU. A
  freshly compiled AnyCPU library serves as the negative control.
- Under PowerShell 7, `ConvertFrom-Json` turned the recorded install time into a
  DateTime, so a repair rewrote the ownership record. The module now keeps the
  recorded text.
- The install tests refuse to run while any `ff7_en` process exists, by design.

Fixture proofs: the two Version proxy loads described above, and the decompiled
contract.

Not verified: live 2013 gameplay, Blind Soldier speech in any 2013 game, audible
screen-reader output from the new button, the original launcher under SteamStub,
and Steam overlay or achievements with the original driver.

## Integration and deployment

Packaging dependencies are unchanged in shape.

The 2013 ZIP and the manager's `ffviiold` source carry:

- The launcher bundle under `Blind-Soldier\Launcher2013\`:
  `FF7_Launcher.exe`, `FF7_Launcher.exe.config` and `launcher-2013-bundle.json`.
- `FF7Launcher2013Install.psm1` with its install and restore commands.
- The existing x86 payload: root `version.dll`, the x86 broker, the private .NET
  9 x86 runtime, and `Reloaded-II` in portable mode.

The launcher has no new runtime dependencies. It uses only .NET Framework 4.8 and
kernel32.

CI should run:

- The C# suite.
- Both Pester suites, on a machine without `ff7_en` running.
- The native host tests, optionally with `BLIND_SOLDIER_STOCK_2013_FF7_EN`
  pointing at a private copy of the real executable.

A live listening test is needed before release:

- One test on Windows 10, or on Windows 11 23H2 or earlier, to exercise the late
  load.
- One on Windows 11 24H2 or later, to exercise the `d3d9` load.
- Each with the original driver, from Steam, with a screen reader running, and
  including a save.

## References

- FF78Launcher: https://github.com/julianxhokaxhiu/FF78Launcher and https://github.com/ProjectXsent/FF78Launcher
- FFNx 1.25.2 (`0adf0b3`), `src/common.cpp`, `src/metadata.cpp`: https://github.com/julianxhokaxhiu/FFNx
- 7th Heaven `4c373d1`: https://github.com/tsunamods-codes/7th-Heaven
- Accessibility Mod Manager `f8fcf28`: https://github.com/RealAmethyst/AccessibilityModManager
- Winbindex `d3d9.dll` index and the Microsoft symbol server: https://winbindex.m417z.com/?file=d3d9.dll
- NirSoft DLL imports for Windows 8 and Windows 10 `d3d9.dll`: https://www.nirsoft.net/dll_information/windows8/d3d9_dll.html and https://windows10dll.nirsoft.net/d3d9_dll.html
- Steam guides 3684396301 and 3676961263: https://steamcommunity.com/sharedfiles/filedetails/?id=3684396301
