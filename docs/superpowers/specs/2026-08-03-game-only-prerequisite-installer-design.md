# Blind Swordsman Game-Only Prerequisite Installer Design

## Goal

Running `Blind-Swordsman-Setup.exe` on a supported Windows PC must require the
player to supply only a supported Final Fantasy VII installation. Reloaded-II,
Reloaded Shared Hooks, and the architecture-matched .NET desktop runtime are
implementation details that setup installs or repairs. 7th Heaven and FFNx
remain optional integrations.

This supersedes the prerequisite section of the original accessible installer
design, which deliberately made an existing Reloaded-II installation blocking.

## Selected approach

The verified Blind Swordsman runtime ZIP will contain immutable copies of:

- Reloaded-II `1.30.3` `Release.zip`, expanded as a complete portable loader;
- Reloaded Shared Hooks `1.16.3`, expanded for x86 and x64;
- Microsoft .NET Desktop Runtime `9.0.8` installers for Windows x86 and x64;
- the upstream licenses, source locations, versions, sizes, and cryptographic
  digests for each redistributed component.

The release builder downloads only the exact locked upstream artifacts, checks
their published or repository-pinned hashes, validates archive paths, expands
them into a deterministic prerequisite tree, and includes that tree in the
existing signed-by-hash runtime payload. It never follows a `latest` URL.

Two alternatives were rejected. Downloading prerequisites during installation
would add several external failure points after the user has already downloaded
setup. Launching the official Reloaded installer would add an inaccessible
second wizard and would not configure Blind Swordsman automatically. Bundling
the pinned inputs costs more download space but provides the reliable one-step
experience requested.

## Fresh and existing installations

Preflight still validates the selected game before enabling Install. Missing
Reloaded-II, loader files, Shared Hooks, or .NET are reported as components
that setup will provide, not as blocking user dependencies. A missing or
unsupported FFVII installation remains blocking.

When no usable Reloaded registration exists, setup selects
`<GameRoot>\Reloaded-II`, installs the complete pinned portable distribution,
creates the required folders, and writes Reloaded's per-user
`%APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json` with absolute paths into
that root. No developer-specific location is used.

When a registered Reloaded root exists, setup reuses it. User-owned `Apps`,
`Mods`, `User`, and `Plugins` content is preserved. Core loader files are
updated from the coherent pinned distribution through a preflighted,
per-file transactional overlay. Replaced core files receive a recovery backup.
The Shared Hooks directory is preserved when already valid; a missing or
invalid owned `reloaded.sharedlib.hooks` package is transactionally replaced
after backing up the prior package. An unrelated directory or reparse point is
never overwritten.

The .NET 9.0.8 desktop runtime installer runs only for a detected architecture
whose global runtime is not already usable. An x86-only game installs/checks
only x86; an x64-only game installs/checks only x64; a dual-runtime game checks
both. Setup accepts normal success and reboot-required installer exit codes,
then verifies `hostfxr` and `Microsoft.WindowsDesktop.App` before proceeding.
The shared Microsoft runtime is deliberately retained on Blind Swordsman
uninstall because other applications may depend on it.

## Reloaded configuration and profiles

The injected Reloaded bootstrapper reads the global `ReloadedII.json` before
Blind Swordsman code can run. Provisioning therefore validates or writes these
paths before copying any bootstrapper into a game directory:

- x86 and x64 loader DLLs;
- x86 and x64 bootstrapper DLLs;
- Reloaded launcher;
- application, mod, plugin, and user configuration directories.

Existing non-path preferences are preserved. A changed settings file is backed
up and atomically replaced. A newly created configuration suppresses Reloaded's
first-launch tutorial because setup has already completed those steps.

Setup creates or updates a legacy x86 application profile as well as the
existing native x64 profile. Both enable Shared Hooks before Blind Swordsman.
When a legacy profile already contains other mods, their enabled state and
relative ordering are retained; only the missing required entries and current
game paths are added. Profile creation/replacement is recorded in install state
so uninstall can restore the exact prior profile or remove an unchanged profile
that setup created.

## Payload and deployment contract

The runtime ZIP gains `prerequisites/` beside the existing `package/` and
`launcher/` directories. A strict `dependency-bundle.json` identifies the
three upstream products and every executable archive by version, architecture,
size, URL, and digest. The setup payload validator rejects a missing manifest,
unexpected file, unsafe path, incorrect architecture, or incomplete loader
tree before invoking PowerShell.

`SetupOrchestrator` passes the validated prerequisite root to
`Install-FF7ReloadedMod.ps1`. A focused prerequisite module owns bundle
validation, .NET detection/installation, Reloaded overlay, Shared Hooks, and
global settings. The existing deployment script remains responsible for the
Blind Swordsman package, application profiles, game-directory loaders, the
accessible launcher, and structured install state.

Provisioned Reloaded and Shared Hooks remain installed during product
uninstall because they are shared infrastructure and may be used by other
mods. Blind Swordsman itself, its setup-owned application profiles, launcher
changes, and unchanged game-directory loader copies continue to follow the
existing cautious uninstall rules.

## Failure handling

All archive, identity, collision, reparse-point, profile, and destination
checks happen before the first related mutation. A failed core overlay restores
files already replaced in that overlay. A failed Shared Hooks replacement
restores its prior owned package. A failed settings write restores the prior
settings bytes. If a later Blind Swordsman step fails after prerequisites were
successfully provisioned, the usable shared prerequisites remain installed;
the mod/profile/launcher transaction still rolls back as before. The error and
the retained prerequisite state are written to the accessible setup log.

## Tests and acceptance criteria

Automated tests must prove all of the following before publication:

- preflight permits a valid x86-only, x64-only, and dual-runtime game when no
  Reloaded folder, Shared Hooks, or .NET runtime exists;
- the payload validator requires the exact prerequisite layout and rejects
  wrong-machine loader and incomplete bundle fixtures;
- fresh provisioning creates a coherent Reloaded root, global settings,
  Shared Hooks, and the architecture-appropriate legacy/native profile;
- x86-only provisioning never references or installs x64 inputs, and vice
  versa;
- existing user mods, profiles, preferences, and optional 7th Heaven/FFNx
  files remain byte-for-byte preserved except for the explicit profile merge;
- repair is idempotent, malformed/reparse-point targets are rejected, and a
  forced mid-transaction failure restores prior bytes;
- .NET installers are skipped when runtime 9.0.8 or newer is present and their
  results are revalidated when invoked;
- two clean release builds produce byte-identical payloads from the same locked
  inputs;
- Ghidra and PE validation identify the bundled ASI loaders, bootstrappers, and
  Shared Hooks entry assemblies as their claimed x86/x64 architectures;
- the full Research verification gate passes, a controlled game-only install
  fixture completes, and the public replacement release downloads anonymously
  with hashes matching the published channel manifest.

The README and installer dependency page must say plainly that FFVII is the
only external product prerequisite and that setup supplies Reloaded-II, Shared
Hooks, and .NET automatically.
