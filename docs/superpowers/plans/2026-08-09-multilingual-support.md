# Multilingual Accessibility Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make both Blind Soldier runtimes automatically use Final Fantasy VII's selected language for native text and core accessibility-generated speech, with per-message English fallback.

**Architecture:** Establish one shared language context at startup, select localized game assets through that context, decode field and kernel text with explicit language-aware codecs, and localize generated speech at the two final Prism output boundaries. Replace English-only UI recognition gates with native state or stable menu context.

**Tech Stack:** C# 12, .NET 8, Prism UTF-8 speech, Reloaded-II x86/x64, JSON embedded resources, PowerShell, Ghidra 12, FF7Tools reference tables.

## Global constraints

- Support `en`, `fr`, `de`, `es`, and `ja` in both runtimes.
- Never machine-translate native game text or infer a language from one visible
  phrase.
- Preserve speech on missing translations by using English per message.
- Do not use OCR or modify FFVII, FFNx, 7th Heaven, or Steam configuration.
- Preserve public decoder overloads during migration; explicit-language
  overloads are authoritative for new code.
- Test before each implementation slice and observe the intended failure.
- Push only the test branch; do not tag a release or update the mod manager.

---

### Task 1: Add the shared language context and detector

**Files:**
- Create: `Ff7.Accessibility.LegacyLayout/Ff7GameLanguage.cs`
- Create: `Ff7.Accessibility.LegacyLayout/Ff7GameLanguageDetector.cs`
- Modify: `Ff7.Accessibility.Core/AccessibilityConfig.cs`
- Test: `Ff7.Accessibility.Shared.Tests/GameLanguageDetectorTests.cs`
- Modify: `Ff7.Accessibility.Shared.Tests/Program.cs`

- [ ] Write failing tests for override precedence, executable suffixes, Steam
  language names, single-directory detection, missing-data rejection, invalid
  override continuation, English fallback, and all five archive/path mappings.
- [ ] Add `GameLanguage = "auto"` to configuration.
- [ ] Implement immutable language descriptors and a detector whose filesystem,
  process path, and manifest inputs can be supplied by tests.
- [ ] Require `kernel/kernel2.bin` before accepting a language source and log
  one deterministic selection/fallback reason.
- [ ] Run `dotnet run --project Ff7.Accessibility.Shared.Tests` and require all
  detector tests to pass.
- [ ] Commit as `feat: detect the active FF7 language`.

### Task 2: Decode localized field and kernel text

**Files:**
- Modify: `Ff7.Accessibility.LegacyLayout/Ff7EncodedTextDecoder.cs`
- Create: `Ff7.Accessibility.LegacyLayout/Ff7TextEncoding.cs`
- Modify: `Ff7.Accessibility.LegacyLayout/FflevelDataSource.cs`
- Modify: `Ff7.Accessibility.LegacyLayout/FflevelFieldTextResolver.cs`
- Modify: `Ff7.Accessibility.LegacyLayout/Kernel2ItemNameResolver.cs`
- Modify: `Ff7.Accessibility.LegacyLayout/Kernel2TextDatabase.cs`
- Modify: `Ff7.Accessibility.LegacyLayout/Ff7.Accessibility.LegacyLayout.csproj`
- Create: `docs/third-party/ff7tools-notice.md`
- Test: `Ff7.Accessibility.Shared.Tests/LocalizedTextDecoderTests.cs`
- Test: `Ff7.Accessibility.Shared.Tests/LocalizedKernel2Tests.cs`

- [ ] Write failing decoder tests using fixed byte vectors for English,
  accented French/German/Spanish, Japanese kana, Japanese multibyte kanji,
  page/line controls, and unknown-byte preservation.
- [ ] Port the complete field/kernel Western and Japanese tables from FF7Tools,
  retaining its copyright and permission notice.
- [ ] Add explicit `DecodeField*` and `DecodeKernel*` entry points; retain legacy
  methods as English-field compatibility wrappers until all call sites migrate.
- [ ] Write failing data-source tests proving each locale chooses the matching
  field archive and never silently substitutes another non-English archive.
- [ ] Write failing structural-kernel tests for all 18 section indices, localized
  item/ability/equipment/materia/battle names, malformed offsets, and counts.
- [ ] Replace English-signature section discovery with structural section-index
  parsing and explicit counts.
- [ ] Run the shared suite and commit as `feat: decode localized FF7 data`.

### Task 3: Add generated-speech catalogs and overrides

