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
