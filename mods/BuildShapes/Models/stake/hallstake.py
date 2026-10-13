# The Hallwright stake (BuildShapes 0.8.10+): a builder's setting-out peg. A square hewn post pointed at its foot (the point is the
# corner), two bands of ochre survey paint, and on its head a little roof truss: rafters crossing above the ridge into carved curled
# horns, a tie beam and a king post, pinned with treenails. A carpenter's iron square hangs from a peg on its front.
# Run: blender -b --python ~/.cache/stakes/hallstake.py -- [render]   (writes ~/.cache/stakes/hallstake.json.gz; previews with "render")
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material: 0 wood, 1 dark wood (the truss), 2 iron, 3 ochre paint.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/stakes/'
WOOD, DARK, IRON, OCHRE = range(4)
SMOOTH = ('horn',)  # (objects drawn smooth; the timbers, paint and iron keep hard facets)
rng = random.Random(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in ('wood', 'dark', 'iron', 'ochre')]

def link(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[mat])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob

def hull(name, pts, mat):
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(3), verts=bm.verts, edges=bm.edges)
    return link(name, bm, mat)

def tube(name, pts, radius, mat, taper=None):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 2
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for k, p in enumerate(pts):
        sp.points[k].co = (p.x, p.y, p.z, 1)
        if taper: sp.points[k].radius = taper(k / (len(pts) - 1))
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

SWELL = [1 + rng.uniform(-0.04, 0.04) for _ in range(4)]  # the adze leaving it a little uneven, the same all the way up
def square(z, half, chamfer, jit=False):
    """A hewn square section with chamfered corners (eight points)."""
    pts = []
    for k, (sx, sy) in enumerate(((1, 1), (-1, 1), (-1, -1), (1, -1))):
        h = half * (SWELL[k] if jit else 1)
        pts += [Vector((sx * h, sy * (h - chamfer), z)), Vector((sx * (h - chamfer), sy * h, z))]
    return pts

def beam(name, a, b, w, d, mat):
    """A square timber from a to b in the front plane: w across in that plane, d deep (along y)."""
    along = (b - a).normalized(); across = along.cross(Vector((0, 1, 0))).normalized()
    pts = [p + across * sx * w / 2 + Vector((0, sy * d / 2, 0)) for p in (a, b) for sx in (-1, 1) for sy in (-1, 1)]
    return hull(name, pts, mat)

def peg(name, at, r, length, mat, axis=Vector((0, -1, 0))):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=r, radius2=r * 0.9, depth=length)
    q = Vector((0, 0, 1)).rotation_difference(axis)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=q.to_matrix())
    bmesh.ops.translate(bm, vec=at + axis * length / 2, verts=bm.verts)
    return link(name, bm, mat)

# ---- the post: square hewn, pointed at its foot ----
HALF, HEAD = 0.036, 1.42  # half its width; where it meets the tie beam
def half(z): return HALF * (1.04 - 0.04 * min(1, (z - 0.2) / 0.4))  # a little fuller at the foot
# in lengths, so its two bands of ochre survey paint take the place of the wood (no shell over it to fight with)
BANDS = ((1.2, 1.26), (1.31, 1.34))
cuts = [0.2, 0.6] + [z for b in BANDS for z in b] + [HEAD]
for z0, z1 in zip(cuts, cuts[1:]):
    paint = (z0, z1) in BANDS
    hull('band' if paint else 'post', square(z0, half(z0) * (1.015 if paint else 1), 0.009, jit=True) + square(z1, half(z1) * (1.015 if paint else 1), 0.009, jit=True), OCHRE if paint else WOOD)
hull('point', square(0.2, half(0.2), 0.009, jit=True) + square(0.1, HALF * 0.6, 0.005) + [Vector((0.004, -0.003, 0.0))], WOOD)

