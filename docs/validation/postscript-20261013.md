# Postscript 1.78 native compatibility evidence

Research completed October 13, 2026 for Blind Soldier 0.9.9. The author archive
SHA-256 is `85E87A9AA57AAEEE9D191378EEACC494E9E73C3B7511F2C5AA0AE01D224A7F5A`.
It contains 102 Hext files, 905 literal writes and 42 actionable configuration
options. The reference legacy engine SHA-256 is
`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`.

## Native evidence

Ghidra 12.1.2 read-only analysis covered equipment selection and comparison,
Materia links, battle command routing, PHS and Limit renderers, assistance text
draws, KERNEL2 loading, character-role mapping, field comparisons and randomized
pickup grant/message routines. These are native pseudocode and disassembly,
not recovered original source. Private author code and licensed field/driver
assets remain outside this repository and its packages.

- The second accessory occupies character offset `+0x1B`; the original remains
  at `+0x1E`. The native equipment screen exposes category 3.
- Battle commands `0x1D` and `0x1E` reuse renderers 6 and 4. Readers verify the
  route, command/mode, roster, learned slots, cursor and gauge bookends rather
  than interpreting them as stock Magic or Enemy Skill.
- Assistance observes completed B text draws or renderer `0x17`'s formatted
  help pointer. No hidden weakness, loot or enemy HP records are queried.
- FFNx 1.24.3 replaces the original `0x419457` lookup and owns heap sections.
  PDB/Ghidra verified getter/allocator/reset code and section RVAs. Relocated
  function hashes, exact installed DLL identity, committed regions and selected
  string/owner bookends gate reads. Supported code can bind before its getter
  hook is installed, remaining unreadable until it acquires native ownership.
- Character Order's CHAR mapping changes role ownership while fixed HRC models
  remain bound to their original entities. Only affected byte equality operands
  for party slots are mapped; raw inequality/word conditions remain native.
  A private pure RNG replay was compared with 63 independently executed original
  instruction vectors. Production never calls or writes the game's RNG.
- Randomized pickup grants and received-message handlers change reward IDs after
  interaction. Static field pickup IDs cannot be announced as future rewards.
- MateriaDrops extends inventory reward IDs from `0x140` through `0x19F`.
  Its checked name route subtracts `0x140` for the native Materia table. The
  alias mapping and results acceptance retain stock bounds without the patch.
- The changed opening script has an exact hash/entity count and five checked
  cue mappings. Unrelated loaded scripts and nearby positions remain rejected.
- None of the 905 writes intersected the 24 audited accessibility hook entry or
  prologue spans. Changed routine semantics still require the checked readers.

## Regression coverage and release checks

The Postscript suite covers both accessory rows, displayed equipment totals,
socket groups, PHS and Limit selections/availability, native command labels,
completed assistance draws, owner/target changes, loaded text overrides,
FFNx relocation and delayed getter ownership, remapped navigation conditions,
dialogue role/party tokens, opening identities and generic randomized pickups.
Materia reward regressions include the first/last alias, native loaded names,
experience/AP/gil preservation, and absent, unknown or changing patch cohorts.
It runs in the normal legacy test suite as well as `--postscript-only`.

Licensed optional fixtures enable full Postscript code-cohort and FFNx DLL
integration. Fixture-free cases always run in CI, including native RNG vectors,
behavior/coherence negatives and backend preference. Full private fixture runs
are retained in the local release evidence.

The release workflow verifies both runtime builds, the ordinary stock suites,
game-data integration against stock and five Echo-S variants, loader contracts,
Ghidra evidence, exact packaged assemblies, checksums, deployment, preservation
and manager/catalog publication. Final results are retained in the private
`release-0.9.9-20261013` evidence directory; only completed checks may be claimed
in the handoff and public completion report.

Live Prism speech, keyboard/controller play, assist-box toggles, randomized
receipts, PHS/Limit choices and a full playthrough of random seeds have not been
verified by this offline work. 7th Heaven and native Steam launch/playback tests
remain separate from source and package validation.