**Files:**
- Create: `Ff7.Accessibility.LegacyLayout/BlindSoldierLocalizer.cs`
- Create: `Ff7.Accessibility.LegacyLayout/Localization/en.json`
- Create: `Ff7.Accessibility.LegacyLayout/Localization/fr.json`
- Create: `Ff7.Accessibility.LegacyLayout/Localization/de.json`
- Create: `Ff7.Accessibility.LegacyLayout/Localization/es.json`
- Create: `Ff7.Accessibility.LegacyLayout/Localization/ja.json`
- Modify: `Ff7.Accessibility.LegacyLayout/Ff7.Accessibility.LegacyLayout.csproj`
- Test: `Ff7.Accessibility.Shared.Tests/BlindSoldierLocalizerTests.cs`

- [ ] Inventory exact and composite generated speech in navigation, battle,
  save/load, menu, system-menu, timer, and minigame code.
- [ ] Write failing tests for exact phrases, typed templates, format arguments,
  five languages, missing-key English fallback, unknown native-text pass-through,
  invalid/oversized override rejection, and valid per-key overrides.
- [ ] Implement embedded UTF-8 catalogs and bounded additive external override
  loading from `Languages/<code>.json`.
- [ ] Fill the non-English core catalogs with reviewed translations for the
  interactive surface listed in the design; retain English fallback for long
  story and cutscene prose.
- [ ] Run the shared suite and commit as `feat: localize accessibility speech`.

### Task 4: Wire one language through x86 and x64 speech/data paths

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchSession.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchAccessibilityOutput.cs`
- Modify: localized data-reader construction call sites in both runtimes
- Test: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Test: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026ResearchAccessibilityOutputTests.cs`
- Test: `Ff7.Accessibility.Parity.Tests/Program.cs`

- [ ] Write failing x86 and x64 endpoint tests proving localization occurs
  before Prism output and before `R` repeat storage.
- [ ] Detect once after configuration/game-root discovery, then pass that
  context into kernel, field, save, menu, and native text readers.
- [ ] Localize generated speech at each final output endpoint without changing
  unmatched decoded native text.
- [ ] Log selected language, detection source, English catalog fallback, and
  non-English use of the existing English opening-description audio.
- [ ] Add parity tests requiring both runtimes to emit identical localized
  navigation, battle-status, save, and menu messages.
- [ ] Run the Reloaded, x64, and parity suites and commit as
  `feat: use localized speech in both runtimes`.

### Task 5: Remove English-only UI gates and preserve Unicode

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/FieldDialogueDrawSpeechTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/MenuTextRenderDiagnostics.cs`
- Modify: `Ff7.Accessibility.Reloaded/StaticMenuCursorSpeechTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/PartyFormationSpeechTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/StatusMenuSpeechTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/TitleLoadMenuSpeechTracker.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Menus/Steam2026InGameMenuSpeechBridge.cs`
- Modify other exact-English state gates identified by `rg`
- Test: relevant Reloaded and x64 tracker tests

- [ ] Write failing tests showing accented and Japanese rendered text survives
  normalization.
- [ ] Write failing menu tests using non-English labels for quit, name entry,
  save/load, party formation, status/config, and x64 escape/system screens.
- [ ] Replace recognition text with module/menu state, cursor coordinates,
  geometry, or exact localized semantic sets only where state is insufficient.
- [ ] Retain decoded visible text as the content spoken to the player.
- [ ] Search for remaining English equality/contains gates, document justified
  content comparisons, and run affected suites.
- [ ] Commit as `fix: recognize menus independently of language`.

### Task 6: Documentation, packaging, and verification

**Files:**
- Modify: `README.md`
- Modify if required: `Build-DualRuntimePackage.ps1`
- Modify if required: package verification scripts

- [ ] Document automatic language selection, supported languages,
  `GameLanguage`, external override location, and the English fallback for
  untranslated narrative/audio descriptions.
- [ ] Verify embedded catalogs and FF7Tools notice are present in both packaged
  runtimes without requiring loose JSON files.
- [ ] Run `git diff --check`.
- [ ] Run the shared, Reloaded, x64, and parity test projects from a clean build.
- [ ] Run the repository's dual-runtime/package verification scripts and record
  exact results; do not launch FFVII.
- [ ] Inspect `git status`, commit remaining documentation/package changes as
  `docs: explain multilingual support`, and ensure the branch contains only the
  intended commits.

### Task 7: Push for live testing

- [ ] Push `agent/multilingual-support` to `origin`.
- [ ] Open a draft pull request to `main` summarizing supported languages,
  automatic detection, fallback boundaries, and verification results.
- [ ] Provide the branch/PR link and a compact live-test matrix covering one
  European language and Japanese in x86 and x64, without publishing a release
  or updating the mod manager.
