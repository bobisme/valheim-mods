The Hallwright stake's look (BuildShapes 0.8.10+): made from scratch by hallstake.py in Blender.
Remake: copy hallstake.py to ~/.cache/stakes (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/stakes/hallstake.py -- render`
(previews hall_*.png there), copy ~/.cache/stakes/hallstake.json.gz back here. Its materials (wood, dark wood, iron, ochre) are set in
HallStake.cs BuildStake. Paint is its own length of the post, not a shell over the wood: in game a thin shell loses to the wood's long faces.
Since the bake (docs/model-baking.md) what ships is baked: hallstake.py stays the source; tools/bake/bake.py with bake_conf.py writes
hallstake.json.gz (the low copy) and hallstake_albedo/_normal.png (128 px); HallStake.cs draws it with one material when the albedo is there.
