# Description language delivery

The game language detector already distinguishes English, French, German,
Spanish, Japanese and the Bunio Polish profile. Polish shares the English enum
and asset directory, so recording selection must use the language code.

`BlindSoldierLocalizer.UseRecordedAudioDescriptions` selects the existing English
recordings only for `en`. Other codes load packaged description text resources
and pass the resulting Unicode text through Prism. `LocalizeDescription` is called
explicitly at description delivery and inside the appended room-status readout.
Generic `Localize` keeps its interface catalog: a line such as `Cloud: Lifestream.`
also appears in movie sidecars and must not be translated merely because it was
captured as native dialogue. Native dialogue and credits
names remain native text; canonical cue keys, recording names and history keys
remain English identities. No runtime network request or translated voice clone
is involved. A screen reader's configured synthesizer supplies its pronunciation
and volume; the recording-volume option continues to apply to English clips.

The x86 host also supplies whether FFNx is actually loaded to the language
detector. In the English engine's multilingual `workingdir` mode, the validated
`game_language` integer (1-4) wins over the executable suffix. This follows
[FFNx's multilingual selection](https://github.com/julianxhokaxhiu/FFNx/blob/0adf0b3/src/common.cpp)
and its [configuration contract](https://github.com/julianxhokaxhiu/FFNx/blob/0adf0b3/misc/FFNx.toml).
An inactive driver, original 2013 layout, auto/unsupported values, duplicate keys,
or absent localized kernel do not authorize an override. Explicit mod language
selection wins; Polish fingerprints retain precedence within active English data.

The inventory exported by `--export-description-texts PATH` combines the actual
field/action/area catalogs, every recorded-description manifest entry, opening
schedule, movie paragraphs and timed sidecars, battle cues and credits
introduction. It contains 1,615 distinct texts. Both runtime test programs compare
every inventory text against every non-English embedded catalog. This catches a
future unlocalized cue, missing resource or accidental English-only fallback.

The output guards cover field clips, independent film tracks, opening playback,
battle/Limit/summon narration, endgame films and credits introductions. Opening
description defaults still work in other languages: enabling the recorded-opening
option requests the screen-reader schedule there. Turning both opening options
off remains off. Missing/refused speech stays retryable through the existing
delivery queues; history is not reset merely because a language changes.

Offline text generation uses [Argos Translate's published models](https://github.com/argosopentech/argospm-index)
with the [CTranslate2 Python interface](https://opennmt.net/CTranslate2/quickstart.html).
The initial draft generator is a development tool under
`tools/AudioDescriptionLocalization`; it refuses to overwrite the curated source
catalogs. Regeneration produces review drafts, not the corrected shipping text.
Models and its Python environment are not shipped. Model URLs and SHA256s are
retained with the generation receipt. The English-Polish model credits Jörg
Tiedemann and Santhosh Thottingal, OPUS-MT (EAMT 2020), CC BY 4.0; the retained
model READMEs provide each package's attribution. Text generation is followed by
the separately recorded translation review. Automated coverage, semantic review
and native-speaker listening are distinct evidence.

The native localized-script anchor audit is documented separately in
`localized-description-triggers-20261125.md`. A known navigation layout alone
must never authorize English action byte offsets on an unrelated translation.
