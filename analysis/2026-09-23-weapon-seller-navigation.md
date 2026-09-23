# Weapon seller navigation

The new x64 user log (23 September, 07:44–08:53 UTC) reaches the optional
weapon seller's house, field 79 `zz2`, after successfully boarding the Tiny Bronco.
At 08:44:52 Story is empty; the NPC target is called Man. This is separate from
Gongaga village's weapon shop.

## Seller versus reward box

Native entity 4 `m` has a full Talk script. Its Init disables Talk and visibility
only when GameMoment is below 566. The counter-proxy extractor sees that conditional
TLKON and finds entity 5 `l1`, whose Confirm handler requests the same actor's scripts
5 and 7. Those are permission/refusal and reward handling, not the actor's Talk.
Consequently the shipped NPC reader targets the upstairs box at (-466,-88,240)
even when the live seller is standing downstairs at (-176,-12,0).

The NPC reader now gives a dialogue-bearing, currently enabled native Talk precedence
over an inferred line that requests a different interaction. A LINE requesting the
actor's own Talk script remains a conversation counter, even when personal Talk is
enabled. Reviewed manual proxies, actors with empty personal Talk scripts, and
shopkeepers whose personal Talk is disabled retain their counter behavior.
Visibility remains required. Entity 4 is labeled Weapon seller.

## Story and optional interactions

The Story generator incorrectly classified `zz2` as a dialogue-test room. Its real
world entrance and native gateway to field 10 make this a playable location.
The authored regional source now supplies the real conversation after the automatic
introduction, from moments 566 through 608, and the real exit from moment 566 onward.
The conversation and exit share priority because asking each question has no native
persistent completion flag. This optional detour does not block the way out.

The two reward boxes and bed are independent Objects, using their native LINEs:

| Entity | Interaction | Native line |
|---|---|---|
| 5 `l1` | Small box upstairs | (-467,-127,240) to (-465,-48,240) |
| 6 `l2` | Large box downstairs | (310,-19,36) to (141,-29,36) |
| 7 `l3` | Bed | (-13,84,36) to (192,90,36) |

The boxes require Bank11[132] bit4, set after trading Mythril. Either reward script
clears it, removing both choices. Labels do not reveal the contents before opening.
The bed retains its native rest confirmation. Navigation never confirms these choices.

## Verification

The shipped x64 DLL reproduced the upstairs-box NPC target; the corrected build
produces the live NPC position and follows subsequent changes to it even when the
unrelated box LINE is disabled. Regression checks cover the Story moment range,
introduction flag, reward permission, mutual withdrawal, visibility and native LINE
bytes extracted directly from the installed field archives.

Route checks use all 65 floor triangles connected to Cloud's native entrance triangle
28, for the seller, exit, both boxes and bed: 325 routes per tested archive. The other
eight triangles are three disconnected furniture-top components and are not part of
the walking floor. These checks validate static routes; they do not substitute for
live player testing of the wandering seller or dialogue.

Related world-map entrance and Tiny Bronco evidence is recorded separately with
the world-map correction.
