# Gongaga exit and companion interactions

## Report and reproduced data gap

The reporting x64 session enters Gongaga Village (field 518, `gongaga`) at game moment 469. Its six gateway targets lead to buildings, but the way back out is missing. The player later reaches the jungle manually. The optional conversation with Zack's parents includes both Aerith and Tifa; back in the village, navigation lists only the two ordinary residents.

Game moment 469 is compatible with the early optional visit. The existing story record for the village exit covers the later mandatory visit at moments 641–651 and cannot substitute for an ordinary exit target.

## Native scripts

Entity 11 (`line4`) defines the exit segment from (-798, -1333, 17) to (-529, -1386, 17). Its S1/Confirm handler always leaves the village. Native bank 3, address 132, bit 6 selects world-map field 17; the other branch returns to jungle field 514. Both branches must remain under game control. The catalog previously excluded this handler because the verified action-exit list covered only field 238.

Aerith (entity 16) and Tifa (entity 15) have empty personal Talk scripts. Their optional conversations instead belong to nearby native interaction lines: entity 8 for Aerith; entities 9 and 10, which delegate through event entity 7, for Tifa. An empty personal Talk script therefore does not establish that these visible companions cannot be approached for a conversation. The verified NPC-proxy mechanism is the appropriate representation, as with existing counter interactions.

Bank 3, address 129, bit 1 enables Aerith's pending conversation; bit 2 enables Tifa's. The respective handlers clear those bits when the conversation starts. Native visibility, model ownership, and LINE state still matter. In particular, Tifa's appearance during a later story scene must not make this old optional conversation available.

The village exit also clears remaining pending bits and restores the corresponding party members when leaving the optional scene (entity 11, script 1, offsets 0–38). The reader follows the native pending bits rather than inventing a separate game-moment restriction.

## Independent checks

A fresh read-only Ghidra pass over the installed legacy executable (SHA-256 `4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225`) confirms the shared TLKON, VISI, LINE, and LINON state semantics. The corresponding handlers resolve to 0x618A80, 0x618A01, 0x6111D8, and 0x6115AD. This supports the shared memory-layout interpretation; it does not claim an interactive x64 playthrough.

An independent geometry probe reads the native incoming gateways from fields 513–524 and the real field-518 walkmesh. All 18 routes pass: six incoming gateways from the jungle and buildings to each of the exit, Aerith interaction line, and Tifa interaction line. This checks geometry without live actor collision state.

Primary format references: [LINE](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes/D0_LINE), [LINON](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes/D1_LINON), and [field triggers](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Triggers). The LINE documentation counts Init and Main separately; the parser stores them together as slot 0, so its slot 1 is S1.

Private logs and licensed field dumps are kept outside the repository. Final in-game confirmation remains with the reporter.
