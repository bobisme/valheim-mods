# The stave socket (Shieldwall 0.4.16+): a squat dry-stone cairn of flat slabs in three courses under a broad capstone cut with the war
# rune (Tiwaz) in its front, and on it a collar stone girt with iron, where the stave stands in an iron ring wedged with stone shims.
# Run: blender -b --python ~/.cache/warstone/socket.py -- [render]   (writes ~/.cache/warstone/socket.json.gz; previews with "render")
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material, numbered as the Warstone's: 0 rock, 1 iron, 2 rune.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/warstone/'
ROCK, IRON, RUNE = range(3)
SMOOTH = ('ring',)  # (objects drawn smooth; the stones keep hard facets)
rng = random.Random(5)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in ('rock', 'iron', 'rune')]

def link(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[mat])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob

def hull(name, pts, mat, tidy=True):
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    if tidy: bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(3), verts=bm.verts, edges=bm.edges)
    else: bmesh.ops.triangulate(bm, faces=bm.faces)  # (left in triangles: the boolean cuts these cleanly)
    return link(name, bm, mat)

def tube(name, pts, radius, mat, cyclic=False):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 2
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1); sp.use_cyclic_u = cyclic
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

def slab(name, centre, width, depth, height, yaw, mat=ROCK, rough=0.12):
    """A flat split stone: a box with its corners knocked about and its top a little uneven."""
    rot = Matrix.Rotation(yaw, 3, 'Z'); pts = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (0, 1):
                for k in range(2):  # two points per corner, nudged apart: chipped corners
                    c = Vector((sx * width / 2 * (1 - rng.uniform(0, rough)), sy * depth / 2 * (1 - rng.uniform(0, rough)), sz * height * (1 + rng.uniform(-0.15, 0.1))))
                    c += Vector((-sx * rng.uniform(0, 0.03), -sy * rng.uniform(0, 0.03), 0)) if k else Vector()
                    pts.append(centre + rot @ c)
    return hull(name, pts, mat)

def course(z, height, radius, count, width, depth, phase):
    """A ring of slabs laid flat, long sides round the cairn, leaning a touch inward."""
    for k in range(count):
        a = phase + 2 * math.pi * k / count + rng.uniform(-0.08, 0.08)
        r = radius - depth / 2 + rng.uniform(-0.015, 0.015)
        slab('stone', Vector((math.cos(a) * r, math.sin(a) * r, z + rng.uniform(-0.01, 0.01))), width * rng.uniform(0.9, 1.1), depth, height, a + math.pi / 2)

# ---- the cairn: a hidden core, and three courses of slabs, each narrower ----
hull('core', [Vector((math.cos(a) * r, math.sin(a) * r, z)) for z, r in ((0, 0.34), (0.55, 0.2)) for a in [i * math.pi / 4 for i in range(8)]], ROCK)
course(0.0, 0.2, 0.46, 7, 0.4, 0.2, 0.2)
course(0.19, 0.19, 0.38, 6, 0.38, 0.18, 0.55)
course(0.37, 0.18, 0.3, 5, 0.34, 0.16, 0.1)
# small stones chinked into the gaps of the lowest course, and a couple fallen at its foot
for k in range(7):
    a = 0.2 + 2 * math.pi * (k + 0.5) / 7
    slab('chink', Vector((math.cos(a) * 0.43, math.sin(a) * 0.43, 0.07 + rng.uniform(0, 0.06))), 0.09, 0.07, 0.06, a + math.pi / 2, rough=0.3)
for a in (1.2, 3.9):
    slab('fallen', Vector((math.cos(a) * 0.55, math.sin(a) * 0.55, 0.0)), 0.13, 0.1, 0.07, a, rough=0.3)

# ---- the capstone: one broad slab, an irregular eight-sided stone, its front dressed flat for the rune ----
CAP0, CAP1 = 0.54, 0.7
cap = []
for k in range(8):
    a = -math.pi / 2 + math.pi / 8 + 2 * math.pi * k / 8
    r = 0.32 * (1 + (0 if k in (0, 7) else rng.uniform(-0.06, 0.05)))  # (the two front corners true, for a flat face)
    for z, s in ((CAP0, 1.0), (CAP1, 0.93)):
        cap.append(Vector((math.cos(a) * r * s, math.sin(a) * r * s, z + (rng.uniform(-0.012, 0.012) if z == CAP1 else 0))))
capstone = hull('capstone', cap, ROCK, tidy=False)
# the front face, facing -y, between its two true corners (where the rune is cut, halfway up)
FRONT_Y = -0.32 * math.cos(math.pi / 8) * 0.965

