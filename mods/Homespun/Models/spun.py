# Homespun's looks (Homespun 0.1.0), made from scratch: the retting pool, the flax brake (with its hackle and basket), the warp-weighted
# loom, the sewing table, and the items: retted flax, line flax, combed wool, yarn, a linen bolt, a wool bolt, a woven band, the drop spindle,
# and the five garments folded as they lie on the ground (undertunic, tunic, dress, trousers, cloak).
# Run: blender -b --python ~/.cache/homespun/spun.py -- [render] [pool|brake|...|spindle|undertunic|tunic|dress|trousers|cloak]
# (writes ~/.cache/homespun/<name>.json.gz; previews with "render"). What ships is the bake of these (bake_conf.py, docs/model-baking.md).
# Blender frame: z up, front +y (the game's +z, where a player stands to use the piece); the pieces stand on z 0, the items lie about the
# origin, the spindle's shaft along z with the hand at the origin. Written in the game's frame (x, z, y): per corner position/normal/uv
# (uvs in metres; wood's grain runs along v), triangles per material: 0 wood, 1 stone, 2 water, 3 flax, 4 linen, 5 wool, 6 iron, 7 red,
# 8 blue, 9 retted, 10 oat (undyed light wool), 11 darkwool, 12 cloakwool, 13 bronze.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/homespun/'
WOOD, STONE, WATER, FLAX, LINEN, WOOL, IRON, RED, BLUE, RETTED, OAT, DARKWOOL, CLOAKWOOL, BRONZE = range(14)
NAMES = ('wood', 'stone', 'water', 'flax', 'linen', 'wool', 'iron', 'red', 'blue', 'retted', 'oat', 'darkwool', 'cloakwool', 'bronze')
SMOOTH = ('rock', 'weight', 'sheaf', 'hank', 'wool', 'ball', 'cloth', 'roll', 'cop', 'whorl', 'heap', 'tie', 'basket', 'spool', 'drape', 'hood', 'brooch', 'bead', 'stalk', 'ply', 'knot', 'tuft')
rng = random.Random(7)
mats = []
V = Vector

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

def tube(name, pts, radius, mat, cyclic=False, res=2):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = res; cu.use_fill_caps = True
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1); sp.use_cyclic_u = cyclic
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o['grain'] = tuple(pts[-1] - pts[0])
    return o

def smooth_line(pts, rounds=2):
    # a polyline's corners cut off (Chaikin), its ends kept
    for _ in range(rounds):
        out = [pts[0]]
        for a, b in zip(pts, pts[1:]): out += [a.lerp(b, 0.25), a.lerp(b, 0.75)]
        pts = out + [pts[-1]]
    return pts

def pole(name, a, b, radius, mat=WOOD, seed=0, bend=0.012):
    # a round pole, a little crooked (its middle pushed aside)
    r = random.Random(seed); a, b = V(a), V(b)
    mid = [a.lerp(b, t) + V((r.uniform(-bend, bend), r.uniform(-bend, bend), r.uniform(-bend, bend) * 0.3)) for t in (0.33, 0.66)]
    return tube(name, [a] + mid + [b], radius, mat)

def blob(name, centre, size, mat, seed=0, subdiv=2, lumps=0.12):
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    r = random.Random(seed)
    for v in bm.verts:
        v.co = V((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) * (1 + r.uniform(-lumps, lumps)) + V(centre)
    return link(name, bm, mat)

def rock(name, centre, radius, seed, mat=STONE, segs=(10, 7)):
    # a well-rounded stream stone: an egg, flattened, swelling and dipping gently, turned its own way
    r = random.Random(seed)
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=segs[0], v_segments=segs[1], radius=1.0)
    sx, sy, sz = r.uniform(1.0, 1.4), r.uniform(0.75, 1.0), r.uniform(0.55, 0.8)
    egg = r.uniform(-0.18, 0.18)
    waves = [(V((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1))).normalized() * r.uniform(1.5, 2.8), r.uniform(0, 6.3), r.uniform(0.05, 0.1)) for _ in range(3)]
    turn = Matrix.Rotation(r.uniform(0, 2 * math.pi), 4, 'Z') @ Matrix.Rotation(r.uniform(-0.3, 0.3), 4, 'X')
    for v in bm.verts:
        d = v.co.normalized()
        k = (1 + sum(a * math.sin(d.dot(w) + p) for w, p, a in waves)) * (1 + egg * d.x)
        v.co = turn @ (V((d.x * sx, d.y * sy, d.z * sz)) * k * radius) + V(centre)
    return link(name, bm, mat)

