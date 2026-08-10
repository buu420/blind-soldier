# Multilingual Accessibility Design

## Goal

Make Blind Soldier follow the language selected for Final Fantasy VII in both
supported runtimes. Native dialogue, menus, item names, ability names, battle
text, and field text must be decoded from the matching game-language assets.
Accessibility-only speech must use the same language whenever a bundled
translation exists.

The initial supported languages match the current Windows releases of Final
Fantasy VII: English, French, German, Spanish (Spain), and Japanese. Missing
individual accessibility translations fall back to English so that a blind
player receives information rather than silence.

## Language selection

One shared `Ff7GameLanguageContext` is established during mod startup and is
used for the entire process lifetime. Detection order is deterministic:

1. `GameLanguage` in Blind Soldier configuration when set to `en`, `fr`, `de`,
   `es`, or `ja`;
2. the x86 executable suffix (`ff7_en.exe`, `ff7_fr.exe`, and equivalents);
3. the matching Steam app manifest's `UserConfig.language` value;
4. the only usable `data/lang-*` directory when exactly one is present;
5. English, with a diagnostic explaining why fallback was used.

The default configuration value is `auto`. Invalid overrides are logged and
continue through automatic detection. Detection verifies that the selected
language directory contains `kernel/kernel2.bin`; a stale Steam preference
does not select missing data.

The context exposes the language code, display name, Japanese-text flag,
localized kernel path, and field archive name. Field archives are selected as
follows:

| Language | Language directory | Field archive |
| --- | --- | --- |
| English | `lang-en` | `flevel.lgp` |
| French | `lang-fr` | `fflevel.lgp` |
| German | `lang-de` | `gflevel.lgp` |
| Spanish | `lang-es` | `sflevel.lgp` |
| Japanese | `lang-ja` | `jfleve.lgp` |

Both runtimes use this same context; x86 and x64 must not independently infer
different languages.

## Native game text

Field and kernel text use related but distinct FFVII encodings, so decoding is
split into explicit field-text and kernel-text operations. The existing ASCII
behavior is preserved while adding the complete Western extended-character
table and Japanese multibyte tables required by the shipped data.

The tables are derived from the open-source FF7Tools decoder under its
permissive notice, with attribution retained in source and release notices.
Decoders accept an explicit language context so tests and offline readers do
not depend on process-global state.

`KERNEL2.BIN` is parsed structurally. Its eighteen length-prefixed sections
have fixed meanings and entry counts; item, ability, weapon, armor, accessory,
materia, battle, and summon names are selected by section index. English words
such as `Potion` or `Cure` are never used to discover a section. Malformed
offset tables, unexpected entry counts, or out-of-bounds data fail safely and
leave the corresponding resolver unavailable.

The field-text resolver opens the language-appropriate field archive. Existing
field ids, script opcodes, window geometry, cursor state, menu ids, and battle
state remain authoritative. Language-specific visible words are not used as
state gates where native state or stable geometry already identifies the UI.

Text normalization retains printable Unicode. It removes control characters
and FFVII layout codes without dropping accented Latin characters or Japanese
characters.

## Accessibility-generated speech

Blind Soldier bundles one UTF-8 JSON catalog per supported language. Each
catalog maps stable message keys to either a complete phrase or a composite
format string. The English catalog is complete for generated messages in the
first test release. French, German, Spanish, and Japanese catalogs cover the
core interactive surface:

- navigation categories, directions, route lifecycle, arrivals, ladders, and
  progress;
- battle party selection, HP, MP, status effects, limit gauge, encounters,
  victory, rewards, and targets;
- title, save/load, party formation, status, equipment, materia, and common
  menu controls;
- the x64 escape/system menu, toggles, lists, sliders, controls, and help;
- timers and accessibility minigame prompts.

The localizer supports exact messages and typed templates. It never translates
substrings inside native game text. Unrecognized text is returned unchanged;
for a known key missing in the active catalog, the matching English value is
used and a bounded diagnostic is emitted once.

Both final speech endpoints localize before sending UTF-8 text to Prism and
before storing the value used by the `R` repeat hotkey. Therefore repeat speaks
exactly what the player originally heard.

Long handcrafted story labels, cutscene descriptions, and opening-movie audio
are not machine translated in this first test branch. They remain available in
English through the per-key fallback instead of being suppressed. The catalog
format and an external `Languages/<code>.json` override allow reviewed
translations to be added without changing runtime code. The existing English
opening audio track is explicitly logged as an English fallback in a
non-English session.

## Language-independent UI recognition

English text comparisons that merely recognize a screen are replaced with
native state, cursor coordinates, window geometry, row count, or menu context.
When text is genuinely the data being communicated, the decoded localized text
is spoken as-is.

The initial conversion includes title/name entry, quit confirmation, save/load,
party formation, status/configuration, and x64 system-menu recognition. A
screen is not announced from geometry alone unless the surrounding module and
menu state make that geometry unique. This preserves the existing rule that a
false announcement is worse than silence.

## Configuration and overrides

`AccessibilityConfig.GameLanguage` defaults to `auto`. Accepted values are
case-insensitive `auto`, `en`, `fr`, `de`, `es`, `ja`, and the Bunio Polish fan
translation profile `pl`, plus the matching language names. The Polish profile
retains English game-asset paths and English generated-message behavior while
decoding the translation's repurposed one-byte font slots as Polish characters.
Automatic detection recognizes the verified Polish translation font fingerprint;
`pl` remains available as an explicit override for compatible variants.

Bundled catalogs are embedded in the shared assembly so a partial package
cannot silently remove required core translations. An optional
`Languages/<code>.json` beside the mod can override individual keys. Override
files are additive, UTF-8, bounded in size, and validated as a string-to-string
object. Invalid files are ignored with a diagnostic and do not disable the
bundled catalog.

## Failure handling

- Missing selected language data falls through to the next verified detection
  source and ultimately English.
- A malformed localized kernel or field archive disables only that resolver;
  it does not stop Prism or the mod.
- Unknown native bytes are represented consistently rather than deleting the
  rest of a line.
- Missing generated translations use the English value for that key.
- Unknown speech text is passed through unchanged so native localized content
  is never accidentally converted back to English.
- Language choice and fallback reasons are written once to both runtime logs.

## Verification and test release

Automated tests cover all detection sources and precedence, missing-data
fallback, each field archive mapping, European and Japanese decoding, all
eighteen structural `KERNEL2.BIN` sections, Unicode normalization, exact and
formatted localization, English per-key fallback, override validation, and
language-neutral recognition of the affected menus.

Parity tests require x86 and x64 to resolve the same language and localize the
same generated message. Full verification builds and runs the shared, x86,
x64, and parity suites and checks the portable package for all embedded
catalogs and notices.

This change is pushed to a test branch. It does not publish a release or update
the mod manager until live testing confirms the selected language, native
dialogue/menu text, battle text, save/load screens, navigation, and `R` repeat
in both runtimes.
