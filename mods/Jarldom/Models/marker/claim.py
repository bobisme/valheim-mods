# The ore claim (Jarldom 0.1.48+): a prospector's marker. A stout log driven into a heap of rubble and ore, a pick and a sledge crossed
# and lashed on its front, a bracket arm at its head with an iron lantern hung from it (its panes glow), and a hooped bucket of ore beside.
# Run: blender -b --python ~/.cache/jarldom-stake/claim.py -- [render]   (writes ~/.cache/jarldom-stake/claim.json.gz; previews with "render")
# Blender frame: z up, front -y. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres), triangles per
# material: 0 wood, 1 rope, 2 iron, 3 stone, 4 ore, 5 glow (the lantern's panes).
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/jarldom-stake/'
WOOD, ROPE, IRON, STONE, ORE, GLOW = range(6)
SMOOTH = ('lash', 'hoop', 'pick', 'ring', 'link')  # (objects drawn smooth; the rest keep hard facets)
rng = random.Random(31)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in ('wood', 'rope', 'iron', 'stone', 'ore', 'glow')]

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

def tube(name, pts, radius, mat, cyclic=False, taper=None):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 2
    cu.use_fill_caps = not cyclic
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1); sp.use_cyclic_u = cyclic
    for k, p in enumerate(pts):
        sp.points[k].co = (p.x, p.y, p.z, 1)
        if taper: sp.points[k].radius = taper(k / (len(pts) - 1))
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

def ring(z, r, n, jit=0.0, rot=0.0, at=Vector()):
    return [at + Vector((math.cos(2 * math.pi * i / n + rot) * r * (1 + rng.uniform(-jit, jit)),
                         math.sin(2 * math.pi * i / n + rot) * r * (1 + rng.uniform(-jit, jit)), z)) for i in range(n)]

def lump(name, at, size, mat, squash=0.7):
    """A broken rock: a hull of points scattered on a squashed ball."""
    pts = []
    for _ in range(14):
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        pts.append(at + Vector((d.x * size * rng.uniform(0.75, 1.1), d.y * size * rng.uniform(0.75, 1.1), d.z * size * squash * rng.uniform(0.75, 1.1))))
    return hull(name, pts, mat)

# ---- the post: a stout log, its top sawn flat with the edge knocked off ----
R, TOP = 0.062, 1.32
lumps_ = [rng.uniform(-0.05, 0.05) for _ in range(9)]
def lring(z, r):
    return [Vector((math.cos(2 * math.pi * i / 9 + 0.3) * r * (1 + lumps_[i]), math.sin(2 * math.pi * i / 9 + 0.3) * r * (1 + lumps_[i]), z)) for i in range(9)]
hull('post', lring(0.0, R * 1.08) + lring(0.5, R * 1.02) + lring(TOP - 0.02, R * 0.97) + lring(TOP, R * 0.85), WOOD)

# ---- the heap at its foot: rubble, and lumps of ore among it ----
for k in range(11):
    a = 2 * math.pi * k / 11 + rng.uniform(-0.2, 0.2); d = rng.uniform(0.1, 0.2)
    s = rng.uniform(0.06, 0.1)
    lump('rock', Vector((math.cos(a) * d, math.sin(a) * d, s * 0.35)), s, STONE)
for k in range(4):
    a = 2 * math.pi * k / 4 + 0.6; d = rng.uniform(0.07, 0.12)
    lump('rock', Vector((math.cos(a) * d, math.sin(a) * d, 0.1)), rng.uniform(0.05, 0.07), STONE)
for k in range(7):
    a = 2 * math.pi * k / 7 + 0.25; d = rng.uniform(0.12, 0.24)
    lump('ore', Vector((math.cos(a) * d, math.sin(a) * d, rng.uniform(0.05, 0.13))), rng.uniform(0.03, 0.05), ORE, squash=0.85)

