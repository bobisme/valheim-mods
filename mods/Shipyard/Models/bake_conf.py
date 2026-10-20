# Shipyard's bake (tools/bake/bake.py, docs/model-baking.md): blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Shipyard/Models/bake_conf.py <kind>
# (faering_keel faering_ribs faering_strakes faering_fit slipway;
#  knarr_keel knarr_ribs knarr_strakes knarr_deck knarr_mast knarr_yard knarr_sail knarr_cradle)
import os
HERE = os.path.dirname(os.path.abspath(__file__))
WORK = '/home/bob/.cache/shipyard'               # (yard.py writes its own exports here)
COPY = [HERE + '/yard.py']
SCRIPT = WORK + '/yard.py'
SHIP = HERE
ARGS = lambda kind: [kind]
NAME = lambda kind: kind
BUDGET = {'faering_keel': 600, 'faering_ribs': 900, 'faering_strakes': 1800, 'faering_fit': 1200, 'slipway': 1500,
          'knarr_keel': 500, 'knarr_ribs': 1600, 'knarr_strakes': 3200, 'knarr_deck': 1600, 'knarr_mast': 1500, 'knarr_yard': 400,
          'knarr_sail': 400, 'knarr_cradle': 500}
PX = {k: 512 if k in ('faering_strakes', 'knarr_strakes') else 256 for k in BUDGET}
DECALS = ('seam',)
TOL = {}

# The sail's wool: vertical red and white stripes, STRIPE m wide, by x in the part's own frame (as yard.py's preview draws them).
STRIPE, WHITE, RED = 0.8, (0.86, 0.82, 0.72), (0.58, 0.12, 0.09)
def striped(m, plain):
    plain(m, (1, 1, 1)); nt = m.node_tree; b = nt.nodes['Principled BSDF']
    lin = lambda c: tuple(((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c)
    tc = nt.nodes.new('ShaderNodeTexCoord'); sx = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['Object'], sx.inputs[0])
    dv = nt.nodes.new('ShaderNodeMath'); dv.operation = 'DIVIDE'; dv.inputs[1].default_value = STRIPE; nt.links.new(sx.outputs['X'], dv.inputs[0])
    md = nt.nodes.new('ShaderNodeMath'); md.operation = 'FLOORED_MODULO'; md.inputs[1].default_value = 2.0; nt.links.new(dv.outputs[0], md.inputs[0])
    st = nt.nodes.new('ShaderNodeMath'); st.operation = 'GREATER_THAN'; st.inputs[1].default_value = 1.0; nt.links.new(md.outputs[0], st.inputs[0])
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; nt.links.new(st.outputs[0], mix.inputs['Factor'])
    mix.inputs['A'].default_value = (*lin(WHITE), 1); mix.inputs['B'].default_value = (*lin(RED), 1); nt.links.new(mix.outputs['Result'], b.inputs['Base Color'])

# Pine planking and oak timbers both on the game's wood pole picture (oak tinted darker), tar near black, iron dark.
def look(mats, kind, plain, grained):
    for m in mats:
        n = m.name
        if n == 'wood': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.8, 0.7, 0.58), on_uv=True)
        elif n == 'oak': grained(m, 'woodpole_MainTex_128.png', 0.6, (0.56, 0.45, 0.34), on_uv=True)
        elif n == 'wool': striped(m, plain)
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
