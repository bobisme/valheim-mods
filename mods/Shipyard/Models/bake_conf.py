# Shipyard's bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Shipyard/Models/bake_conf.py <kind>
# (faering_keel faering_ribs faering_strakes faering_fit slipway)
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/shipyard'               # (yard.py writes its own exports here)
COPY = [HERE + '/yard.py']
SCRIPT = WORK + '/yard.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: kind
BUDGET = {'faering_keel': 600, 'faering_ribs': 900, 'faering_strakes': 1800, 'faering_fit': 1200, 'slipway': 1500}
PX = {k: 512 if k == 'faering_strakes' else 256 for k in BUDGET}
DECALS = ('seam',)
TOL = {}

# Pine planking and oak timbers both on the game's wood pole picture (oak tinted darker), tar near black, iron dark.
def look(mats, kind, plain, grained):
    for m in mats:
        n = m.name
        if n == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.8, 0.7, 0.58), on_uv=True)
        elif n == 'oak': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.56, 0.45, 0.34), on_uv=True)
        else: plain(m, {'tar': (0.1, 0.08, 0.07), 'iron': (0.17, 0.17, 0.18)}[n])

def prepare(high):
    """Each part's uvs as yard.py's export writes them: in metres, along the part's grain where it has one, else by its facing."""
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
