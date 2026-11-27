# Russian support for the FFRTT 7th Heaven translation

User confirmed full Russian game text, Blind Soldier menus/navigation, and text audio descriptions, with a local test deployment. No public release is requested in this scope.

The target is ayumosa/ff7-7h-russian 1.02 (IRO SHA-256 `70ED814EC7A287D29E913670D82CAF6B557C3ED5EE8D73067B130FD48A0746AA`). It overlays English-engine archives and Hext menu strings, using replacement glyph slots rather than CP1251. Keep the engine language English and add a separate `ru` text profile, as with Polish. Decode native control codes without changing their meaning. Preserve Latin words and numeric values where the font shares Cyrillic glyphs.

Automatic selection must establish the running translation, not merely an installed IRO. Use coherent, bookended matches of multiple loaded Hext strings at the verified English legacy addresses; explicit `GameLanguage=ru` remains available. Inactive, partial, unreadable or inconsistent matches must not select Russian. Do not probe those addresses in native x64 mode.

Russian own-speech and description catalogs are embedded. Use Prism for every Russian description channel and bypass English recordings. Keep native game dialogue distinct from description-only translations. Align field description triggers against the actual Russian field scripts, with the existing whole-slot equivalence rules and exact prefix identities; do not weaken identity checks.

Evidence: extracted font atlas and translated kernel/menu strings; Ghidra renderer `006E706D` reads the glyph byte, computes `(byte % 21)*24` and `(byte/21)*24`, and splits the texture at x=256 between `00DC100C` and `00DC1010`. Private game assets and IROs stay outside source and distributable packages.

Local installation must preserve saves, settings, description history, other mods, stock AppLoader/ReShade and loader layout. Report static/native fixture proof separately from live Russian gameplay and listener verification.
