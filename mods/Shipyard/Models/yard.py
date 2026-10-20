# Shipyard's looks, made from scratch: the faering (a Gokstad-style four-oared boat) in the four stages it's built in on the slipway
# (keel and stems; the ribs; the clinker strakes and gunwale; the thwarts, oars, oarlocks and steering oar), and the slipway itself.
# Also the knarr (a Skuldelev 1-style cargo ship, 16 m): knarr_keel, _ribs, _strakes, _deck, _mast, _yard, _sail and _cradle (see below).
# Run: blender -b --python ~/.cache/shipyard/yard.py -- [render] [faering_keel|faering_ribs|faering_strakes|faering_fit|slipway|faering|knarr_*|knarr|knarr_onslip]
# (writes ~/.cache/shipyard/<name>.json.gz; previews with "render"; "faering", "onslip", "knarr" and "knarr_onslip" are previews only).
# What ships is the bake of these (bake_conf.py, docs/model-baking.md).
# Blender frame: z up, +y the bow (the game's +z), +x starboard. The hull's origin is on the centreline amidships at the WATERLINE
# (the game's y 0); the four stages share it and line up. The slipway stands on z 0 with its length along y.
# Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres; wood's grain runs along v), triangles per
# material: 0 wood (pine planking), 1 oak (keel, stems, ribs: darker), 2 tar (the tarred seams and the oar blades' edges), 3 iron.
# 4 wool (the knarr's sail: red and white stripes by x, drawn by the material, not the uvs).
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/shipyard/'
WOOD, OAK, TAR, IRON, WOOL = range(5)
NAMES = ('wood', 'oak', 'tar', 'iron', 'wool')
SMOOTH = ('strake', 'stem', 'rib', 'gunwale', 'oar', 'log', 'shore', 'sleeper', 'futtock', 'mast', 'yard', 'furl', 'sail', 'shroud', 'stay',
          'halyard', 'tiller', 'lrail', 'truck', 'waterway')
mats = []
V = Vector

def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats.clear()
    mats.extend(bpy.data.materials.new(n) for n in NAMES)

def link(name, bm, mat, grain=None):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[mat])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    if grain is not None: ob['grain'] = tuple(grain)
    return ob

def tube(name, pts, radius, mat, res=2, square=False):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 0 if square else res; cu.use_fill_caps = True
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o['grain'] = tuple(pts[-1] - pts[0])
    return o

def pole(name, a, b, radius, mat=WOOD, seed=0, bend=0.01):
    r = random.Random(seed); a, b = V(a), V(b)
    mid = [a.lerp(b, t) + V((r.uniform(-bend, bend), r.uniform(-bend, bend), r.uniform(-bend, bend) * 0.3)) for t in (0.33, 0.66)]
    return tube(name, [a] + mid + [b], radius, mat)

