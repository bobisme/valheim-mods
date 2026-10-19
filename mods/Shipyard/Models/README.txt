Shipyard models: a færing (6.0 m four-oared clinker boat) in four build stages, and the slipway it's built on.
Made by yard.py in Blender, baked low-poly by tools/bake (docs/model-baking.md).

  Export (high, untextured; add "render" for previews; 'faering' and 'onslip' are preview-only, not shipped):
    cp mods/Shipyard/Models/yard.py ~/.cache/shipyard/
    blender -b --python ~/.cache/shipyard/yard.py -- [render] <kind>     (writes ~/.cache/shipyard/<kind>.json.gz)
  Bake (what ships):
    blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Shipyard/Models/bake_conf.py <kind>
  kinds: faering_keel faering_ribs faering_strakes faering_fit slipway

Each stage is drawn whole on its own; the stages are meant shown together, each added to the last
(keel, + ribs, + strakes, + fit), all at the same origin.

Materials (slots): 0 pine, 1 oak, 2 tar, 3 iron.

Frames (game: +x starboard, +y up, +z bow):
  Hull pieces: origin amidships on the centreline at the waterline.
    keel bottom y -0.45; gunwale y +0.41 / x ±0.70 amidships; stem tips y ~0.93 at z ±2.95..3.0.
  Slipway: origin on the ground at its centre. Keel-block tops at y 0.55, so a hull sits with its
    origin 1.00 above the slipway's (keel bottom -0.45 on 0.55).

Bounds (x / y / z):                 baked tris
  faering_keel     ±0.048 / -0.45..0.961 / ±3.008       276
  faering_ribs     ±0.685 / -0.343..0.36 / ±1.482       840
  faering_strakes  ±0.73  / -0.362..0.93 / ±2.955      1732
  faering_fit      ±0.716 / -0.631..0.994 / -2.71..1.5  804
  slipway          ±1.87  / -0.006..0.877 / ±4.001      720

Places on the finished boat (hull frame):
  Thwarts (seat centres): (0, 0.043, 0.90) and (0, 0.043, -0.70); each 0.26 deep, gunwale to gunwale.
  Oarlocks (keipr + thole) on both gunwales at z 0.48 and -1.12; oars lie stowed at y ~0.1, z -1.55..1.5.
  Steering oar: starboard quarter, x ~0.44, z -2.25, loom top y ~0.99; tiller reaches inboard to x ~-0.06, y ~0.93.
    Helm (sit to steer): ~(0.15, 0.05, -1.9).
  Boarding: over the port gunwale amidships, ~(-0.8, 0.4, 0) (the steering oar is on starboard).
