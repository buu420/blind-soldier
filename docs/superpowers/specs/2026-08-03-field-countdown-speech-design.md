# Field Countdown Speech Design

## Goal

Make every visible FFVII field countdown communicate the same information a sighted player receives without reading the changing clock every second.

## Chosen design

Use the game's native field-clock state, gated by a coherently visible WSPCL clock window. Do not run an independent wall clock. This keeps announcements aligned when the game pauses, changes speed, switches fields, or resets a timer.

The shared countdown tracker will be used by both the legacy x86 and Steam 2026 x64 backends. Ordinary dialogue dispatch will ignore the changing timer window so it cannot speak every second or displace real dialogue.

## Announcement schedule

- At every whole-minute boundary while at least two minutes remain, including 2:00.
- Then at 1:30, 1:00, and 0:30.
- Then at 0:15.
- Then every second from 0:10 through 0:00.

Minute announcements use natural phrases such as `9 minutes remaining`, `2 minutes remaining`, and `1 minute remaining`. Mixed thresholds use `1 minute 30 seconds remaining`. The final ten seconds use short numbers (`10`, `9`, and so on through `0`) so Prism can keep pace.

If polling crosses a threshold without observing that exact second, emit only the most urgent crossed threshold. Never queue several stale announcements. Repeated snapshots, pauses, or an unchanged timer do not repeat speech. A timer increase or a newly visible timer starts a new lifecycle.

## Evidence and alternatives

Makou Reactor defines STTIM as hour, minute, and second inputs and WSPCL type 1 as the field clock display. The existing mod already writes FFVII's native countdown value when restoring Echo-S's reactor timer.

Alternatives rejected:

1. Parse rendered clock text only. Simpler, but special numerical displays and localization can make text less reliable.
2. Start a separate wall-clock timer when STTIM executes. This drifts whenever FFVII pauses or changes its timer state.

## Safety and tests

Fail closed when the native timer value or visible clock ownership is incoherent. Test the complete threshold sequence, skipped thresholds, pause/deduplication, resets, non-countdown clocks, dialogue filtering, and parity through both runtime test suites.
