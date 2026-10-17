The demolition stake's look (Demolisher 0.1.1+): made from scratch by demostake.py in Blender.
Remake: copy demostake.py to ~/.cache/stakes (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/stakes/demostake.py -- render`
(previews demo_*.png there), copy ~/.cache/stakes/demostake.json.gz back here. Its materials (wood, weathered boards, iron, red, black) are
set in Stake.cs BuildStake. Paint is its own length of the log, not a shell over the wood: in game a thin shell loses to the wood's long faces.
Since the bake (docs/model-baking.md) what ships is baked: demostake.py stays the source; tools/bake/bake.py with bake_conf.py writes
demostake.json.gz (the low copy) and demostake_albedo/_normal.png (128 px); Stake.cs draws it with one material when the albedo is there.
