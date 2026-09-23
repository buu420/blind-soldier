# Native gil display evidence

Verified with Ghidra 12.1.2 against the supported legacy `ff7_en.exe`, SHA-256
`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`.
The x64 runtime reads the corresponding verified translated guest layout.
This is static renderer evidence, not a live gameplay test.

`analysis/ghidra/DumpGilEvidence.java` collects wallet references and decompiles
their menu functions. Its first argument is an output filename; optional remaining
arguments are hexadecimal function addresses. The project can be opened with
`-readOnly -noanalysis`.

## Main menu

The current balance is an unsigned 32-bit value at `0x00DC08B4`.
The root menu renderer `FUN_006CA346` draws it beside play time and location in
both resolution branches: reference `0x006CAADA` uses `FUN_006FCF5B`; reference
`0x006CB11F` uses `FUN_006F9739`.

`FUN_006CB56A` dispatches through the table at `0x0091AB98`. Entry zero is
`FUN_006CA346`. The dispatch target is `0x00DC12EC`, except in transition states
2, 4, and 5, which use `0x00DC12E8`. `FUN_006C9808` returns the state at
`0x00DC1294`. An old open flag or recent text alone cannot prove that the current
screen still draws the root wallet after a full submenu replaces it.

`FUN_006CB56A` sets the menu-session flag `0x00DC12F0` while active and clears it
on exit when root phase `0x00DC1298` becomes -1. The root renderer accepts input
when phase is 1; phase 0 opens and phase 2 closes. The general open flag
`0x00DC1108` is also reused by submenu transitions in `FUN_006C98A6`, so it must
not alone authorize the hotkey. `FUN_006C9812` changes the transition state and
`FUN_006C6AEE` moves the previous target into `0x00DC12E8` before changing
`0x00DC12EC`.

The original Eidos PC manual corroborates the root display under "The Menu
Screen": it includes the party's total gil alongside the total play time.
[Original manual mirror](https://manualmachine.com/gamespc/finalfantasyvii/1119662-user-manual/).

## Shops

`FUN_0071AAA3` switches on `0x0092565C` in both resolution branches:

| State | Screen | Current balance drawn |
| --- | --- | --- |
| 0 | Buy / Sell / Exit | No |
| 1 | Buy list | Yes |
| 2 | Sell-item list | No |
| 3 | Sell materia | Yes |
| 4 | Buy quantity | Yes, falls through to state 1 rendering |
| 5 | Sell-item quantity | Yes, then falls through to state 2 rendering |
| 6 | Items / Materia sell choice | No |

`FUN_00719E27`, called before this switch, draws the store's name, not a wallet.
The buy panel draws current gil at references `0x0071B5DE` / `0x0071CCD0`.
The materia sell panel uses `0x0071B09E` / `0x0071C79F`; the item quantity panel
uses `0x0071BD89` / `0x0071D460`. These selling panels also draw projected
after-sale balances, which must not replace current gil in the repeat readout.

## Other contexts

Battle results render earned/uncollected gil at `0x0099E2C8` separately from the
wallet at `0x00DC08B4`. Coin/Toss and Fort Condor have distinct state machines.
This menu feature does not change those existing readers or announce the hidden
wallet during normal field/world movement.
