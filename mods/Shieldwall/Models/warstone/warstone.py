# The Warstone (Shieldwall 0.5.0+): a split, iron-bound menhir on a rough plinth ringed by stone shields, war runes cut into it.
# Run: blender -b --python ~/.cache/warstone/warstone.py -- [render]   (writes ~/.cache/warstone/warstone.json.gz, and previews with "render")
# Blender frame: z up, front -y. Written out in the game's frame (x, z, y), triangles per material: 0 rock, 1 iron, 2 runes.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

OUT = '/home/bob/.cache/warstone/'
ROCK, IRON, RUNE = 0, 1, 2
rng = random.Random(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [bpy.data.materials.new(n) for n in ('rock', 'iron', 'rune')]

def link(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[mat])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob

def hull(name, pts, mat=ROCK):
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(4), verts=bm.verts, edges=bm.edges)  # flat chisel facets
    return link(name, bm, mat)

def ring(z, hw, hd, n=9, jit=0.07, cx=0.0, cy=0.0, twist=0.0):
    pts = []
    for i in range(n):
        a = 2 * math.pi * (i + rng.uniform(-0.25, 0.25)) / n + twist
        k = 1 + rng.uniform(-jit, jit)
        # a squared-off ellipse: a slab, not a pillar
        c, s = math.cos(a), math.sin(a)
        r = 1 / ((abs(c) ** 3 + abs(s) ** 3) ** (1 / 3))
        pts.append(Vector((cx + hw * k * c * r, cy + hd * k * s * r, z + rng.uniform(-0.03, 0.03))))
    return pts

# ---- the menhir: a massive slab, broad at the foot, leaning a little, its head sheared off on a slant (one jagged peak) ----
body = []
for i, z in enumerate([-0.25, 0.35, 1.0, 1.65, 2.25]):
    t = max(z, 0) / 2.6
    body += ring(z, 0.84 * (1 - 0.17 * t), 0.54 * (1 - 0.2 * t), n=8, jit=0.12, cx=-0.08 * t, cy=0.05 * t, twist=0.13 * i)
# the broken head: high on the left, sheared down to the right
for k in range(9):
    x = -0.62 + 1.2 * k / 8
    body.append(Vector((x - 0.08, rng.uniform(-0.28, 0.28), 2.72 - 0.42 * (k / 8) + rng.uniform(-0.12, 0.12))))
body += [Vector((-0.45, 0.04, 3.32)), Vector((-0.3, -0.16, 3.05)), Vector((-0.58, 0.2, 3.0)), Vector((-0.62, -0.1, 2.9))]
hull('menhir', body)
# a jagged splinter riding the shear, and a fallen chunk at its foot
hull('splinter', ring(2.45, 0.2, 0.16, n=5, jit=0.2, cx=0.25, cy=0.05) + [Vector((0.42, 0.08, 2.92)), Vector((0.32, -0.05, 2.8))])
hull('chunk', [Vector((0.95 + rng.uniform(-0.25, 0.25), -0.75 + rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.32))) for _ in range(12)])

# ---- the plinth: a sunk, fitted slab on a rough ring of boulders ----
hull('slab', ring(-0.2, 1.1, 0.84, n=10, jit=0.06) + ring(0.24, 1.0, 0.76, n=10, jit=0.06))
for i in range(9):
    a = 2 * math.pi * (i + rng.uniform(0.2, 0.8)) / 9
    c = Vector((math.cos(a) * 1.2, math.sin(a) * 0.95, 0))
    sz = rng.uniform(0.75, 1.25)
    hull(f'block{i}', [c + Vector((rng.uniform(-0.3, 0.3) * sz, rng.uniform(-0.25, 0.25) * sz, rng.uniform(-0.25, 0.12 + 0.12 * sz))) for _ in range(12)])

