Rune-carved pillar shafts (Jarldom 0.1.18+). The game's own shaft meshes (wood_pole_log_4, wood_pole2, darkwood_pole4) with
Óðinn (left pillar) and Þórr (right) cut into their fronts in Blender, the grooves a material of their own (painted in game).
Remake: in the game `jarldom dumpmesh <prefab> <file>` for each into ~/.cache/jarldom-carve/{log,pole,dark}.json, copy these
scripts there, `blender -b --python ~/.cache/jarldom-carve/carve.py`, copy the *.json.gz back here.
