# World-map combat resume and story catalog implementation plan

1. Add failing shared tests for native post-battle module `0x11`, permanent-exit rejection, representative full-game Story ranges, and dynamic Key/Diamond objectives.
2. Extend `WorldMapNavigationLifecycle` with the Ghidra-verified post-battle module.
3. Replace the one-off Kalm branch with a data-driven ordered progression table and native live-entity Story targets.
4. Normalize the existing `Ancient Forset` source typo while retaining all other native labels.
5. Run focused and full regression builds, inspect diffs, and deploy both runtime artifacts without replacing user configuration.
