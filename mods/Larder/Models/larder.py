# Larder's looks (dairy first), made from scratch: the butter churn (with its stool, a dish of butter and the paddle), the lever cheese
# press (bench, hoop, follower, beam, hung stone, whey bucket), and the items: butter, cheese, skyr.
# Run: blender -b --python ~/.cache/larder/larder.py -- [render] [churn|press|butter|cheese|skyr]
# (writes ~/.cache/larder/<name>.json.gz; previews <name>_<yaw>_<distance>.png with "render"). What ships is the bake of these
# (bake_conf.py, docs/model-baking.md). Blender frame: z up, front +y (the game's +z, where a player stands to use the piece); the pieces
# stand on z 0, the items lie about the origin. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres;
# wood's grain runs along v), triangles per material: 0 wood, 1 stone, 2 iron, 3 butter, 4 rind, 5 curd, 6 skyr, 7 blueberry, 8 rope,
# 9 horn, 10 whey.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

OUT = '/home/bob/.cache/larder/'
WOOD, STONE, IRON, BUTTER, RIND, CURD, SKYR, BERRY, ROPE, HORN, WHEY = range(11)
NAMES = ('wood', 'stone', 'iron', 'butter', 'rind', 'curd', 'skyr', 'blueberry', 'rope', 'horn', 'whey')
SMOOTH = ('rock', 'hoop', 'band', 'pat', 'berry', 'rope', 'skyr', 'spoon', 'curd', 'dish', 'bowl', 'cheese', 'knob', 'stream')
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
    # a torus round axis (a hoop, a rope loop); squash flattens its section along the axis
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

def cyl(name, a, b, r0, r1, mat, segs=12):
    return lathe(name, a, b, lambda t: r0 + (r1 - r0) * t, mat, segs=segs)
def hull(name, pts, mat, grain=None):
    # the solid round a handful of points (a tub's stave, a hewn block)
    bm = bmesh.new()
    for p in pts: bm.verts.new(V(p))
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, mat, grain)


def link_m(name, bm, slots, grain=None):
    # a part of several materials: each face's material_index points into slots
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for s in slots: me.materials.append(mats[s])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    if grain is not None: ob['grain'] = tuple(grain)
    return ob

def revolve(name, centre, prof, mat, segs=16, a0=0.0, a1=2 * math.pi, cut_mat=None, wobble=0.0, seed=0, grain=None):
    # a solid of turning about the z axis through centre: prof is its outline (r, z), from the axis round to the axis again. Swept from
    # a0 to a1; short of a full turn, its two cut faces are cut_mat (a cheese with a wedge out). wobble roughens it round (hand-made)
    c = V(centre); full = a1 - a0 > 2 * math.pi - 1e-4
    n = segs if full else segs + 1
    r_ = random.Random(seed); wob = [1 + r_.uniform(-wobble, wobble) for _ in range(n)]
    bm = bmesh.new(); rows = []
    for r, z in prof:
        if r < 1e-6: rows.append([bm.verts.new(c + V((0, 0, z)))] * n); continue
        row = []
        for j in range(n):
            a = a0 + (a1 - a0) * j / (segs if full else segs)
            rr = r * wob[j]
            row.append(bm.verts.new(c + V((math.cos(a) * rr, math.sin(a) * rr, z))))
        rows.append(row)
    for i in range(len(rows) - 1):
        A, B = rows[i], rows[i + 1]
        for j in range(segs):
            k = (j + 1) % n if full else j + 1
            q = [A[j], A[k], B[k], B[j]]
            uq = []
            for v in q:
                if v not in uq: uq.append(v)
            if len(uq) >= 3: bm.faces.new(uq).material_index = 0
    if not full:
        for j in (0, n - 1):
            ring_ = []
            for row in rows:
                if row[j] not in ring_: ring_.append(row[j])
            bm.faces.new(ring_).material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link_m(name, bm, [mat] if cut_mat is None else [mat, cut_mat], grain)

