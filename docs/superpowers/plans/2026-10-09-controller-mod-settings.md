# Controller and Mod Settings Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans task by task. Steps use checkbox syntax for tracking.

**Goal:** Deliver the confirmed controller scheme, spoken settings, replay reset
and live narration volume for legacy x86 and Steam x64.

**Architecture:** Keep input decisions in Core and capture them within existing
XInput/SDL getters. A shared settings catalogue/store/menu and narration gain
provider serve both hosts; runtime workers perform speech and history changes.

**Tech Stack:** C#/.NET 8, Reloaded hooks, XInput, SDL2, Prism, NAudio/Vorbis.

**Spec:** ../../plans/2026-10-09-controller-mod-settings-spec.md

## Global Constraints

- Both supported FFVII runtimes; no synthetic Confirm, attack or hidden state.
- Preserve config.json, per-save histories, narration and game saves.
- Controller commands and their release tails stay out of native game input.
- Live recorded-description level 50-300 percent, step 25, default 100.
- All existing player toggles listed; announce restart-only settings explicitly.
- Publish only after checks/deployment, using Author CLI 0.30.0.

## Review Focus

- Releasing the trigger before R3 must not toggle Battle Assist.
- Changing domain/device/focus must not convert held input into a fresh action.
- Reset during narration must not repopulate history or restart a current cast.
- A startup-only setting must not claim a live effect; menu must remain audible.
- Updates must preserve player overrides and both description histories.

### Task 1: Modifier navigation and party/settings input

Files: Core/GamepadInput.cs, ControllerNavigationCapture.cs,
ControllerNavigationMenu.cs (policy interface), new ControllerAccessibilityMenu.cs;
Reloaded/XInputCaptureHook.cs; Steam2026X64/Runtime/Input/Steam2026SdlControllerCaptureHook.cs.

- [x] Write failing raw-input tests for trigger navigation, party commands,
  settings chord, stale/busy R3 tails, axes, reconnect and multiple devices.
- [x] Implement policy and input-hook adapters; retain the old modal policy only
  for compatibility with existing adapter tests, never install it in runtime.
- [x] Run new tests and existing input/ownership suites on both architectures.

### Task 2: Settings, narration gain and replay reset

Claude owns new Core settings catalogue/store/menu/gain files,
AccessibilityConfig.cs, FieldAreaDescriptionHistory.cs and narration player/runtime
files plus their dedicated tests. Root owns runtime integration and installer.
Contract: ModSettingsMenu exposes IsOpen, Open, Close, Handle; commands Previous,
Next, Decrease, Increase, Activate and Repeat return spoken feedback. It accepts
config/store and a battle-reset callback; volume is read live from config.

- [x] Write and run failing tests for overrides, restart wording, self-audible
  settings, current-save reset and in-flight reset, live limiter/gain.
- [x] Implement catalogue/store/menu and actual narration provider/history reset.
- [x] Run dedicated tests and return code ownership with evidence.

### Task 3: Host integration and protection

Files: Reloaded/Mod.cs; Steam2026X64/Mod.cs and Runtime/Steam2026ResearchSession.cs;
controller dispatch/services and installer FF7SteamInstall.psm1.

- [x] Load overrides before creating runtime services; wire settings input/speech,
  pause auto walk while browsing, and guard competing mod hotkeys.
- [x] Wire native party readouts and settings capture context on both hosts.
- [ ] Persist existing progress/highway hotkeys; preserve override/history files
  through installer updates with an update regression.
- [ ] Run host integration and packaged suites; address parity defects affecting
  listed switches and clearly mark startup-only options.

### Task 4: Review, release and deployment

- [ ] Fresh independent review; repair actionable findings and rerun affected tests.
- [ ] Update controls/docs/version, run the established dual-runtime release gates.
- [ ] Deploy and compare protected hashes, publish assets/catalogs using new CLI,
  verify all completed phases/public hashes and retain a current handoff.
- [ ] Report exact tested coverage and unperformed live gameplay/hardware checks.
