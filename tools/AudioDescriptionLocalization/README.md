# Description catalog maintenance

The shipping dictionaries are the six UTF-8 JSON files in
`Ff7.Accessibility.LegacyLayout/AudioDescriptionLocalization`. Their keys are
canonical English descriptions. Non-English values are curated translations;
English recordings and cue/history identities continue to use the original keys.

Run either runtime test runner with `--localized-descriptions-only` to check
every description in the real catalogs, movie sidecars and voice manifests.
The x86 runner's `--export-description-texts PATH` writes that source inventory
for translation work. A new description needs a translation in each catalog.

`generate_catalogs.py` is the initial Argos draft experiment. It requires the
model files and Python packages named in its docstring. It only writes to a
separate scratch directory and refuses the shipping catalog directory. Its
output needs full semantic review: the first drafts confused creatures,
movement and actors and left some English text or glossary markers behind.
Regenerating those drafts does not reproduce the curated release dictionaries.

The game loads only the embedded curated text. It does not invoke this script,
download a model, contact a translation service or synthesize a cloned voice.
See `docs/description-translation-review-20261125.md` for review coverage and
`docs/localized-description-triggers-20261125.md` for native trigger evidence.
