# The war staves' bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python tools/bake/bake.py -- mods/Shieldwall/Models/stave/bake_conf.py <kind>
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/staves'                 # (staves.py reads its lib from here)
COPY = [HERE + '/lib.py', HERE + '/staves.py']
SCRIPT = WORK + '/staves.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: 'stave_' + kind
BUDGET, PX = 1500, 256
DECALS = ('cut', 'crack', 'inlay', 'wrap', 'lash', 'scorch')

# As StaveModel used to draw them (wood tint, glow, its brightness, crystal, paint).
OWN = {'ember': ((0.6, 0.5, 0.45), (1, 0.24, 0.03), 0.9, (1, 0.5, 0.2), (0.4, 0.12, 0.06)),
       'frost': ((1.15, 1.4, 1.95), (0.3, 0.6, 1), 0.8, (0.55, 0.75, 0.95), (0.3, 0.4, 0.5)),
       'thunder': ((0.6, 0.72, 1.1), (0.42, 0.3, 1), 0.9, (0.7, 0.6, 1), (0.3, 0.2, 0.4)),
       'blast': ((0.62, 0.55, 0.52), (1, 0.38, 0.06), 0.9, (1, 0.6, 0.2), (0.46, 0.25, 0.15)),
       'hearth': ((0.9, 0.85, 0.8), (1, 0.6, 0.22), 0.8, (0.95, 0.68, 0.36), (0.5, 0.3, 0.15))}

def look(mats, kind, plain, grained):
    wood, glow, bright, crystal, paint = OWN[kind]
    cols = {'dark': (0.07, 0.06, 0.055), 'iron': (0.2, 0.195, 0.19), 'gold': (0.78, 0.5, 0.26), 'bone': (0.7, 0.66, 0.56), 'hide': (0.36, 0.25, 0.16),
            'rope': (0.48, 0.4, 0.27), 'shade': (0.12, 0.1, 0.09), 'paint': paint}
    for m in mats:
        if m.name == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, wood)
        elif m.name == 'glow': plain(m, glow, tuple(c * bright for c in glow))
        elif m.name == 'crystal': plain(m, crystal, tuple(c * 0.3 for c in crystal))
        else: plain(m, cols[m.name])
