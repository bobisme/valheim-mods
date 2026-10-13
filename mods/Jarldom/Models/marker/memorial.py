# The memorial stone (Jarldom 0.1.65+; 0.1.74 smaller, rougher, carved only): a raised runestone for the colony's dead, about a metre
# high. A rough, lopsided slab with an uneven rounded head set in a few footing stones; cut into its face, in black, a serpent band with
# runes along it, its head and tail meeting at the foot, and Odin's valknut in the field it encloses. The carving follows the stone's
# uneven face (each corner found by a ray onto the slab). Modelled at twice the size and halved on export (SCALE).
# Run: blender -b --python ~/.cache/jarldom-stake/memorial.py -- [render]   (writes ~/.cache/jarldom-stake/memorial.json.gz)
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material: 0 stone, 1 paint (no longer used: kept so the game's material slots line up), 2 rune (dark, carved).
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

OUT = '/home/bob/.cache/jarldom-stake/'
STONE, PAINT, RUNE = range(3)
SMOOTH = ()
SCALE = 0.5
rng = random.Random(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in ('stone', 'paint', 'rune')]

def link(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[mat])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob

def hull(name, pts, mat, limit=3):
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(limit), verts=bm.verts, edges=bm.edges)
    return link(name, bm, mat)

def tube(name, pts, radius, mat, cyclic=False):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 2
    cu.use_fill_caps = not cyclic
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1); sp.use_cyclic_u = cyclic
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

def lump(name, at, size, squash=0.6):
    pts = []
    for _ in range(16):
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        pts.append(at + Vector((d.x * size * rng.uniform(0.8, 1.1), d.y * size * 0.8 * rng.uniform(0.8, 1.1), d.z * size * squash * rng.uniform(0.8, 1.1))))
    return hull(name, pts, STONE, 8)

# ---- the slab: a rounded head, a little thinner as it rises; rough and lopsided, its faces lumpy ----
FOOT, SHOULDER, W = 0.18, 1.62, 0.56     # (its foot sits in the footing stones; the head's round starts at the shoulder)
def thick(z): return 0.17 - 0.035 * (z - FOOT) / 2.0
LEAN = rng.uniform(0.12, 0.16)  # (the head sits a little to one side, one shoulder lower)
def outline():
    pts = [(-W * 0.95, FOOT), (W * 0.97, FOOT)]
    for k in range(1, 9):
        z = FOOT + (SHOULDER - FOOT) * k / 8
        pts += [(-W * (0.95 + 0.04 * k / 8) + rng.uniform(-0.045, 0.03), z + rng.uniform(-0.04, 0.04)), (W * (0.97 + 0.02 * k / 8) + rng.uniform(-0.03, 0.045), z + rng.uniform(-0.04, 0.04))]
    for k in range(1, 14):
        a = math.pi * k / 14
        r = 1 + rng.uniform(-0.1, 0.05)
        pts.append((math.cos(a) * W * r + LEAN * math.sin(a), SHOULDER - 0.06 * math.cos(a) + math.sin(a) * 0.48 * r))
    return pts
slab = []
for x, z in outline():
    t = thick(z) * rng.uniform(0.88, 1.1)
    slab += [Vector((x, -t, z)), Vector((x, t * rng.uniform(0.9, 1.05), z))]
    slab += [Vector((x * 0.96, -t * rng.uniform(1.04, 1.14), z)), Vector((x * 0.96, t * rng.uniform(1.0, 1.1), z))]  # (knocked-off edges)
for _ in range(40):  # (the faces: lumps and hollows, as split from the rock)
    x, z = rng.uniform(-W * 0.8, W * 0.8), rng.uniform(FOOT + 0.05, SHOULDER + 0.3)
    if z > SHOULDER and abs(x) > W * 0.8 * math.sqrt(max(0, 1 - ((z - SHOULDER) / 0.45) ** 2)): continue
    t = thick(z)
    slab.append(Vector((x, -t * rng.uniform(1.06, 1.2), z))); slab.append(Vector((x, t * rng.uniform(1.04, 1.16), z)))
