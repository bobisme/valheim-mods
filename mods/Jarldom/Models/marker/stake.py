# The marking stake (the look of the hammer's "Mark rooms and zones", Jarldom 0.1.30+): a hewn stake wedged in a little cairn, an iron band
# under its carved head, the othala rune (home, land held) cut and painted on its face, a swallow-tailed pennant on a crossbar, and a
# knotted measuring cord wound round it with a plumb bob on its end.
# Run: blender -b --python ~/.cache/jarldom-stake/stake.py -- [render]   (writes ~/.cache/jarldom-stake/stake.json.gz; previews with "render")
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material: 0 wood, 1 rope, 2 iron, 3 cloth, 4 paint, 5 stone.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

OUT = '/home/bob/.cache/jarldom-stake/'
WOOD, ROPE, IRON, CLOTH, PAINT, STONE = range(6)
SMOOTH = {ROPE, CLOTH}  # (the rest keeps hard facets)
rng = random.Random(11)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in ('wood', 'rope', 'iron', 'cloth', 'paint', 'stone')]

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

def ring(z, r, n, jit=0.0, rot=0.0, sx=1.0):
    return [Vector((math.cos(2 * math.pi * i / n + rot) * r * sx * (1 + rng.uniform(-jit, jit)),
                    math.sin(2 * math.pi * i / n + rot) * r * (1 + rng.uniform(-jit, jit)), z)) for i in range(n)]

H = 1.27  # the top of the head
# ---- the stake: an axe-hewn post of seven faces, a touch thicker at the foot, a waist cut under its head, a pointed head ----
post = []
for z, r in ((0.0, 0.068), (0.5, 0.064), (0.98, 0.058)):
    post += ring(z, r, 7, jit=0.06, rot=0.2)
hull('post', post, WOOD)
head = ring(1.0, 0.05, 7, rot=0.2) + ring(1.03, 0.066, 7, jit=0.04, rot=0.2) + ring(1.12, 0.066, 7, jit=0.05, rot=0.2) + ring(1.14, 0.045, 7, rot=0.2)
hull('head', head, WOOD)
# a carved finial: a narrow neck, a swelling knop and a short point
hull('neck', ring(1.13, 0.036, 8) + ring(1.17, 0.032, 8), WOOD)
hull('knop', ring(1.165, 0.04, 8) + ring(1.195, 0.058, 8, rot=0.2) + ring(1.23, 0.05, 8) + ring(1.25, 0.026, 8, rot=0.2) + [Vector((0, 0, H))], WOOD)
# the iron band round its neck, with two rivets
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=14, radius1=0.071, radius2=0.071, depth=0.045)
bmesh.ops.translate(bm, vec=Vector((0, 0, 0.955)), verts=bm.verts)
link('band', bm, IRON)
for a in (-1.35, -1.8):
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.011)
    bmesh.ops.translate(bm, vec=Vector((math.cos(a) * 0.072, math.sin(a) * 0.072, 0.955)), verts=bm.verts)
    link('rivet', bm, IRON)

# ---- the crossbar through the head, and the pennant hanging from it ----
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.016, radius2=0.014, depth=0.44)
bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'))
bmesh.ops.translate(bm, vec=Vector((0.15, 0, 1.075)), verts=bm.verts)
link('crossbar', bm, WOOD)
# a pin knob at its end
bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.022)
bmesh.ops.translate(bm, vec=Vector((0.375, 0, 1.075)), verts=bm.verts)
link('knob', bm, WOOD)
# the cloth: hangs from the bar, swallow-tailed, stirring in the wind
L, Hc, NX, NZ = 0.3, 0.36, 12, 16
bm = bmesh.new(); grid = {}
for i in range(NX + 1):
    for j in range(NZ + 1):
        u, v = i / NX, j / NZ                          # u along the bar, v down from it
        x = 0.07 + u * L
        depth = Hc * (1 - 0.12 * u)
        notch = 0.16 * (1 - abs(u - 0.5) * 2)          # the swallowtail cut up into the hem's middle
        z = 1.06 - v * (depth - notch * max(0, (v - 0.55) / 0.45))
        y = 0.035 * math.sin(v * 3.2 + u * 1.5) * v + 0.02 * math.sin(u * 7) * v   # a ripple, more at the hem
        grid[i, j] = bm.verts.new((x, y, z))
