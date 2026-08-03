# Blind Swordsman installer details

The standard Blind Swordsman installer is a self-contained, 64-bit Windows EXE
built with .NET 8 WinForms. It uses ordinary Windows labels, text boxes, list
boxes, progress bars, and buttons so keyboard users and screen readers receive
the same setup information. Every meaningful progress change also has visible
text and a native UI Automation notification.

## What setup detects

Setup first searches Steam for a supported original Final Fantasy VII install.
It recognizes the legacy x86 runtime, the Steam 2026 x64 runtime, or both when
they are installed together. Exact executable identity checks prevent a build
for an unknown game update from attaching to the wrong memory layout.

Setup also checks:

- the selected Reloaded-II folder;
- the x86 and x64 ASI loaders and bootstrapper files;
- the x86 and x64 Reloaded Shared Hooks dependency;
- an existing 7th Heaven installation, when present;
- an existing FFNx driver, when present.

Final Fantasy VII and Reloaded-II are required. 7th Heaven and FFNx are
optional interoperability checks. If automatic detection is wrong, use the
labeled folder buttons and choose the relevant root folder, then choose
**Scan again**.

The game path comes from Steam library discovery or the folder the user
chooses. Reloaded-II discovery uses an existing saved install path, the
`RELOADED_II_ROOT` environment variable, Reloaded-II's own registered launcher,
then portable and common Windows locations. No path from the developer's PC is
compiled into setup. If Reloaded-II is not installed, setup proposes
`Reloaded-II` inside the game folder as the portable location; Reloaded-II's
official portable mode supports keeping its launcher in a game subfolder.

## Install, update, and repair

The installer queries GitHub Releases for the current channel manifest. It
accepts only HTTPS GitHub asset URLs whose names, lengths, and SHA-256 hashes
match the release metadata. The runtime archive contains its own sorted file
manifest. Setup rejects absolute paths, path traversal, duplicate names,
reparse points, missing files, unlisted files, length mismatches, and hash
mismatches before invoking deployment.

The action shown by setup depends on saved per-user state:

- **Install** appears when no prior setup state exists.
- **Update** appears when the available release is newer.
- **Repair** appears when the same version is already installed.
- A downgrade is blocked when the installed version is newer.

Repair reruns the validated deployment from the current release. Deployment
backs up an existing Blind Swordsman mod folder before replacement and writes
new state only after the result matches the selected release and detected
locations.

For native Steam 2026 installs, setup also validates and installs the bundled
accessible `FFVII_LAUNCHER.exe`, its configuration, and its launcher-only x86
Prism library. The original stock launcher is copied into the selected
Reloaded-II folder's `AccessibilityBackups` directory and verified before the
replacement is committed. An older launcher-accessibility manifest is migrated
to this managed backup layout during update or repair.

After a successful operation, setup copies itself to
`%LOCALAPPDATA%\Programs\Blind Swordsman\Blind-Swordsman-Setup.exe`, registers
Blind Swordsman in Windows Installed apps, and creates **Check for Blind
Swordsman Updates** in the Start menu. That shortcut performs an explicit
check; there is no background service or scheduled updater.

If a later release requires a newer setup engine, the current setup downloads
the newer EXE from the release, verifies its length and SHA-256, and continues
in that verified executable. An update continuation is rejected if the new EXE
still does not meet the release's declared minimum setup version.

## Compatibility with 7th Heaven and FFNx

Blind Swordsman uses Reloaded-II for both game runtimes. On legacy x86 it is
designed to run alongside 7th Heaven and FFNx. Setup deliberately skips broad
7th Heaven settings changes, records exact files it installs, and never
installs or replaces FFNx. Existing mod ordering and rendering choices remain
under 7th Heaven's control.

Close the game, 7th Heaven, and Reloaded-II before installation so no target
file is locked. If another mod replaces the same loader or changes a file after
Blind Swordsman installs it, uninstall preserves that changed file instead of
silently deleting it.

## Uninstall and recovery

Use Windows **Settings > Apps > Installed apps > Blind Swordsman**, or run the
managed setup with `--uninstall`. Uninstall validates saved state against the
currently detected game before changing anything. It removes setup-created
files only when they still match their recorded hashes. Changed files are
preserved and listed in the log. A prior mod backup is restored only when its
recorded fingerprint still matches. For Steam 2026, uninstall also restores the
verified original launcher and removes setup-created launcher support files.
If any launcher file changed after installation, that file is preserved and
reported instead.

The installed setup EXE schedules its own removal through Windows after the
uninstall process exits. Per-user registration, update shortcut, and install
state are then removed.

## Logs and state

Setup writes plain-text logs to:

```text
%LOCALAPPDATA%\Blind Swordsman\Logs
```

Saved install state is:

```text
%LOCALAPPDATA%\Blind Swordsman\install-state.json
```

Do not edit the state file. Corrupt or unexpected state is preserved for
diagnosis and setup refuses to guess what should be removed.

## Local release mode

For an offline transfer, place these three files from one release together:

- `Blind-Swordsman-Setup.exe`
- `Blind-Swordsman-Runtime.zip`
- `blind-swordsman-channel.json`

Run:

```powershell
.\Blind-Swordsman-Setup.exe --local-manifest ".\blind-swordsman-channel.json"
```

The local ZIP is accepted only when it exactly matches the manifest. If it is
absent, setup uses the trusted GitHub URL from the manifest and still performs
all integrity checks.

## Unsigned prerelease warning

Version `0.1.0-pre.3` is not Authenticode-signed. Windows SmartScreen can show
**Unknown publisher** even when the file is intact. Download only from the
project's GitHub Releases page and, when desired, verify it against the
adjacent `.sha256` file. This warning is separate from the installer's own
payload verification.

## Developer release commands

Build the five release assets into a new output folder:

```powershell
.\Build-BlindSwordsmanRelease.ps1 `
  -Version "0.1.0-pre.3" `
  -Tag "v0.1.0-pre.3" `
  -MinimumSetupVersion "0.1.0-pre.3" `
  -OutputPath ".\artifacts\release\v0.1.0-pre.3"
```

After verification, publish them using the authenticated GitHub CLI session:

```powershell
.\Publish-BlindSwordsmanRelease.ps1 `
  -Tag "v0.1.0-pre.3" `
  -ArtifactPath ".\artifacts\release\v0.1.0-pre.3"
```

The publisher reads the channel manifest, requires all exact asset names,
refuses to replace an existing GitHub release, and never stores credentials in
the repository.