def box(name, lo, hi, mat, grain=None, bevel=0.0, jitter=0.0, seed=0):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    lo, hi = V(lo), V(hi)
    bmesh.ops.scale(bm, vec=hi - lo, verts=bm.verts); bmesh.ops.translate(bm, vec=(lo + hi) / 2, verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES', profile=0.5)
    if jitter:
        r = random.Random(seed)
        for v in bm.verts: v.co += V((r.uniform(-jitter, jitter), r.uniform(-jitter, jitter), r.uniform(-jitter, jitter)))
    return link(name, bm, mat, grain)

def turned(ob, m, about=(0, 0, 0)):
    about = V(about)
    ob.data.transform(Matrix.Translation(about) @ m.to_4x4() @ Matrix.Translation(-about))
    if 'grain' in ob: ob['grain'] = tuple(m.to_3x3() @ V(ob['grain']))
    return ob

# ---------------- the hull's lines ----------------
# a = |y|, the distance from amidships. The keel runs straight to a 2.0; the stems curve up from there on an arc (centre (2.0, 0.55),
# radius 0.95, on their centre line) to a 2.95, then stand up to their tips at z 0.9. The planking's lower edge (the rabbet) follows
# the keel's top and then the stem; the sheer rises from 0.40 amidships to the stem heads; the beam is 1.4 at the gunwale amidships.
KEEL_BOTTOM, KEEL_H, KEEL_W = -0.45, 0.1, 0.08
ARC_Y, ARC_Z, ARC_R = 2.0, 0.55, 0.95
END = 2.95                 # (where the planking ends in the stems)
STRAKES = 4

def stem_z(a):
    # the stem's centre line at a (a past ARC_Y), or the keel's centre
    if a <= ARC_Y: return KEEL_BOTTOM + KEEL_H / 2
    d = min(a - ARC_Y, ARC_R)
    return ARC_Z - math.sqrt(max(0.0, ARC_R * ARC_R - d * d))
def rabbet(a): return stem_z(a) + 0.045
def sheer(a): return 0.40 + 0.5 * (min(a, END) / END) ** 4
def beam(a): return 0.70 * max(0.0, 1 - (min(a, 2.985) / 2.985) ** 2) ** 0.75
def section(y, u):
    # a point of the hull's outer skin: u 0 at the rabbet, 1 at the gunwale; starboard (+x)
    a = abs(y); zb, zt, B = rabbet(a), sheer(a), beam(a)
    x = B * math.sin(u * math.pi / 2) ** 0.7
    z = zb + (zt - zb) * u ** 1.6
    return V((x, y, z))
def outward(y, u):
    e = 0.01
    p0, p1 = section(y, max(0, u - e)), section(y, min(1, u + e))
    t = (p1 - p0); n = V((t.z, 0, -t.x))
    return n.normalized() if n.length > 1e-6 else V((1, 0, 0))
def half_width_at(y, z):
    # how far out the inside of the hull is at height z (for the thwarts' ends)
    best = 0.0
    for i in range(201):
        p = section(y, i / 200)
        if p.z <= z: best = p.x
    return best - 0.03

def mirrored(ob):
    # its port twin (x turned over, faces turned back outward)
    me = ob.data.copy(); me.transform(Matrix.Scale(-1, 4, (1, 0, 0)))
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.reverse_faces(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(ob.name + '_p', me); bpy.context.scene.collection.objects.link(o)
    if 'grain' in ob: o['grain'] = ob['grain']
    # (and its modifiers: a strake's thickness is a solidify, and without it the twin is a bare sheet, gone seen from inside)
    for m in ob.modifiers:
        c = o.modifiers.new(m.name, m.type)
        for p in m.bl_rna.properties:
            if not p.is_readonly: setattr(c, p.identifier, getattr(m, p.identifier))
    for k in ob.keys():
        if k != 'grain': o[k] = ob[k]
    return o

def keel():
    # the keel, square in section, and the two stems curving up from its ends to their tall heads (a little curled in)
    box('keel', (-KEEL_W / 2, -ARC_Y - 0.02, KEEL_BOTTOM), (KEEL_W / 2, ARC_Y + 0.02, KEEL_BOTTOM + KEEL_H), OAK, grain=(0, 1, 0), bevel=0.01)
    for s, name in ((1, 'stem'), (-1, 'stem_stern')):
        pts = []
        for i in range(13):
            th = math.radians(90 * i / 12)
            pts.append(V((0, s * (ARC_Y + ARC_R * math.sin(th)), ARC_Z - ARC_R * math.cos(th))))
        pts += [V((0, s * (ARC_Y + ARC_R + 0.01), 0.72)), V((0, s * (ARC_Y + ARC_R - 0.0), 0.86)), V((0, s * (ARC_Y + ARC_R - 0.06), 0.93))]
        tube(name, pts, 0.048, OAK, square=True)

def ribs():
    # five frames across the hull, each one bent timber up the inside of the planking from the keel to under the gunwale, a floor
    # timber over the keel; bolted to the keel with an iron spike (seen as a dark head)
    for i, y in enumerate((-1.45, -0.72, 0.0, 0.72, 1.45)):
        side = []
        for k in range(10):
            u = 0.04 + 0.9 * k / 9
            p = section(y, u) - outward(y, u) * 0.045
            side.append(p)
        pts = [V((-p.x, p.y, p.z)) for p in reversed(side)] + side
        tube(f'rib_{i}', pts, 0.032, OAK, square=True)
        box(f'riv_{i}', (-0.03, y - 0.03, rabbet(abs(y)) + 0.012), (0.03, y + 0.03, rabbet(abs(y)) + 0.03), IRON)

def strakes():
    # four clinker strakes a side, each overlapping the one below on the outside (its lower edge stands out), a tarred seam along each
    # lap; a gunwale timber along the top. The planks run from stem to stem.
    n = 34
    ys = [-END + 2 * END * (0.5 - 0.5 * math.cos(math.pi * i / n)) for i in range(n + 1)]  # (closer together at the ends, where it bends)
    for k in range(STRAKES):
        u0, u1 = max(0.0, k / STRAKES - 0.035), (k + 1) / STRAKES
        bm = bmesh.new(); rows = []
        for y in ys:
            row = []
            for j in range(3):
                v = j / 2; u = u0 + (u1 - u0) * v
                p = section(y, u) + outward(y, u) * (0.016 * k * 0 + 0.018 * (1 - v) * (1 if k else 0.4))
                row.append(bm.verts.new(p))
            rows.append(row)
        for i in range(n):
            for j in range(2):
                bm.faces.new((rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        # (normals outward: the centre of the hull is inside)
        for f in bm.faces:
            c = f.calc_center_median()
            if f.normal.x < 0: f.normal_flip()
        o = link(f'strake_{k}', bm, WOOD, grain=(0, 1, 0))
        m = o.modifiers.new('solid', 'SOLIDIFY'); m.thickness = 0.022; m.offset = -1
        mirrored(o)
        # the tarred seam where this strake laps over the one below
        if k:
            bm = bmesh.new(); prev = None
            for y in ys[2:-2]:
                u = u0 + 0.004
                a = section(y, u) + outward(y, u) * 0.0195; b = section(y, u + 0.02) + outward(y, u + 0.02) * 0.0195
                cur = (bm.verts.new(a), bm.verts.new(b))
                if prev: bm.faces.new((prev[0], cur[0], cur[1], prev[1]))
                prev = cur
            for f in bm.faces:
                if f.normal.x < 0: f.normal_flip()
            s = link(f'seam_{k}', bm, TAR); mirrored(s)
    # the gunwale: a timber along the sheer, over the top strake's edge
    pts = [section(y, 1.0) + V((0, 0, 0.012)) for y in ys[1:-1]]
    g = tube('gunwale', pts, 0.03, OAK, square=True); mirrored(g)

def fit():
    # two thwarts, four oars laid in the boat, oarlocks (keipr) on the gunwale aft of each thwart, and the steering oar on the starboard
    # quarter on its withy, its tiller reaching inboard
    for i, y in enumerate((0.9, -0.7)):
        w = half_width_at(y, 0.05)
        box(f'thwart_{i}', (-w, y - 0.13, 0.02), (w, y + 0.13, 0.065), WOOD, grain=(1, 0, 0), bevel=0.008)
        for s in (1, -1):
            box(f'knee_{i}_{s}', (s * (w - 0.08) - 0.025, y - 0.03, -0.12), (s * (w - 0.08) + 0.025, y + 0.03, 0.02), OAK)
    for i, (x, z) in enumerate(((-0.24, 0.1), (-0.08, 0.1), (0.08, 0.1), (0.24, 0.1))):
        y0 = -1.55 + 0.1 * i
        pole(f'oar_{i}', (x, y0, z), (x * 0.9, y0 + 2.25, z + 0.01), 0.022, WOOD, seed=10 + i, bend=0.004)
        bl = box(f'oarblade_{i}', (x - 0.06, y0 + 2.2, z - 0.006), (x + 0.06, y0 + 2.75, z + 0.006), WOOD, grain=(0, 1, 0), bevel=0.004)
    for i, y in enumerate((0.9 - 0.42, -0.7 - 0.42)):
        for s in (1, -1):
            p = section(y, 1.0)
            box(f'keip_{i}_{s}', (s * p.x - 0.03, y - 0.12, p.z + 0.03), (s * p.x + 0.03, y + 0.12, p.z + 0.09), OAK, bevel=0.01)
            box(f'thole_{i}_{s}', (s * p.x - 0.012, y + 0.02, p.z + 0.08), (s * p.x + 0.012, y + 0.05, p.z + 0.2), OAK)
    # steering oar: hung outside the starboard side near the stern, its loom upright and a little raked, the blade below the keel's line
    y = -2.25; p = section(y, 1.0); x = p.x + 0.07
    box('wart', (p.x - 0.01, y - 0.09, p.z - 0.32), (p.x + 0.05, y + 0.09, p.z - 0.05), OAK, bevel=0.01)
    top, low = V((x, y + 0.05, p.z + 0.42)), V((x, y - 0.12, -0.25))
    pole('oar_steer', top, low, 0.035, WOOD, seed=30, bend=0.003)
    blade = box('steerblade', (x - 0.02, y - 0.38, -0.62), (x + 0.02, y - 0.02, -0.2), WOOD, grain=(0, 0, 1), bevel=0.008)
    turned(blade, Matrix.Rotation(math.radians(-14), 3, 'X'), (x, y - 0.12, -0.25))
    pole('tiller', top + V((0, 0, -0.06)), top + V((-0.5, 0.06, -0.08)), 0.018, WOOD, seed=31, bend=0.002)
    box('withy', (x - 0.05, y + 0.0, p.z - 0.04), (x + 0.03, y + 0.1, p.z + 0.03), TAR)

def slipway():
    # the stocks: two log runners on cross sleepers, three keel blocks down the middle (the boat's keel lies on them), and a shore each
    # side propping the hull at its turn of the bilge
    for i, y in enumerate((-3.3, -1.1, 1.1, 3.3)):
        pole(f'sleeper_{i}', (-1.55, y, 0.09), (1.55, y + 0.04, 0.09), 0.09, OAK, seed=40 + i, bend=0.02)
    for s in (1, -1):
        pole(f'log_{s}', (s * 1.0, -4.0, 0.25), (s * 1.0, 4.0, 0.25), 0.11, WOOD, seed=50 + s, bend=0.03)
    for i, y in enumerate((-1.8, 0.0, 1.8)):
        box(f'block_{i}a', (-0.3, y - 0.22, 0.14), (0.3, y + 0.22, 0.36), OAK, grain=(1, 0, 0), bevel=0.015, jitter=0.01, seed=60 + i)
        box(f'block_{i}b', (-0.2, y - 0.17, 0.36), (0.2, y + 0.17, BLOCK_TOP), OAK, grain=(0, 1, 0), bevel=0.015, jitter=0.006, seed=70 + i)
    # (the shores: the hull, set on the blocks, has its waterline BLOCK_TOP - KEEL_BOTTOM above the ground)
    up = BLOCK_TOP - KEEL_BOTTOM
    for s in (1, -1):
        for j, y in enumerate((-0.9, 0.9)):
            p = section(y, 0.42); top = V((s * (p.x + 0.04), y, p.z + up))
            pole(f'shore_{s}_{j}', (s * 1.75, y, 0.05), top, 0.05, WOOD, seed=80 + j + (s > 0) * 5, bend=0.01)
            box(f'chock_{s}_{j}', (s * 1.75 - 0.12, y - 0.1, 0.0), (s * 1.75 + 0.12, y + 0.1, 0.1), OAK, bevel=0.01)

BLOCK_TOP = 0.55

def finish(base, views):
    export(base)
    if 'render' in sys.argv and __name__ != '__bake__': preview(base, views)

def export(base):
    deps = bpy.context.evaluated_depsgraph_get()
    verts, norms, uvs, tris, index = [], [], [], [[] for _ in mats], {}
    for ob in list(bpy.context.scene.collection.objects):
        if ob.type != 'MESH': continue
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(80 if ob.name.startswith(SMOOTH) else 35))
        me.update()
        grain = V(ob['grain']).normalized() if 'grain' in ob else None
        cn = me.corner_normals
        for p in me.polygons:
            slot = mats.index(me.materials[p.material_index]) if me.materials else WOOD
            corner = []
            fn = p.normal
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                n = cn[li].vector
                if grain is not None and abs(fn.dot(grain)) < 0.7:
                    across = fn.cross(grain).normalized()
                    u, v = co.dot(across), co.dot(grain)
                else: u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + base + '.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    xs, ys, zs = verts[0::3], verts[1::3], verts[2::3]
    print(base, 'verts', len(verts) // 3, 'tris', sum(len(t) for t in tris) // 3, 'x', round(min(xs), 3), round(max(xs), 3), 'y', round(min(ys), 3), round(max(ys), 3), 'z', round(min(zs), 3), round(max(zs), 3))

COLS = [(0.5, 0.36, 0.22, 1), (0.32, 0.22, 0.13, 1), (0.05, 0.04, 0.035, 1), (0.1, 0.1, 0.11, 1), (0.8, 0.75, 0.62, 1)]
# the sail's wool: vertical stripes, red and white, STRIPE m wide, laid on by x in the part's own frame (bake_conf.py does the same)
STRIPE, WHITE, RED = 0.8, (0.86, 0.82, 0.72), (0.58, 0.12, 0.09)
def striped(m, white, red):
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    tc = nt.nodes.new('ShaderNodeTexCoord'); sx = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['Object'], sx.inputs[0])
    dv = nt.nodes.new('ShaderNodeMath'); dv.operation = 'DIVIDE'; dv.inputs[1].default_value = STRIPE; nt.links.new(sx.outputs['X'], dv.inputs[0])
    md = nt.nodes.new('ShaderNodeMath'); md.operation = 'FLOORED_MODULO'; md.inputs[1].default_value = 2.0; nt.links.new(dv.outputs[0], md.inputs[0])
    st = nt.nodes.new('ShaderNodeMath'); st.operation = 'GREATER_THAN'; st.inputs[1].default_value = 1.0; nt.links.new(md.outputs[0], st.inputs[0])
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; nt.links.new(st.outputs[0], mix.inputs['Factor'])
    mix.inputs['A'].default_value = (*white, 1); mix.inputs['B'].default_value = (*red, 1); nt.links.new(mix.outputs['Result'], b.inputs['Base Color'])
def preview(base, views, ground=None, size=20):
    # yaw 0 looks at the bow (from +y), 90 from starboard
    for m, c in zip(mats, COLS):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = c; b.inputs['Roughness'].default_value = 0.85
        if m.name == 'wool': striped(m, tuple(x ** 2.2 for x in WHITE), tuple(x ** 2.2 for x in RED))
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH':
            for p in ob.data.polygons: p.use_smooth = True
            ob.data.set_sharp_from_angle(angle=math.radians(80 if ob.name.startswith(SMOOTH) else 35))
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 800, 500
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    if ground is None: ground = -0.002 if base in ('slipway', 'onslip') else -0.5
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, ground)); g = bpy.context.active_object; gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.36, 0.22, 1); g.data.materials.append(gm)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 2.6; sun.rotation_euler = (math.radians(50), 0, math.radians(150)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 35
    for yaw, d, z, look in views:
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, math.cos(a) * d, z)
        cam.rotation_euler = (V((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'{base}_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

HULL_VIEWS = [(60, 7.5, 3.0, 0.0), (90, 6.5, 0.6, 0.1), (160, 5.5, 3.5, 0.0)]
def faering_keel(): fresh(); keel(); finish('faering_keel', HULL_VIEWS)
def faering_ribs(): fresh(); ribs(); finish('faering_ribs', HULL_VIEWS)
def faering_strakes(): fresh(); strakes(); finish('faering_strakes', HULL_VIEWS)
def faering_fit(): fresh(); fit(); finish('faering_fit', HULL_VIEWS)
def slipway_(): fresh(); slipway(); finish('slipway', [(60, 9.0, 4.0, 0.3), (90, 7.0, 1.2, 0.4)])
def faering():
    # (all four together: a preview of the finished boat, not shipped)
    fresh(); keel(); ribs(); strakes(); fit()
    if 'render' in sys.argv and __name__ != '__bake__': preview('faering', HULL_VIEWS + [(0, 5.5, 2.5, 0.0), (200, 3.0, 2.2, 0.0)])

def onslip():
    # (the finished boat set on the slipway: a preview of how they meet, not shipped)
    fresh(); keel(); ribs(); strakes(); fit()
    for ob in bpy.context.scene.collection.objects: ob.data.transform(Matrix.Translation((0, 0, BLOCK_TOP - KEEL_BOTTOM)))
    slipway()
    if 'render' in sys.argv and __name__ != '__bake__': preview('onslip', [(60, 9.0, 4.0, 0.6), (90, 7.0, 1.0, 0.6), (5, 6.0, 1.0, 0.6)])

# ---------------- the knarr ----------------
# A Skuldelev 1-style cargo ship, 16 m long and 4.8 m in the beam, built in the same stages on the same slipway (which carries its middle
# 8 m; the cradle holds the rest). Same frame and materials as the faering; slot 4 is the sail's wool (red and white stripes).
# The hull's lines: the keel runs straight to a 5.0, the stems curve up from there on an arc (centre (5.0, K_ARC_Z), radius 2.9) to
# stand upright at a 7.9, then rise to their plain heads at z ~3.0, curled a little inboard. Planking ends in the stems at K_END.
K_KEEL_BOTTOM, K_KEEL_H, K_KEEL_W = -1.10, 0.25, 0.20
K_ARC_Y, K_ARC_R = 5.0, 2.9
K_ARC_Z = K_KEEL_BOTTOM + K_KEEL_H / 2 + K_ARC_R
K_END = 7.85
K_STRAKES = 8
K_PLANK = 0.03
K_DECK, K_HOLD, K_GROUND = 0.35, -0.60, -1.65      # (deck surface, hold floor surface, the slipway's ground in the hull frame)
K_MAST_Y = 0.6                                       # (the mast's axis, Blender y = the game's z)
K_AFT, K_FORE = (-6.8, -3.6), (4.0, 6.8)             # (the half-decks)
K_YARD_Z, K_YARD_DY = 11.0, 0.24                     # (the yard's height; how far forward of the mast's axis it hangs)
K_SAIL_TOP, K_SAIL_FOOT, K_SAIL_W, K_BELLY = 10.9, 3.5, 9.6, 0.6

def k_stem_z(a):
    if a <= K_ARC_Y: return K_KEEL_BOTTOM + K_KEEL_H / 2
    d = min(a - K_ARC_Y, K_ARC_R)
    return K_ARC_Z - math.sqrt(max(0.0, K_ARC_R * K_ARC_R - d * d))
def k_keel_under(a):
    # the underside of the keel and stem at a (for the cradle's blocks)
    if a <= K_ARC_Y: return K_KEEL_BOTTOM
    r = K_ARC_R + K_KEEL_H / 2; d = min(a - K_ARC_Y, r)
    return K_ARC_Z - math.sqrt(max(0.0, r * r - d * d))
def k_rabbet(a): return k_stem_z(a) + K_KEEL_H / 2
def k_sheer(a): return 1.0 + 0.6 * (min(a, K_END) / K_END) ** 4
def k_beam(a): return 2.4 * max(0.0, 1 - (min(a, K_END) / K_END) ** 4) ** 0.75
def k_section(y, u):
    a = abs(y); zb, zt, B = k_rabbet(a), k_sheer(a), k_beam(a)
    return V((B * math.sin(u * math.pi / 2) ** 0.65, y, zb + (zt - zb) * (0.25 * u + 0.75 * u * u)))
def k_outward(y, u):
    e = 0.01; p0, p1 = k_section(y, max(0, u - e)), k_section(y, min(1, u + e))
    t = p1 - p0; n = V((t.z, 0, -t.x))
    return n.normalized() if n.length > 1e-6 else V((1, 0, 0))
def k_out_at(y, z):
    # how far out the hull's outer skin is at height z (station y)
    prev = k_section(y, 0)
    if z <= prev.z: return 0.0
    for i in range(1, 401):
        p = k_section(y, i / 400)
        if p.z >= z: return prev.x + (p.x - prev.x) * (z - prev.z) / max(1e-9, p.z - prev.z)
        prev = p
    return prev.x
def k_in_at(y, z): return k_out_at(y, z) - K_PLANK - 0.01

def sweep(name, pts, side, w, h, mat, grain=None):
    # a timber of rectangular section along a curve lying in the plane square to `side`: w across (along side), h in the curve's plane
    side = V(side).normalized(); n_ = len(pts)
    ws = w if isinstance(w, (list, tuple)) else [w] * n_; hs = h if isinstance(h, (list, tuple)) else [h] * n_
    bm = bmesh.new(); rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n_ - 1)] - pts[max(i - 1, 0)]).normalized(); nn = side.cross(t).normalized()
        a, b = side * ws[i] / 2, nn * hs[i] / 2
        rings.append([bm.verts.new(p + a + b), bm.verts.new(p - a + b), bm.verts.new(p - a - b), bm.verts.new(p + a - b)])
    for i in range(n_ - 1):
        for k in range(4): bm.faces.new((rings[i][k], rings[i][(k + 1) % 4], rings[i + 1][(k + 1) % 4], rings[i + 1][k]))
    bm.faces.new(rings[0][::-1]); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, mat, grain if grain is not None else pts[-1] - pts[0])