# ---- the rune: Tiwaz (an arrow up), cut into the capstone's front, its grooves painted ----
cutters = []
def stroke(a, b, w=0.016, depth=0.016):
    d = (b - a).normalized(); n = Vector((d.z, 0, -d.x))
    pts = [p + n * s * w / 2 + Vector((0, y, 0)) for p in (a - d * 0.004, b + d * 0.004) for s in (-1, 1) for y in (-0.05, depth)]
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    me = bpy.data.meshes.new('cut'); bm.to_mesh(me); bm.free(); me.materials.append(mats[RUNE])
    ob = bpy.data.objects.new('cut', me); bpy.context.scene.collection.objects.link(ob); ob.hide_render = True
    cutters.append(ob)
RZ = (CAP0 + CAP1) / 2
top = Vector((0, 0, RZ + 0.055)); foot = Vector((0, 0, RZ - 0.06))
stroke(foot, top)
stroke(top, top + Vector((-0.045, 0, -0.045)))
stroke(top, top + Vector((0.045, 0, -0.045)))
for c in cutters:
    for v in c.data.vertices: v.co.y += FRONT_Y
    c.data.update()
    m = capstone.modifiers.new('rune', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.object = c; m.solver = 'EXACT'; m.material_mode = 'TRANSFER'
    bpy.context.view_layer.objects.active = capstone; bpy.ops.object.modifier_apply(modifier=m.name)  # (one stroke at a time)

# ---- the collar stone on top, girt with an iron band; the iron ring the stave stands in, wedged with shims ----
COL0, COL1 = CAP1 - 0.01, CAP1 + 0.13
hull('collar', [Vector((math.cos(a) * r, math.sin(a) * r, z)) for z, r in ((COL0, 0.2), (COL1 - 0.02, 0.185), (COL1, 0.16)) for a in [i * math.pi / 5 + 0.1 for i in range(10)]], ROCK)
hull('band', [Vector((math.cos(a) * 0.208, math.sin(a) * 0.208, z)) for z in (COL0 + 0.035, COL0 + 0.085) for a in [i * math.pi / 8 for i in range(16)]], IRON)
for a in [i * math.pi / 2 + math.pi / 4 for i in range(4)]:  # its rivets
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.011)
    bmesh.ops.translate(bm, vec=Vector((math.cos(a) * 0.21, math.sin(a) * 0.21, COL0 + 0.06)), verts=bm.verts)
    link('rivet', bm, IRON)
RING = 0.1  # (round a stave's foot: they're some 16 cm thick)
tube('ring', [Vector((math.cos(a) * RING, math.sin(a) * RING, COL1 + 0.014)) for a in [i * 2 * math.pi / 24 for i in range(24)]], 0.017, IRON, cyclic=True)
for k in range(4):  # stone shims driven in round the stave
    a = 2 * math.pi * k / 4 + 0.4
    hull('shim', [Vector((math.cos(a) * r, math.sin(a) * r, 0)) + Vector((-math.sin(a) * s, math.cos(a) * s, z)) for r, z in ((0.085, COL1 + 0.045), (0.11, COL1 + 0.045), (0.095, COL1 - 0.01), (0.125, COL1 - 0.01)) for s in (-0.018, 0.018)], ROCK)

# ---- write it out ----
def export():
    deps = bpy.context.evaluated_depsgraph_get()
    verts, norms, uvs, tris, index = [], [], [], [[] for _ in mats], {}
    for ob in list(bpy.context.scene.collection.objects):
        if ob.type != 'MESH' or ob.name.startswith('cut'): continue
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        me.update()
        cn = me.corner_normals
        for p in me.polygons:
            slot = mats.index(me.materials[p.material_index]) if me.materials else ROCK
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
    gzip.open(OUT + 'socket.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    print('socket verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'height', round(max(verts[1::3]), 3))

def preview():
    cols = [(0.3, 0.31, 0.33, 1), (0.13, 0.13, 0.14, 1), (0.6, 0.12, 0.04, 1)]
    for m, c in zip(mats, cols):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = c
        if m.name == 'iron': b.inputs['Metallic'].default_value = 0.7
        if m.name == 'rune': b.inputs['Emission Color'].default_value = (1, 0.3, 0.08, 1); b.inputs['Emission Strength'].default_value = 2
    # a stave stood in it, for scale
    hull('stave', [Vector((math.cos(a) * 0.08, math.sin(a) * 0.08, z)) for z in (COL1 - 0.04, COL1 + 1.2) for a in [i * math.pi / 4 for i in range(8)]], IRON)
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 700, 700
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
    for yaw, d, z in ((0, 3.2, 1.2), (35, 3.2, 1.4), (120, 3.2, 1.3), (15, 1.6, 0.9)):
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, -math.cos(a) * d, z)
        cam.rotation_euler = (Vector((0, 0, 0.55 if d > 2 else 0.62)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'socket_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
