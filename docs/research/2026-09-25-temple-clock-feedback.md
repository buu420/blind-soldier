# Temple of the Ancients clock: brief moving and time feedback

The clock room (field 607, kuro_4) used to speak a 25 to 35 word paragraph every 2.5 seconds
while the hands moved: the party's place, all three hands and both bridges. Each paragraph
was queued behind the previous one. At ordinary screen reader rates that is 5 to 12 seconds
of speech per paragraph, so the speech fell further and further behind the hands.

The player asked for clock time, such as "Moving, ten thirty."

## What the clock says on its own

- **"Moving, ten thirty."** or **"Moving, about ten thirty."** while the two hands the player sets are moving.
  - It is said when they start moving, if the clock has been quiet for 1.5 seconds.
  - Then it is said again at most every 1.5 seconds while they keep moving.
  - It is not said while a hand is only settling on its numeral, since the stop is said moments later.
  - "About" means one of the hands is between numerals at that moment.
- **"Stopped, ten thirty."** once both hands have been watched on their numerals, unchanged, for 350 ms. This is also how the clock is first read on entering the room.
- **Nothing** until the hands have been watched long enough. One look proves where the hands are, not that they are at rest: a spin carries the long hand through every numeral. This applies on entering, after a reset, after regaining focus and after a torn look. If a hand changes during that time, the clock is moving. If a hand is between numerals, it is moving too, because every turn in the room's scripts ends on a numeral.
- **"Cannot read the clock hands."** if a hand cannot be read. What was seen before is forgotten, so the next readable look starts afresh, with no stale time, and is not held back by the moving-reading pace.

The second hand never stops, and it is not one of the hands the player sets. It never causes
automatic speech, and it never makes the clock count as moving.

### How the time is read

