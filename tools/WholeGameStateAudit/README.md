# WholeGameStateAudit

A whole-game audit of the field scripts. It decodes every field's script section with
its own decoder and control-flow model, compares the result with the shipping readers,
and lists candidate deficiencies with the bytes that show them. Runtime code does not use
it.

```
WholeGameStateAudit --archive legacy="C:\Games\Final Fantasy VII\workingdir" ^
                    --archive x64="C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir" ^
                    --out "D:\Private-FFVII-Research"
```

- `--field <id>` (repeatable) or `--fields <a>-<b>` limits the run. The Story/Object row
  cross-check needs every field, so it runs only when no field is selected.
- `--no-instructions` and `--no-text` leave out the instruction listing and dialogue.
- Run it from PowerShell or cmd. Git Bash rewrites a leading `\\` and sends the output to
  `C:\`.
- The current project is an x86 audit host. Archive labels such as `legacy` and `x64`
  name the data being read; they do not select the runtime assembly. Validate the actual
  x64 implementation with its test host and the x64 StoryCoverageAudit build. The complete
  independent x64 run and its assembly identities for this pass are recorded in
  `analysis/2026-09-24-whole-game-engine-audit.md`.

The output is decoded game data and dialogue. The tool refuses to write inside the
repository. It writes:

- `fields\<label>\NNN-name.json`: per field, the section header, every entity and all 32
  slots, and every entry point with its reach, calls, effects and guards. It also has
  interactions with their call closures, gateways, triggers, exit arrows, the shipping
  comparison, Story and Object rows, dialogue, and the instruction listing.
- `reports\claude-field-audit-<label>-summary.json`: counts, candidate classes, and a
  per-field table with hashes.
- `reports\claude-field-audit-<label>-candidates.json`: every candidate with its witness
  and a reproduction command.
- `reports\claude-field-audit-<label>-flags.json`: the cross-field flag graph. It covers
  every savemap and temporary byte that is written, tested, or guards something, and the
  Story and Object rows that read it.
- `reports\claude-field-audit-archives.json`: the per-field hash and candidate comparison
  between archives.

## Model

- **Entry points.** Every slot runs the code at its pointer, whatever other slots share
  that pointer. Script 0 is Init. Main begins after Init's return: both Makou Reactor's
  static split and every return reachable from Init are kept, and a disagreement is
  reported.
- **Engine events.**
  - A model's slots 1 and 2 are Talk and Contact. Party characters count as models: their
    Talk runs while they are not leading.
  - A line's slots 1-6 are [OK], [Move], [Move], [Go], [Go 1x] and [Go away].
  - Anything else runs only when requested.
- **Decoding.** Recursive descent from every entry point, with each offset decoded once. An
  undefined opcode, a truncated instruction, a jump out of the code or into another
  instruction, and the opcodes PC does not implement are reported. None of them is decoded
  past.
- **Calls.**
  - REQ, REQSW and REQEW resolve to an entity's slot.
  - RETTO resolves to this entity's slot and ends the script.
  - PREQ and its variants resolve to every party-character entity and are marked dynamic.
- **Guards.**
  - Must-guards are the tests that hold on every path, from a dataflow over the control
    flow; path-dependent tests are counted separately.
  - A called script's effects carry the guards of the call site that reached them.
- **Banks.**
  - 1/2, 3/4, 5/6, 11/12, 13/14 and 15/7 alias the same 256 bytes.
  - An address is the operand's first byte. Word helpers touch two bytes only on banks
    2, 4, 6, 12, 14 and 7.
  - BITON, BITOFF and BITXOR change bit `position & 31` of the one byte, so positions
    8-31 change nothing.

Opcode names, lengths and read operands are generated from Makou Reactor (commit
2452025714c0, used as reference only). The fixed lengths equal the shipping table.

Write destinations, SPECIAL lengths and the unimplemented 0x1A-0x1C come from the PC
executable's own handlers. Root extracted these from a full Ghidra export of legacy
`ff7_en.exe` (SHA-256 `4274ab2d...`). `Opcodes.VariableTableCorrections` lists each
correction with its handler address.

## Comparison with the shipping catalog

- **Decoder.** `ParseScriptGroups` and `ReadOpcodes` are called through reflection, and
  every slice is matched to the audit's replica.
- **Walker.** For every line entity, the audit calls the shipping walker,
  `CollectNavigationActionPaths`. Its actions are compared, by destination or landing,
  with what the engine can run from the line's events. Each difference is explained by
  replaying the walker with one shipping rule at a time replaced by the engine's.
- **NPCs, script exits and labels.** These come from the public `ReadField`. Labels come
  from the shipping `FieldNavigationNpcReader` in the same harness FieldInteractionAudit
  uses.
- **Objects and Story.** The embedded Object and Story catalogs are compared per entity
  and per flag bit.

## Tests

`tools/WholeGameStateAudit.Tests` assembles synthetic fields in memory and needs no game
data. `--installed <working dir>` adds checks against an installed archive.

```
dotnet build tools/WholeGameStateAudit.Tests/WholeGameStateAudit.Tests.csproj -c Release --artifacts-path artifacts/claude-whole-game-audit
artifacts\claude-whole-game-audit\bin\WholeGameStateAudit.Tests\release\WholeGameStateAudit.Tests.exe [--installed "C:\Games\Final Fantasy VII\workingdir"]
```

## Limits

- Static reachability is not player reachability. Guards say what must hold, not whether
  a save can reach it.
- The world map, battle, menu and minigame modules also write the savemap and are not
  modelled here.
- Native dispatch was checked in both engines: repeated slot pointers retain their
  slot identity and can execute through the appropriate event or request. Static
  dispatch modeling still does not prove that its live input, visibility, collision
  and state conditions hold in every save.
- A candidate is a place to look, not a confirmed defect.