def box(name, lo, hi, mat, grain=None, bevel=0.0, jitter=0.0, seed=0):
    # a box from corner lo to corner hi, its edges chamfered, its corners pushed about a little (a hewn plank)
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    lo, hi = V(lo), V(hi)
    bmesh.ops.scale(bm, vec=hi - lo, verts=bm.verts); bmesh.ops.translate(bm, vec=(lo + hi) / 2, verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES', profile=0.5)
    if jitter:
        r = random.Random(seed)
        for v in bm.verts: v.co += V((r.uniform(-jitter, jitter), r.uniform(-jitter, jitter), r.uniform(-jitter, jitter)))
    return link(name, bm, mat, grain)

def turned(ob, m, about=(0, 0, 0)):
    # a part moved: turned by the matrix m about a point (its grain turned with it)
    about = V(about)
    ob.data.transform(Matrix.Translation(about) @ m.to_4x4() @ Matrix.Translation(-about))
    if 'grain' in ob: ob['grain'] = tuple(m.to_3x3() @ V(ob['grain']))
    return ob

def lathe(name, a, b, profile, mat, segs=14, flute=0.0, lobes=5, twist=0.0, seed=0, grain=True, step=0.04):
    # a solid of turning from a to b: profile(t) is its radius t of the way along; flute ripples it round (a bundle of stalks, a cop of
    # yarn), twist turns the ripples along it. Capped at both ends
    a, b = V(a), V(b); axis = (b - a); L = axis.length; axis.normalize()
    q = V((0, 0, 1)).rotation_difference(axis).to_matrix()
    r = random.Random(seed); ph = r.uniform(0, 6.3)
    n = max(4, int(L / step) + 2)
    bm = bmesh.new(); rings = []
    for i in range(n + 1):
        t = i / n; rad = profile(t)
        ring = []
        for k in range(segs):
            th = 2 * math.pi * k / segs
            rr = rad * (1 + flute * math.sin(lobes * th + ph + twist * t) + r.uniform(-flute, flute) * 0.3)
            ring.append(bm.verts.new(a + q @ V((math.cos(th) * rr, math.sin(th) * rr, t * L))))
        rings.append(ring)
    for i in range(n):
        for k in range(segs):
            j = (k + 1) % segs
            bm.faces.new((rings[i][k], rings[i][j], rings[i + 1][j], rings[i + 1][k]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, mat, tuple(axis) if grain else None)

def ring(name, centre, R, r, axis, mat, segs=16, rsegs=6, squash=1.0):
    # a torus round axis (a loom weight, a tie round a bundle); squash flattens its section along the axis
    axis = V(axis).normalized(); q = V((0, 0, 1)).rotation_difference(axis).to_matrix()
    bm = bmesh.new(); grid = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        row = []
        for k in range(rsegs):
            b = 2 * math.pi * k / rsegs
            p = V(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b) * squash))
            row.append(bm.verts.new(V(centre) + q @ p))
        grid.append(row)
    for i in range(segs):
        for k in range(rsegs):
            bm.faces.new((grid[i][k], grid[(i + 1) % segs][k], grid[(i + 1) % segs][(k + 1) % rsegs], grid[i][(k + 1) % rsegs]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, mat)

def sheaf(name, a, b, radius, mat, seed=0, ties=(0.5,), splay=0.35, twist=0.0, tie_mat=None, segs=10):
    # a bundle of stalks or fibre (a sheaf of flax, a strick of line flax): rippled round with its stalks, splayed at the ends, tied
    prof = lambda t: radius * (1 + splay * (max(0, 0.18 - t) / 0.18) ** 1.5 + splay * (max(0, t - 0.82) / 0.18) ** 1.5) * (0.92 if any(abs(t - x) < 0.04 for x in ties) else 1)
    ob = lathe(name, a, b, prof, mat, segs=segs, flute=0.12, lobes=7, twist=twist, seed=seed, step=max(0.04, (V(b) - V(a)).length / 9))
    a, b = V(a), V(b)
    for i, t in enumerate(ties):
        ring(f'tie_{name}_{i}', a.lerp(b, t), radius * 0.93, 0.008, b - a, FLAX if tie_mat is None else tie_mat, segs=12, rsegs=5)
    return ob

def ribbon(name, pts, w0, w1, mat, thick=0.004, normal=(0, 1, 0)):
    # a flat strip through pts (a group of warp threads, a band's loose end), w0 wide at its first point and w1 at its last; it faces normal
    normal = V(normal).normalized(); bm = bmesh.new(); rows = []
    for i, p in enumerate(pts):
        t = i / (len(pts) - 1); w = w0 + (w1 - w0) * t
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = d.cross(normal).normalized() * (w / 2); off = normal * (thick / 2)
        rows.append([bm.verts.new(p - side - off), bm.verts.new(p + side - off), bm.verts.new(p + side + off), bm.verts.new(p - side + off)])
    for i in range(len(rows) - 1):
        for k in range(4):
            bm.faces.new((rows[i][k], rows[i][(k + 1) % 4], rows[i + 1][(k + 1) % 4], rows[i + 1][k]))
    bm.faces.new(list(reversed(rows[0]))); bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, mat, tuple(pts[-1] - pts[0]))

def strands(name, pts, radius, mat, n=4, spread=0.02, seed=0):
    # a hank of fibre hung or laid over something: n strands side by side along pts, wandering a little apart, their ends ragged
    r = random.Random(seed); pts = smooth_line(pts, 1)
    d = (pts[-1] - pts[0]).normalized(); side = d.cross(V((0, 0, 1)))
    side = side.normalized() if side.length > 1e-3 else V((1, 0, 0))
    out = []
    for k in range(n):
        off = (k - (n - 1) / 2) / max(1, n - 1) * spread * 2
        ph = r.uniform(0, 6.3)
        p = [q + side * (off + 0.006 * math.sin(i * 0.9 + ph)) + V((0, 0, r.uniform(-0.003, 0.003))) for i, q in enumerate(pts)]
        cut0, cut1 = r.randint(0, 1), len(p) - r.randint(0, 1)
        out.append(tube(f'{name}_{k}', p[cut0:max(cut1, cut0 + 2)], radius * r.uniform(0.85, 1.15), mat, res=1))
    return out

def drape(name, x0, x1, path, mat, folds=2, amp=0.008, thick=0.006, nx=8):
    # a cloth laid over something: a sheet swept along x through the side-on outline path (y, z), rippled in folds where it hangs
    bm = bmesh.new(); grid = []
    r = random.Random(len(name)); ph = r.uniform(0, 6.3)
    zt = max(p[1] for p in path)
    for i in range(nx + 1):
        x = x0 + (x1 - x0) * i / nx
        col = []
        for y, z in path:
            t = min(1.0, (zt - z) / 0.2)
            col.append(bm.verts.new((x, y + amp * t * math.sin(2 * math.pi * folds * i / nx + ph), z)))
        grid.append(col)
    for i in range(nx):
        for j in range(len(path) - 1):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    bm.normal_update()
    ob = link(name, bm, mat)
    m = ob.modifiers.new('solid', 'SOLIDIFY'); m.thickness = thick; m.offset = 0
    return ob

def cyl(name, a, b, r0, r1, mat, segs=12):
    return lathe(name, a, b, lambda t: r0 + (r1 - r0) * t, mat, segs=segs)

