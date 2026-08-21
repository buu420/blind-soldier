# Fort Condor complete unit-name table

Date: 2026-08-21

Source: `emes01.tex` extracted from the installed game's `condor.lgp`.

- `emes01.tex` SHA-256:
  `B47EA8B4186182BD161A4A8FC6355FBA2A63D1AFDA6ABFC6BB11FDEA0F24640A`
- decoded reference image: `.artifacts/condor-lgp/emes01.png`
- decoder: `analysis/ghidra/DecodeFf7Tex.ps1`

## Code-proven lookup rule

`FUN_005F933F` creates 24 regions. The draw path selects:

```text
regionIndex = 0x5F + typeId
sourceX     = (typeId / 6) * 64
sourceY     = 32 + (typeId % 6) * 16
width       = 64
height      = 16
texture     = emes01
```

The decoded PC texture is displayed at four times those logical coordinates,
but the order is unchanged: four columns, each read down six rows.

## Complete mapping

| Type ID | Column | Row | Drawn cell | Spoken label |
| ---: | ---: | ---: | --- | --- |
| 0 | 0 | 0 | `ダミー` | Dummy |
| 1 | 0 | 1 | Fighter | Fighter |
| 2 | 0 | 2 | Attacker | Attacker |
| 3 | 0 | 3 | Defender | Defender |
| 4 | 0 | 4 | Shooter | Shooter |
| 5 | 0 | 5 | Stoner | Stoner |
| 6 | 1 | 0 | Tristoner | Tristoner |
| 7 | 1 | 1 | Catapult | Catapult |
| 8 | 1 | 2 | Fire Catapult | Fire Catapult |
| 9 | 1 | 3 | `ダミー` | Dummy |
| 10 | 1 | 4 | `ダミー` | Dummy |
| 11 | 1 | 5 | `ダミー` | Dummy |
| 12 | 2 | 0 | Repairer | Repairer |
| 13 | 2 | 1 | Worker | Worker |
| 14 | 2 | 2 | `ダミー` | Dummy |
| 15 | 2 | 3 | `ダミー` | Dummy |
| 16 | 2 | 4 | Commander | Commander |
| 17 | 2 | 5 | Wyvern | Wyvern |
| 18 | 3 | 0 | Beast | Beast |
| 19 | 3 | 1 | Barbarian | Barbarian |
| 20 | 3 | 2 | `ダミー` | Dummy |
| 21 | 3 | 3 | `ダミー` | Dummy |
| 22 | 3 | 4 | `ダミー` | Dummy |
| 23 | 3 | 5 | `ダミー` | Dummy |

`ダミー` is katakana for "dummy." Speaking `Dummy` is a translation of the
label a sighted player sees, not an invented unit identity. Values outside
`0..23` do not select a valid name region and remain `unit` / `enemy unit`,
with the raw type logged once for diagnosis.

This table lives in shared source (`CondorUnitCatalog.cs`), so x86 and x64 use
the same labels.
