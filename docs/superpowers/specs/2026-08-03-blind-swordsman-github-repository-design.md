# Blind Swordsman GitHub Repository Design

## Goal

Publish the existing Final Fantasy VII accessibility mod as a clean, private
GitHub repository named `blind-swordsman`, with an accurate user-facing README
and enough checked-in inputs for a fresh clone to build without depending on
the current workstation's parent directories.

## Repository identity

- The product name is **Blind Swordsman**.
- The stable Reloaded-II mod ID remains `ff7.accessibility.reloaded` so existing
  installations and profiles continue to work.
- The initial GitHub repository is private. Visibility can be changed after the
  source, third-party asset permissions, and release package have received a
  final review.
- The default branch is `main`.

## Published contents

The existing `reloaded` directory becomes the repository root. Source projects,
tests, installer scripts, required runtime assets, and focused research notes
are tracked. Build output, local logs, downloaded reversing tools, Ghidra
databases, and machine-local artifacts are ignored.

Three build inputs that currently live outside the repository are copied into
stable in-repository locations:

- the dual-runtime parity matrix;
- Kujata's field-to-world-map coordinate metadata;
- Kujata's world-map menu-name metadata.

Build, installer, launcher, and test references are updated to those local
copies. No FFVII executable, game archive, save file, or user credential is
published.

## README design

The root README is written for a blind player rather than for a developer. It
contains:

1. a short explanation of what Blind Swordsman provides;
2. supported Windows/FFVII launch paths and prerequisites;
3. current source-install instructions and the future release-package path;
4. every mod-specific key, including motorcycle auto-steering and navigation
   progress controls;
5. a detailed navigation guide for fields and the world map;
6. progress-indicator behavior, with a strong recommendation to leave it on
   and exact instructions for disabling or changing it;
7. concise troubleshooting and useful bug-report information;
8. project status, credits, and a non-affiliation notice.

The README does not attempt to provide a gameplay walkthrough. Strategy and
story progression remain the job of an external guide and the mod's own story,
object, NPC, and navigation information.

## Navigation explanation

The documentation uses the actual shared control scheme:

- `U` and `O`: previous and next category;
- `J` and `L`: previous and next target;
- `K`: repeat the selection or active-route status;
- `I`: start or stop navigation;
- `F5`: toggle the native accessible route-progress indicator;
- `F6` and `F7`: choose the previous or next 5, 10, 15, or 20 percent interval;
- `F8`: toggle motorcycle auto-steering while the motorcycle minigame is active.

Field categories are Exits, Story, NPCs, and Objects. World-map categories are
Locations, Story, Transportation, Events, and Chocobo Tracks. Documentation
explains reachable-target filtering, native walkmesh routing, straight-run
grouping, live position and elevation tracking, off-route correction, arrival,
combat resumption, and progress moving backward during genuine backtracking.

## Verification

- Review the README against constants and behavior in the shared x86/x64 code.
- Confirm ignored files exclude generated and machine-local data without
  excluding required source or assets.
- Run the relevant shared, x86, x64, package, installer, and verification tests.
- Inspect the exact staged file list and scan it for credentials before commit.
- Create the private GitHub repository, push `main`, then verify its visibility,
  default branch, description, and remote commit.
