# Postscriptthree Gameplay Tweaks compatibility

Blind Soldier 0.9.9 adds checked readers for Postscriptthree's Gameplay Tweaks
1.78, released July 3, 2026. The verified native driver is FFNx 1.24.3. These
gameplay patches are installed through 7th Heaven on the legacy/2013 edition.
The native Steam 2026 edition retains its existing accessibility controls and
stock behavior; this update does not install 7th Heaven patches into it.

Use Postscript's normal configuration and input controls. Blind Soldier detects
the relevant native patch witnesses automatically. Its existing navigation,
party readouts and spoken settings controls remain available. It does not
choose gameplay options or change the 7th Heaven profile.

## Added presentation support

- **Two Accessories:** Equip has four categories and Status reads both accessory
  slots. Comparisons follow the actual displayed totals when Fix Equip Display
  is enabled.
- **Multi Linked Slots:** weapon and armor sockets use the native linked groups,
  including empty sockets and the separate 3/3/2 groups.
- **Battle PHS and Limit Overhaul:** announce the actual command labels, selected
  character or learned Limit, and the native availability state. Normal game
  buttons select, confirm or cancel. No gauge cost is invented.
- **Battle help:** speak the HP/level help, Weak/Resists/Absorbs/Immune and Status
  Immune labels, and steal/morph information only when the recognized patch has
  actually drawn them for the current target. The author’s Sense requirements
  still apply.
- **Overridden text:** selected item, ability, equipment, Materia and command
  names/help come from FFNx's loaded text sections. If a recognized native text
  owner or selected entry cannot be verified, stale disk text does not replace
  it. Other FFNx binaries require new native verification.
- **Character Order Randomizer:** dialogue name tokens and navigation follow
  the remapped roles and current party. Already expanded dialogue and party-slot
  names are read directly. Original fixed field models and movies keep their
  visual descriptions. The altered opening field has a separate checked identity
  and description cue positions.
- **Item Randomizer:** visible uncollected targets say “Item pickup” or “Materia
  pickup”. The native received message reveals the actual reward after normal
  interaction. The mod never predicts a seed's hidden reward.
- **Materia Drops:** verified IDs 320–415 resolve to the loaded Materia names
  and remain part of the full battle results, alongside experience, AP and gil.
  Stock reward validation retains its original bounds when the patch is absent.

## Option audit

The 1.78 archive contains 42 actionable options and seven heading entries. This
table records how accessibility follows each option. It is a native/source
audit with focused regressions, rather than proof of every option combination
in live gameplay.

| Options | Information and control coverage |
| --- | --- |
| VanPlus, TheWorks, RandoChallenge | Presets use the coverage of their underlying options. |
| BattleRando, DoubleBosses, PartyRandomizer, SoloChallenge | Actual loaded battle actors, names, party and visible target masks. |
| ItemRando, SACCompat | Generic pre-receipt pickups, current loaded shop rows and native reward messages. |
| CharOrderRando | Verified role names, native party ownership and remapped field-routing conditions. |
| TrueWait, TurboTurns, SpeedMult, StatusSpeedMult | Actual native readiness, displayed status and action progression; normal input. |
| BDL, StatMult, FixedStatGains, LevelCap, FixOverflow | Current displayed HP, damage, stats, levels and results. |
| MLS, MLSEnabler, MorePairs | Native slot groups, overridden Materia help and current commands. |
| BattlePHS | Native battle command and current character selection/availability. |
| LimitOverhaul | Native learned Limit selection and current gauge availability. |
| TwoAccs, MagicDisplayFix | Both equipment slots and native displayed comparison totals. |
| ControlSeph | Loaded kernel and actor values; the author's Echo-S restriction remains. |
| CAE | Current native action/result presentation. |
| Sense, ShowWeak | Completed native help and effectiveness draws; native visibility requirements. |
| AnimationSkip | Native active animation state; skipped presentations do not trigger narration. |
| DialogueMash | Actual visible windows/pages and ordinary game input. |
| EXPShare, APPlus | Actual displayed results, saved growth and loaded Materia help. |
| PartyLeaderOverride | Actual world player entity, camera and terrain ownership. |
| NBM, MusicFix | Original/native soundtrack choices and accessibility audio controls. |
| StealModifier, MorphMult, MorphAsWell, MateriaDrops | Actual displayed loot help, results and received item/Materia names. |
| FixWItem | Current item selection, quantity, cancel and targeting state. |

## Echo-S and upstream restrictions

Echo-S 1.24's five reviewed field variants retain their existing compatibility
checks. Postscript's altered opening field is accepted separately by exact loaded
script identity. Field name/role compatibility does not rewrite Echo-S voice,
fixed models or movie identities.

The author marks Control Sephiroth incompatible with Echo-S and other edited
kernels. The author also documents Character Order Randomizer crashes in some
Kalm configurations and Animation Skip/Sneak Attack issues. Those gameplay
restrictions remain; accessibility readers do not repair the underlying mods.
Character Order and Party Leader Override also share some patch addresses.

Author documentation: [current 1.78 page](https://www.nexusmods.com/finalfantasy7/mods/82)
and [earlier author thread](https://forums.qhimm.com/index.php?topic=21592.0).
The supplied 1.78 archive, its option XML and native code were the authority for
this audit. No Postscript or Echo-S game assets are redistributed by Blind Soldier.