- **The short hand gives the hour.** Its numeral is the hour.
- **The long hand gives the minutes.** Its numeral times five is the minutes: twelve is "o'clock", one is "oh five", six is "thirty".
- **A hand between two numerals** is read the way a clock face is read, as the numeral it has already passed, and the time is then "about".
- **Only rendered positions are used.** These are the hand positions the game draws on screen (each model's rendered bearing). They are not the script's hour counters (4[226] and 4[228]), and they are not a prediction of where a spin will stop.
- **During a spin** the short hand runs backwards on its own while the long hand runs forwards. The times heard during a spin are what the face shows, not a real passage of time.

## The repeat key

R says everything a sighted player can see of the clock:
- whether the hands are moving, and the time. If they have not been watched long enough to know, it gives the time alone: "Ten thirty.";
- the doorway side or the middle where the party stands;
- all three hands, including the second hand;
- which bridges are open, from the native triangle lock state.

It is only a question: asking it changes nothing about what the clock says next.

Example: "Stopped, ten thirty. You are by doorway ten. Long hand at six, short hand at ten,
second hand at three. The bridge to doorway six is open. The bridge to doorway ten is open."

A bridge is only ever claimed from the actual lock state of its two triangles, never from the
hands' positions or their movement alone.

## The Time Guardian's controls (native scripts)

- **On entering (before the mural):** the session starts with dialog 8: "[MENU] Move it myself / [OK] Spin / [CANCEL] Proceed now!".
- **Move it myself:** shows dialog 9: "[MENU] Go back in time / [OK] Speed up time / [CANCEL] Proceed now!".
  - Holding OK moves the long hand forward one numeral (five minutes) at a time. Holding MENU moves it back.
  - Passing twelve carries the short hand forward one hour, or borrows one back.
- **Spin** shows "[OK] Stop!":
  - The long hand races forwards and the short hand walks backwards.
  - OK slows both to a stop over a few more numerals.
- **Proceed now!** ends the session: "It is time. You may proceed."
- **On the first visit** the hands start at ten ten. After the mural the game sets six o'clock itself and runs no session.

### Hand timing

Every hand turn is one TURNGEN (opcode B4). Its steps operand is the number of native updates
the turn takes, not a speed:
- A manual step takes 6 updates, about 0.2 seconds at 30 updates a second, plus a little script overhead. It is linear forwards and eased backwards.
- A carry or borrow of the short hand takes 10 updates, eased.
- A spin moves the long hand 1 numeral per update and the short hand back 1 numeral per 4 updates, both linear.
- On OK the long hand slows over 8, 8 and 10 updates. The short hand slows over 10, 10 and 10 updates; the last of these (e22 script 5) is eased.
- Linear turns use FUN_006430a4. Eased turns use FUN_006430f2, which reads a cosine table at 0x00908E32 (within 1 of 4096 cos(2 pi k / 256) in the installed ff7_en.exe).

The 350 ms settle time is longer than the pause between steps while a button is held. It is
also longer than any stretch where a fast host samples the same bearing twice in the middle of
a turn.

## Delivery: newest reading only, dialogue first

Both runtimes pass the clock's readings through `FieldActivityLatestLineDelivery`.
- **At most one reading is waiting.** When it is spoken, it is the clock as it is at that moment, and it interrupts an older clock reading that is still playing.
- **Speech that was just spoken is not cut off.** This covers the Guardian's "[OK] Stop!", dialogue and the repeat key. The reading waits while the screen reader reports it is still speaking.
  - If the screen reader cannot report this, the reading waits for about as long as those words take at 180 words a minute.
  - A screen reader that keeps reporting that it is speaking is believed for a ceiling that scales with the line: the words at 100 a minute plus 5 seconds, and never less than 6 seconds. The 34-word repeat line is therefore protected for up to 25.4 seconds and heard to its end, while a stuck device still gives the clock back after that.
  - A short line spoken while a longer one is still protected does not shorten the longer one's wait.
  - Only the speech is protected, not the window. The "[OK] Stop!" window stays open for the whole spin, but once its words have been said the moving readings carry on.
- **Focus and speech settings are respected.** On both runtimes the clock is watched and spoken only while the game has focus. Losing focus forgets its motion and any reading owed. The check is repeated at the moment of speaking, so focus lost in between is not treated as delivery.
- **A muted or refusing speaker is not treated as delivery.** This covers speech turned off in the configuration as well as a speaker that refuses the line. The reading is retried every half second while it is still current, so on unmute the time as it is then is said, once. It is dropped when the room is left, the readout is turned off, or focus is lost.

This applies to the clock only. Every other field activity keeps its host's existing focus
rules, timing and queued delivery. The shared rules live in
`FieldActivityClockHost`, which both runtimes call.

## Limits

- Speech is protected when it goes through the runtime's own speech output. Recorded voice playback is not protected, because it does not pass through the screen reader.
- If a screen reader backend wrongly reports that it is not speaking, the wait for other speech ends after 250 ms.
- Entering the room, or regaining focus, a stopped clock is first announced after the hands have been watched unchanged for 350 ms. Observed movement can be announced sooner.
- The time heard while hands move is a sample. With one reading at most every 1.5 seconds, not every numeral passed during a spin is spoken.
- This has not been checked in a live game session. The tests replay the native hand timing, both linear and eased, through the real readout and both runtimes' delivery paths.
- The tests simplify the timing in two ways. They take the native update as 1/30 s and the pause between script steps as a fixed number of updates. They also approximate when bridges lock and unlock: in Move it myself, after each turn; in a spin, locked throughout. No automatic line depends on the bridges.

## Final verification, 2026-09-26

The complete x86 and x64 suites passed against the published ReadyToRun DLLs, using each
installation's game data. The shared and parity suites also passed. The package validators
confirmed the managed ReadyToRun headers and native dependency architectures. Independent
probes confirmed that an aligned first sample does not claim a stop, and that a repeat still
being spoken after seven seconds is protected.

The verified binaries and corrected Story catalog were installed into both local game
installations, with backups and SHA-256 verification. The other assets and configuration
were preserved. This initial deployment was a local test build; public release packaging is
documented in [the 0.7.2 release notes](../releases/v0.7.2.md).
