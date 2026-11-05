# Emerald tracking and Key to Sector 5 Story guidance

The user approved release 0.10.10 and explicitly corrected Emerald's earlier sighting requirement: Emerald must be trackable whenever the native underwater game presents its live entity, including before any sighting, so the player can deliberately approach or avoid it. This supersedes the visible/last-seen-only tracking policy from 0.10.8. Render visibility still determines whether speech can truthfully say Emerald is visible.

The Key to Sector 5 needs a Story objective after the lost-key clue from the man at Midgar's entrance is established. It follows the native excavation activity and result collection, and retires when the key is acquired. The native scripts must establish the actual clue/state conditions; a guide's suggested conversation order is not proof of a native flag. Existing saves must receive an honest path to establish the clue if it cannot be recovered from native state.

Both fixes use the existing controls on legacy x86/7th Heaven and Steam x64, including all five reviewed Echo-S layouts. Digging choices, climbing, confirmation, reward collection and encounters keep their native behavior. No automatic Confirm, game-state/quest-flag writes, reward changes or guaranteed random result is authorized.

## Emerald behavior

- A single coherent live native Emerald entity supplies a current destination even if never drawn, out of view, fogged or behind terrain.
- Hidden patrol movement updates the destination; a retained old sighting must not masquerade as a live tracked position.
- Automatic pursuit follows the current position after selection. Camera/render loss alone does not interrupt it.
- Ordinary submarine routes avoid the same live Emerald position; selecting Emerald deliberately permits the existing approach behavior.
- Unreadable or inconsistent state releases input, and retirement, defeat, map change or a new world/save cannot retain a stale contact.
- Other underwater discoveries and visible-sighting announcements keep their existing rules.

## Key behavior

- Establish the clue through the verified native condition or an observation of its actual dialogue if the conversation has no native flag.
- Offer a named Story objective through the relevant foreman, active dig and pending results stages, without promising a random outcome or labelling an unopened unknown reward as certain.
- Retire the objective after native key ownership or the completed one-time reward, including when acquired outside the mod's route.
- Keep Objects and ordinary excavation controls useful alongside Story, without duplicate or contradictory objectives.
- Check stock and all five Echo-S variants directly, and keep unknown script layouts conservative.

## Delivery

Work in the single canonical Dropbox checkout on fix/emerald-key-story-01010. Preserve the released 0.10.9 source, saves/settings, recordings, unrelated mods and licensed native files. Use independent Claude Opus 5.5/max review, appropriate red/green and full package tests, Ghidra/native evidence, guarded deployment, GitHub and both Mod Manager records. Distinguish automated/native/package proof from live traversal and audible testing. Remove task-owned temporary builds after retaining the final evidence.
