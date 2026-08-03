# Echo-S Opening Pages and Reactor Timer Design

## Goal

Finish Echo-S 1.24 compatibility without changing ordinary FF7 behavior:

1. Speak every Echo-S disclaimer page shown before the opening movie and tell the player that confirm advances it.
2. Restore the first reactor escape countdown to ten minutes when, and only when, a supported Echo-S field script changes it to five.

The implementation remains native-state-backed and fail-closed. Unknown field scripts receive no guessed speech and no timer mutation.

## Verified Runtime Facts

### Opening pages

- Echo-S field 109 displays four blocking `MESSAGE` instructions with message IDs 1 through 4 before the opening movie.
- Runtime traces show those four instructions arriving through the native field-opcode message hook. The older field-message-open compatibility branch is not reached.
- Each page waits for confirm. Therefore every spoken page must end with `Press confirm to continue.`
- Supported Echo-S field 109 is identified by SHA-256 `95D109B176FB1A007A076D5CBFE74BA9E00396A87194BBA552699898E841C57D`.
- The exact reviewed page wording already lives in `EchoSCompatibilityManifest`; no OCR or inferred wording is needed.

### Reactor timer

- The native field opcode table is at `0x009055A0`; opcode `0x38` (`STTIM`) dispatches to `0x0061FCD8` in the supported x86 executable.
- Ghidra confirms the handler calculates `hours * 3600 + minutes * 60 + seconds`, writes the total to `0x00DC08BC`, advances the active script by six bytes, and returns zero.
- Vanilla field 125 sets 10 minutes at byte index `0x011E`.
- Supported Echo-S field 125 variants set 5 minutes at byte indices `0x0089` and `0x0091`, for entity 1, script 0.
- The base and three supported alternate UI fingerprints are already represented by `EchoSCompatibilityManifest.ResolveVariant`.

## Opening-Page Design

The field-opcode message observation is the primary signal because that is the event confirmed in the live trace. The existing field-message-open hook remains a secondary source and routes through the same coordinator to avoid duplicate behavior.

An `EchoSDisclaimerSpeechTracker` maintains at most four pending message IDs per loaded script pointer. It accepts only field 109 IDs 1 through 4. This allows page 1 to survive the short load-time identity race without weakening fingerprint validation.

On each monitor tick:

1. Read the coherent loaded field-script identity.
2. Reject and discard a pending candidate if it no longer belongs to the same loaded script.
3. Resolve speech only if the exact field 109 fingerprint is supported.
4. Speak the reviewed page text followed by `Press confirm to continue.`
5. Mark it delivered only after the speech call succeeds; a failed delivery remains pending for retry.

Once exact disclaimer speech owns a page, generic visible-window speech is suppressed for that page. Unknown fingerprints continue through ordinary fail-open message handling and never receive the manifest wording.

## Reactor-Timer Design

A dedicated x86 cdecl hook wraps opcode `0x38`. The detour is deliberately minimal:

1. Capture the coherent `FieldScriptContext` before calling the original handler.
2. Call the original exactly once so FF7 performs its normal script advancement and timer write.
3. Enqueue the captured context and original result in the existing bounded native-hook event queue.

No hashing, allocation-heavy work, logging, or process-memory mutation happens inside the detour.

The monitor thread applies a ten-minute override only when all of these conditions match:

- field 125;
- entity 1;
- script 0;
- opcode `0x38`;
- byte index `0x0089` or `0x0091`;
- loaded script pointer matches the captured script lifecycle;
- `EchoSCompatibilityManifest.ResolveVariant(identity)` is Echo-S 1.24.

The decision remains pending briefly if identity is not yet coherent during field initialization. It is applied at most once per loaded script pointer. Vanilla byte index `0x011E`, unknown hashes, and unrelated timer instructions are untouched.

The override writes integer `600` to native timer address `0x00DC08BC` through a checked `WriteProcessMemory` wrapper. Failure is logged and remains retryable within the bounded pending lifetime.

## Reset and Failure Behavior

- Field lifecycle reset clears pending disclaimer pages and timer decisions.
- A new loaded script pointer creates a new lifecycle and may be handled once.
- Unsupported or unreadable identities cause silence/no mutation, not a guess.
- Queue overflow follows the existing bounded-event behavior and is diagnosed by existing counters.
- Either feature can fail independently without breaking native message display, movie playback, or the timer handler.

## Tests

Opening-page tests cover exact fingerprint acceptance, unsupported rejection, prompt suffix, all four IDs, deduplication, identity-race retry, failed-speech retry, and lifecycle reset.

Timer tests cover both exact Echo-S offsets, base and alternate supported fingerprints, a 600-second decision, vanilla rejection, unknown-hash rejection, nearby-offset rejection, once-per-script behavior, pending identity retry, and reset/new-pointer behavior. Queue and delegate metadata tests cover the new native event and x86 calling convention.

## Deployment

Build and test the shared dual-runtime package, then deploy it through the existing installer to `C:\Users\buu42\AccessXI\external\Reloaded-II\Mods\ff7.accessibility.reloaded`. Preserve the installed configuration. Do not launch, stop, or focus FF7, 7th Heaven, or Reloaded during deployment.
