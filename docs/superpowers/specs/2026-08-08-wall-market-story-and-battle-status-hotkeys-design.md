# Wall Market Story Routing and Battle Status Hotkeys

## Goal

Restore state-aware Story navigation through the first Wall Market visit and add instant, battle-only party status queries that behave identically in the 2013 x86 and 2026 x64 runtimes.

## Evidence and constraints

- The Wall Market interior objectives already exist. The missing layer is the outdoor gateway chain between lower Wall Market, upper Wall Market, the boutique, the bar, the gym, and Corneo Hall.
- The current save is at game moment 191 with the dress and wig obtained, but the fitting-room change incomplete. Story must therefore lead from lower Wall Market to the boutique fitting room.
- Battle HP, MP, maximum values, and status masks are available from the live battle actor structures.
- Ghidra confirms that character-record offset `0x0F` is the persisted limit value. Battle setup copies it into the live battle records at `0x009A8DC0 + partySlot * 0x34`, and battle cleanup copies that live value back to the savemap. The hotkey therefore reads the live battle record, where `0xFF` represents a full gauge, rather than reporting the stale battle-entry savemap value.
- Hotkeys must never consume or synthesize game input. They only observe foreground key transitions and speak state visible to a sighted player.

## Wall Market Story chain

All targets are conditional. Completed steps disappear and expose the next valid step.

| Plot phase | Lower Wall Market target | Upper Wall Market target | Interior target |
| --- | --- | --- | --- |
| Clothes clerk not consulted | Boutique | — | Ask the clothes clerk |
| Clerk consulted; dress not chosen | North to upper market | Bar | Ask the boutique owner at the bar |
| Dress chosen; dress not collected | Boutique | South to lower market | Collect the dress |
| Dress collected; wig not obtained | North to upper market | Men's Hall | Win or complete the gym sequence |
| Wig obtained; clothes not changed | Boutique | South to lower market | Enter the fitting room and change clothes |
| Clothes changed | North to upper market | Corneo Hall | Continue through the doorman |

The existing Honey Bee Inn branch remains available only in its valid earlier state. Each gateway target uses the same required/completed plot flags as its destination objective so stale targets cannot remain selected after progression.

## Battle status interaction

The feature is active only while the game is foregrounded and the live module is battle. It is disabled during field, world map, menus, and victory/results.

- `1`, `2`, `3`: select the corresponding current party slot and immediately identify the member.
- `H`: speak the selected member's current and maximum HP.
- `M`: speak current and maximum MP.
- `D`: speak active harmful or impairing statuses; explicitly say when there are none.
- `S`: speak active beneficial statuses; explicitly say when there are none.
- `L`: speak the native limit gauge as a percentage.

Example speech:

- `Cloud selected.`
- `Cloud HP 379 of 379.`
- `Cloud MP 74 of 74.`
- `Cloud debuffs: Poison, Slow.`
- `Cloud has no buffs.`
- `Cloud limit 73 percent.`

If a numbered slot is empty, speech says `Party member 3 unavailable.` and retains the last valid selection. If the selected member later becomes unavailable, a query reports that slot as unavailable rather than silently selecting someone else.

## Status classification

Beneficial statuses are Haste, Regen, Barrier, Magic Barrier, Reflect, Shield, Peerless, Death Force, Resist, and Lucky Girl. Every other active native status is reported by `D`; this avoids withholding unusual but visible battle state.

## Shared architecture

One shared controller owns selection, key mapping, status naming, classification, limit conversion, and speech formatting. The x86 and x64 adapters only provide foreground rising-edge input and live actor snapshots. This prevents the runtimes from drifting.

The existing battle-status announcement tracker will use the same shared status catalog, so automatic changes and manual queries cannot disagree about status names.

## Verification

- Simulate each Wall Market flag phase and assert the correct outdoor and interior Story target.
- Reproduce the current save flags and assert that lower Wall Market leads to the boutique fitting room.
- Verify party-slot selection, empty slots, HP, MP, no-status speech, mixed buff/debuff classification, and limit conversion at empty, partial, and full values.
- Seed different persisted and live limit values, then change the live value between polls and verify speech follows it.
- Verify `L` belongs to battle status in battle and remains Next Target for field and world navigation.
- Verify foreground and battle gating in both runtime adapters.
- Run shared, x86, x64 module, and parity tests before packaging.
- Deploy both runtime builds to the local game installations for live testing.
