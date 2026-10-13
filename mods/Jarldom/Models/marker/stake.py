# The marking staff (the look of the hammer's "Mark rooms and zones", Jarldom 0.1.30+; a staff you'd hold since 0.1.32): a slim
# shaft shod with iron, a leather grip at the hand, an iron band under its carved head and finial, a swallow-tailed pennant on a
# lashed crossbar with the othala rune (home, land held) in gold on it, and a knotted measuring cord wound round the shaft with a plumb
# bob on its end.
# Run: blender -b --python ~/.cache/jarldom-stake/stake.py -- [render]   (writes ~/.cache/jarldom-stake/stake.json.gz; previews with "render")
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material: 0 wood, 1 rope, 2 iron, 3 cloth, 4 paint (the pennant's stripe and rune), 5 leather.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/jarldom-stake/'
WOOD, ROPE, IRON, CLOTH, PAINT, LEATHER = range(6)
SMOOTH = {ROPE, CLOTH, PAINT, LEATHER}  # (the wood and iron keep hard facets)
rng = random.Random(11)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in ('wood', 'rope', 'iron', 'cloth', 'paint', 'leather')]

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

def ring(z, r, n, jit=0.0, rot=0.0):
    return [Vector((math.cos(2 * math.pi * i / n + rot) * r * (1 + rng.uniform(-jit, jit)),
                    math.sin(2 * math.pi * i / n + rot) * r * (1 + rng.uniform(-jit, jit)), z)) for i in range(n)]

def tube(name, pts, radius, mat):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 2
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

def bowed(z):  # how far the shaft bows out at a height (it follows the shaft's rings below)
    return 0.006 * math.sin(math.pi * min(1, max(0, (z - 0.07) / (BAND - 0.07))))

def helix(r, z0, z1, turns, wobble=0.0, a0=0.0):
    return [Vector((bowed(z0 + (z1 - z0) * k / (turns * 24)) + math.cos(a0 + k / 24 * 2 * math.pi) * r, math.sin(a0 + k / 24 * 2 * math.pi) * r,
                    z0 + (z1 - z0) * k / (turns * 24) + wobble * math.sin(a0 + k / 24 * 2 * math.pi))) for k in range(int(turns * 24) + 1)]

TOP = 1.8          # the finial's point
BAND = 1.53        # the iron band under the head
# ---- the shaft: eight faces, a touch thicker at the foot, slightly bowed like a cut sapling ----
shaft = []
for z, r, bow in ((0.07, 0.033, 0.0), (0.5, 0.032, 0.004), (1.0, 0.031, 0.006), (BAND, 0.029, 0.002)):
    shaft += [p + Vector((bow, 0, 0)) for p in ring(z, r, 8, jit=0.03, rot=0.1)]
hull('shaft', shaft, WOOD)
# the iron shoe at its foot
hull('shoe', ring(0.0, 0.006, 6) + ring(0.04, 0.03, 8) + ring(0.1, 0.036, 8), IRON)
hull('shoering', ring(0.09, 0.039, 10) + ring(0.115, 0.039, 10), IRON)
# the head: a swelling block carved above the band, a neck, a knop and a point
hull('head', ring(BAND + 0.01, 0.033, 8, rot=0.1) + ring(BAND + 0.035, 0.045, 8, jit=0.03, rot=0.1) + ring(BAND + 0.13, 0.045, 8, jit=0.03, rot=0.1) + ring(BAND + 0.15, 0.03, 8, rot=0.1), WOOD)
hull('neck', ring(BAND + 0.14, 0.022, 8) + ring(BAND + 0.18, 0.02, 8), WOOD)
hull('knop', ring(BAND + 0.175, 0.026, 8) + ring(BAND + 0.205, 0.038, 8, rot=0.2) + ring(BAND + 0.235, 0.032, 8) + ring(BAND + 0.255, 0.016, 8, rot=0.2) + [Vector((0, 0, TOP))], WOOD)
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.036, radius2=0.036, depth=0.03)
bmesh.ops.translate(bm, vec=Vector((0, 0, BAND)), verts=bm.verts)
link('band', bm, IRON)
for a in (-1.35, -1.8):
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.007)
    bmesh.ops.translate(bm, vec=Vector((math.cos(a) * 0.037, math.sin(a) * 0.037, BAND)), verts=bm.verts)
    link('rivet', bm, IRON)

# ---- the leather grip at the hand: a strap wound edge to edge, a tie at each end ----
GRIP0, GRIP1 = 0.98, 1.2
tube('grip', helix(0.034, GRIP0, GRIP1, 14), 0.0085, LEATHER)  # (turns edge to edge)
for z in (GRIP0 - 0.006, GRIP1 + 0.006):
    tube('tie', helix(0.037, z - 0.004, z + 0.004, 2), 0.004, ROPE)