SLAB = hull('slab', slab, STONE, 1)
# (then weathered: remeshed fine, pushed in and out by noise, coarse and fine, and thinned back to a light mesh)
def weather(ob, voxel, big, small, ratio, seed):
    rm = ob.modifiers.new('remesh', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = voxel
    for size, strength, name in ((0.5, big, 'big'), (0.08, small, 'small')):
        tex = bpy.data.textures.new(name + str(seed), 'CLOUDS'); tex.noise_scale = size; tex.noise_depth = 2
        d = ob.modifiers.new(name, 'DISPLACE'); d.texture = tex; d.strength = strength; d.mid_level = 0.5; d.texture_coords = 'GLOBAL'
    dec = ob.modifiers.new('decimate', 'DECIMATE'); dec.ratio = ratio
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    ob.modifiers.clear(); ob.data = me; me.materials.clear(); me.materials.append(mats[STONE])
weather(SLAB, 0.025, 0.12, 0.03, 0.12, 1)
# (the faces dressed back most of the way to flat, as a carver would choose: the edges and sides stay rough, and the carving is seen
# from the side too, not hidden behind a lump)
_bm = bmesh.new(); _bm.from_mesh(SLAB.data); _bm.normal_update()
for v in _bm.verts:
    w = max(0.0, abs(v.normal.y)) ** 2
    face = (-1 if v.co.y < 0 else 1) * thick(v.co.z) * 1.12
    v.co.y += (face - v.co.y) * 0.7 * w
_bm.to_mesh(SLAB.data); _bm.free()
_bm = bmesh.new(); _bm.from_mesh(SLAB.data); FACE = BVHTree.FromBMesh(_bm); _bm.free()

# ---- the footing: rough stones round its foot, inside a tight footprint ----
for k, (x, y, s) in enumerate(((-0.52, -0.16, 0.2), (0.5, -0.18, 0.21), (-0.18, -0.27, 0.16), (0.22, -0.26, 0.17), (-0.48, 0.17, 0.19), (0.46, 0.18, 0.18), (0.0, 0.25, 0.17))):
    lump('foot', Vector((x, y, s * 0.42)), s)

# ---- the serpent band: two carved lines round the face, runes cut between them ----
IN = 0.13        # (its middle, in from the stone's edge)
HALF = 0.055     # (half its width)
def path():
    pts = []
    for k in range(9):  # up the left side
        z = FOOT + 0.2 + (SHOULDER - FOOT - 0.2) * k / 8
        pts.append(Vector((-(W - IN), 0, z)))
    for k in range(1, 16):  # over the head
        a = math.pi - math.pi * k / 16
        pts.append(Vector((math.cos(a) * (W - IN) + LEAN * math.sin(a), 0, SHOULDER - 0.06 * math.cos(a) + math.sin(a) * (0.48 - IN))))
    for k in range(9):  # down the right side
        z = SHOULDER - (SHOULDER - FOOT - 0.2) * k / 8
        pts.append(Vector((W - IN, 0, z)))
    for k in range(1, 6):  # the tail curls in along the foot
        a = -math.pi * 0.5 * k / 5
        pts.append(Vector(((W - IN) - 0.18 + math.cos(a) * 0.18, 0, FOOT + 0.2 + math.sin(a) * 0.12)))
    return pts
# A point on the stone's face (found by a ray from the front), lifted by proud (in front of it: + out of the stone).
def onface(p, proud):
    hit = FACE.ray_cast(Vector((p.x, -2.0, p.z)), Vector((0, 1, 0)))
    y = hit[0].y if hit[0] is not None else -thick(p.z) * 1.1
    return Vector((p.x, y - proud, p.z))
P = path()
def side(i):  # the band's sideways, in the face's plane
    a, b = P[max(0, i - 1)], P[min(len(P) - 1, i + 1)]
    d = (b - a).normalized(); return Vector((-d.z, 0, d.x)), d
# A cut line: dark, a thin sliver on the face (a hair proud, so it's never lost in the stone).
CUT = 0.009
def stroke(a, b, w=CUT):
    d = (b - a); n = Vector((-d.z, 0, d.x)).normalized() * w
    pts = []
    for p in (a, b):
        for o in (n, -n):
            q = p + o; pts += [onface(q, -0.01), onface(q, 0.007)]
    hull('rune', pts, RUNE, 1)
def line(pts, w=CUT, cyclic=False):  # (in short pieces, each following the rough face)
    for i in range(len(pts) - (0 if cyclic else 1)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n = max(1, math.ceil((b - a).length / 0.035))
        for j in range(n): stroke(a.lerp(b, j / n), a.lerp(b, (j + 1) / n), w)
for s_ in (-1, 1):
    line([P[i] + side(i)[0] * HALF * s_ for i in range(len(P))])
# the serpent's head at the left foot, looking in: a blunt snout drawn round, an eye
H = P[0] + Vector((0.02, 0, -0.1))
line([H + Vector(o) for o in ((-0.07, 0, 0.07), (0.07, 0, 0.07), (0.13, 0, 0.0), (0.08, 0, -0.05), (-0.06, 0, -0.03))], cyclic=True)
stroke(H + Vector((0.02, 0, 0.025)), H + Vector((0.045, 0, 0.025)), 0.014)
# runes along the ribbon: a stave across it, a twig or two off it
length = 0.0; nxt = 0.06
for i in range(1, len(P) - 4):
    length += (P[i] - P[i - 1]).length
    if length < nxt: continue
    nxt = length + rng.uniform(0.06, 0.08)
    sd, along = side(i)
    a, b = P[i] - sd * HALF * 0.62, P[i] + sd * HALF * 0.62
    stroke(a, b)
    m = rng.random()
    if m < 0.35: stroke(a.lerp(b, 0.5), a.lerp(b, 0.5) + along * 0.03 + sd * HALF * 0.5)
    elif m < 0.7: stroke(a.lerp(b, 0.75), a.lerp(b, 0.35) + along * 0.032)
    elif m < 0.85: stroke(a.lerp(b, 0.3), a.lerp(b, 0.3) + along * 0.028); stroke(a.lerp(b, 0.7), a.lerp(b, 0.7) + along * 0.028)
    if rng.random() < 0.12: nxt += 0.05  # (a word's end)

# ---- the valknut in the field: three triangles, cut in ----
C = Vector((LEAN * 0.5, 0, 1.1)); R = 0.19
for k in range(3):
    rot = 2 * math.pi * k / 3
    c = C + Vector((math.cos(rot - math.pi / 2) * R * 0.42, 0, math.sin(rot - math.pi / 2) * R * 0.42))  # (each shifted toward its own base: they cross)
    line([c + Vector((math.cos(rot + math.pi / 2 + 2 * math.pi * j / 3) * R, 0, math.sin(rot + math.pi / 2 + 2 * math.pi * j / 3) * R)) for j in range(3)], 0.011, cyclic=True)

# ---- write it out ----
def export():
    deps = bpy.context.evaluated_depsgraph_get()
    verts, norms, uvs, tris, index = [], [], [], [[] for _ in mats], {}
    for ob in list(bpy.context.scene.collection.objects):
        if ob.type != 'MESH': continue
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        me.update()
        cn = me.corner_normals
        for p in me.polygons:
            slot = mats.index(me.materials[p.material_index]) if me.materials else STONE
            corner = []
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co * SCALE
                n = cn[li].vector if ob.name.startswith(SMOOTH) else p.normal
                fn = p.normal
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + 'memorial.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    xs, ys, zs = verts[0::3], verts[2::3], verts[1::3]
    print('memorial verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'size x', round(max(xs) - min(xs), 2), 'depth', round(max(ys) - min(ys), 2), 'height', round(max(zs), 2))

def preview():
    cols = [(0.42, 0.41, 0.39, 1), (0.55, 0.12, 0.07, 1), (0.08, 0.07, 0.07, 1)]
    for m, c in zip(mats, cols):
        m.use_nodes = True; m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = c
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH' and ob.name.startswith(SMOOTH):
            for p in ob.data.polygons: p.use_smooth = True
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 600, 800
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
    for yaw, d, z, look in ((0, 4.2, 1.2, 1.05), (35, 4.2, 1.2, 1.05), (-40, 4.2, 1.2, 1.05), (90, 4.2, 1.2, 1.05)):
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, -math.cos(a) * d, z)
        cam.rotation_euler = (Vector((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'memorial_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
