# Controller controls and spoken mod settings

The user confirmed this scope on October 9: use Cyber Sleuth-style held-trigger
navigation and party readouts, add a spoken menu containing existing player
toggles, reset battle descriptions for the current save, and allow recorded
descriptions to be made louder. Both supported FFVII runtimes are required.

## Controls

Ghidra 12.1.2 research of the installed CyberSleuthAccessibility.dll verifies
either trigger as a field navigation modifier, D-pad Left/Right for categories,
Up/Down for targets, L3 for path guidance and R3 for auto walk. In battle, its
left trigger selects party information: Left/Right changes member, Up reads a
summary and Down reads additional status. Blind Soldier will use this scheme,
retain explicit starts and offer modifier+B to stop navigation. Party summary
reads HP, MP and limit; status reads the existing buffs/debuffs. Normal combat
and Confirm remain player controlled. Holding either trigger suspends browsing
movement, and the closing press stays captured until released. In particular,
R3 within a modifier chord must never reach native Battle Assist, including
stale/busy contexts, focus transitions, another device and release order.
R3 remains reserved outside a complete chord too: Steam toggles Battle Assist on
release, so allowing an R3-first sample before its trigger would expose a false
release when capture begins. R3 alone performs no accessibility command.

F11 or both triggers+Y opens settings. Settings uses the existing safe mod
keyboard keys J/L (items), U/O (values), I (activate), K (repeat), F11 (close).
Controller: D-pad Up/Down (items), Left/Right (values), A (activate), B (close).
The menu speaks labels, values, controls and persistence/restart requirements.
It pauses accessibility auto walk and inhibits competing mod hotkeys. It does
not use native Confirm or directional keyboard keys.

## Settings and narration

Use a declarative, labelled catalogue of player settings; diagnostic hooks,
addresses, scan tuning and asset paths do not belong in the player menu. Store
only explicit player overrides atomically in Configuration/player-settings.json,
apply them after config.json, and preserve unrelated configuration. Startup-only
settings must announce that a restart is required. Menu speech remains available
when the general speech option is disabled. Existing progress and highway
shortcuts update their saved settings through the same store.

Recorded-description volume is a live master level, 50-300 percent in 25-percent
steps, initially 100 percent. It multiplies the existing per-track calibration,
including the quieter opening recording. It affects recorded scene, movie and
battle descriptions, not Prism voice, music or cue sounds. A limiter prevents
boosted samples exceeding digital full scale; changing level affects a playing
recording without restarting its timing or breaking pause/completion tracking.

Reset battle descriptions clears only the current playthrough/current native
save slot in battle-descriptions.json. It includes summons, limits and the shared
summon opening. Other slots and room history survive. A second activation on the
reset item confirms the action. An old in-flight description must not repopulate
the cleared history, and an already-visible animation must not restart midway.
Descriptions become eligible on subsequent native animations.

## Verification and delivery

Verify raw controller/axis output and press/release ordering on the real hook
adapters, not just command flags. Cover focus, reconnect, multiple pads, stale
context, battle/field/world/submarine ownership, held buttons and menu reopening.
Verify current-save reset persistence and in-flight history races; verify gain,
limiter output bounds and live changes with the actual runtime decoder/provider.
Run both runtime suites and release gates. Preserve saved configuration, both
histories, narration assets and game saves in local deployment and installer
updates. Publish using Author CLI 0.30.0's verified phases. Do not claim live
controller or gameplay validation without exercising it.
