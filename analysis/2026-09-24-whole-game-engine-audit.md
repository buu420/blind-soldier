# Whole-game engine and script evidence

This pass replaces selected-town extraction with complete inventories of the two
installed English PC versions. Generated game code and game resources remain in
the private research directory; only original audit tools and findings belong in
this repository.

Evidence is retained in a private research directory outside this repository.
Implementation and deployment status is tracked in
`docs/superpowers/plans/2026-09-24-whole-game-engine-audit.md`.

## Native engines

| Input | SHA-256 |
| --- | --- |
| Legacy `ff7_en.exe` | `4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225` |
| Steam 2026 `FFVII.exe`, installed file | `57a23d166d69e46b9e3339f779d4a3c4feb402a989fa7291d0d9b4a1953abb4b` |
| Steam 2026, main module after its normal loader ran | `d7caa76e9cec08e495c0d4334deefaa76b2c9ef2a7dd8d3d0801c52c15873e2a` |

The protected x64 file is not a useful substitute for its loaded code. Its first
Ghidra export returned C for most functions but truncated control flow in 11,118
of them. That export is retained as rejected evidence. A read-only capture of the
normally loaded main module supplied the actual x64 instructions. No game memory
or installed executable was changed. The private capture has PE layout metadata
adapted for analysis and must not be installed as a game executable.

The loaded module contains a contiguous table of 10,953 legacy-to-host function
registrations, anchored independently at the field `REQ` handler. Every table
entry points into initialized executable memory in both engines. Those native
registrations recovered function entries that auto-analysis alone missed.

An additional lookup index links 683 address-bearing FFNx reference names to 676
registered functions in both engines. It pins FFNx commit
`65e4d84faaf8960bfdbcd1d58d0405a58fba1ab7` and retains source links. These are
community name candidates, not independently verified prototypes or semantics;
they are not silently applied as authoritative Ghidra symbols.

The legacy export contains 10,941 internal functions and 234 external imports.
All internal functions have assembly and returned C; two C outputs have truncated
control flow and remain marked unresolved. Fourteen registration addresses are
instruction-aligned alternate entries inside existing functions, with their
enclosing assembly retained. This is not evidence that every decompiler expression
has the original program's semantics.

The final loaded x64 export contains 13,542 internal functions and 301 external
imports. Bounded retries recovered C for 13,539 functions; three retain assembly only.
Two failed host functions map to legacy functions `0041fba4` and `00623d28`, whose
legacy C and assembly are available. The third is host function `7ff70169792a`.
No returned x64 C file has truncated control flow, although 10,492 carry decompiler
warnings. Both exports pass the artifact identity/count checks. There are 35,467
legacy and 421,494 x64 executable bytes outside discovered function bodies.
Unassigned bytes may be padding, data or undiscovered instructions; they are not
silently counted as recovered code.

The x64 Windows unwind table supplied three additional primary function entries
after the native-registration pass. All three now have C and assembly. Chained
unwind ranges were kept separate from primary function entries, as required by
the [Windows x64 unwind format](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170).
The authoritative x64 export is `engine/x64-final/`; `engine/x64-loaded/` retains
the earlier snapshot used for the initial native reviews. The private function
crosswalk points to the final export. Every primary unwind entry is now accounted
for; six unassigned chained ranges remain explicitly listed for review.

The reproducible exporter writes per-function C, assembly with references,
metadata, a call graph, symbols, memory blocks and uncovered executable ranges.
`tools/WholeGameResourceAudit/verify_engine_export.py` checks their identity and
internal consistency. A structurally valid export can still contain decompiler
warnings or failed functions.

## Field control flow

The independent field audit retains all 32 script slots per entity, shared entry
pointers, both sides of conditional branches and cross-entity requests. It
compares these against the shipping parser and records bank dependencies, effects,
native gateways, model/line events and the existing accessibility catalog.

Confirmed native differences are recorded privately in
`reports/root-native-field-findings.md`:

- `JMPFL` (0x11) at legacy address `00613141` adds its word operand plus **one**
  to the instruction pointer. The shipping navigation walker added two.