# ---- the crossbar through the head, lashed, and the pennant hanging from it ----
BAR = BAND + 0.08
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.011, radius2=0.01, depth=0.33)
bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'))
bmesh.ops.translate(bm, vec=Vector((0.12, 0, BAR)), verts=bm.verts)
link('crossbar', bm, WOOD)
bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.015)
bmesh.ops.translate(bm, vec=Vector((0.29, 0, BAR)), verts=bm.verts)
link('knob', bm, WOOD)
lash = [Vector((math.cos(k / 24 * 2 * math.pi) * 0.048, math.sin(k / 24 * 2 * math.pi) * 0.048, BAR - 0.022 + 0.044 * k / 72 + 0.012 * math.sin(k / 24 * 2 * math.pi))) for k in range(73)]
tube('lashing', lash, 0.0048, ROPE)
# the cloth: swallow-tailed, stirring; a gold stripe under the bar and the othala rune in gold on its middle
L, HC, NX, NZ = 0.22, 0.3, 36, 48
X0 = 0.055
ODAL = [(.5, 1, .12, .62), (.5, 1, .88, .62), (.12, .62, .88, .2), (.88, .62, .12, .2), (.12, .2, .02, 0), (.88, .2, .98, 0)]
def on_rune(u, v):
    # the rune in cloth coordinates: u along the bar (0-1), v down from it (0-1); it fills the cloth's middle
    gx, gy = (u - 0.24) / 0.52, 1 - (v - 0.24) / 0.46     # into the glyph's unit box (y up)
    if not (-0.1 < gx < 1.1 and -0.1 < gy < 1.1): return False
    for (x1, y1, x2, y2) in ODAL:
        ax, ay, bx, by = x1, y1, x2, y2
        dx, dy = bx - ax, by - ay; t = max(0, min(1, ((gx - ax) * dx + (gy - ay) * dy) / (dx * dx + dy * dy)))
        if math.hypot(gx - (ax + t * dx), (gy - (ay + t * dy)) * 0.46 / 0.52) < 0.085: return True
    return False
bm = bmesh.new(); grid = {}
for i in range(NX + 1):
    for j in range(NZ + 1):
        u, v = i / NX, j / NZ
        depth = HC * (1 - 0.1 * u)
        notch = 0.12 * (1 - abs(u - 0.5) * 2)
        z = BAR - 0.012 - v * (depth - notch * max(0, (v - 0.62) / 0.38))
        y = 0.022 * math.sin(v * 3.2 + u * 1.5) * v + 0.012 * math.sin(u * 7) * v
        grid[i, j] = bm.verts.new((X0 + u * L, y, z))
for i in range(NX):
    for j in range(NZ):
        f = bm.faces.new((grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]))
        u, v = (i + 0.5) / NX, (j + 0.5) / NZ
        f.material_index = 1 if (0.07 < v < 0.13) or on_rune(u, v) else 0
cloth = link('cloth', bm, CLOTH); cloth.data.materials.append(mats[PAINT])
m = cloth.modifiers.new('thick', 'SOLIDIFY'); m.thickness = 0.006; m.offset = 0
bm = bmesh.new()  # the hem wrapped over the bar
bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.015, radius2=0.015, depth=L + 0.01)
bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'))
bmesh.ops.translate(bm, vec=Vector((X0 + L / 2, 0, BAR)), verts=bm.verts)
link('hem', bm, CLOTH)

# ---- the measuring cord: a coil below the grip, its tail out over the left and hanging, knotted every span, a plumb bob on its end ----
coil = helix(0.038, 0.78, 0.88, 4.5, a0=-0.6)
end = coil[-1]
ex = Vector((-0.11, -0.03, end.z - 0.05))
tail = [end]
for k in range(1, 25):
    s = k / 24
    if s < 0.25: q = end.lerp(ex, s / 0.25); q.z += 0.025 * math.sin(s / 0.25 * math.pi)
    else: q = ex + Vector((0.008 * math.sin(s * 4), 0, -0.4 * (s - 0.25) / 0.75))
    tail.append(q)
tube('cord', coil + tail[1:], 0.005, ROPE)
for s in (0.45, 0.72):
    p = tail[int(s * 24)]
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.0105)
    bmesh.ops.scale(bm, vec=Vector((1, 1, 1.35)), verts=bm.verts); bmesh.ops.translate(bm, vec=p, verts=bm.verts)
    link('knot', bm, ROPE)
bob = tail[-1]
bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.0, radius2=0.024, depth=0.055)
bmesh.ops.translate(bm, vec=bob + Vector((0, 0, -0.036)), verts=bm.verts)
link('bob', bm, IRON)
bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.024, radius2=0.009, depth=0.016)
bmesh.ops.translate(bm, vec=bob, verts=bm.verts)
link('bobcap', bm, IRON)

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
                n = cn[li].vector if slot in SMOOTH else p.normal
                fn = p.normal
                # box-mapped in metres; the wood keeps its grain up the shaft (v along z on the sides)
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + 'stake.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    print('stake verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'height', round(max(verts[1::3]), 2))

def preview():
    cols = [(0.42, 0.3, 0.18, 1), (0.6, 0.5, 0.32, 1), (0.13, 0.13, 0.14, 1), (0.55, 0.1, 0.06, 1), (0.85, 0.65, 0.25, 1), (0.25, 0.15, 0.09, 1)]
    for m, c in zip(mats, cols):
        b = m.node_tree.nodes['Principled BSDF'] if m.node_tree else None
        if b is None: m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = c
        if m.name == 'iron': b.inputs['Metallic'].default_value = 0.7
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH' and ob.data.materials and ob.data.materials[0].name in ('rope', 'cloth', 'leather'):
            for p in ob.data.polygons: p.use_smooth = True
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 500, 900
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
    for yaw in (0, 35, 90, 180):
        a = math.radians(yaw); d = 4.0
        cam.location = (math.sin(a) * d, -math.cos(a) * d, 1.1)
        cam.rotation_euler = (Vector((0.05, 0, 0.9)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'preview_{yaw}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