def staves(name, centre, z0, z1, r0, r1, th, n, seed, jag=0.01, mat=WOOD):
    # a ring of hewn staves (a churn, a cheese hoop, a bucket): radius r0 at z0 to r1 at z1, th thick, one stave's middle at the front (+y)
    r = random.Random(seed); c = V(centre); R = lambda z: r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    ph = math.pi / 2 - math.pi / n; out = []
    for k in range(n):
        a0, a1 = ph + 2 * math.pi * (k + 0.01) / n, ph + 2 * math.pi * (k + 0.99) / n
        top = z1 + r.uniform(-jag, jag * 0.5)
        pts = []
        for z in (z0, top):
            for rr in (R(z), R(z) - th):
                for a in (a0, a1): pts.append(c + V((math.cos(a) * rr, math.sin(a) * rr, z)))
        out.append(hull(f'{name}_{k}', pts, mat, (0, 0, 1)))
    return R

# ---------------- the churn ----------------
# An upright stave churn (radius 0.18 at its foot to 0.14 at its rim, z 0.9), three iron hoops, a lid with a collar, the dasher's shaft
# standing up through it to z 1.3 with a cross handle. To its right (+x) a three-legged stool (seat radius
# 0.17 at x 0.55, top z 0.45) with a wooden dish of butter and the butter paddle lying by it.
CH_TOP, CH_R0, CH_R1 = 0.9, 0.18, 0.14
def churn():
    fresh(); n = 16
    R = staves('stave', (0, 0, 0), 0.0, CH_TOP, CH_R0, CH_R1, 0.022, n, seed=3, jag=0.008)
    cyl('floor', (0, 0, 0.02), (0, 0, 0.06), R(0.02) - 0.02, R(0.06) - 0.02, WOOD, segs=16)
    for k, z in enumerate((0.1, 0.46, 0.8)):
        ring(f'hoop_{k}', (0, 0, z), R(z) + 0.004, 0.0075, (0, 0, 1), IRON, segs=18, rsegs=4, squash=2.6)
    # (the lid: sits down inside the rim, its top a little proud of it; a collar where the shaft goes through)
    revolve('lid', (0, 0, 0), [(0, CH_TOP - 0.03), (R(CH_TOP) - 0.024, CH_TOP - 0.03), (R(CH_TOP) - 0.024, CH_TOP - 0.004),
                              (R(CH_TOP) + 0.008, CH_TOP - 0.002), (R(CH_TOP) + 0.01, CH_TOP + 0.016), (R(CH_TOP) - 0.004, CH_TOP + 0.026),
                              (0, CH_TOP + 0.03)], WOOD, segs=14, wobble=0.01, seed=5, grain=(1, 0, 0))
    revolve('collar', (0, 0, 0), [(0, CH_TOP + 0.02), (0.042, CH_TOP + 0.02), (0.038, CH_TOP + 0.06), (0.028, CH_TOP + 0.068), (0, CH_TOP + 0.068)],
            WOOD, segs=10, grain=(0, 0, 1))
    pole('shaft', (0, 0, 0.3), (0.006, -0.004, 1.27), 0.017, seed=6, bend=0.004)
    pole('handle', (-0.09, -0.004, 1.27), (0.1, -0.004, 1.27), 0.015, seed=7, bend=0.004)
    revolve('knob', (0.006, -0.004, 0), [(0, 1.255), (0.022, 1.255), (0.024, 1.285), (0, 1.3)], WOOD, segs=8)
    # the stool
    sx = 0.55
    revolve('seat', (sx, 0, 0), [(0, 0.41), (0.165, 0.41), (0.172, 0.425), (0.168, 0.45), (0, 0.452)], WOOD, segs=14, wobble=0.03, seed=8, grain=(1, 0.3, 0))
    for k in range(3):
        a = math.radians(30 + 120 * k)
        top, foot = V((sx + math.cos(a) * 0.09, math.sin(a) * 0.09, 0.43)), V((sx + math.cos(a) * 0.19, math.sin(a) * 0.19, 0.0))
        pole(f'leg_{k}', foot, top, 0.024, seed=10 + k, bend=0.006)
    # (the dish, and the pat of butter on it, the paddle beside it)
    dx, dy, dz = sx - 0.04, -0.035, 0.452
    revolve('dish', (dx, dy, 0), [(0, dz), (0.06, dz), (0.095, dz + 0.03), (0.087, dz + 0.032), (0.055, dz + 0.01), (0, dz + 0.01)], WOOD, segs=14, wobble=0.015, seed=12, grain=(1, 0, 0))
    pat = box('pat', (dx - 0.05, dy - 0.035, dz + 0.009), (dx + 0.05, dy + 0.035, dz + 0.052), BUTTER, bevel=0.012, jitter=0.003, seed=13)
    turned(pat, Matrix.Rotation(math.radians(20), 3, 'Z'), (dx, dy, 0))
    paddle((sx - 0.1, 0.1, dz + 0.0055), (1, 0.15, 0), 0.0, 0.8)
    finish('churn', [(0, 2.6, 1.3, 0.6), (35, 2.4, 1.6, 0.55), (90, 2.4, 1.2, 0.55), (-140, 2.6, 1.5, 0.55)])