- `RETTO` (0x07, `00612e47`) loads another slot from the current entity's original
  pointer table and transfers execution to it. Aliased slot pointers are valid.
- Input tests (0x30-0x32) and party/member tests (0xcb-0xcc) have two control-flow
  successors. Treating them as straight-line instructions loses alternatives.
- The native event dispatcher (`0060c94d` / `0060d29b`, independently checked in
  loaded x64 too) uses each requested slot without rejecting shared pointers.
  Nonempty walk events that share an entry with the confirmation event remain valid.
- `MAPJUMP` ends the current field-script execution even when its destination is
  the same field. The field loop reloads it with previous module 1; setup then
  resets the script context and runs Init again. A movement following that jump
  must not become a route in the old execution. This was checked through the
  native handler, field loop and setup chain rather than inferred from the opcode alone.
- Byte and word writes alias the same bank storage. Other scripts and later
  writes can invalidate a previously constant value; assuming it remains constant
  hides valid navigation branches.
- Some operations write the bank storage directly without calling the byte/word
  helpers. Party changes, for example, write bank 3 bytes 9-11. Independent
  regression probes show that ignoring these writes hides valid exits after
  adding, removing or replacing party members. The native-reference review also
  records menu returns, materia results, room-name text and initialization writes.
- The installed PC handler for 0x1b is the unimplemented `006107e1`, returning
  without advancing the script. PSX/community names alone do not establish PC
  behavior. Its occurrence must stay explicit in the audit.
- Movement ownership is essential: native JUMP `00615ca3` moves the current
  script entity's model, and does nothing for an entity without a model. An
  ordinary NPC's jump is not a player traversal. `CC` can change the controlled
  model independently of party order, so filtering by party slot zero alone is
  insufficient. All modeled actors in the installed fields fit the checked
  entity range (highest PC-bound entity 33; highest CHAR-bearing entity 45).

The private baseline and subsequent comparison retain unresolved or dynamic
conditions. A possible branch, unnamed model, debug field, unused script or item
grant during an automatic event is not automatically a player-accessible target.

## World and battle resources

Both installations were fully read. Their world archive, battle scene archive and
kernel hashes match. Each independently produced inventory contains:

| Resource | Inventory |
| --- | --- |
| World events | 207 call-table entries across `wm0.ev`, `wm2.ev`, `wm3.ev` |
| Battle scenes | 256 compressed scenes |
| Formation/enemy AI | 1,555 scripts |
| Kernel | 27 compressed sections, including 11 character AI scripts |

The decoders follow branches across entry boundaries, retain aliases, validate
bounds and preserve unknown or overlapping instructions as findings. Both runs
completed without structural decoding errors. This inventories the data; it does
not expose enemy AI or future story information in the mod.

Private outputs: `resources/legacy/`, `resources/x64/`, and `reports/`.
Original tools and synthetic tests: `tools/WholeGameResourceAudit/`.

Format references used for cross-checking:

