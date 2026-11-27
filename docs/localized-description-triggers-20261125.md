# Localized field audio-description triggers

Field audio descriptions are keyed to the exact native instruction that shows the
action: field, entity, script slot, byte index within the slot, and opcode, all read
from the loaded script. The English catalog's 946 anchors use English byte offsets.
The official French, German, Spanish and Japanese archives load different script
prefixes in most fields, so until now those fields were silent: navigation knew the
prefixes (`WholeGameEchoNavigationManifest.OfficialStock`), but
`ResolveDescriptionVariant` deliberately refused to apply English offsets to them.

That boundary is kept. English offsets are still never applied to another prefix.
Instead each localized prefix now has its own generated, exact map from every English
cue to the coordinates where that prefix runs the same native instruction. No game
script is edited, and the English cue keys, texts, recurring groups and room history
are unchanged; only the coordinates the loaded script runs them at differ.

## Coverage

Every English anchor is covered in every official language: either the language's
prefix is identical to English (the existing stock path), or the reviewed localized
map authorizes it at its native coordinates. There are no official exclusions.

| Archive | Stock-identical prefix | Reviewed localized rows | Excluded | Reviewed prefixes |
| --- | ---: | ---: | ---: | ---: |
| French `fflevel.lgp` | 19 | 927 | 0 | 607 |
| German `gflevel.lgp` | 21 | 925 | 0 | 608 |
| Spanish `sflevel.lgp` | 24 | 922 | 0 | 606 |
| Japanese `jfleve.lgp` | 81 | 865 | 0 | 562 |
| Polish V1.05 `flevel.lgp` | 39 | 906 | 1 | 586 |
| Polish V1.07 `flevel.lgp` | 16 | 929 | 1 | 608 |

The manifest holds 3,504 exact identities (five prefixes are shared by two official
archives: fields 225, 459, 527, 710 and 742, and 68 by both Polish releases), 5,371
authorized rows and two exclusions (`crater_1` in each Polish release).
All 97 `FieldMovieNarrationPolicy` film anchors are English catalog MOVIE cues, so the
reviewed rows give every one of them an exact localized position: 507 anchor
occurrences across the reviewed identities. They keep their English coordinates in all
four official archives; Polish moves two of them (see below).

Both runtimes deliver every row in the table, the Polish ones included: x86 through
`Mod.cs`, x64 through `Steam2026FieldCutsceneDescriptionCoordinator`.

## How a row is decided

`tools/LocalizedDescriptionCueAligner` reads each installed archive the way the runtime
identifies a loaded field: SHA-256 of section 1 through the text offset
(`LoadedFieldScriptIdentityReader`), the 8-byte entity names, the 32-slot pointer
table, and each slot's instructions with the engine's opcode lengths
(`FieldScriptNavigationCatalog.ReadAllScriptOpcodes`) at the byte index
`FieldScriptContextReader` reports. A cue is authorized only when all of these hold:

1. **Entity.** English and target entity names are aligned as sequences; an entity is
   mapped only when every maximum alignment pairs it the same way. The only shift in
   the official archives is `woa_1` (709): French, German and Spanish insert `zcheck`
   after `dust`, so English entities 3-21 are target 4-22. This agrees with the
   reviewed navigation map for those prefixes (asserted by the tests).
2. **Slot.** The same slot number of the mapped entity, with the same set of slots
   sharing its pointer (the runtime reports the slot that ran).
3. **Forced alignment.** The whole English slot is aligned with the target slot. The
   cue instruction must be paired, and paired with one and the same target
   instruction, in every maximum alignment; a repeated, moved or optional instruction
   is never guessed into place. Pairs may differ only by: a branch distance; an
   entity operand that names the mapped entity (REQ, REQSW, REQEW, MOVA, TURA, DIRA,
   CC, GETDIR, GETAXY, GETAI); MESSAGE, MPNAM or ASK text-table ids and ASK choice
   lines; WINDOW, WSIZW or WMOVE geometry.
4. **The instruction itself.** The cue's own instruction must be byte-identical (with
   the entity map for REQ-family targets). The single exception is MPNAM's room-name
   text id, because an area description belongs to the field, not to the message
   index. All six such cases are the localized names of the same rooms:

   | Archive | Field | English id and text | Target id and text |
   | --- | --- | --- | --- |
   | fr | 182 `chrin_1a` | 29 church in the slums | 0 église des Taudis |
   | de | 182 `chrin_1a` | 29 church in the slums | 0 Kirche in den Slums |
   | fr, de, es | 188 `ealin_1` | 96 Aerith's House | 97 Maison de Aerith / Aeriths Haus / Casa de Aerith |
   | de | 509 `chorace` | 59 Chocobo Square | 60 Chocobo-Rennen |

