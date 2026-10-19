Homespun's looks (Homespun 0.1.0+): made from scratch by spun.py in Blender.
Remake: copy spun.py to ~/.cache/homespun (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/homespun/spun.py -- render [pool|brake|loom|table|retted|line|wool|yarn|linenbolt|woolbolt|band|spindle|undertunic|tunic|dress|trousers|cloak|nettle|nettleplant|vat|lichen|lichenplant|frieze|hanging]`
(previews <name>_<yaw>_<distance>.png there, yaw 0 = the front). Material slots, in this order: 0 wood, 1 stone, 2 water, 3 flax,
4 linen, 5 wool, 6 iron, 7 red, 8 blue, 9 retted, 10 oat, 11 darkwool, 12 cloakwool, 13 bronze, 14 nettle, 15 lichen, 16 xanthoria, 17 weld (Looks.Slots makes them, for a
model not yet baked). The garments (undertunic tunic dress trousers cloak) are their folded looks on the ground and in the bag; worn, they're the
game's meshes with recoloured pictures (Looks.Recolour), in Chain.Garments' colours, which bake_conf's oat/darkwool/cloakwool match.
Numbers the C# side (Kit.cs's colliders and switches) relies on, game frame, metres, +z = front:
pool trough x -1.22..0.72, z -0.47..0.47, walls to y 0.52, water y 0.40; drying rack x 0.8..1.3, z -0.42..0.42, to y 1.24.
brake bed x -1.0..0.4, z +-0.09, blades to y 0.82, the hinged jaw raised 11 degrees about (-0.95, 0.84); hackle post x 0.75 (to y 0.9);
basket at x 0.3, z 0.45, rim y 0.2. loom: uprights x +-0.8 to 2.0, leaned back 12 degrees about x (top towards -z), weights about
y 0.4, struts back to z -0.95. table top 1.4 x 0.7 at y 0.8. Items lie about the origin; the spindle's shaft runs along y, the
hand at the origin (Things.cs gives it an equipoffset turned -90 about x, as Godforged's hammer).
vat (0.2.0): stave tub, outer radius 0.425 at its foot (y 0.09) to 0.40 at the rim (y 0.66); inside 0.375 at the rim, floor at y 0.13;
NO dye surface in the model: the C# draws a tinted disc at y 0.52 (inside radius there about 0.38). Three flat stones under it out to
radius 0.47; the paddle leans out over the front-left rim (top at x -0.3, y 1.12, z 0.3); a wool cloth hangs over the rim on +x (out to
x 0.455, down to y 0.27). lichenplant: a lichen-crusted stone, x -0.31..0.26, y -0.09..0.37 (sunk), z -0.25..0.26; the crust patches
are decals (baked into the stone's colour). lichen: the item, about 0.13 across, y -0.01..0.04.
Tapestries, hung as the game's banners are: the origin is the top centre of the rod, the cloth hangs down (-y), width along z, the picture
faces +x. frieze (Oseberg-style procession): x -0.03..0.03, y -0.58..0.026, z -1.613..1.613 (cloth 3.0 x 0.55 from y -0.03; rod and knobs
to z +-1.61). hanging (Overhogdal-style rows of beasts): x -0.03..0.03, y -1.83..0.026, z -0.812..0.813 (cloth 1.4 x 1.8, softly folded).
Their figures are "weave" decals laid on the cloth's facets (spun.py's Weave), baked into its picture; slot 17 weld is their yellow.
What ships is BAKED (docs/model-baking.md): from the repo root,
blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Homespun/Models/bake_conf.py <kind>
then python3 tools/bake/viewer.py ~/.cache/bake/viewer/homespun.html ~/.cache/homespun/view to compare. The game textures it lays on come
from tools/bake/gametex.py (woodpole stone_pile).
