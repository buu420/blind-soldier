# Russian FFRTT / 7th Heaven support

The requested translation is [ayumosa's 7th Heaven package](https://github.com/ayumosa/ff7-7h-russian), release 1.02. Its IRO SHA-256 is `70ED814EC7A287D29E913670D82CAF6B557C3ED5EE8D73067B130FD48A0746AA`; its `flevel.lgp` SHA-256 is `E3AEAF27B0D2F2AA3636802EE0CC063B1040B055F2292F461AB57C8020F9EBD5`. The IRO, game assets, decoded dialogue and private research remain outside the repository and packages.

## Text and active translation

FFRTT replaces glyph slots in the English engine's font. It is not CP1251. The `ru` descriptor retains English engine/file layout and uses its own text encoding profile. The map was read from `menu_us.lgp` font textures and checked against translated native Hext and kernel strings, including `Сефирот`, `Магия`, `Призыв`, `Эфир`, `Зелье` and `Лечить3`. Shared Latin/Cyrillic shapes are normalized in words; numeric spell suffixes and numeric values remain numbers. Native page, choice and button controls retain their meaning.

Ghidra's `006E706D` renderer reads the glyph byte, addresses 21 columns at 24 pixels per cell, and splits its texture at x=256 between font objects `00DC100C` and `00DC1010`. It also aliases bytes D2/D3 to glyphs 84/85 (Щ/Ы in the replacement font); these occur in ordinary Russian field dialogue. The translation shares 3/4 glyph shapes with З/Ч in words; standalone numbers and spell-level suffixes remain digits. This establishes the actual slot mapping, rather than assuming a Windows code page.

Automatic legacy detection requires the enabled patch's loaded `Магия` string at `0091A8D4` and `Сефирот` at `00922430`, matching twice in one observation. It also requires the supported English legacy executable and active FFNx. Merely downloading or enabling an unrelated archive does not select Russian. Explicit `GameLanguage: "ru"` takes precedence. These legacy addresses are not probed by the native x64 language detector.

7H leaves the original English archive physically on disk while loading replacement data. The current-field text resolver therefore reads the checked, loaded message table for Russian NPC headings and choice pages. Unreadable or changing loaded text cannot fall back to an unrelated English table. The known original English archive is decoded as English for other-field fallback data.

## Mod speech and descriptions

Russian own-speech catalogs contain the base interface and additional generated runtime speech. Composed settings/navigation output uses a separate, bounded translation path. Short structural templates are kept out of ordinary native-dialogue matching. Description-only translations do not rewrite native dialogue or production-credit rows.

Four known generated wrappers around native choices are also localized: opening position/count, disabled status, and Chocobo Ranch availability. The native option text remains separate from the general mod-speech composition rules.

The embedded catalogs contain 152 base prompts, 4,509 additional runtime prompts, 44 trusted composition entries, and all 1,615 description texts. Generated text is localized at the x86 and x64 speech boundaries; native field/world dialogue, tutorial text and production credits retain their separate path. Russian-only template matching protects abbreviations such as `Mt. Nibel` and native `Критич. удар`, and each template match has a time limit. The named controls and numbers remain part of the readout.

The exact 70-cell naming grid follows the patch's `Naming3` table at `00921D70`. Its alphabet cells distinguish uppercase/lowercase Russian letters, including the shared З/3 and Ч/4 glyphs; its numeric row still reads digits. A saved isolated shared glyph is reported as ambiguous rather than inventing a distinction the game does not store. Loaded message pages resolve canonical character tokens from actual saved names. NPC identity evidence keeps the canonical role separate, so a renamed party reply cannot label an unrelated NPC.

Russian descriptions use Prism text, including the opening, films, field descriptions, battle descriptions and ending/credits cues. Packaged English recordings are bypassed. The screen reader controls Russian voice, speed and volume. Existing save-linked description history is preserved; use the mod menu's reset actions to hear descriptions again.

## Exact description triggers

The Russian archive has 946 English-catalog field-description anchors: 21 have stock-identical script prefixes and 925 are authorized by exact reviewed localized identities. No Russian cue is omitted. The full combined manifest has 3,563 identities and 5,457 authorized rows; the pre-existing Polish exclusion remains separate.

`crater_1` (700) needed explicit review. Its exact Russian prefix is `2AEFA4D8984F70EC75FD5D5131E3E896AC30A08DDED69E2752CD324B4BC3336B`. Entity 1, slot 0 starts with the unchanged `4300 F000 F601` room-entry/fade sequence. The Russian script removes the later English party-mask setup, so the whole-slot similarity threshold cannot authorize the area-entry cue automatically. The camera, models, palette, walkmesh, encounter and trigger sections are byte-identical. Its background contains the complete identical English background followed only by a 28-byte footer, `00000000000000454E4446494E414C2046414E544153593741535937`; the unused tile-map section also differs. The explicit review authorizes only that exact prefix and its unconditional MPNAM entry. General whole-slot alignment remains unchanged.

## Navigation and native activities

All 702 reviewed navigation fields are accounted for: 34 retain the English script prefix, 607 changed prefixes pass semantic equivalence, and 61 more have explicit structural review. No audited Russian navigation field is excluded. The current loaded Russian script is parsed against only the exact audited English disk prefix/model-loader hash or its own physical Russian archive. Three late-Crater fields require explicit entity maps. `las0_8` (751) retains entity numbers but moves its ladder LINEs, so its Story/Object approaches are rebased to the actual loaded segments.

Cross-field planning has separate transfer/arrival proof for 56 structurally reviewed fields. Five fields (732, 747, 749, 751, 757) have genuine route differences: their original English future metadata is refused under the Russian overlay. Current loaded navigation in those fields remains available. Unknown or edited prefixes, wrong-field snapshots and unaudited disk metadata remain refused.

Ancient Forest's native puzzle state, entity layout, treasure bits and plant timer anchors are unchanged. Its Russian reset acts on Switch press rather than release, and guidance follows that difference. Existing authorized extended plant timing is retained. Junon's chair script removes a WAIT before its input loop; the readout now verifies the stock and Russian loop anchors independently, including their actual loop boundaries.

## User setup

Install and enable the linked Russian translation using its upstream instructions. Its font must take priority over incompatible UI/font replacements. Leave Blind Soldier's `GameLanguage` set to `auto`; `ru` is available as an explicit override. Select a Russian voice in the screen reader. If combining the translation with an English voice-over, enable the mod's voiced-dialogue reading setting when Russian written dialogue should also be read.

This is a local test change based on 1.0.4. The older 7H IRO does not establish native Steam 2026 translation compatibility merely because shared code builds for x64. Native-data/static checks and local deployment do not establish Russian live gameplay, audible voice quality or controller listening results.

Research receipts are under `C:\Users\buu42\Dropbox\FFVII\FFVII-ActiveBuild\russian-20261127`. Large private inputs and disposable test output are under `D:\BlindSoldier-russian-20261127`.

## Verification

The shared layout suite, full x86 suite, full x64 suite and x64 module suite passed with installed English/official-language/Echo-S/Polish/Russian data fixtures. The final disabled-option wrapper was added after that full run and is checked by a failing-then-passing regression in the Russian speech suite on both hosts. Final ReadyToRun payloads are rechecked by the Russian settings, composition, native-output, naming, all-702-field navigation, description-catalog and localized-cue suites; the x64 output/module checks and x86 NPC-name checks are included. The final payload hashes are compared against those exact test hosts before local deployment and ZIP verification.

These are offline/native-data and instrumented speech-output checks. Native keyboard-overlay installation is not exercised in the unit-test host. No live Russian game, Russian listener or physical-controller session is claimed. Translation terminology and description fidelity were reviewed against the patch; further Russian-player feedback remains useful for fluency and pronunciation.
