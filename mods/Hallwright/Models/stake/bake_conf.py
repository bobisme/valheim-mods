# The Hallwright stake's bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Hallwright/Models/stake/bake_conf.py hallstake
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/stakes'
COPY = [HERE + '/hallstake.py']
SCRIPT = WORK + '/hallstake.py'
SHIP = HERE
ARGS = lambda kind: []
NAME = lambda kind: 'hallstake'
BUDGET, PX = 600, 128
DECALS = ('tick',)   # (the square's graduation marks)

# As HallStake.cs BuildStake draws it: wood_pole's grain at 0.6 per metre, tinted; iron and ochre plain.
def look(mats, kind, plain, grained):
    for m in mats:
        if m.name == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.95, 0.9, 0.82))
        elif m.name == 'dark': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.55, 0.45, 0.38))
        elif m.name == 'iron': plain(m, (0.16, 0.155, 0.15))
        elif m.name == 'ochre': plain(m, (0.82, 0.42, 0.1))
