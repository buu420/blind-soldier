# Parked submarine navigation evidence

Confirmed scope: list the usable parked submarine in Transportation and reuse
Highwind landing followed by an on-foot approach. Landing and boarding remain
the player's normal Cancel and Confirm. Both runtimes use the shared flow.

Fresh read-only Ghidra evidence from licensed ff7_en.exe SHA256
4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225:
model 13 mask at 0096DE18 is 00 18 3C 7E 7E 3C 18 00. FUN_00762A21 tests
8x8 masks over 256-unit cells, within 1024 wrapped units. FUN_00762993 records
the contacting entity at player+4; FUN_0076420A uses it for Confirm. Installed
wm0.ev 4D04 permits ENTER VEHICLE only for player models 0, 1 or 2.

The existing Highwind planner reproduces grass landing and disembark ground,
checks drift and live rotation, and proves a route on foot. Its flight endpoint
is the landing spot, so catalog refresh must preserve the landing phase unless
the submarine's X/Z actually moves. On-foot arrivals use contact points; another
vehicle gets no submarine boarding approach.

Primary reconstruction cross-checks:

- https://github.com/ergonomy-joe/ff7-worldmap/blob/master/NEWFF7/C_00760FB0.cpp
- https://github.com/ergonomy-joe/ff7-worldmap/blob/master/NEWFF7/C_007663E0.cpp

Installed wm0 progress 2 has three dock areas. Cases at (15,18) and (20,18)
prove a grass landing, manual landing prompt, continued walking to native
contact, and route stop after boarding. Dock (12,17) has no grass in its boarding
ground's walking component and must give a truthful Highwind refusal. This is
the game's terrain limitation, not a license to invent a landing.

Shared regressions also cover on-foot paths, recorded versus stale contact,
flying non-arrival, hidden/skipped/ridden entities, disappearance, open water
without a shore, other vehicles, and exclusion from underwater Transportation.
The full surface/underwater navigation suites preserve 0.8.6 steering behavior.
No live gameplay, physical controller or real native hook installation was
exercised. Static legacy evidence and shared x86/x64 automated tests do not
establish live native Steam behavior.

Private research and red/green logs remain outside the shipped package under
C:\Users\buu42\Documents\FFVII-ActiveBuild\submarine-transportation-20261006.