def taper(name, pts, radii, mat, res=2):
    # a round pole along pts, its radius per point
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = 1.0; cu.bevel_resolution = res; cu.use_fill_caps = True
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for k, (p, r) in enumerate(zip(pts, radii)): sp.points[k].co = (p.x, p.y, p.z, 1); sp.points[k].radius = r
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o['grain'] = tuple(pts[-1] - pts[0])
    return o

def hexa(name, lo4, hi4, mat, grain=None):
    # a six-sided block from its four bottom and four top corners (each in order round the block)
    bm = bmesh.new(); b = [bm.verts.new(V(p)) for p in lo4]; t = [bm.verts.new(V(p)) for p in hi4]
    bm.faces.new(b[::-1]); bm.faces.new(t)
    for k in range(4): bm.faces.new((b[k], b[(k + 1) % 4], t[(k + 1) % 4], t[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, mat, grain)

def rope(name, a, b, r=0.022, mat=TAR):
    a, b = V(a), V(b); return tube(name, [a, a.lerp(b, 0.5) + V((0, 0, -0.03 * (a - b).length / 10)), b], r, mat, res=1)

def k_keel():
    # the keel, and the two stems scarfed to its ends: curving up, standing upright, rising to a plain rounded head curled a little
    # inboard; a carved groove (decal) along each side of the head
    box('keel', (-K_KEEL_W / 2, -K_ARC_Y - 0.06, K_KEEL_BOTTOM), (K_KEEL_W / 2, K_ARC_Y + 0.06, K_KEEL_BOTTOM + K_KEEL_H), OAK, grain=(0, 1, 0), bevel=0.015)
    for s, name in ((1, 'stem'), (-1, 'stem_stern')):
        pts, w, h = [], [], []
        for i in range(15):
            th = math.radians(90 * i / 14)
            pts.append(V((0, s * (K_ARC_Y + K_ARC_R * math.sin(th)), K_ARC_Z - K_ARC_R * math.cos(th)))); w.append(0.22); h.append(K_KEEL_H)
        head = [(7.92, 2.35, 0.2, 0.24), (7.91, 2.65, 0.19, 0.22), (7.85, 2.86, 0.18, 0.2), (7.73, 2.98, 0.17, 0.18), (7.6, 3.0, 0.16, 0.16), (7.52, 2.95, 0.15, 0.14)]
        for a, z, ww, hh in head: pts.append(V((0, s * a, z))); w.append(ww); h.append(hh)
        sweep(name, pts, (1, 0, 0), w, h, OAK)
        for side in (1, -1):
            g = [V((side * 0.0, s * a, z)) for a, z, _, _ in head[:-1]]
            bm = bmesh.new(); prev = None
            for i, p in enumerate(g):
                ww = head[i][2] / 2 + 0.003
                t = (g[min(i + 1, len(g) - 1)] - g[max(i - 1, 0)]).normalized(); nn = V((1, 0, 0)).cross(t).normalized()
                cur = (bm.verts.new(V((side * ww, p.y, p.z)) + nn * 0.02), bm.verts.new(V((side * ww, p.y, p.z)) + nn * 0.045))
                if prev: bm.faces.new((prev[0], cur[0], cur[1], prev[1]))
                prev = cur
            for f in bm.faces:
                if f.normal.x * side < 0: f.normal_flip()
            link(f'seam_carve_{s}_{side}', bm, TAR)

K_FRAMES = [-6.5 + 1.3 * i for i in range(11)]
K_BEAMS = (-6.65, -5.15, -3.7, K_MAST_Y, 4.15, 5.45, 6.65)
FLOOR_TOP = K_HOLD - 0.045          # (the floor timbers' flat tops, under the hold's loose planks)
def k_floor_pts(y, u):
    p = k_section(y, u); o = k_outward(y, u)
    bot = p - o * K_PLANK; top = p - o * (K_PLANK + 0.07)
    top.z = max(top.z, FLOOR_TOP)
    return bot, top
def k_ribs():
    # the frames: a floor timber across the bottom over the keel (flat on top, where the hold's planks lie), a futtock each side up the
    # planking to under the gunwale beside it; crossbeams under the half-decks and the mast beam (the mast partner lies on it)
    U = 0.42
    for i, y in enumerate(K_FRAMES):
        us = [U * (1 - k / 4) for k in range(4)] + [0.0] + [U * k / 4 for k in range(1, 5)]
        sgn = [-1] * 4 + [1] + [1] * 4
        bm = bmesh.new(); rows = []
        for u, sg in zip(us, sgn):
            b, t = k_floor_pts(y, u)
            rows.append([bm.verts.new(V((sg * b.x, y + d, b.z))) for d in (-0.07, 0.07)] + [bm.verts.new(V((sg * t.x, y + d, t.z))) for d in (-0.07, 0.07)])
        for j in range(len(rows) - 1):
            a, c = rows[j], rows[j + 1]
            for q in ((0, 1), (2, 3), (0, 2), (1, 3)):
                bm.faces.new((a[q[0]], c[q[0]], c[q[1]], a[q[1]]))
        bm.faces.new((rows[0][0], rows[0][1], rows[0][3], rows[0][2])); bm.faces.new((rows[-1][0], rows[-1][2], rows[-1][3], rows[-1][1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        link(f'rib_floor_{i}', bm, OAK, grain=(1, 0, 0))
        side = [k_section(y + 0.14, 0.3 + 0.67 * k / 3) - k_outward(y + 0.14, 0.3 + 0.67 * k / 3) * (K_PLANK + 0.05) for k in range(4)]
        mirrored(sweep(f'futtock_{i}', side, (0, 1, 0), 0.12, 0.1, OAK))
    for i, y in enumerate(K_BEAMS):
        w = k_in_at(y, 0.23)
        box(f'beam_{i}', (-w, y - 0.09, K_DECK - 0.2), (w, y + 0.09, K_DECK - 0.04), OAK, grain=(1, 0, 0))

def k_strakes():
    # eight clinker strakes a side, each lapping over the one below outside, a tarred seam along each lap; the top (sheer) strake
    # heavier, and a gunwale timber along it. The hull is closed, from stem to stem.
    n = 30
    ys = [-K_END + 2 * K_END * (0.5 - 0.5 * math.cos(math.pi * i / n)) for i in range(n + 1)]
    for k in range(K_STRAKES):
        u0, u1 = max(0.0, k / K_STRAKES - 0.03), (k + 1) / K_STRAKES
        bm = bmesh.new(); rows = []
        for y in ys:
            row = []
            for j in range(2):
                v = j; u = u0 + (u1 - u0) * v
                row.append(bm.verts.new(k_section(y, u) + k_outward(y, u) * (0.025 * (1 - v) * (1 if k else 0.4))))
            rows.append(row)
        for i in range(n):
            for j in range(1): bm.faces.new((rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1]))
        for f in bm.faces:
            if f.normal.x < 0: f.normal_flip()
        o = link(f'strake_{k}', bm, WOOD, grain=(0, 1, 0))
        m = o.modifiers.new('solid', 'SOLIDIFY'); m.thickness = 0.045 if k == K_STRAKES - 1 else K_PLANK; m.offset = -1
        mirrored(o)
        if k:
            bm = bmesh.new(); prev = None
            for y in ys[2:-2]:
                u = u0 + 0.003
                a = k_section(y, u) + k_outward(y, u) * 0.0265; b = k_section(y, u + 0.012) + k_outward(y, u + 0.012) * 0.0265
                cur = (bm.verts.new(a), bm.verts.new(b))
                if prev: bm.faces.new((prev[0], cur[0], cur[1], prev[1]))
                prev = cur
            for f in bm.faces:
                if f.normal.x < 0: f.normal_flip()
            mirrored(link(f'seam_{k}', bm, TAR))
    pts = [k_section(y, 1.0) + V((0, 0, 0.02)) for y in ys[1:-1]]
    mirrored(tube('gunwale', pts, 0.05, OAK, square=True))

K_LADDER_Y, K_LADDER_FOOT = -1.0, -0.3
def k_ladder_line():
    # the ladder's top (hooked over the gunwale) and foot (x, z), clear of the hull's outer skin all the way down
    zt = k_sheer(abs(K_LADDER_Y)) + 0.11; xt = k_section(K_LADDER_Y, 1.0).x + 0.11
    xf = xt
    while True:
        ok = all(xt + (xf - xt) * (zt - z) / (zt - K_LADDER_FOOT) >= k_out_at(K_LADDER_Y, z) + 0.025 + 0.05 for z in [K_LADDER_FOOT + 0.05 * i for i in range(int((zt - K_LADDER_FOOT) / 0.05))])
        if not ok or xf < 1.5: break
        xf -= 0.01
    return (xt, zt), (xf + 0.01, K_LADDER_FOOT)
K_CHESTS = ((0.95, -5.4, 'helm'), (-0.95, -4.7, 'aft_port'), (0.95, 5.1, 'fore_stbd'), (-0.95, 5.1, 'fore_port'))
def k_deck():
    # the half-decks fore and aft: planks fore-and-aft on the crossbeams, cut to the hull, a waterway timber along the hull; the hold
    # between them open, its floor loose planks on the floor timbers; lashing cleats on the futtocks; sea chests (seats) on the decks;
    # boarding ladders over both sides amidships
    for name, (z0, z1), inner in (('aft', K_AFT, K_AFT[1]), ('fore', K_FORE, K_FORE[0])):
        outer = z0 if inner == z1 else z1; sgn = 1 if outer > inner else -1
        W = k_in_at(inner, K_DECK - 0.04); n = 2 * max(1, round(W / 0.26)); xs = [-W + 2 * W * i / n for i in range(n + 1)]
        def zend(x):
            z = inner
            while (z - outer) * sgn < 0 and k_in_at(z + sgn * 0.01, K_DECK - 0.04) >= abs(x): z += sgn * 0.01
            return z
        for i in range(n):
            xa, xb = xs[i] + 0.004, xs[i + 1] - 0.004
            za, zb = zend(xa), zend(xb)
            c = [(xa, inner), (xb, inner), (xb, zb), (xa, za)]
            if sgn < 0: c = c[::-1]
            hexa(f'deck_{name}_{i}', [(x, z, K_DECK - 0.04) for x, z in c], [(x, z, K_DECK) for x, z in c], WOOD, grain=(0, 1, 0))
        ws = [inner + (outer - inner) * k / 10 for k in range(11)]
        mirrored(tube(f'waterway_{name}', [V((k_in_at(z, K_DECK) - 0.03, z, K_DECK + 0.02)) for z in ws], 0.04, OAK, square=True))
    # the hold's floor: where the floor timbers are flat under it
    W = min(max(x / 200 for x in range(0, 400) if all(k_out_at(y, FLOOR_TOP + 0.1) > x / 200 and k_floor_top_at(y, x / 200) <= FLOOR_TOP + 1e-4 for y in (-3.3, 0.0, 3.3))), 1.6) - 0.02
    n = 8; rr = random.Random(7)
    for i in range(n):
        xa, xb = -W + 2 * W * i / n + 0.006, -W + 2 * W * (i + 1) / n - 0.006
        box(f'hold_{i}', (xa, K_AFT[1] + 0.06 + rr.uniform(0, 0.08), FLOOR_TOP), (xb, K_FORE[0] - 0.06 - rr.uniform(0, 0.08), K_HOLD), WOOD, grain=(0, 1, 0), bevel=0.006)
    for i, y in enumerate((-2.6 + 0.14, 0.14, 2.6 + 0.14)):
        x = k_in_at(y, 0.0) - 0.1
        box(f'cleat_{i}', (x - 0.06, y - 0.07, -0.05), (x, y + 0.07, 0.05), OAK, bevel=0.01)
        box(f'cleatbar_{i}', (x - 0.1, y - 0.18, -0.02), (x - 0.06, y + 0.18, 0.02), OAK, grain=(0, 1, 0), bevel=0.008)
        mirrored(bpy.data.objects[f'cleat_{i}']); mirrored(bpy.data.objects[f'cleatbar_{i}'])
    for x, y, name in K_CHESTS:
        lo, hi = V((x - 0.45, y - 0.23, K_DECK)), V((x + 0.45, y + 0.23, K_DECK + 0.42))
        box(f'chest_{name}', lo, hi, OAK, grain=(1, 0, 0), bevel=0.02)
        for bx in (x - 0.3, x + 0.3):
            bm = bmesh.new()   # (an iron band over the lid and down the sides: a mark on the surface)
            q = [bm.verts.new(V(p)) for p in ((bx - 0.025, y - 0.232, K_DECK + 0.05), (bx + 0.025, y - 0.232, K_DECK + 0.05), (bx + 0.025, y - 0.232, K_DECK + 0.423), (bx - 0.025, y - 0.232, K_DECK + 0.423),
                                                (bx - 0.025, y + 0.232, K_DECK + 0.423), (bx + 0.025, y + 0.232, K_DECK + 0.423), (bx + 0.025, y + 0.232, K_DECK + 0.05), (bx - 0.025, y + 0.232, K_DECK + 0.05))]
            bm.faces.new((q[0], q[1], q[2], q[3])); bm.faces.new((q[3], q[2], q[5], q[4])); bm.faces.new((q[4], q[5], q[6], q[7]))
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            for f in bm.faces:
                c = f.calc_center_median() - V((bx, y, K_DECK + 0.21))
                if f.normal.dot(c) < 0: f.normal_flip()
            link(f'seam_band_{name}_{bx:.2f}', bm, IRON)
    (xt, zt), (xf, zf) = k_ladder_line()
    y = K_LADDER_Y; gx = k_section(y, 1.0).x
    parts = []
    for d in (-0.22, 0.22):
        parts.append(pole(f'lrail_{d}', (xf, y + d, zf), (xt, y + d, zt), 0.035, WOOD, seed=90, bend=0.004))
        parts.append(pole(f'lrail_hook_{d}', (xt, y + d, zt), (gx - 0.12, y + d, zt), 0.03, WOOD, seed=91, bend=0.002))
        parts.append(pole(f'lrail_tip_{d}', (gx - 0.12, y + d, zt + 0.01), (gx - 0.12, y + d, zt - 0.18), 0.03, WOOD, seed=92, bend=0.002))
    for i in range(5):
        t = (i + 0.5) / 5; z = zf + (zt - zf) * t; x = xf + (xt - xf) * t
        parts.append(box(f'rung_{i}', (x - 0.03, y - 0.24, z - 0.02), (x + 0.03, y + 0.24, z + 0.02), WOOD, grain=(0, 1, 0), bevel=0.006))
    for p in parts: mirrored(p)
def k_floor_top_at(y, x):
    # the floor timbers' top at x (from their own construction)
    prev = None
    for i in range(0, 201):
        b, t = k_floor_pts(y, 0.42 * i / 200)
        if t.x >= x: return t.z
    return 9.0

K_OAR_Y = -6.4
def k_oar():
    y = K_OAR_Y; p = k_section(y, 1.0); xo = p.x + 0.25
    return y, p, xo
def k_mast():
    # everything fixed to the hull: the mast step on the floor timbers, the mast partner (fish) on the mast beam at the deck, the
    # shrouds down to the gunwales on deadeyes, the forestay to the stem, and the steering oar on its boss on the starboard quarter
    my = K_MAST_Y
    box('step', (-0.2, my - 0.9, FLOOR_TOP - 0.06), (0.2, my + 0.9, FLOOR_TOP + 0.2), OAK, grain=(0, 1, 0), bevel=0.03)
    box('fish', (-0.27, my - 0.95, K_DECK - 0.04), (0.27, my + 0.95, K_DECK + 0.16), OAK, grain=(0, 1, 0), bevel=0.03)
    for s in (1, -1):
        for j, y in enumerate((0.3, -0.6)):
            g = k_section(y, 1.0); top = V((g.x - 0.02, y, g.z + 0.07))
            box(f'deadeye_{s}_{j}', (s * top.x - 0.06, y - 0.07, top.z), (s * top.x + 0.06, y + 0.07, top.z + 0.14), OAK, bevel=0.02)
            rope(f'shroud_{s}_{j}', (s * 0.06, my, 10.45), (s * top.x, y, top.z + 0.12))
    rope('stay_fore', (0, my + 0.06, 11.75), (0, 7.86, 2.55))
    y, p, xo = k_oar()
    box('wart', (p.x - 0.05, y - 0.22, p.z - 0.62), (xo - 0.04, y + 0.22, p.z - 0.18), OAK, grain=(0, 0, 1), bevel=0.04)
    top, low = V((xo, y + 0.04, 1.62)), V((xo, y - 0.1, -0.35))
    pole('oar_steer', top, low, 0.065, WOOD, seed=30, bend=0.004)
    blade = box('steerblade', (xo - 0.04, y - 0.48, -1.2), (xo + 0.04, y - 0.02, -0.25), WOOD, grain=(0, 0, 1), bevel=0.015)
    turned(blade, Matrix.Rotation(math.radians(-12), 3, 'X'), (xo, y - 0.1, -0.35))
    pole('tiller', top + V((0, 0, -0.2)), V((xo - 1.1, y + 0.45, 1.42)), 0.04, WOOD, seed=31, bend=0.003)
    box('withy', (p.x - 0.03, y - 0.06, p.z + 0.02), (xo + 0.09, y + 0.12, p.z + 0.1), TAR)

def k_mast_r(z): return 0.18 - 0.09 * (z + 0.6) / 12.6
def k_yard():
    # what turns about the mast's axis: the mast itself, the yard hung forward of it on a parrel, the sail furled tight under the yard,
    # and the halyard from its block on the yard up to the masthead
    my = K_MAST_Y; yz = my + K_YARD_DY
    zs = [-0.6, 3.0, 7.0, 10.0, 12.0]
    taper('mast', [V((0, my, z)) for z in zs], [k_mast_r(z) for z in zs], WOOD, res=2)
    taper('truck', [V((0, my, 11.97)), V((0, my, 12.07))], [0.11, 0.11], OAK, res=2)
    taper('yard', [V((x, yz, K_YARD_Z)) for x in (-5.0, -2.5, 0.0, 2.5, 5.0)], [0.06, 0.09, 0.11, 0.09, 0.06], WOOD, res=2)
    taper('furl', [V((x, yz + 0.02, K_YARD_Z - 0.19)) for x in (-4.75, -4.4, -2.0, 0.0, 2.0, 4.4, 4.75)], [0.03, 0.07, 0.09, 0.1, 0.09, 0.07, 0.03], WOOL, res=2)
    box('parrel', (-0.12, my + k_mast_r(K_YARD_Z) - 0.01, K_YARD_Z - 0.07), (0.12, yz - 0.09, K_YARD_Z + 0.07), OAK, bevel=0.015)
    box('block_halyard', (-0.05, yz - 0.06, K_YARD_Z + 0.09), (0.05, yz + 0.06, K_YARD_Z + 0.26), OAK, bevel=0.015)
    rope('halyard', (0.0, yz, K_YARD_Z + 0.25), (0.05, my + 0.04, 11.96), r=0.02)

def k_sail():
    # the square sail set: hung from the yard (its head under the yard), 9.6 m wide, bellied forward, red and white stripes
    cols, rows = 16, 10; yz = K_MAST_Y + K_YARD_DY
    bm = bmesh.new(); g = []
    for j in range(rows + 1):
        s = j / rows; z = K_SAIL_TOP + (K_SAIL_FOOT - K_SAIL_TOP) * s
        g.append([])
        for i in range(cols + 1):
            x = -K_SAIL_W / 2 + K_SAIL_W * i / cols
            belly = K_BELLY * (1 - (2 * x / K_SAIL_W) ** 2) * math.sin(math.pi * 0.75 * s) / math.sin(math.pi * 0.75 * 0.667)
            g[-1].append(bm.verts.new(V((x, yz + belly, z))))
    for j in range(rows):
        for i in range(cols): bm.faces.new((g[j][i], g[j][i + 1], g[j + 1][i + 1], g[j + 1][i]))
    for f in bm.faces:
        if f.normal.y < 0: f.normal_flip()
    o = link('sail', bm, WOOL, grain=(0, 0, 1))
    m = o.modifiers.new('solid', 'SOLIDIFY'); m.thickness = 0.025; m.offset = 0

def k_cradle():
    # what holds the knarr on the slipway beyond the slipway's ends: cribbed keel blocks at a 5.5 and 7.0 each end, from the ground up to
    # the keel and the stems (their tops cut to the curve), and three pairs of side shores to the bilge, each on a chock with a pad
    g = K_GROUND; rr = random.Random(5)
    for s in (1, -1):
        for a in (5.5, 7.0):
            y = s * a
            box(f'cblock_{s}_{a}_base', (-0.45, y - 0.25, g), (0.45, y + 0.25, g + 0.22), OAK, grain=(1, 0, 0), bevel=0.015, jitter=0.01, seed=rr.randint(0, 99))
            z = g + 0.22; top_lo = min(k_keel_under(a - 0.2), k_keel_under(a + 0.2))
            k = 0
            while top_lo - z > 0.45:
                h = min(0.3, top_lo - z - 0.2)
                if k % 2 == 0: box(f'cblock_{s}_{a}_{k}', (-0.18, y - 0.36, z), (0.18, y + 0.36, z + h), OAK, grain=(0, 1, 0), bevel=0.012, jitter=0.006, seed=rr.randint(0, 99))
                else: box(f'cblock_{s}_{a}_{k}', (-0.34, y - 0.18, z), (0.34, y + 0.18, z + h), OAK, grain=(1, 0, 0), bevel=0.012, jitter=0.006, seed=rr.randint(0, 99))
                z += h; k += 1
            c = [(-0.16, y - 0.2), (0.16, y - 0.2), (0.16, y + 0.2), (-0.16, y + 0.2)]
            hexa(f'cblock_{s}_{a}_top', [(x, yy, z) for x, yy in c], [(x, yy, k_keel_under(abs(yy)) + 0.005) for x, yy in c], OAK, grain=(0, 1, 0))
    for y in (-4.0, 0.0, 4.0):
        x = k_out_at(y, -0.5) + 0.02
        for s in (1, -1):
            pole(f'shore_{s}_{y}', (s * 2.6, y, g + 0.08), (s * (x + 0.05), y, -0.5), 0.06, WOOD, seed=rr.randint(0, 99), bend=0.01)
            box(f'chock_{s}_{y}', (s * 2.6 - 0.16, y - 0.12, g), (s * 2.6 + 0.16, y + 0.12, g + 0.1), OAK, bevel=0.01)
            pad = box(f'shorepad_{s}_{y}', (s * x - 0.0, y - 0.15, -0.58), (s * (x + 0.08), y + 0.15, -0.42), OAK, bevel=0.01)

K_STAGES = (k_keel, k_ribs, k_strakes, k_deck, k_mast, k_yard)
KNARR_VIEWS = [(60, 21.0, 7.0, 0.5), (90, 19.0, 1.5, 0.6), (160, 17.0, 9.0, 0.0)]
def shift(d):
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH': ob.data.transform(Matrix.Translation(d))
def knarr_kind(fn, base, d=None, views=KNARR_VIEWS, ground=-1.25):
    fresh(); fn()
    if d is not None: shift(d)
    export(base)
    if 'render' in sys.argv and __name__ != '__bake__':
        if d is not None: shift(-V(d))
        preview(base, views, ground=ground, size=80)
def knarr_keel(): knarr_kind(k_keel, 'knarr_keel')
def knarr_ribs(): knarr_kind(k_ribs, 'knarr_ribs')
def knarr_strakes(): knarr_kind(k_strakes, 'knarr_strakes')
def knarr_deck(): knarr_kind(k_deck, 'knarr_deck')
def knarr_mast(): knarr_kind(k_mast, 'knarr_mast')
# (the yard and sail turn about the mast's axis: their own origin is on it, hull (0, 0, 0.6); the sail's also at its head, y 10.9)
MAST_VIEWS = [(60, 24.0, 12.0, 7.0), (0, 20.0, 7.0, 7.0)]
def knarr_yard(): knarr_kind(k_yard, 'knarr_yard', V((0, -K_MAST_Y, 0)), MAST_VIEWS)
def knarr_sail(): knarr_kind(k_sail, 'knarr_sail', V((0, -K_MAST_Y, -K_SAIL_TOP)), MAST_VIEWS)
def knarr_cradle(): knarr_kind(k_cradle, 'knarr_cradle', views=KNARR_VIEWS, ground=K_GROUND - 0.002)
def knarr():
    # (the finished ship, sail set: a preview, not shipped)
    fresh()
    for f in K_STAGES + (k_sail,): f()
    if 'render' in sys.argv and __name__ != '__bake__':
        preview('knarr', [(60, 30.0, 9.0, 4.0), (125, 28.0, 5.0, 4.5), (200, 24.0, 13.0, 3.0), (90, 22.0, 2.0, 1.0), (20, 13.0, 13.0, -0.5), (215, 9.0, 4.0, 0.0), (35, 6.0, 11.6, 10.6), (160, 6.0, 12.0, 10.6)], ground=-1.25, size=80)
def knarr_onslip():
    # (the stages one by one on the slipway with the cradle, each added to the last: previews, not shipped)
    up = V((0, 0, BLOCK_TOP - K_KEEL_BOTTOM))
    for k in range(1, len(K_STAGES) + 1):
        fresh()
        for f in K_STAGES[:k] + (k_cradle,): f()
        shift(up); slipway()
        if 'render' in sys.argv and __name__ != '__bake__':
            preview(f'knarr_onslip{k}', [(60, 22.0, 8.0, 1.8), (100, 17.0, 2.0, 1.4)] + ([(20, 12.0, 11.0, 0.5)] if k in (3, 4, 6) else []), ground=-0.002, size=80)

KINDS = ('faering_keel', 'faering_ribs', 'faering_strakes', 'faering_fit', 'slipway', 'faering', 'onslip',
         'knarr_keel', 'knarr_ribs', 'knarr_strakes', 'knarr_deck', 'knarr_mast', 'knarr_yard', 'knarr_sail', 'knarr_cradle', 'knarr', 'knarr_onslip')
PREVIEW_ONLY = ('faering', 'onslip', 'knarr', 'knarr_onslip')
FUN = {'slipway': slipway_}
which = [a for a in sys.argv if a in KINDS] or [k for k in KINDS if k not in PREVIEW_ONLY]
for w in which: FUN.get(w, globals()[w])()
