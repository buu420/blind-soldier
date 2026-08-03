# Blind Swordsman Accessible Windows Installer Design

## Goal

Ship Blind Swordsman through one conventional Windows executable that a blind
player can operate with a screen reader. The setup program must find and
validate the supported Final Fantasy VII installations, verify required
dependencies, download a known release payload from GitHub, and install or
update the mod without requiring a terminal, source checkout, .NET SDK, or
manual file copying.

The first public build is a prerelease. It supports the legacy Steam x86 path
and the current Steam x64 path. The existing native x64 parity gate remains
truthful: the setup identifies that profile as prerelease rather than silently
claiming the research matrix is release-ready.

## User experience

`Blind-Swordsman-Setup.exe` is a self-contained .NET 8 Windows x64 executable.
It uses ordinary WinForms labels, edit fields, check boxes, list controls,
progress bars, and buttons. It has no owner-drawn controls, image-only buttons,
custom keyboard model, or web view. Every interactive control has a useful
accessible name, a deterministic tab order, and a keyboard path. Changing
status is exposed through visible text and a native UI Automation notification
so screen readers receive progress and error information without focus hunting.

The setup flow is:

1. **Welcome and mode:** report whether this is a new install, update, repair,
   or uninstall and show the newest available version.
2. **Locations and dependencies:** show the detected FFVII folder and
   Reloaded-II folder in editable fields with Browse buttons. Show blocking and
   optional dependency results in a text-backed status list.
3. **Review:** name the x86 and x64 runtimes that will be configured and clearly
   identify the native x64 profile as prerelease when its parity gate is closed.
4. **Progress:** download, verify, stage, install, and report each operation.
5. **Completion:** summarize installed runtimes and provide Finish, View log,
   and Check for updates actions as appropriate.

The setup never requires the player to accept an extra native-x64 confirmation
box. The warning is informational; a validated detected runtime is installed as
part of the approved dual-runtime product.

## Detection and prerequisites

Game discovery uses the repository's verified Steam library and App ID rules
for App IDs `39140` and `3837340`. A manually browsed directory is accepted only
after the same executable identity, architecture, hash, and layout checks used
by the current PowerShell installer. Unknown executable builds remain blocked.

Reloaded-II discovery checks, in order:

- an existing recorded installation state;
- `RELOADED_II_ROOT`;
- the current AccessXI-compatible default beneath the user's profile;
- common user-selected locations recorded by prior setup runs.

The dependency page validates Reloaded-II's x86 and x64 loader/bootstrapper
files and the `reloaded.sharedlib.hooks` mod. A missing dependency is a blocking
result with an accessible explanation, Browse action, and official download
link. The first release does not silently install or replace Reloaded-II.
Prism and the Steam Audio native library are part of the Blind Swordsman
payload. 7th Heaven and FFNx are detected and reported for the legacy path but
remain optional. Existing 7th Heaven and FFNx configuration is preserved.

Because setup is self-contained, end users do not need a .NET runtime or SDK.
Windows PowerShell 5.1 is used only as the hidden deployment engine and is
present on supported Windows versions.

## Release discovery and integrity

The installer queries the public GitHub Releases API for the newest non-draft
Blind Swordsman release in its channel. Prerelease setup builds include
prereleases; stable builds ignore them. Every release provides:

- `Blind-Swordsman-Setup.exe`;
- `Blind-Swordsman-Runtime.zip`;
- `blind-swordsman-channel.json`;
- SHA-256 values inside the channel manifest and as sidecar files.

The channel manifest has a versioned schema, semantic product version, release
tag, payload asset name and SHA-256, setup asset name and SHA-256, minimum setup
version, and publication channel. Setup rejects unsupported schemas, invalid
versions, unexpected asset names, non-GitHub download hosts, hash mismatches,
missing package members, and archive paths that escape the staging directory.

If setup cannot reach GitHub, it presents an accessible error and permits the
user to select a previously downloaded runtime ZIP plus its channel manifest.
No installation begins until integrity and structure validation succeed.

## Runtime payload and deployment

The release payload contains the prebuilt dual-runtime mod package and only the
deployment inputs required at install time:

- `package/ff7.accessibility.reloaded/...`;
- `deploy/Install-FF7ReloadedMod.ps1`;
- `deploy/FF7SteamInstall.psm1`;
- native profile template and parity matrix;
- a payload manifest listing every file, length, and SHA-256.

`Build-DualRuntimePackage.ps1` remains the authoritative mod builder. A new
release builder stages those outputs, writes the deterministic payload
manifest, archives the payload, and writes the update-channel manifest. The
end-user setup passes the verified prebuilt package to the deployment script;
it never compiles source on the player's machine.

The existing installer gains an explicit prebuilt-package mode and a structured
JSON result path. Its current identity checks, loader collision protection,
atomic candidate deployment, recoverable backup, configuration preservation,
profile validation, and rollback remain authoritative. Setup captures all
PowerShell output in a per-run log under local application data and turns a
nonzero exit into a focused accessible error instead of a console window.

## Install state, repair, update, and uninstall

Per-user state is stored beneath
`%LOCALAPPDATA%\Blind Swordsman\install-state.json`. It records the installed
product version, release tag, validated game and Reloaded-II roots, package
fingerprint, installed profiles, files created by setup, and recoverable backup
locations. State writes are atomic.

Re-running setup compares this state with the release channel:

- a newer version offers **Update**;
- the same version offers **Repair**;
- a missing or invalid deployment offers **Repair**;
- Add or Remove Programs invokes the same executable with `--uninstall`.

Repair redownloads and revalidates the payload before using the same idempotent
deployment path. Update never runs in the background and never forces a game
restart. Setup installs a Start Menu shortcut named **Check for Blind Swordsman
Updates** that launches its managed installed copy. If a release requires a
newer setup engine, the running setup verifies the replacement executable,
starts it with a continuation token, and exits before deployment.

Uninstall removes the Blind Swordsman mod package, setup-owned Reloaded
profiles, shortcuts, and registration. Loader files are removed only when the
recorded install says setup created them and their current hashes still match.
Pre-existing shared loaders are left alone. A displaced earlier mod package is
restored only from the exact recorded backup. User configuration and any file
that changed after installation are preserved and reported rather than
silently destroyed.

## Windows integration

Setup installs its managed copy beneath
`%LOCALAPPDATA%\Programs\Blind Swordsman`. It registers a per-user Add or Remove
Programs entry and Start Menu shortcuts without requiring elevation. The
uninstall command, display version, publisher, project URL, update URL, and icon
are recorded under HKCU.

The initial executable is not Authenticode-signed because no signing
certificate is available. The README and release notes disclose that Windows
SmartScreen may therefore show **Unknown publisher**. Hash verification still
protects the downloaded runtime payload, but it is not a substitute for a
future code-signing certificate.

## Publishing

A Windows GitHub Actions release workflow and an equivalent local PowerShell
release script:

1. run the shared, x86, x64, parity, installer, and packaging tests;
2. build the dual-runtime package;
3. publish the self-contained setup executable;
4. build and validate the runtime ZIP;
5. generate hashes and `blind-swordsman-channel.json`;
6. create or update a GitHub prerelease and upload all assets.

The repository is changed from private to public only after those artifacts
pass local verification. The first tag is `v0.1.0-pre.1`. The README's primary
installation path becomes a direct link to the latest installer asset; the
source/manual workflow moves to a developer section.

## Testing and acceptance criteria

The installer core is separated from WinForms so it can be tested without
clicking the interface. Automated coverage includes:

- game and Reloaded-II discovery precedence and browse validation;
- required and optional dependency reporting;
- release selection for stable and prerelease channels;
- strict channel and payload manifest parsing;
- URL allow-listing, safe ZIP extraction, and SHA-256 rejection;
- new install, idempotent repair, update, rollback, and cautious uninstall;
- atomic state writes and corrupted-state recovery;
- command-line quoting and hidden PowerShell execution;
- WinForms accessible names, tab order, keyboard activation, and status
  notifications;
- deterministic release assets and GitHub workflow validation.

Tests use temporary fake game, Reloaded, and package trees. A final controlled
live pass on this machine verifies detection and a repair install without
launching FFVII. Existing dual-runtime verification must remain green. The
published release is accepted only when its public API metadata, asset hashes,
direct installer download, repository visibility, and README links all agree.
