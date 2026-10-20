# Shipyard's naust: a long timber boat shed standing at the shore, its land end on the ground, its sea end open over the water (a ship
# floats in and out there), the land end closed by a plank wall. An open slot runs the length of the ridge, 1.0 m wide, so a ship's
# standing mast (up to 12 m) passes through the roof: nothing crosses x ±0.5 above the water anywhere (no cross-ties; the tie beams
# come in pairs, each ending at a short post under its own ridge timber).
# Run: blender -b --python ~/.cache/shipyard_naust/naust.py -- [render] naust      (writes ~/.cache/shipyard_naust/naust.json.gz)
# What ships is the bake of this (naust_bake_conf.py, docs/model-baking.md). Helpers copied from yard.py (keep the two in step by hand).
# Blender frame: z up, +y toward the sea (the game's +z), +x right. Origin on the centreline at ground/water level, mid-length.
# Written in the game's frame (x, z, y), as yard.py: per corner position/normal/uv (uvs in metres; wood's grain along v), triangles
# per material: 0 wood (pine: wall and gable boards), 1 oak (posts, plates, beams, rafters, ridge timbers, bollards), 2 tar (the
# tarred roof planks), 3 iron (bolt heads, baked as decals).
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/shipyard_naust/'
WOOD, OAK, TAR, IRON = range(4)
NAMES = ('wood', 'oak', 'tar', 'iron')
SMOOTH = ()                         # (all squared timber: nothing drawn smooth)
mats = []
V = Vector

# ---------------- helpers (from yard.py) ----------------
def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats.clear()
    mats.extend(bpy.data.materials.new(n) for n in NAMES)

def link(name, bm, mat, grain=None):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[mat])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    if grain is not None: ob['grain'] = tuple(grain)
    return ob

def tube(name, pts, radius, mat, res=2, square=False):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = 0 if square else res; cu.use_fill_caps = True
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o['grain'] = tuple(pts[-1] - pts[0])
    return o

