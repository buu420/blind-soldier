# Navigation Progress Controls and 7th Heaven Interop

## Goal

Give both FFVII runtimes one consistent set of navigation-progress controls,
while reducing the x86 runtime's dependence on private 7th Heaven loader
changes.

## User-facing controls

- `F5`: toggle all route progress controls on or off for the current game
  session.
- `F6`: select the previous notification interval.
- `F7`: select the next notification interval.
- Supported intervals are `5`, `10`, `15`, and `20` percent, wrapping at
  either end.
- Prism announces the resulting state or interval once per rising key edge.
- Field and world-map navigation share the same setting. A route that is
  active when progress is re-enabled immediately restores its current value.
- Progress may move downward at the same interval boundaries when the player
  backtracks.

The installed configuration supplies startup defaults only. Hotkey changes
are session-local so the mod does not rewrite user configuration while the
game is running.

## Architecture

`NavigationProgressController` owns enabled state and interval normalization.
Each route controller receives a separate
`IntervalFieldNavigationProgressSink`, but both sinks observe the same
controller. The wrapper tracks active/current route state, quantizes native
MSAA/UIA value changes, and hides or restores its native progress bar as the
shared setting changes.

The x86 monitor and x64 research session each poll `F5` through `F7` exactly
once per worker iteration before field/world navigation runs. Existing
foreground and rising-edge gates remain authoritative, preventing delayed key
presses after an Alt-Tab.

## Key compatibility evidence

- The active `ff7input.cfg` contains no DirectInput bindings for `DIK_F5`,
  `DIK_F6`, or `DIK_F7`.
- Ghidra analysis of the exact active `ff7_en` executable found only Ctrl+F2
  and Ctrl+Q direct Win32 hotkey checks; it found no direct F5-F7 hotkey path.
- FFNx 1.24.3 and current upstream FFNx use Ctrl combinations for their game
  shortcuts. Their only configurable standalone function key in the active
  configuration is F12 for developer tools.

## 7th Heaven and FFNx compatibility

Current runtime logs prove that Reloaded-II, FFNx, Cosmo Memory, field
textures, and the FPS mod can run together. Cosmo's own footstep and world-map
layers are intentionally disabled because this mod consumes the extracted
Cosmo samples from native player state and would otherwise duplicate them.

There are nevertheless two real loader-order hazards:

1. Reloaded starts the .NET 9 host before 7th Heaven's AppWrapper. Stock 7th
   Heaven then attempts BinaryFormatter deserialization without enabling its
   compatibility switch, so the wrapper profile may fail before IRO mods are
   mounted.
2. Stock 7th Heaven 4.5.2 forwards unrelated file/search handles through its
   managed wrapper callbacks. The locally installed loader has narrow
   handle-ownership guards that prevent crashes when Reloaded and other
   managed/native loaders coexist.

The mod will set
`System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization`
before normal x86 initialization. This removes the first private-patch
dependency in the proven startup order. The existing 7th Heaven handle guards
remain necessary until accepted upstream; the installer must preserve those
binaries and report their compatibility state rather than silently replacing
them.

## Failure behavior

- Invalid configured intervals normalize to 5 percent.
- Disabled progress suppresses activation, changes, and completion UI while
  retaining enough route state to restore the current value.
- A missing native progress control does not affect route speech or routing.
- 7th Heaven compatibility setup is idempotent and runs before any x86 mod
  logging, Prism, hooks, or route initialization.

## Verification

1. Unit-test interval wrapping, quantization, backtracking, disable/restore,
   and completion behavior.
2. Unit-test rising-edge routing and default configuration.
3. Unit-test the early 7th Heaven runtime switch as an idempotent behavior.
4. Run x86, x64, shared, installer, and dual-runtime verification suites.
5. Build and deploy the dual-runtime package without launching FFVII, 7th
   Heaven, or Reloaded-II, and verify the installed config hash is unchanged.
