# Optional interaction route replay

Build with `dotnet build tools/TownInteractionRouteAudit -c Release`, then run:

```powershell
.\tools\TownInteractionRouteAudit\bin\Release\net8.0-windows\TownInteractionRouteAudit.exe '<licensed workingdir>' '<report.json>'
```

This checks `TownInteractionObjectCatalog` using native walkmeshes, gateway and MAPJUMP arrivals, and production script transitions. A passing target has at least one reachable arrival state. The report retains every failed entrance pairing: one passing entrance is not proof that the target is reachable from every disconnected component.

Ancient Forest beehives reached only after a native player jump are reported separately in `puzzleLandingsOnly`. The replay checks the approach after that landing and does not solve the puzzle. Live locks, moving actors, timed inputs and an actual playthrough are outside this audit. Model coordinates use native initialization, except for the explicitly documented frog whose hidden initial location is replaced with its first visible landing.

Run the audit against both archives. Use the shared `--town-coverage-only` tests in both runtime hosts for visibility, Talk/LINE gates, exact native bindings and activation geometry; the route audit does not substitute for those state checks.