for i in range(NX):
    for j in range(NZ):
        f = bm.faces.new((grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]))
        f.material_index = 1 if j in (2, 3) else 0  # a gold stripe under the bar
cloth = link('cloth', bm, CLOTH); cloth.data.materials.append(mats[PAINT])
m = cloth.modifiers.new('thick', 'SOLIDIFY'); m.thickness = 0.008; m.offset = 0
# a hem: the cloth wrapped over the bar
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.022, radius2=0.022, depth=L + 0.01)
bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'))
bmesh.ops.translate(bm, vec=Vector((0.07 + L / 2, 0, 1.075)), verts=bm.verts)
link('hem', bm, CLOTH)

# ---- the measuring cord: wound round the stake, its tail hanging down with knots every span, a plumb bob at its end ----
def tube(name, pts, radius, mat):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 2
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o
coil = []
turns, z0, z1, rr = 4.5, 0.52, 0.66, 0.074
for k in range(int(turns * 24) + 1):
    t = k / 24; a = t * 2 * math.pi - 0.6
    coil.append(Vector((math.cos(a) * rr, math.sin(a) * rr, z0 + (z1 - z0) * t / turns)))
end = coil[-1]
# the tail: off the coil's last turn, out over the front and down, hanging in a slight curve
tail = [end]
ex = Vector((-0.16, -0.05, end.z - 0.06))  # out over the left, then hanging straight
for k in range(1, 25):
    s = k / 24
    if s < 0.25: q = end.lerp(ex, s / 0.25); q.z += 0.03 * math.sin(s / 0.25 * math.pi)
    else: q = ex + Vector((0.01 * math.sin(s * 4), 0, -0.36 * (s - 0.25) / 0.75))
    tail.append(q)
tube('cord', coil + tail[1:], 0.0075, ROPE)
# a lashing where the crossbar goes through the head: crossed turns over the joint
lash = []
for k in range(3 * 24 + 1):
    t = k / 24; a = t * 2 * math.pi
    lash.append(Vector((math.cos(a) * 0.07, math.sin(a) * 0.07, 1.045 + 0.06 * (t / 3) + 0.018 * math.sin(a))))
tube('lashing', lash, 0.0065, ROPE)
for s in (0.45, 0.72):  # knots: a span measured
    p = tail[int(s * 24)]
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.0145)
    bmesh.ops.scale(bm, vec=Vector((1, 1, 1.35)), verts=bm.verts); bmesh.ops.translate(bm, vec=p, verts=bm.verts)
    link('knot', bm, ROPE)
bob = tail[-1]
bm = bmesh.new()  # the plumb bob: an iron drop, point down
bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.0, radius2=0.03, depth=0.07)
bmesh.ops.translate(bm, vec=bob + Vector((0, 0, -0.045)), verts=bm.verts)
link('bob', bm, IRON)
bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.03, radius2=0.012, depth=0.02)
bmesh.ops.translate(bm, vec=bob + Vector((0, 0, -0.0)), verts=bm.verts)
link('bobcap', bm, IRON)

# ---- the cairn: five stones wedging its foot ----
for k in range(5):
    a = 2 * math.pi * k / 5 + rng.uniform(-0.3, 0.3); d = rng.uniform(0.12, 0.17); s = rng.uniform(0.07, 0.11)
    c = Vector((math.cos(a) * d, math.sin(a) * d, 0))
    hull(f'stone{k}', [c + Vector((rng.uniform(-s, s), rng.uniform(-s * 0.8, s * 0.8), rng.uniform(0, s * 1.1))) for _ in range(11)], STONE)