# ---------------- the retting pool ----------------
# A plank trough the flax lies soaking in, weighted under with boards and stones (interior x -1.17..0.67, y -0.42..0.42, its water at
# z 0.40, its walls 0.52 high), and beside it at x 0.8..1.3 an A-frame rack hung with retted flax drying.
def pool():
    fresh()
    box('floor', (-1.22, -0.47, 0), (0.72, 0.47, 0.04), WOOD, (1, 0, 0), bevel=0.006)
    for side, (y0, y1) in enumerate(((0.42, 0.47), (-0.47, -0.42))):
        for k, (z0, z1) in enumerate(((0.04, 0.28), (0.284, 0.52))):
            box(f'plank_{side}{k}', (-1.22, y0, z0), (0.72, y1, z1), WOOD, (1, 0, 0), bevel=0.008, jitter=0.004, seed=side * 3 + k)
    for side, (x0, x1) in enumerate(((-1.22, -1.17), (0.67, 0.72))):
        for k, (z0, z1) in enumerate(((0.04, 0.28), (0.284, 0.52))):
            box(f'end_{side}{k}', (x0, -0.42, z0), (x1, 0.42, z1), WOOD, (0, 1, 0), bevel=0.008, jitter=0.004, seed=10 + side * 3 + k)
    for i, (x, y) in enumerate(((-1.25, -0.5), (-1.25, 0.5), (0.75, -0.5), (0.75, 0.5))):
        box(f'post_{i}', (x - 0.04, y - 0.04, 0), (x + 0.04, y + 0.04, 0.58), WOOD, (0, 0, 1), bevel=0.01, jitter=0.004, seed=20 + i)
    # (the water: one face looking up)
    bm = bmesh.new(); f = bm.faces.new([bm.verts.new(p) for p in ((-1.17, -0.42, 0.40), (0.67, -0.42, 0.40), (0.67, 0.42, 0.40), (-1.17, 0.42, 0.40))])
    f.normal_update()
    if f.normal.z < 0: f.normal_flip()
    link('water', bm, WATER)
    for i, y in enumerate((-0.33, -0.22, -0.11, 0.0, 0.11, 0.22, 0.33)):
        x0 = -1.1 + rng.uniform(-0.04, 0.04)
        sheaf(f'sheaf_{i}', (x0, y, 0.375), (x0 + 1.6 + rng.uniform(-0.06, 0.0), y + rng.uniform(-0.02, 0.02), 0.375 + rng.uniform(-0.01, 0.01)), 0.044, FLAX, seed=30 + i, ties=(), splay=0.5, segs=8)
    # (boards across the bundles, a stone or two on each, holding them under)
    for i, x in enumerate((-0.68, 0.18)):
        box(f'weightboard_{i}', (x - 0.06, -0.4, 0.44), (x + 0.06, 0.4, 0.47), WOOD, (0, 1, 0), bevel=0.006, jitter=0.004, seed=40 + i)
        for k, y in enumerate((-0.18, 0.14)):
            rock(f'rock_{i}{k}', (x + rng.uniform(-0.02, 0.02), y, 0.52), 0.075, seed=50 + i * 2 + k)
    # the drying rack
    for i, y in enumerate((-0.33, 0.33)):
        pole(f'leg_{i}a', (0.82, y, 0), (1.06, y, 1.22), 0.03, seed=60 + i)
        pole(f'leg_{i}b', (1.28, y, 0), (1.04, y, 1.22), 0.03, seed=62 + i)
    pole('rackbar', (1.05, -0.42, 1.17), (1.05, 0.42, 1.17), 0.03, seed=64, bend=0.006)
    for i, x in enumerate((0.93, 1.17)):
        pole(f'rung_{i}', (x, -0.38, 0.6), (x, 0.38, 0.6), 0.022, seed=66 + i, bend=0.005)
    for i, y in enumerate((-0.24, -0.09, 0.07, 0.22)):
        y += rng.uniform(-0.015, 0.015); drop = rng.uniform(0.0, 0.08)
        strands(f'hank_{i}', [V((0.95, y, 0.72 + drop)), V((0.99, y, 1.08)), V((1.05, y, 1.215)), V((1.11, y, 1.08)), V((1.15, y + 0.01, 0.74 + drop))], 0.016, RETTED, n=4, spread=0.028, seed=70 + i)
    # (a bundle laid over the lower rung, drying too)
    strands('hank_low', [V((0.86, -0.1, 0.42)), V((0.9, -0.1, 0.58)), V((0.93, -0.1, 0.63)), V((0.97, -0.1, 0.58)), V((1.0, -0.1, 0.45))], 0.015, RETTED, n=4, spread=0.025, seed=79)
    finish('pool', [(30, 3.6, 2.2, 0.45), (-140, 3.6, 2.4, 0.45), (90, 3.2, 1.6, 0.4)])

