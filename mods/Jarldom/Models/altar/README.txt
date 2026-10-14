The gods' altars (Jarldom 0.1.86+), one model each (altar_<god>.json.gz), made from scratch in Blender by altars.py with its kit lib.py.
Remake: copy both to ~/.cache/jarldom-altars (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/jarldom-altars/altars.py -- <god> [render]`
(previews altar_<god>_<yaw>.png there), copy the json.gz back here. The face (where the worshipper kneels) is the piece's forward.
Material slots, the same for every altar (lib.SLOTS): stone, cut, wood, dark, iron, gold, bone, cloth, rope, glow, leaf, bloom, paint, hide, shade (a shadowed hollow, since 0.1.92);
set in Pieces.AltarModel, cloth/bloom/paint coloured per god (Pieces.AltarColours).
