# Shipyard's looks, made from scratch: the faering (a Gokstad-style four-oared boat) in the four stages it's built in on the slipway
# (keel and stems; the ribs; the clinker strakes and gunwale; the thwarts, oars, oarlocks and steering oar), and the slipway itself.
# Run: blender -b --python ~/.cache/shipyard/yard.py -- [render] [faering_keel|faering_ribs|faering_strakes|faering_fit|slipway|faering]
# (writes ~/.cache/shipyard/<name>.json.gz; previews with "render"; "faering" is all four hull stages together, for a preview only).
# What ships is the bake of these (bake_conf.py, docs/model-baking.md).
# Blender frame: z up, +y the bow (the game's +z), +x starboard. The hull's origin is on the centreline amidships at the WATERLINE
# (the game's y 0); the four stages share it and line up. The slipway stands on z 0 with its length along y.
# Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres; wood's grain runs along v), triangles per
# material: 0 wood (pine planking), 1 oak (keel, stems, ribs: darker), 2 tar (the tarred seams and the oar blades' edges), 3 iron.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/shipyard/'
WOOD, OAK, TAR, IRON = range(4)
NAMES = ('wood', 'oak', 'tar', 'iron')
SMOOTH = ('strake', 'stem', 'rib', 'gunwale', 'oar', 'log', 'shore', 'sleeper')
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

COLS = [(0.5, 0.36, 0.22, 1), (0.32, 0.22, 0.13, 1), (0.05, 0.04, 0.035, 1), (0.1, 0.1, 0.11, 1)]
def preview(base, views):
    # yaw 0 looks at the bow (from +y), 90 from starboard
    for m, c in zip(mats, COLS):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = c; b.inputs['Roughness'].default_value = 0.85
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH':
            for p in ob.data.polygons: p.use_smooth = True
            ob.data.set_sharp_from_angle(angle=math.radians(80 if ob.name.startswith(SMOOTH) else 35))
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 800, 500
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    ground = -0.002 if base in ('slipway', 'onslip') else -0.5
    bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, ground)); g = bpy.context.active_object; gm = bpy.data.materials.new('ground'); gm.use_nodes = True
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

KINDS = ('faering_keel', 'faering_ribs', 'faering_strakes', 'faering_fit', 'slipway', 'faering', 'onslip')
FUN = {'slipway': slipway_}
which = [a for a in sys.argv if a in KINDS] or [k for k in KINDS if k not in ('faering', 'onslip')]
for w in which: FUN.get(w, globals()[w])()