# ---------------- the flax brake ----------------
# A trestle (x -1.0..0.4, its bed's top at z 0.84) with two fixed blades on edge and a hinged third that comes down between them,
# its handle at the free end; a hank of flax laid across. At x 0.6..0.9 a hackle: a block of iron teeth on a stump, for combing the
# broken flax (and wool from a lox pelt). A basket at (0.3, 0.45) the line flax and wool come out into.
def brake():
    fresh()
    for i, x in enumerate((-0.92, 0.32)):
        for k, s in enumerate((-1, 1)):
            pole(f'leg_{i}{k}', (x + 0.03 * s, 0.24 * s, 0), (x, 0.07 * s, 0.6), 0.035, seed=i * 2 + k)
    box('bed', (-1.0, -0.09, 0.56), (0.4, 0.09, 0.66), WOOD, (1, 0, 0), bevel=0.01, jitter=0.004, seed=5)
    for k, (y0, y1) in enumerate(((-0.065, -0.04), (0.04, 0.065))):
        box(f'blade_{k}', (-1.0, y0, 0.66), (0.4, y1, 0.82), WOOD, (1, 0, 0), bevel=0.006, jitter=0.003, seed=6 + k)
    for k, (y0, y1) in enumerate(((-0.09, -0.065), (0.065, 0.09))):
        box(f'cheek_{k}', (-1.0, y0, 0.66), (-0.9, y1, 0.9), WOOD, (0, 0, 1), bevel=0.006, seed=8 + k)
    hinge = V((-0.95, 0, 0.84))
    tube('pin', [V((-0.95, -0.105, 0.84)), V((-0.95, 0.105, 0.84))], 0.012, IRON, res=1)
    # (the hinged blade, raised a little: its lower edge sits between the fixed blades when it's down)
    up = Matrix.Rotation(math.radians(-11), 3, 'Y')
    jaw = [box('jaw', (-0.97, -0.015, 0.70), (0.42, 0.015, 0.86), WOOD, (1, 0, 0), bevel=0.006, jitter=0.003, seed=12),
           box('jawtop', (-0.9, -0.04, 0.86), (0.4, 0.04, 0.9), WOOD, (1, 0, 0), bevel=0.008, seed=13),
           tube('handle', smooth_line([V((0.38, 0, 0.88)), V((0.5, 0, 0.9)), V((0.66, 0, 0.97))], 1), 0.026, WOOD)]
    for o in jaw: turned(o, up, hinge)
    # (a hank of flax half broken, lying across the blades and hanging down both sides)
    strands('hank', [V((0.02, -0.3, 0.6)), V((0.0, -0.12, 0.8)), V((0.0, 0.0, 0.835)), V((-0.01, 0.12, 0.8)), V((0.0, 0.32, 0.62))], 0.014, FLAX, n=5, spread=0.04, seed=17)
    # the hackle
    lathe('stump', (0.75, 0, 0), (0.75, 0, 0.7), lambda t: 0.065 + 0.08 * max(0, 0.25 - t) / 0.25, WOOD, segs=10, flute=0.04, lobes=5)
    box('hackle', (0.61, -0.09, 0.7), (0.89, 0.09, 0.8), WOOD, (1, 0, 0), bevel=0.01, seed=14)
    for i in range(7):
        for k in range(3):
            x, y = 0.65 + 0.034 * i, -0.05 + 0.05 * k + (0.017 if i % 2 else 0)
            cyl(f'tooth_{i}{k}', (x, y, 0.79), (x, y, 0.9), 0.0055, 0.0015, IRON, segs=5)
    # the basket, and what's come out into it
    lathe('basket', (0.3, 0.45, 0), (0.3, 0.45, 0.2), lambda t: 0.14 + 0.04 * t, WOOD, segs=16, flute=0.02, lobes=16)
    for i, z in enumerate((0.05, 0.1, 0.15)):
        ring(f'weave_{i}', (0.3, 0.45, z), 0.14 + 0.04 * z / 0.2, 0.005, (0, 0, 1), WOOD, segs=16, rsegs=4)
    blob('heap_flax', (0.27, 0.45, 0.205), (0.13, 0.11, 0.05), FLAX, seed=15, lumps=0.15)
    blob('heap_wool', (0.36, 0.48, 0.215), (0.07, 0.07, 0.05), WOOL, seed=16, lumps=0.2)
    finish('brake', [(30, 3.0, 1.8, 0.5), (-130, 3.0, 2.0, 0.5), (80, 2.4, 1.3, 0.5)])

# ---------------- the loom ----------------
# A warp-weighted loom: two uprights at x +-0.8, 2.0 tall, and all of it leaned back 12 degrees (about x, the top towards -y). The cloth
# beam across the top with woven cloth wound on it and down to the fell at about 1.3; below, the warp hangs in groups, each to a ring
# loom weight (two rows, about 0.4 up): the front threads straight, the back ones over the shed rod behind; the heddle rod in front on
# its brackets, its loops round the back threads. Two struts behind hold it up.
def loom():
    fresh()
    parts = []
    for i, s in enumerate((-1, 1)):
        parts.append(pole(f'upright_{i}', (0.8 * s, 0, 0), (0.8 * s, 0, 2.0), 0.055, seed=i, bend=0.01))
        parts.append(box(f'foot_{i}', (0.8 * s - 0.08, -0.12, 0), (0.8 * s + 0.08, 0.12, 0.08), WOOD, (0, 1, 0), bevel=0.012, jitter=0.005, seed=2 + i))
        parts.append(pole(f'bracket_{i}', (0.8 * s, -0.02, 1.02), (0.8 * s, 0.25, 1.03), 0.02, seed=4 + i, bend=0.003))
        parts.append(pole(f'bracketup_{i}', (0.8 * s, 0.25, 1.02), (0.8 * s, 0.26, 1.08), 0.016, seed=6 + i, bend=0.002))
        parts.append(pole(f'shedpeg_{i}', (0.8 * s, 0.02, 0.83), (0.8 * s, -0.16, 0.83), 0.018, seed=8 + i, bend=0.002))
    parts.append(pole('beam', (-0.96, 0, 1.86), (0.96, 0, 1.86), 0.05, seed=10, bend=0.004))
    parts.append(pole('beampeg', (-0.92, -0.12, 1.86), (-0.92, 0.12, 1.86), 0.014, seed=11, bend=0.0))
    parts.append(cyl('roll', (-0.66, 0, 1.86), (0.66, 0, 1.86), 0.095, 0.095, WOOL, segs=16))
    fell, fy = 1.30, 0.09
    parts.append(box('cloth', (-0.64, 0.083, fell), (0.64, 0.097, 1.86), WOOL, (0, 0, 1)))
    parts.append(box('stripe_red', (-0.64, 0.096, 1.40), (0.64, 0.1, 1.46), RED))
    parts.append(box('stripe_blue', (-0.64, 0.096, 1.49), (0.64, 0.1, 1.51), BLUE))
    parts.append(pole('shedrod', (-0.92, -0.12, 0.85), (0.92, -0.12, 0.85), 0.024, seed=12, bend=0.003))
    parts.append(pole('heddlerod', (-0.92, 0.25, 1.1), (0.92, 0.25, 1.1), 0.022, seed=13, bend=0.003))
    n = 9
    for i in range(n):
        x = -0.57 + 1.14 * i / (n - 1)
        wz = 0.44 + rng.uniform(-0.02, 0.02)
        parts.append(ribbon(f'warp_f{i}', [V((x, fy, fell)), V((x, 0.075, wz + 0.05))], 0.135, 0.018, WOOL))
        parts.append(ring(f'weight_f{i}', (x + rng.uniform(-0.005, 0.005), 0.075, wz), 0.045, 0.022, (0, 1, 0), STONE, segs=10, rsegs=5, squash=1.3))
    for i in range(n - 1):
        x = -0.5 + 1.0 * i / (n - 2)
        wz = 0.38 + rng.uniform(-0.02, 0.02)
        parts.append(ribbon(f'warp_b{i}', [V((x, fy - 0.006, fell)), V((x, -0.145, 0.85)), V((x, -0.15, wz + 0.05))], 0.13, 0.018, WOOL))
        parts.append(ring(f'weight_b{i}', (x + rng.uniform(-0.005, 0.005), -0.15, wz), 0.045, 0.022, (0, 1, 0), STONE, segs=10, rsegs=5, squash=1.3))
        # (a heddle loop from the rod round these back threads, where they pass under it)
        t = (fell - 1.08) / (fell - 0.85); by = fy + (-0.145 - fy) * t
        parts.append(tube(f'heddle_{i}', [V((x, 0.25, 1.09)), V((x, by, 1.08))], 0.0035, LINEN, res=1))
    lean = Matrix.Rotation(math.radians(12), 3, 'X')
    for o in parts: turned(o, lean)
    for i, s in enumerate((-1, 1)):
        top = lean @ V((0.8 * s, 0, 1.6))
        pole(f'strut_{i}', (0.86 * s, -0.95, 0), (top.x + 0.05 * s, top.y - 0.02, top.z), 0.035, seed=20 + i, bend=0.006)
    # (the weaving sword, leaning on the right upright)
    sword = box('sword', (-0.02, -0.006, 0), (0.02, 0.006, 0.62), WOOD, (0, 0, 1), bevel=0.003)
    turned(sword, Matrix.Rotation(math.radians(27), 3, 'X') @ Matrix.Rotation(math.radians(-4), 3, 'Y'))
    sword.data.transform(Matrix.Translation((0.89, 0.2, 0.0)))
    finish('loom', [(20, 4.0, 1.6, 1.0), (-150, 4.0, 1.8, 1.0), (75, 3.5, 1.4, 1.0), (0, 1.8, 0.9, 0.6)])