def box(name, lo, hi, mat, grain=None, bevel=0.0, jitter=0.0, seed=0):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    lo, hi = V(lo), V(hi)
    bmesh.ops.scale(bm, vec=hi - lo, verts=bm.verts); bmesh.ops.translate(bm, vec=(lo + hi) / 2, verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES', profile=0.5)
    if jitter:
        r = random.Random(seed)
        for v in bm.verts: v.co += V((r.uniform(-jitter, jitter), r.uniform(-jitter, jitter), r.uniform(-jitter, jitter)))
    return link(name, bm, mat, grain)

def beam(name, a, b, w, h, mat, up=(0, 0, 1), bevel=0.0, jitter=0.0, seed=0):
    # a squared timber from a to b (centre line), w wide across, h deep toward 'up'
    a, b = V(a), V(b); d = b - a; L = d.length; d.normalize()
    side = d.cross(V(up))
    if side.length < 1e-6: side = d.cross(V((1, 0, 0)))
    side.normalize(); u = side.cross(d)
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(w, L, h), verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES', profile=0.5)
    m = Matrix((side, d, u)).transposed().to_4x4(); m.translation = (a + b) / 2
    bm.transform(m)
    if jitter:
        r = random.Random(seed)
        for v in bm.verts: v.co += V((r.uniform(-jitter, jitter), r.uniform(-jitter, jitter), r.uniform(-jitter, jitter)))
    return link(name, bm, mat, grain=d)

def slab(name, x0, x1, y0, y1, zb, zt0, zt1, mat):
    # a gable board standing in x-z, its top cut on the slope (zt0 at x0, zt1 at x1); grain up
    bm = bmesh.new()
    c = [bm.verts.new(p) for p in ((x0, y0, zb), (x1, y0, zb), (x1, y0, zt1), (x0, y0, zt0), (x0, y1, zb), (x1, y1, zb), (x1, y1, zt1), (x0, y1, zt0))]
    for f in ((0, 1, 2, 3), (5, 4, 7, 6), (4, 0, 3, 7), (1, 5, 6, 2), (3, 2, 6, 7), (4, 5, 1, 0)): bm.faces.new([c[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, mat, grain=(0, 0, 1))

# ---------------- the shed's lines ----------------
HALF_L = 11.0                       # (y -11 land end .. +11 sea end)
FRAMES = [-10.7 + 21.4 * i / 6 for i in range(7)]   # posts every ~3.57 m
POST_X, POST_W = 3.6, 0.30          # post centres; posts x 3.45..3.75
POST_FOOT, PLATE_LO, PLATE_HI = -4.0, 3.25, 3.55
WALL_IN, WALL_LO = 3.39, -0.5       # the side boards lie on the posts' inner faces (the frame shows outside), from y -0.5 up to the plate
EAVE_X, EAVE_Z = 4.0, 3.5           # the roof's lower edge (top face)
SLOT = 0.5                          # the mast slot: x ±0.5 clear, the whole length
RIDGE_X0, RIDGE_X1, RIDGE_LO, RIDGE_HI = 0.5, 0.8, 5.7, 6.0    # each ridge timber
ROOF_TOP_Z = 5.85                   # the roof plane where it meets the ridge timber's outer face
K = (ROOF_TOP_Z - EAVE_Z) / (EAVE_X - RIDGE_X1)                  # slope (rise per metre in)
def roof_z(x): return EAVE_Z + (EAVE_X - abs(x)) * K           # the roof plane's top (under the planks' laps)
SLOPE_L = math.hypot(EAVE_X - RIDGE_X1, ROOF_TOP_Z - EAVE_Z)

def frames():
    # the posts down both sides, the wall plates on them, and in each frame: a tie beam from each post in to a short post under that
    # side's ridge timber (the pair stops 1 m apart: the mast slot), a rafter from the ridge timber down to the plate, a knee
    # brace post to tie beam; knee braces along the length post to plate and short post to ridge timber
    for s in (1, -1):
        sx = lambda x: s * x
        for i, y in enumerate(FRAMES):
            box(f'post_{s}_{i}', (sx(POST_X) - POST_W / 2, y - 0.15, POST_FOOT), (sx(POST_X) + POST_W / 2, y + 0.15, PLATE_LO), OAK,
                grain=(0, 0, 1), bevel=0.025, jitter=0.012, seed=i + 10 * (s > 0))
            # tie beam half, post to short post
            box(f'tie_{s}_{i}', (min(sx(3.8), sx(SLOT + 0.01)), y - 0.11, 3.27), (max(sx(3.8), sx(SLOT + 0.01)), y + 0.11, 3.52), OAK,
                grain=(1, 0, 0), bevel=0.02, jitter=0.008, seed=30 + i)
            cx = s * (SLOT + 0.135)
            box(f'king_{s}_{i}', (cx - 0.125, y - 0.11, 3.52), (cx + 0.125, y + 0.11, RIDGE_LO), OAK, grain=(0, 0, 1), bevel=0.02)
            # rafter: its top under the roof plane, from the ridge timber to past the plate
            n = V((s * (ROOF_TOP_Z - EAVE_Z), 0, EAVE_X - RIDGE_X1)).normalized()
            a = V((s * RIDGE_X1, y, ROOF_TOP_Z)) - n * 0.13; b = V((s * 3.85, y, roof_z(3.85))) - n * 0.13
            beam(f'rafter_{s}_{i}', a, b, 0.17, 0.22, OAK, up=n, bevel=0.015)
            # knee brace post -> tie beam, across
            beam(f'knee_{s}_{i}', (sx(3.33), y, 2.55), (sx(2.6), y, 3.3), 0.16, 0.14, OAK, up=(0, 1, 0), bevel=0.012)
            # along the length: post -> plate and short post -> ridge timber, each way unless at the end
            for d in (1, -1):
                if (i == 0 and d < 0) or (i == len(FRAMES) - 1 and d > 0): continue
                beam(f'brace_{s}_{i}_{d}', (sx(POST_X), y + d * 0.14, 2.65), (sx(POST_X), y + d * 0.95, PLATE_LO + 0.02), 0.14, 0.14, OAK,
                     up=(1, 0, 0), bevel=0.012)
                beam(f'rbrace_{s}_{i}_{d}', (cx, y + d * 0.1, 4.95), (cx, y + d * 0.85, RIDGE_LO + 0.02), 0.13, 0.13, OAK,
                     up=(1, 0, 0), bevel=0.012)
            # bolt heads where the tie beam meets the post (decals)
            for z in (3.33, 3.45):
                box(f'nail_tie_{s}_{i}_{z}', (sx(POST_X) - 0.025, y - 0.135, z - 0.025), (sx(POST_X) + 0.025, y - 0.11, z + 0.025), IRON)
        # the wall plate and the ridge timber, the whole length
        box(f'plate_{s}', (min(sx(3.45), sx(3.8)), -HALF_L, PLATE_LO), (max(sx(3.45), sx(3.8)), HALF_L, PLATE_HI), OAK, grain=(0, 1, 0), bevel=0.025)
        box(f'ridge_{s}', (min(sx(RIDGE_X0), sx(RIDGE_X1)), -HALF_L, RIDGE_LO), (max(sx(RIDGE_X0), sx(RIDGE_X1)), HALF_L, RIDGE_HI), OAK,
            grain=(0, 1, 0), bevel=0.025)
        # under the water: a diagonal brace in each bay, on the posts' outer faces
        for i in range(len(FRAMES) - 1):
            y0, y1 = FRAMES[i] + 0.15, FRAMES[i + 1] - 0.15
            a, b = (y0, -3.6), (y1, -0.75)
            if i % 2: a, b = (y0, -0.75), (y1, -3.6)
            beam(f'xbrace_{s}_{i}', (sx(3.82), a[0], a[1]), (sx(3.82), b[0], b[1]), 0.18, 0.13, OAK, up=(s, 0, 0), bevel=0.012, jitter=0.01, seed=50 + i)

def walls():
    # the side walls: horizontal boards lapped clinker-fashion (each over the one below) on the posts' inner faces, -0.5 .. the plate
    n = 13; h = (PLATE_LO + 0.05 - WALL_LO) / n
    for s in (1, -1):
        for k in range(n):
            zt = PLATE_LO + 0.05 - k * h; zb = zt - h - 0.05
            top, bot = V((s * (WALL_IN + 0.012), 0, zt)), V((s * (WALL_IN + 0.05), 0, zb))
            mid = (top + bot) / 2; across = (bot - top).normalized(); up = across.cross(V((0, s, 0)))
            if up.x * s < 0: up = -up
            beam(f'board_{s}_{k}', mid + V((0, -HALF_L + 0.08, 0)), mid + V((0, HALF_L, 0)), (top - bot).length, 0.03, WOOD, up=up,
                 jitter=0.01, seed=k + 20 * (s > 0))
        # the mooring rail on the inside of the posts, and on it a horn cleat (bollard) by three posts
        box(f'rail_{s}', (min(s * 3.22, s * 3.37), -HALF_L + 0.3, 0.42), (max(s * 3.22, s * 3.37), HALF_L, 0.72), OAK, grain=(0, 1, 0), bevel=0.02)
        for j, y in enumerate((FRAMES[1], FRAMES[3], FRAMES[5])):
            yb = y + 0.6
            box(f'bollard_{s}_{j}', (min(s * 2.98, s * 3.22), yb - 0.13, 0.3), (max(s * 2.98, s * 3.22), yb + 0.13, 0.95), OAK, grain=(0, 0, 1), bevel=0.03)
            tube(f'horn_{s}_{j}', [V((s * 3.07, yb - 0.38, 0.8)), V((s * 3.07, yb, 0.84)), V((s * 3.07, yb + 0.38, 0.8))], 0.055, OAK, square=True)
            for z in (0.5, 0.62):
                box(f'nail_boll_{s}_{j}_{z}', (min(s * 2.97, s * 2.98), yb - 0.025, z - 0.025), (max(s * 2.97, s * 2.98), yb + 0.025, z + 0.025), IRON)

def gable():
    # the land end: upright boards, board-on-board (an outer board over each gap), from y -0.5 up under the roof; across the slot they
    # stand to the ridge timbers' tops; two girts inside, between the corner posts
    def top(x):
        x = abs(x)
        return RIDGE_HI if x <= RIDGE_X1 else roof_z(x) - 0.02
    y = -HALF_L
    n = 16; x0 = -3.75; pitch = 7.5 / n
    for k in range(n):
        a, b = x0 + k * pitch + 0.03, x0 + (k + 1) * pitch - 0.03
        slab(f'gboard_{k}', a, b, y + 0.04, y + 0.08, WALL_LO, top(a), top(b), WOOD)
    for k in range(n - 1):
        c = x0 + (k + 1) * pitch; a, b = c - 0.09, c + 0.09
        slab(f'gcover_{k}', a, b, y, y + 0.04, WALL_LO, top(a) - 0.01, top(b) - 0.01, WOOD)
    for z in (0.2, 1.9):
        box(f'girt_{z}', (-3.45, y + 0.08, z), (3.45, y + 0.22, z + 0.2), OAK, grain=(1, 0, 0), bevel=0.015)

def roof():
    # each half: tarred planks running the whole length, lapped clinker-fashion down the slope (each plank's lower edge over the next),
    # from the ridge timber's outer face to the eave; a barge board standing on the plank ends at each gable
    n = 11; w = SLOPE_L / n
    for s in (1, -1):
        d = V((s * (EAVE_X - RIDGE_X1), 0, EAVE_Z - ROOF_TOP_Z)).normalized()     # down the slope
        nrm = V((s * (ROOF_TOP_Z - EAVE_Z), 0, EAVE_X - RIDGE_X1)).normalized()   # out of the roof
        R = V((s * RIDGE_X1, 0, ROOF_TOP_Z))
        for k in range(n):
            s0 = max(0.0, k * w - 0.02); s1 = (k + 1) * w + 0.06 if k < n - 1 else SLOPE_L - 0.07
            top = R + d * s0 + nrm * 0.02; bot = R + d * s1 + nrm * 0.055
            mid = (top + bot) / 2; across = (bot - top).normalized(); up = V((0, 1, 0)).cross(across) * s
            if up.z < 0: up = -up
            beam(f'plank_{s}_{k}', mid + V((0, -HALF_L, 0)), mid + V((0, HALF_L, 0)), (bot - top).length, 0.035, TAR, up=up,
                 jitter=0.004, seed=k + 40 * (s > 0))
        for e in (1, -1):
            a = R + V((0, e * (HALF_L - 0.025), 0)) + nrm * 0.13 + d * 0.0
            b = R + d * (SLOPE_L - 0.2) + V((0, e * (HALF_L - 0.025), 0)) + nrm * 0.13
            beam(f'barge_{s}_{e}', a, b, 0.05, 0.24, OAK, up=nrm, bevel=0.01)

def naust():
    fresh(); frames(); walls(); gable(); roof()
    finish('naust')

# ---------------- export (from yard.py) ----------------
def finish(base):
    export(base)
    if 'render' in sys.argv and __name__ != '__bake__': preview(base)

def export(base):
    deps = bpy.context.evaluated_depsgraph_get()
    verts, norms, uvs, tris, index = [], [], [], [[] for _ in mats], {}
    for ob in list(bpy.context.scene.collection.objects):
        if ob.type != 'MESH': continue
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(80 if ob.name.startswith(SMOOTH) else 35))
        me.update()
        grain = V(ob['grain']).normalized() if 'grain' in ob else None
        cn = me.corner_normals
        for p in me.polygons:
            slot = mats.index(me.materials[p.material_index]) if me.materials else WOOD
            corner = []
            fn = p.normal
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                n = cn[li].vector
                if grain is not None and abs(fn.dot(grain)) < 0.7:
                    across = fn.cross(grain).normalized()
                    u, v = co.dot(across), co.dot(grain)
                else: u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + base + '.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    xs, ys, zs = verts[0::3], verts[1::3], verts[2::3]
    print(base, 'verts', len(verts) // 3, 'tris', sum(len(t) for t in tris) // 3, 'x', round(min(xs), 3), round(max(xs), 3), 'y', round(min(ys), 3), round(max(ys), 3), 'z', round(min(zs), 3), round(max(zs), 3))

# ---------------- previews ----------------
COLS = [(0.2, 0.19, 0.17, 1), (0.12, 0.11, 0.095, 1), (0.06, 0.05, 0.045, 1), (0.1, 0.1, 0.11, 1)]
# (name, camera, target, with water and shore) in the Blender frame (+y the sea end)
VIEWS = [('sea34', (17, 26, 9), (0, 0, 1.5), True), ('side', (34, -2, 3), (0, 0, 1.0), True),
         ('inside', (2.2, -6.5, 1.0), (0, 4, 6.2), True), ('bare34', (-18, 24, 6), (0, 0, 0.5), False),
         ('land34', (-14, -24, 7), (0, 0, 1.5), True)]
def preview(base):
    for m, c in zip(mats, COLS):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = c; b.inputs['Roughness'].default_value = 0.85
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH':
            for p in ob.data.polygons: p.use_smooth = True
            ob.data.set_sharp_from_angle(angle=math.radians(80 if ob.name.startswith(SMOOTH) else 35))
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 32; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 960, 600
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    env = []
    # the sea from y -2 outward at z 0, the shore rising gently from there to the land end (and beyond)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 25, -0.01)); sea = bpy.context.active_object; sea.scale = (80, 54, 1)
    sm = bpy.data.materials.new('sea'); sm.use_nodes = True; sb = sm.node_tree.nodes['Principled BSDF']
    sb.inputs['Base Color'].default_value = (0.08, 0.16, 0.2, 1); sb.inputs['Roughness'].default_value = 0.15; sea.data.materials.append(sm)
    bm = bmesh.new()
    pts = [bm.verts.new(p) for p in ((-40, -2, -0.3), (40, -2, -0.3), (40, -40, 1.6), (-40, -40, 1.6))]
    bm.faces.new(pts); me = bpy.data.meshes.new('shore'); bm.to_mesh(me); bm.free()
    shore = bpy.data.objects.new('shore', me); sc.collection.objects.link(shore)
    gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.33, 0.22, 1); shore.data.materials.append(gm)
    env = [sea, shore]
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.0; sun.rotation_euler = (math.radians(35), 0, math.radians(140)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    for name, loc, look, world in VIEWS:
        for o in env: o.hide_render = not world
        cam.data.lens = 18 if name == 'inside' else 35
        cam.location = loc
        cam.rotation_euler = (V(look) - V(loc)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'{base}_{name}.png'; bpy.ops.render.render(write_still=True)

KINDS = ('naust',)
which = [a for a in sys.argv if a in KINDS] or list(KINDS)
for k in which: globals()[k]()
