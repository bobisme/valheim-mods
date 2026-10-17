The bathhouse's looks (Bathhouse 0.1.0+): made from scratch by bath.py in Blender.
Remake: copy bath.py to ~/.cache/bathhouse (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/bathhouse/bath.py -- render [stove|bench|bucket|plunge|locker]`
(previews <name>_<yaw>_<distance>.png there, yaw 0 = the front), copy ~/.cache/bathhouse/<name>.json.gz back here. Material slots, in this order:
0 wood, 1 stone, 2 soot, 3 iron, 4 water, 5 ember, 6 rope (the C# side makes them). Wood uvs are in metres with the grain along v.
Numbers the C# side relies on (game frame, metres, +z = front): stove body 0.9 x 0.8 to y 0.75, stones heaped to ~1.05, firebox 0.36 wide
from y 0.12 to 0.44 and 0.35 deep, embers at y ~0.14, hearth z 0.4-0.6; bench seat top y 0.85 over z -0.55..-0.05, footrest top y 0.42
over z -0.05..0.45, frames at x -0.9/0/0.9; bucket water y 0.23; plunge water y 0.80, step top y 0.40 at z 0.75-1.05.
locker chest x -0.55..0.55, z -0.275..0.275, y 0..0.5 (iron proud to x +-0.554, z 0.279, hasp staple to z 0.31), lid top y 0.50 (sit on it) over
z -0.2..0.275; back posts x +-0.48..0.55 at z -0.275..-0.205 up to y 1.6, back boards z -0.275..-0.25; pegs at x -0.32/0/0.32, y 1.33 at
the rail (z -0.22), tips y ~1.37 at z ~-0.04; cloth hems at y 0.70 (tunic, x -0.32) and 0.95 (towel, x +0.32).
Since the bake (docs/model-baking.md) what ships is BAKED: the script's detailed model stays the source; tools/bake/bake.py with
bake_conf.py makes a low-poly copy and bakes the detail into <name>_albedo/_normal[/_glow].png (256 px); <name>.json.gz is that low copy
(one material, Kit.Baked). From the repo root: blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Bathhouse/Models/bake_conf.py <stove|bench|bucket|plunge|locker>
then python3 tools/bake/viewer.py ~/.cache/bake/viewer/bathhouse.html ~/.cache/bathhouse/view to compare. The game textures it
lays on come from tools/bake/gametex.py (woodpole stone_pile).