# ---- the roof truss on its head ----
APEX = 1.63
SPAN = 0.2                    # half the span, eave to eave
EAVE = HEAD + 0.013
beam('tie', Vector((-SPAN - 0.02, 0, EAVE)), Vector((SPAN + 0.02, 0, EAVE)), 0.03, 0.034, DARK)
beam('king', Vector((0, 0, EAVE)), Vector((0, 0, APEX)), 0.032, 0.03, DARK)
for s in (-1, 1):
    foot = Vector((s * SPAN, 0, EAVE + 0.01))
    top = Vector((0, 0, APEX))
    up = (top - foot).normalized()
    cross = top + up * 0.06  # past the apex, crossing its fellow
    beam('rafter', foot - up * 0.03, cross, 0.026, 0.026 if s < 0 else 0.024, DARK)
    # its horn: curling out and down from the crossing, like the gable ends of a hall, its tip in ochre
    side = Vector((-s, 0, 0))
    pts = []
    for k in range(19):
        t = k / 18; a = t * 3.6
        r = 0.05 * (1 - 0.55 * t)
        pts.append(cross + up * (r * math.sin(a)) + side * (0.05 - r * math.cos(a)) + Vector((0, 0.0 if s < 0 else 0.001, 0)))
    tube('horn', pts[:14], 0.012, DARK, taper=lambda t: 1 - 0.45 * t)
    tube('horntip', pts[13:], 0.0075, OCHRE, taper=lambda t: 1 - 0.6 * t)
    # a brace from the king post to the rafter's middle
    mid = foot.lerp(top, 0.5)
    beam('brace', Vector((0, 0, EAVE + 0.04)), mid, 0.018, 0.02, DARK)
# treenails through the joints
for at in (Vector((0, 0, APEX - 0.02)), Vector((-SPAN + 0.02, 0, EAVE + 0.012)), Vector((SPAN - 0.02, 0, EAVE + 0.012)), Vector((0, 0, EAVE))):
    peg('nail', at + Vector((0, -0.016, 0)), 0.006, 0.012, WOOD)

# ---- a carpenter's square hung on a peg on the front ----
PEG = Vector((0.006, -HALF, 1.07))
peg('peg', PEG, 0.008, 0.06, WOOD, axis=Vector((0, -1, 0.25)).normalized())
corner = PEG + Vector((0, -0.04, 0.005))
def leg(angle, length, width):
    d = Vector((math.cos(angle), 0, math.sin(angle))); n = Vector((-d.z, 0, d.x))
    base = corner - n * width / 2 * (1 if angle < -math.pi / 2 else -1)
    pts = [base + d * l + n * w + Vector((0, y, 0)) for l in (0, length) for w in (-width / 2, width / 2) for y in (-0.002, 0.002)]
    hull('square', pts, IRON)
    # its marks, cut every few fingers along the edge
    for k in range(1, int(length / 0.04)):
        m = base + d * (k * 0.04) + n * (width / 2 - 0.004) * (1 if angle < -math.pi / 2 else -1)
        hull('tick', [m + d * u + n * w + Vector((0, y, 0)) for u in (-0.0015, 0.0015) for w in (-0.004, 0.004) for y in (-0.0028, -0.0018)], DARK)
leg(math.radians(-128), 0.3, 0.032)
leg(math.radians(-38), 0.21, 0.026)

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
            slot = mats.index(me.materials[p.material_index]) if me.materials else WOOD
            corner = []
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                n = cn[li].vector if ob.name.startswith(SMOOTH) else p.normal
                fn = p.normal
                # box-mapped in metres; the wood keeps its grain along its length (v along z on the sides)
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + 'hallstake.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    print('hallstake verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'height', round(max(verts[1::3]), 2))

def preview():
    cols = [(0.45, 0.32, 0.19, 1), (0.22, 0.15, 0.1, 1), (0.13, 0.13, 0.14, 1), (0.85, 0.42, 0.1, 1)]
    for m, c in zip(mats, cols):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = c
        if m.name == 'iron': b.inputs['Metallic'].default_value = 0.7
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH' and ob.name.startswith(SMOOTH):
            for p in ob.data.polygons: p.use_smooth = True
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 500, 800
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
    for yaw, d, z, look in ((0, 3.4, 0.9, 0.8), (35, 3.4, 0.9, 0.8), (90, 3.4, 0.9, 0.8), (20, 1.1, 1.45, 1.35)):
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, -math.cos(a) * d, z)
        cam.rotation_euler = (Vector((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'hall_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
