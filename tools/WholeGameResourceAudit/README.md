# Private whole-game resource audit

This read-only tool inventories all world event entry points, all 256 English
battle scenes (formation and enemy AI), and all 27 kernel sections including
character AI. It retains aliased entries, follows both sides of conditional
branches, follows branches past other entry points, bounds all reads, and reports
unknown instructions or overlapping code instead of guessing their meaning.
The complete-inventory command requires all three world event files, all 256
battle scenes, all 27 kernel sections and all 12 character AI table slots. Missing
members, duplicate indices and structural decoding issues produce a nonzero exit
status while preserving the report for inspection.

Run with Python 3.11 or newer:

```powershell
python -m unittest discover -s tools/WholeGameResourceAudit -v
python tools/WholeGameResourceAudit/resource_audit.py '<game-workingdir>' '<private-output>'
```

Outputs contain licensed game bytes. The command refuses a Git checkout as its
output location. Never commit or publish the generated output. Original tool
source and synthetic tests may be published with the mod.

The report counts structural decoding and possible control flow. It does not
evaluate all live game states, prove every path reachable, reproduce the engine,
or expose enemy AI and hidden information in the accessibility UI. The field
state audit and live readers remain separate.

Format references:

- <https://ff7-mods.github.io/ff7-flat-wiki/FF7/WorldMap_Module/Script.html>
- <https://ff7-mods.github.io/ff7-flat-wiki/FF7/WorldMap_Module/Script/Opcodes>
- <https://ff7-mods.github.io/ff7-flat-wiki/FF7/Battle/Battle_Scenes.html>
- <https://github.com/cebix/ff7tools/blob/master/ff7/scene.py> (message delimiters and scene structure cross-check)

The installed kernel has two final zero alignment bytes after its 27 complete
members. These are preserved as metadata; truncated headers elsewhere are errors.

## Native engine evidence

The companion scripts in `analysis/ghidra/` operate on private Ghidra projects:

1. `CaptureLoadedEngine.ps1 -GameProcessId <pid> -PrivateOutputDirectory <inputs>`
   reads the main module of the hash-verified x64 game after its normal loader has
   run. Use PowerShell 7. The resulting PE has analysis layout metadata and must
   never replace the installed executable. Existing captures are not overwritten.
2. `ExportWholeGameEngine.java <export-directory> [timeout-seconds]` exports all
   discovered functions, assembly, references, symbols, memory blocks and gaps.
3. `python tools/WholeGameResourceAudit/engine_map.py <private-research-root>`
   indexes the translated-function registrations using the validated REQ anchor.
   It requires a completed legacy export and the loaded capture under `inputs/`.
4. `SeedTranslatedEngineFunctions.java <map-json> <legacy|host> <report-json>`
   seeds missing entries in the private project without splitting overlaps.
   `SeedUnwindEngineFunctions.java <review-json> <report-json>` handles the
   separately reviewed primary x64 unwind entries; chained ranges stay distinct.
5. Re-export after seeding. `RetryIncompleteDecompilations.java <export-directory>
   [entry ...]` retries selected hard failures and retains earlier retry records.
6. `python tools/WholeGameResourceAudit/verify_engine_export.py <export-directory>
   <exact-input-binary> <headless-log> <report-json>` validates the final export.

Pass Java scripts with Ghidra headless `-scriptPath` and `-postScript`; use a
separate private project and output directory for each input. The capture and
seeding helpers intentionally bind to the investigated executable identities.
An updated game build requires new evidence, not removal of the identity checks.
See `analysis/2026-09-24-whole-game-engine-audit.md` for the authoritative export
paths, counts, remaining failures and interpretation limits.

For repeated text searches, keep a disposable local copy of the exported `.c`
files. Searching thousands of small files over an SMB share is much slower than
copying them once with `robocopy <export>\functions <private-cache> *.c /S /MT:16`
and running `rg` locally. Treat robocopy exit codes above 7 as errors. Keep this
cache private and ignored by Git; retain the authoritative export metadata and
assembly alongside the original input hashes. A cache of C files alone omits
failed decompilations and is not the complete export.