# ---- the othala rune cut into its front face and painted ----
postob = bpy.data.objects['post']
ODAL = [(.5, 1, .12, .62), (.5, 1, .88, .62), (.12, .62, .88, .2), (.88, .62, .12, .2), (.12, .2, .02, 0), (.88, .2, .98, 0)]
def cut_rune(target, glyph, top, Hh, W, stroke, depth):
    bm = bmesh.new(); bm.from_mesh(target.data); tree = BVHTree.FromBMesh(bm); bm.free()
    cutters = []
    for (x1, v1, x2, v2) in glyph:
        a = Vector(((x1 - .5) * W, 0, top - Hh + v1 * Hh)); b = Vector(((x2 - .5) * W, 0, top - Hh + v2 * Hh))
        mid = (a + b) / 2
        hit = tree.ray_cast(Vector((mid.x, -1, mid.z)), Vector((0, 1, 0)))
        s = hit[0].y if hit[0] else -0.06
        d = b - a; Ls = d.length + stroke; ang = math.atan2(d.z, d.x)
        bmc = bmesh.new(); bmesh.ops.create_cube(bmc, size=1.0)
        bmesh.ops.scale(bmc, vec=Vector((Ls, 0.1, stroke)), verts=bmc.verts)
        bmesh.ops.rotate(bmc, verts=bmc.verts, cent=Vector(), matrix=Matrix.Rotation(-ang, 3, 'Y'))
        bmesh.ops.translate(bmc, vec=Vector((mid.x, s - 0.05 + depth, mid.z)), verts=bmc.verts)
        cutters.append(link('stroke', bmc, PAINT))
    cutter = cutters[0]
    for c in cutters[1:]:
        u = cutter.modifiers.new('u', 'BOOLEAN'); u.operation = 'UNION'; u.solver = 'EXACT'; u.object = c
        bpy.context.view_layer.objects.active = cutter; bpy.ops.object.modifier_apply(modifier='u'); bpy.data.objects.remove(c)
    m = target.modifiers.new('cut', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.solver = 'EXACT'; m.object = cutter
    m.use_hole_tolerant = True; m.material_mode = 'TRANSFER'
    bpy.context.view_layer.objects.active = target; bpy.ops.object.modifier_apply(modifier='cut'); bpy.data.objects.remove(cutter)
# (the post is turned so a flat face looks to the front first)
best = min((f for f in postob.data.polygons if abs(f.normal.z) < 0.3), key=lambda f: abs(math.atan2(f.normal.y, f.normal.x) + math.pi / 2))
turn = -(math.atan2(best.normal.y, best.normal.x) + math.pi / 2)
for o in ('post', 'head'):
    bpy.data.objects[o].data.transform(Matrix.Rotation(turn, 4, 'Z'))
cut_rune(postob, ODAL, 0.86, 0.17, 0.085, 0.016, 0.012)

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
                # box-mapped in metres; wood keeps its grain up the stake (v along z on the sides)
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + 'stake.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    print('stake verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'height', round(max(verts[1::3]), 2))

def preview():
    cols = [(0.42, 0.3, 0.18, 1), (0.6, 0.5, 0.32, 1), (0.13, 0.13, 0.14, 1), (0.55, 0.1, 0.06, 1), (0.85, 0.65, 0.25, 1), (0.4, 0.4, 0.38, 1)]
    for m, c in zip(mats, cols):
        b = m.node_tree.nodes['Principled BSDF'] if m.node_tree else None
        if b is None: m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = c
        if m.name == 'iron': b.inputs['Metallic'].default_value = 0.7
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH' and ob.data.materials and ob.data.materials[0].name in ('rope', 'cloth'):
            for p in ob.data.polygons: p.use_smooth = True
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 600, 800
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
    for yaw in (0, 35, 90, 180):
        a = math.radians(yaw); d = 3.2
        cam.location = (math.sin(a) * d, -math.cos(a) * d, 1.0)
        cam.rotation_euler = (Vector((0.1, 0, 0.62)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'preview_{yaw}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
