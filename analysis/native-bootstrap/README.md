# Supported FFVII host evidence

Blind Soldier fails closed unless the executable being launched is one of the
FFVII hosts this project has inspected. The generated
`supported-hosts.json` file is the human-readable source of truth. The same
generator emits immutable C++ and C# constants consumed by the native bootstrap
and Reloaded runtime; production code does not read a mutable JSON file.

## Acceptance policy

- The stock 2013 x86 `ff7_en.exe` and Steam 2026 x64 `FFVII.exe` require their
  exact filename, PE machine, and SHA-256 digest.
- The two known 7th Heaven x86 layouts may be named `ff7.exe` or `ff7_en.exe`.
  They must be PE32/i386, import `WINMM.DLL`, contain no embedded application
  manifest that would suppress `.local` redirection, match every section in one
  recorded layout, and match three masked code signatures at game functions
  already used by Blind Soldier.
- A wrong name, architecture, digest, import, section, resource policy, or code
  signature is rejected with an explicit diagnostic.

No FFVII executable is stored in this repository. Licensed local evidence files
belong under `analysis/native-bootstrap/local-fixtures/`, which is ignored by
Git.

## Evidence inputs

The evidence pass used official Ghidra 12.1.2 with Microsoft OpenJDK 21 and
`analysis/ghidra/BlindSoldierHostEvidence.java`. The pinned Ghidra archive was
verified as SHA-256
`B62E81A0390618466C019C60D8C2F796CED2509C4C1AEA4A37644A77272CF99D`.

| Host sample | SHA-256 |
| --- | --- |
| Stock x86 `ff7_en.exe` | `4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225` |
| 7th Heaven 1.02 patch `ff7.exe` | `C1437392C5E4178765FBD238DCC9B33D86D2B97337310131C874F302236E4B6F` |
| 7th Heaven converted `ff7_en.exe` | `68CF1B8C1D732CC00A1DDB02CED161F7C94B06680D9E8641A11C7361417375C2` |
| Steam 2026 x64 `FFVII.exe` | `57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B` |

The Ghidra script reports the PE machine, image base, section layout and flags,
import modules and symbols, manifest-resource presence, and the three legacy
game-code signatures. The PowerShell generator independently parses bounded PE
headers and refuses an input whose pinned digest or architecture has changed.

## Regeneration

From the repository root:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
  -File .\tools\Generate-BlindSoldierHostManifest.ps1
```

This updates:

- `analysis/native-bootstrap/supported-hosts.json`
- `native/BlindSoldier.Common/supported_hosts.generated.h`
- `Ff7.Accessibility.Reloaded/Runtime/SupportedHosts.Generated.cs`

Run the managed `--host-validation-only` tests and
`native/BlindSoldier.Native.Tests.ps1` after regeneration.
