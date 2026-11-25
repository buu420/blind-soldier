# Description translation review

This review covers the five shipping description catalogs in
`Ff7.Accessibility.LegacyLayout/AudioDescriptionLocalization/`: `fr.json`, `de.json`,
`es.json`, `ja.json` and `pl.json`. English keeps its recordings, and `en.json` was
not changed. Each catalog maps every English description text, byte for byte, to
the text the screen reader speaks in that language.

## Coverage

Every one of the 1,615 entries was reviewed with Claude Opus in all five languages.
This was an AI language review, not a human native-speaker review or listening test.
The review went in 39 chunks of up to 59 entries. Each chunk was recorded only after
its corrections were applied. "Changed" counts entries that differ from the
machine-translated drafts (`description-translations-v2`), which were the
catalogs when the review started. "Unchanged" entries were read and judged
correct as drafted.

| Language | Reviewed | Changed | Unchanged after review |
| --- | ---: | ---: | ---: |
| French | 1,615 | 1,503 | 112 |
| German | 1,615 | 1,366 | 249 |
| Spanish | 1,615 | 1,493 | 122 |
| Japanese | 1,615 | 1,615 | 0 |
| Polish (Bunio profile) | 1,615 | 1,489 | 126 |

The progress record is
`D:\BlindSoldier-localized-model-launcher-20261125\translation-quality-progress.json`.
Every correction is kept as plain text under
`D:\BlindSoldier-localized-model-launcher-20261125\translation-review\corrections\chunk-*.txt`,
one `@index language` header followed by one line of text. Files are applied in
name order, so `chunk-027b-consistency.txt` and `chunk-028b-consistency.txt`
override earlier lines.

## Method

- Entries were read in game order, with their source context (field, movie cue
  and time, battle animation and frame) next to the English and all five drafts.
- No new machine translator was used. The drafts were treated as untrusted. Most
  were rewritten because the offline models misread game vocabulary. Examples:
  "party" became fête, fiesta, Party or impreza; "staff" (a stick) became
  personnel or Mitarbeiter; a treasure "chest" became poitrine, pecho or Brust;
  "Materia" became Substanz; "Holy" became Heilig; "crag" became cangrejo (crab).
- Names and terms follow the official localized game text wherever it exists:
  - field dialogue dumped from each language's `flevel`;
  - summon names from `kernel2.bin`;
  - the epilogue card from the credits archives `data/cd/cr_{us,fr,gm,sp}.lgp`
    (`after_1.tex`).

  Polish follows the Bunio fan translation's naming. Japanese uses the original
  katakana and kanji names, such as エアリス, レッドXIII, 魔晄炉 and 星.
- Movie paragraphs repeat their cues. A paragraph's translation is the cue
  translations joined with single spaces; Japanese uses no space. A check
  confirmed that all 378 sentence-aligned cue and paragraph pairs match in every
  language.
- Nothing was added that the English does not say. In particular, summons stay
  unnamed when the English describes only what they look like.

## Style decisions

- Quotation marks: French « », German „“, Spanish «», Japanese 「」, Polish „”.
  " - " dashes were removed because screen readers announce them.
- Dialogue labels: French `Nom : réplique`; German, Spanish and Polish
  `Name: line`; Japanese `クラウド「…」`, with continuation lines also in 「」.
- Japanese uses the plain descriptive form throughout, with no です or ます.
- German uses "Gegner" for the battle enemy.
- Battle "party" means the group: le groupe, die Gruppe, el grupo, 仲間たち,
  drużyna.
- Names and terms:

  | English | French | German | Spanish | Japanese | Polish |
  | --- | --- | --- | --- | --- | --- |
  | Aerith (all spellings) | Aeris | Aeris | Aeris | エアリス | Aeris |
  | Red XIII | Rouge XIII | Red XIII | Red XIII | レッドXIII | Red XIII |
  | Sephiroth | Sephiroth | Sephiroth | Sefirot | セフィロス | Sephiroth |
  | Highwind | le Hautvent | die Highwind | el Viento Fuerte | ハイウインド | Highwind |
  | moogle | Mog | Mog | Mog | モーグリ | moogle |
  | Lifestream | la Rivière de la vie | der Lebensstrom | la Corriente vital | ライフストリーム | Strumień Życia |
  | Meteor | le Météore | der Meteor | Meteorito | メテオ | Meteor |
  | Holy | le Sacre | Holy | Sagrado | ホーリー | Holy |

- On-screen English inside unlocalized films stays verbatim in quotes, because
  that is what every player sees. Examples are the Shinra car advertisement
  labels, "GOBLINS BAR" and "SONS".

## Defects in the drafts that were specifically confirmed fixed