5. **Control flow.** Every forced branch pair in the slot must land before the same
   next common instruction, and at least half the slot must align (similarity 0.5).
6. **Films.** A MOVIE cue's nearest preceding PMVIE in the slot must be forced and
   identical, so the same film number starts.

The generator also reports each REQ-family cue's requested script: in every archive it
is identical or differs only in window and text presentation.

## Edited slots that still align

Most changed slots differ only in window geometry and text ids. These official slots
contain other edits; each cue in them is still the identical instruction, forced, with
consistent branches:

- fr 192 `mds6_2` 0/3: WAIT 20 frames becomes 40 at byte 620, after the cues at
  282, 503 and 525.
- fr, de, es 210 `colne_5` 7/4: an extra WINDOW at byte 40, after the cue at 0.
- fr 211 `colne_6` 12/9: the ASK lists its three options in another order and the
  IFUB tests at 53 and 91 swap 0 and 2 to match, so each answer still reaches the same
  message. All three branches rejoin before the cue REQ at 125 ("Don Corneo presses a
  hidden switch").
- fr, de, es 242 `blin62_1` 2/0: an extra BITON `82 30 B6 02` at byte 8, after MPNAM
  at 2.
- fr, de, es 686 `gaiafoot` 3/0: an extra IDLCK `6D 5D 00 01` at byte 6, after MPNAM
  at 0.
- fr, de 746 `las0_3` 0/0: ASK choice lines 2-3 become 3-4 and the matching IFUB value
  3 becomes 4 at byte 61; MPNAM is at 0.
- es 416 `junone7` 0/0: the English MUSIC `F0 00` at byte 0 is absent, so MPNAM moves
  from byte 2 to byte 0. This is the only moved official cue apart from `woa_1`.
- es 502 `clsin2_1` 0/0: FMUSC `FC 02` at byte 195 is absent; the cues at 14-152
  precede it.

## Creature gates

Field 767 (`las4_4`) changes prefix in every archive, but entity 12's two OFSTW episodes
(script 3 byte 12, script 4 byte 22) are identical slots. The warning MESSAGE at
entity 3 slot 6 byte 44 is the identical `40 01 0A` (window 1, dialog 10); the entity
list is unchanged; and every model-assigning CHAR and PC is forced exact, so
`FieldCreatureApproachReader`'s creature model slot 8 still holds. For exactly those
reviewed prefixes:

- `FieldCreatureApproachNarrationCoordinator` accepts the identity, and its canonical
  key check maps the episodes to 767/12/3/12 and 767/12/4/22.
- `FieldCreatureWarningReader` uses dialog 10 and reads that page from the loaded
  text table. With the installed archives the tests read each localized warning page
  and confirm a changed dialog operand fails closed.

## Polish fan translation

The public Bunio translation V1.05
(<https://www.bunio.eu/>, `Final Fantasy VII Steam spolszczenie V1.05.zip`, SHA-256
`5A32AC14F76E75F3ECE3B17967A54B87AF67E270CF80BE4F86B3B38D64788B63`) replaces
`data/field/flevel.lgp` (SHA-256
`138AEBBE8A920371A1B3C221A05D69D7960403882606268D6637FB759B1FEAD4`). Its
`WINDOW.BIN` matches the detector's Polish fingerprint
`84886B3F59DFB302A2936B3924E8C468790D582C3F11FC0508106DA42A01FEA3`.

It does not preserve the English prefixes: 39 cues are in fields whose prefix is
identical, and 586 cue fields change. Its scripts appear to be built on pre-2013
flevel data: several 2013 additions are absent (for example the REQ at `md1stin` byte
322, the AKAO at the start of `kuro_4`, the party setup in `crater_1`, and the
`line3_d`/`_e`, `down_8_d`/`_e` and `jump_6_d`/`_e` entities in 747, 749 and 757).
Within the changed fields 637 cues are in identical slots and 269 align; these Polish
cues move: 290 MPNAM 5 to 3, 322 MPNAM 54 to 64 and WAIT 446 to 456, 607 MPNAM 32 to
18, 729 MOVIE 104 to 101 and 730 MOVIE 90 to 70.

### V1.07

V1.07 (`Final Fantasy VII Steam spolszczenie V1.07.rar`, SHA-256
`573E3566F209B8341E9DC30CD5A5974937348DB7025008B1CDECC4B38A0544BC`) replaces
`data/field/flevel.lgp` (SHA-256
`FE1EBCE2BD6E0D4A8DD3B9A82DA3B5B1BAFD28FFA1D4DF85EDC0BC9835CC7BB6`), adds
`data/battle/scene.bin` and `data/lang-en/kernel/kernel.bin`, and replaces
`kernel2.bin`. It ships no `WINDOW.BIN` and no executable (its menu text comes from a
7th Heaven hext mod), so it is recognized by its kernel item descriptions instead of the
font fingerprint. Its scripts keep V1.05's structure (the same 44 fields differ in
length from the 2026 English data) but adjust window geometry in most fields: only 118
of 702 prefixes equal V1.05, and 68 of the cue fields share one. Every cue resolves
exactly as in V1.05, with the same six moved Polish cues (the two nvlmk films included)
and the same `crater_1` exclusion. The aligner takes `--polish` once per release and
reviews each archive on its own; both share the `pl` archive code.