- [FF7 world script format](https://ff7-mods.github.io/ff7-flat-wiki/FF7/WorldMap_Module/Script.html)
- [FF7 world opcodes](https://ff7-mods.github.io/ff7-flat-wiki/FF7/WorldMap_Module/Script/Opcodes)
- [FF7 battle scene format](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Battle/Battle_Scenes.html)
- [ff7tools scene decoder](https://github.com/cebix/ff7tools/blob/master/ff7/scene.py)
- [ff7tools kernel decoder](https://github.com/cebix/ff7tools/blob/master/ff7/kernel.py)
- [Pinned FFNx engine references](https://github.com/julianxhokaxhiu/FFNx/blob/65e4d84faaf8960bfdbcd1d58d0405a58fba1ab7/src/ff7_data.h)

## Verification boundary

All extracted native code is approximate reverse-engineering evidence. The
inventory does not establish a full live playthrough, every possible save state,
or every input and animation timing. Runtime changes must have independent
regression checks, installed-data tests on both hosts and deployment verification.
Remaining findings stay in the coverage ledger rather than being reported as
completed gameplay coverage.

## Complete archive baseline

The first independent field pass decoded 341,152 instructions across all 702 mapped
field files in each installation, preserving 337,088 original script slots.
Every corresponding field hashes equally between the two installed archives.
The baseline reports 890 candidates per archive; these are findings to classify,
not 890 confirmed player-visible defects. The baseline is retained privately under
`baseline/` before any runtime repair.

A separate complete LGP inventory accounts for all 753 entries: 702 mapped fields,
18 platform field variants, nine tutorial assets, 22 textures and two metadata
files. All variants have the same entry tables as their mapped base fields.
Seventeen have identical instruction bytes. `jundoc1a.NX` changes only two
confirmation/cancel key operands, retaining the same control-flow boundaries.
Auxiliary assets and their hashes are retained outside Git.

Cross-module checks matter: four apparent unwritten story conditions at field bank
13 address 82 bit 1 are correctly set by the underwater world script when obtaining
the Key to the Ancients. They are not missing field-story conditions. The exact
writer and call-table witnesses are in `reports/world-progression-witnesses.json`.

## Catalog changes

The original and repaired catalogs have been compared across every mapped field
in both installed archives. These are definition counts, not a count of routes
proven playable in every state:

| Definition | Original | Repaired |
| --- | --- | --- |
| Scripted traversals | 1,043 | 1,989 |
| Scripted exits | 575 | 645 |
| NPC declarations | 1,753 | 1,762 |

The traversal comparison records 1,129 additions and 183 removals, each removal
with a native justification. No original scripted exit definition was lost.
The single removed NPC declaration was `582/yufy2` entity 1, Cloud with an empty
dialogue list. Contact-only discovery adds five people to the live NPC list:
the Wall Market promoter, Bugenhagen, and three Icicle Inn soldiers. Scenery and
the developer warp remain excluded from that list. The larger declaration count
also includes those classified model definitions.

Supported live flag checks now govern 115 scripted exits. These checks preserve
routes when required bytes cannot be read; they do not prove every dynamic
condition or save state. Native bank writes, event ownership,
same-field reloads and shared script entries are covered by the new execution
regressions. Exact comparisons and removal reasons remain in the private after
reports and the checked-in regression fixtures.

## Integration status, 2026-09-25 06:43 UTC

Both complete runtime test hosts passed the earlier systemic repair. A later
independent check found a false route where a family ladder accepted Cloud as
controlled while Tifa led, although its native party-slot-zero request moves
Tifa. The fix now preserves each leader/model pairing. All 22 unchanged
independent probes pass against both newly built runtime assemblies, with every
copied dependency hash checked. Evidence is in `after/root-current-guard-review/`;
the original failure remains in `reports/root-leader-pairing-blocker.md`.

The separate shared-layout test host also passes after its stable-memory fixture
was supplied with the newly required script-context pointer and enabled MPJPO
byte; its assertions remain unchanged.

Both production coordinators now apply shared live predicates to supported
conditional script exits. Three additional installed-data regressions pass in
each runtime: the Highwind's conditional exit disappears and reappears with its
saved flag, its unrelated hatch remains available, and Junon's guarded exit
requires the correct game moment and flag together. This brings the independent
check set to 25 per runtime. The evaluator deliberately omits predicates whose
values can change in the triggering routine or concurrent scripts; its current
scope is not a proof of every dynamic exit.

Contact-only NPC discovery is implemented with native visibility and collision
gates. The first independent contact probe exposed premature arrival before the
native collision. A subsequent repair observes the target entity's priority and
script slot; the unchanged pre-contact probe now passes against fresh x86 DLLs.

A second independent production-reader/controller probe exposed a missed
activation when the whole Contact script ran during dialogue suppression.
Autowalk then pushed into the still-visible NPC again after control returned.
The controller now retains that native activation during suppression. The
unchanged lifecycle probe passes against both fresh architectures: approach
continues, activation is observed, and movement remains stopped after dialogue.
Evidence and exact DLL hashes are in `after/root-contact-lifecycle-fixed/`;
the original failure remains in `after/root-contact-suppression-review/` and
`reports/root-contact-suppression-blocker.md`.

## Final integration, 2026-09-25 08:02 UTC

The repaired development build is deployed to both local installations, retaining
version 0.6.8. The installer changed 18 code/catalog files per installation and
verified each destination against the tested package. All 7,618 protected
configuration, narration and other asset files retain their original hashes.
Backups are under each game's `AccessibilityBackups/whole-game-engine-20260925-030155`
directory. No game executable or save was modified, and this pass was not published
as a GitHub release.

The final Story catalog contains 1,390 definitions. Alongside the systemic parser,
ownership and contact repairs, this pass adds or corrects:

- Bugenhagen's contact step before descending the Cosmo Canyon stairwell;
- the Gold Saucer information counter as an interactive object;
- Lucrecia's native reward/collection conditions;
- the Fort Condor summit and Junon Respectable Inn exits driven by player triangles;
- returning Junon's dolphin whistle location, completing only on native triangle 25;
- the later Corel pursuit's return route, selecting the gateway on the player's
  current upper or lower track.

The independent Story matrix exposed that last Corel error during final checks.
Its former upper-track target could not be reached from the lower North Corel
arrival. The corrected test retains that negative case, verifies Cid's actual
movement ownership and bridge locks, and checks all four native arrivals. The
original failed reports are preserved alongside the passing results.

The final ledger preserves all 890 original findings: 160 repaired, 485 covered by
existing behavior, 53 automatic events, 30 reviewed exclusions, 158 unused/debug
cases, and four conditions written by another game module. No original finding is
left without a disposition. This is a review of those specific findings, not a
claim that every live state is verified. Identity checks and source hashes are in
`after/root-final-ledger-identity.json`; the ledger is
`after/claude-stage2-disposition-ledger.json`.

Final verification:

| Check | Result | Private evidence under `after/` |
| --- | --- | --- |
| Fresh full x86, x64, shared-layout and parity suites | All passed; all 925 source inputs unchanged | `root-final-suite-corel-final-20260925/` |
| Story matrix | All 280 checkpoints passed in all four runtime/archive combinations | `root-story-corel-final/` |
| Complete actual x64 runtime audit | All 702 fields read; zero unusable catalogs; catalog matches the legacy runtime field for field | `root-actual-x64-corel-final/`, `root-actual-runtime-catalog-comparison.json` |
| Packaged DLL regressions | Per runtime: 22 native-execution cases, three installed guard cases, one pre-contact negative case, six contact-lifecycle checks passed | `root-package-verification/` |
| Package/source identity | Tested sources unchanged after publishing | `root-package-source-verification.json` |
| Deployment | 36 changed files verified; 7,618 protected files unchanged | `deployment-verification.json` |

## Remaining verification limits

This is whole-game extraction and systematic static/state verification, not an
end-to-end live playthrough. The evidence does not certify every possible save,
animation timing, minigame or path.

The optional-object geometry audit checked 72 definition rows representing 69
distinct objects, with 224 approaches per actual runtime. Every row has at least
one successful approach, but 25 individual arrivals still lack a direct geometric
route. These are retained in Sector 7, Cosmo Canyon and Ancient Forest; the latter
includes puzzle ledges that require native jumps. They cannot be declared fixed
or inherently impossible from this replay alone. It assumes possible LINE/mover
transitions and does not simulate live locks or all cross-room travel. See
`reports/root-town-route-boundaries-final.json`.

The three remaining manual-slot heuristic warnings were reviewed separately:
one is a debug-room script and two are automatically requested final-dungeon
battle routines with no visible NPC model or LINE. They remain in the raw reports;
their native callers and exclusions are recorded in
`reports/root-story-manual-candidates.md`.

Guard predicates cover supported persistent-byte conditions, with unreadable
bytes leaving a possible exit offered. Input, temporary-state and unsupported
concurrent conditions are not fully simulated. A Contact script that starts and
returns between observations without yielding can still escape the polling
reader; the route's stall handling then ends the approach. The Great Glacier map
view remains undescribed, and a fixed destination is not promised for the random
Corel Desert exits. Native decompiler failures and unassigned code ranges are
listed above rather than counted as recovered C.