# ---- (unused) a stone shield with an iron rim and boss ----
def shield(ang, lean, R=0.38, roll=0.0, sink=0.0):
    T, n = 0.07, 18
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=n, radius1=R, radius2=R * 0.96, depth=T)
    face = link('shield', bm, ROCK)
    bm = bmesh.new()  # rim: a flat ring a little proud of the face
    outer = bmesh.ops.create_circle(bm, cap_ends=False, segments=n, radius=R + 0.025)['verts']
    inner = bmesh.ops.create_circle(bm, cap_ends=False, segments=n, radius=R - 0.04)['verts']
    for k in range(n):
        bm.faces.new((outer[k], outer[(k + 1) % n], inner[(k + 1) % n], inner[k]))
    geo = bmesh.ops.extrude_face_region(bm, geom=list(bm.faces))
    bmesh.ops.translate(bm, vec=Vector((0, 0, T + 0.03)), verts=[v for v in geo['geom'] if isinstance(v, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.translate(bm, vec=Vector((0, 0, -T / 2 - 0.005)), verts=bm.verts)
    rim = link('rim', bm, IRON)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=5, radius=0.1)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -0.01], context='VERTS')
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary])
    bmesh.ops.translate(bm, vec=Vector((0, 0, T / 2 - 0.01)), verts=bm.verts)
    boss = link('boss', bm, IRON)
    # stand it on its rim, facing out, leaning back against the plinth
    out = Vector((math.cos(ang), math.sin(ang), 0))
    rot = Matrix.Rotation(ang - math.pi / 2, 4, 'Z') @ Matrix.Rotation(math.radians(90 - lean), 4, 'X') @ Matrix.Rotation(math.radians(roll), 4, 'Z')
    pos = Vector((math.cos(ang) * 1.36, math.sin(ang) * 1.1, R - 0.06 - sink))
    for ob in (face, rim, boss):
        ob.data.transform(Matrix.Translation(pos) @ rot)
# (the shields themselves are the game's own, set round the plinth by Look.cs)

# ---- iron: two bands cinched round the menhir, spiked ----
menhir = bpy.data.objects['menhir']
def section(z0, z1):
    # the menhir's outline between two heights (from a copy cut by both planes)
    bm = bmesh.new(); bm.from_mesh(menhir.data)
    for z, keep_above in ((z0, True), (z1, False)):
        g = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=g, plane_co=(0, 0, z), plane_no=(0, 0, 1), clear_inner=keep_above, clear_outer=not keep_above)
    pts = [v.co.copy() for v in bm.verts]; bm.free()
    return pts
def band(z0, z1, spikes):
    pts = section(z0, z1)
    cx = sum((p.x for p in pts), 0) / len(pts); cy = sum((p.y for p in pts), 0) / len(pts)
    pts = [Vector((cx + (p.x - cx) * 1.05 + math.copysign(0.02, p.x - cx), cy + (p.y - cy) * 1.08 + math.copysign(0.02, p.y - cy), p.z)) for p in pts]
    hull('band', pts, IRON)
    bvh = BVHTree.FromObject(bpy.data.objects[-1] if False else menhir, bpy.context.evaluated_depsgraph_get())
    zm = (z0 + z1) / 2
    for a in spikes:
        d = Vector((math.cos(a), math.sin(a), 0))
        # where the band's surface is in that direction
        far = Vector((cx, cy, zm)) + d * 3
        hit = bvh.ray_cast(far, -d)
        base = (hit[0] if hit[0] else Vector((cx, cy, zm)) + d * 0.6) + d * 0.04
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=5, radius1=0.075, radius2=0.0, depth=0.34)
        bmesh.ops.translate(bm, vec=Vector((0, 0, 0.17)), verts=bm.verts)
        ob = link('spike', bm, IRON)
        up = d.copy(); up.z = 0.18; up.normalize()
        ob.data.transform(Matrix.Translation(base) @ up.to_track_quat('Z', 'Y').to_matrix().to_4x4())
band(0.95, 1.13, [math.radians(a) for a in (0, 180, 30, 150, 215, 325)])
band(1.98, 2.12, [math.radians(a) for a in (0, 180, 55, 125)])