Its kernel2 leaves an empty 17th entry in the last section (summon attack names). The
engine reads 16; `Kernel2TextDatabase` now drops empty entries past the fixed counts and
still rejects any other count. V1.07 also writes `Ź` in the former `Ü` slot (0x66).

Excluded: Polish 700 `crater_1` 1/0/0 MPNAM (both releases). The MPNAM is forced and identical, but
only 23 of the English slot's 53 instructions align (the Polish slot lacks the 2013
party setup), below the similarity rule. It stays silent rather than being argued
into place.

The navigation manifest does not know the Polish prefixes, and that is unchanged:
navigation for the 586 changed Polish fields is still unavailable. Descriptions are
delivered for them. Both hosts' description gates now ask
`LocalizedFieldScriptIdentityPolicy.AllowsDescriptions`, which accepts a reviewed
navigation identity or an exact reviewed localized prefix and nothing else: one changed
prefix digit, or a reviewed prefix observed in another field, stays silent. The x86
host also caches reviewed localized identities
(`LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity`), so its description hook
no longer re-hashes the loaded script on every opcode in those fields; unknown
prefixes are still read again every time. Its identity log line now names the archive,
for example `variant=Localized pl`.

The two moved Polish films are the nvlmk film (movie 34): `zcoal_2` English entity 4
slot 0 byte 104 runs at Polish byte 101, and `zcoal_3` entity 11 slot 3 byte 90 at
byte 70. `FieldMovieNarrationPolicy` matches English coordinates, so on raw Polish bytes
the film start opened no opportunity and the cue was read at once, before the film. In
the no-recording mode non-English languages use, the film's own cue schedule then
described it a second time. Both hosts now ask the film policy about
`LocalizedFieldScriptIdentityPolicy.FilmAnchor`, at native ingress and at delivery.
For a reviewed localized prefix that is the English anchor its reviewed row names, and
for an unreviewed position of that prefix it is no anchor, so raw localized offsets are
never matched against English ones. Every other identity (stock, Echo-S, Postscript,
unknown or unreadable) keeps the raw coordinates it always used, so official and Echo-S
behaviour is unchanged. The moved films now wait for the native film and start
like English.

## Runtime

- `LocalizedFieldDescriptionCueManifest.Generated.cs` is the generated data; it records
  every source archive's SHA-256 and the English catalog fingerprint.
- `LocalizedFieldDescriptionCueManifest.cs` parses it once, keyed by field and exact
  prefix. A malformed or stale row is dropped and counted rather than thrown, so it can
  never take English descriptions down; the tests require zero such rows.
- `EchoSFieldCutsceneDescriptionTracker` sends identities that resolve to no other
  variant to `LocalizedFieldCutsceneDescriptionTracker`, which runs a normal
  `FieldCutsceneDescriptionTracker` over that identity's cues, so opcode checks,
  once-per-visit delivery and recurring groups behave as in English.
- `EchoSCompatibilityManifest.ResolveCanonicalCueKey` maps a reviewed localized event
  back to its English key, which the visible-actor checks and the creature coordinator
  use.
- Room history stays keyed by field id, and cue texts stay English for the
  translation step.
- `EchoSFieldCutsceneDescriptionTracker` now resets every catalog when the observed
  field changes. Without it, a room read through one catalog with a room read through
  another in between would not be a new visit in the x86 runtime, which only resets on
  module changes.

- `LocalizedFieldScriptIdentityPolicy` (same file) holds the three decisions both hosts
  share: the description gate, the identity cache rule and the film anchor. `Mod.cs`
  uses it in `observedAreaKnown`, `TryGetLoadedFieldScriptIdentity`, the film-ingress
  call in the cutscene hook and the film `Begin` in delivery. The x64 coordinator uses
  it in `ReadSupportedIdentity`, its film ingress and its film `Begin`.