# ---------------- the sewing table ----------------
# A trestle table (1.4 x 0.7, its top at 0.8): a stack of folded cloth, a linen bolt half unrolled over the front edge with the
# shears on it, a spool of thread, a ball of yarn, a roll of woven band and a needle case.
def table():
    fresh()
    for i, (y0, y1) in enumerate(((-0.35, -0.12), (-0.115, 0.115), (0.12, 0.35))):
        box(f'top_{i}', (-0.7, y0, 0.75), (0.7, y1, 0.8), WOOD, (1, 0, 0), bevel=0.008, jitter=0.003, seed=i)
    for i, x in enumerate((-0.52, 0.52)):
        box(f'bearer_{i}', (x - 0.04, -0.33, 0.69), (x + 0.04, 0.33, 0.75), WOOD, (0, 1, 0), bevel=0.008, seed=4 + i)
        for k, s in enumerate((-1, 1)):
            pole(f'leg_{i}{k}', (x, 0.3 * s, 0), (x, 0.17 * s, 0.7), 0.035, seed=6 + i * 2 + k)
    pole('stretcher', (-0.55, 0, 0.26), (0.55, 0, 0.26), 0.03, seed=10, bend=0.004)
    z = 0.8
    # (folded cloth: wool, linen and a blue-dyed piece on top)
    for i, (w, d, h, m) in enumerate(((0.36, 0.26, 0.05, WOOL), (0.34, 0.24, 0.04, LINEN), (0.3, 0.22, 0.035, BLUE))):
        cx, cy = -0.42 + rng.uniform(-0.015, 0.015), -0.08 + rng.uniform(-0.015, 0.015)
        box(f'cloth_{i}', (cx - w / 2, cy - d / 2, z), (cx + w / 2, cy + d / 2, z + h), m, bevel=0.014, jitter=0.004, seed=12 + i)
        z += h
    z = 0.8
    # (the linen bolt at the back, its cloth run forward and over the front edge)
    cyl('roll', (-0.12, -0.22, z + 0.06), (0.38, -0.22, z + 0.06), 0.06, 0.06, LINEN, segs=14)
    drape('drape', -0.1, 0.36, [(-0.2, z + 0.005), (0.0, z + 0.004), (0.2, z + 0.004), (0.33, z + 0.004), (0.36, z - 0.015), (0.375, z - 0.08), (0.38, z - 0.2)], LINEN, folds=2)
    # (spring shears lying on it: two blades and the bow that joins them)
    sh = [box('blade_a', (0.0, -0.009, 0), (0.15, 0.009, 0.004), IRON, bevel=0.0015), box('blade_b', (0.0, -0.009, 0.004), (0.15, 0.009, 0.008), IRON, bevel=0.0015),
          tube('bow', smooth_line([V((-0.07, -0.006, 0.004)), V((-0.11, -0.012, 0.004)), V((-0.13, 0, 0.004)), V((-0.11, 0.012, 0.004)), V((-0.07, 0.006, 0.004))]), 0.004, IRON, res=1),
          box('grip_a', (-0.07, -0.016, 0.0), (0.0, -0.004, 0.006), IRON, bevel=0.0015), box('grip_b', (-0.07, 0.004, 0.002), (0.0, 0.016, 0.008), IRON, bevel=0.0015)]
    turned(sh[0], Matrix.Rotation(math.radians(5), 3, 'Z')); turned(sh[1], Matrix.Rotation(math.radians(-5), 3, 'Z'))
    for o in sh: turned(o, Matrix.Rotation(math.radians(25), 3, 'Z')); o.data.transform(Matrix.Translation((0.12, 0.06, z + 0.006)))
    # (a spool of linen thread, standing)
    cyl('spool_core', (0.5, 0.1, z), (0.5, 0.1, z + 0.09), 0.015, 0.015, WOOD, segs=8)
    cyl('spool_thread', (0.5, 0.1, z + 0.012), (0.5, 0.1, z + 0.078), 0.032, 0.03, LINEN, segs=12)
    for k, h in enumerate((0, 0.078)): cyl(f'spool_end{k}', (0.5, 0.1, z + h), (0.5, 0.1, z + h + 0.012), 0.042, 0.042, WOOD, segs=12)
    # (a ball of yarn and its loose end)
    blob('ball', (0.56, -0.2, z + 0.05), (0.05, 0.05, 0.048), WOOL, seed=20, subdiv=2, lumps=0.04)
    for k in range(4):
        a = math.pi * k / 4
        ring(f'wrap_ball{k}', (0.56, -0.2, z + 0.05), 0.05, 0.004, (math.cos(a), math.sin(a), 0.4 * (k % 2)), WOOL, segs=14, rsegs=4)
    tube('yarnend', smooth_line([V((0.6, -0.17, z + 0.02)), V((0.63, -0.1, z + 0.004)), V((0.6, -0.02, z + 0.004))], 1), 0.004, WOOL, res=1)
    # (a roll of tablet-woven band, red with a blue stripe, and its end)
    cyl('roll_band', (-0.12, 0.2, z), (-0.12, 0.2, z + 0.035), 0.04, 0.04, RED, segs=14)
    ring('stripe_band', (-0.12, 0.2, z + 0.0175), 0.04, 0.003, (0, 0, 1), BLUE, segs=14, rsegs=4)
    ribbon('bandend', [V((-0.08, 0.205, z + 0.002)), V((0.0, 0.24, z + 0.002)), V((0.06, 0.22, z + 0.002))], 0.035, 0.035, RED, thick=0.003, normal=(0, 0, 1))
    # (a needle case of bone)
    cyl('needlecase', (0.4, 0.27, z + 0.01), (0.5, 0.29, z + 0.01), 0.01, 0.009, LINEN, segs=8)
    finish('table', [(25, 2.6, 1.9, 0.75), (-140, 2.6, 2.0, 0.7), (0, 1.3, 1.5, 0.8)])