# ---- war runes cut into the front and back: a great Týr between the horns' feet, a column of runes beneath ----
TYR = [(.5, 0, .5, 1), (.5, 1, .12, .62), (.5, 1, .88, .62)]
UR = [(.2, 0, .2, 1), (.2, 1, .8, .72), (.8, .72, .8, 0)]
THURS = [(.25, 0, .25, 1), (.25, .78, .78, .5), (.78, .5, .25, .22)]
HAGAL = [(.25, 0, .25, 1), (.75, 0, .75, 1), (.25, .62, .75, .38)]
SOL = [(.25, 1, .25, .55), (.25, .55, .75, .45), (.75, .45, .75, 0)]
ODAL = [(.5, 1, .15, .62), (.5, 1, .85, .62), (.15, .62, .85, .2), (.85, .62, .15, .2), (.15, .2, .05, 0), (.85, .2, .95, 0)]
def strokes_on(face_dir, glyphs, top, H, W, gap, stroke, depth):
    """Boxes cutting each stroke into the menhir's face toward face_dir (-1 front, +1 back)."""
    bm = bmesh.new(); bm.from_mesh(menhir.data); tree = BVHTree.FromBMesh(bm); bm.free()
    cutters = []
    for i, g in enumerate(glyphs):
        y0 = top - i * (H + gap) - H
        for (x1, v1, x2, v2) in g:
            if face_dir > 0: x1, x2 = 1 - x1, 1 - x2  # read the right way round from behind
            a = Vector(((x1 - .5) * W, 0, y0 + v1 * H)); b = Vector(((x2 - .5) * W, 0, y0 + v2 * H))
            mid = (a + b) / 2
            hit = tree.ray_cast(Vector((mid.x, face_dir * 3, mid.z)), Vector((0, -face_dir, 0)))
            s = hit[0].y if hit[0] else 0
            d = b - a; L = d.length + stroke; ang = math.atan2(d.z, d.x)
            bmc = bmesh.new(); bmesh.ops.create_cube(bmc, size=1.0)
            bmesh.ops.scale(bmc, vec=Vector((L, 0.3, stroke)), verts=bmc.verts)
            bmesh.ops.rotate(bmc, verts=bmc.verts, cent=Vector(), matrix=Matrix.Rotation(-ang, 3, 'Y'))
            bmesh.ops.translate(bmc, vec=Vector((mid.x, s + face_dir * (0.15 - depth), mid.z)), verts=bmc.verts)
            cutters.append(link('stroke', bmc, RUNE))
    return cutters
def cut(target, cutters):
    cutter = cutters[0]
    for c in cutters[1:]:
        u = cutter.modifiers.new('u', 'BOOLEAN'); u.operation = 'UNION'; u.solver = 'EXACT'; u.object = c
        bpy.context.view_layer.objects.active = cutter; bpy.ops.object.modifier_apply(modifier='u'); bpy.data.objects.remove(c)
    m = target.modifiers.new('cut', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.solver = 'EXACT'; m.object = cutter
    m.use_hole_tolerant = True; m.material_mode = 'TRANSFER'
    bpy.context.view_layer.objects.active = target; bpy.ops.object.modifier_apply(modifier='cut'); bpy.data.objects.remove(cutter)
cuts = strokes_on(-1, [TYR], 2.66, 0.44, 0.32, 0, 0.06, 0.04)
cuts += strokes_on(-1, [THURS, UR, HAGAL, SOL], 1.86, 0.15, 0.11, 0.05, 0.035, 0.03)
cuts += strokes_on(1, [ODAL, TYR, HAGAL, UR], 1.88, 0.15, 0.11, 0.05, 0.035, 0.03)
cut(menhir, cuts)

# ---- write it out: flat facets, box-mapped uvs in metres (scaled per material in the game) ----
def export():
    verts, norms, uvs, tris, index = [], [], [], [[], [], []], {}
    for ob in bpy.context.scene.collection.objects:
        me = ob.data
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for p in me.polygons:
            slot = mats.index(me.materials[p.material_index]) if me.materials else ROCK
            n = p.normal; corner = []
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                u, v = (co.x, co.z) if abs(n.y) >= max(abs(n.x), abs(n.z)) else ((co.y, co.z) if abs(n.x) >= abs(n.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + 'warstone.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    zs = verts[1::3]
    print('warstone verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'height', round(max(zs), 2), 'x', round(min(verts[0::3]), 2), round(max(verts[0::3]), 2), 'z', round(min(verts[2::3]), 2), round(max(verts[2::3]), 2))

def preview():
    cols = [(0.32, 0.3, 0.28, 1), (0.12, 0.12, 0.13, 1), (1.0, 0.25, 0.08, 1)]
    for m, c in zip(mats, cols):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = c
        if m.name == 'iron': b.inputs['Metallic'].default_value = 0.8; b.inputs['Roughness'].default_value = 0.45
        if m.name == 'rune': b.inputs['Emission Color'].default_value = c; b.inputs['Emission Strength'].default_value = 4
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 700, 800
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.6
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
    for yaw in (0, 35, 90, 180):
        a = math.radians(yaw); d = 9.5
        cam.location = (math.sin(a) * d, -math.cos(a) * d, 2.6)
        cam.rotation_euler = (Vector((0, 0, 1.5)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'preview_{yaw}.png'; bpy.ops.render.render(write_still=True)

export()
if 'render' in sys.argv: preview()
