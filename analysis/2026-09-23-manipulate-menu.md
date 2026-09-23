# Manipulate enemy action menu

The 23 September x64 log announces `Manipulated Razor WeedA.` at 09:45:56, but
does not announce the controlled enemy's action choices. The initial party
command (`Manip.`) and target selection already speak. The missing interface
is the enemy's own command list after control succeeds.

## Native evidence

Ghidra 12.1.2 was used with the installed `ff7_en.exe`, SHA-256
`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`.
[`DumpManipulateMenuEvidence.java`](ghidra/DumpManipulateMenuEvidence.java)
repeats the extraction to a private output path against an analyzed executable.
No player logs or decompiled game code are included in this repository.

| Native function | Observed behavior |
| --- | --- |
| `006D797C` | Renderer state `0x13` dispatches to the Manipulate action list. |
| `006D8C75` | A ready enemy sets **byte** `DC3C64` to actor index minus four; the party owner `DC3C7C` is not updated. |
| `006D8A71` | Initializes a one-column, three-row menu; cursor row at `DC276C`, scroll row at `DC277C`. |
| `006D8B1E` | Selected record is `9AC114 + enemySlot * 0x60 + (row + scroll) * 6`. Byte zero is scene attack index; byte three bit `0x02` disables confirmation. |
| `006E1F64` | Draws those records using text category 9. No MP cost is displayed. |
| `0041963C` | Category 9 resolves a 32-byte name at `9A9484 + sceneAttackIndex * 32`. |
| `005D0690` | Populates three commands per enemy from the loaded scene; unused records have index `FF` and flags `03`. |

Instruction inspection, rather than inferred Ghidra global types, confirms that
`DC3C64` is read and written as a byte. Scene action IDs are 16-bit entries at
`9A9444`, with 32 entries. Names come from the loaded battle scene, not the
KERNEL spell table. This also preserves installed/localized scene names.

## Implementation

The shared reader accepts enemy owners 4 through 9 only for this renderer state.
It verifies the owner, cursor, action index, action ID and terminated name before
publishing a selection. Invalid, unreadable or changing observations do not
produce guessed speech. A native blank row is identified as an unavailable
empty slot. Enemy HP and MP remain subject to the existing Sense privacy rules.

The x64 callback allowlist, menu worker and full battle observation path accept
the same enemy-owned state. The existing command-menu lifecycle now includes
the controlled enemy's root menu so an unchanged selected move speaks again
when that enemy gets another turn. The mod does not choose moves or change
battle state.

## Verification

`BattleManipulateMenuTests` reproduced the silence before the reader change.
The shared regression covers native action names, stale party ownership,
correct owner width, selection changes, blank/unavailable rows, reopening the
menu, hidden enemy stats, and invalid, unreadable and changing native data.

`Steam2026BattleRendererIngressTests` additionally sends state `0x13` through
the translated callback and worker, checks spoken action changes and repeated
turns, verifies the full translated battle frame, and rejects torn cursors and
unmapped name tables. These are native-layout fixtures, not a live battle test.

The shared regression runs in both default test hosts. Both also accept
`--battle-manipulate-only`; the x64 focused run includes the translated callback
and worker checks.
