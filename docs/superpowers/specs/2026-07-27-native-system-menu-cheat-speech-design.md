# Native System Menu and Cheat Speech Design

## Goal

Make every interactive part of the Steam 2026 x64 Escape menu speak through
Prism, and announce the visible FFNx status popups used by the legacy x86
runtime. Speech must report the same state a sighted player sees without
changing gameplay or inferring state from input.

## Supported runtimes

- Steam 2026 x64 is enabled only for executable SHA-256
  `57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B`.
- FFNx popup speech is enabled only when the known x86 FFNx 1.24.3 module is
  present and its popup storage identity validates.
- An unsupported or incoherent runtime remains unhooked and logs one bounded
  diagnostic. It never receives guessed speech.

## Scope

The x64 reader covers the native MUI system:

- Escape root: Game Options, Boosts, Exit, and Return to Game.
- Game Options: System and Edit Controls.
- Display and audio settings, including lists, toggles, sliders, and their
  current values.
- Keyboard and controller binding lists, primary and secondary assignments,
  input-capture prompts, defaults, apply actions, conflicts, and validation
  messages.
- Speed Boost, Battle Assist, No Encounters, the Apply action, and the
  achievement/save warning.
- Autosave settings whenever that native scene is presented.
- Exit, restore-default, apply/revert, and other MUI confirmation dialogs.

The x86 reader covers every status message actually submitted through FFNx's
visible popup path, including speed, battle, auto-attack, voice auto-text,
encounter, FMV, and configuration/error notifications. It speaks the exact
visible popup text and does not maintain a second guessed cheat-state model.

## Architecture

### Native MUI capture

`Steam2026SystemMenuCallbackCatalog` stores Ghidra-verified callback RVAs,
calling conventions, and pristine instruction prefixes for the supported x64
build. `Steam2026SystemMenuHookSet` validates the whole callback cohort before
activation. Each detour calls its original exactly once and publishes only a
small immutable observation to a fixed-capacity ingress queue.

`Steam2026SystemMenuObservationReader` turns a captured MUI control or scene
identity into verified state: scene, node identifier, focus, enabled state,
selection index, item count, toggle value, slider value, and key assignment.
Reads use structural bounds and coherent bookend checks. A failed value read
may produce a verified label alone; it never produces a fabricated value.

`Steam2026SystemMenuCatalog` maps verified MUI node/resource identities to the
English labels and help text shipped in
`ff7/workingdir/data/layout_pc/system/en`. The catalog is checked against the
installed asset set during development. Other languages do not borrow English
labels; they fail closed until a verified language catalog exists.

### Speech coordination

`Steam2026SystemMenuSpeechCoordinator` owns scene lifecycle, focus deduplication,
value changes, modal ownership, and delayed help. It is independent of Prism
and emits speech requests that the existing x64 research session sends through
`Steam2026ResearchAccessibilityOutput`.

Focus speech interrupts immediately. Help speech becomes eligible after
500 milliseconds of stable focus and is non-interrupting. A focus, value,
scene, ownership, suspend, or shutdown change cancels stale help.

### FFNx popup capture

`FfnxPopupIdentity` validates the loaded x86 module, known popup message buffer,
TTL, and color globals. `FfnxPopupSpeechTracker` polls those verified globals
from the existing x86 runtime loop and recognizes a new popup generation from
its message and TTL transition. It announces a repeated identical message when
FFNx visibly restarts that popup's TTL, but it never repeats an unchanged live
popup every frame.

Both adapters use the existing Prism speaker. No OCR, screen scraping, input
replay, or key-based selection inference is introduced.

## Speech behavior

- Opening a scene speaks the scene name and focused item.
- A button speaks its visible label.
- A toggle speaks `Label, On` or `Label, Off`.
- A list speaks `Label, Value, N of M`.
- A slider speaks `Label, N percent`, or its native bounded numeric value when
  the control is not percentage based.
- A binding row speaks the action plus its primary and secondary assignments.
- Entering binding capture speaks the visible capture prompt. Accepted,
  conflicting, missing-primary, and restored-default states speak only after
  the native state or modal confirms them.
- A modal speaks its complete visible warning once, followed by the focused
  choice. Moving between choices speaks only the choice.
- Applying or leaving a screen does not invent `saved`; it announces only a
  visible native result or the newly focused scene.
- Moving before 500 milliseconds prevents the old item's help from speaking.
- FFNx popup speech reproduces the normalized visible text once per visible
  popup generation.

## Failure handling

- Exact executable/module identity and pristine callback prefixes are required.
- All hooks in a cohort validate before any is activated.
- Native detours do no speech, allocation-heavy formatting, blocking, or
  logging. They call the original exactly once and publish a bounded snapshot.
- Queue overflow, invalid pointers, incoherent reads, unexpected scene
  identities, or hook-lease failure revoke the affected cohort and leave the
  game running normally.
- Diagnostics are rate-limited. Unknown labels or values remain silent rather
  than being guessed.
- Suspend, resume, unload, and process shutdown clear pending descriptions and
  dispose hooks idempotently.

## Configuration

The shared accessibility configuration gains:

- `EnableNativeSystemMenuSpeech`, default `true`.
- `NativeSystemMenuHelpDelayMs`, default `500`, clamped to `0..5000`.
- `EnableFfnxPopupSpeech`, default `true`.

Existing installed configuration files remain valid because missing properties
receive these defaults. Deployment preserves the user's current configuration.

## Verification

Automated tests cover:

- catalog completeness and formatting for buttons, toggles, lists, sliders,
  bindings, and modals;
- immediate focus speech, stable delayed help, cancellation, deduplication,
  scene ownership, and suspend/reset;
- value changes and repeated identical values;
- FFNx new-popup generation and same-text TTL restart behavior;
- invalid pointers, incoherent snapshots, unknown identities, queue overflow,
  fingerprint rejection, hook activation rollback, original-once detours, and
  idempotent teardown;
- integration of both adapters with their existing runtime loops.

Release verification builds and runs the x64, x86, shared, and parity test
projects, publishes both runtime DLLs, checks PE architecture and hashes, and
copies the package into the installed Reloaded-II mod without launching FFVII,
7th Heaven, or Reloaded-II. The user performs the live in-game validation.
