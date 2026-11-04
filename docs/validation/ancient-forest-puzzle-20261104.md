# Ancient Forest puzzle: evidence and validation scope

## Report and native evidence

The supplied 0.10.8 Steam x64 log reaches the Ancient Forest entrance screen,
anfrst_3 (field 622). It shows duplicate creature names, no carrying/plant feedback
and repeated flytrap bites during automatic travel toward the Spring Gun Clip.
The retained log and full native dumps remain private.

The audit covers fields 620–624 in the installed original and Steam data, with all
five reviewed Echo-S layouts. The native temporary bank identifies the carried
creature and visible plant closures. Pitcher plants shake through their own
background-entity routines. Creature feedback uses current visible model readings.
Neither future timers nor hidden destinations supply puzzle guidance.

Read-only Ghidra analysis of the verified original engine confirms the distinction
between touching and crossing a LINE (00637ABB), and the native JUMP handler
(00615CA3). The original executable SHA-256 is
4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225.
Native field scripts establish the actual throwing zones, per-plant flags, manual
direction tests, jaw contact geometry and existing walk-on crossings.

Echo-S preserves the forest geometry and puzzle banks, while shifting some entities
and moving bite damage to another script. The reviewed entity map omitted two
same-named frogs; their individual native model slots remain distinct. Bite
animations must remain excluded even when the leader's routine no longer contains
the damage instruction.

## Behavior and regression coverage

Shared code supplies both runtime readouts. A failed or mixed sample stays unknown;
it never becomes empty hands or a newly open plant. Current activity speech replaces
expired timed readings and is retired after focus loss, field changes or reset.
Navigation and speech use the same local creature and plant numbers.

When native scripts close a plant and clear the carried creature on successive
frames, speech retains both current facts until they have had time to be heard.
Newer state replaces older state for the same subject. A shaking plant under the
player takes priority over protected speech; leaving it retires that urgency.
Routine direction announcements wait while a current forest fact is still owed,
so running guidance cannot repeatedly cut the same plant warning short. Explicit
commands, arrival instructions and other game speech retain their normal paths.

Focused regressions exercise pickup/release, plant changes, visible shaking,
unreadable state, speech replacement, reset, focus and loaded-script admission.
Native tests bind carried identities and raised throwing spots to the actual
scripts. Route tests cover active-jaw rejection, native Talk reach, real terrain
and retained vine crossings. Native movement replay includes the eight-direction
steering and delayed updates found in the tester's log. Loaded Echo-S tests cover
all three first-screen frogs, bite exclusions and the held Down action on each of
the five layouts.

Release verification uses the project's complete dual-runtime gate, release
contracts and full suites against the exact packaged assemblies and dependencies.
Private release receipts retain artifact hashes, preservation baselines, deployment
backups, publication checks and CI results. Only those recorded results establish
which checks passed; this document describes their scope.

## Limits

The puzzle reader recognizes English stock fields and the five reviewed Echo-S
1.24 layouts. Unreviewed or other-language scripts remain unknown; their temporary
flags are not interpreted as proof that a flytrap has closed.

No live forest traversal, physical controller, audible listening or recipient-PC
test is established by static analysis, fixture replay or package checks. The
movement bound covers native walking/running and the Steam 3x booster; other speed
edits are outside this bound. If another automatic step is unsafe, spoken guidance
continues after an announced manual handoff. The mod does not choose a puzzle
sequence, freeze a timer, place a creature or press a jump/interaction button.
