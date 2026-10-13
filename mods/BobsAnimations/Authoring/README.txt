Making clips for Bob's Animations, in Blender (no Unity, no asset bundles).

1. Capture a character, posed as you want to start from (for seated clips: someone sitting in a chair): Claude Tools `anim rig seated`
   (or a name or number from `anim chars`) writes BepInEx/claude/shots/anim-rig.json. Copy it to ~/.cache/bob-anim/ (the flatpak Blender
   can't read /tmp) with these scripts.
2. A clip script (sit_drink.py; seated.py holds the rest of the seated set; standing.py the standing set, over a standing capture, one clip per run: `-- <rig> <out> <clip> [render]`) makes
   a Clip (clip.py) and keys it (Clip(..., mirror=True) makes the mirror image of a clip written toward the character's right):
   - turn(frame, {bone: [(axis, degrees)]}): spine, neck, head or shoulder turns, in the character's own axes (X its right, Y its front,
     Z up); exported as turns from the game's own pose, so the seat's posture and breathing show through;
   - hold(frame, side, item, centre, up, toward) or hand(frame, side, ...): the hand placed by IK (hand, forearm, upper arm) so a held
     item sits where you say; the arm's bones are exported as they come out;
   - pose(frame, side, wrist, fingers, palm): the wrist at a point, the fingers pointing one way and the palm facing another;
   - curl(frame, hand, 0..1, Index=..): a grip, finger by finger if need be; prop(item, bone, start, end, swap=): an item in the hand;
   - turn(..., hips=(x, y, z)) with pin_feet(): the hips moved (crouching) with the feet kept planted by IK; exported as a shift.
   hold_item() takes the game's numbers for a held item from `anim hold <who> <item>` (centre and up axis in the hand bone's frame).
3. `blender -b --python ~/.cache/bob-anim/sit_drink.py -- ~/.cache/bob-anim/anim-rig.json ~/.cache/bob-anim render` writes
   sit_drink.json.gz and contact sheets (sit_drink_<view>_<frame>.png, the item as a stand-in cylinder). Copy the .json.gz to Clips/.
4. In game: `anim play sit_drink <who>`, `anim near <who>` (the furniture round a seat, measured in the character's frame), and `anim shot <who> [yaw] [pitch] [dist] [up]` (taken at the end of the frame, as drawn).

Gotchas: worn clothes carry their own copies of the skeleton (attach_skin(Clone)/Armature): bones are found by name in the body's own
skeleton first (the runtime and rig.first_index). Blender's previews draw the stand-in, not the real item: check holds in game. The rig
captured was Rognvald (a man); the male and female bodies share bone frames, so one clip serves both.
Standing captures sway: the game's idle stance is turned and staggered (one foot and hand forward). `anim chars` says who stands still, and
`anim params <who>` should show only onGround (no forward_speed, no statei) when capturing.
Fingers: on this skeleton a finger joint bends toward the palm about its own -x; clip.py's curl starts from straight (rest) fingers.
Arms: placed per frame by two-bone geometry in clip.py (_place_arm), not Blender IK (which flips elbow solutions between frames: the
hand stays smooth but arm and forearm spin, and the game blends each bone apart). Check new clips with a reach/steps script: wrist misses
and per-frame turns of the arm bones (a jump over ~40 deg in one frame is a flip). Aim at the mouth where the head's turn puts it.