- German Jessie soot scene (155) and Kalm flashback (259): placeholder debris
  `zxq12xqz` replaced with Cloud's name in a full rewrite.
- Japanese Tifa memory (184) and air-duct meeting (231): broken `ZXQZ` name
  mappings rewritten.
- Spanish English sentences:
  - mansion bookcases (28);
  - lightning (1383) and ice (1404);
  - frost sentence with "jagged" (1379);
  - Cid's "crouches and rockets upward, spear poised" (1505), which is now
    "Cid se agacha y sale disparado hacia arriba, con la lanza preparada.".
- Polish "Silver hair flowing, he walks into the towering flames." now reads
  "Z rozwianymi srebrnymi włosami wkracza w strzeliste płomienie." This applies
  to both the standalone cue (1262) and the paragraph (62).
- Choco/Mog (1374, 1551): the moogle now rides the chocobo and charges in every
  language. Previously it was "se chargeant", 充電, and similar.
- The epilogue card (1614) now uses the official card wording:
  - French: « Cinq cents ans plus tard »
  - German: „500 Jahre später“
  - Spanish: «Quinientos años más tarde»

## Checks on the final files

- All five files are valid UTF-8 JSON without a BOM. They have sorted keys, an
  indent of 2 and CRLF in the working tree, matching `en.json`.
- Each has exactly the 1,615 keys of `en.json`, which equals the current
  `--export-description-texts` inventory.
- No value is empty or contains a newline. No value contains `zxq`/`xqz`, `▁`,
  `@@` or U+FFFD.
- No value equals its English key, except German `Barret: Hey!`, which
  `AudioDescriptionLocalizationTests` already lists as reviewed-identical.
- No English sentence of five or more words survives in any translation. This is
  the rule `AudioDescriptionLocalizationTests` enforces.
- A word scan outside quoted on-screen text found no leftover English in French,
  German, Spanish or Polish. In Japanese the only Latin text is レッドXIII and the
  quoted on-screen English.
- Glossary scans found no forbidden draft terms. The remaining "personnel" and
  "poitrine/Brust/pecho" hits are real amusement-park staff and real chests.
- `Ff7.Accessibility.Reloaded.Tests --localized-descriptions-only` was built
  from this checkout to a scratch `--artifacts-path` and passes: "Description
  translations cover 1615 distinct texts in five non-English languages." Its
  `--export-description-texts` inventory still equals the `en.json` key set.

## Open points for native-speaker listening

1. Some localized film subtitles were translated rather than copied. The game
   ships `ending2.avi` and `jenova_e.avi` per language (`data/lang-xx/movies`),
   but only `lang-en` is installed here. So the quoted dialogue in the ending
   film (cues 1070-1125, paragraph 1126) and the JENOVA film (cues 1211-1217,
   paragraph 1218) was translated from the English subtitles. Its wording may
   differ from the French, German, Spanish and Japanese subtitles on screen.
2. The Japanese and Polish credits archives were not available. Japanese uses
   「500年後」. Polish uses „Pięćset lat później”, and it is not known whether the
   Bunio patch replaces this image.
3. Polish is an unofficial fan translation. Its names follow Bunio where known;
   other terms are standard Polish.
4. Some English is kept on purpose, and the screen reader's voice will pronounce
   it in the target language's style:
   - product text such as "new model S five ten";
   - "Final Fantasy 7", which is ファイナルファンタジーセブン in Japanese;
   - the JENOVA label, which is written 「JENOVA」（ジェノバ） in Japanese.
5. How Rouge XIII, Red XIII and レッドXIII are pronounced depends on the user's
   synthesizer.
6. Gold Saucer squares use the localized names, but the in-game map image may
   still be in English.
7. A native listening pass is still recommended for all five languages. Japanese
   and Polish need it most, because their screen-reader voices differ most from
   the reading conventions used here.

## Reproducing the review

The helper scripts are in
`D:\BlindSoldier-localized-model-launcher-20261125\translation-review\`.

| Command | What it does |
| --- | --- |
| `py review.py sheet N` | Prints a chunk with its context |
| `py review.py complete N` | Applies all corrections, records the chunk and rewrites the catalogs |
| `py review.py apply` | Applies all corrections and rewrites the catalogs |
| `py review.py status` | Prints the coverage summary |
| `related.py` | Lists earlier entries that share text with a chunk |
| `cues_from_paragraph.py` | Splits an earlier paragraph translation into cue translations |
| `build_paragraph.py` | Rebuilds a paragraph from its cues |
| `show.py` | Prints entries in all languages |
| `consistency.py` | Checks that every cue translation appears in its paragraphs |
| `qa.py`, `english_scan.py`, `glossary_scan.py` | Run the checks above |
