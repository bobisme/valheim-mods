# Byre's goods bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Byre/Models/bake_conf.py <kind>
# (shears pail milk wool trough rack stall truffle). The animals (animals.py) are made low-poly and skinned: not baked.
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/byre-goods'               # (goods.py writes its own exports here)
COPY = [HERE + '/goods.py']
SCRIPT = WORK + '/goods.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: kind
BUDGET = {'shears': 600, 'pail': 900, 'milk': 500, 'wool': 500, 'trough': 1400, 'rack': 1600, 'stall': 500, 'truffle': 400}
PX = {'shears': 128, 'pail': 128, 'milk': 128, 'wool': 256, 'trough': 256, 'rack': 256, 'stall': 128, 'truffle': 64}   # (the fleece's locks want the room)
DECALS = ('wisp', 'wrap')   # (the loose straws on the trough's hay, the shears' grip bindings: texture only)
# (how far the low copy may stray: the shears' blades, the pail's staves and the trough's log kept true, wool and hay rough already)
TOL = {'slat': 0.004, 'plank': 0.004, 'post': 0.004, 'end': 0.004, 'shears': 0.0012, 'grip': 0.0025, 'log': 0.01, 'fleece': 0.006, 'band': 0.004, 'hay': 0.01, 'hoop': 0.004, 'staves': 0.0015, 'cord': 0.003}

# As Byre's props draw (wood_pole's Custom/Piece material wearing the bake): wood grained along each part's grain, the rest plain:
# forged iron, a dark tanned leather, goat's milk, greasy undyed wool, red-brown earthenware, hay and a hemp cord.
def look(mats, kind, plain, grained):
    for m in mats:
        n = m.name
        if n == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.78, 0.7, 0.6), on_uv=True)
        else: plain(m, {'iron': (0.2, 0.2, 0.21), 'leather': (0.33, 0.2, 0.11), 'milk': (0.92, 0.9, 0.82), 'wool': (0.8, 0.74, 0.62),
                        'clay': (0.6, 0.38, 0.26), 'hay': (0.74, 0.62, 0.34), 'rope': (0.62, 0.53, 0.38), 'truffle': (0.09, 0.075, 0.065)}[n])

def prepare(high):
    """Each part's uvs as goods.py's export writes them: in metres, along the part's grain where it has one, else by its facing."""
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
