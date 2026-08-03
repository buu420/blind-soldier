# Blind Swordsman

Blind Swordsman is an accessibility mod for the original Windows PC version
of **Final Fantasy VII**. It presents information that a sighted player would
normally see through screen-reader speech, audio description, positional
sounds, footsteps, and spoken navigation.

The mod includes support for menus, dialogue and choices, battles, field and
world-map travel, objects, NPCs, story targets, selected minigames, and timed
events. Screen-reader output is provided through the bundled Prism library; it
is not tied to one particular screen reader.

> [!IMPORTANT]
> This project is for the original Final Fantasy VII PC game, not Final
> Fantasy VII Remake or Rebirth. It is a pre-release project under active
> development. Current live testing covers the story from the opening through
> the first arrival on the world map.

## Requirements

- Windows 10 or Windows 11.
- A legal Steam installation of the original Final Fantasy VII.
- [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II).
- Optional: 7th Heaven and FFNx for the legacy x86 game path.

Blind Swordsman contains separate x86 and x64 backends. The installer detects
either the legacy Steam 2013 runtime (`ff7_en.exe`) or the current native Steam
2026 runtime (`FFVII.exe`) and refuses unknown executable builds instead of
reading unverified game memory.

## Installation

Download and run
[Blind-Swordsman-Setup.exe](https://github.com/buu420/blind-swordsman/releases/download/v0.1.0-pre.2/Blind-Swordsman-Setup.exe).
If that direct link changes, use the
[Blind Swordsman Releases page](https://github.com/buu420/blind-swordsman/releases).

1. Close Final Fantasy VII, 7th Heaven, and Reloaded-II.
2. Run `Blind-Swordsman-Setup.exe`. You do not need to extract a ZIP, install a
   .NET runtime, open a terminal, or run a script.
3. Review the detected Final Fantasy VII and Reloaded-II folders. If setup did
   not find one, activate its labeled **Choose folder** button and select it.
   The game is discovered from Steam libraries. Reloaded-II is discovered from
   its own registered launcher or from a portable `Reloaded-II` folder inside
   or alongside the game; no developer-specific folder is built into setup.
4. Review the dependency status list. Setup will not continue until the game,
   correct x86 and x64 loaders, and Reloaded Shared Hooks are ready.
5. Choose **Install**. Keep the setup window open until it announces completion.

> [!WARNING]
> The first public installer is not code-signed. Windows SmartScreen may say
> **Unknown publisher**. Only continue when you downloaded the file from this
> repository's Releases page. A future release can remove that warning after a
> code-signing certificate is available.

Setup downloads the matching runtime ZIP from the same GitHub release, checks
its exact length and SHA-256 hash, validates every file before extraction, and
then installs the prebuilt x86 and x64 mod. It preserves unrelated 7th Heaven
and FFNx settings. Both integrations are optional: setup detects them for
compatibility reporting, but never installs, requires, or replaces either one.

For the native Steam 2026 edition, the same verified runtime archive also
contains the accessible `FFVII_LAUNCHER.exe`, its configuration, and a
launcher-only x86 Prism library. Setup records and verifies the original Steam
launcher before replacing it. Repair keeps the original backup, and uninstall
restores it when the installed launcher is still unchanged. A launcher file
changed after installation is preserved rather than overwritten.

The native Steam 2026 x64 backend is included, but remains prerelease research
software. The setup labels the detected runtimes and refuses unknown game
executables instead of reading unverified memory.

### Update, repair, or remove

- Run the installer again to update to a newer release or repair the installed
  version.
- Open the Start menu and choose **Check for Blind Swordsman Updates** to run a
  manual update check. No updater runs in the background.
- Remove the mod through **Settings > Apps > Installed apps > Blind Swordsman**.
  Uninstall removes setup-owned files, preserves files changed after
  installation, and restores a recorded prior mod package only when it still
  matches the saved backup. On Steam 2026 it also restores the verified
  original FFVII launcher.

Installer state and readable logs are stored under
`%LOCALAPPDATA%\Blind Swordsman`. The completion page includes a link that
opens the current setup log.

### Offline or local release files

Keep `Blind-Swordsman-Setup.exe`, `Blind-Swordsman-Runtime.zip`, and
`blind-swordsman-channel.json` from the same release in one folder. From a
Command Prompt or PowerShell window, run:

```powershell
.\Blind-Swordsman-Setup.exe --local-manifest ".\blind-swordsman-channel.json"
```

Setup uses the sibling runtime ZIP only after its size and SHA-256 match the
manifest. See [Installer details](docs/installer.md) for the full setup and
recovery behavior.

### Launching the game

- **Native Steam 2026 x64:** launch Final Fantasy VII normally from Steam. The
  included FFVII launcher is screen-reader accessible, and starting the game
  from it loads the x64 accessibility backend automatically. You may also run
  `FFVII.exe` directly.
- **Legacy x86 with 7th Heaven:** launch the game through 7th Heaven as usual.
- **Legacy x86 without 7th Heaven:** use your Reloaded-II FFVII profile, or run
  `Launch-FF7Reloaded.cmd -Runtime Legacy` and provide `-ReloadedRoot` if needed.

On a successful load, Prism announces that Final Fantasy VII accessibility is
active.

## Developer installation from source

End users should use the setup EXE above. To build and deploy from a source
checkout, install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0),
close the game and launchers, then build and extract a complete runtime payload:

```powershell
$release = .\Build-BlindSwordsmanRelease.ps1 -Version 0.1.0-pre.2 -Tag v0.1.0-pre.2 -OutputPath ".\artifacts\developer-$([guid]::NewGuid())"
$runtimeRoot = "$($release.OutputPath)\runtime"
Expand-Archive -LiteralPath $release.PayloadPath -DestinationPath $runtimeRoot
.\Install-FF7ReloadedMod.ps1 -ReloadedRoot "C:\Path\To\Reloaded-II" -PackagePath "$runtimeRoot\package" -LauncherBundlePath "$runtimeRoot\launcher" -AllowResearchNativeProfile
```

Add `-GameRoot "C:\Path\To\FINAL FANTASY VII"` only when Steam detection does
not find the game. This applies the same packaged files and the same identity,
backup, and rollback checks used by setup.

## Mod keys

These keys work only while the Final Fantasy VII game window is in the
foreground. The normal game controls are unchanged.

| Key | Action |
| --- | --- |
| `U` | Previous navigation category |
| `O` | Next navigation category |
| `J` | Previous target in the selected category |
| `L` | Next target in the selected category |
| `K` | Repeat the selected target, or report the active route and progress |
| `I` | Start navigation to the selected target, or stop the active route |
| `F5` | Turn route progress indicators off or on |
| `F6` | Select the previous progress interval |
| `F7` | Select the next progress interval |
| `F8` | Toggle automatic steering during the motorcycle minigame |

Progress intervals are `5`, `10`, `15`, and `20` percent. `F6` and `F7` wrap
around at either end. Key changes to progress settings last for the current
game session; the installed configuration supplies the next launch's defaults.

## Navigation

Blind Swordsman uses the same navigation controls in fields and on the world
map. You select a category, select a target, and then start a route.

### Quick start

1. Press `U` or `O` to choose a category.
2. Press `J` or `L` to browse targets. Each target is spoken with its name and
   an initial direction or distance when that information is available.
3. Press `I` to start the route.
4. Follow the spoken directions. Press `K` at any time to hear the target,
   current direction, and route progress again.
5. The route completes when you reach the target's usable interaction area or
   pass through its destination exit. Press `I` to cancel sooner.

Changing category or target while navigation is active locks the route to the
new selection. On the world map, targets that cannot be reached by the current
character or vehicle are filtered out. In a field, a visible target may remain
selectable but report `direction unavailable` when no safe walkmesh route can
currently be built.

### Field categories

- **Exits:** doors, field transitions, and other ways into a different area.
- **Story:** the next available interaction or location that advances the
  current sequence.
- **NPCs:** visible characters that can currently be approached or spoken to.
- **Objects:** items, materia, chests, switches, ladders, elevators, and other
  useful interactable points.

### World-map categories

- **Locations:** towns, caves, and other entrances.
- **Story:** the currently available story destination.
- **Transportation:** vehicles and other usable transport.
- **Events:** temporary or progression-dependent world-map targets.
- **Chocobo Tracks:** reachable track areas used when catching chocobos.

The world map is divided internally into connected regions, but those regions
are not another category you must manage. Blind Swordsman shows only targets
the current character or vehicle can reach and finds the connected route
between regions when one exists.

### How routes work

In fields, the mod reads the game's native walkmesh: the same triangles,
boundaries, ladders, jumps, and elevations that control where Cloud can walk.
On the world map it reads the native map triangles and their connections. A
route is built from the player's live X, Y, and Z position to the chosen target.

The route then combines connected waypoints that continue in the same usable
direction. That is why a wide, clear corridor can be announced as `up 20`
instead of repeating several shorter `up 4` instructions. A new direction is
spoken when the route actually turns, changes elevation, enters a ladder or
other traversal link, or must go around an obstacle.

The spoken number is an approximate travel-distance count, not an exact number
of key presses. Walking, running, animation speed, and the shape of the field
can change how many physical steps it takes. Keep moving in the announced
direction until the next instruction, arrival message, or progress change.

Navigation follows live movement rather than blindly playing a fixed list. If
you stray far enough from the route or a live obstacle blocks the intended
path, it can safely recalculate. Small movement inside a wide clear path should
not cause constant corrections. During supported ladders, the spoken direction
describes the control input needed to continue along the route, including
horizontal climbs. Routes pause across battles and other temporary ownership
changes and resume when the game returns to the navigable field or world map.

### Progress indicators

Route progress uses a native Windows progress control so each screen reader can
announce it using its own familiar progress behavior. It applies to every
field and world-map route and runs from 0 to 100 percent.

**Keeping progress indicators on is strongly recommended.** Directions can
sound circular in stairs, multi-level rooms, winding passages, and wrapped
world-map regions. The progress value tells you whether you are genuinely
advancing even when the geometry turns back toward an earlier compass
direction.

Progress is based on your position along the whole connected route, including
elevation. If you backtrack toward the start, the value goes down; this is
intentional. When you return to the route and move toward the destination, it
rises again.

- Press `F5` to turn progress off. Press `F5` again to restore it at the active
  route's current value.
- Press `F6` for the previous interval or `F7` for the next interval.
- The default is every 5 percent. Choose 10, 15, or 20 percent if your screen
  reader announces progress more often than you prefer.

Turning the progress indicator off does not stop navigation or its spoken
directions. It only hides the native progress control for the current session.

### If a route seems wrong

- Press `K` to repeat the active route status before changing direction.
- If you are touching a wall or object, step away from it and allow the route a
  moment to update.
- On a ladder, continue with the announced climb direction until the landing
  transition completes.
- Press `I` once to stop and again to rebuild the route from your current
  position.
- If the problem repeats, report the field or world-map location, selected
  category and target, whether you were walking or running, and what was spoken.

## Gameplay help

This README explains the accessibility layer, not how to beat Final Fantasy
VII. Use a walkthrough or strategy guide for combat tactics, puzzle solutions,
missable items, and story choices. Blind Swordsman reports available game
information and navigation targets without choosing those decisions for you.

## Troubleshooting

### The mod does not speak

1. Confirm that Reloaded-II loaded `ff7.accessibility.reloaded` for the game
   process you launched.
2. Confirm that the game window is in the foreground.
3. Make sure your screen reader is running before launching the game.
4. Check `Configuration\config.json` inside the installed mod and make sure
   `EnableSpeech` is `true`.

### Navigation keys do nothing

Navigation is intentionally disabled while another application has focus and
during menus, dialogue, movies, battles, or scripted control locks that own the
same game state. Return to controllable field or world-map movement and press
`K` or `I` again.

### Installation rejects the game executable

The installer supports exact verified FFVII builds and fails closed after an
unknown game update. Do not bypass that check. Include the installer error and
your executable version in a bug report.

### Setup cannot find Reloaded-II

Choose **Choose Reloaded-II folder** and select the folder containing
`Reloaded-II.exe`, the `Loader` folder, and the `Mods` folder. Setup also checks
that both architecture loaders and Reloaded Shared Hooks are present. Review
the dependency list for the exact missing component.

### Windows reports Unknown publisher

That warning is expected for the unsigned prerelease installer. Cancel if the
file did not come from this repository's Releases page. You can compare the
download against `Blind-Swordsman-Setup.exe.sha256` on the same release before
running it.

## Reporting a problem

Open a GitHub issue and include:

- whether you used legacy x86/7th Heaven or native Steam 2026 x64;
- the field, menu, battle, or world-map location;
- the exact action and keys that led to the problem;
- what Blind Swordsman said and what you expected it to say;
- `ff7_accessibility_reloaded.log` from the installed
  `Mods\ff7.accessibility.reloaded` folder;
- the matching Reloaded-II log from
  `%APPDATA%\Reloaded-Mod-Loader-II\Logs`.

Do not attach a game executable, copyrighted game archive, account credential,
or other private data.

## Credits and legal notice

Blind Swordsman is created by buu420 with development assistance from Codex.
It builds on Reloaded-II, Prism, FFNx interoperability work, Kujata metadata,
and accessibility audio derived from the supported game. Third-party
components, game-derived material, names, and assets remain subject to their
respective rights and licenses.

Final Fantasy VII and related names and assets are trademarks or copyrights of
their respective owners. Blind Swordsman is an independent accessibility
project and is not affiliated with or endorsed by Square Enix.
