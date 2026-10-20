# Larder's looks (dairy first), made from scratch: the butter churn (with its stool, a dish of butter and the paddle), the lever cheese
# press (bench, hoop, follower, beam, hung stone, whey bucket), and the items: butter, cheese, skyr.
# Run: blender -b --python ~/.cache/larder/larder.py -- [render] [churn|press|butter|cheese|skyr|smokehouse|saltpan|saltbarrel|dryingrack|
#      smokedmeat|saltmeat|stockfish|salt]
# (writes ~/.cache/larder/<name>.json.gz; previews <name>_<yaw>_<distance>.png with "render"). What ships is the bake of these
# (bake_conf.py, docs/model-baking.md). Blender frame: z up, front +y (the game's +z, where a player stands to use the piece); the pieces
# stand on z 0, the items lie about the origin. Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres;
# wood's grain runs along v), triangles per material: 0 wood, 1 stone, 2 iron, 3 butter, 4 rind, 5 curd, 6 skyr, 7 blueberry, 8 rope,
# 9 horn, 10 whey, 11 soot, 12 sootstone, 13 salt, 14 smoked, 15 meat, 16 fat, 17 fish, 18 smudge.
# Preserves (after the dairy): the smokehouse, the salt pan, the salt barrel, the stockfish rack, and the items smokedmeat, saltmeat,
# stockfish, salt.
# Baking (after the preserves): the bake oven (a stone-and-clay dome on a dry-stone plinth), and the items flatbread, flatbreaddough.
# Slots 19 clay, 20 bread, 21 toast, 22 dough, 23 flour. Then spoiled (food gone off), slots 24 rot, 25 mould.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

OUT = '/home/bob/.cache/larder/'
WOOD, STONE, IRON, BUTTER, RIND, CURD, SKYR, BERRY, ROPE, HORN, WHEY, SOOT, SOOTSTONE, SALT, SMOKED, MEAT, FAT, FISH, SMUDGE, \
    CLAY, BREAD, TOAST, DOUGH, FLOUR, ROT, MOULD = range(26)
NAMES = ('wood', 'stone', 'iron', 'butter', 'rind', 'curd', 'skyr', 'blueberry', 'rope', 'horn', 'whey',
         'soot', 'sootstone', 'salt', 'smoked', 'meat', 'fat', 'fish', 'smudge', 'clay', 'bread', 'toast', 'dough', 'flour', 'rot', 'mould')
SMOOTH = ('rock', 'hoop', 'band', 'pat', 'berry', 'rope', 'skyr', 'spoon', 'curd', 'dish', 'bowl', 'cheese', 'knob', 'stream',
          'haunch', 'bone', 'string', 'heap', 'crust', 'scoop', 'lip', 'handle_ring', 'ash', 'fish', 'dome', 'vent', 'bread', 'dough', 'lump', 'knuckle')
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

# ================ preserves: smoke, salt, stockfish ================
def plank(name, a, b, wdir, w, th, mat=WOOD, jitter=0.003, seed=0, chamfer=0.0, nrm=None):
    # a hewn board from a to b (its middle line), w wide along wdir, th thick; chamfer cuts its long edges
    a, b = V(a), V(b); L = (b - a).normalized(); wd = V(wdir); wd = (wd - L * wd.dot(L)).normalized()
    n = L.cross(wd).normalized() if nrm is None else V(nrm).normalized()
    r = random.Random(seed); pts = []
    sec = [(-1, -1), (1, -1), (1, 1), (-1, 1)] if not chamfer else \
          [(s * (1 - 2 * chamfer / w), t) for s in (-1, 1) for t in (-1, 1)] + [(s, t * (1 - 2 * chamfer / th)) for s in (-1, 1) for t in (-1, 1)]
    for p in (a, b):
        for s, t in sec:
            pts.append(p + wd * (s * w / 2) + n * (t * th / 2) + V((r.uniform(-jitter, jitter), r.uniform(-jitter, jitter), r.uniform(-jitter, jitter))))
    return hull(name, pts, mat, tuple(b - a))

def stone(name, centre, size, seed, mat=STONE, yaw=None, extra=3, round_=0.18):
    # a hand-sized to knee-high field stone, flat-faced: a box with its corners knocked off (pulled in), a few bulges, turned its own way
    r = random.Random(seed); c = V(centre); sx, sy, sz = size
    pts = []
    for X in (-1, 1):
        for Y in (-1, 1):
            for Z in (-1, 1):
                k = 1 - r.uniform(0.3 * round_, round_)
                pts.append(V((X * sx / 2 * k * r.uniform(0.9, 1.0), Y * sy / 2 * k * r.uniform(0.9, 1.0), Z * sz / 2 * k)))
    for _ in range(extra):
        d = V((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 0.6))).normalized()
        pts.append(V((d.x * sx / 2, d.y * sy / 2, d.z * sz / 2)) * r.uniform(0.92, 1.02))
    m = Matrix.Rotation(r.uniform(0, 2 * math.pi) if yaw is None else yaw + r.uniform(-0.06, 0.06), 3, 'Z') @ Matrix.Rotation(r.uniform(-0.05, 0.05), 3, 'X')
    return hull(name, [c + m @ p for p in pts], mat)

def cobble(name, centre, size, seed, mat=STONE, yaw=None, n=16):
    # a rounded field stone, flat-faced: the hull of points spread over an egg, a little uneven
    r = random.Random(seed); c = V(centre); sx, sy, sz = size; pts = []
    for k in range(n):
        zz = 1 - 2 * (k + 0.5) / n; rr = math.sqrt(1 - zz * zz); a = k * 2.39996 + r.uniform(-0.3, 0.3)
        f = r.uniform(0.85, 1.0)
        pts.append(V((math.cos(a) * rr * sx / 2 * f, math.sin(a) * rr * sy / 2 * f, zz * sz / 2 * (0.85 if abs(zz) > 0.8 else 1.0))))
    m = Matrix.Rotation(r.uniform(0, 2 * math.pi) if yaw is None else yaw + r.uniform(-0.15, 0.15), 3, 'Z') @ Matrix.Rotation(r.uniform(-0.08, 0.08), 3, 'X')
    return hull(name, [c + m @ p for p in pts], mat)

def patch(name, centre, normal, rx, ry, mat, seed=0, up=(0, 0, 1), lift=0.004, n=9, boxy=1.0):
    # a mark lying flat on a surface (soot, salt flecks): an uneven blot rx by ry, lifted a hair off it. Name it 'decal...': the bake
    # takes it into the texture only
    r = random.Random(seed); c = V(centre); nr = V(normal).normalized(); u = V(up); u = (u - nr * u.dot(nr)).normalized(); s = u.cross(nr)
    pts = []
    for k in range(n):
        a = 2 * math.pi * (k + 0.5) / n; k_ = r.uniform(0.7, 1.05) if boxy == 1.0 else r.uniform(0.85, 1.0)
        ca, sa = math.cos(a), math.sin(a); ca, sa = math.copysign(abs(ca) ** boxy, ca), math.copysign(abs(sa) ** boxy, sa)   # (boxy < 1: squarer)
        for t in (0.0, 0.002):
            pts.append(c + nr * (lift + t) + s * ca * rx * k_ + u * sa * ry * k_)
    return hull(name, pts, mat)

def roofline(x, eave_x, eave_z, ridge_x, ridge_z):
    # the underside of a roof slope at x (a straight pitch from the eave up to the ridge)
    return eave_z + (eave_x - abs(x)) * (ridge_z - eave_z) / (eave_x - ridge_x)

