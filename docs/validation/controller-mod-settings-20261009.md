# Controller and mod settings validation, October 9, 2026

The confirmed scope is held-trigger navigation and party readouts, spoken mod
settings, current-save battle-description reset, and saved live description volume,
for legacy x86 and Steam 2026 x64.

## Controller research

The installed Cyber Sleuth accessibility plugin was read in Ghidra 12.1.2:
`CyberSleuthAccessibility.dll`, SHA-256
`0197ba2894e9e5fef8bbd25931d3cfb3f0044bd85faf894590259a3024482156`.
Its base plugin has no source in the downloaded helper archive. The native
dispatch uses either trigger above SDL value 8000, D-pad Left/Right for categories,
Up/Down for targets, L3 for guidance and R3 for automatic travel. LT party controls
select members horizontally and read summary/statuses vertically. Blind Soldier
uses that scheme and its existing state readers, with LT+Y for the limit gauge.

SDL's documented input meanings were checked against the disassembly:
[controller axes](https://wiki.libsdl.org/SDL2/SDL_GameControllerGetAxis) and
[controller buttons](https://wiki.libsdl.org/SDL2/SDL_GameControllerGetButton).
Prior native FFVII evidence identifies R3's Battle Assist toggle on release.
Keeping R3 private in both button orders is necessary; only trigger-held R3
produces an accessibility command.

Research exports and test logs are retained under
`C:\Users\buu42\Documents\FFVII-ActiveBuild\controller-menu-20261009`.

## Focused checks

- Core, XInput and SDL cases cover real trigger values, both R3 press/release
  orders, axes, foreign devices, stale/busy contexts, reconnect and held controls.
- Worker cases cover settings independent of speech/controller polling, native
  party ownership, navigation retirement and focus loss. A reproduced Prism
  rejection no longer exits the Steam worker; current speech remains retryable.
- Settings tests cover all 65 player entries and explicit exclusion of technical
  options, immediate/restart wording, overrides, unknown keys, damaged/duplicate
  JSON, locked files, future-format preservation and saved shortcuts.
- History reset tests cover the bound save only, unsaved/no-game states, write
  failures, waiting/playing descriptions and the independent reset epoch. A cast
  already in progress stays identified and cannot silently mark itself heard again.
- Both architectures decode the actual loudest scene, battle, film and opening
  recordings through the production playback sample chain. Default output matches
  existing samples; live levels glide over 20 ms, preserve timing and stay within
  the 0.95 limiter ceiling. High boosts involve audible compression.
- An isolated x64 host exercises the installed SDL library's real native button
  and axis hooks using virtual controllers. It uses a hook-compatible Reloaded
  Memory 7 test backend because the standalone test host cannot initialize that
  hook library with its normal Memory 9 dependency. No game dependency was changed.

An independent read-only reviewer checked controller/host integration, settings
storage/effects, reset scheduling and gain/limiter behavior. All reported P1/P2
findings were repaired with regressions before packaging.

Full release-gate, exact-package, public-hash and deployment records are retained
separately in `C:\Users\buu42\Documents\FFVII-ActiveBuild\release-0.8.10-20261009`.
These checks do not establish live gameplay, audible timing in a game session,
or hardware-controller delivery in either FFVII runtime.
