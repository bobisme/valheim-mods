# The demolition stake's bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Demolisher/Models/stake/bake_conf.py demostake
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/stakes'
COPY = [HERE + '/demostake.py']
SCRIPT = WORK + '/demostake.py'
SHIP = HERE
ARGS = lambda kind: []
NAME = lambda kind: 'demostake'
BUDGET, PX = 700, 128
DECALS = ('daub', 'nail')   # (the red slash across the X, the boards' nail heads)

# As Stake.cs BuildStake draws it: wood_pole's grain at 0.6 per metre, tinted; iron and paints plain.
def look(mats, kind, plain, grained):
    for m in mats:
        if m.name == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.85, 0.78, 0.72))
        elif m.name == 'board': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.72, 0.7, 0.66))
        elif m.name == 'iron': plain(m, (0.16, 0.155, 0.15))
        elif m.name == 'red': plain(m, (0.55, 0.06, 0.04))
        elif m.name == 'black': plain(m, (0.05, 0.045, 0.04))
