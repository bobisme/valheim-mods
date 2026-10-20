Larder's looks (dairy): made from scratch by larder.py in Blender. Pieces: churn, press. Items: butter, cheese, skyr.

Remake the detailed models: copy larder.py to ~/.cache/larder (the flatpak Blender can't read /tmp), then
`blender -b --python ~/.cache/larder/larder.py -- render [churn|press|butter|cheese|skyr]`. This writes previews
<name>_<yaw>_<distance>.png there (yaw 0 = the front) and an unbaked <name>.json.gz.

What ships is BAKED (docs/model-baking.md). From the repo root:
blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Larder/Models/bake_conf.py <kind>
Then compare with python3 tools/bake/viewer.py ~/.cache/bake/viewer/larder.html ~/.cache/larder/view
The game textures that the bake lays on come from tools/bake/gametex.py (woodpole, stone_pile; they go into tools/bake/tex/, which
git ignores). Shipped: <kind>.json.gz, <kind>_albedo.png and <kind>_normal.png, each with one material (no glow).

Baked triangles (budget) and texture size:
churn 1,257 (1,300) 256 px, press 1,424 (1,500) 256 px, butter 224 (250) 128 px, cheese 242 (250) 128 px, skyr 317 (300) 128 px.

Material slots of the detailed model, in this order (sRGB as the game draws them; bake_conf.look):
0 wood (woodpole grain), 1 stone (stone_pile), 2 iron (0.17,0.17,0.18), 3 butter (0.93,0.82,0.42), 4 rind (0.82,0.66,0.35),
5 curd (0.95,0.86,0.55), 6 skyr (0.94,0.93,0.88), 7 blueberry (0.2,0.22,0.45), 8 rope (0.55,0.45,0.3), 9 horn (0.75,0.68,0.55),
10 whey (0.85,0.86,0.7).

Numbers for the C# side. Game frame, metres, y up, +z = the front where the player stands. The piece origin is on the ground at its centre.

churn (a crafting station):
  churn body (staves + hoops + lid)  x -0.19..0.19, y 0..0.93, z -0.19..0.19 (radius 0.18 at the foot, 0.14 at the rim)
  dasher shaft + handle              x -0.09..0.10, y 0.93..1.30, z -0.03..0.02 (too thin for a collider; the body box is enough)
  stool (seat + legs)                x 0.36..0.74, y 0..0.45, z -0.21..0.17 (seat radius 0.17 at x 0.55, top y 0.45)
  dish of butter + paddle on seat    x 0.40..0.61, y 0.45..0.51, z -0.13..0.13
  whole footprint                    x -0.19..0.74, z -0.21..0.18, top y 1.30
  hand to use it: the lid / dasher top at (0, 0.95, 0), handle at (0, 1.27, 0); the butter dish at (0.51, 0.48, -0.035)
  output point on the ground in front: (0.25, 0, 0.45)

press (a cheese press):
  bench top (two planks)             x -0.56..0.70, y 0.41..0.45, z -0.25..0.25
  legs + back rail                   x -0.52..0.66, y 0..0.42, z -0.25..0.25 (the one rail is at the back, z -0.20..-0.15, y 0.16..0.21)
  post + cheeks + foot block         x -0.75..-0.50 (post itself -0.69..-0.56), y 0..0.95, z -0.12..0.12 (post z +-0.07)
  draining board                     x -0.45..-0.05, y 0.45..0.48, z -0.20..0.20
  cheese hoop (staves + iron bands)  x -0.41..-0.09, y 0.48..0.63, z -0.16..0.16 (centre (-0.25, z 0), radius 0.15); curd rim at its foot
  follower + two pressing blocks     x -0.38..-0.12, y 0.60..0.80, z -0.13..0.13
  lever beam                         x -0.70..0.66, y 0.77..0.90, z -0.03..0.03 (pin at (-0.62, 0.85, 0); about 3 degrees down to +x)
  hung stone + rope                  x 0.43..0.68, y 0.49..0.86, z -0.12..0.11 (stone y 0.50..0.69, clear of the bench top)
  spout + whey stream                x -0.29..-0.21, y 0.17..0.47, z 0.20..0.33
  whey bucket                        x -0.35..-0.15, y 0..0.20, z 0.20..0.40 (centre (-0.25, 0, 0.30), whey surface y 0.15)
  whole footprint                    x -0.75..0.70, z -0.25..0.41, top y 0.95
  hand to put milk/curd in: the hoop top, (-0.25, 0.64, 0) (with the beam lifted; over the follower is (-0.25, 0.70, 0))
  hand to take cheese out: the bench end by the hoop, (-0.25, 0.47, 0.22); the whey bucket at (-0.25, 0.15, 0.30)
  output point on the ground in front: (0.15, 0, 0.55) (clear of the bucket at z 0.20..0.40)

Items (lie about the origin; game frame):
  butter  x -0.070..0.071, y -0.020..0.039, z -0.070..0.071 (dish 0.14 across, pat on it)
  cheese  x -0.094..0.093, y -0.040..0.040, z -0.093..0.090 (0.18 across, 0.08 thick, a quarter cut out facing +z/+x)
  skyr    x -0.070..0.102, y -0.030..0.080, z -0.069..0.069 (bowl 0.14 across; the horn spoon sticks out to x 0.10, y 0.08)

Larder's looks (preserves): made the same way by larder.py, and baked the same way. Pieces: smokehouse, saltpan, saltbarrel,
dryingrack. Items: smokedmeat, saltmeat, stockfish, salt. Remake with `... larder.py -- render <kind>` as above (yaw 0 = the front).

Baked triangles (budget) and texture size:
smokehouse 1,849 (1,800) 256 px, saltpan 1,254 (1,200) 256 px, saltbarrel 952 (900) 256 px, dryingrack 540 (900) 256 px,
smokedmeat 232 (250) 128 px, saltmeat 84 (250) 128 px, stockfish 60 (150) 128 px, salt 258 (250) 128 px.

Material slots added after the dairy ones (sRGB as the game draws them; bake_conf.look):
11 soot (0.11,0.1,0.09), 12 sootstone (stone_pile, tinted (0.36,0.32,0.29)), 13 salt (0.9,0.9,0.87), 14 smoked (0.3,0.17,0.09),
15 meat (0.72,0.33,0.3), 16 fat (0.86,0.78,0.68), 17 fish (0.74,0.68,0.56), 18 smudge (0.3,0.24,0.19).
Decals (texture only, no triangles; names start with 'decal'): the soot along the smokehouse's ridge gap and on its sill over the
mouth, the hatch's iron hinge straps, the salt flecks on the salt meat, the string round the smoked meat.

Numbers for the C# side. Game frame, metres, y up, +z = the front where the player stands. The piece origin is on the ground at its centre.

smokehouse (a cooking station: wood in the firebox, meat through the hatch):
  firebox (dry-stone, 3 courses)     x -0.67..0.65, y -0.04..0.47, z -0.66..0.67 (sunk 0.04 into the ground)
  firebox mouth (open)               x -0.21..0.21, y 0..0.31, at the front face z 0.65 (lintel stone over it, y 0.31..0.44)
  stone slab on the firebox          x -0.64..0.64, y 0.43..0.50, z -0.63..0.63
  hut (sills, posts, plank walls, wall plates)  x -0.61..0.61, y 0.49..1.44, z -0.61..0.61
  hatch (frame + door + battens)     x -0.24..0.24, y 0.87..1.31, z 0.58..0.63 (closed: the door is drawn shut)
  roof (planks + barge boards)       x -0.80..0.80, y 1.29..1.86, z -0.77..0.77 (pitched 35 degrees, ridge along z; eaves y 1.34 at x +-0.74)
  ridge cap                          x -0.19..0.19, y 1.79..1.90, z -0.78..0.78 (the smoke gap is under it: x -0.08..0.08 open to y 1.80)
  whole footprint                    x -0.80..0.80, z -0.78..0.78 (on the ground: the stones, x/z +-0.67), top y 1.90
  colliders: base box x/z +-0.66, y 0..0.50; hut box x/z +-0.61, y 0.50..1.44; roof box x +-0.78, y 1.33..1.90, z +-0.78 (or two slabs at 35 degrees)
  hand to put wood in: the firebox mouth, (0, 0.15, 0.62)
  hand to put meat in / take it out: the hatch, (0, 1.09, 0.64) (hatch centre; its latch is at (0.22, 1.10, 0.63))
  output point on the ground in front: (0.40, 0, 1.00) (clear of the stones at z 0.67 and the eaves at z 0.78)
  smoke: out of the ridge gap, along (0, 1.84, -0.70..0.70), middle (0, 1.86, 0); a wisp from the mouth at (0, 0.30, 0.66)

saltpan (a cooking station that stands in shallow sea water):
  hearth stones (two layers)         x -0.54..0.53, y -0.06..0.38, z -0.41..0.35 (sunk 0.06 into the ground, so water round them reads fine)
  firewood gap in the stones         x -0.13..0.16 at the front (z 0.20..0.35), under the pan
  iron pan (tray + lip)              x -0.51..0.51, y 0.38..0.51, z -0.36..0.36 (floor y 0.39, rim y 0.50)
  ring handles at the ends           x +-0.50..0.60, y 0.42..0.52, z -0.03..0.03
  salt crust in the pan              x -0.47..0.47, y 0.39..0.45, z -0.32..0.32 (surface y 0.41..0.43, raked up to 0.45 at the -x end)
  charred sticks + ash bed           x -0.26..0.27, y -0.05..0.13, z -0.15..0.53 (the stick ends stick out of the gap to z 0.53)
  whole footprint                    x -0.60..0.60, z -0.41..0.53, top y 0.52
  colliders: hearth + pan box x +-0.52, y 0..0.51, z -0.40..0.36 (the sticks are too thin to need one)
  hand to put wood in: the firebox gap, (0, 0.12, 0.38)
  output point on the ground in front: (0.35, 0, 0.85) (clear of the sticks at z 0.53)
  smoke / steam: off the pan's surface, (0, 0.46, 0) (spread x +-0.45, z +-0.30); fire smoke out of the gap at (0, 0.25, 0.36)

saltbarrel (a station: salt + meat in, salt meat out):
  barrel (16 staves + 4 iron hoops)  x -0.28..0.28, y 0..0.80, z -0.28..0.28 (radius 0.25 at the ends, 0.275 at the belly y 0.40)
  salt inside                        surface y 0.75 at the staves to 0.785 in the middle (no head: the barrel is open)
  scoop stood in the salt            x -0.21..-0.02, y 0.71..0.98, z -0.18..-0.01 (its bowl in the salt at (-0.06, 0.76, -0.05))
  lid leaning on it (front right)    x 0.02..0.43, y 0..0.46, z 0.15..0.48 (radius 0.24, leaning 18 degrees, turned 35 degrees toward +x)
  whole footprint                    x -0.28..0.43, z -0.28..0.48, top y 0.98 (the scoop's handle; the rim is y 0.80)
  colliders: barrel box x/z +-0.28, y 0..0.80 (or a cylinder radius 0.28); lid box x 0.02..0.43, y 0..0.46, z 0.15..0.48
  hand to put meat in / take it out: the open top, (0, 0.80, 0)
  hand for the salt: the scoop, (-0.06, 0.80, -0.05)
  output point on the ground in front: (-0.30, 0, 0.55) (to the left, clear of the lid)

dryingrack (a stockfish rack, hjell: fish hang on it to dry):
  trestle at -x (two legs crossed)   x -1.21..-0.99, y 0..1.94, z -0.57..0.57 (the legs cross at y 1.65, z 0; their tops at y 1.94)
  trestle at +x                      x 0.99..1.21, y 0..1.94, z -0.57..0.57
  ties under the lower bar           x +-1.10, y 1.19..1.26, z -0.21..0.21 (one per trestle)
  lower bar                          x -1.32..1.32, y 1.26..1.35, z -0.04..0.05 (centre y 1.30; radius 0.042)
  upper bar                          x -1.32..1.32, y 1.70..1.79, z -0.04..0.05 (centre y 1.75, in the legs' crotch)
  brace (on the back)                x -1.14..1.22, y 0.15..1.17, z -0.56..-0.19
  whole footprint                    x -1.32..1.32, z -0.57..0.57, top y 1.94
  colliders: each trestle a box x +-(0.99..1.21), y 0..1.94, z +-0.57 (or two capsules along the legs); the bars as boxes above
  hand to hang fish / take them: the lower bar's middle, (0, 1.30, 0.05)
  output point on the ground in front: (0, 0, 0.90) (clear of the feet at z 0.57)
  the 8 hang points: copies of the stockfish item, hung by the tail in front of the bar. Each is the fish's ORIGIN (its centre):
    lower bar (centre y 1.30):  (-0.81, 1.171, 0.054), (-0.27, 1.173, 0.056), (0.27, 1.177, 0.054), (0.81, 1.179, 0.052)
    upper bar (centre y 1.75):  (-0.81, 1.620, 0.052), (-0.27, 1.619, 0.052), (0.27, 1.615, 0.050), (0.81, 1.613, 0.050)
    (= bar centre y - 0.13, and z 0.05 in front of the bar's centre line; the bars sag and tilt a little, so the y's differ)
  the stockfish's rotation for hanging: Quaternion.Euler(0, 90, 90), i.e. the quaternion (x 0.5, y 0.5, z 0.5, w 0.5). This takes the
    item's +x (to the tail) to +y (up), its +y (its back) to +z (facing the player) and its +z (its width) to +x (along the bar).
    The tail's narrow wrist (item x 0.125) then sits at the bar's centre height, the fin fanned against the bar's front up to about
    bar centre + 0.05, and the cut neck hangs down to bar centre - 0.305. So the upper fish end at y 1.44, clear of the lower bar's top
    (y 1.35). A few degrees of random turn about y (or z) per fish is fine.

Items (lie about the origin; game frame):
  smokedmeat x -0.105..0.129, y -0.042..0.039, z -0.060..0.056 (a haunch 0.23 long: the thick end at -x, the bone knuckle at +x)
  saltmeat   x -0.095..0.097, y -0.021..0.029, z -0.059..0.052 (a slab 0.19 x 0.11, the fat along its +z edge, salt crystals on top)
  stockfish  x -0.175..0.180, y -0.006..0.020, z -0.062..0.062 (0.355 long, 0.12 wide, its back up: the cut neck at -x, the tail fin at +x)
  salt       x -0.064..0.065, y -0.030..0.041, z -0.065..0.066 (a bowl 0.13 across, the salt heaped 0.03 over its rim)

Larder's looks (baking): made the same way by larder.py, and baked the same way. Piece: bakeoven. Items: flatbread, flatbreaddough,
spoiled. Remake with `... larder.py -- render <kind>` as above (yaw 0 = the front).

Baked triangles (budget) and texture size:
bakeoven 1,430 (1,500) 256 px, flatbread 104 (150) 128 px, flatbreaddough 116 (150) 128 px, spoiled 162 (150) 128 px.

Material slots added after the preserves ones (sRGB as the game draws them; bake_conf.look):
19 clay (0.56,0.48,0.4), 20 bread (0.8,0.62,0.36), 21 toast (0.6,0.4,0.2), 22 dough (0.9,0.84,0.7), 23 flour (0.95,0.92,0.84),
24 rot (0.4,0.34,0.27), 25 mould (0.55,0.62,0.45). The oven also uses stone, sootstone, soot, smudge and wood (the peel, the logs).
Decals (texture only): the soot over the oven's mouth and in its vent's throat, the toasted spots on the flatbread, the flour on the
dough, the mould on the spoiled food. The dome's inside is soot; the mouth is a real opening, so what sits on the floor shows through it.

Numbers for the C# side. Game frame, metres, y up, +z = the front where the player stands. The piece origin is on the ground at its centre.

bakeoven (a cooking station with wood fuel, like the game's oven: wood in the fire hole, dough through the mouth):
  plinth (dry-stone, 3 courses on a ring)  x -0.76..0.77, y -0.05..0.43, z -0.74..0.70 (ring radius 0.53..0.73; hollow: the fire chamber)
  fire hole in the plinth's front (open)   x -0.16..0.16, y 0..0.28, at the front face z 0.73 (lintel stone over it, y 0.28..0.41)
  hearth slab on the plinth                radius 0.73, y 0.40..0.462 (x -0.73..0.75, z -0.73..0.74)
  dome (clay, walls 0.1 thick)             radius 0.64 at its foot (y 0.455), crown y 1.06; x -0.63..0.65, z -0.63..0.60
  stones round the dome's foot             radius 0.62..0.72, y 0.445..0.57 (none in front, by the mouth)
  mouth (arched, open)                     x -0.20..0.20, floor y 0.462, springs at y 0.60, crown y 0.80; its outer face at z 0.64
  arch + jamb stones round the mouth       x -0.32..0.33, y 0.46..0.90, z 0.42..0.63
  floor inside the dome (soot-stone)       radius 0.51, top y 0.468 (the dome's inside is radius 0.54 at the floor, 0.43 at y 0.80)
  ledge in front of the mouth              x -0.33..0.33, y 0.40..0.47, z 0.61..0.92 (on the lintel, out over the fire hole)
  smoke vent (clay pot-chimney, back)      x -0.08..0.08, y 0.88..1.07, z -0.43..-0.27 (top centre (0, 1.065, -0.35))
  peel leaning at the right front          x 0.36..0.99, y 0..1.09, z 0.18..0.60 (its blade on the ground at about (0.93, 0, 0.50))
  firewood on the ground at the left       x -1.05..-0.80, y 0..0.19, z -0.23..0.30
  whole footprint                          x -1.05..0.99, z -0.74..0.92, top y 1.09 (the peel's handle; the vent is 1.07)
  colliders: plinth box x/z +-0.74, y 0..0.46 (or a cylinder radius 0.74); dome lower box x/z +-0.62, y 0.46..0.80; dome upper box
    x/z +-0.42, y 0.80..1.06; ledge box x +-0.33, y 0.40..0.47, z 0.61..0.92; firewood box x -1.05..-0.80, y 0..0.19, z -0.23..0.30
    (the peel is too thin to need one)
  hand to put dough in / take bread out: the mouth, (0, 0.60, 0.62)
  hand to add fuel: the fire hole, (0, 0.15, 0.75) (under the ledge's front edge at z 0.92)
  fire: in the fire chamber in the plinth, on the ash under the floor, (0, 0.08, 0.15) (it shows through the fire hole); if the glow
    should show in the mouth too, a second point inside the dome at (0, 0.56, -0.05)
  smoke: out of the vent's top, (0, 1.08, -0.35); a wisp from the top of the mouth at (0, 0.79, 0.64)
  4 cooking slots on the floor inside the dome (the item's ORIGIN: floor y 0.468 + 0.010, the dough's half-thickness; flatbread also
    reaches y -0.010 below its origin):
    (0, 0.478, 0.22) (by the mouth, the one most seen), (-0.22, 0.478, -0.02), (0.22, 0.478, -0.02), (0, 0.478, -0.26)
    (0.18 across each, kept 0.05 clear of the wall and each other; a random turn about y per loaf is fine)
  output point on the ground in front: (-0.45, 0, 1.15) (clear of the ledge at z 0.92 and the peel at x 0.36..0.99)

Items (lie about the origin; game frame):
  flatbread      x -0.112..0.123, y -0.010..0.009, z -0.117..0.107 (two rounds 0.22 across, 1 cm thick, the top one a touch askew)
  flatbreaddough x -0.089..0.090, y -0.010..0.010, z -0.090..0.091 (a round 0.18 across, 2 cm thick)
  spoiled        x -0.065..0.095, y -0.026..0.034, z -0.053..0.058 (a lump 0.13 across, 0.06 high; the bone end sticks out to x 0.095)
