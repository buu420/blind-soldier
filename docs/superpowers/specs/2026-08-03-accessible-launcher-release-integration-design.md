# Accessible Launcher Release Integration Design

## Goal

Blind Swordsman `v0.1.0-pre.2` must include and install the accessible Steam
2026 `FFVII_LAUNCHER.exe` so a fresh user receives the same launcher speech,
keyboard behavior, native UI Automation tree, and Prism output already used on
the development machine.

The legacy x86 game remains supported without requiring this launcher. 7th
Heaven and FFNx remain optional and setup must not change either integration.

## Release contents

The repository will contain a release-ready launcher payload made from the
current verified accessible launcher build:

- `FFVII_LAUNCHER.exe`, x86, assembly version `2.0.0.0`;
- `FFVII_LAUNCHER.exe.config`;
- `FFVII_LAUNCHER.prism.x86.dll`, installed only beneath
  `launcher_accessibility/native/x86`;
- a strict JSON bundle manifest containing the stock launcher identity and the
  exact SHA-256 and size of every packaged launcher file.

The release builder validates the bundle, rejects reparse points or extra
files, and copies it into `Blind-Swordsman-Runtime.zip` beside the Reloaded mod
package. The ZIP's existing payload manifest continues to authenticate every
entry.

No development-machine path is stored in the launcher payload, channel
manifest, setup executable, or install state.

## Installation flow

Setup requires the launcher bundle whenever the detected game has the native
Steam 2026 runtime. It passes the extracted bundle to the deployment script;
legacy-only installations skip launcher deployment.

Before changing anything, deployment validates all of the following:

1. The launcher bundle exactly matches its manifest.
2. The packaged launcher and Prism library are x86 PE files.
3. The packaged launcher has assembly name `FFVII_LAUNCHER` and version
   `2.0.0.0`.
4. The target is the verified stock launcher, the current packaged accessible
   launcher, or a prior launcher whose ownership is proven by the existing
   launcher accessibility manifest.
5. The game and launcher are not running.

An unknown launcher is never overwritten.

When replacing a recognized stock or prior accessible launcher, setup keeps a
verified persistent backup beneath
`<Reloaded-II>/AccessibilityBackups/ff7-launcher.backup-<id>`. An older
launcher installation whose stock backup lives elsewhere is migrated by
copying and re-verifying that stock file into this managed backup directory.
The installer never records the old development-machine backup path as its new
default.

The launcher executable, configuration, and launcher-only Prism library are
installed with exact post-copy hash checks. A setup-owned manifest in
`launcher_accessibility/install-manifest.json` records persistent ownership
and original backups. Repair and update reuse those ownership records instead
of losing the original stock backup.

## Install state and rollback

The setup install-state schema remains backward compatible with
`v0.1.0-pre.1`. A nullable `launcher` object is added; old schema-one states
without it still parse and uninstall normally.

The launcher object records three independently managed files: launcher,
configuration, and Prism. Each record contains:

- target path;
- installed SHA-256;
- whether Blind Swordsman owns the change;
- optional backup path and backup SHA-256.

It also records the launcher manifest path and hash. The deployment script
performs a validate-only pass before the first mutation. If a later deployment
step fails, transaction snapshots restore the exact launcher, configuration,
Prism, and prior manifest state.

## Uninstall behavior

Uninstall validates every recorded launcher target against the detected game
root before touching it.

For each setup-owned file:

- if the current file still matches the installed hash, restore the verified
  backup or remove the file when no prior file existed;
- if the file changed after installation, preserve it and report that fact;
- if a required backup is missing or changed, preserve the installed file
  rather than risk destructive recovery.

The launcher manifest is removed only when it still matches the recorded
hash. Logs or other user-created files beneath `launcher_accessibility` are
preserved.

## User interface and documentation

Setup's dependency review reports the accessible launcher as part of the
native Steam 2026 installation. Progress text explicitly announces launcher
installation. The README states that setup installs the accessible launcher
for the x64 edition and restores the verified prior launcher during uninstall.

The primary download link changes to `v0.1.0-pre.2`. The incomplete
`v0.1.0-pre.1` release remains available as historical prerelease evidence but
is marked superseded in its release notes.

## Verification

Automated coverage must prove:

- release archives contain the exact launcher bundle;
- malformed manifests, wrong hashes, wrong PE architecture, unexpected files,
  and unknown target launchers are rejected before mutation;
- stock install creates verified backups and installs all three files;
- repair is idempotent and retains the original stock backup;
- update from the previously installed accessible launcher migrates its stock
  backup into the managed backup root;
- uninstall restores stock files, removes newly created files, and preserves
  post-install user changes;
- legacy-only installation leaves launcher state null;
- setup install state parses both old and new schema-one documents;
- no 7th Heaven or FFNx file changes during controlled deployment.

The final release must pass the full repository verification gate, two
deterministic release builds, a controlled live update and repair, anonymous
public download/hash checks, and the tag-triggered GitHub Actions build.
