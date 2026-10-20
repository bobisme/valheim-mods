# The naust's bake (tools/bake/bake.py, docs/model-baking.md):
#   blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Shipyard/Models/naust_bake_conf.py naust
# Its own work directory, apart from yard.py's (~/.cache/shipyard), so the two families never overwrite each other's copies.
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/shipyard_naust'          # (naust.py writes its own exports here)
COPY = [HERE + '/naust.py']
SCRIPT = WORK + '/naust.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: kind
BUDGET = {'naust': 2600}   # (asks lower than the 3500 allowed: every bevelled timber then collapses to a plain 12-triangle box)
PX = {'naust': 512}
DECALS = ('seam', 'nail')
TOL = {}

# Grey, weathered timber: pine boards and oak frame both on the game's wood pole picture, tinted grey (oak darker); the roof planks
# tarred, near black but still grained; iron dark.
def look(mats, kind, plain, grained):
    for m in mats:
        n = m.name
        if n == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (1.84, 2.9, 5.8), on_uv=True)
        elif n == 'oak': grained(m, 'woodpole_MainTex_128.png', 0.6, (1.0, 1.6, 3.4), on_uv=True)
        elif n == 'tar': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.21, 0.33, 0.7), on_uv=True)
        else: plain(m, (0.17, 0.17, 0.18))

def prepare(high):
    """Each part's uvs as naust.py's export writes them: in metres, along the part's grain where it has one, else by its facing."""
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