# ---------------- the smokehouse ----------------
# A dry-stone firebox (x, y -0.65..0.65, z 0..0.45; the mouth at the front, x -0.2..0.2 to z 0.30 under a lintel stone) under a stone slab;
# on it a plank hut (corner posts, sills, wall plates; walls at |x|, |y| 0.6) with a small batten hatch in the front wall
# (x -0.19..0.19, z 0.92..1.26). A plank roof pitched to the ridge along y (eaves z 1.34 at x +-0.74, front and back overhang to y +-0.74),
# left open 0.16 along the ridge for the smoke under a raised ridge cap (top z 1.9). Soot round the mouth and along the ridge gap.
SH = dict(W=0.65, IN=0.47, TOPZ=0.45, EX=0.74, EZ=1.34, RX=0.08, RZ=1.80, OV=0.74)
def smokehouse():
    fresh(); r = random.Random(61); W, IN = SH['W'], SH['IN']
    # (the firebox: three courses of stones, the joints staggered; the front left open for the mouth, a long lintel over it)
    courses = [(-0.03, 0.15), (0.15, 0.30), (0.30, 0.45)]
    k = 0
    def course_side(ci, z0, z1, axis, sgn, a0, a1, soot=()):
        nonlocal k
        L = a1 - a0; n = max(1, round(L / 0.36)); cuts = [a0] + sorted(a0 + L * (i + r.uniform(-0.22, 0.22)) / n for i in range(1, n)) + [a1]
        for s0, s1 in zip(cuts, cuts[1:]):
            mid, ln = (s0 + s1) / 2, s1 - s0 + 0.04
            th = W - IN + 0.02; off = sgn * ((W + IN) / 2 + r.uniform(-0.012, 0.012))
            hz = z1 - z0 + 0.03 + r.uniform(-0.015, 0.02); zc = (z0 + z1) / 2 + r.uniform(-0.008, 0.008)
            c = (mid, off, zc) if axis == 'x' else (off, mid, zc)
            size = (ln, th, hz) if axis == 'x' else (th, ln, hz)
            sootish = any(lo <= mid <= hi for lo, hi in soot)
            stone(f'stone_{k}', c, size, seed=100 + k, mat=SOOTSTONE if sootish else STONE, yaw=(0.0 if axis == 'x' else 0.0), extra=4, round_=0.3); k += 1
    for ci, (z0, z1) in enumerate(courses):
        long_x = ci % 2 == 0   # (alternate courses: the front/back stones or the side stones run past the corners)
        ax0, ax1 = (-W, W) if long_x else (-IN, IN)
        ay0, ay1 = (-IN, IN) if long_x else (-W, W)
        course_side(ci, z0, z1, 'x', -1, ax0, ax1)                                      # back
        for sg in (-1, 1): course_side(ci, z0, z1, 'y', sg, ay0, ay1)                   # sides
        if ci < 2:                                                                       # front, round the mouth
            course_side(ci, z0, z1, 'x', 1, ax0, -0.21, soot=((-0.4, -0.2),) if ci == 1 else ())
            course_side(ci, z0, z1, 'x', 1, 0.21, ax1, soot=((0.2, 0.4),) if ci == 1 else ())
        else:
            course_side(ci, z0, z1, 'x', 1, ax0, -0.33); course_side(ci, z0, z1, 'x', 1, 0.33, ax1)
    stone('lintel', (0, (W + IN) / 2 + 0.004, 0.375), (0.74, W - IN + 0.02, 0.13), seed=90, mat=SOOTSTONE, yaw=0.0, extra=3, round_=0.2)
    box('slab', (-W + 0.02, -W + 0.02, 0.43), (W - 0.02, W - 0.02, 0.49), STONE, jitter=0.006, seed=91)
    # (inside the mouth: the ash bed and two charred logs)
    blob('ash', (0, 0.1, 0.0), (0.3, 0.3, 0.035), SOOT, seed=92, subdiv=1, lumps=0.15)
    pole('log_0', (-0.2, 0.42, 0.05), (0.08, -0.15, 0.04), 0.045, SOOT, seed=93, bend=0.01)
    pole('log_1', (0.22, 0.38, 0.05), (-0.06, -0.2, 0.08), 0.04, SOOT, seed=94, bend=0.01)
    # (the hut: sills on the slab, corner posts, wall plates, plank walls)
    P, T = 0.6, 0.09   # (the walls' outer face; the posts' size)
    for s in (-1, 1):
        box(f'sill_x{s}', (-P, s * P - (T if s > 0 else 0), 0.49), (P, s * P + (0 if s > 0 else T), 0.57), WOOD, (1, 0, 0), jitter=0.004, seed=95 + s)
        box(f'sill_y{s}', (s * P - (T if s > 0 else 0), -P + T, 0.49), (s * P + (0 if s > 0 else T), P - T, 0.57), WOOD, (0, 1, 0), jitter=0.004, seed=97 + s)
        box(f'plate_{s}', (s * P - (T if s > 0 else 0) - 0.01, -P - 0.01, 1.36), (s * P + (0 if s > 0 else T) + 0.01, P + 0.01, 1.44), WOOD, (0, 1, 0), jitter=0.004, seed=99 + s)
        box(f'plate_y{s}', (-P + T, s * P - (T if s > 0 else 0), 1.36), (P - T, s * P + (0 if s > 0 else T), 1.43), WOOD, (1, 0, 0), jitter=0.003, seed=103 + s)
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        x0, y0 = (sx * P - T, sy * P - T) if sx > 0 or sy > 0 else (0, 0)
        lo = (sx * P - (T if sx > 0 else 0), sy * P - (T if sy > 0 else 0), 0.49)
        box(f'post_{i}', lo, (lo[0] + T, lo[1] + T, 1.44), WOOD, (0, 0, 1), bevel=0.01, jitter=0.004, seed=110 + i)
    wall = P - T / 2 + 0.005   # (the planks' middle plane, between the posts' faces)
    def wall_planks(tag, axis, sgn, z0=0.57, z1=1.36):
        n = 4; span = 2 * (P - T)
        for j in range(n):
            a0 = -(P - T) + span * j / n - (0.012 if j else 0.0); a1 = -(P - T) + span * (j + 1) / n + (0.012 if j < n - 1 else 0.0)
            o_ = sgn * (0.007 if j % 2 else -0.004); hw = 0.014 if j % 2 else 0.016
            if axis == 'x': lo, hi = (a0, sgn * wall + o_ - hw, z0), (a1, sgn * wall + o_ + hw, z1)
            else: lo, hi = (sgn * wall + o_ - hw, a0, z0), (sgn * wall + o_ + hw, a1, z1)
            box(f'wall_{tag}{j}', lo, hi, WOOD, (0, 0, 1), jitter=0.003, seed=120 + hash(tag) % 50 + j)
    wall_planks('b', 'x', -1); wall_planks('f', 'x', 1); wall_planks('l', 'y', -1); wall_planks('r', 'y', 1)
    # (the hatch: two boards and two battens, a frame round it, a turn-button latch; iron strap hinges laid on it)
    fy = wall + 0.024
    for k_, (x0, x1, z0, z1) in enumerate(((-0.24, 0.24, 0.87, 0.92), (-0.24, 0.24, 1.26, 1.31), (-0.24, -0.19, 0.92, 1.26), (0.19, 0.24, 0.92, 1.26))):
        box(f'frame_{k_}', (x0, fy, z0), (x1, fy + 0.02, z1), WOOD, (1, 0, 0) if k_ < 2 else (0, 0, 1), jitter=0.002, seed=140 + k_)
    for k_, (x0, x1) in enumerate(((-0.185, 0.0), (0.004, 0.185))):
        box(f'hatch_{k_}', (x0, fy + 0.002, 0.925), (x1, fy + 0.026, 1.255), WOOD, (0, 0, 1), jitter=0.002, seed=145 + k_)
    for k_, z in enumerate((0.97, 1.21)):
        box(f'batten_{k_}', (-0.17, fy + 0.026, z - 0.022), (0.17, fy + 0.046, z + 0.022), WOOD, (1, 0, 0), jitter=0.002, seed=148 + k_)
    box('latch', (0.2, fy + 0.02, 1.07), (0.235, fy + 0.04, 1.13), WOOD, (0, 0, 1), jitter=0.002, seed=150)
    for k_, z in enumerate((0.97, 1.21)):
        box(f'decal_hinge_{k_}', (-0.185, fy + 0.0465, z - 0.012), (-0.06, fy + 0.0485, z + 0.012), IRON)
    # (soot up the front from the mouth, on the sill (the lintel stone is sooty too))
    patch('decal_smudge_sill', (0, P + 0.007, 0.53), (0, 1, 0), 0.3, 0.036, SMUDGE, seed=151, lift=0.0, n=12, boxy=0.5)
    patch('decal_soot_sill', (0, P + 0.009, 0.52), (0, 1, 0), 0.17, 0.026, SOOT, seed=152, lift=0.0, n=12, boxy=0.5)
    # (the roof: two slopes of planks running down the pitch, open along the ridge; gables of planks clipped to the pitch)
    EX, EZ, RX, RZ, OV = SH['EX'], SH['EZ'], SH['RX'], SH['RZ'], SH['OV']
    slope = math.atan2(RZ - EZ, EX - RX); th = 0.03
    nr = 6
    for s in (-1, 1):
        nrm = V((s * math.sin(slope), 0, math.cos(slope)))
        for j in range(nr):
            y0 = -OV + 2 * OV * j / nr - (0.012 if j else 0); y1 = -OV + 2 * OV * (j + 1) / nr + (0.012 if j < nr - 1 else 0); yc = (y0 + y1) / 2
            dz = 0.012 if j % 2 else 0.0
            a = V((s * EX, yc, EZ)) + nrm * (th / 2 + dz); b = V((s * RX, yc, RZ)) + nrm * (th / 2 + dz)
            plank(f'roof_{s}{j}', a + (a - b).normalized() * 0.02 * r.random(), b, (0, 1, 0), y1 - y0, th, seed=160 + j + (10 if s > 0 else 0), chamfer=0.006, nrm=nrm)
            # (soot along the ridge gap, on the plank's top: a dark edge and a fainter smudge below it)
            up_ = (b - a).normalized(); top_ = b + nrm * (th / 2 + 0.004)
            hw = (y1 - y0) / 2 + 0.01
            patch(f'decal_smudge_ridge{s}{j}', top_ - up_ * 0.07, nrm, 0.07, hw, SMUDGE, seed=170 + j + (10 if s > 0 else 0), up=(0, 1, 0), lift=0.0, n=12, boxy=0.25)
            patch(f'decal_soot_ridge{s}{j}', top_ - up_ * 0.025, nrm, 0.03, hw, SOOT, seed=185 + j + (10 if s > 0 else 0), up=(0, 1, 0), lift=0.0, n=12, boxy=0.25)
    for gs in (-1, 1):   # (the gables: vertical boards clipped to the pitch, in the walls' plane)
        n = 5; gy = gs * wall
        for j in range(n):
            x0 = -(P - 0.005) + 2 * (P - 0.005) * j / n + 0.003; x1 = -(P - 0.005) + 2 * (P - 0.005) * (j + 1) / n - 0.003
            top = lambda x: roofline(max(abs(x), RX), EX, EZ, RX, RZ) - 0.004
            pts = [(x, gy + t, z) for x in (x0, x1) for t in (-0.016, 0.016) for z in (1.425, top(x))]
            if x0 < 0 < x1: pts += [(0, gy + t, top(0.0)) for t in (-0.016, 0.016)]
            hull(f'gable_{gs}{j}', pts, WOOD, (0, 0, 1))
        # (barge boards along the gable's edges, a hair outside the roof planks' ends)
        for s in (-1, 1):
            nrm = V((s * math.sin(slope), 0, math.cos(slope)))
            a = V((s * (EX + 0.01), gs * (OV + 0.012), EZ - 0.02)) + nrm * 0.02; b = V((s * RX, gs * (OV + 0.012), RZ)) + nrm * 0.02
            plank(f'barge_{gs}{s}', a, b, nrm, 0.11, 0.022, seed=180 + s + gs * 3, nrm=(0, 1, 0))
    # (the ridge cap over the gap, on three blocks each side)
    for s in (-1, 1):
        cs = math.atan2(0.05, 0.17); cn = V((s * math.sin(cs), 0, math.cos(cs)))
        plank(f'cap_{s}', V((s * 0.18, 0, 1.825)) + cn * 0.012, V((0, 0, 1.875)) + cn * 0.012, (0, 1, 0), 2 * OV + 0.08, 0.024, seed=190 + s, chamfer=0.005, nrm=cn)
        for q, y in enumerate((-0.66, 0.0, 0.66)):
            top_roof = roofline(0.115, EX, EZ, RX, RZ) + th / math.cos(slope)
            box(f'capblock_{s}{q}', (s * 0.115 - 0.03, y - 0.04, top_roof - 0.02), (s * 0.115 + 0.03, y + 0.04, 1.855 - (0.115 * 0.05 / 0.17)), WOOD, (0, 1, 0), jitter=0.002, seed=195 + q)
    finish('smokehouse', [(0, 4.2, 1.6, 0.95), (35, 4.0, 2.4, 0.9), (90, 4.0, 1.4, 0.95), (-140, 4.2, 2.2, 0.9), (10, 2.2, 0.7, 0.3), (60, 3.6, 3.6, 1.2)])

