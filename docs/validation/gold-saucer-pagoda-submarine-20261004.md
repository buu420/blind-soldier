# Gold Saucer, Pagoda, controller shortcuts and submarine

This is a local candidate after 0.8.2, without a version bump or public release.
Evidence comes from the tester's cumulative x64 log through October 3, the
licensed field archives, and cached Ghidra output for both supported executables.

## Changes and evidence

- **GP exchange, field 496:** `ayasii` and `kawari` draw WNUMB amount/cost in
  windows 0/1. Both hosts now report those numeric windows as the amount changes,
  using latest-value delivery rather than queueing old quantities.
- **Battle Square between rounds:** native menu 0x1C, phase DC209C, cursor DC2228,
  and BP DC094C. Shared reader follows GREAT, Continue/Quit, slot start and the
  stopped handicap. Both hosts poll independently of combat victory suppression.
  Native references: 006DAA08, 006E3C9C, 7ff70274a210, 7ff702779b90.
- **Registration:** the x64 ASK keeps its already-visible GP companion window
  instead of repeatedly treating it as a new page. Party-slot tokens F3-F5 resolve
  through current savemap names. The legacy native ASK and cursor paths also use
  the checked live message reader before the archive fallback.
- **Battle Assist:** supported native shortcut action 15 maps to logical pad
  button 1, SDL R3, and toggles on release. The installed navigation hook now
  reserves R3 even during busy/unavailable contexts or another pad's ownership.
  This also reserves native R3 chords; F1/F2/F9 and native Boosts settings remain
  available. References: 7ff7016adee0,
  7ff7016d1330, 7ff7016d1f20 and shortcut table RVA 1649A90.
- **Submarine x64:** connects the existing shared native instrument/visible-marker
  reader, protected lock sound and K repeat to the host. Module 10 releases field
  navigation input ownership. Readout state resets on leaving the mission.

The SDL seam uses the documented [button getter](https://wiki.libsdl.org/SDL2/SDL_GameControllerGetButton)
and [controller button enum](https://wiki.libsdl.org/SDL2/SDL_GameControllerButton).

## Pagoda audit

The supplied log's Pagoda sessions predate the current catalog; none occur in the
October sessions. Field 586 was checked across all five opponents, floor byte
15[138], victory bits 15[139], native stair locks and same-field stair transitions.
Existing route tests cover the actual walkmesh and each floor's locks. New tests
cover loss flags and Godo's reset to the courtyard. No current route defect was
reproduced. A new failure on the current build needs a log from that attempt.

## Remaining investigation

The post-submarine dialogue freeze is not a confirmed fix. The user reports both
keyboard and controller Confirm failing after Cloud's failure line (field 406,
dialogue 85). Native script flow requires ordinary message advancement. The old
log's owner value 13 identifies Cloud; it does not identify the window phase.
A bounded, read-only return trace records phase, flags, native Confirm edge and
held masks, the raw Confirm flag, Cloud's script position, MESSAGE callback
sequence and navigation ownership for a fresh attempt. It arms across an
intermediate loading module and includes a one-second heartbeat. No auto-confirm
or control-flag writes were added.

The local native Steam installation lacks FFVII.exe and SDL2.dll. Static checks
use the saved matching licensed executable; they are not an in-game test. Real
SDL controller integration therefore remains a tester check.