`EchoSCompatibilityManifest.ResolveVariant` still returns `Unknown` for localized
prefixes, so the Ancient Forest readers, Echo-S reactor timer and other variant gates
are unchanged.

## Regenerating and testing

```powershell
dotnet build tools\LocalizedDescriptionCueAligner\LocalizedDescriptionCueAligner.csproj -c Debug --artifacts-path D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\localized-cues-artifacts
& D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\localized-cues-artifacts\bin\LocalizedDescriptionCueAligner\debug\LocalizedDescriptionCueAligner.exe `
  "D:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir" D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\report-final `
  --polish D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\polish\workingdir `
  --polish D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\polish-v107\workingdir `
  --emit Ff7.Accessibility.Reloaded\LocalizedFieldDescriptionCueManifest.Generated.cs
```

The reports beside the manifest list every decision (`cue-alignment.tsv`), edited
slots, entity maps, MPNAM text evidence, film anchors and the creature checks.
Regeneration from the same archives is byte-identical.

```powershell
dotnet build Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj -c Debug --artifacts-path D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\localized-cues-artifacts
$env:FF7_ACCESSIBILITY_DATA_ROOT = "D:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir"
# One or more Polish release roots, separated by ';'
$env:FF7_ACCESSIBILITY_POLISH_DATA_ROOT = "D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\polish\workingdir;D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\polish-v107\workingdir"
& D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\localized-cues-artifacts\bin\Ff7.Accessibility.Reloaded.Tests\debug\Ff7.Accessibility.Reloaded.Tests.exe --localized-cues-only
```

Without the data roots the manifest, tracker, canonical-key, recurring-group, revisit
and creature tests still run. With them, every installed field is loaded through the
production identity reader: stock-identical prefixes must resolve as stock, every
other prefix must be in the manifest for that archive code and nothing else may be (for
`pl`, the union of every supplied Polish release), every
authorized row must be the installed native instruction and deliver its cue, and each
archive's coverage must add up to all 946 anchors. If the English catalog's anchors
change, the fingerprint test fails until the manifest is regenerated.

`LocalizedDescriptionDeliveryTests` (in the same x86 flag, and linked into the x64 runner)
cover:
- the gate and cache rule for all 1,126 Polish prefixes (V1.05 and V1.07) against their one-digit
  near-misses, stock, official, Echo-S, the Echo-S disclaimer and unknown prefixes;
- both moved films resolving to their English anchors and starting through the shared
  film tracker, with no anchor taken from raw localized offsets;
- unchanged raw anchors for stock, Echo-S, unknown and unreadable identities;
- every film anchor being a reviewed catalog cue;
- a source contract holding the four `Mod.cs` call sites to the policy.

The x86 host cannot be constructed in a test; that contract is what fails if `Mod.cs`
stops using the policy. The x64 coordinator is tested end to end by
`Steam2026LocalizedDeliveryTests`:
- the reviewed Polish `nvmkin1` WAIT is queued at Polish byte 456 and delivered;
- an unknown prefix queues and says nothing, at either byte;
- each moved Polish film is held until the native film starts, then described by its
  own track once, without the paragraph.

```powershell
dotnet build Ff7.Accessibility.Steam2026X64.Tests\Ff7.Accessibility.Steam2026X64.Tests.csproj -c Debug --artifacts-path D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\localized-cues-artifacts
& D:\BlindSoldier-localized-model-launcher-20261125\localized-cues\localized-cues-artifacts\bin\Ff7.Accessibility.Steam2026X64.Tests\debug\Ff7.Accessibility.Steam2026X64.Tests.exe --localized-cues-only
```

Remaining limits:

- Navigation (exits, objects, NPCs, Story targets) for the 586 changed Polish fields is
  still unavailable: the Polish prefixes have no reviewed navigation entity maps.
- The x86 field status command accepts reviewed description identities through
  `LocalizedFieldScriptIdentityPolicy.AllowsDescriptions(statusIdentity)`. The x64
  navigation status action still depends on its navigation observation; automatic
  description delivery uses the separately reviewed description identity.
- Polish 700 `crater_1` keeps its explicit MPNAM exclusion. Its room is still offered
  once from the stable-room cold start, which is keyed by field rather than by offset.
- Unknown and unreadable identities keep the raw film-anchor coordinates the film
  tracker has always used; only reviewed localized prefixes are canonicalized.

This is offline and native-data proof. It does not establish in-game speech, timing
against a running localized game, or listener review.
