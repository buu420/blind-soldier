# Blind Soldier

Blind Soldier is an accessibility mod for the original Windows PC version
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
- Optional: 7th Heaven for the legacy x86 game path. Its normal FFNx runtime is
  managed by 7th Heaven, not by Blind Soldier.

**Final Fantasy VII is the only software a native Steam player must install first.**
Players using legacy x86 through 7th Heaven need the official stable 7th Heaven
4.5.2 release, which owns its FFNx runtime. The portable ZIP contains Reloaded-II,
Reloaded Shared Hooks, Prism, and private x86 and x64 .NET runtimes. Development
builds targeting .NET 10 are not part of this release's verified compatibility set.

Blind Soldier contains separate x86 and x64 backends. Its bootstrap validates
the legacy Steam 2013 runtime (`ff7_en.exe`), converted x86 runtime (`ff7.exe`),
or current native Steam 2026 runtime (`FFVII.exe`) and refuses unknown builds
instead of reading unverified game memory.

## Installation

Choose one download from the
[Blind Soldier Releases page](https://github.com/buu420/blind-soldier/releases):

- [Blind-Soldier-Portable.zip](https://github.com/buu420/blind-soldier/releases/download/v0.4.1/Blind-Soldier-Portable.zip)
  is the complete dual-runtime package. Use it for Steam 2026 x64 or when one
  extracted package must support both x86 and x64 installations.
- [Blind-Soldier-2013-x86-Portable.zip](https://github.com/buu420/blind-soldier/releases/download/v0.4.1/Blind-Soldier-2013-x86-Portable.zip)
  is the smaller legacy-only package. Use it for the 2013 x86 game, including
  stock 7th Heaven/FFNx. It deliberately contains no Steam 2026 launcher or
  x64 files.

1. Close Final Fantasy VII and 7th Heaven.
2. For Steam 2026 or direct 2013 launch, extract every file in the selected ZIP
   directly into the Final Fantasy VII game folder. Do not extract it into a
   separate subfolder. For 7th Heaven, extract the x86 ZIP into the directory
   that contains its `workingdir` folder, not inside `workingdir` itself.
3. Start the game normally from Steam or 7th Heaven.

There is no installer to run, no administrator prompt, and no registry change.
The dual-runtime archive supports Steam 2026 x64, stock 7th Heaven's sibling
`workingdir` layout, and 7th Heaven's nested `ff7\workingdir` layout. The x86
archive supports direct 2013 launch and the sibling `workingdir` layout while
omitting the 2026 launcher, nested 2026 compatibility layout, x64 backend, x64
Reloaded loader, and x64 private .NET runtime. Neither archive replaces or
edits a 7th Heaven or FFNx file. Never run a file under
`Blind-Soldier\Bootstrap` yourself.

> [!WARNING]
> If a supported layout-scoped path already contains an unknown
> `version.dll`, move it to a safe backup before extraction. These paths include
> the executable-specific `.local` folders and the stock 7th Heaven sibling
> `workingdir`. Blind Soldier does not merge with or overwrite another Version
> proxy safely.

For x86 FF7, the proxy forwards all 17 Windows Version APIs. If FFNx redirects
the system request back to the proxy, Blind Soldier makes a private byte-for-byte
copy of this PC's own Windows library under
`%LOCALAPPDATA%\Blind Soldier\NativeCache` and loads that distinct copy. The
release does not ship a Windows system DLL.

### Update or remove

- To update or repair, close the game and extract the newer ZIP over the same
  game folder.
- To remove the mod, close the game and delete the files listed by
  `portable-manifest.json`, then restore any launcher or `.local\version.dll` you
  backed up before extraction.
- Logs are written beneath `Blind-Soldier\Logs` in the game folder.
- Steam **Verify integrity of game files** may restore the stock launcher.
  Extract Blind Soldier again afterward to restore launcher accessibility.

### Launching the game

- **Native Steam 2026 x64:** launch Final Fantasy VII normally from Steam. The
  included FFVII launcher is screen-reader accessible, and its Play button
  loads the x64 accessibility backend automatically. Starting `FFVII.exe`
  directly is unsupported because it bypasses that accessibility boundary.
- **Legacy x86 with 7th Heaven:** launch the game through 7th Heaven as usual.
  7th Heaven manages the normal FFNx installation for that converted game. Blind
  Soldier never ships or overwrites FFNx files, and its compatibility path does
  not modify the stock 7th Heaven application.
- **Legacy x86 without 7th Heaven:** extract the x86-only ZIP beside
  `ff7_en.exe`, then launch the legacy game normally. Its sibling `version.dll`
  loads the accessibility backend without a registry change.

On a successful load, Prism announces that Final Fantasy VII accessibility is
active.

## Language support

Blind Soldier supports the current Windows game's English, French, German,
Spanish, and Japanese releases on both the x86 and x64 backends. It also
supports the Bunio Polish fan translation's native dialogue and item text. With the
default `"GameLanguage": "auto"` setting, it detects the active language from
the game executable, Steam manifest, installed language data, and the known
Polish translation font or kernel text. Menus, dialogue, item names, spell
names, and other native game text are read from the matching localized FFVII
data rather than translated or guessed by the mod.

Blind Soldier's own generated messages—such as navigation, battle status, and
accessibility prompts—use its matching `en`, `fr`, `de`, `es`, or `ja`
translation catalog. If an individual generated description has not been
translated yet, Blind Soldier speaks that message in English so the player
still receives the visual information. The opening movie's recorded audio
description is English in this first multilingual test build.

To override automatic detection, edit
`Reloaded-II\Mods\ff7.accessibility.reloaded\Configuration\config.json` and set
`GameLanguage` to `en`, `fr`, `de`, `es`, `ja`, or `pl`. The full names
`English`, `French`, `German`, `Spanish`, `Japanese`, and `Polish` are also
accepted. Use `auto` to restore detection. The Polish profile deliberately uses
the translation's English asset paths while applying its Polish font mapping;
Blind Soldier's generated prompts and recorded opening description remain in
English unless a Polish override catalog or recording is supplied.

Translators can override or extend generated messages without rebuilding the
mod by creating
`Reloaded-II\Mods\ff7.accessibility.reloaded\Languages\<code>.json`. The file
must be a JSON object whose keys are the original English messages and whose
values are their translations. Invalid or oversized override files are ignored
and reported in the Blind Soldier log.

## Developer installation from source

End users should use the portable ZIP above. To build it from a source
checkout, install the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0),
Visual Studio C++ Build Tools, and PowerShell, then run:

```powershell
.\Build-BlindSoldierPortablePackage.ps1 `
  -OutputPath .\artifacts\Blind-Soldier-Portable.zip `
  -Version 0.4.1
.\Verify-BlindSoldierPortablePackage.ps1 `
  -ArchivePath .\artifacts\Blind-Soldier-Portable.zip `
  -ExpectedVersion 0.4.1
.\Build-BlindSoldier2013PortablePackage.ps1 `
  -SourceArchivePath .\artifacts\Blind-Soldier-Portable.zip `
  -OutputPath .\artifacts\Blind-Soldier-2013-x86-Portable.zip `
  -Version 0.4.1
.\Verify-BlindSoldier2013PortablePackage.ps1 `
  -ArchivePath .\artifacts\Blind-Soldier-2013-x86-Portable.zip `
  -ExpectedVersion 0.4.1 `
  -ExpectedSourceArchivePath .\artifacts\Blind-Soldier-Portable.zip
```

The builder compiles the launcher, x86/x64 brokers, and x86 Version proxy from source,
then packages the pinned Reloaded and private .NET dependencies. The second
builder derives the x86-only archive from that verified dual-runtime package;
it does not rebuild or substitute the native proxy. The x86 Reloaded
compatibility patch and its exact build instructions ship in `LICENSES`; 7th
Heaven manages FFNx and remains unmodified.

## Mod keys

These keys work only while the Final Fantasy VII game window is in the
foreground. The normal game controls are unchanged.

| Key | Action |
| --- | --- |
| `R` | Repeat the last message Blind Soldier spoke |
| `U` | Previous navigation category |
| `O` | Next navigation category |
| `J` | Previous target in the selected category |
| `L` | Next target in the selected category |
| `K` | Repeat the selected target, or report the active route and progress |
| `I` | Start navigation to the selected target, or stop the active route |
| `P` | Start or stop automatic walking to the selected navigation target |
| `F5` | Turn route progress indicators off or on |
| `F6` | Select the previous progress interval |
| `F7` | Select the next progress interval |
| `F8` | Toggle automatic steering during the motorcycle minigame |

Progress intervals are `5`, `10`, `15`, and `20` percent. `F6` and `F7` wrap
around at either end. Key changes to progress settings last for the current
game session; the installed configuration supplies the next launch's defaults.

### Battle status keys

These keys are active only during battle. `L` checks the selected member's
limit gauge in battle and remains the next-target navigation key everywhere
else.

| Key | Action |
| --- | --- |
| `1` | Select and identify party member 1 |
| `2` | Select and identify party member 2 |
| `3` | Select and identify party member 3 |
| `H` | Read current and maximum HP |
| `M` | Read current and maximum MP |
| `D` | Read active debuffs and other harmful conditions |
| `S` | Read active buffs |
| `L` | Read the native limit gauge percentage |

An empty numbered slot is announced as unavailable and does not replace the
last valid selection. Buff and debuff queries explicitly say when there are
none.

## Navigation

Blind Soldier uses the same navigation controls in fields and on the world
map. You select a category, select a target, and then start a route.

### Quick start

1. Press `U` or `O` to choose a category.
2. Press `J` or `L` to browse targets. Each target is spoken with its name and
   an initial direction or distance when that information is available.
3. Press `I` to start a route you will follow manually, or press `P` to start
   the route and walk it automatically.
4. Follow the spoken directions when walking manually. Press `K` at any time
   to hear the target, current direction, and route progress again.
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
are not another category you must manage. Blind Soldier shows only targets
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

### Auto walk

After selecting a destination with `J` or `L`, press `P`. Blind Soldier starts
the same native-walkmesh route used by spoken navigation and holds only the
directional controls needed for the current route segment. Press `P` again to
stop automatic movement without changing the selected target.

Auto walk never presses the action or confirm button. At a door, switch, chest,
NPC, ladder entrance, or other interaction point, it releases the directional
keys and leaves the interaction to you. Once Cloud is mounted on a supported
ladder, auto walk can follow the route's live climb direction, including a
horizontal climb.

For safety, all directions are released during dialogue, menus, movies,
battles, control locks, loading transitions, focus loss, unreadable game state,
route failure, and arrival. A route paused by a normal battle or transition can
resume when the same valid route returns. Changing the category or target
automatically stops the current auto walk before selecting a different route.

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
missable items, and story choices. Blind Soldier reports available game
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
`K`, `I`, or `P` again.

### Launch rejects the game executable

The bootstrap supports exact verified FFVII builds and fails closed after an
unknown game update. Do not bypass that check. Include the spoken error, the
matching file under `Blind-Soldier\Logs`, and your executable version in a bug
report.

### A Version proxy collision is reported

Close the game, preserve the existing `version.dll`, and determine which mod owns
it before extracting Blind Soldier. The portable package deliberately does not
guess how to combine two proxy DLLs.

### The downloaded ZIP fails verification

Download the ZIP and `.sha256` sidecar again from the same GitHub release. Do
not use an archive whose SHA-256 does not match the sidecar.

## Reporting a problem

Open a GitHub issue and include:

- whether you used legacy x86/7th Heaven or native Steam 2026 x64;
- the field, menu, battle, or world-map location;
- the exact action and keys that led to the problem;
- what Blind Soldier said and what you expected it to say;
- the matching bootstrap or Version log under `Blind-Soldier\Logs`;
- `ff7_accessibility_reloaded.log` from
  `Reloaded-II\Mods\ff7.accessibility.reloaded`;
- the matching Reloaded-II log from
  `%APPDATA%\Reloaded-Mod-Loader-II\Logs`.

Do not attach a game executable, copyrighted game archive, account credential,
or other private data.

## Credits and legal notice

Blind Soldier is created by buu420 with development assistance from Codex.
It builds on Reloaded-II, Prism, FFNx interoperability work, Kujata metadata,
and accessibility audio derived from the supported game. Third-party
components, game-derived material, names, and assets remain subject to their
respective rights and licenses.

Final Fantasy VII and related names and assets are trademarks or copyrights of
their respective owners. Blind Soldier is an independent accessibility
project and is not affiliated with or endorsed by Square Enix.
