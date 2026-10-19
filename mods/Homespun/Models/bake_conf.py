# Homespun's bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Homespun/Models/bake_conf.py <kind>
# (pool brake loom table retted line wool yarn linenbolt woolbolt band spindle undertunic tunic dress trousers cloak nettle nettleplant vat lichen lichenplant)
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/homespun'               # (spun.py writes its own exports here)
COPY = [HERE + '/spun.py']
SCRIPT = WORK + '/spun.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: kind
BUDGET = {'pool': 1500, 'brake': 1300, 'loom': 1800, 'table': 1300,
          'retted': 600, 'line': 700, 'wool': 200, 'yarn': 300, 'linenbolt': 350, 'woolbolt': 200, 'band': 250, 'spindle': 250,
          'undertunic': 200, 'tunic': 300, 'dress': 450, 'trousers': 350, 'cloak': 400,
          'nettle': 1200, 'nettleplant': 2400, 'vat': 1500, 'lichen': 400, 'lichenplant': 1200}
PX = {k: 256 if k in ('pool', 'brake', 'loom', 'table', 'nettleplant', 'vat', 'lichenplant') else 128 for k in BUDGET}
DECALS = ('wrap', 'stripe', 'weave', 'crust')
TOL = {'hank': 0.01, 'sheaf': 0.01, 'stalk': 0.004, 'tuft': 0.004, 'ply': 0.006, 'knot': 0.006}   # (bundles of fibre: rough already)

# As Looks.Slots draws them: wood grained (on spun.py's uvs: the grain along each part's 'grain'), the stone pile's picture for the
# stones and loom weights, the rest plain: undyed flax, linen and wool (three shades for the garments), iron, bronze, madder red and woad blue.
def look(mats, kind, plain, grained):
    for m in mats:
        n = m.name
        if n == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.78, 0.7, 0.6), on_uv=True)
        elif n == 'stone': grained(m, 'stone_pile_MainTex_32.png', 2.0, (0.85, 0.8, 0.74), on_uv=True)
        else: plain(m, {'water': (0.13, 0.2, 0.18), 'flax': (0.78, 0.68, 0.45), 'linen': (0.9, 0.87, 0.78), 'wool': (0.66, 0.6, 0.52),
                        'iron': (0.17, 0.17, 0.18), 'red': (0.55, 0.13, 0.1), 'blue': (0.15, 0.22, 0.42), 'retted': (0.62, 0.6, 0.55),
                        # (the garments: as Chain.Garments' colours, which the worn look is recoloured to)
                        'oat': (0.72, 0.62, 0.48), 'darkwool': (0.45, 0.38, 0.3), 'cloakwool': (0.33, 0.28, 0.23), 'bronze': (0.62, 0.45, 0.2),
                        'nettle': (0.24, 0.36, 0.15), 'lichen': (0.62, 0.66, 0.52), 'xanthoria': (0.85, 0.55, 0.15)}[n])

def prepare(high):
    """Each part's uvs as spun.py's export writes them: in metres, along the part's grain where it has one, else by its facing."""
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