# ---------------- the items ----------------
def retted():
    # a sheaf of retted flax: grey stalks bound round the middle with a twist of flax, their cut ends flat at one end, the tops
    # thinning out and splaying at the other
    fresh(); r = random.Random(1)
    lathe('sheaf', (-0.24, 0, 0), (0.21, 0, 0), lambda t: 0.036 * (1 - 0.15 * t), RETTED, segs=10, flute=0.1, lobes=9, step=0.075)
    for k in range(16):
        a = 2 * math.pi * k / 16 + r.uniform(-0.15, 0.15); rr = r.uniform(0.012, 0.03)
        y, z = math.cos(a) * rr, math.sin(a) * rr
        tube(f'stalk_root{k}', [V((-0.24 - r.uniform(0.004, 0.022), y, z)), V((-0.2, y * 0.95, z * 0.95))], 0.0045, RETTED, res=0)
        sp, L = r.uniform(1.0, 1.8), r.uniform(0.06, 0.13)
        tube(f'stalk_top{k}', [V((0.19, y * 0.85, z * 0.85)), V((0.21 + L * 0.5, y * (1 + sp * 0.4), z * (1 + sp * 0.4))), V((0.21 + L, y * (1 + sp), z * (1 + sp)))], 0.003, RETTED, res=0)
    ring('tie_band', (-0.05, 0, 0), 0.035, 0.008, (1, 0, 0), FLAX, segs=14, rsegs=5, squash=1.8)
    tube('tie_end', smooth_line([V((-0.05, 0.03, 0.028)), V((-0.035, 0.05, 0.045)), V((-0.01, 0.055, 0.05))], 1), 0.0045, FLAX, res=0)
    finish('retted', [(30, 1.0, 0.6, 0)])

def line():
    # a strick of line flax: the long golden fibre twisted on itself, tied in a loose knot at one end, the other end loose and fine
    fresh(); r = random.Random(2)
    for ply in range(2):
        pts = []
        for i in range(25):
            t = i / 24; a = 2 * math.pi * 1.6 * t + math.pi * ply; rad = 0.015 * (1 - 0.3 * t)
            pts.append(V((-0.14 + 0.34 * t, math.cos(a) * rad, math.sin(a) * rad)))
        tube(f'ply_{ply}', pts, 0.016, FLAX)
    tube('knot', smooth_line([V((-0.13, 0, 0)), V((-0.17, 0.0, -0.01)), V((-0.21, 0.01, 0.012)), V((-0.22, 0.012, 0.05)), V((-0.18, 0.015, 0.075)),
                              V((-0.14, 0.01, 0.05)), V((-0.155, -0.005, 0.008)), V((-0.2, -0.012, -0.012)), V((-0.25, -0.01, -0.008))]), 0.016, FLAX)
    for k in range(7):
        a = 2 * math.pi * k / 7; o = V((0, math.cos(a), math.sin(a)))
        tube(f'tuft_tail{k}', [V((-0.245, -0.01, -0.008)) + o * 0.006, V((-0.27, -0.01, -0.008)) + o * 0.011, V((-0.3 - r.uniform(0, 0.02), -0.01, -0.008)) + o * r.uniform(0.016, 0.026)], 0.0035, FLAX, res=0)
        tube(f'tuft_end{k}', [V((0.19, 0, 0)) + o * 0.006, V((0.23, 0, 0)) + o * 0.011, V((0.27 + r.uniform(0, 0.03), 0, 0)) + o * r.uniform(0.016, 0.03)], 0.003, FLAX, res=0)
    finish('line', [(30, 1.0, 0.6, 0)])

def wool():
    fresh()
    blob('wool', (0, 0, 0.0), (0.11, 0.075, 0.05), WOOL, seed=3, subdiv=3, lumps=0.14)
    blob('wool_b', (0.07, 0.03, 0.03), (0.06, 0.05, 0.035), WOOL, seed=4, subdiv=2, lumps=0.16)
    blob('wool_c', (-0.08, -0.02, 0.025), (0.05, 0.045, 0.03), WOOL, seed=5, subdiv=2, lumps=0.16)
    finish('wool', [(30, 0.7, 0.45, 0)])

def yarn():
    # a ball of yarn, wound round and round, its end hanging
    fresh()
    blob('ball', (0, 0, 0), (0.065, 0.065, 0.062), WOOL, seed=6, subdiv=3, lumps=0.03)
    for k in range(6):
        a = math.pi * k / 6
        ring(f'wrap_{k}', (0, 0, 0), 0.065, 0.005, (math.cos(a), math.sin(a), 0.5 * ((k % 3) - 1)), WOOL, segs=16, rsegs=4)
    tube('yarnend', smooth_line([V((0.04, 0.04, -0.03)), V((0.08, 0.07, -0.06)), V((0.13, 0.06, -0.062))], 1), 0.005, WOOL, res=1)
    finish('yarn', [(30, 0.6, 0.35, 0)])

def linenbolt():
    # a bolt of linen rolled up, tied, its end lying out
    fresh()
    cyl('roll', (-0.21, 0, 0), (0.21, 0, 0), 0.07, 0.07, LINEN, segs=18)
    for s in (-1, 1):
        for k, r in enumerate((0.025, 0.045, 0.062)):
            ring(f'wrap_end{s}{k}', (0.211 * s, 0, 0), r, 0.0025, (1, 0, 0), LINEN, segs=16, rsegs=4)
    for k, x in enumerate((-0.12, 0.12)): ring(f'tie_{k}', (x, 0, 0), 0.071, 0.005, (1, 0, 0), FLAX, segs=16, rsegs=5)
    drape('drape', -0.205, 0.205, [(0.0, -0.07), (0.05, -0.068), (0.1, -0.069), (0.14, -0.07)], LINEN, folds=1, amp=0.0, thick=0.004, nx=4)
    finish('linenbolt', [(30, 1.0, 0.6, 0)])