# ---- a bucket of ore beside it: a staved tub, two iron hoops, heaped with ore ----
B = Vector((0.36, -0.1, 0.0)); BH = 0.2
hull('tub', ring(0.0, 0.1, 14, at=B) + ring(BH, 0.12, 14, at=B), WOOD)
for z, r in ((0.035, 0.103), (BH - 0.035, 0.117)):
    tube('hoop', ring(z, r + 0.003, 20, at=B), 0.006, IRON, cyclic=True)
for k in range(6):
    a = 2 * math.pi * k / 6; d = 0.055 if k else 0.0
    lump('ore', B + Vector((math.cos(a) * d, math.sin(a) * d, BH + 0.01 + (0.03 if k == 0 else 0))), rng.uniform(0.035, 0.045), ORE, squash=0.8)
tube('ring', [B + Vector((0.123 * math.cos(math.pi * t), 0, BH + 0.07 * math.sin(math.pi * t))) for t in [i / 12 for i in range(13)]], 0.0045, IRON)  # its bail

# ---- a pick and a sledge crossed on its front, lashed on ----
FRONT = -R - 0.022
CROSS = 0.74
def tool(angle, length, depth):
    d = Vector((math.sin(angle), 0, math.cos(angle)))
    a = Vector((0, depth, CROSS)) - d * length * 0.45; b = a + d * length  # (its foot and top)
    round_ = [Vector((math.cos(t) * 0.015, math.sin(t) * 0.015, 0)) for t in [i * math.pi / 4 for i in range(8)]]
    hull('haft', [a + o for o in round_] + [b + o for o in round_], WOOD)
    return d, b
d, top = tool(math.radians(-36), 0.62, FRONT)
# the pick's head: an arc of iron to two points, through a socket at the haft's top
n = Vector((d.z, 0, -d.x))
arc = [top + n * (0.16 * t) - d * (0.05 * t * t) for t in [i / 10 - 1 for i in range(21)]]
tube('pick', arc, 0.017, IRON, taper=lambda t: max(0.15, 1 - abs(2 * t - 1) ** 1.6))
hull('socket', [top + n * x + d * z + Vector((0, y, 0)) for x in (-0.025, 0.025) for z in (-0.03, 0.025) for y in (-0.022, 0.022)], IRON)
d2, top2 = tool(math.radians(36), 0.56, FRONT - 0.006)
n2 = Vector((d2.z, 0, -d2.x))
hull('sledge', [top2 + n2 * x + d2 * z + Vector((0, y, 0)) for x in (-0.075, 0.075) for z in (-0.03, 0.03) for y in (-0.03, 0.03)] +
     [top2 + n2 * x + d2 * z + Vector((0, y, 0)) for x in (-0.085, 0.085) for z in (-0.022, 0.022) for y in (-0.022, 0.022)], IRON)
# the lashing: turns round the post and both hafts where they cross
for k, dz in enumerate((-0.03, 0.0, 0.03)):
    pts = []
    for i in range(24):
        a = 2 * math.pi * i / 24
        y = math.sin(a) * (R + 0.012)
        if y < 0: y = min(y, math.sin(a) * (R + 0.012)) * (1.0 + 0.55 * max(0, -math.sin(a)) ** 3)  # out over the hafts in front
        pts.append(Vector((math.cos(a) * (R + 0.012), y, CROSS + dz + 0.01 * math.cos(a))))
    tube('lash', pts, 0.0065, ROPE, cyclic=True)

# ---- a bracket at its head, a lantern hung from it ----
ARM = 0.42
hull('arm', [Vector((x, y, z)) for x in (-0.02, ARM) for y in (-0.022, 0.022) for z in (TOP - 0.1, TOP - 0.055)], WOOD)
hull('brace', [Vector((x, y, z)) for x, z in ((R * 0.6, TOP - 0.38), (R * 0.6 + 0.03, TOP - 0.38), (ARM * 0.62, TOP - 0.1), (ARM * 0.62 - 0.05, TOP - 0.1)) for y in (-0.017, 0.017)], WOOD)
for at in (Vector((ARM * 0.62 - 0.025, -0.024, TOP - 0.11)), Vector((0.02, -0.024, TOP - 0.078))):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.008, radius2=0.008, depth=0.006)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'X')); bmesh.ops.translate(bm, vec=at, verts=bm.verts)
    link('nail', bm, IRON)
