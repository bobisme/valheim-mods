Making clips for Bob's Animations, in Blender (no Unity, no asset bundles).

1. Capture a character, posed as you want to start from (for seated clips: someone sitting in a chair): Claude Tools `anim rig seated`
   (or a name or number from `anim chars`) writes BepInEx/claude/shots/anim-rig.json. Copy it to ~/.cache/bob-anim/ (the flatpak Blender
   can't read /tmp) with these scripts.
2. A clip script (sit_drink.py is the example) makes a Clip (clip.py) and keys it:
   - turn(frame, {bone: [(axis, degrees)]}): spine, neck, head or shoulder turns, in the character's own axes (X its right, Y its front,
     Z up); exported as turns from the game's own pose, so the seat's posture and breathing show through;
   - hold(frame, side, item, centre, up, toward) or hand(frame, side, ...): the hand placed by IK (hand, forearm, upper arm) so a held
     item sits where you say; the arm's bones are exported as they come out;
   - curl(frame, hand, 0..1): a grip; prop(item, bone, start, end): an item shown in the hand while the clip plays.
   hold_item() takes the game's numbers for a held item from `anim hold <who> <item>` (centre and up axis in the hand bone's frame).
3. `blender -b --python ~/.cache/bob-anim/sit_drink.py -- ~/.cache/bob-anim/anim-rig.json ~/.cache/bob-anim render` writes
   sit_drink.json.gz and contact sheets (sit_drink_<view>_<frame>.png, the item as a stand-in cylinder). Copy the .json.gz to Clips/.
4. In game: `anim play sit_drink <who>`, and `anim shot <who> [yaw] [pitch] [dist] [up]` (taken at the end of the frame, as drawn).

Gotchas: worn clothes carry their own copies of the skeleton (attach_skin(Clone)/Armature): bones are found by name in the body's own
skeleton first (the runtime and rig.first_index). Blender's previews draw the stand-in, not the real item: check holds in game. The rig
captured was Rognvald (a man); the male and female bodies share bone frames, so one clip serves both.
