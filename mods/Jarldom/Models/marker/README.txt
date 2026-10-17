The marking staff, the look of the hammer's "Mark rooms and zones" (Jarldom 0.1.30+, a tall staff since 0.1.32, ribbons for its pennant since 0.1.34): made from scratch by stake.py in Blender.
Remake: copy stake.py to ~/.cache/jarldom-stake (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/jarldom-stake/stake.py -- render`
(previews preview_*.png there), copy ~/.cache/jarldom-stake/stake.json.gz back here. Its materials (wood, rope, iron, cloth, paint, leather) are
set in Pieces.Designator.
post.py (post.json.gz) is the stocky stake the marker had in 0.1.30-0.1.31, the watch post's look since 0.1.33 (Pieces.WatchPost);
remade the same way.
claim.py (claim.json.gz) is the ore claim's look since 0.1.48 (Pieces.OreClaim; slots wood, rope, iron, stone, ore, glow); remade the same way.
Since the bake (docs/model-baking.md) stake, post, claim and memorial ship BAKED: the scripts stay the source; tools/bake/bake.py with
bake_conf.py writes <name>.json.gz (the low copy) and <name>_albedo/_normal[/_glow].png, drawn by Pieces.BakedLook as one material.
From the repo root: blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Jarldom/Models/marker/bake_conf.py <stake|post|claim|memorial>
