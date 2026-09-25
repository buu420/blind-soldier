# Dot-sourced by Generate-FieldStoryEvents.ps1 after the reviewed regions.
#
# Calling the dolphin again at the Lower Junon beach, ujunon2 (field 429), on any visit
# after the first one.
#
# On the first visit Priscilla hands over the whistle and the ride is hers to offer (the
# Story rows at 394..405 and ujunon3's launch point). Coming back later, nobody says a
# word about it: the only thing in the field that knows the dolphin is still there is ad
# (entity 3), whose Main polls every frame, read from the installed archive:
#
#   16200000F0030384      IFSW 2[0] < 1008        the whole poll is skipped from 1008 on
#   16200000D501047C      IFSW 2[0] >= 469        and before 469 (the first visit's own)
#   B9060506              GETAI 6[6] = cloud's triangle (entity 5, PC 0: the leader)
#   31800072              IFKEYON 0x0080          a fresh [SWITCH] press
#   F100930140 / 0305CD   SOUND, REQEW cloud 13   the whistle is blown wherever it is
#   1660060019000062      IFSW 6[6] == 25         but the dolphin only comes to 25
#   3301 ... 48050020020308  UC 1, then ASK dialog 32 "Jump up to the cargo ship on the
#                         dolphin?" Yes/No into 5[8]
#   145008020020          IFUB 5[8] == 2 (Yes)    BITON 3[207] bit 7, MAPJUMP 38 (wm37)
#   on No                 IDLCK 160 and 159 back on, UC 0: the party is left where it was
#
# drctr (entity 2) locks triangles 159 and 160 on every entry once 2[0] >= 388
# (1620000084010409, 6DA00001, 6D9F0001), and the dolphin comes up on 160 (iruka
# script 6 moves it to (-407,343)), so triangle 25 is the water's edge beside it. Dialog
# 31, "[SWITCH] Blow the whistle to call the dolphin.", is only shown by Priscilla's Talk.
#
# The row finishes on triangle 25 itself (the navigation assistant has no radius for a
# completion triangle), at its centroid. Nothing here presses [SWITCH] or answers the
# question: both are the player's, as they are for anyone who can see the beach. A ride
# changes no GameMoment, so the row stays for the next visit.
Add-Definition -FieldId 429 -FieldName 'ujunon2' -Kind Location `
    -Label "Stand at the water's edge and press Switch to call the dolphin (optional)" `
    -X -553 -Y 566 -Z -10 `
    -MinimumGameMoment 469 -MaximumGameMoment 1007 -Priority 1 `
    -CompletionPlayerTriangles @(25) `
    -EntityName 'ad' -ScriptType 'Main'
