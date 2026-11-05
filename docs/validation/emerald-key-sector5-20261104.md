# Emerald Weapon and Key to Sector 5 validation

## Emerald Weapon

The installed underwater world script allocates one model 30 while the submarine is available and Emerald is undefeated. Its encounter and patrol script reads and moves that model at its underwater position independently of drawing. In the 2013 engine, Ghidra identifies the render-flag clear at `0x76315C` and the separate model position copy at `0x7631DF`. The reader requires one coherent live model with a valid identity. A camera or fog change does not erase it; an unreadable or retired model cannot supply pursuit input.

The native script puts Emerald at depth -4250 and starts its encounter within 1215 units. Ordinary travel stays at depth -3000; the mod uses the current position for warnings when the player takes over to descend. Pursuit tests cover native camera gaps, moving routes, invalid reads, replacement, and avoidance. The reviewed Echo-S world script is byte identical for this behavior.

## Key to Sector 5

The man at Midgar's gate (field 170, entity 7) presents the clue above game moment 1195. The original dialogue is 141; the five reviewed Echo-S layouts use 23. The dialogue sets no saved flag. The mod matches the actual visible native window to the loaded clue pages and keeps the observation with the native save slot. This also works when dialogue speech is off. The separate observation is loaded and saved at the same checked points as description history.

Native field tests cover the foreman's talk approach from the world-map arrival, the three exact key dig triangles (60, 12 and 8), normal ladder and Switch input, the chest at the dig return, and retirement when the native reward flag is set. Any pending result takes precedence over another dig so that it is not overwritten. The unopened chest uses a neutral label. The Echo-S field map now includes the verified foreman, whose script entity has no name.

These are native data, simulated state and package checks on 2013 x86 and Steam x64, including the five reviewed Echo-S layouts. They do not establish live gameplay, physical controller or audible screen-reader behavior.
