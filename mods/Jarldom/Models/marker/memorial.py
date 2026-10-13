# The memorial stone (Jarldom 0.1.65+): a raised runestone for the colony's dead. A tall slab with a rounded head set in a few rough
# footing stones; on its face a serpent band, a ribbon edged in red with runes carved along it, its head and tail meeting at the foot,
# and Odin's valknut in the field it encloses.
# Run: blender -b --python ~/.cache/jarldom-stake/memorial.py -- [render]   (writes ~/.cache/jarldom-stake/memorial.json.gz)
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material: 0 stone, 1 paint (red ochre), 2 rune (dark, carved).
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/jarldom-stake/'
STONE, PAINT, RUNE = range(3)
SMOOTH = ('band', 'knot', 'head')
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

# ---- the slab: tall, a rounded head, a little thinner as it rises; rough-hewn edges ----
FOOT, SHOULDER, W = 0.18, 1.62, 0.56     # (its foot sits in the footing stones; the head's round starts at the shoulder)
def thick(z): return 0.17 - 0.035 * (z - FOOT) / 2.0
def front(z): return -thick(z) * 1.08  # (the face's bulge, where the carving sits)
def outline():
    pts = [(-W * 0.97, FOOT), (W * 0.97, FOOT)]
    for k in range(1, 7):
        z = FOOT + (SHOULDER - FOOT) * k / 6
        pts += [(-W * (0.97 + 0.03 * k / 6) + rng.uniform(-0.012, 0.012), z), (W * (0.97 + 0.03 * k / 6) + rng.uniform(-0.012, 0.012), z)]
    for k in range(1, 12):
        a = math.pi * k / 12
        pts.append((math.cos(a) * W * (1 + rng.uniform(-0.015, 0.015)), SHOULDER + math.sin(a) * 0.52))
    return pts
slab = []
for x, z in outline():
    t = thick(z)
    slab += [Vector((x, -t, z)), Vector((x, t, z))]
    slab += [Vector((x * 0.97, -t * 1.08, z)), Vector((x * 0.97, t * 1.08, z))]  # (faces bulge a little: knocked-off edges)
hull('slab', slab, STONE, 2)

# ---- the footing: rough stones round its foot, inside a tight footprint ----
for k, (x, y, s) in enumerate(((-0.52, -0.16, 0.2), (0.5, -0.18, 0.21), (-0.18, -0.27, 0.16), (0.22, -0.26, 0.17), (-0.48, 0.17, 0.19), (0.46, 0.18, 0.18), (0.0, 0.25, 0.17))):
    lump('foot', Vector((x, y, s * 0.42)), s)

# ---- the serpent band: a ribbon round the face, edged in red, runes carved along it ----
IN = 0.12        # (its middle, in from the stone's edge)
HALF = 0.055     # (half its width)
def path():
    pts = []
    for k in range(9):  # up the left side
        z = FOOT + 0.2 + (SHOULDER - FOOT - 0.2) * k / 8
        pts.append(Vector((-(W - IN), 0, z)))
    for k in range(1, 16):  # over the head
        a = math.pi - math.pi * k / 16
        pts.append(Vector((math.cos(a) * (W - IN), 0, SHOULDER + math.sin(a) * (0.52 - IN))))
    for k in range(9):  # down the right side
        z = SHOULDER - (SHOULDER - FOOT - 0.2) * k / 8
        pts.append(Vector((W - IN, 0, z)))
    for k in range(1, 6):  # the tail curls in along the foot
        a = -math.pi * 0.5 * k / 5
        pts.append(Vector(((W - IN) - 0.18 + math.cos(a) * 0.18, 0, FOOT + 0.2 + math.sin(a) * 0.12)))
    return pts
def onface(p, proud): return Vector((p.x, front(p.z) - proud, p.z))
P = path()
def side(i):  # the band's sideways, in the face's plane
    a, b = P[max(0, i - 1)], P[min(len(P) - 1, i + 1)]
    d = (b - a).normalized(); return Vector((-d.z, 0, d.x)), d
for s in (-1, 1):
    tube('band', [onface(P[i] + side(i)[0] * HALF * s, 0.006) for i in range(len(P))], 0.016, PAINT)
# the serpent's head at the left foot, looking in: a blunt snout, an eye
H = P[0] + Vector((0.02, 0, -0.1))
hull('head', [onface(H + Vector((dx, 0, dz)), pr) for dx, dz in ((-0.07, 0.07), (0.07, 0.07), (-0.06, -0.03), (0.08, -0.05), (0.13, 0.0)) for pr in (-0.01, 0.02)], PAINT, 10)
hull('eye', [onface(H + Vector((0.03 + dx, 0, 0.025 + dz)), 0.024 + pr) for dx in (-0.012, 0.012) for dz in (-0.012, 0.012) for pr in (0, 0.006)], RUNE)
# runes along the ribbon: a stave across it, a twig or two off it (carved: dark, standing a hair proud)
def stroke(a, b):
    d = (b - a); n = Vector((-d.z, 0, d.x)).normalized() * 0.006
    pts = []
    for p in (a, b):
        for o in (n, -n):
            q = p + o; pts += [onface(q, -0.002), onface(q, 0.009)]
    hull('rune', pts, RUNE, 1)
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

# ---- the valknut in the field: three triangles, interlaced ----
C = Vector((0, 0, 1.1)); R = 0.19
for k in range(3):
    rot = 2 * math.pi * k / 3
    c = C + Vector((math.cos(rot - math.pi / 2) * R * 0.42, 0, math.sin(rot - math.pi / 2) * R * 0.42))  # (each shifted toward its own base: they cross)
    tri = [c + Vector((math.cos(rot + math.pi / 2 + 2 * math.pi * j / 3) * R, 0, math.sin(rot + math.pi / 2 + 2 * math.pi * j / 3) * R)) for j in range(3)]
    tube('knot', [onface(p, 0.006 + 0.004 * k) for p in tri], 0.014, PAINT, cyclic=True)

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
                co = me.vertices[me.loops[li].vertex_index].co
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
    for yaw, d, z, look in ((0, 4.2, 1.2, 1.05), (35, 4.2, 1.2, 1.05), (90, 4.2, 1.2, 1.05), (15, 1.8, 1.3, 1.2)):
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, -math.cos(a) * d, z)
        cam.rotation_euler = (Vector((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'memorial_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
