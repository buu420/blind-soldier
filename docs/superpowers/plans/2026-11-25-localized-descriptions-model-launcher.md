# Localized descriptions, Midgar model and legacy launcher implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement and verify the parent tasks. Claude owns the independent legacy launcher task.

**Goal:** Speak every non-English audio description through Prism, restore the native final Midgar-model interaction, and provide accessible standalone 2013 launching.

**Architecture:** Keep English audio and native cue/history identities intact. Package offline text translations separately, select by the existing detected language code (including Polish), and bypass every English recording output for other languages. Base puzzle guidance on native availability and empty-slot state. Reuse verified accessible launcher controls with an explicit legacy installation/launch contract.

**Tech Stack:** .NET 8 C#, Prism, existing WinForms launcher, native field-script audit and Ghidra, PowerShell packaging, Python tooling for offline translation catalogs.

**Spec:** User request of November 25, 2026 in the current Blind Soldier chat.

## Global constraints

- Preserve English recordings, saves, settings, histories, native interactions, other mods and the existing 7th Heaven/ReShade loader repair.
- Source and records in Dropbox FFVII; large scratch/build/model files on D:.
- No repeated approval questions for authorized bug fixes. Verify closed processes before local game-tree writes.
- Offline/package proof does not establish in-game speech, physical-controller or listener proof.

## Review focus

- Polish uses the English enum with code pl: choose voice by code, not enum.
- Movies, opening, summons, limits, credits and post-credits must bypass English tracks as well as field cues.
- Native dialogue and displayed credits remain untouched by description-only translation.
- A model piece can be placed in any empty slot; carry identity is not a slot prerequisite.
- Standalone 2013 and nested legacy/7th Heaven paths differ; identify actual host before installing.

## Tasks

- [ ] Add failing language/output regressions; export an exhaustive English description inventory from real catalogs and sidecars.
- [ ] Implement and package a bounded, offline description localizer and translated catalogs; verify complete inventory coverage and representative translations. Record generation provenance and limits.
- [ ] Route all description channels in both runtimes through the language policy; preserve retries, timings, enabled settings and histories. Verify English behavior and non-English recording bypass.
- [ ] Reproduce the model-room missing target with native slot/carry states, correct availability, verify stock and all five Echo-S scripts plus actual walkmesh targets.
- [ ] Integrate Claude's legacy-launcher changes after independent review and focused tests, inspect actual UI accessibility and launch arguments without altering saves.
- [ ] Run both full runtime suites and shared/package gates, inspect final archives, deploy with backups/protected-file proof, and publish the authorized completed update using established conventions.

## Evidence ledger

- Ruling: use explicit description-only localization, including the on-demand room-status append. A red Shared test proved that merging description translations into general speech rewrote a native dialogue line that also appeared in a movie catalog. The corrected Shared suite passes; native dialogue and credits rows retain the ordinary speech path.

- Downloads ff7_accessibility_reloaded (3).log: final model-room Story is none at 00:55:23, 00:55:58 and 01:01:01 UTC November 24, after the outer room offered Carry the Midgar part into the model room. Native interaction eventually succeeds at 01:01:46.
- Native stock and all five reviewed Echo-S field 248 PARTE set bank 1 address 68 bit 7 and address 57 bit 2. Field 249 CITYA-E accept any carried piece and clear all carry bits after placing it, setting their own slot bit in bank 3 address 180.
- Existing core UI translations contain 152 strings per language and zero of the 1,515 recorded-description texts. English track factories currently have no language guard.
- Official localized script navigation identities deliberately do not authorize English action byte offsets; preserve that safety boundary unless separately aligned against native scripts.
