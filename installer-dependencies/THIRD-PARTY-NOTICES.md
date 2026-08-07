# Blind Soldier third-party notices

This file distinguishes components redistributed in current release payloads from
provenance-only references. Only the **Shipped redistributions** section describes
components carried by the setup or portable release; provenance-only entries are
not distributed in the portable ZIP.

## Shipped redistributions

The standard setup installs missing prerequisites. The portable native installer
carries the minimal Reloaded runtime and documents its Microsoft .NET runtime
requirement.

### Reloaded-II 1.30.3

- Project: https://github.com/Reloaded-Project/Reloaded-II
- Exact source: https://github.com/Reloaded-Project/Reloaded-II/tree/1.30.3
- License: GNU General Public License version 3, included as
  `Reloaded-II-GPL-3.0.txt`.
- Blind Soldier's x86 copy contains a hostfxr compatibility correction. The
  exact source patch is included as `Reloaded-II-1.30.3-hostfxr.patch`; the
  matching source and reproducible build instructions are included as
  `Reloaded-II-1.30.3-Blind-Soldier-source.md`.

Reloaded-II is separate software aggregated with Blind Soldier. Its source code
is available without charge at the exact source link above. This correction is
limited to Blind Soldier's bundled x86 Reloaded host. It does not replace or
edit any file in a 7th Heaven installation.

### Reloaded Shared Hooks 1.16.3

- Project: https://github.com/Sewer56/Reloaded.SharedLib.Hooks.ReloadedII
- Exact source: https://github.com/Sewer56/Reloaded.SharedLib.Hooks.ReloadedII/tree/1.16.3
- License: GNU Lesser General Public License version 3, included as
  `Reloaded-Shared-Hooks-LGPL-3.0.txt`.

### Microsoft .NET Desktop Runtime 9.0.8

- Project: https://github.com/dotnet/runtime
- Exact source: https://github.com/dotnet/runtime/tree/v9.0.8
- License and third-party notices: included as `dotnet-LICENSE.txt` and
  `dotnet-THIRD-PARTY-NOTICES.txt`.

The exact upstream URLs, file sizes, and cryptographic digests used by the
release builder are recorded in `dependency-bundle.json`.

## Provenance-only references (not shipped in the portable ZIP)

### Ultimate ASI Loader

- Project: https://github.com/ThirteenAG/Ultimate-ASI-Loader
- License: MIT. This reference is retained for provenance only. Blind Soldier's
  portable ZIP does not ship `dsound.dll`, Ultimate ASI Loader, or an ASI
  bootstrap.
