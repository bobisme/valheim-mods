# The Warstone upgrades' bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Shieldwall/Models/upgrade/bake_conf.py <banner|totem|brazier|obelisk>
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/upgrades'                # (upgrades.py reads its lib from here)
COPY = [HERE + '/lib.py', HERE + '/upgrades.py']
SCRIPT = WORK + '/upgrades.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: 'up_' + kind
BUDGET = {'banner': 2500, 'totem': 2500, 'brazier': 1800, 'obelisk': 1200}
PX = 256
DECALS = ('cut', 'wrap', 'lash', 'ridge', 'rivet', 'nail', 'stud')

# As UpgradeModel.Materials draws them; the rock is the Warstone's (Look.RockFrom's texture at Look.RockScale).
COLS = {'dark': (0.07, 0.06, 0.055), 'iron': (0.2, 0.195, 0.19), 'gold': (0.72, 0.5, 0.25), 'bone': (0.72, 0.68, 0.58), 'hide': (0.36, 0.25, 0.16),
        'rope': (0.48, 0.4, 0.27), 'cloth': (0.42, 0.06, 0.04), 'paint': (0.06, 0.055, 0.05), 'shade': (0.24, 0.21, 0.17)}

def look(mats, kind, plain, grained):
    for m in mats:
        if m.name == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.75, 0.68, 0.62))
        elif m.name == 'rock': grained(m, 'rock1_MainTex_256.png', 0.35, (0.52, 0.53, 0.57))
        elif m.name == 'rune': grained(m, 'rock1_MainTex_256.png', 0.35, (0.35, 0.06, 0.02), emit=(0.9, 0.198, 0.054))
        elif m.name == 'glow': plain(m, (1, 0.4, 0.1), (0.9, 0.27, 0.045))
        else: plain(m, COLS[m.name])