def woolbolt():
    # a bolt of wool cloth folded in four
    fresh()
    for i in range(4):
        box(f'cloth_{i}', (-0.18 + rng.uniform(-0.006, 0.006), -0.11 + rng.uniform(-0.006, 0.006), -0.05 + 0.025 * i),
            (0.18 + rng.uniform(-0.006, 0.006), 0.11 + rng.uniform(-0.006, 0.006), -0.025 + 0.025 * i), WOOL, bevel=0.011, jitter=0.003, seed=30 + i)
    for k, x in enumerate((-0.09, 0.09)):
        box(f'tie_{k}', (x - 0.006, -0.116, -0.054), (x + 0.006, 0.116, 0.054), FLAX, bevel=0.002)
    finish('woolbolt', [(30, 1.0, 0.6, 0)])

def band():
    # a roll of tablet-woven band: red, a blue stripe along it, its end run out
    fresh()
    cyl('roll_band', (0, 0, -0.018), (0, 0, 0.018), 0.045, 0.045, RED, segs=16)
    ring('stripe_a', (0, 0, -0.008), 0.045, 0.0028, (0, 0, 1), BLUE, segs=16, rsegs=4)
    ring('stripe_b', (0, 0, 0.008), 0.045, 0.0028, (0, 0, 1), BLUE, segs=16, rsegs=4)
    ribbon('bandend', smooth_line([V((0.03, 0.035, -0.016)), V((0.08, 0.05, -0.016)), V((0.15, 0.03, -0.016)), V((0.2, 0.05, -0.016))], 1), 0.036, 0.036, RED, thick=0.003, normal=(0, 0, 1))
    box('stripe_end', (0.06, 0.03, -0.0148), (0.19, 0.06, -0.0142), BLUE)
    finish('band', [(30, 0.6, 0.4, 0)])

def spindle():
    # a drop spindle: a shaft (along z, the hand at the origin) with a stone whorl at its foot and a cop of yarn spun onto it above
    # the whorl, its thread running up from the top
    fresh()
    cyl('shaft_lo', (0, 0, -0.17), (0, 0, -0.04), 0.004, 0.0075, WOOD, segs=8)
    cyl('shaft_hi', (0, 0, -0.04), (0, 0, 0.21), 0.0075, 0.004, WOOD, segs=8)
    lathe('whorl', (0, 0, -0.145), (0, 0, -0.125), lambda t: 0.024 + 0.006 * math.sin(math.pi * t), STONE, segs=14)
    lathe('cop', (0, 0, -0.123), (0, 0, -0.01), lambda t: 0.009 + 0.017 * math.sin(math.pi * min(1, t * 1.35)) ** 0.8, WOOL, segs=12, flute=0.03, lobes=3, twist=20)
    tube('thread', [V((0, 0.006, -0.012)), V((0.002, 0.008, 0.1)), V((0, 0.006, 0.205)), V((0.0, 0.004, 0.3))], 0.0018, WOOL, res=1)
    finish('spindle', [(30, 0.8, 0.2, 0.0), (90, 0.6, 0.05, 0.0)])

# ---------------- the garments, folded (as they lie on the ground and in the bag) ----------------
def stack(name, w, d, layers, th, mat, seed, cx=0.0, cy=0.0):
    # cloth folded and laid flat: layers of rounded slabs, each a little off the one under it. Returns the top's height
    r = random.Random(seed); z = 0.0
    for i in range(layers):
        ox, oy = r.uniform(-0.006, 0.006), r.uniform(-0.006, 0.006)
        box(f'cloth_{name}{i}', (cx - w / 2 + ox, cy - d / 2 + oy, z), (cx + w / 2 + ox, cy + d / 2 + oy, z + th), mat, bevel=min(0.012, th * 0.45), jitter=0.003, seed=seed + i)
        z += th * 0.96
    return z

def laid(ob, deg, at):
    turned(ob, Matrix.Rotation(math.radians(deg), 3, 'Z')); ob.data.transform(Matrix.Translation(at)); return ob

def undertunic():
    # a linen undertunic folded: a sleeve turned back across the front, the neck's keyhole slit at the top
    fresh()
    z = stack('body', 0.34, 0.28, 3, 0.02, LINEN, 40)
    laid(box('cloth_sleeve', (-0.19, -0.035, 0), (0.19, 0.035, 0.013), LINEN, bevel=0.006, jitter=0.002, seed=44), -16, (0.0, -0.03, z - 0.002))
    box('stripe_neck', (-0.005, 0.07, z - 0.001), (0.005, 0.14, z + 0.001), WOOL)
    box('stripe_hem', (-0.17, -0.137, z - 0.001), (0.17, -0.131, z + 0.001), WOOL)
    finish('undertunic', [(30, 0.9, 0.55, 0)])

def tunic():
    # a wool tunic folded: a sleeve back across it, a red woven band round the neck and along the hem
    fresh()
    z = stack('body', 0.36, 0.3, 3, 0.022, OAT, 50)
    laid(box('cloth_sleeve', (-0.2, -0.04, 0), (0.2, 0.04, 0.015), OAT, bevel=0.007, jitter=0.002, seed=54), 14, (0.0, -0.03, z - 0.002))
    neck = [V((0.065 * math.cos(a), 0.15 + 0.065 * math.sin(a), z + 0.001)) for a in [math.pi + math.pi * i / 10 for i in range(11)]]
    ribbon('trim_neck', neck, 0.02, 0.02, RED, thick=0.004, normal=(0, 0, 1))
    box('trim_hem', (-0.18, -0.15, z - 0.002), (0.18, -0.125, z + 0.003), RED, bevel=0.0015)
    box('stripe_hem', (-0.18, -0.14, z + 0.0028), (0.18, -0.135, z + 0.0034), BLUE)
    finish('tunic', [(30, 0.9, 0.55, 0)])

