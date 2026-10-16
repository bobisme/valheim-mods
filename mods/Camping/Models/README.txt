The camping kit's looks (Camping 0.1.0+): made from scratch by camp.py in Blender.
Remake: copy camp.py to ~/.cache/camping (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/camping/camp.py -- render [bedroll|leanto|tent]`
(previews <name>_*.png there), copy ~/.cache/camping/<name>.json.gz back here. Material slots (wood, hide, fur, wool, leather, rope, red wool, stone) are
set in Kit.cs Materials: hide and fur use the game's deer and wolf rug textures with their cut-out turned off.
