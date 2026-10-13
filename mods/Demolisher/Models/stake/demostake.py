# The demolition stake (Demolisher 0.1.1+): a rough log, pointed and driven in, its head split by an iron wedge hammered into it. Banded
# red and black under the split, two broken boards nailed across its front in an X, and a hauling chain hung from a staple with a hook
# on its end.
# Run: blender -b --python ~/.cache/stakes/demostake.py -- [render]   (writes ~/.cache/stakes/demostake.json.gz; previews with "render")
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material: 0 wood, 1 weathered boards, 2 iron, 3 red paint, 4 black paint.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/stakes/'
WOOD, BOARD, IRON, RED, BLACK = range(5)
SMOOTH = ('link', 'hook')  # (objects drawn smooth; the rest keep hard facets)
rng = random.Random(23)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in ('wood', 'board', 'iron', 'red', 'black')]

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

def tube(name, pts, radius, mat, cyclic=False):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 1
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1); sp.use_cyclic_u = cyclic
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

R = 0.05  # the log's radius
SIDES = 10
lumps = [rng.uniform(-0.07, 0.07) for _ in range(SIDES)]  # the log's own unevenness, the same all the way up (so paint can follow it)
def ring(z, r, keep=lambda a: True, shift=Vector()):
    return [Vector((math.cos(a) * r * (1 + lumps[i]), math.sin(a) * r * (1 + lumps[i]), z)) + shift
            for i, a in ((i, 2 * math.pi * i / SIDES + 0.2) for i in range(SIDES)) if keep(a)]

# ---- the log: pointed at its foot, in lengths so its bands of paint take the place of the wood (no shell over it to fight with) ----
SPLIT = 1.16  # where the split begins
def radius(z):  # the log's girth up its length: a little fuller at the foot
    return R * (1.04 - 0.08 * (z - 0.2) / (SPLIT - 0.2))
BANDS = ((0.98, 1.03, RED), (1.03, 1.06, BLACK), (1.06, 1.11, RED), (1.11, 1.14, BLACK))  # red and black under the split, as warning
cuts = [(0.2, BANDS[0][0], WOOD)] + list(BANDS) + [(BANDS[-1][1], SPLIT, WOOD)]
for z0, z1, mat in cuts:
    proud = 1.0 if mat == WOOD else 1.012
    hull('band' if mat != WOOD else 'log', ring(z0, radius(z0) * proud) + ring(z1, radius(z1) * proud), mat)
hull('point', ring(0.2, radius(0.2)) + ring(0.1, R * 0.62) + [Vector((-0.006, 0.004, 0.0))], WOOD)
# the split head: two halves leaning apart, an iron wedge driven down between them, its top burred by the sledge
TOP = 1.36
for s in (-1, 1):
    side = lambda a, s=s: math.cos(a) * s > 0.05
    lean = Vector((s * 0.022, 0, 0))
    flat = [Vector((s * 0.004, y, z)) for y in (-R * 0.93, R * 0.93) for z in (SPLIT + 0.02,)] + [Vector((s * 0.016, y, TOP - 0.01)) for y in (-R * 0.88, R * 0.88)]
    hull('half', ring(SPLIT, R * 0.96, side) + ring(TOP - 0.03, R * 0.94, side, lean) + ring(TOP, R * 0.82, side, lean) + flat, WOOD)
hull('wedge', [Vector((x, y, z)) for x, y, z in ((-0.002, -0.034, SPLIT + 0.04), (0.002, -0.034, SPLIT + 0.04), (-0.002, 0.034, SPLIT + 0.04), (0.002, 0.034, SPLIT + 0.04))]
     + [Vector((x, y, TOP + 0.03)) for x in (-0.019, 0.019) for y in (-0.038, 0.038)], IRON)
hull('burr', [Vector((x * 1.1, y * 1.1, TOP + 0.03)) for x in (-0.024, 0.024) for y in (-0.042, 0.042)] + [Vector((x * 0.8, y * 0.8, TOP + 0.048)) for x in (-0.024, 0.024) for y in (-0.042, 0.042)], IRON)