def dress():
    # a wool apron dress folded long, its pair of oval brooches still pinned on with a string of beads between them
    fresh()
    z = stack('body', 0.42, 0.26, 4, 0.018, OAT, 60)
    for k, x in enumerate((-0.08, 0.08)):
        blob(f'brooch_{k}', (x, 0.06, z), (0.034, 0.024, 0.016), BRONZE, seed=61 + k, subdiv=2, lumps=0.02)
        ring(f'brooch_rim{k}', (x, 0.06, z + 0.001), 0.03, 0.0035, (0, 0, 1), BRONZE, segs=12, rsegs=4)
    for i in range(9):
        t = (i + 1) / 10; x = -0.08 + 0.16 * t; y = 0.06 - 0.07 * math.sin(math.pi * t)
        blob(f'bead_{i}', (x, y, z + 0.006), (0.007, 0.007, 0.006), (RED, BLUE, LINEN)[i % 3], seed=70 + i, subdiv=1, lumps=0.05)
    laid(box('strap', (-0.012, -0.06, 0), (0.012, 0.06, 0.004), OAT, bevel=0.0015), 0, (-0.08, 0.13, z - 0.001))
    laid(box('strap_b', (-0.012, -0.06, 0), (0.012, 0.06, 0.004), OAT, bevel=0.0015), 0, (0.08, 0.13, z - 0.001))
    finish('dress', [(30, 0.9, 0.55, 0)])

def trousers():
    # wool trousers folded in two: the legs side by side, splayed a little from the waist, the drawstring hanging out
    fresh()
    z = stack('waist', 0.1, 0.24, 2, 0.022, DARKWOOL, 80, cx=-0.15)
    for k, (deg, y) in enumerate(((3, -0.058), (-3, 0.058))):
        for i in range(2):
            laid(box(f'cloth_leg{k}{i}', (0, -0.054, 0.022 * i), (0.32, 0.054, 0.022 * i + 0.022), DARKWOOL, bevel=0.009, jitter=0.003, seed=82 + k * 2 + i), deg, (-0.12, y, 0))
    tube('cord', smooth_line([V((-0.19, 0.0, z)), V((-0.22, 0.02, z + 0.004)), V((-0.25, 0.01, z - 0.01)), V((-0.27, 0.04, 0.008)), V((-0.26, 0.08, 0.004))]), 0.0045, FLAX, res=1)
    tube('cord_b', smooth_line([V((-0.19, 0.0, z)), V((-0.23, -0.03, z - 0.004)), V((-0.26, -0.05, 0.01)), V((-0.25, -0.09, 0.004))]), 0.0045, FLAX, res=1)
    finish('trousers', [(30, 0.9, 0.55, 0)])

def cloak():
    # a hooded wool cloak folded square, the hood bunched on top with its point, the ring-pin through it
    fresh()
    z = stack('body', 0.36, 0.34, 3, 0.024, CLOAKWOOL, 90)
    blob('hood', (0.02, 0.05, z + 0.012), (0.11, 0.085, 0.032), CLOAKWOOL, seed=91, subdiv=3, lumps=0.06)
    lathe('hood_point', (0.1, 0.07, z + 0.016), (0.165, 0.085, z + 0.006), lambda t: 0.03 * (1 - t) ** 0.5 + 0.004, CLOAKWOOL, segs=10, step=0.02)
    ring('pin_ring', (-0.1, -0.08, z + 0.004), 0.024, 0.0042, (0, 0, 1), IRON, segs=14, rsegs=5)
    laid(cyl('pin', (0, 0, 0), (0.1, 0, 0), 0.003, 0.0012, IRON, segs=6), 35, (-0.13, -0.11, z + 0.004))
    finish('cloak', [(30, 0.9, 0.55, 0)])

# ---------------- out ----------------
def finish(base, views):
    export(base)
    if 'render' in sys.argv and __name__ != '__bake__': preview(base, views)

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
        vol = sum(me.vertices[p.vertices[0]].co.dot(me.vertices[p.vertices[1]].co.cross(me.vertices[p.vertices[2]].co)) for p in me.polygons) / 6
        if ob.name == 'water': assert all(p.normal.z > 0.99 for p in me.polygons), 'water faces down'
        elif vol <= 0: print('INSIDE OUT?', ob.name, vol)
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

COLS = [(0.36, 0.24, 0.14, 1), (0.42, 0.4, 0.37, 1), (0.08, 0.16, 0.15, 1), (0.6, 0.48, 0.24, 1), (0.8, 0.76, 0.64, 1),
        (0.5, 0.45, 0.38, 1), (0.1, 0.1, 0.11, 1), (0.45, 0.08, 0.06, 1), (0.08, 0.13, 0.32, 1), (0.48, 0.45, 0.38, 1),
        (0.55, 0.47, 0.36, 1), (0.28, 0.23, 0.18, 1), (0.2, 0.16, 0.12, 1), (0.6, 0.4, 0.15, 1)]
def preview(base, views):
    # yaw 0 looks at the piece's front (from +y)
    for m, c in zip(mats, COLS):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = c; b.inputs['Roughness'].default_value = 0.9
        if m.name == 'iron': b.inputs['Metallic'].default_value = 0.6; b.inputs['Roughness'].default_value = 0.5
        if m.name == 'water': b.inputs['Roughness'].default_value = 0.05
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH':
            for p in ob.data.polygons: p.use_smooth = True
            ob.data.set_sharp_from_angle(angle=math.radians(80 if ob.name.startswith(SMOOTH) else 35))
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 640, 480
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    ground = -0.002 if base in ('pool', 'brake', 'loom', 'table') else -0.12 if base == 'spindle' else -0.08
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, ground if base != 'spindle' else -0.2)); g = bpy.context.active_object; gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.36, 0.22, 1); g.data.materials.append(gm)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 2.4; sun.rotation_euler = (math.radians(50), 0, math.radians(150)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 40
    for yaw, d, z, look in views:
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, math.cos(a) * d, z)
        cam.rotation_euler = (V((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'{base}_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

KINDS = ('pool', 'brake', 'loom', 'table', 'retted', 'line', 'wool', 'yarn', 'linenbolt', 'woolbolt', 'band', 'spindle', 'undertunic', 'tunic', 'dress', 'trousers', 'cloak')
which = [a for a in sys.argv if a in KINDS] or list(KINDS)
for w in which: globals()[w]()
