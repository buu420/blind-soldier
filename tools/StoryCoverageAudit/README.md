# Story verification

Run `tools/Test-StoryCoverage.ps1 -GameRoots <licensed workingdir>,<second workingdir> -OutputDirectory <report directory>` from PowerShell. This builds and runs each shipping assembly against each archive. The ordinary CI job also runs portable Story regression tests in both assemblies.

The audit rejects unreadable authored fields, wrong field identities, impossible moment ranges, nonexistent entities, invalid triangle references, unsupported flags, and trigger lines that disagree with native LINE instructions. Explicit checkpoints test local flags, party membership, completed objectives, and branching decisions. Selected checkpoints pin native opcode bytes as well as the expected spoken target. The broad shared regression suite and Nibelheim route replays run alongside the audit.

Add checkpoints from a user log or independently reviewed native script, with evidence and expected labels. Do not generate the expected labels or flags from the catalog being tested. Negative states and completed steps matter as much as first arrival. A checkpoint document with no expectations is rejected.

Reports distinguish these evidence levels:

- Native inventory: every readable field and the authored story/object references.
- State replay: only the named checkpoints, through the production reader.
- Route replay: the native test suite and checkpoints with `routeArrivalFromFieldId`. The latter resolve actual incoming gateways/MAPJUMPs, replay the active target at each arrival, and require a static walkmesh route. `expectedDestinationFieldIds` pins the target's native outgoing destination, including the field variant. These routes do not model moving NPCs or script-controlled locks.
- Live play: requires a separate gameplay log; these tools do not drive the game.

Passing does **not** prove that every scene, side interaction, route, or chapter is playable. Unlisted progression states remain unverified. Manual reward/menu candidates need human review because they may be scene callbacks rather than visible interactables. Automatic events and unclassified events must not be represented as required player actions.

`reviewed-transit-checkpoints.json` covers independently traced gaps between milestones: Coral Valley, Forgotten Capital detours and the Key return, Highwind departures, and the Junon underwater-reactor corridor chain. The verification script runs both checkpoint files against both assemblies and archives. Forbidden-only cases test chapter boundaries without requiring unrelated valid targets to disappear.

`Generate-FieldStoryEvents.ps1` also writes a `.coverage.json` extraction ledger (or the supplied `-LedgerPath`), recording unresolved writes and any old rows retained for unreadable cloud files. Retained rows are explicitly unverified. Regeneration tests ensure they are present in the output, not merely reported as carried forward.
