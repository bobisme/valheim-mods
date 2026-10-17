# The gods' altars' bake (tools/bake/bake.py, docs/model-baking.md):
#   blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Jarldom/Models/altar/bake_conf.py <odin|thor|freyja|njord|frigg|tyr|ullr|hel>
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/jarldom-altars'          # (altars.py imports its lib from here)
COPY = [HERE + '/lib.py', HERE + '/altars.py']
SCRIPT = WORK + '/altars.py'
SHIP = HERE
ARGS = lambda god: [god]
NAME = lambda god: 'altar_' + god
BUDGET, PX = 2500, 256
DECALS = ('cut', 'rivet', 'lash', 'wire', 'bind', 'scorch', 'coin', 'ear')   # (small things lying on another part: painted on it)

# As Pieces.AltarModel draws them: the stone and carving the rock's texture in the pieces' shader at 0.4, the wood the hewn grain
# (Pieces.StakeGrain), the rest plain; cloth, bloom and paint each god's own (Pieces.AltarColours).
OWN = {'odin': ((0.3, 0.32, 0.45), (0.7, 0.3, 0.35), (0.56, 0.1, 0.06)), 'thor': ((0.5, 0.12, 0.08), (0.7, 0.3, 0.35), (0.6, 0.11, 0.06)),
       'freyja': ((0.55, 0.3, 0.35), (0.86, 0.4, 0.5), (0.62, 0.14, 0.08)), 'njord': ((0.25, 0.35, 0.42), (0.7, 0.3, 0.35), (0.45, 0.56, 0.62)),
       'frigg': ((0.17, 0.26, 0.5), (0.7, 0.3, 0.35), (0.78, 0.62, 0.3)), 'tyr': ((0.5, 0.12, 0.08), (0.7, 0.3, 0.35), (0.58, 0.1, 0.06)),
       'ullr': ((0.3, 0.35, 0.3), (0.7, 0.3, 0.35), (0.6, 0.12, 0.08)), 'hel': ((0.33, 0.34, 0.32), (0.5, 0.45, 0.5), (0.3, 0.3, 0.32))}
COLS = {'dark': (0.09, 0.085, 0.085), 'iron': (0.2, 0.195, 0.19), 'gold': (0.9, 0.66, 0.24), 'bone': (0.66, 0.63, 0.56), 'rope': (0.55, 0.46, 0.3),
        'leaf': (0.2, 0.36, 0.12), 'hide': (0.42, 0.3, 0.19), 'shade': (0.24, 0.21, 0.17)}

def look(mats, god, plain, grained):
    cloth, bloom, paint = OWN[god]
    for m in mats:
        if m.name == 'stone': grained(m, 'rock1_MainTex_256.png', 0.4, (0.85, 0.85, 0.86))
        elif m.name == 'cut': grained(m, 'rock1_MainTex_256.png', 0.4, (0.08, 0.075, 0.07))
        elif m.name == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (1, 1, 1))
        elif m.name == 'glow': plain(m, (1, 0.78, 0.42), (1, 0.6, 0.22))
        elif m.name == 'cloth': plain(m, cloth)
        elif m.name == 'bloom': plain(m, bloom)
        elif m.name == 'paint': plain(m, paint)
        else: plain(m, COLS[m.name])
