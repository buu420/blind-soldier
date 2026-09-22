# Native field interaction inventory

Build with `dotnet build tools/FieldInteractionAudit -c Release`, then run the generated executable with the licensed game's `workingdir` and a private output directory. The export reads the installed archive through the production decoder and records entities, opcodes, dialogue references, models, gateways, and current catalog bindings for every readable field.

The output contains game dialogue. Keep it outside the repository and release packages. An exported model label assumes an enabled, visible actor; it does not prove that actor is available in the player's current state. Models without `CHAR` are not evidence of an NPC. Automatic scene callbacks are not player actions.

Use the inventory to inspect native Talk/Confirm handlers and local flags before adding a target. Then add a regression to the shared test suite and run `tools/Test-StoryCoverage.ps1` against both installed archives. Required story actions belong in the region scripts; optional background interactions belong in the object catalog.