# ---------------- the salt pan ----------------
# A shallow iron pan (outer rim 1.0 x 0.7 at z 0.5, its floor 0.94 x 0.64 at z 0.38, a rolled lip, a ring handle at each end) on a ring
# of hearth stones (two layers, sunk 0.06 into the ground for standing in water), the front left open for the fire: charred sticks on a
# bed of ash, their ends out of the gap. A white-grey salt crust in the pan, raked up at its -x end.
SP = dict(X=0.5, Y=0.35, BX=0.47, BY=0.32, Z0=0.38, Z1=0.5, T=0.012)
def saltpan():
    fresh(); r = random.Random(71); X, Y, BX, BY, Z0, Z1, T = (SP[k] for k in ('X', 'Y', 'BX', 'BY', 'Z0', 'Z1', 'T'))
    # (the hearth: stones round an oval, a gap at the front (+y))
    k = 0
    for layer, (z0, z1, ax, by, n, ph) in enumerate(((-0.06, 0.2, 0.44, 0.30, 11, 0.0), (0.18, Z0 + 0.004, 0.42, 0.28, 11, 0.5))):
        for i in range(n):
            a = 2 * math.pi * (i + ph) / n + r.uniform(-0.05, 0.05)
            if abs(math.atan2(math.sin(a - math.pi / 2), math.cos(a - math.pi / 2))) < 0.5: continue   # (the fire's gap)
            c = V((math.cos(a) * ax, math.sin(a) * by, (z0 + z1) / 2))
            tang = math.atan2(math.cos(a) * by, -math.sin(a) * ax)
            sz = (r.uniform(0.3, 0.36), r.uniform(0.22, 0.27), z1 - z0 + 0.05)
            cobble(f'stone_{k}', c, sz, seed=200 + k, mat=SOOTSTONE if (layer == 1 and math.sin(a) > 0.3) else STONE, yaw=tang); k += 1
    # (the pan: a tray of plate, its walls flared; four rings of corners, outside and in)
    bm = bmesh.new()
    def rect(hx, hy, z, cut=0.05):
        return [bm.verts.new((sx * (hx - (cut if j else 0)), sy * (hy - (0 if j else cut)), z)) for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1)) for j in ((0, 1) if sx * sy > 0 else (1, 0))]
    ob_, ot_ = rect(BX, BY, Z0), rect(X, Y, Z1)
    it_, ib_ = rect(X - T, Y - T, Z1, 0.045), rect(BX - T, BY - T, Z0 + T, 0.045)
    rings_ = [ib_, ob_, ot_, it_, ib_]
    n = len(ob_)
    for A, B in zip(rings_[:-1], rings_[1:]):
        if A is ib_ and B is ob_: continue
        for j in range(n): bm.faces.new((A[j], A[(j + 1) % n], B[(j + 1) % n], B[j]))
    bm.faces.new(list(reversed(ob_))); bm.faces.new(ib_)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    link('pan', bm, IRON)
    pts = [V((sx * (X - (0.05 if j else 0)), sy * (Y - (0 if j else 0.05)), Z1)) for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1)) for j in ((0, 1) if sx * sy > 0 else (1, 0))]
    tube('lip', pts, 0.011, IRON, cyclic=True, res=1)
    for s in (-1, 1):
        ring(f'handle_ring{s}', (s * (X + 0.045), 0, Z1 - 0.03), 0.045, 0.008, (0, 1, 0), IRON, segs=8, rsegs=4)
        box(f'handle_strap{s}', (s * X - 0.012 - (0 if s > 0 else 0.01), -0.03, Z1 - 0.075), (s * X + 0.012 + (0.01 if s > 0 else 0), 0.03, Z1 - 0.01), IRON)
    # (the salt crust: a lumpy sheet over the pan's floor, raked up to the -x end, climbing the walls a little)
    bm = bmesh.new(); nx, ny = 9, 6; top, bot = [], []
    zf = Z0 + T
    for i in range(nx + 1):
        for j in range(ny + 1):
            u, v = i / nx * 2 - 1, j / ny * 2 - 1
            edge = max(abs(u), abs(v))
            h = 0.022 + 0.035 * max(0.0, -u - 0.2) ** 1.5 + r.uniform(-0.005, 0.006) + 0.01 * edge ** 4
            zt = zf + h
            f = (zt - Z0) / (Z1 - Z0); hx = BX - T + (X - BX) * f - 0.004; hy = BY - T + (Y - BY) * f - 0.004
            def clip(x, y, hx, hy, cut=0.055):
                ex = abs(x) + abs(y) - (hx + hy - cut)
                return (x - math.copysign(ex / 2, x), y - math.copysign(ex / 2, y)) if ex > 0 else (x, y)
            top.append(bm.verts.new((*clip(u * hx, v * hy, hx, hy), zt)))
            bot.append(bm.verts.new((*clip(u * (BX - T - 0.004), v * (BY - T - 0.004), BX - T - 0.004, BY - T - 0.004), zf - 0.002)))
    idx = lambda i, j: i * (ny + 1) + j
    for i in range(nx):
        for j in range(ny):
            bm.faces.new((top[idx(i, j)], top[idx(i + 1, j)], top[idx(i + 1, j + 1)], top[idx(i, j + 1)]))
            bm.faces.new((bot[idx(i, j)], bot[idx(i, j + 1)], bot[idx(i + 1, j + 1)], bot[idx(i + 1, j)]))
    border = [idx(i, 0) for i in range(nx)] + [idx(nx, j) for j in range(ny)] + [idx(i, ny) for i in range(nx, 0, -1)] + [idx(0, j) for j in range(ny, 0, -1)]
    for a, b in zip(border, border[1:] + border[:1]): bm.faces.new((top[a], top[b], bot[b], bot[a]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    link('crust', bm, SALT)
    # (the fire: an ash bed under the pan, charred sticks from under it out through the gap)
    blob('ash', (0, 0.05, -0.01), (0.3, 0.22, 0.04), SOOT, seed=210, subdiv=1, lumps=0.15)
    for k_, (a, b, rr) in enumerate((((-0.12, -0.05, 0.05), (0.05, 0.5, 0.06), 0.03), ((0.1, -0.02, 0.05), (-0.02, 0.52, 0.09), 0.028),
                                    ((0.02, 0.0, 0.08), (0.14, 0.46, 0.05), 0.025), ((-0.05, 0.1, 0.11), (-0.13, 0.44, 0.06), 0.022))):
        pole(f'stick_{k_}', a, b, rr, SOOT, seed=220 + k_, bend=0.01)
    finish('saltpan', [(0, 2.6, 1.2, 0.3), (35, 2.4, 1.6, 0.3), (90, 2.4, 0.9, 0.3), (-140, 2.6, 1.4, 0.3), (5, 1.4, 0.5, 0.15)])

# ---------------- the salt barrel ----------------
# A stout oak barrel (16 staves, radius 0.25 at the heads to 0.275 at the belly, z 0..0.8; four iron hoops) without its head: coarse salt
# heaped inside to about z 0.76, a wooden scoop stood in it; the lid (three boards and two battens, radius 0.24) leans on its front right.
BA = dict(R0=0.25, R1=0.275, H=0.8, TH=0.024)
def saltbarrel():
    fresh(); r = random.Random(81); R0, R1, H, TH = (BA[k] for k in ('R0', 'R1', 'H', 'TH')); n = 16
    Rl = lambda z: R0 + (R1 - R0) * (1 - abs(z - H / 2) / (H / 2))
    def band(name, z, ri, ro, h, n, ph):
        # an iron hoop: a band of n flats on the staves' corners, open on the inside (that face is in the staves)
        bm = bmesh.new(); rows = []
        for rr, zz in ((ri, z - h / 2), (ro, z - h / 2), (ro, z + h / 2), (ri, z + h / 2)):
            rows.append([bm.verts.new((math.cos(ph + 2 * math.pi * j / n) * rr, math.sin(ph + 2 * math.pi * j / n) * rr, zz)) for j in range(n)])
        for A, B in zip(rows, rows[1:]):
            for j in range(n): bm.faces.new((A[j], A[(j + 1) % n], B[(j + 1) % n], B[j]))
        bm.normal_update()
        for f in bm.faces:   # (facing out of the barrel, up or down)
            c = f.calc_center_median(); out = V((c.x, c.y, 0)).normalized() * 0.5 + V((0, 0, c.z - z)) * 40
            if f.normal.dot(out) < 0: f.normal_flip()
        return link(name, bm, IRON)
    ph = math.pi / 2 - math.pi / n
    for k in range(n):
        a0, a1 = ph + 2 * math.pi * (k + 0.01) / n, ph + 2 * math.pi * (k + 0.99) / n
        top = H + r.uniform(-0.008, 0.004); pts = []
        for z in (0.0, H / 2, top):
            for rr in (Rl(z), Rl(z) - TH):
                for a in (a0, a1): pts.append(V((math.cos(a) * rr, math.sin(a) * rr, z)))
        hull(f'stave_{k}', pts, WOOD, (0, 0, 1))
    for k, z in enumerate((0.06, 0.24, 0.56, 0.74)):
        band(f'hoop_{k}', z, Rl(z) - 0.002, Rl(z) + 0.006, 0.022, n, ph)
    # (the salt: a heap inside, its foot hidden down in the barrel; the scoop stood in it, leaning on the back-left rim)
    Ri = (R0 - TH) * 0.975
    revolve('heap', (0, 0, 0), [(0, H - 0.2), (Ri, H - 0.2), (Ri, H - 0.05), (Ri * 0.7, H - 0.035), (Ri * 0.35, H - 0.02), (0, H - 0.015)], SALT, segs=12, wobble=0.0, seed=82)
    for k in range(4):
        a = r.uniform(0, 2 * math.pi); d = r.uniform(0.04, 0.16)
        stone(f'heap_lump{k}', (math.cos(a) * d, math.sin(a) * d, H - 0.03), (0.035, 0.03, 0.022), seed=300 + k, mat=SALT, extra=1, round_=0.3)
    foot = V((-0.06, -0.05, H - 0.04)); tip = V((-0.2, -0.17, H + 0.17)); d = (tip - foot).normalized()
    revolve('scoop', (0, 0, 0), [(0, -0.04), (0.042, -0.026), (0.048, 0.0), (0.038, 0.0), (0, -0.028)], WOOD, segs=8, wobble=0.02, seed=83, grain=(1, 0, 0))
    sc = bpy.data.objects['scoop']; turned(sc, Matrix.Rotation(math.radians(-35), 3, 'X') @ Matrix.Rotation(math.radians(20), 3, 'Y'))
    sc.data.transform(Matrix.Translation(foot)); sc['grain'] = tuple(sc['grain'])
    pole('scoop_handle', foot + d * 0.035, tip, 0.012, seed=84, bend=0.003)
    # (the lid leaning on the barrel: built lying in x/z (its face +y), turned up about its foot)
    lid_r, lid_t = 0.24, 0.024; parts_ = []
    for j, (x0, x1) in enumerate(((-lid_r, -0.083), (-0.079, 0.079), (0.083, lid_r))):
        pts = []
        for q in range(6):
            x = x0 + (x1 - x0) * q / 5; zh = math.sqrt(max(0.0, lid_r ** 2 - x * x)) * 0.995
            for z in (-zh, zh):
                for y in (-lid_t / 2, lid_t / 2): pts.append(V((x, y + r.uniform(-0.002, 0.002), z)))
        parts_.append(hull(f'lid_{j}', pts, WOOD, (0, 0, 1)))
    for j, z in enumerate((-0.11, 0.11)):
        w = math.sqrt(lid_r ** 2 - z * z) - 0.03
        parts_.append(box(f'lidbatten_{j}', (-w, -lid_t / 2 - 0.022, z - 0.03), (w, -lid_t / 2 + 0.001, z + 0.03), WOOD, (1, 0, 0), jitter=0.002, seed=85 + j))
    yaw = math.radians(35); lean = math.radians(18)
    out = V((math.sin(yaw), math.cos(yaw), 0))
    # (the face toward the barrel is the batten side (-y); its foot on the ground, its top edge against the staves)
    hb = Rl(lid_r * 2 * math.cos(lean) - 0.02) + 0.008
    A = hb + 2 * lid_r * math.sin(lean) + lid_t / 2 + 0.022 * math.cos(lean)
    m = Matrix.Rotation(-yaw, 3, 'Z') @ Matrix.Rotation(lean, 3, 'X')
    for o in parts_:
        o.data.transform(Matrix.Translation(V((0, 0, lid_r))))   # (its foot at the origin)
        turned(o, m)
        o.data.transform(Matrix.Translation(out * A + V((0, 0, 0.004))))
    finish('saltbarrel', [(0, 2.2, 1.3, 0.45), (35, 2.0, 1.5, 0.45), (90, 2.0, 1.0, 0.4), (-140, 2.2, 1.4, 0.45), (20, 1.1, 1.4, 0.6)])

# ---------------- the stockfish rack (hjell) ----------------
# Two A-frame trestles of rough poles at x -1.1 and 1.1, their legs from feet at y +-0.52 crossing at y 0, z 1.65, up to z 1.92; a tie across
# each at z 1.22 under the lower bar. Two long bars along x: the lower at z 1.30 resting on the ties, the upper at z 1.75 in the crotches. A
# diagonal brace on the back. Rope lashings at the crossings. The fish are the game's stockfish, hung by the C# side (README).
RK = dict(X=1.1, FOOT=0.52, CROSS=1.65, TOP=1.92, LOW=1.30, HIGH=1.75, BAR_R=0.042, LEG_R=0.048, BAR_X=1.32)
def dryingrack():
    fresh(); X, FOOT, CROSS, TOP, LOW, HIGH, BR, LR, BX = (RK[k] for k in ('X', 'FOOT', 'CROSS', 'TOP', 'LOW', 'HIGH', 'BAR_R', 'LEG_R', 'BAR_X'))
    for i, s in enumerate((-1, 1)):
        x = s * X
        # (each leg goes from its foot through the crossing at y 0 on up; the two side by side in x so they cross without meeting)
        zt = LOW - BR - 0.035
        for j, (fy, dx) in enumerate(((FOOT, -LR * 0.9), (-FOOT, LR * 0.9))):
            top_y = -fy * (TOP - CROSS) / CROSS
            a, b = V((x + dx + s * 0.02, fy, -0.03)), V((x + dx, top_y, TOP))
            pole(f'leg_{i}{j}', a, b, LR, seed=400 + 2 * i + j, bend=0.01)
            # (a lashing round the leg where the tie crosses it)
            at = a.lerp(b, (zt - a.z) / (b.z - a.z))
            ring(f'rope_tie{i}{j}', at, LR + 0.006, 0.008, b - a, ROPE, segs=8, rsegs=3)
        yl = FOOT * (1 - (LOW - 0.08) / CROSS) + 0.07
        pole(f'tie_{i}', (x, -yl, zt), (x, yl, zt), 0.035, seed=410 + i, bend=0.004)
        # (the lashing round both legs where they cross: an oval, tight on them)
        o = ring(f'rope_cross{i}', (x, 0, CROSS + 0.02), 0.098, 0.009, (0, 0, 1), ROPE, segs=8, rsegs=3, squash=1.4)
        o.data.transform(Matrix.Translation(V((x, 0, 0))) @ Matrix.Diagonal((1, 0.6, 1, 1)) @ Matrix.Translation(V((-x, 0, 0))))
    pole('bar_low', (-BX, 0, LOW), (BX, 0.0, LOW + 0.01), BR, seed=420, bend=0.012)
    pole('bar_high', (-BX, 0, HIGH), (BX, 0.0, HIGH - 0.008), BR, seed=421, bend=0.012)
    # (the brace: from low on one trestle's back leg up to the other's, lying on their backs)
    def back_leg(s, z):
        x = s * X; a, b = V((x + LR * 0.9 + s * 0.02, -FOOT, -0.03)), V((x + LR * 0.9, FOOT * (TOP - CROSS) / CROSS, TOP))
        return a.lerp(b, (z - a.z) / (b.z - a.z)) + V((0, -(LR + 0.032 - 0.012), 0))
    lo_, hi_ = back_leg(-1, 0.2), back_leg(1, 1.12)
    d_ = (hi_ - lo_).normalized()
    pole('brace', lo_ - d_ * 0.06, hi_ + d_ * 0.06, 0.032, seed=422, bend=0.006)
    finish('dryingrack', [(0, 5.0, 1.6, 1.0), (35, 4.6, 2.2, 1.0), (90, 4.0, 1.4, 1.0), (-140, 4.8, 2.0, 1.0)])

# ---------------- preserved foods (items: lie about the origin) ----------------
def smokedmeat():
    # a smoked haunch, 0.22 long along x: the thick end at -x, the shank and a knuckle of bone at +x; tied twice with string, and a loop
    fresh()
    prof = lambda t: 0.012 + 0.052 * math.sin(math.pi * min(1.0, 0.18 + t * 0.95)) ** 0.7 * (1 - 0.55 * t)
    o = lathe('haunch', (-0.105, 0, 0), (0.08, 0, 0), prof, SMOKED, segs=8, flute=0.05, lobes=3, seed=1, step=0.026)
    for v in o.data.vertices: v.co.z *= 0.72; v.co.y *= 1.05
    cyl('bone', (0.07, 0, 0.004), (0.112, 0, 0.006), 0.012, 0.011, HORN, segs=6)
    blob('bone_knob', (0.115, 0, 0.006), (0.016, 0.018, 0.015), HORN, seed=2, subdiv=1, lumps=0.05)
    for k, x in enumerate((0.0, 0.05)):
        rr = prof((x + 0.105) / 0.185)
        ring(f'decal_string_{k}', (x, 0, 0), rr * 1.0 + 0.002, 0.0035, (1, 0, 0), ROPE, segs=10, rsegs=3, squash=1.0)
        bpy.data.objects[f'decal_string_{k}'].data.transform(Matrix.Diagonal((1, 1.05, 0.72, 1)))
    finish('smokedmeat', [(30, 0.55, 0.32, 0), (-60, 0.5, 0.3, 0)])

def saltmeat():
    # a slab of salted meat (0.2 x 0.12, 0.045 thick), a rind of fat along its back edge, coarse salt crystals on top and flecks between
    fresh(); r = random.Random(5)
    stone('meat', (0, -0.012, 0), (0.2, 0.1, 0.046), seed=1, mat=MEAT, yaw=0.0, extra=6, round_=0.28)
    stone('fat', (0, 0.04, -0.004), (0.188, 0.022, 0.036), seed=2, mat=FAT, yaw=0.0, extra=3, round_=0.3)
    for k in range(4):
        x, y = r.uniform(-0.075, 0.075), r.uniform(-0.045, 0.02)
        stone(f'crystal_{k}', (x, y, 0.024), (0.013, 0.012, 0.01), seed=10 + k, mat=SALT, extra=0, round_=0.05)
    for k in range(10):
        x, y = r.uniform(-0.085, 0.085), r.uniform(-0.05, 0.05)
        patch(f'decal_salt{k}', (x, y, 0.0225 + (0.002 if y > 0.033 else 0)), (0, 0, 1), r.uniform(0.006, 0.012), r.uniform(0.005, 0.01), SALT, seed=20 + k, up=(1, 0, 0), lift=0.0, n=6)
    finish('saltmeat', [(30, 0.5, 0.3, 0), (-60, 0.45, 0.28, 0)])

SF = dict(L=0.35, W=0.12)
def stockfish():
    # a split, dried fish lying flat (back up): 0.35 long along x, 0.12 wide; the cut neck at -x (its cut face pale), the tail fin at +x.
    # Stiff and a little warped. Very cheap: up to eight hang on a rack
    fresh(); L, W = SF['L'], SF['W']
    # (stations along x: half-width, thickness)
    st = [(-0.175, 0.052, 0.016), (-0.12, 0.06, 0.017), (-0.04, 0.056, 0.015), (0.04, 0.042, 0.012), (0.1, 0.024, 0.009), (0.13, 0.013, 0.006)]
    bm = bmesh.new(); rows = []
    for x, hw, t in st:
        bend = 0.012 * math.sin((x + 0.175) / 0.35 * math.pi)
        sec = [(-hw, 0.0), (0.0, t * 0.6), (hw, 0.0), (0.0, -t * 0.4)]
        rows.append([bm.verts.new((x, y, z + bend)) for y, z in sec])
    for A, B in zip(rows, rows[1:]):
        for k in range(4): bm.faces.new((A[k], A[(k + 1) % 4], B[(k + 1) % 4], B[k]))
    cut = bm.faces.new(list(reversed(rows[0]))); bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    cut.material_index = 1
    link_m('fish', bm, [FISH, FAT])
    for k, s in enumerate((-1, 1)):
        hull(f'fish_tail{k}', [(0.122, s * 0.001, 0.005), (0.122, s * 0.014, 0.005), (0.122, s * 0.001, 0.0), (0.122, s * 0.014, 0.0),
                               (0.18, s * 0.062, 0.006), (0.163, s * 0.006, 0.006), (0.18, s * 0.062, 0.003), (0.163, s * 0.006, 0.003)], FISH, (1, s * 0.6, 0))
    finish('stockfish', [(30, 0.7, 0.35, 0), (-60, 0.6, 0.3, 0)])

def salt():
    # a small wooden bowl (0.13 across) heaped with coarse white sea salt
    fresh(); r = random.Random(9); z0, H, Rr, t = -0.03, 0.04, 0.065, 0.008
    revolve('bowl', (0, 0, 0), [(0, z0), (0.034, z0), (0.04, z0 + 0.006), (0.058, z0 + 0.018), (Rr, z0 + H), (Rr - t, z0 + H + 0.002),
                                (0.05, z0 + 0.02), (0.03, z0 + t), (0, z0 + t)], WOOD, segs=12, wobble=0.012, seed=5, grain=(1, 0, 0))
    zs = z0 + H - 0.004
    revolve('heap', (0, 0, 0), [(0, z0 + t + 0.002), (0.03, z0 + t + 0.002), (0.05, z0 + 0.022), (Rr - t + 0.002, zs), (0.042, zs + 0.016), (0.02, zs + 0.028), (0, zs + 0.032)],
            SALT, segs=10, wobble=0.06, seed=6)
    for k in range(5):
        a = r.uniform(0, 2 * math.pi); d = r.uniform(0.0, 0.035)
        zz = zs + 0.032 * (1 - (d / 0.06) ** 1.5)
        stone(f'heap_lump{k}', (math.cos(a) * d, math.sin(a) * d, zz), (0.012, 0.011, 0.008), seed=30 + k, mat=SALT, extra=0, round_=0.1)
    finish('salt', [(30, 0.5, 0.32, 0), (-60, 0.45, 0.3, 0)])

# ================ baking: the bake oven, flatbread, dough ================
# ---------------- the bake oven ----------------
# A round dry-stone plinth (three courses on a ring, radius 0.53..0.73, z 0..0.41) with the fire hole in its front (x -0.16..0.16, z 0..0.28,
# under a lintel stone), a stone hearth slab on it (radius 0.72, top z 0.462). On the slab a hand-plastered clay beehive dome (outer radius
# 0.64 at its foot, crown z 1.06; walls 0.1 thick, soot inside), a ring of stones round its foot, an arched mouth at the front (x -0.2..0.2,
# floor z 0.462, crown z 0.80) framed by stones; a soot-stone floor inside where the loaves sit. A flat stone ledge out in front of the mouth,
# a short clay smoke vent on the dome's back. A wooden peel leans on the dome's right, a few logs lie on the ground at its left.
OVN = dict(PR=0.63, PT=0.2, HR=0.72, HZ=0.462, Z0=0.455, DR=0.64, DH=0.605, T=0.1, MW=0.2, MS=0.6, FX=0.16, FZ=0.28)
def dome_r(z, inner=False):
    # the dome's radius at height z (its profile r = R cos(phi)^0.75, z = Z0 + H sin(phi): steep at the foot, a beehive)
    R, H = (OVN['DR'] - OVN['T'], OVN['DH'] - OVN['T']) if inner else (OVN['DR'], OVN['DH'])
    s = max(0.0, min(1.0, (z - OVN['Z0']) / H)); return R * math.sqrt(max(0.0, 1 - s * s)) ** 0.75
def dome_noise(a, phi):
    # the plaster's unevenness (metres, outward)
    return 0.011 * math.sin(3 * a + 1.3) * math.cos(1.6 * phi) + 0.007 * math.sin(5 * a + 3.1 * phi + 0.4) + 0.004 * math.sin(9 * a - 2 * phi)
def dome_at(x, z):
    # the dome's outer surface in front (+y) at (x, z): the point and its outward normal
    r = dome_r(z); y = math.sqrt(max(1e-6, r * r - x * x)); rho = math.hypot(x, y)
    dr = (dome_r(z + 0.005) - dome_r(z - 0.005)) / 0.01
    return V((x, y, z)), V((x / rho, y / rho, -dr)).normalized()
def framed(o, centre, normal, radial):
    # a part built about the origin (x along, y out of the wall, z 'radial') turned so its y is normal and its z is radial, moved to centre
    Y = V(normal).normalized(); Z = V(radial); Z = (Z - Y * Z.dot(Y)).normalized(); X = Y.cross(Z)
    m = Matrix((X, Y, Z)).transposed().to_4x4()
    o.data.transform(Matrix.Translation(V(centre)) @ m); return o
def bakeoven():
    fresh(); r = random.Random(91)
    PR, PT, HR, HZ, Z0, DR, DH, T, MW, MS, FX, FZ = (OVN[k] for k in ('PR', 'PT', 'HR', 'HZ', 'Z0', 'DR', 'DH', 'T', 'MW', 'MS', 'FX', 'FZ'))
    # (the plinth: three courses of stones round a ring, the joints staggered; the two lower courses open at the front for the fire hole,
    # the top one round a lintel stone over it)
    front = math.pi / 2; k = 0
    gap = math.asin((FX + 0.01) / PR); lgap = math.asin(0.33 / PR)
    for ci, (z0, z1) in enumerate(((-0.03, 0.145), (0.135, 0.29), (0.28, 0.41))):
        g = gap if ci < 2 else lgap
        a0, a1 = front + g, front + 2 * math.pi - g
        n = 9 if ci != 1 else 8
        cuts = [a0] + sorted(a0 + (a1 - a0) * (i + r.uniform(-0.2, 0.2) + (0.5 if ci == 1 else 0) * 0) / n for i in range(1, n)) + [a1]
        for s0, s1 in zip(cuts, cuts[1:]):
            mid = (s0 + s1) / 2; ln = PR * (s1 - s0) + 0.05
            rc = PR + r.uniform(-0.012, 0.012); zc = (z0 + z1) / 2 + r.uniform(-0.008, 0.008)
            hz = z1 - z0 + 0.03 + r.uniform(-0.015, 0.02)
            soot = ci == 1 and abs(math.cos(mid)) < 0.5 and math.sin(mid) > 0
            stone(f'stone_{k}', (math.cos(mid) * rc, math.sin(mid) * rc, zc), (ln, PT + r.uniform(-0.01, 0.02), hz), seed=500 + k,
                  mat=SOOTSTONE if soot else STONE, yaw=mid + math.pi / 2, extra=3, round_=0.3); k += 1
    stone('lintel', (0, PR + 0.005, 0.345), (0.7, PT + 0.02, 0.13), seed=590, mat=SOOTSTONE, yaw=0.0, extra=3, round_=0.2)
    # (inside the plinth: the ash bed and a charred log, seen through the fire hole)
    blob('ash', (0, 0.12, 0.0), (0.32, 0.32, 0.035), SOOT, seed=592, subdiv=1, lumps=0.15)
    pole('log_0', (-0.1, 0.66, 0.05), (0.1, -0.2, 0.05), 0.045, SOOT, seed=593, bend=0.01)
    # (the hearth slab on the plinth, and the floor inside the dome, sooty)
    revolve('hearth', (0, 0, 0), [(0, 0.4), (HR - 0.02, 0.4), (HR + 0.01, 0.425), (HR - 0.01, HZ), (0, HZ)], STONE, segs=12, wobble=0.03, seed=595)
    revolve('floor', (0, 0, 0), [(0, HZ - 0.004), (DR - T - 0.03, HZ - 0.004), (DR - T - 0.035, HZ + 0.006), (0, HZ + 0.006)], SOOTSTONE, segs=12, wobble=0.01, seed=596)
    # (the dome: a shell, clay outside and soot inside, open at its foot only where the mouth is cut)
    segs, nr = 16, 6
    bm = bmesh.new(); rows = {}
    for side, (R, H) in (('o', (DR, DH)), ('i', (DR - T, DH - T))):
        rr = []
        for kk in range(nr):
            phi = (math.pi / 2) * kk / nr * (1 - 0.05 * (kk / nr))
            row = []
            for j in range(segs):
                a = 2 * math.pi * j / segs
                rad = R * math.cos(phi) ** 0.75 + (dome_noise(a, phi) if side == 'o' else 0.0)
                z = Z0 + H * math.sin(phi) + (dome_noise(a + 1.0, phi) * 0.4 * math.sin(phi) if side == 'o' else 0.0)
                row.append(bm.verts.new((math.cos(a) * rad, math.sin(a) * rad, z)))
            rr.append(row)
        apex = bm.verts.new((0, 0, Z0 + H + (0.004 if side == 'o' else 0.0)))
        rows[side] = (rr, apex)
    for side, mi in (('o', 0), ('i', 1)):
        rr, apex = rows[side]
        for kk in range(nr - 1):
            for j in range(segs):
                jj = (j + 1) % segs
                bm.faces.new((rr[kk][j], rr[kk][jj], rr[kk + 1][jj], rr[kk + 1][j])).material_index = mi
        for j in range(segs):
            bm.faces.new((rr[-1][j], rr[-1][(j + 1) % segs], apex)).material_index = mi
    ro, ri = rows['o'][0][0], rows['i'][0][0]
    for j in range(segs):
        jj = (j + 1) % segs
        bm.faces.new((ro[j], ri[j], ri[jj], ro[jj])).material_index = 0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    dome = link_m('dome', bm, [CLAY, SOOT])
    # (the mouth: an arch cut through the wall, a prism of its outline along y)
    bm = bmesh.new(); outline = [V((MW, 0, Z0 - 0.06)), V((MW, 0, MS))]
    outline += [V((math.cos(a) * MW, 0, MS + math.sin(a) * MW)) for a in (math.pi * q / 10 for q in range(1, 10))]
    outline += [V((-MW, 0, MS)), V((-MW, 0, Z0 - 0.06))]
    fr = [bm.verts.new(p + V((0, 0.3, 0))) for p in outline]; bk = [bm.verts.new(p + V((0, 1.0, 0))) for p in outline]
    n_ = len(outline)
    for q in range(n_): bm.faces.new((fr[q], fr[(q + 1) % n_], bk[(q + 1) % n_], bk[q]))
    bm.faces.new(list(reversed(fr))); bm.faces.new(bk)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    cutter = link('cutter', bm, CLAY)
    mod = dome.modifiers.new('mouth', 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.object = cutter; mod.solver = 'EXACT'
    try: mod.material_mode = 'TRANSFER'
    except Exception: pass
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(dome.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    dome.modifiers.clear(); old = dome.data; dome.data = me; bpy.data.meshes.remove(old)
    bpy.data.objects.remove(cutter)
    # (stones framing the mouth: seven round the arch, a jamb each side under its spring; sooty over it)
    for q in range(7):
        a = math.pi * (q + 0.5) / 7; rad = V((math.cos(a), 0, math.sin(a)))
        c2 = V((math.cos(a) * (MW + 0.055), 0, MS + math.sin(a) * (MW + 0.055)))
        p, nrm = dome_at(c2.x, c2.z)
        o = stone(f'arch_{q}', (0, 0, 0), (0.125 + r.uniform(-0.01, 0.01), 0.12, 0.1), seed=600 + q, mat=SOOTSTONE if 2 <= q <= 4 else STONE, yaw=0.0, extra=3, round_=0.3)
        framed(o, p - nrm * 0.035, nrm, rad)
    for q, s_ in enumerate((-1, 1)):
        p, nrm = dome_at(s_ * (MW + 0.065), Z0 + 0.075)
        o = stone(f'jamb_{q}', (0, 0, 0), (0.13, 0.13, 0.17), seed=610 + q, mat=STONE, yaw=0.0, extra=3, round_=0.25)
        framed(o, p - nrm * 0.03 + V((0, 0, 0.012)), nrm, (0, 0, 1))
    # (soot up the clay over the arch)
    p, nrm = dome_at(0.0, 0.95)
    patch('decal_smudge_mouth', p, nrm, 0.17, 0.08, SMUDGE, seed=620, lift=0.002, n=12, boxy=0.6)
    p, nrm = dome_at(0.0, 0.915)
    patch('decal_soot_mouth', p, nrm, 0.1, 0.04, SOOT, seed=621, lift=0.003, n=10, boxy=0.7)
    # (a ring of stones round the dome's foot, holding the clay; none by the mouth)
    nf = 10
    for q in range(nf):
        a = 2 * math.pi * (q + 0.5) / nf + front + r.uniform(-0.05, 0.05)
        if abs(math.atan2(math.sin(a - front), math.cos(a - front))) < 0.6: continue
        rc = DR + 0.02 + r.uniform(-0.01, 0.01)
        stone(f'foot_{q}', (math.cos(a) * rc, math.sin(a) * rc, HZ + 0.045), (0.26, 0.12, 0.12 + r.uniform(-0.01, 0.02)), seed=630 + q,
              mat=STONE, yaw=a + math.pi / 2, extra=3, round_=0.3)
    # (the ledge in front of the mouth: a flat stone on the lintel, out over the fire hole)
    stone('ledge', (0, 0.77, 0.437), (0.7, 0.32, 0.07), seed=640, mat=STONE, yaw=0.0, extra=4, round_=0.12)
    # (the smoke vent: a short clay pot-chimney on the dome's back, soot in its throat)
    vz = 0.98; vr = dome_r(vz)
    lathe('vent', (0, -vr + 0.03, vz - 0.1), (0, -vr + 0.035, vz + 0.085), lambda t: 0.078 - 0.016 * t + 0.01 * (t > 0.92), CLAY, segs=8, grain=False, step=0.09)
    patch('decal_vent', (0, -vr + 0.035, vz + 0.085), (0, 0, 1), 0.047, 0.047, SOOT, seed=650, up=(0, 1, 0), lift=0.002, n=10)
    # (a wooden peel leaning on the dome at its right front: its foot on the ground, the handle laid against the clay)
    az = math.radians(28); rf = 1.06; foot = V((math.cos(az) * rf, math.sin(az) * rf, 0.0))
    best = max(math.atan2(z, rf - (dome_r(z) + 0.035)) for z in (Z0 + 0.01 * i for i in range(1, 60)))
    best = max(best, math.atan2(HZ + 0.01, rf - (HR + 0.04)))
    out_ = V((math.cos(az), math.sin(az), 0)); tang = V((-math.sin(az), math.cos(az), 0))
    d = (-out_ * math.cos(best) + V((0, 0, math.sin(best)))).normalized(); nb = tang.cross(d).normalized()
    pts = []
    for u, w in ((0.0, 0.11), (0.06, 0.125), (0.22, 0.11), (0.3, 0.045)):
        for s_ in (-1, 1):
            for t in (-0.007, 0.007): pts.append(foot + d * u + tang * (s_ * w) + nb * t + V((0, 0, 0.004)))
    hull('peel_blade', pts, WOOD, tuple(d))
    pole('peel_handle', foot + d * 0.28 + V((0, 0, 0.004)), foot + d * 1.25, 0.017, seed=660, bend=0.004)
    # (firewood on the ground at the left: two logs and one on them)
    for q, (x, z, y0, y1) in enumerate(((-0.87, 0.05, -0.2, 0.26), (-0.98, 0.05, -0.17, 0.3), (-0.925, 0.135, -0.22, 0.24))):
        pole(f'log_wood{q}', (x + 0.02, y0, z), (x - 0.02, y1, z + 0.005), 0.05, WOOD, seed=670 + q, bend=0.006)
    finish('bakeoven', [(0, 3.0, 1.5, 0.55), (35, 2.8, 1.8, 0.5), (90, 2.8, 1.2, 0.5), (-140, 3.0, 1.7, 0.5), (5, 1.6, 0.75, 0.5), (-40, 2.6, 2.4, 0.4)])

# ---------------- items ----------------
def flatbread():
    # two round barley flatbreads (flatbrod), 0.22 across, 1 cm thick, edges a little uneven, stacked a touch askew; toasted spots on top
    fresh(); r = random.Random(13)
    def disc(name, cx, cy, cz, R, h, seed):
        prof = [(0, cz - h / 2), (R * 0.9, cz - h / 2), (R, cz - h * 0.05), (R * 0.93, cz + h / 2), (R * 0.45, cz + h / 2 + 0.0012), (0, cz + h / 2 + 0.0016)]
        return revolve(name, (cx, cy, 0), prof, BREAD, segs=12, wobble=0.06, seed=seed)
    disc('bread_0', 0.0, 0.0, -0.0045, 0.11, 0.01, 1)
    disc('bread_1', 0.012, -0.01, 0.0048, 0.106, 0.0095, 2)
    for k in range(16):
        a = r.uniform(0, 2 * math.pi); dd = math.sqrt(r.uniform(0.0, 1.0)) * 0.085
        x, y = 0.012 + math.cos(a) * dd, -0.01 + math.sin(a) * dd
        patch(f'decal_toast{k}', (x, y, 0.0048 + 0.0047 + 0.0012 * (1 - dd / 0.1)), (0, 0, 1), r.uniform(0.005, 0.013), r.uniform(0.004, 0.01), TOAST,
              seed=20 + k, up=(math.cos(a), math.sin(a), 0), lift=0.0005, n=7)
    for k, a in enumerate((2.0, 3.3)):   # (and on the lower one's edge, where it shows)
        x, y = math.cos(a) * 0.095, math.sin(a) * 0.095
        patch(f'decal_toastb{k}', (x, y, -0.0045 + 0.005), (0, 0, 1), 0.009, 0.007, TOAST, seed=40 + k, up=(math.cos(a), math.sin(a), 0), lift=0.0005, n=7)
    finish('flatbread', [(30, 0.6, 0.35, 0), (-60, 0.55, 0.3, 0)])

def flatbreaddough():
    # a round of raw dough, 0.18 across, 2 cm thick with a soft rounded edge, flour dusted on it
    fresh(); r = random.Random(17)
    revolve('dough', (0, 0, 0), [(0, -0.01), (0.08, -0.01), (0.088, -0.0075), (0.091, -0.001), (0.087, 0.006), (0.076, 0.0098), (0.04, 0.0108), (0, 0.011)],
            DOUGH, segs=12, wobble=0.025, seed=3)
    for k in range(6):
        a = r.uniform(0, 2 * math.pi); dd = math.sqrt(r.uniform(0.0, 1.0)) * 0.06
        patch(f'decal_flour{k}', (math.cos(a) * dd, math.sin(a) * dd, 0.0108 - 0.0012 * dd / 0.06), (0, 0, 1), r.uniform(0.012, 0.022), r.uniform(0.01, 0.018), FLOUR,
              seed=30 + k, up=(math.cos(a), math.sin(a), 0), lift=0.0005, n=8)
    finish('flatbreaddough', [(30, 0.5, 0.32, 0), (-60, 0.45, 0.3, 0)])

def spoiled():
    # food gone off: a low, sagging lump of rot (0.14 across, 0.06 high), a bone end poking out of it, sickly mould patches over it
    fresh(); r = random.Random(23)
    for name, c, size, seed, sub in (('lump_0', (0.0, 0.0, 0.0), (0.068, 0.058, 0.034), 1, 2), ('lump_1', (-0.035, 0.03, -0.006), (0.035, 0.03, 0.022), 2, 1)):
        o = blob(name, c, size, ROT, seed=seed, subdiv=sub, lumps=0.16)
        for v in o.data.vertices: v.co.z = max(v.co.z, -0.024 + 0.004 * v.co.x / 0.07)   # (slumped flat where it lies)
    cyl('knuckle_shaft', (0.03, -0.012, 0.004), (0.074, -0.032, 0.018), 0.011, 0.01, HORN, segs=6)
    blob('knuckle_end', (0.079, -0.034, 0.02), (0.017, 0.018, 0.014), HORN, seed=3, subdiv=1, lumps=0.05)
    for k in range(10):
        a = r.uniform(0, 2 * math.pi); dd = math.sqrt(r.uniform(0.0, 1.0)) * 0.055
        x, y = math.cos(a) * dd, math.sin(a) * dd
        z = 0.034 * math.sqrt(max(0.0, 1 - (x / 0.068) ** 2 - (y / 0.058) ** 2)) - 0.003
        nrm = V((x / 0.068 ** 2, y / 0.058 ** 2, max(z, 0.002) / 0.034 ** 2)).normalized()
        patch(f'decal_mould{k}', (x, y, z), nrm, r.uniform(0.013, 0.024), r.uniform(0.011, 0.02), MOULD, seed=40 + k, up=(math.cos(a), math.sin(a), 0), lift=0.002, n=8)
    finish('spoiled', [(30, 0.5, 0.32, 0), (-60, 0.45, 0.3, 0)])

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
        (0.94, 0.93, 0.88), (0.2, 0.22, 0.45), (0.55, 0.45, 0.3), (0.75, 0.68, 0.55), (0.85, 0.86, 0.7),
        (0.11, 0.1, 0.09), (0.3, 0.28, 0.26), (0.9, 0.9, 0.87), (0.3, 0.17, 0.09), (0.72, 0.33, 0.3), (0.86, 0.78, 0.68), (0.74, 0.68, 0.56), (0.3, 0.24, 0.19),
        (0.56, 0.48, 0.4), (0.8, 0.62, 0.36), (0.6, 0.4, 0.2), (0.9, 0.84, 0.7), (0.95, 0.92, 0.84), (0.4, 0.34, 0.27), (0.55, 0.62, 0.45)]
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

KINDS = ('churn', 'press', 'butter', 'cheese', 'skyr', 'smokehouse', 'saltpan', 'saltbarrel', 'dryingrack', 'smokedmeat', 'saltmeat', 'stockfish', 'salt', 'bakeoven', 'flatbread', 'flatbreaddough', 'spoiled')
which = [a for a in sys.argv if a in KINDS] or list(KINDS)
for w in which: globals()[w]()