# ---- two broken boards nailed across its front in an X ----
CROSS = 0.45
def board(name, angle, depth, length, width, seed):
    r = random.Random(seed)
    d = Vector((math.sin(angle), 0, math.cos(angle))); n = Vector((d.z, 0, -d.x))
    # its outline: straight sides, splintered ends (a jagged row of teeth across each end)
    outline = []
    for end in (1, -1):
        tip = d * (end * length / 2)
        teeth = [tip + n * (w * end) + d * (end * r.uniform(-0.035, 0.0) if 0 < k < 6 else 0)
                 for k, w in enumerate([width / 2 - width * j / 6 for j in range(7)])]
        outline += teeth
    bm = bmesh.new()
    vs = [bm.verts.new(p + Vector((0, -depth, CROSS))) for p in outline]
    f = bm.faces.new(vs)
    ret = bmesh.ops.extrude_face_region(bm, geom=[f])
    bmesh.ops.translate(bm, vec=Vector((0, -0.018, 0)), verts=[e for e in ret['geom'] if isinstance(e, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    link(name, bm, BOARD)
    # nails near each end, bent over on one
    for end in (1, -1):
        at = d * (end * (length / 2 - 0.05)) + Vector((0, -depth - 0.018, CROSS))
        nb = bmesh.new(); bmesh.ops.create_cone(nb, cap_ends=True, segments=6, radius1=0.007, radius2=0.006, depth=0.004)
        bmesh.ops.rotate(nb, verts=nb.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'X'))
        bmesh.ops.translate(nb, vec=at, verts=nb.verts); link('nail', nb, IRON)
board('board', math.radians(38), R * 0.86, 0.46, 0.075, 3)
board('board', math.radians(-42), R * 0.86 + 0.018, 0.42, 0.07, 5)
nb = bmesh.new(); bmesh.ops.create_cone(nb, cap_ends=True, segments=6, radius1=0.008, radius2=0.007, depth=0.005)
bmesh.ops.rotate(nb, verts=nb.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'X'))
bmesh.ops.translate(nb, vec=Vector((0, -R * 0.86 - 0.038, CROSS)), verts=nb.verts); link('nail', nb, IRON)
# a slash of red daubed across the X
hull('daub', [Vector((x, -R * 0.86 - 0.0375, CROSS + z)) + Vector((0, y, 0)) for x, z in ((-0.1, 0.07), (-0.08, 0.095), (0.1, -0.06), (0.085, -0.085)) for y in (0, -0.002)], RED)

# ---- a hauling chain hung from a staple on its right, a hook on its end ----
STAPLE = Vector((R * 0.98, -0.006, 1.0))
tube('staple', [STAPLE + Vector((-0.012, 0, 0.012 * math.cos(t))) + Vector((0.012 + 0.012 * math.sin(t), 0, 0)) * 1 for t in [k * math.pi / 8 for k in range(9)]], 0.0035, IRON)
pts = []
for k in range(7):  # where each link hangs: falling from the staple, swinging a little out and forward
    t = k / 6
    pts.append(STAPLE + Vector((0.03 + 0.04 * math.sin(t * 2.4), -0.02 * t, -0.012 - 0.042 * k)))
for k, p in enumerate(pts):
    down = ((pts[k + 1] if k + 1 < len(pts) else p + (p - pts[k - 1])) - (pts[k - 1] if k else STAPLE)).normalized()
    side = down.cross(Vector((0, 1, 0))).normalized() if k % 2 == 0 else Vector((0, 1, 0))
    loop = [p + down * (0.021 * math.cos(a)) + side * (0.011 * math.sin(a)) for a in [i * 2 * math.pi / 14 for i in range(14)]]
    tube('link', loop, 0.0042, IRON, cyclic=True)
end = pts[-1] + Vector((0, 0, -0.03))
tube('hook', [end + Vector((0.026 * (1 - math.cos(a)) * (0.5 if a > math.pi else 1), 0, -0.05 * min(a, math.pi) / math.pi + (0.02 * (a - math.pi) if a > math.pi else 0) - 0.004 * math.sin(a)))
              for a in [i * 4.0 / 16 for i in range(17)]], 0.0065, IRON)

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
                # box-mapped in metres; the wood keeps its grain along the log (v along z on the sides)
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + 'demostake.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    print('demostake verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'height', round(max(verts[1::3]), 2))

def preview():
    cols = [(0.36, 0.25, 0.15, 1), (0.42, 0.4, 0.36, 1), (0.13, 0.13, 0.14, 1), (0.6, 0.06, 0.04, 1), (0.04, 0.04, 0.04, 1)]
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
    for yaw, d, z, look in ((0, 3.0, 0.8, 0.7), (35, 3.0, 0.8, 0.7), (90, 3.0, 0.8, 0.7), (25, 1.3, 1.1, 0.95)):
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, -math.cos(a) * d, z)
        cam.rotation_euler = (Vector((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'demo_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
