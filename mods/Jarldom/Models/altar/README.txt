The gods' altars (Jarldom 0.1.86+), one model each (altar_<god>.json.gz), made from scratch in Blender by altars.py with its kit lib.py.
Remake: copy both to ~/.cache/jarldom-altars (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/jarldom-altars/altars.py -- <god> [render]`
(previews altar_<god>_<yaw>.png there), copy the json.gz back here. The face (where the worshipper kneels) is the piece's forward.
Material slots, the same for every altar (lib.SLOTS): stone, cut, wood, dark, iron, gold, bone, cloth, rope, glow, leaf, bloom, paint, hide, shade (a shadowed hollow, since 0.1.92);
set in Pieces.AltarModel, cloth/bloom/paint coloured per god (Pieces.AltarColours).
Since the bake (docs/model-baking.md) what ships is BAKED: altars.py stays the source; tools/bake/bake.py with bake_conf.py (colours as
Pieces.AltarModel drew them, per god) writes altar_<god>.json.gz (the low copy) and altar_<god>_albedo/_normal[/_glow].png, drawn by
Pieces.BakedLook as one material. From the repo root: blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Jarldom/Models/altar/bake_conf.py <god>
Hel's bones stay the game's own, kitbashed at runtime (Pieces.HelKit).