def paddle(at, along, tilt, scale=1.0):
    # a butter paddle lying flat: the blade at 'at', its handle running off along 'along'; tilt raises the handle end
    c = V(at); d = V(along).normalized(); d = (d + V((0, 0, tilt))).normalized(); side = d.cross(V((0, 0, 1))).normalized(); up = side.cross(d)
    s = scale
    hull('paddle_blade', [c + d * u * s + side * w * s + up * t * s for u in (-0.05, 0.055) for w in (-0.032, 0.032) for t in (-0.005, 0.005)]
         + [c + d * -0.06 * s + side * w * s + up * t * s for w in (-0.02, 0.02) for t in (-0.004, 0.004)], WOOD, tuple(d))
    pole('paddle_handle', c + d * 0.05 * s + up * 0.001, c + d * 0.17 * s + up * 0.003, 0.009 * s, seed=21, bend=0.002)

# ---------------- the cheese press ----------------
# A plank bench (top x -0.56..0.70, y -0.25..0.25, z 0.41..0.45) with a stout post at its -x end (x -0.68..-0.56, to z 0.95, the beam
# pinned between its cheeks at (-0.62, 0.85)). On a draining board at x -0.25: the cheese hoop (radius 0.15, z 0.48..0.63), curd
# squeezed out round its foot, the follower board and two pressing blocks; the lever beam over them, down to a stone hung on a rope at
# x 0.55 (stone about z 0.51..0.69). A spout off the board's front over a little stave bucket of whey on the ground at (-0.25, 0.3).
PIVOT = V((-0.62, 0, 0.85)); HX, HY = -0.25, 0.0
def press():
    fresh(); r = random.Random(41)
    box('top_0', (-0.56, -0.25, 0.41), (0.70, -0.003, 0.45), WOOD, (1, 0, 0), bevel=0.008, jitter=0.003, seed=1)
    box('top_1', (-0.56, 0.003, 0.41), (0.70, 0.25, 0.45), WOOD, (1, 0, 0), bevel=0.008, jitter=0.003, seed=2)
    for i, x in enumerate((-0.46, 0.6)):
        for k, s in enumerate((-1, 1)):
            pole(f'leg_{i}{k}', (x + 0.03 * (1 if i else -1), 0.22 * s, 0), (x, 0.17 * s, 0.42), 0.032, seed=3 + i * 2 + k, bend=0.006)
    box('rail', (-0.48, -0.2, 0.16), (0.62, -0.15, 0.21), WOOD, (1, 0, 0), jitter=0.003, seed=7)
    # (the post, its two cheeks above, the iron pin)
    box('post', (-0.69, -0.065, 0.0), (-0.56, 0.065, 0.79), WOOD, (0, 0, 1), bevel=0.012, jitter=0.004, seed=9)
    for k, (y0, y1) in enumerate(((-0.065, -0.031), (0.031, 0.065))):
        box(f'cheek_{k}', (-0.69, y0, 0.78), (-0.56, y1, 0.95), WOOD, (0, 0, 1), jitter=0.003, seed=10 + k)
    tube('pin', [PIVOT + V((0, -0.085, 0)), PIVOT + V((0, 0.085, 0))], 0.011, IRON, res=1)
    box('foot', (-0.75, -0.12, 0.0), (-0.5, 0.12, 0.06), WOOD, (1, 0, 0), jitter=0.004, seed=12)
    # (the draining board and its spout)
    box('board', (HX - 0.2, -0.2, 0.45), (HX + 0.2, 0.2, 0.478), WOOD, (1, 0, 0), jitter=0.002, seed=13)
    hull('spout', [(x, y, z) for x in (HX - 0.035, HX + 0.035) for y in (0.2, 0.33) for z in (0.44, 0.47)]
         + [(x, y, 0.47) for x in (HX - 0.012, HX + 0.012) for y in (0.2, 0.33)], WOOD, (0, 1, 0))
    cyl('stream', (HX, 0.322, 0.445), (HX + 0.002, 0.305, 0.17), 0.0045, 0.0035, WHEY, segs=4)
    # (the hoop, curd squeezed out at its foot, the follower, the blocks)
    staves('hoopstave', (HX, HY, 0), 0.478, 0.63, 0.15, 0.15, 0.018, 10, seed=17, jag=0.004)
    for k, z in enumerate((0.505, 0.6)): ring(f'band_{k}', (HX, HY, z), 0.154, 0.006, (0, 0, 1), IRON, segs=12, rsegs=4, squash=2.4)
    ring('curd_rim', (HX, HY, 0.484), 0.152, 0.011, (0, 0, 1), CURD, segs=12, rsegs=4, squash=0.6)
    revolve('follower', (HX, HY, 0), [(0, 0.6), (0.128, 0.6), (0.13, 0.64), (0, 0.642)], WOOD, segs=12, wobble=0.01, seed=18, grain=(1, 0, 0))
    box('block_0', (HX - 0.09, -0.055, 0.64), (HX + 0.09, 0.055, 0.715), WOOD, (1, 0, 0), jitter=0.003, seed=19)
    box('block_1', (HX - 0.055, -0.1, 0.715), (HX + 0.055, 0.1, 0.795), WOOD, (0, 1, 0), jitter=0.003, seed=20)
    # (the beam, from past the pivot, resting on the top block, out to the stone's rope)
    bottom_at_hoop = 0.797; half = 0.04
    tilt = math.atan2(PIVOT.z - (bottom_at_hoop + half), HX - PIVOT.x)
    beam = box('beam', (PIVOT.x - 0.08, -0.028, PIVOT.z - half), (0.66, 0.028, PIVOT.z + half), WOOD, (1, 0, 0), bevel=0.01, jitter=0.004, seed=21)
    turned(beam, Matrix.Rotation(tilt, 3, 'Y'), PIVOT)
    on_beam = lambda x: PIVOT.z - math.tan(tilt) * (x - PIVOT.x)   # (the beam's middle line, its height at x)
    # (the stone, a rope round it and up over the beam)
    sx = 0.55; zc = 0.6
    st = rock('rock', (sx, 0, zc), 0.12, seed=44, segs=(8, 6))
    me = st.data; bvh = BVHTree.FromPolygons([v.co[:] for v in me.vertices], [p.vertices[:] for p in me.polygons])
    loop = []
    for k in range(10):
        a = 2 * math.pi * k / 10; d = V((0, math.cos(a), math.sin(a)))
        hit = bvh.ray_cast(V((sx, 0, zc)) + d * 0.4, -d)
        loop.append((hit[0] if hit[0] is not None else V((sx, 0, zc)) + d * 0.1) + d * 0.006)
    tube('rope_stone', loop, 0.008, ROPE, cyclic=True, res=0)
    hit = bvh.ray_cast(V((sx, 0, zc + 0.4)), V((0, 0, -1)))
    stone_top = hit[0].z if hit[0] is not None else zc + 0.09
    yb = on_beam(sx)
    for s in (-1, 1):
        tube(f'rope_up{s}', [V((sx + 0.01 * s, 0.035 * s, stone_top - 0.005)), V((sx + 0.005 * s, 0.034 * s, yb - 0.02)), V((sx, 0.032 * s, yb + 0.02))], 0.008, ROPE, res=0)
    ring('rope_beam', (sx, 0, yb), 0.046, 0.009, (math.cos(tilt), 0, -math.sin(tilt)), ROPE, segs=8, rsegs=4, squash=1.0)
    # the whey bucket
    bx, by = HX, 0.3
    Rb = staves('bucket', (bx, by, 0), 0.0, 0.2, 0.095, 0.11, 0.014, 8, seed=50, jag=0.006)
    for k, z in enumerate((0.04, 0.16)): ring(f'hoop_b{k}', (bx, by, z), Rb(z) + 0.003, 0.005, (0, 0, 1), IRON, segs=10, rsegs=4, squash=2.4)
    revolve('whey', (bx, by, 0), [(0, 0.02), (Rb(0.02) - 0.016, 0.02), (Rb(0.15) - 0.016, 0.15), (0, 0.15)], WHEY, segs=8)
    finish('press', [(0, 3.0, 1.4, 0.55), (35, 2.8, 1.8, 0.5), (90, 2.8, 1.2, 0.5), (-130, 3.0, 1.6, 0.5), (15, 1.4, 1.1, 0.5)])

