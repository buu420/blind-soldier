# Town NPC coverage and the Costa del Sol return ship

The reporting player's harbor log identifies field 441 (`del1`) at game moment 469.
At 20:16:50 UTC the sailor's MESSAGE 60 was spoken, followed by gameplay with
`npcs=0`. Opening NPC navigation at 20:17:47 still announced an empty list.
This is a discovery/label failure: the player had just interacted with a native NPC.

The installed field has six modelled Talk entities: three sailors (14–16), a
Shinra employee (17), a woman (18), and a man (19). Their ordinary quoted dialogue
does not begin with a speaker heading. The previous uncurated-field fallback
required such a heading and otherwise discarded the target.

## Native model and interaction evidence

The PC field header points to section 3 at offset `0x0E`. That section contains an
ordered model-loader table. An actor's `CHAR` argument indexes this table; a name
scan across unrelated sections is not a reliable substitute for parsing it.
The documented record layout was independently replayed against the installed
files, including all 13 harbor models and 14 town-square models.

An independent scan inspected 5,454 native `CHAR` actors, including 104 actors
with empty script entity names. It also checked actors omitted by the old NPC
catalog, since a dog can respond with a bark and a shopkeeper can open a menu
without any MESSAGE opcode. LINE callers require further review: a scene trigger
that happens to call an actor is not automatically a counter interaction.

Ghidra rechecked TLKON, VISI, LINE, LINON and MENU in the supported legacy binary
(`4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225`).
Live visibility, model ownership, Talk and LINE state remain the runtime gates.

References: [field format](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field.html),
[model loader](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Model_Loader),
[TLKON](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes/7E_TLKON),
[LINON](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes/D1_LINON).

## Return ship

In `del1/crew1` Talk, byte 155 deducts 100 gil, byte 171 displays MESSAGE 60,
byte 174 unlocks triangle 133, byte 191 requests `wmJump` script 7 (enabling
LINE 6), and byte 194 locks triangle 132. The boarding script jumps to transport
entry 39. Its line runs from `(-737,672,63)` to `(-737,867,63)`.

Installed-walkmesh replay reaches boarding from the paid-side triangles 133/134.
The log's two native exits but one reachable exit does not establish a broken ship
route: payment changes which side of the dock is open. Entry 39 has no normal room
name, however, so the boarding target fell back to just `Exit`. Its shared label
is now `Board the ship to Junon`; geometry and native activation gates are unchanged.

Licensed field dumps and the friend's full log remain private investigation data.

## Final implementation and verification

The shared catalog now retains modelled Talk actors that respond with sound,
animation, or a menu. Counter targets require an actor model and a native manual
interaction. The same requirement also applies to older inferred proxies: Junon's
parade formation (363:19–27) and the escape scene at 416:7 are not conversations.
The Story navigation data and route planner are unchanged.

Generic labels use the CHAR-selected model together with reviewed script roles.
Specific reviewed rows cover shopkeepers, hotel staff, named companions, and
ambiguous models. Periods remain valid in names such as Mr.Coates, while a heading
must contain a letter and introduce quoted speech. Highwind crew retain a neutral
crew description; the harbor crew remain sailors.

The final independent replay covers 5,454 native CHAR actors, including unnamed
entities and actors absent from the old dialogue-only catalog. Against the shipped
0.5.8 DLL, potential labelled targets increase from 661 to 976: 364 additions,
49 removed false labels, and 72 corrected labels. This replay forces visibility
and interaction availability to test discovery; it is not a count of simultaneously
visible NPCs or a playthrough of every story state. The removed non-debug labels
were attached to scenery, including barrels named Aerith and a chest named Barret.

Eleven newly supported counter targets also pass route checks from eight native
walkmesh positions each. Icicle's shop is tested from the customer component
containing entrance triangle 8; the larger disconnected component is behind the
counter. Live visibility, Talk, LINE activation, and player/object exclusion
continue to determine what appears during play.

Final validation passed the full x86 suite, x64 module and native town suites,
Shared, Parity, and launcher tests, plus 69 release-script checks. The existing
North Corel alternate-route test is sensitive to its 40 ms wall-clock budget under
load; the final quiet full run passed without changing routing code or relaxing
its assertions. Package and publication receipts are retained with the release.

Opcode classification follows the native dispatcher and the
[field opcode table](https://ff7-mods.github.io/ff7-flat-wiki/FF7/Field/Script/Opcodes.html):
A4 is VISI and AB is TURA, rather than animation commands. Native animation
commands, sounds, menus, and script requests supply the non-dialogue evidence.
