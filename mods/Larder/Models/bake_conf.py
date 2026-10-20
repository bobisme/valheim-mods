# Larder's bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Larder/Models/bake_conf.py <kind>
# (churn press butter cheese skyr; smokehouse saltpan saltbarrel dryingrack smokedmeat saltmeat stockfish salt; bakeoven flatbread flatbreaddough spoiled)
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/larder'                 # (larder.py writes its own exports here)
COPY = [HERE + '/larder.py']
SCRIPT = WORK + '/larder.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: kind
BUDGET = {'churn': 1300, 'press': 1500, 'butter': 250, 'cheese': 250, 'skyr': 300,
          'smokehouse': 1800, 'saltpan': 1200, 'saltbarrel': 900, 'dryingrack': 900,
          'smokedmeat': 250, 'saltmeat': 250, 'stockfish': 150, 'salt': 250,
          'bakeoven': 1500, 'flatbread': 150, 'flatbreaddough': 150, 'spoiled': 150}
PX = {k: 256 if k in ('churn', 'press', 'smokehouse', 'saltpan', 'saltbarrel', 'dryingrack', 'bakeoven') else 128 for k in BUDGET}
DECALS = ('decal',)   # (soot on the smokehouse, hinge straps on its hatch, salt flecks on the salt meat, the string on the smoked meat;
              # soot over the oven's mouth and in its vent, toasted spots on the flatbread, flour on the dough, mould on the spoiled food)

# Wood grained (on larder.py's uvs: the grain along each part's 'grain'), the stone pile's picture for the press's weight, the rest plain.
def look(mats, kind, plain, grained):
    for m in mats:
        n = m.name
        if n == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.78, 0.7, 0.6), on_uv=True)
        elif n == 'stone': grained(m, 'stone_pile_MainTex_32.png', 2.0, (0.85, 0.8, 0.74), on_uv=True)
        elif n == 'sootstone': grained(m, 'stone_pile_MainTex_32.png', 2.0, (0.36, 0.32, 0.29), on_uv=True)
        else: plain(m, {'iron': (0.17, 0.17, 0.18), 'butter': (0.93, 0.82, 0.42), 'rind': (0.82, 0.66, 0.35), 'curd': (0.95, 0.86, 0.55),
                        'skyr': (0.94, 0.93, 0.88), 'blueberry': (0.2, 0.22, 0.45), 'rope': (0.55, 0.45, 0.3), 'horn': (0.75, 0.68, 0.55),
                        'whey': (0.85, 0.86, 0.7), 'soot': (0.11, 0.1, 0.09), 'salt': (0.9, 0.9, 0.87), 'smoked': (0.3, 0.17, 0.09),
                        'meat': (0.72, 0.33, 0.3), 'fat': (0.86, 0.78, 0.68), 'fish': (0.74, 0.68, 0.56), 'smudge': (0.3, 0.24, 0.19),
                        'clay': (0.56, 0.48, 0.4), 'bread': (0.8, 0.62, 0.36), 'toast': (0.6, 0.4, 0.2), 'dough': (0.9, 0.84, 0.7),
                        'flour': (0.95, 0.92, 0.84), 'rot': (0.4, 0.34, 0.27), 'mould': (0.55, 0.62, 0.45)}[n])

def prepare(high):
    """Each part's uvs as larder.py's export writes them: in metres, along the part's grain where it has one, else by its facing."""
    from mathutils import Vector
    for o in high:
        me = o.data
        if not me.uv_layers: me.uv_layers.new(name='uv')
        if me.uv_layers.active is None: continue
        uv = me.uv_layers.active.data
        grain = Vector(o['grain']).normalized() if 'grain' in o else None
        for p in me.polygons:
            fn = p.normal
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                if grain is not None and abs(fn.dot(grain)) < 0.7:
                    across = fn.cross(grain).normalized(); u, v = co.dot(across), co.dot(grain)
                else: u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                uv[li].uv = (u, v)
        o['bake_uv'] = 1
