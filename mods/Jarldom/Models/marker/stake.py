# The marking staff (the look of the hammer's "Mark rooms and zones", Jarldom 0.1.30+; a staff you'd hold since 0.1.32, ribbons for its
# pennant since 0.1.34): a slim shaft shod with iron, a leather grip at the hand, an iron band under its carved head and finial, red and
# gold cloth ribbons tied round it under the band, and a knotted measuring cord wound round the shaft with a plumb bob on its end.
# Run: blender -b --python ~/.cache/jarldom-stake/stake.py -- [render]   (writes ~/.cache/jarldom-stake/stake.json.gz; previews with "render")
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material: 0 wood, 1 rope, 2 iron, 3 cloth (red), 4 paint (the gold ribbons), 5 leather.
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

# ---- ribbons tied round it under the band: a cloth knot, and streamers of red and gold hanging from it, twisting and lifting in the wind ----
TIE = BAND - 0.045
tube('wrap', helix(0.036, TIE - 0.012, TIE + 0.012, 2.5), 0.0075, CLOTH)
bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.017)  # the knot, on the right
bmesh.ops.scale(bm, vec=Vector((1.1, 0.9, 1.25)), verts=bm.verts)
bmesh.ops.translate(bm, vec=Vector((0.043, -0.004, TIE)), verts=bm.verts)
link('knot', bm, CLOTH)
def streamer(name, mat, angle, length, width, lift, twist, sway, phase):
    """A cloth strip from the knot: out from the shaft at an angle (round from +x), falling, lifted a little by the wind, twisting."""
    n = 22
    out = Vector((math.cos(angle), math.sin(angle), 0)); wind = Vector((0.2, -1, 0)).normalized()
    pts = []
    for k in range(n + 1):
        t = k / n
        p = Vector((0.045, 0, TIE)) + out * (0.02 + 0.07 * t ** 0.6) + Vector((0, 0, -length * t)) \
            + wind * (lift * t * t) + Vector((0, 0, lift * 0.5 * t * t)) + out.cross(Vector((0, 0, 1))) * (sway * math.sin(t * 5 + phase) * t)
        pts.append(p)
    bm = bmesh.new(); rows = []
    for k, p in enumerate(pts):
        tan = (pts[min(k + 1, n)] - pts[max(k - 1, 0)]).normalized()
        side = tan.cross(out).normalized()
        side = Matrix.Rotation(twist * k / n + 0.3 * math.sin(k / n * 4 + phase), 3, tan) @ side
        w = width * (1 - 0.15 * k / n)
        rows.append((bm.verts.new(p - side * w / 2), bm.verts.new(p + side * w / 2)))
    for k in range(n):
        bm.faces.new((rows[k][0], rows[k][1], rows[k + 1][1], rows[k + 1][0]))
    # a forked end: the last row pinched in at the middle
    a0, a1 = rows[n]; mid = bm.verts.new((a0.co + a1.co) / 2 + (pts[n] - pts[n - 1]).normalized() * -0.025)
    bm.faces.new((a0, mid, a1))
    ob = link(name, bm, mat)
    m = ob.modifiers.new('thick', 'SOLIDIFY'); m.thickness = 0.004; m.offset = 0
    return ob
streamer('ribbon1', CLOTH, -0.35, 0.44, 0.05, 0.1, 2.2, 0.03, 0.0)
streamer('ribbon2', PAINT, -0.05, 0.35, 0.036, 0.13, -1.8, 0.025, 1.3)
streamer('ribbon3', CLOTH, 0.3, 0.28, 0.042, 0.08, 1.4, 0.02, 2.4)
streamer('ribbon4', PAINT, -0.75, 0.22, 0.03, 0.06, -2.6, 0.015, 3.1)

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
