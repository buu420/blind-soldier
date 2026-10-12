# Emerald and keyboard input, 2026-10-11

The user approved repairing visibly rendered Emerald's entity selection and
keyboard feedback, preserving controller input and normal encounter controls,
for both runtimes and release 0.9.5.

## Supplied log

Downloads `ff7_accessibility_steam2026_x64 (15).log` is an exact append of (14).
The 9,631,611 new bytes contain 55,256 lines. Delta SHA-256:
`68b780580e16dc5b3c7cf1bd1020a02889463038921f4fbcf4e2f9808c0f1dfa`.
Startup identifies build 0.9.4 and MVID
`bbe665cb-6bb3-43e6-b21a-9d07f8efd574`, matching the deployed release assembly.
The tester is not being treated as out of date.

Emerald's native patrol is repeatedly rejected as `caller position is from
another frame`. There are brief positive sightings but no recorded successful
controller Events selection of Emerald. The source read entity positions, then
independently captured native draw/camera state and required the former positions
to remain unchanged. This rejects ordinary motion between two valid observations.

The shared visibility reader now validates caller identity/model/topology against
the checked native capture and publishes the capture's fresh world coordinates,
terrain and flags together with visibility. The runtime replaces the older
candidate with that observation. Native hidden/draw flags, exact drawn-position
correlation, duplicate-model, player, fog, projection, curvature and seabed checks
remain in place. No patrol script coordinates or held stale sighting are exposed.

## Native research

Fresh read-only Ghidra decompilation used the licensed legacy executable SHA-256
`4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225`.
Functions 0076328F, 0075E0BA, 0076315C, 00762D74, 0074D50E and 00768A37 confirm
entity placement, per-frame draw reset, native record lookup and packed camera
coordinates. 0041F55E and 0041A21E confirm ordinary game input ownership. Retained
Steam engine decompilation and its validated translated address space preserve
the same guest layout. Both hosts compile the shared world reader.

Primary reference: [native camera and draw loop reconstruction](https://github.com/ergonomy-joe/ff7-worldmap/blob/master/NEWFF7/C_0074C9A0.cpp).

## Keyboard evidence and correction

The original keyboard adapter observes only the current GetAsyncKeyState high
bit on the accessibility worker. That worker sleeps 35 ms after its reader work;
a press during 10-30 ms disappears before samples at 0/35 ms. Independent review
reproduced this against unedited production source; a retained red regression
fails with `expected True, got False`. Controller input is latched separately in
native callbacks. The new observer samples high-bit state independently every
8 ms and retains bounded edges with monotonic two-second expiry. It does not use
the unreliable low-bit history described in [Microsoft's GetAsyncKeyState documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate).

Each capture and consumption checks foreground and coherent native ownership.
Only the active field or world discriminator is read; title/menu/battle feedback
does not depend on inactive globals. Settings ownership is a volatile snapshot,
so settings persistence cannot lock the observer. Context round trips invalidate
the buffer generation. Existing holds at attach/recovery, expired edges, read
failures, suspension and disposal cannot create delayed commands. Multiple
captured commands retain order within navigation, battle, progress and settings
groups, with one command per owner per worker frame. Closed settings consume only
F11. Legacy L uses the buffer at its active owner rather than an unbounded native
navigation pending flag. Empty buffers never fall back to a held physical edge.
Valid pending navigation bypasses scan throttles without consuming commands.
The legacy worker wakes for captured input and re-signals an active ordered
backlog, so coalesced wake signals cannot strand presses behind a polling
interval longer than their deadline. Unused keys cannot sustain a wake loop.
Startup failure disposes the observer before releasing the runtime lease.
Coherent native field activity suppression participates in keyboard ownership.
Blocked field decisions retire the complete navigation group; recovery changes
the generation so late activity presses cannot replay. Settings, R/current
activity readouts and world navigation retain their own ownership, and the
existing mounted-ladder exception is preserved.
World-map windows, scripts and unreadable movement ownership use a separate
world-navigation generation and full-group retirement. Normal field/world
wrappers reject known-busy navigation before toggle/selection side effects;
generic activity readouts and global settings use their own consumers.
Condor cursor/jump actions and submarine selection/stop/pursuit retain their
existing physical rising-edge policy. Their native action phases are not the
field/world ownership token, so completed action taps are deliberately not
banked across those phases. Status/global readouts still use the observer.

The legacy process reader and translated reader use per-call memory buffers and
re-resolve addresses, so the observer does not share mutable native read scratch.
Native DirectInput/SDL callbacks and ordinary game key mappings are unchanged.
Change-only observer state/gap diagnostics and accepted accessibility-key logs
support another report without recording unrelated typing.

## Verification and limits

Moving-Emerald regressions cover a stale caller scan, repeated patrol motion,
fresh Events coordinates, installed-map selection/pursuit, visibility loss,
ordinary-route avoidance and recycled model/player/topology identities. The
focused visibility group contains 28 scenarios in both hosts.

Keyboard regressions cover completed taps, separate repeated taps, held-key
deduplication, focus and ownership round trips, native unreadability, deadlines,
faults, overflow, disposal, command order and F11/settings ownership.
Three repeated taps and mixed navigation commands cover coalesced wake signals
with a simulated 3,000 ms polling interval and a 2,000 ms deadline. A real
observer thread is exercised over deterministic physical-state delegates.
The final release record binds complete suites to the exact published packages.

The supplied log lacks physical keyboard transition telemetry, and the user
cannot distinguish ordinary game keys from mod hotkeys in the report. The
proven tap-loss mechanism is repaired; the report's entire keyboard-silence cause
is not established by this evidence. No live gameplay, hardware input delivery
or speech listening is claimed. The 8 ms observer reduces missed taps but cannot
guarantee subinterval pulses or Windows scheduling latency.
