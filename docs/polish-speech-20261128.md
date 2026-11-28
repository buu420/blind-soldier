# Polish own speech

Blind Soldier's Polish interface catalog had the same 153 base prompts as every other
language. Almost everything the mod composes itself (navigation lines, readouts, minigame
status, the settings menu) is built from pieces, so with the Polish fan translation the
player heard English: `Story, Barret. up 3.` or `Story: none for this field yet.` matched no
entry even though `Story` and `up {0}` had one.

1.0.5 solved this for Russian with own-speech catalogs and a separate path for composed
mod speech. Polish now uses the same path.

## Catalogs

- `Localization/pl.json`: the 153 base prompts (unchanged role).
- `Localization/pl.runtime.json`: 5,717 generated runtime prompts. They cover every key of
  `ru.runtime.json` (the author's list) plus the speech literals and templates of the runtime
  projects, the story/object target labels, story guidance and world-map location labels
  that list lacked. Spell and command names follow the Polish game's kernel2; place names follow
  the translation's own map names (`MPNAM`); character, item, materia and enemy names stay as
  the Polish game shows them.
- `Localization/pl.composition.json`: the 44 trusted composition entries of
  `ru.composition.json`, in Polish. As for Russian they are kept out of native-text matching.

Names the Polish game keeps in English are deliberately absent, so a native line that is just
that name is not rewritten: the materia `Fire`, `Barrier`, `Shield`, `Poison`, `Steal`,
`Morph`, `Manipulate`, `Underwater`, and `Black Materia`.

## Runtime

`BlindSoldierLocalizer.ComposesModSpeech` is true for `ru` and `pl`. Everything 1.0.5 gated on
`"ru"` for composed speech uses it: loading `{code}.runtime.json` and `{code}.composition.json`,
`LocalizeModSpeech`, capture localization, the native-template restriction, the x86
`LocalizePieceForNativeLine` and the x64 field navigation speaker. Russian-only behavior that
belongs to the 7th Heaven overlay (loaded-text resolver, abbreviation guard) is unchanged.

Two rules are added for every composing language:

- A translation may pick a noun form by a captured number: `{0|sekunda|sekundy|sekund}`
  inserts the one/few/many word for the value of `{0}` (Polish and Russian rules).
- A one-sentence frame does not take whole sentences at its edges. Before, `{0}, {1} and {2}`
  matched the settings menu's opening (`Mod settings. J and L move through settings, U and O
  change a value, …`) as one list and left most of it in English.

## Checks

- `PolishSpeechLocalizationTests` (x86 and x64): every settings entry, both control schemes and
  both runtime policies say no English label, percent, count or on/off; field navigation lines;
  plural forms; native Polish lines and English-kept names are unchanged; catalog placeholders.
- A native-text audit of all 54,250 distinct field messages, pages and lines of V1.07 plus its
  kernel2 strings: `Localize` changes 40, all of them English lines the translation left
  untranslated (`Beach`, `[CANCEL]`, `Temple of the Ancients`), none of its Polish text.
