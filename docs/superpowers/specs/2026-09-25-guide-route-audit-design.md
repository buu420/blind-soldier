# Guide and route verification

The user requested a start-to-finish review of Absolute Steve's walkthrough,
including optional items, NPC approaches, exits and previously checked areas.
Two reported failures anchor this pass: Added Effect in the Cave of the Gi and
talking to Don Corneo in his room. Finding an entry in a catalog is insufficient;
the player must be able to approach and activate it using a valid native route.

Use the walkthrough as an independent checklist and the installed English PC
archives, native scripts, Ghidra evidence and current logs to establish geometry
and state. Keep downloaded guide text and extracted game data outside Git.
Only original audit code, concise findings and source citations belong here.

Extend the existing audit approach in three layers:

1. A chapter checklist covers the full main walkthrough and its optional-route
   chapters, with native fields and concrete interactions accounted for.
2. A route matrix checks every native arrival separately against authored object
   and Story approaches and discovered NPCs. It preserves unavailable model
   positions, conditional branches and required detours as explicit review cases.
   An aggregate success must not conceal failed arrivals. Static geometry is
   reported separately from supported live conditions and interaction behavior.
3. Confirmed defects receive reproductions against 0.7.0, native-backed repairs
   and regressions in both runtime assemblies. Final checks use packaged DLLs.

Respect current game state, visibility, collision and input ownership. Do not
solve puzzles, reveal hidden future objectives, alter saves or invent approach
coordinates. Normal controller and keyboard behavior remain supported. A useful
cross-room approach must identify the actual exit and return entrance, including
the original target and the conditions that make that detour available.

Claude owns the two initial runtime repairs and focused tests. The primary agent
owns the independent guide ledger, broad audit, review and final deployment.
Additional confirmed failures are pursued through the same process. There is no
new release request in this turn; keep the published 0.7.0 assets immutable.

Live playability and timing are stronger claims than static script or route
replays. Record exact limits and unresolved states rather than counting them as
passed. The aim is to remove gaps proactively, including gaps in the verification.
