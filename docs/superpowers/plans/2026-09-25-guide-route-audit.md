# Guide and Route Verification Implementation Plan

> **For agentic workers:** Execute this plan in the current task using the existing Claude teammate for coding/review and the primary agent for independent auditing. Do not create additional agents.

**Goal:** Audit the walkthrough from beginning to ending and correct confirmed navigation and interaction gaps on both supported runtimes.

**Architecture:** Reuse native archive/script readers and the shipping planner; add an independent arrival-by-arrival audit rather than weakening existing checks. A guide ledger ties chapters and optional routes to native evidence. Focused state and interaction tests validate fixes that static geometry cannot prove.

**Tech Stack:** C#/.NET 8, PowerShell, Python for private evidence processing, native FFVII field archives, existing Ghidra exports and both mod runtime assemblies.

**Spec:** `docs/superpowers/specs/2026-09-25-guide-route-audit-design.md`

## Global constraints

- Preserve published 0.7.0, game executables, save files, narration and user configuration.
- Use native game state; no guessed labels, coordinates or bypassed locks.
- Validate both legacy x86/7th Heaven and Steam 2026 x64.
- Keep raw guide and extracted native content private.
- Separate catalog presence, static geometry, state-backed behavior and live verification.

## Review focus

- A target reached from one entrance but disconnected from another must remain a partial failure.
- A nearby endpoint outside the actual interaction area must not count as arrival.
- A required detour through another field must not be erased by same-field reachability assumptions.
- Conditional and moving targets must retain their unavailable or unknown states.
- Both production coordinators must consume any repaired shared behavior.

### 1. Establish the independent guide checklist

**Files:** private `guide-route-audit-20260925/guide/`; public `docs/guide-route-audit-20260925.md` and an original chapter ledger under `tools/GuideRouteAudit/`.

- [x] Read every walkthrough chapter through the ending, including the optional chapters.
- [x] Record required transitions, talk/activation steps, item approaches and alternate routes with chapter URLs.
- [x] Map them to native fields and existing Story/Object/NPC definitions; investigate every unmatched entry.
- [x] Record review status explicitly so merely retrieving a page cannot count as review.

### 2. Build and run the route matrix

**Files:** `tools/GuideRouteAudit/GuideRouteAudit.csproj`, `Program.cs`, `RouteAssessment.cs` and focused classification tests.

**Interfaces:** consume a licensed archive root and a specified shipping runtime assembly; produce JSON retaining each native arrival, target-position witness, route result, final distance and planner diagnostic.

- [x] Add failing classification tests for mixed successful/failed arrivals, out-of-range endpoints and unknown target positions.
- [x] Implement assessments that preserve each failed or unverified case instead of accepting one successful approach.
- [x] Collect native arrivals from gateways and MAPJUMP; distinguish constant positions from unresolved script values.
- [x] Run across every readable native field, retaining geometric versus conditional/script-assisted results separately.
- [x] Review failures against the guide and native scripts; add state-backed route cases for confirmed gaps.

### 3. Repair reported and audit-discovered failures

**Files:** shared route/target readers and focused runtime test files identified by each native reproduction; both host registrations where required.

- [x] Reproduce Added Effect and Don Corneo against released 0.7.0 DLLs before editing their behavior.
- [x] Trace the actual route, activation conditions and all relevant native arrival/party branches.
- [x] Implement each confirmed correction with negative cases for locked, hidden, unavailable and lost targets.
- [x] Run the same tests against both runtime assemblies and re-run the broad matrix for changed behavior.
- [x] Investigate newly found failures throughout the guide, including previously reviewed regions.

### 4. Verify and deploy

- [x] Review the final source changes independently, retaining disagreements and their resolution.
- [x] Run both full runtime executable test hosts, shared-layout/parity checks and the guide/Story route audits against fresh outputs.
- [x] Package both runtimes and bind checks to the exact DLL/dependency hashes.
- [x] Back up and deploy verified payload changes to both local installations while preserving narration, configuration and room history.
- [x] Report confirmed fixes, complete chapter coverage, exact verification scope and any remaining live-testing limits.