# ---------------- items ----------------
def butter():
    # a pat of butter on a small round wooden dish (0.14 across)
    fresh(); z0 = -0.02
    revolve('dish', (0, 0, 0), [(0, z0), (0.05, z0), (0.07, z0 + 0.018), (0.064, z0 + 0.021), (0.042, z0 + 0.008), (0, z0 + 0.008)], WOOD, segs=16, wobble=0.012, seed=2, grain=(1, 0, 0))
    pat = box('pat', (-0.04, -0.03, z0 + 0.007), (0.04, 0.03, z0 + 0.045), BUTTER, bevel=0.01, jitter=0.002, seed=3)
    turned(pat, Matrix.Rotation(math.radians(15), 3, 'Z'))
    # (a curl shaved off the top)
    tube('pat_curl', smooth_line([V((-0.012, -0.004, z0 + 0.046)), V((0.002, 0.004, z0 + 0.052)), V((0.012, 0.0, z0 + 0.054)), V((0.012, -0.008, z0 + 0.049))], 1), 0.006, BUTTER, res=1)
    finish('butter', [(30, 0.55, 0.32, 0)])

def cheese():
    # a round cheese (0.18 across, 0.08 thick), waxy rind, a wedge out of its front showing the paler inside
    fresh(); h, R, e = 0.04, 0.09, 0.012
    prof = [(0, -h), (R - e, -h), (R - e * 0.3, -h + e * 0.3), (R, -h + e), (R + 0.004, 0), (R, h - e), (R - e * 0.3, h - e * 0.3), (R - e, h), (0, h)]
    wedge = math.radians(90); mid = math.radians(90 - 30)   # (a quarter out, facing front-right; a narrower cut bakes black where its faces meet)
    revolve('cheese', (0, 0, 0), prof, RIND, segs=20, a0=mid + wedge / 2, a1=mid - wedge / 2 + 2 * math.pi, cut_mat=CURD, wobble=0.01, seed=4)
    finish('cheese', [(30, 0.6, 0.35, 0), (-30, 0.6, 0.3, 0)])

