# Guide route evidence collector

This collector keeps every native entrance and target-position witness. It does
not collapse a field to "one entrance worked", which missed the Cave of the Gi's
Added Effect ledge. It also checks the route's endpoint rather than accepting a
planner success flag alone.

`guide-chapters.json` records 293 original checkpoints across all 43 chronological
walkthrough chapters in Absolute Steve's web conversion. `review-notes.json`
contains native-script dispositions, including automatic entry movements,
moving actors, puzzle prerequisites and confirmed defects. Neither is a claim
that an end-to-end playthrough has occurred.

Build against the exact runtime directory to be tested, using separate output
directories for architectures and package revisions:

```powershell
dotnet build tools/GuideRouteAudit/GuideRouteAudit.csproj -c Release `
  --artifacts-path artifacts/guide-audit-build/x86 `
  -o artifacts/guide-audit/x86 `
  -p:AuditRuntime=x86 -p:AuditRuntimeDirectory=C:\verified-package\x86
```

Use `x64` and the x64 package directory to load the Steam runtime instead. Copy
all DLLs from the specified runtime directory into the audit output directory
before running, and verify their hashes match. The report records the loaded
runtime assembly hash. Do not silently reuse old dependencies between builds.

```powershell
artifacts/guide-audit/x86/GuideRouteAudit.exe --classification-tests
$env:FF7_ACCESSIBILITY_DATA_ROOT = 'C:\licensed-game\workingdir'
artifacts/guide-audit/x86/GuideRouteAudit.exe --endpoint-tests
artifacts/guide-audit/x86/GuideRouteAudit.exe `
  'C:\licensed-game\workingdir' 'C:\private-research\routes.json'
python tools/GuideRouteAudit/render_ledger.py `
  'C:\private-research\routes.json' 'C:\private-research\chapters.md'
```

Run both runtime assemblies against both licensed archives. The independent
StoryCoverageAudit state fixtures, full executable test hosts, and live replay
cases remain necessary; this tool does not replace them.

## Evidence levels

- `geometry-from-all-arrivals`: every evaluated entrance can reach an endpoint
  within the modeled interaction range. No live availability is implied.
- `script-assisted-geometry`: the above requires a possible ladder, jump or other
  script transition. Its prerequisite state is not simulated.
- `partial-geometry`: at least one entrance fails even though another works.
- `unreachable`: none of the evaluated direct approaches reaches the endpoint.
  This is a review candidate, not an automatic bug report.
- `unverified`: a position or exact interaction range is not established. A
  varying/bank-backed radius never earns a geometry pass merely from its maximum.
- `no-native-arrivals`: no playable field-script transfer was available to test.
  World-map entry mechanisms are separate.
- `manual-interaction`: the production catalog supplies manual activity guidance
  instead of a walking route, such as the Mt. Corel falling-track rewards.

The `staticOutAndBack` witness is recorded separately from a failed direct route.
Its name is retained in the report format; the production resolver can now find
a bounded chain through neighboring fields and back into the original field.
Future fields' exits and transitions are only static possibilities. The
production controller re-reads each field's live exit list and walkmesh after
arriving. A static detour is not a certified live route.

Only `blackbg*` and `startmap` origins are excluded as debug warps. The `qa` through
`qd` fields are the Gelnika and must remain in the audit. Raw MAPJUMP coordinates
preceding a reviewed compulsory entry animation are retained; only the traced
control-return landing is used for that particular route attempt. Changes in
the corresponding native JUMP bytes cause the review fixture to fail.

Geometry testing assumes possible script traversals and enabled lines. It does
not emulate the active party, ownership, visibility, story flags, native locks,
moving collision bodies, facing, timed input or all saved-game states. Objects
at initialization, after collection, while carried, or before a cutscene can
occupy different positions. These must remain separate evidence.

The materia caves have world-map entrances, so their main matrix entries remain
`no-native-arrivals`. `MateriaCaveNavigationTests` independently uses each cave's
native return gateway to identify its floor component and tests the new crystal
interaction from every triangle in that component. Do not relabel the main
matrix merely because that separate regression passes.

NPC witnesses include the shipping reader's reviewed counter overrides as well
as script discovery. Ordinary Talk and alternate counter states remain separate
when the reader can choose between them. A contract check compares audit target
metadata with targets emitted by the real NPC reader. Endpoint checks include
height: Talk's strict native band and the three-dimensional LINE distance. LINE
projection reproduces the native signed arithmetic and 8-bit fraction before
bounds checking; a floating-point projection can incorrectly reject native
doorway endpoints. Unknown ranges remain unverified even when the nominal route
fails. Triangle and side-of-line eligibility are retained when deciding whether
an entrance is a possible start for that particular catalog row.

Background LINE objects use the production reader's live-segment and collision
radius metadata. The collector keeps each literal LINE/SLINE segment as a
separate possible-state witness, and retains variable SLINE positions as unknown.
It never substitutes the catalog midpoint for an unreadable live segment. Reader
contract checks cover both live-segment and older constructor paths, plus the
forest's explicit crossing metadata. Controller arrival replays in the runtime
tests are still required: a valid final waypoint alone cannot prove that autowalk
will continue all the way to native activation.

The map-name table includes world slots and unused names. `readErrors` retains
those unavailable names; it does not by itself imply damaged installed data.
Any unexpected unavailable story field requires investigation.

`--dump-field <root> <id> <output.json>` is a private geometry inspection aid.
Reports and dumps contain derived data from the licensed game and belong in a
private research directory. Do not commit raw guide text or extracted game data.
