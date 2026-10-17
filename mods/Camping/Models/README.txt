The camping kit's looks (Camping 0.1.0+): made from scratch by camp.py in Blender.
Remake: copy camp.py to ~/.cache/camping (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/camping/camp.py -- render [bedroll|leanto|tent]`
(previews <name>_*.png there), copy ~/.cache/camping/<name>.json.gz back here. Material slots (wood, hide, fur, wool, leather, rope, red wool, stone) are
set in Kit.cs Materials: hide and fur use the game's deer and wolf rug textures with their cut-out turned off.
Since the bake (docs/model-baking.md) what ships is BAKED: the script's detailed model stays the source; tools/bake/bake.py with
bake_conf.py makes a low-poly copy and bakes the detail into <name>_albedo/_normal[/_glow].png (256 px); <name>.json.gz is that low copy
(one material, Kit.Baked). From the repo root: blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Camping/Models/bake_conf.py <bedroll|leanto|tent>
then python3 tools/bake/viewer.py ~/.cache/bake/viewer/camping.html ~/.cache/camping/view to compare. The game textures it
lays on come from tools/bake/gametex.py (woodpole deerhide_rug rug_fur rug_wolf).