def skyr():
    # a small wooden bowl (0.14 across) of thick skyr, blueberries on it, a horn spoon stood in it
    fresh(); z0, H, Rr, t = -0.03, 0.055, 0.07, 0.008
    revolve('bowl', (0, 0, 0), [(0, z0), (0.035, z0), (0.04, z0 + 0.006), (0.062, z0 + 0.02), (Rr, z0 + H), (Rr - t, z0 + H + 0.002),
                                (0.055, z0 + 0.024), (0.03, z0 + t), (0, z0 + t)], WOOD, segs=14, wobble=0.012, seed=5, grain=(1, 0, 0))
    zs = z0 + H - 0.008
    revolve('skyr', (0, 0, 0), [(0, z0 + t + 0.002), (0.03, z0 + t + 0.002), (0.056, z0 + 0.026), (Rr - t + 0.001, zs), (0.045, zs + 0.006), (0.02, zs + 0.01), (0, zs + 0.012)],
            SKYR, segs=12, wobble=0.04, seed=6)
    r = random.Random(8)
    for k, (x, y) in enumerate(((0.012, 0.018), (-0.016, 0.01), (0.002, -0.014), (0.028, -0.006), (-0.024, -0.02))):
        rr = 0.0065 + r.uniform(-0.001, 0.001)
        zz = zs + 0.012 * (1 - (math.hypot(x, y) / 0.06) ** 2) + rr * 0.6
        blob(f'berry_{k}', (x, y, zz), (rr, rr, rr * 0.9), BERRY, seed=9 + k, subdiv=1, lumps=0.04)
    # (the spoon: its bowl down in the skyr at the back, the handle leaning out over the rim to the right)
    foot, top = V((-0.01, -0.02, zs - 0.01)), V((0.1, -0.03, zs + 0.06))
    d = (top - foot).normalized()
    blob('spoon_bowl', foot - d * 0.012, (0.016, 0.012, 0.005), HORN, seed=20, subdiv=1, lumps=0.02)
    turned(bpy.data.objects['spoon_bowl'], V((1, 0, 0)).rotation_difference(d).to_matrix(), foot - d * 0.012)
    tube('spoon_handle', smooth_line([foot, foot.lerp(top, 0.5) + V((0, 0, 0.006)), top], 1), 0.0045, HORN, res=1)
    finish('skyr', [(30, 0.55, 0.35, 0), (-60, 0.5, 0.3, 0)])

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

