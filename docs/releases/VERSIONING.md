# Version numbering

Releases use `MAJOR.MINOR.PATCH` and the tag `v<version>`.

Starting with 0.6.0, the patch number stops at 9. The sequence is
`0.6.0`, `0.6.1`, through `0.6.9`, then `0.7.0`. Do not release `0.6.10`.
This rule caps the patch component; it does not impose a cap on the minor component.
Earlier published tags retain their original numbers.

For each release, update these fields together:

- `Ff7.Accessibility.Reloaded/ModConfig.json`: `ModVersion`.
- `launcher/Ff7.Launcher.Accessible/Properties/AssemblyInfo.cs`: the Blind Soldier
  suffix in `AssemblyInformationalVersion`. Preserve the launcher's native assembly identity.
- `.github/workflows/release.yml`: the manual build version default.
- `README.md`: download links and build/verification command examples.
- `docs/releases/v<version>.md`: the release notes.

The package version, tag, portable metadata and Accessibility Mod Manager release
records must agree. Run the relevant managed and packaging checks, verify the built
archives and Ghidra evidence, and publish the same checked artifacts. Preserve user
configuration when deploying locally.

`tools/Resolve-BlindSoldierReleaseTrack.ps1` keeps `0.x.y` and versions with a
prerelease suffix on the prerelease track. Version 0.6.0 remains a prerelease.
