# Guide and native route audit — 25 September 2026

This pass follows all 43 chronological walkthrough chapters in [Absolute Steve's
web conversion](https://www.supercheats.com/guides/final-fantasy-vii), from the first
reactor to the ending, including its optional chapters. The original checklist in
`tools/GuideRouteAudit/guide-chapters.json` contains 293 checkpoints. This is not a
claim that every possible save state has been played through.

## What this audit checks

The collector reads the installed game's field archives and uses each supported
runtime's real target catalogs and route planner. It retains each native entrance
and target-position witness separately. A route from one entrance cannot erase a
failure from another. It also checks the final approach against the interaction
range, rather than accepting the planner's success flag alone.

Native scripts distinguish compulsory entry animations, background OK lines,
moving actors, collected items, puzzle locks and ordinary walkable paths. Reviewed
entry landings are pinned to the actual native instructions. Ghidra-derived
collision and Talk semantics determine interaction ranges; unknown or variable
ranges remain unverified.

The guide ledger and `review-notes.json` preserve these distinctions. Raw game
exports and full guide text remain in the private research directory.

## Confirmed corrections

- **Added Effect in the Cave of the Gi:** the upper ledge is reached through the
  neighboring cave and back through a different entrance. Navigation retains the
  selected target over the native field transfers and checks each next leg's live
  exits and locks.
- **Corneo's bedroom:** when Cloud is selected, the conversation uses the OK line
  beside the bed. Corneo's own Talk script is empty. The Story target now uses the
  bedside interaction, with the actual selection flag, live line state and native
  touch range. Controller replays cover every arrival into the bedroom at both
  the default and a larger configured arrival distance. The later rug event is
  gated by its own line.
- **Observatory and cargo-room approaches:** a failed trigger-line attempt reset
  the endpoint before a successful point fallback. The fallback now restores the
  actual target coordinates.
- **Train Graveyard barrels and the slum television:** these use native Talk
  interaction ranges. The previous generic pickup range could not reach them.
- **Sector 5 bedroom:** the boy's early conversation and the later adult's
  conversation use the enabled line beside the bed. Once the boy is standing
  and that line is disabled, his normal Talk and later reward remain available.
  Both states now use their actual reader targets in native route regressions.
- **Materia caves:** the Mime, Quadra Magic and Knights of Round crystals are
  background interactions absent from the model-based scan. Each now has one
  visible "Materia crystal" Objects entry, controlled by its native enabled line
  and collection bit. Its approach was checked from every triangle on the cave's
  entrance floor; the existing HP-MP crystal remains unchanged.
- **Background pickups and counter conversations:** autowalk previously stopped
  at a configured distance even when the game required touching a much narrower
  line. The readers now pass the native line and leader collision radius through
  to the controller. A shared three-dimensional contact test decides arrival.
  The object readers in both production hosts use the line's live coordinates,
  including segments moved by a script. Native-map replays check the moment
  autowalk stops, including approaches near the end of a long line. The same
  contact rule covers all 36 declared non-crossing Story line interactions.
  Missing or temporarily unusable collision radii no longer revive the old
  configured-distance fallback for counters.
- **Gongaga's Turks:** the paired encounter uses its native line interaction
  and visibility state instead of routing to an actor with an empty Talk script.
- **Ancient Forest:** throwing spots retain the selected destination across
  their own trigger lines, and their heights match the walkmesh. Conditional
  plant jumps and damaging flytrap bites are no longer treated as unconditional
  route edges. The take-off entries stop before activation and describe the
  player's directional input; ordinary keyless walk-on jumps remain available.
- **Mega All:** the item is offered at the native jumping take-offs rather than
  as a route to an unreachable floating model. The timed OK press remains manual.
- **Routes through adjacent fields:** a bounded search can find longer return
  routes to disconnected ledges. Each leg uses the new field's available exits
  and locks. An explicit autowalk stop cancels continuation, while a battle or
  temporary scene interruption preserves the pending route.
- **Arrival-state interpretation:** native transfer and entry-script evaluation
  now rejects unsupported control flow and indirect state writes instead of
  inventing a starting position. Startup-cleared selectors are accepted only
  when no concurrent writer invalidates that proof.

## Verification and installation

The [chapter checklist](guide-route-audit-ledger-20260925.md) maps all 43 chapters
and 293 checkpoints to 670 fields. The collector also checked the remaining
readable fields, for 702 total. Each runtime/archive combination produced 6,153
target-position witnesses and 21,542 route attempts. All four reports agree after
normalizing only the licensed archive's path in diagnostics.

| Evidence category | Target-position witnesses |
|---|---:|
| Geometry from all evaluated arrivals | 2,786 |
| Geometry requiring possible script traversals | 307 |
| Mixed successful and failed arrivals | 163 |
| No successful evaluated direct approach | 26 |
| Unresolved position or interaction range | 2,611 |
| No playable native field arrival | 255 |
| Manual activity guidance | 5 |

These are state witnesses, not counts of unique objects or confirmed bugs.
Reviewed automatic movements, inactive puzzle states and controlled actors
remain visible in the report. The native review dispositions explain confirmed
corrections and cases that still need a matching live state.

A fresh dual-runtime ReadyToRun package passed both full executable test suites.
The executable hosts were rebound to every packaged DLL and checked by SHA-256
before and after running. Both assemblies passed the guide audit against both
installed archives, plus 85 base Story fixtures and 207 reviewed transit fixtures
for each combination. The shared-layout and runtime-parity suites passed. The
Story generator reproduces the reviewed JSON catalog. Runtime-hook installation
was not exercised inside these standalone test hosts; no live playthrough was
performed.

| Installed runtime | Main DLL SHA-256 |
|---|---|
| Legacy x86 / 7th Heaven | `7B1ACAB8CC8505A27622D7E88734D571C21E16009FD26B55B778D64A25B9C24C` |
| Steam 2026 x64 | `5523418670F290FE177C41528BAEC25CE4DC2CB3E95B20FA0A6046C2B493D450` |

The verified payload was installed into both local game installations: 36
changed files in total, with backups under each installation's
`AccessibilityBackups/guide-route-audit-20260925-20260925-202047`. All installed
payload hashes match. The 7,621 protected settings, metadata and narration files
were unchanged. This is a local test build; the published 0.7.0 release was not
replaced.

Detailed package-check and deployment manifests remain in the private
`guide-route-audit-20260925` research directory alongside the native reports.

## Limits that remain visible

A geometric route is not proof of live availability. The collector does not
simulate every party, plot flag, moving actor, puzzle state, timed input or
world-map journey. A collected item's old position, a carried insect's reset
position and a cutscene actor's initial position are not interchangeable with
live navigation targets. These witnesses remain in the report rather than being
silently marked passed.

In particular, alternative ship-side NPC positions in Costa del Sol harbor
still require a matching live-state replay. Several compulsory entry animations
remain labeled for further state review rather than normalized by assumption.
Some Ancient Forest throwing approaches are offered only from the triangles or
sides supported by native replay; directional take-off labels still need live
camera/input confirmation. Two stateful field transfers also remain unavailable
when their resulting entry position cannot be established safely.
Minigame completion and puzzle solutions are outside a field-route proof.

The new audit makes future checks reproducible. It does not establish that the
entire game has been completed with the mod or that no route defects remain.
