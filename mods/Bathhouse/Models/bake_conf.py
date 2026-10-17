# The bathhouse's bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Bathhouse/Models/bake_conf.py <stove|bench|bucket|plunge|locker>
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/bathhouse'               # (bath.py writes its own exports here)
COPY = [HERE + '/bath.py']
SCRIPT = WORK + '/bath.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: kind
BUDGET = {'stove': 1800, 'bench': 500, 'bucket': 700, 'plunge': 1300, 'locker': 900}
PX = 256
DECALS = ()

# As Kit.Materials draws them: the wood grain (on bath.py's uvs: the grain along each part's 'grain'), the stone pile's picture for stone
# and soot (scale 2), plain iron, water and rope, glowing embers.
def look(mats, kind, plain, grained):
    for m in mats:
        n = m.name
        if n == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.78, 0.7, 0.6), on_uv=True)
        elif n == 'stone': grained(m, 'stone_pile_MainTex_32.png', 2.0, (0.9, 0.84, 0.76), on_uv=True)
        elif n == 'soot': grained(m, 'stone_pile_MainTex_32.png', 2.0, (0.2, 0.19, 0.185), on_uv=True)
        elif n == 'ember': plain(m, (1, 0.38, 0.08), (1.6, 0.608, 0.128))
        else: plain(m, {'iron': (0.17, 0.17, 0.18), 'water': (0.13, 0.2, 0.22), 'rope': (0.55, 0.48, 0.35)}[n])

def prepare(high):
    """Each part's uvs as bath.py's export writes them: in metres, along the part's grain where it has one, else by its facing."""
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
