# The camping kit's bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Camping/Models/bake_conf.py <bedroll|leanto|tent>
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/camping'                 # (camp.py writes its own exports here)
COPY = [HERE + '/camp.py']
SCRIPT = WORK + '/camp.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: kind
BUDGET = {'bedroll': 600, 'leanto': 2000, 'tent': 1500}
PX = {'bedroll': 256, 'leanto': 256, 'tent': 256}
DECALS = ('stitch',)

# As Kit.Materials draws them: the wood grain; the deer rug's hide on the hides' own uvs; the fur and wolf rugs' middles (scale 0.1,
# offset 0.42 on metre uvs) for furs; plain wool, leather, rope, red wool, stone.
def look(mats, kind, plain, grained):
    for m in mats:
        n = m.name
        if n == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.8, 0.72, 0.64))
        elif n == 'hide': uvmapped(m, 'deerhide_rug_MainTex_128.png', (0.74, 0.69, 0.74))
        elif n == 'hide2': uvmapped(m, 'deerhide_rug_MainTex_128.png', (0.6, 0.54, 0.58))
        elif n == 'fur': grained(m, 'rug_fur_MainTex_128.png', 0.1, (0.9, 0.86, 0.82), offset=0.42)
        elif n == 'fur2': grained(m, 'rug_wolf_MainTex_128.png', 0.1, (0.7, 0.68, 0.66), offset=0.42)
        else: plain(m, {'wool': (0.55, 0.5, 0.43), 'leather': (0.2, 0.12, 0.07), 'rope': (0.55, 0.48, 0.35), 'red': (0.48, 0.09, 0.06), 'stone': (0.42, 0.42, 0.4)}[n])
TOL = {'sheet': 0.045}   # (the lean-to's hide: its ragged hem may lose a little; bake.py's shape check)
