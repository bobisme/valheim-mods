# Shared kit for the gods' altars (Jarldom 0.1.85+): mesh helpers, the common material slots, the export and the previews.
# Blender frame: z up, the altar's face (where the worshipper kneels) toward +y. Written in the game's frame (x, z, y), so the face is
# the piece's forward. Per corner position/normal/uv (uvs in metres), triangles per material slot (SLOTS, the same for every altar).
import bpy, bmesh, json, math, random, gzip
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

OUT = '/home/bob/.cache/jarldom-altars/'
SLOTS = ('stone', 'cut', 'wood', 'dark', 'iron', 'gold', 'bone', 'cloth', 'rope', 'glow', 'leaf', 'bloom', 'paint', 'hide')
(STONE, CUT, WOOD, DARK, IRON, GOLD, BONE, CLOTH, ROPE, GLOW, LEAF, BLOOM, PAINT, HIDE) = range(len(SLOTS))
SMOOTH = set()   # names of objects drawn smooth (the rest keep hard facets)
rng = random.Random(1)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in SLOTS]

def seed(n):
    rng.seed(n)

def link(name, bm, mat, smooth=False):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[mat])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    if smooth: SMOOTH.add(ob.name)
    return ob

def hull(name, pts, mat, limit=3, smooth=False):
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(limit), verts=bm.verts, edges=bm.edges)
    return link(name, bm, mat, smooth)

def box(name, centre, size, mat, turn=None):
    """A box: centre, full size (x, y, z), optionally turned (a Matrix 3x3)."""
    pts = []
    for x in (-1, 1):
        for y in (-1, 1):
            for z in (-1, 1):
                o = Vector((x * size[0] / 2, y * size[1] / 2, z * size[2] / 2))
                pts.append(Vector(centre) + (turn @ o if turn else o))
    return hull(name, pts, mat, 1)

def tube(name, pts, radius, mat, cyclic=False, taper=None, res=2, smooth=True):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = res
    cu.use_fill_caps = not cyclic
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1); sp.use_cyclic_u = cyclic
    for k, p in enumerate(pts):
        sp.points[k].co = (p.x, p.y, p.z, 1)
        if taper: sp.points[k].radius = taper(k / max(1, len(pts) - 1))
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    if smooth: SMOOTH.add(o.name)
    return o

def ring(z, r, n, jit=0.0, rot=0.0, at=Vector(), ry=None):
    ry = r if ry is None else ry
    return [at + Vector((math.cos(2 * math.pi * i / n + rot) * r * (1 + rng.uniform(-jit, jit)),
                         math.sin(2 * math.pi * i / n + rot) * ry * (1 + rng.uniform(-jit, jit)), z)) for i in range(n)]

def cyl(name, a, b, r, mat, n=10, r2=None, smooth=False):
    """A cylinder (a hull) from a to b, radius r (r2 at b)."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    q = d.to_track_quat('Z', 'Y').to_matrix()
    pts = []
    for p, rr in ((a, r), (b, r if r2 is None else r2)):
        for i in range(n):
            t = 2 * math.pi * i / n
            pts.append(p + q @ Vector((math.cos(t) * rr, math.sin(t) * rr, 0)))
    return hull(name, pts, mat, 1, smooth)

def ball(name, at, r, mat, squash=(1, 1, 1), n=8, smooth=True, turn=None):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=n, v_segments=max(4, n // 2 + 1), radius=r)
    for v in bm.verts:
        o = Vector((v.co.x * squash[0], v.co.y * squash[1], v.co.z * squash[2]))
        v.co = (turn @ o if turn else o) + Vector(at)
    return link(name, bm, mat, smooth)

def lump(name, at, size, mat=STONE, squash=0.65, n=16):
    """A broken rock: a hull of points scattered on a squashed ball."""
    pts = []
    for _ in range(n):
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        pts.append(Vector(at) + Vector((d.x * size * rng.uniform(0.78, 1.1), d.y * size * rng.uniform(0.78, 1.1), d.z * size * squash * rng.uniform(0.78, 1.1))))
    return hull(name, pts, mat, 6)

def weather(ob, voxel=0.02, big=0.06, small=0.015, ratio=0.15, seed_=1):
    """Remeshed fine, pushed in and out by noise, coarse and fine, and thinned back to a light mesh."""
    mat = ob.data.materials[0]
    rm = ob.modifiers.new('remesh', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = voxel
    for size, strength, nm in ((0.4, big, 'big'), (0.07, small, 'small')):
        tex = bpy.data.textures.new(nm + str(seed_), 'CLOUDS'); tex.noise_scale = size; tex.noise_depth = 2
        d = ob.modifiers.new(nm, 'DISPLACE'); d.texture = tex; d.strength = strength; d.mid_level = 0.5; d.texture_coords = 'GLOBAL'
        d.texture_coords_object = None
    dec = ob.modifiers.new('decimate', 'DECIMATE'); dec.ratio = ratio
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    ob.modifiers.clear(); ob.data = me; me.materials.clear(); me.materials.append(mat)
    return ob

def flatten(ob, axis, value, weight=0.7, sign=1):
    """Dress one face back toward flat: verts whose normal points along +axis*sign move toward value on that axis."""
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.normal_update()
    for v in bm.verts:
        w = max(0.0, v.normal[axis] * sign) ** 2
        if w > 0: v.co[axis] += (value - v.co[axis]) * weight * w
    bm.to_mesh(ob.data); bm.free()

def bvh(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.transform(ob.matrix_world); t = BVHTree.FromBMesh(bm); bm.free(); return t

def carve(tree, pts, origin_dir, w=0.01, mat=CUT, proud=0.006, cyclic=False, seg=0.035):
    """Cut lines onto a face: pts in the face's plane, each corner found by a ray along origin_dir (toward the face), in short pieces."""
    od = Vector(origin_dir).normalized()
    def on(p, lift):
        hit = tree.ray_cast(p - od * 2.0, od)
        q = hit[0] if hit[0] is not None else p
        return q - od * lift
    def stroke(a, b):
        d = (b - a)
        n = d.cross(od).normalized() * w
        q = []
        for p in (a, b):
            for o in (n, -n): q += [on(p + o, -0.008), on(p + o, proud)]
        hull('cut', q, mat, 1)
    for i in range(len(pts) - (0 if cyclic else 1)):
        a, b = Vector(pts[i]), Vector(pts[(i + 1) % len(pts)])
        k = max(1, math.ceil((b - a).length / seg))
        for j in range(k): stroke(a.lerp(b, j / k), a.lerp(b, (j + 1) / k))

