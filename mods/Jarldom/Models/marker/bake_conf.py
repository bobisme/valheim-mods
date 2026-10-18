# The markers' bake (tools/bake/bake.py, docs/model-baking.md):
#   blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Jarldom/Models/marker/bake_conf.py <stake|post|claim|memorial>
# stake: the marking staff (Pieces.Designator), post: the watch post, claim: the ore claim, memorial: the memorial stone.
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/jarldom-stake'
COPY = [HERE + '/' + f for f in ('stake.py', 'post.py', 'claim.py', 'memorial.py')]
SCRIPT = lambda kind: WORK + '/' + kind + '.py'
ARGS = lambda kind: []
NAME = lambda kind: kind
SHIP = HERE
BUDGET = {'stake': 1200, 'post': 1200, 'claim': 1500, 'memorial': 1000}
PX = 256
DECALS = ('cut', 'stroke', 'rune', 'wrap', 'grip', 'lashing', 'lash', 'rivet', 'nail')

# As Pieces draws them: the hewn grain (StakeGrain 0.6), the rock4_forest rock (its own shader) at 0.35 darkened (memorial: 0.5),
# the rest plain.
WOOD = ('woodpole_MainTex_128.png', 0.6, (1, 1, 1))
def look(mats, kind, plain, grained):
    cols = {'rope': (0.62, 0.52, 0.34), 'iron': (0.16, 0.155, 0.15), 'cloth': (0.56, 0.1, 0.06), 'paint': (0.86, 0.64, 0.24),
            'leather': (0.26, 0.15, 0.09), 'ore': (0.6, 0.32, 0.14)}
    for m in mats:
        if m.name == 'wood': grained(m, *WOOD)
        elif m.name == 'stone' and kind == 'memorial': grained(m, 'rock1_MainTex_256.png', 0.5, (0.95, 0.93, 0.9))
        elif m.name == 'stone': grained(m, 'rock1_MainTex_256.png', 0.35, (0.75, 0.75, 0.78))
        elif kind == 'memorial': grained(m, 'rock1_MainTex_256.png', 0.5, (0.07, 0.065, 0.06))   # (paint and rune: the cut, black)
        elif m.name == 'glow': plain(m, (1, 0.78, 0.42), (1, 0.62, 0.25))
        else: plain(m, cols[m.name])
