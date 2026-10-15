# Priscilla and wounded Tifa interaction targets

The supplied cumulative legacy log records repeated empty NPC/Story lists at
field 429 on 2026-10-14 and Tifa's targets disappearing in field 323 during the
2026-10-12 flashback visit. Earlier unrelated reports in the log were not replayed.
The log alone does not identify the installed mod's release version.

## Native evidence and repair

- Dolphin Cove is field 429, `ujunon2`. Entity 12 `prisl` loads CHAR 7,
  `sub_pricilla`. Echo-S replaces her dialogue indices; looking them up in the
  stock text table is deliberately suppressed. Her model has no generic role in
  the naming vocabulary, so the reader dropped her. A reviewed canonical label
  now names her Priscilla, retaining current model mapping, visibility and TLKON
  checks. Existing Story definitions and availability bounds are unchanged.
  The historical empty Story lists lack a contemporaneous game-moment/visibility
  snapshot, so their exact cause is unconfirmed. The repaired NPC target remains
  the normal way to find her when no chapter-bound Story step is active.
- The flashback reactor chamber is field 323, `nvmkin21`. At game moment 382,
  entity 6 draws Tifa's injured pose while entity 7 `ti2` receives Confirm. The
  proxy's Init and slot 5 enable Talk without enabling VISI, with TALKR 40 and
  coordinates `(39,-256,186)`. The NPC reader accepts this reviewed proxy only
  at moment 382 while Talk is enabled. Its Story row uses the existing hidden
  Talk mechanism with the same moment bound. The ordinary moment-383 route
  toward Jenova's chamber is preserved.
- Ghidra 12.1.2 native pseudocode for `ff7_en.exe` function `00636284` confirms
  that Confirm selection uses the Talk flag at event offset `0x61`, facing,
  height and player collision width plus target TALKR; it does not require VISI.
  `0060C94D` dispatches the selected actor's Talk slot. Pseudocode is not
  recovered engine source. Licensed engine and field data remain private.
- The [Fenrir model opcode implementation](https://github.com/dangarfield/ff7-fenrir/blob/master/app/field/field-op-codes-models.js)
  was consulted as a secondary implementation reference. Native script and
  engine observations determine this repair.

## Verification

Separate failing regressions reproduced Priscilla's missing label, wounded Tifa's
missing NPC target, and her missing Story target before their respective fixes.
The dedicated regression suite then passed on both test hosts.

Native checks load actual stock and five reviewed Echo-S script prefixes into an
address-space fixture, use the production catalog's identity/remapping path, and
verify named NPC/Story targets, current coordinates, unavailable targets, and
routes through each variant's own decoded native field walkmesh. Steam uses its
checked planner wrapper. A global allowlist regression limits hidden Talk rows
to wounded Tifa at moment 382 and Wutai's shaking pot.
These are offline native-data tests, not a live game playthrough.

The Story asset changes only the wounded-Tifa row's hidden-Talk flag; all other
definitions, including Postscript guards and the mansion bookcase repair, are
preserved. The generator carries the same flag.

Live gameplay, physical keyboard/controller behavior and audible Prism speech
were not tested. Package, deployment, preservation and publication checks are
recorded separately in the release evidence.