def sweep(name, path, profile, mat, smooth=False, cap=True, fixed=None):
    """A profile (list of (x, y) in the path's normal plane) swept along a path; profile may be a function of t returning such a list."""
    bm = bmesh.new(); rings_ = []
    for i, p in enumerate(path):
        a, b = path[max(0, i - 1)], path[min(len(path) - 1, i + 1)]
        d = (Vector(b) - Vector(a)).normalized()
        if fixed is not None: side = Vector(fixed) - d * Vector(fixed).dot(d)
        else:
            side = d.cross(Vector((0, 0, 1)))
            if side.length < 1e-4: side = d.cross(Vector((0, 1, 0)))
        side.normalize(); up = side.cross(d).normalized()
        prof = profile(i / (len(path) - 1)) if callable(profile) else profile
        rings_.append([bm.verts.new(Vector(p) + side * x + up * y) for x, y in prof])
    for r0, r1 in zip(rings_, rings_[1:]):
        n = len(r0)
        for k in range(n): bm.faces.new((r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]))
    if cap:
        bm.faces.new(list(reversed(rings_[0]))); bm.faces.new(rings_[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, mat, smooth)

def raven(at, yaw, scale=1.0, mat=DARK, beak=IRON, head_turn=0.0):
    """A perched raven: body, breast, head, heavy beak, tail, folded wings."""
    q = Matrix.Rotation(yaw, 3, 'Z'); s = scale
    def P(x, y, z): return Vector(at) + q @ Vector((x * s, y * s, z * s))
    ball('raven', P(0, 0, 0.09), 0.075 * s, mat, squash=(0.8, 1.3, 0.9))
    ball('raven', P(0, 0.06, 0.12), 0.06 * s, mat, squash=(0.9, 1.0, 1.0))
    hq = Matrix.Rotation(head_turn, 3, 'Z')
    H = P(0, 0.1, 0.19)
    ball('raven', H, 0.045 * s, mat, squash=(0.85, 1.1, 0.95))
    tip = H + q @ hq @ Vector((0, 0.1 * s, -0.012 * s))
    hull('raven', [H + q @ hq @ Vector((x * s, 0.03 * s, z * s)) for x in (-0.016, 0.016) for z in (-0.016, 0.018)] + [tip], beak, 1)
    hull('raven', [P(x, -0.07, 0.1) for x in (-0.03, 0.03)] + [P(x, -0.08, 0.06) for x in (-0.03, 0.03)] + [P(x, -0.2, 0.03) for x in (-0.045, 0.045)] + [P(0, -0.21, 0.045)], mat, 1)
    for sx in (-1, 1):
        hull('raven', [P(sx * 0.055, 0.06, 0.15), P(sx * 0.07, 0.0, 0.14), P(sx * 0.06, -0.13, 0.08), P(sx * 0.04, -0.12, 0.05), P(sx * 0.065, 0.02, 0.06), P(sx * 0.045, 0.05, 0.08)], mat, 1)
        for k in (-1, 1):
            cyl('raven', P(sx * 0.025, 0.0, 0.03), P(sx * 0.028 + k * 0.01, 0.04, -0.005), 0.006 * s, beak, n=5)

def sheet(name, f, nu, nv, mat, thick=0.012, smooth=True):
    """A cloth or hide: f(u, v) -> point for u, v in 0..1, given a thickness (solidified)."""
    bm = bmesh.new()
    vs = [[bm.verts.new(f(i / nu, j / nv)) for j in range(nv + 1)] for i in range(nu + 1)]
    for i in range(nu):
        for j in range(nv): bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
    ob = link(name, bm, mat, smooth)
    so = ob.modifiers.new('solid', 'SOLIDIFY'); so.thickness = thick; so.offset = 0
    return ob

def chain(name, pts, link_len=0.05, r=0.012, wire=0.004, mat=IRON):
    """Links of chain along a polyline, alternate links turned a quarter."""
    path = [Vector(p) for p in pts]
    total = sum((b - a).length for a, b in zip(path, path[1:]))
    n = max(1, int(total / (link_len * 0.75)))
    def at(s):
        for a, b in zip(path, path[1:]):
            l = (b - a).length
            if s <= l: return a.lerp(b, s / max(l, 1e-6)), (b - a).normalized()
            s -= l
        return path[-1], (path[-1] - path[-2]).normalized()
    for k in range(n):
        c, d = at((k + 0.5) * total / n)
        side = d.cross(Vector((0, 0, 1))) if abs(d.z) < 0.9 else d.cross(Vector((1, 0, 0)))
        side.normalize()
        if k % 2: side = d.cross(side).normalized()
        tube(name, [c + d * (link_len / 2) * math.cos(a) + side * r * math.sin(a) for a in [i * 2 * math.pi / 10 for i in range(10)]], wire, mat, cyclic=True, res=1)

def export(name):
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    verts, norms, uvs, tris, index = [], [], [], [[] for _ in mats], {}
    for ob in list(bpy.context.scene.collection.objects):
        if ob.type != 'MESH': continue
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
        me.transform(ob.matrix_world)
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        me.update()
        cn = me.corner_normals
        smooth = ob.name in SMOOTH
        for p in me.polygons:
            slot = mats.index(me.materials[p.material_index]) if me.materials else STONE
            corner = []
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                n = cn[li].vector if smooth else p.normal
                fn = p.normal
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + name + '.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    xs, ys, zs = verts[0::3], verts[1::3], verts[2::3]
    print(name, 'verts', len(verts) // 3, 'tris', sum(len(t) for t in tris) // 3, 'size x', round(max(xs) - min(xs), 2), 'z', round(max(zs) - min(zs), 2), 'height', round(max(ys), 2), 'foot', round(min(ys), 2))

COLS = {'stone': (0.42, 0.41, 0.39), 'cut': (0.05, 0.05, 0.05), 'wood': (0.42, 0.29, 0.17), 'dark': (0.07, 0.07, 0.08), 'iron': (0.15, 0.15, 0.16),
        'gold': (0.85, 0.62, 0.22), 'bone': (0.8, 0.76, 0.64), 'cloth': (0.25, 0.3, 0.55), 'rope': (0.66, 0.55, 0.33), 'glow': (1.0, 0.7, 0.3),
        'leaf': (0.2, 0.38, 0.12), 'bloom': (0.85, 0.35, 0.45), 'paint': (0.6, 0.12, 0.08), 'hide': (0.45, 0.32, 0.2)}

def preview(name, cloth=None, bloom=None, paint=None, views=None, height=1.0):
    over = {'cloth': cloth, 'bloom': bloom, 'paint': paint}
    for m in mats:
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
        c = over.get(m.name) or COLS[m.name]
        b.inputs['Base Color'].default_value = (*c, 1)
        if m.name in ('iron', 'gold'): b.inputs['Metallic'].default_value = 0.7; b.inputs['Roughness'].default_value = 0.4
        if m.name == 'glow': b.inputs['Emission Color'].default_value = (*c, 1); b.inputs['Emission Strength'].default_value = 4
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH' and ob.name in SMOOTH:
            for p in ob.data.polygons: p.use_smooth = True
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 20; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 600, 700
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(160)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 45
    for yaw, d, z, look in (views or ((20, 3.6, 1.2, height * 0.5), (110, 3.6, 1.2, height * 0.5), (200, 3.6, 1.6, height * 0.5))):
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, math.cos(a) * d, z)   # (yaw 0: in front, on +y)
        cam.rotation_euler = (Vector((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'{name}_{yaw}.png'; bpy.ops.render.render(write_still=True)