HOOK = Vector((ARM - 0.04, 0, TOP - 0.1))
tube('ring', [HOOK + Vector((0.016 * math.sin(a), 0, -0.016 + 0.016 * math.cos(a))) for a in [i * 2 * math.pi / 14 for i in range(14)]], 0.004, IRON, cyclic=True)
for k in range(2):
    c = HOOK + Vector((0, 0, -0.045 - 0.032 * k))
    tube('link', [c + Vector((0, 0.009 * math.sin(a), 0.02 * math.cos(a))) if k % 2 == 0 else c + Vector((0.009 * math.sin(a), 0, 0.02 * math.cos(a))) for a in [i * 2 * math.pi / 12 for i in range(12)]], 0.0035, IRON, cyclic=True)
L = HOOK + Vector((0, 0, -0.22))  # the lantern's foot
W, LH = 0.05, 0.15
hull('lamp', [L + Vector((x, y, z)) for x in (-W - 0.012, W + 0.012) for y in (-W - 0.012, W + 0.012) for z in (0, 0.018)], IRON)        # its base
hull('glass', [L + Vector((x, y, z)) for x in (-W + 0.004, W - 0.004) for y in (-W + 0.004, W - 0.004) for z in (0.015, LH)], GLOW)    # its panes
for sx in (-1, 1):
    for sy in (-1, 1):
        hull('bar', [L + Vector((sx * W + x, sy * W + y, z)) for x in (-0.007, 0.007) for y in (-0.007, 0.007) for z in (0.01, LH + 0.005)], IRON)
for z in (0.07,):  # a band round the panes
    for s in (-1, 1):
        hull('bar', [L + Vector((x, s * W + y, z + h)) for x in (-W, W) for y in (-0.005, 0.005) for h in (-0.004, 0.004)], IRON)
        hull('bar', [L + Vector((s * W + y, x, z + h)) for x in (-W, W) for y in (-0.005, 0.005) for h in (-0.004, 0.004)], IRON)
hull('lamp', [L + Vector((x, y, LH)) for x in (-W - 0.016, W + 0.016) for y in (-W - 0.016, W + 0.016)] + [L + Vector((0, 0, LH + 0.065))], IRON)  # its hood
tube('ring', [L + Vector((0.017 * math.sin(a), 0, LH + 0.075 + 0.017 * math.cos(a))) for a in [i * 2 * math.pi / 14 for i in range(14)]], 0.004, IRON, cyclic=True)

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
                # box-mapped in metres; the wood keeps its grain up the post (v along z on the sides)
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + 'claim.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    print('claim verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'height', round(max(verts[1::3]), 2))

def preview():
    cols = [(0.42, 0.3, 0.18, 1), (0.6, 0.5, 0.32, 1), (0.13, 0.13, 0.14, 1), (0.4, 0.4, 0.42, 1), (0.55, 0.3, 0.14, 1), (1.0, 0.7, 0.3, 1)]
    for m, c in zip(mats, cols):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = c
        if m.name in ('iron', 'ore'): b.inputs['Metallic'].default_value = 0.6
        if m.name == 'glow': b.inputs['Emission Color'].default_value = c; b.inputs['Emission Strength'].default_value = 3
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH' and ob.name.startswith(SMOOTH):
            for p in ob.data.polygons: p.use_smooth = True
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 600, 800
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
    for yaw, d, z, look in ((0, 3.0, 0.9, 0.65), (35, 3.0, 0.9, 0.65), (90, 3.0, 0.9, 0.65), (25, 1.2, 1.0, 0.95)):
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, -math.cos(a) * d, z)
        cam.rotation_euler = (Vector((0.1, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'claim_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