SRGB = [(0.55, 0.42, 0.3), (0.62, 0.6, 0.57), (0.17, 0.17, 0.18), (0.93, 0.82, 0.42), (0.82, 0.66, 0.35), (0.95, 0.86, 0.55),
        (0.94, 0.93, 0.88), (0.2, 0.22, 0.45), (0.55, 0.45, 0.3), (0.75, 0.68, 0.55), (0.85, 0.86, 0.7)]
COLS = [tuple(((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c) + (1,) for c in SRGB]
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
    ground = min(min((ob.matrix_world @ V(c)).z for c in ob.bound_box) for ob in bpy.context.scene.collection.objects if ob.type == 'MESH') - 0.002
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, ground)); g = bpy.context.active_object; gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.36, 0.22, 1); g.data.materials.append(gm)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 2.4; sun.rotation_euler = (math.radians(50), 0, math.radians(150)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 40
    for yaw, d, z, look in views:
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, math.cos(a) * d, z)
        cam.rotation_euler = (V((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'{base}_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)


# ---------------- tapestries: hung on a rod as the game's banners are ----------------
# The origin is the top centre of the rod; the cloth hangs down from it (-z), as wide as it is along y, its picture facing +x (the
# game's +x). The figures are 'weave' decals laid a hair in front of the cloth: the bake takes them into its picture only. Each is
# made of convex pieces (a horse is a body, a neck, a head, legs), each piece cut fine and laid onto the cloth's folds.
TH = 0.008   # (the cloth's thickness: solidified about its middle)

KINDS = ('churn', 'press', 'butter', 'cheese', 'skyr')
which = [a for a in sys.argv if a in KINDS] or list(KINDS)
for w in which: globals()[w]()
