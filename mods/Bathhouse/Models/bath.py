# The bathhouse's looks (Bathhouse 0.1.0): a sauna stove (kiuas), a tiered bench (lauteet), a water bucket and ladle (kiulu and kauha),
# a cold plunge tub and a bath locker, made from scratch.
# Run: blender -b --python ~/.cache/bathhouse/bath.py -- [render] [stove|bench|bucket|plunge|locker]   (writes ~/.cache/bathhouse/<name>.json.gz;
# previews with "render")
# Blender frame: z up, front +y (the game's +z, where a player stands to use the piece). Written in the game's frame (x, z, y): per corner
# position/normal/uv (uvs in metres; wood's grain runs along v), triangles per material: 0 wood, 1 stone, 2 soot, 3 iron, 4 water,
# 5 ember, 6 rope.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/bathhouse/'
WOOD, STONE, SOOT, IRON, WATER, EMBER, ROPE = range(7)
NAMES = ('wood', 'stone', 'soot', 'iron', 'water', 'ember', 'rope')
SMOOTH = ('rock', 'stone', 'ember', 'rope', 'cup', 'mound', 'handle', 'brand', 'knot', 'peg', 'cloth', 'staple')  # (smoothed over all but the sharpest edges)
rng = random.Random(11)
mats = []

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

def blob(name, centre, size, mat, seed=0, subdiv=2, lumps=0.12):
    # a lumpy rounded lump (a field stone in the stove's wall, an ember)
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    r = random.Random(seed)
    for v in bm.verts:
        v.co = Vector((v.co.x * size.x, v.co.y * size.y, v.co.z * size.z)) * (1 + r.uniform(-lumps, lumps)) + centre
    return link(name, bm, mat)

def rock(name, centre, radius, seed):
    # a well-rounded stone off a beach or a stream bed: an egg, flattened, one end fuller than the other, swelling and dipping gently
    # (no sharp lumps), turned its own way
    r = random.Random(seed)
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=7, radius=1.0)
    sx, sy, sz = r.uniform(1.0, 1.4), r.uniform(0.75, 1.0), r.uniform(0.55, 0.8)
    egg = r.uniform(-0.18, 0.18)
    waves = [(Vector((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1))).normalized() * r.uniform(1.5, 2.8), r.uniform(0, 6.3), r.uniform(0.05, 0.1)) for _ in range(3)]
    turn = Matrix.Rotation(r.uniform(0, 2 * math.pi), 4, 'Z') @ Matrix.Rotation(r.uniform(-0.35, 0.35), 4, 'X') @ Matrix.Rotation(r.uniform(-0.3, 0.3), 4, 'Y')
    for v in bm.verts:
        d = v.co.normalized()
        k = (1 + sum(a * math.sin(d.dot(w) + p) for w, p, a in waves)) * (1 + egg * d.x)
        v.co = turn @ (Vector((d.x * sx, d.y * sy, d.z * sz)) * k * radius) + centre
    return link(name, bm, STONE)

def smooth_line(pts, rounds=2):
    # a polyline's corners cut off (Chaikin), its ends kept
    for _ in range(rounds):
        out = [pts[0]]
        for a, b in zip(pts, pts[1:]): out += [a.lerp(b, 0.25), a.lerp(b, 0.75)]
        pts = out + [pts[-1]]
    return pts

def box(name, lo, hi, mat, grain=None, bevel=0.0, jitter=0.0, seed=0):
    # a box from corner lo to corner hi, its edges chamfered, its corners pushed about a little (a hewn plank, a rough slab)
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    lo, hi = Vector(lo), Vector(hi)
    bmesh.ops.scale(bm, vec=hi - lo, verts=bm.verts); bmesh.ops.translate(bm, vec=(lo + hi) / 2, verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES', profile=0.5)
    if jitter:
        r = random.Random(seed)
        for v in bm.verts: v.co += Vector((r.uniform(-jitter, jitter), r.uniform(-jitter, jitter), r.uniform(-jitter, jitter)))
    return link(name, bm, mat, grain)

def outline_slab(name, pts2, z0, thick, mat, grain=None):
    # a flat slab (a hearth stone, a bed of embers) from an outline of (x, y)
    bm = bmesh.new()
    bottom = [bm.verts.new((x, y, z0)) for x, y in pts2]
    top = [bm.verts.new((x, y, z0 + thick)) for x, y in pts2]
    bm.faces.new(top); bm.faces.new(list(reversed(bottom)))
    n = len(pts2)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.normal_update()
    return link(name, bm, mat, grain)

def cut(ob, cutter, op='DIFFERENCE'):
    # a boolean, applied at once (the cut's faces take the cutter's material), the cutter thrown away
    mod = ob.modifiers.new('cut', 'BOOLEAN'); mod.object = cutter; mod.operation = op; mod.solver = 'EXACT'; mod.material_mode = 'TRANSFER'
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    ob.modifiers.clear(); ob.data = me
    bpy.data.objects.remove(cutter)
    return ob

def staves(name, r0, r1, thick, z0, z1, n, seed, gap=0.0025, tall=None, uneven=0.003):
    # a ring of n staves standing from z0 to z1, the ring's outer radius r0 at its foot and r1 at its top, each stave its own width and
    # its top a few mm off the others'; tall(stave, from angle, to angle) gives ear staves their extra height. Returns the ring and its staves' middle angles
    r = random.Random(seed)
    w = [r.uniform(0.85, 1.15) for _ in range(n)]
    spans = [x / sum(w) * 2 * math.pi for x in w]
    rad = lambda z: r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    bm = bmesh.new(); mids = []
    a = -spans[0] / 2
    g = gap / r0
    for i, span in enumerate(spans):
        a0, a1 = a + g / 2, a + span - g / 2
        mids.append(a + span / 2); a += span
        top = z1 + r.uniform(-uneven, uneven) + (tall(i, a0, a1) if tall else 0.0)
        ring = []
        for z in (z0, top):
            ro = rad(z); ri = ro - thick
            ring.append(([bm.verts.new((math.cos(t) * ro, math.sin(t) * ro, z)) for t in (a0, (a0 + a1) / 2, a1)],
                         [bm.verts.new((math.cos(t) * ri, math.sin(t) * ri, z)) for t in (a0, (a0 + a1) / 2, a1)]))
        (ob_, ib), (ot, it) = ring
        for k in range(2):
            bm.faces.new((ob_[k], ob_[k + 1], ot[k + 1], ot[k]))
            bm.faces.new((ib[k + 1], ib[k], it[k], it[k + 1]))
            bm.faces.new((ot[k], ot[k + 1], it[k + 1], it[k]))
            bm.faces.new((ob_[k + 1], ob_[k], ib[k], ib[k + 1]))
        bm.faces.new((ob_[0], ot[0], it[0], ib[0])); bm.faces.new((ob_[2], ib[2], it[2], ot[2]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, WOOD, (0, 0, 1)), mids, rad

def band(name, rad, z0, z1, proud, segs=48):
    # a forged iron hoop round staves (rad(z) their outer radius), hugging them as they taper
    bm = bmesh.new(); rings = []
    for z in (z0, z1):
        ro, ri = rad(z) + proud, rad(z) - 0.002
        rings.append(([bm.verts.new((math.cos(2 * math.pi * i / segs) * ro, math.sin(2 * math.pi * i / segs) * ro, z)) for i in range(segs)],
                      [bm.verts.new((math.cos(2 * math.pi * i / segs) * ri, math.sin(2 * math.pi * i / segs) * ri, z)) for i in range(segs)]))
    (ob_, ib), (ot, it) = rings
    for i in range(segs):
        j = (i + 1) % segs
        bm.faces.new((ob_[i], ob_[j], ot[j], ot[i])); bm.faces.new((ib[j], ib[i], it[i], it[j]))
        bm.faces.new((ot[i], ot[j], it[j], it[i])); bm.faces.new((ob_[j], ob_[i], ib[i], ib[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return link(name, bm, IRON)

def disc(name, radius, z, mat, thick=0.0, segs=40, grain=None):
    # a round floor set in the staves (thick), or a water surface (flat, facing up)
    bm = bmesh.new()
    if thick:
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=thick)
        bmesh.ops.translate(bm, vec=(0, 0, z + thick / 2), verts=bm.verts)
    else:
        f = bm.faces.new([bm.verts.new((math.cos(2 * math.pi * i / segs) * radius, math.sin(2 * math.pi * i / segs) * radius, z)) for i in range(segs)])
        f.normal_update()
        if f.normal.z < 0: f.normal_flip()
    return link(name, bm, mat, grain)

# ---------------- the stove ----------------
ST_W, ST_D, ST_H = 0.9, 0.8, 0.75   # the body (x, game z, height)
FB_W, FB_LO, FB_HI, FB_IN = 0.36, 0.12, 0.44, 0.35   # the firebox opening (width, bottom, top, depth in from the front)
def stove():
    fresh()
    # a box of field stones laid in courses in mortar (the core between them is the mortar), a firebox in its front lined with soot, a
    # bed of embers and two charred brands in it, a sooty lintel over it, a heap of rounded stones on top (on a sooty mound, so the
    # gaps between them look deep) and a flat soot-black hearth stone in front. No chimney: a smoke sauna
    hx, hy = ST_W / 2, ST_D / 2
    core = box('core', (-hx + 0.03, -hy + 0.03, 0.0), (hx - 0.03, hy - 0.03, ST_H - 0.02), STONE)
    cutter = box('cutter', (-FB_W / 2, hy - FB_IN, FB_LO), (FB_W / 2, hy + 0.2, FB_HI), SOOT)
    cut(core, cutter)
    r = random.Random(3); k = [0]
    def stone(cx, cy, cz, sx, sy, sz):
        k[0] += 1
        return blob(f'stone{k[0]}', Vector((cx, cy, cz)), Vector((sx, sy, sz)), STONE, seed=100 + k[0], lumps=0.1)
    def course_row(face, a0, a1, z0, z1):
        # stones along one face between a0 and a1 (along the face), from z0 to z1, their outer faces at the face's plane
        a = a0
        while a < a1 - 0.03:
            l = r.uniform(0.16, 0.28)
            if a1 - (a + l) < 0.1: l = a1 - a
            mid, half = a + l / 2, l / 2 * 1.04
            dep = r.uniform(0.1, 0.14); h = (z1 - z0) / 2 * 1.08
            out = (hy if face in ('front', 'back') else hx) - dep / 2 * 1.06
            if face == 'front': stone(mid, out, (z0 + z1) / 2, half, dep / 2, h)
            if face == 'back': stone(mid, -out, (z0 + z1) / 2, half, dep / 2, h)
            if face == 'right': stone(out, mid, (z0 + z1) / 2, dep / 2, half, h)
            if face == 'left': stone(-out, mid, (z0 + z1) / 2, dep / 2, half, h)
            a += l
    def courses():
        zs, z = [0.0], 0.0
        while z < ST_H - 0.2: z += r.uniform(0.12, 0.18); zs.append(z)
        zs.append(ST_H); return list(zip(zs, zs[1:]))
    # the front: a sill course under the opening, two courses beside it, the lintel's course, a top course
    edge = FB_W / 2 + 0.008
    for z0, z1 in ((0.0, FB_LO), (FB_HI, 0.57), (0.57, ST_H)):
        if z0 == FB_HI:
            course_row('front', -hx, -0.31, z0, z1); course_row('front', 0.31, hx, z0, z1)
        else: course_row('front', -hx, hx, z0, z1)
    for z0, z1 in ((FB_LO, 0.28), (0.28, FB_HI)):
        course_row('front', -hx, -edge, z0, z1); course_row('front', edge, hx, z0, z1)
    box('lintel', (-0.31, hy - 0.13, FB_HI), (0.31, hy - 0.002, 0.565), SOOT, bevel=0.018, jitter=0.006, seed=5)
    for face in ('back', 'left', 'right'):
        for z0, z1 in courses():
            if face == 'back': course_row(face, -hx, hx, z0, z1)
            else: course_row(face, -hy + 0.06, hy - 0.06, z0, z1)
    # the firebox: a bed of embers on its floor (top at about 0.14), lumps of coal glowing on it, two charred brands
    bed = [(math.cos(2 * math.pi * i / 16) * (FB_W / 2 - 0.02) * (1 + r.uniform(-0.05, 0.03)),
            hy - FB_IN / 2 - 0.02 + math.sin(2 * math.pi * i / 16) * (FB_IN / 2 - 0.03)) for i in range(16)]
    outline_slab('ember_bed', bed, FB_LO, 0.02, EMBER)
    for i in range(11):
        x, y = r.uniform(-0.13, 0.13), r.uniform(hy - FB_IN + 0.05, hy - 0.06)
        s = r.uniform(0.018, 0.032)
        blob(f'ember{i}', Vector((x, y, FB_LO + 0.02)), Vector((s, s * r.uniform(0.8, 1.2), s * 0.55)), EMBER, seed=200 + i)
    for i, (x, t) in enumerate(((-0.06, 0.25), (0.07, -0.3))):
        a = Vector((x - 0.11 * math.sin(t), hy - FB_IN + 0.05, FB_LO + 0.045)); b = Vector((x + 0.11 * math.sin(t), hy - 0.05, FB_LO + 0.04))
        tube(f'brand{i}', [a, a.lerp(b, 0.5) + Vector((0, 0, 0.004)), b], 0.026 - 0.004 * i, SOOT)
    # the heap: a sooty mound, round stones dropped onto it one by one (each resting on the mound or on stones already there), until
    # the heap stands about 1.05 high
    mound_r = (hx - 0.07, hy - 0.07)
    blob('mound', Vector((0, 0, ST_H - 0.02)), Vector((mound_r[0], mound_r[1], 0.19)), SOOT, seed=9, subdiv=3, lumps=0.04)
    def ground(x, y):
        q = 1 - (x / mound_r[0]) ** 2 - (y / mound_r[1]) ** 2
        return max(ST_H, ST_H - 0.02 + 0.19 * math.sqrt(q) if q > 0 else 0)
    placed = []
    def settle(x, y, rr):
        z = ground(x, y) + rr * 0.45
        for px, py, pz, pr in placed:
            d2 = (x - px) ** 2 + (y - py) ** 2; reach = (rr + pr) * 0.85
            if d2 < reach ** 2: z = max(z, pz + math.sqrt(reach ** 2 - d2) * 0.7)
        return z
    for i in range(70):
        # (each stone tried at a dozen spots, laid at the lowest: the heap fills in as gravel would)
        rr = r.uniform(0.05, 0.1) if i < 45 else r.uniform(0.05, 0.07)
        spots = [(r.uniform(-hx + rr * 1.3, hx - rr * 1.3), r.uniform(-hy + rr * 1.3, hy - rr * 1.3)) for _ in range(12)]
        x, y, z = min(((x, y, settle(x, y, rr)) for x, y in spots), key=lambda t: t[2])
        if z + rr * 0.75 > 1.06: continue
        placed.append((x, y, z, rr))
    print('heap stones', len(placed))
    for i, (x, y, z, rr) in enumerate(placed): rock(f'rock{i}', Vector((x, y, z)), rr, 300 + i)
    # the hearth: a flat stone in front, from the stove's face out to 0.6, 4 cm thick
    hearth = []
    for i in range(20):
        t = 2 * math.pi * i / 20
        x, y = math.cos(t) * 0.33, math.sin(t) * 0.1
        hearth.append((x * (1 + r.uniform(-0.06, 0.04)), max(-0.1, min(0.1, y * (1 + r.uniform(-0.05, 0.08)))) + 0.5))
    outline_slab('hearth', hearth, 0.0, 0.04, SOOT)
    export('stove')
    if 'render' in sys.argv: preview('stove', [(0, 2.6, 1.3, 0.5), (35, 2.6, 1.6, 0.5), (150, 2.6, 1.4, 0.5), (0, 1.3, 0.45, 0.28), (60, 1.6, 2.1, 0.8)])

# ---------------- the bench ----------------
B_LEN, B_UP, B_LOW = 2.0, 0.85, 0.42                 # length, the seat's top, the footrest's top
B_BACK, B_MID, B_FRONT = -0.55, -0.05, 0.45          # (game z) the back edge, where the tiers meet, the front edge
FRAMES = (-0.9, 0.0, 0.9)
def bench():
    fresh()
    # two tiers of planks lengthwise (9-10 cm wide, 1 cm apart), each tier on a cross bearer at every frame; each frame a back post and a
    # middle post up to the seat's bearer (the footrest's bearer tenoned into the middle post) and a short front post, low rails along
    # the back and the front tying the frames together
    r = random.Random(5)
    def tier(name, y0, y1, top):
        n = 5
        w = [r.uniform(0.088, 0.096) for _ in range(n)]
        s = sum(w); w = [x * (y1 - y0 - 0.01 * (n - 1)) / s for x in w]
        y = y0
        for i, wi in enumerate(w):
            t = r.uniform(0.032, 0.037)
            box(f'{name}{i}', (-B_LEN / 2 + r.uniform(0, 0.004), y, top - t), (B_LEN / 2 - r.uniform(0, 0.004), y + wi, top), WOOD, (1, 0, 0), bevel=0.004)
            y += wi + 0.01
        return top - 0.037
    up_under = tier('seat', B_BACK, B_MID, B_UP)
    low_under = tier('step', B_MID, B_FRONT, B_LOW)
    for i, x in enumerate(FRAMES):
        dx = r.uniform(-0.004, 0.004)
        box(f'bearer_up{i}', (x - 0.03, B_BACK, up_under - 0.08), (x + 0.03, B_MID, up_under), WOOD, (0, 1, 0), bevel=0.005)
        box(f'bearer_low{i}', (x - 0.03, B_MID - 0.07, low_under - 0.08), (x + 0.03, B_FRONT, low_under), WOOD, (0, 1, 0), bevel=0.005)
        box(f'post_back{i}', (x - 0.035 + dx, B_BACK, 0.0), (x + 0.035 + dx, B_BACK + 0.07, up_under - 0.08), WOOD, (0, 0, 1), bevel=0.006)
        box(f'post_mid{i}', (x - 0.035 + dx, B_MID - 0.07, 0.0), (x + 0.035 + dx, B_MID, up_under - 0.08), WOOD, (0, 0, 1), bevel=0.006)
        box(f'post_front{i}', (x - 0.035 + dx, B_FRONT - 0.07, 0.0), (x + 0.035 + dx, B_FRONT, low_under - 0.08), WOOD, (0, 0, 1), bevel=0.006)
    for name, y0 in (('rail_back', B_BACK + 0.07), ('rail_mid', B_MID - 0.07), ('rail_front', B_FRONT - 0.07)):
        # (along the inner faces of the posts: 4 cm thick, 7 cm tall, 8 cm off the floor)
        ya = y0 if name == 'rail_back' else y0 - 0.04
        box(name, (-0.95, ya, 0.08), (0.95, ya + 0.04, 0.15), WOOD, (1, 0, 0), bevel=0.005)
    export('bench')
    if 'render' in sys.argv: preview('bench', [(0, 3.6, 1.4, 0.45), (35, 3.6, 1.8, 0.45), (90, 2.6, 0.9, 0.45), (160, 3.4, 1.6, 0.4)])

# ---------------- the bucket ----------------
BK_R0, BK_R1, BK_H, BK_T, BK_WATER = 0.135, 0.15, 0.28, 0.015, 0.23
def bucket():
    fresh()
    # thirteen staves widening a little to the top, two of them standing taller as ears with a hole through each for a rope handle
    # knotted outside, a bottom set in the staves, two iron hoops, water up to 0.23, a long ladle leaning in it on the front rim
    n = 13
    ring, mids, rad = staves('staves', BK_R0, BK_R1, BK_T, 0.0, BK_H, n, seed=4, gap=0.0018,
                             tall=lambda i, a0, a1: 0.065 if i == 0 or a0 <= math.pi <= a1 else 0.0)  # (stave 0 straddles +x)
    hole_z = BK_H + 0.035
    cutter = tube('cutter', [Vector((-0.25, 0, hole_z)), Vector((0.25, 0, hole_z))], 0.011, WOOD)
    cut(ring, cutter)
    disc('floor', BK_R0 - BK_T + 0.004, 0.012, WOOD, thick=0.022, grain=(1, 0, 0))
    band('band_low', rad, 0.035, 0.057, 0.003)
    band('band_high', rad, 0.2, 0.222, 0.003)
    disc('water', rad(BK_WATER) - BK_T + 0.004, BK_WATER, WATER)
    # the rope: knotted outside each ear, through its hole, up in an arch
    side = lambda sx: [Vector((sx * 0.172, 0, hole_z)), Vector((sx * 0.118, 0, hole_z)), Vector((sx * 0.1, 0, hole_z + 0.05)),
                       Vector((sx * 0.06, 0, hole_z + 0.1)), Vector((sx * 0.02, 0, hole_z + 0.118))]
    tube('rope', smooth_line(side(1) + list(reversed(side(-1)))), 0.0075, ROPE)
    for sx in (-1, 1):
        # (a stopper knot: two turns of the rope round its end, outside the ear)
        for k, t in enumerate((0.35, -0.5)):
            c = Vector((sx * (0.167 + 0.008 * k), 0, hole_z))
            tube(f'knot{sx}_{k}', [c + Matrix.Rotation(t, 3, 'X') @ Vector((0, math.cos(a) * 0.007, math.sin(a) * 0.007)) for a in [j * math.pi / 5 for j in range(10)]],
                 0.0075, ROPE, cyclic=True, res=1)
        tube(f'rope_end{sx}', [Vector((sx * 0.17, 0.002, hole_z - 0.004)), Vector((sx * 0.19, 0.006, hole_z - 0.016))], 0.006, ROPE, res=1)
    # the ladle: an 8 cm cup on a half-metre handle, leaning toward the front, the handle resting on the rim's inner edge
    th = math.radians(50)
    d, nrm = Vector((0, math.cos(th), math.sin(th))), Vector((0, -math.sin(th), math.cos(th)))
    rest = Vector((0, rad(BK_H) - BK_T, BK_H)) + nrm * 0.0115
    cup_at = rest - d * 0.29
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=0.04)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z > 0.001], context='VERTS')
    rot = Vector((0, 0, 1)).rotation_difference(nrm).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation(cup_at) @ rot, verts=bm.verts)
    cup = link('cup', bm, WOOD)
    m = cup.modifiers.new('solid', 'SOLIDIFY'); m.thickness = 0.005; m.offset = 0
    a = cup_at + d * 0.036
    tube('handle', [a, a + d * 0.25 + nrm * 0.002, a + d * 0.5 + nrm * 0.008], 0.011, WOOD, res=2)
    export('bucket')
    if 'render' in sys.argv: preview('bucket', [(0, 1.3, 0.7, 0.2), (40, 1.2, 0.9, 0.2), (100, 1.1, 0.5, 0.2), (200, 1.2, 0.8, 0.2), (20, 0.7, 1.0, 0.15), (95, 0.45, 0.36, 0.3)])

# ---------------- the plunge tub ----------------
PT_R0, PT_R1, PT_H, PT_T, PT_WATER = 0.742, 0.725, 0.95, 0.05, 0.80
def plunge():
    fresh()
    # forty staves narrowing a little to the top, a floor of boards set in them, two broad iron hoops, water up to 0.80 filling it, a
    # small step outside at the front
    ring, mids, rad = staves('staves', PT_R0, PT_R1, PT_T, 0.0, PT_H, 40, seed=8, gap=0.003, uneven=0.004)
    disc('floor', PT_R0 - PT_T + 0.008, 0.04, WOOD, thick=0.04, segs=48, grain=(1, 0, 0))
    band('band_low', rad, 0.13, 0.18, 0.006)
    band('band_high', rad, 0.74, 0.79, 0.006)
    disc('water', rad(PT_WATER) - PT_T + 0.004, PT_WATER, WATER, segs=48)
    # the step: two planks across on two upright side boards, a brace between them; its back against the hoops
    r = random.Random(2)
    y0, y1 = PT_R0 + 0.008, PT_R0 + 0.008 + 0.3
    for i in range(2):
        box(f'step{i}', (-0.3 + r.uniform(0, 0.004), y0 + i * 0.155, 0.36), (0.3 - r.uniform(0, 0.004), y0 + i * 0.155 + 0.145, 0.40), WOOD, (1, 0, 0), bevel=0.004)
    for sx in (-1, 1):
        box(f'side{sx}', (sx * 0.25 - 0.02, y0 + 0.01, 0.0), (sx * 0.25 + 0.02, y1 - 0.01, 0.36), WOOD, (0, 0, 1), bevel=0.004)
    box('brace', (-0.23, (y0 + y1) / 2 - 0.02, 0.2), (0.23, (y0 + y1) / 2 + 0.02, 0.27), WOOD, (1, 0, 0), bevel=0.004)
    export('plunge')
    if 'render' in sys.argv: preview('plunge', [(0, 4.0, 1.9, 0.45), (35, 4.0, 2.4, 0.45), (150, 4.0, 2.0, 0.45), (0, 2.4, 2.9, 0.4)])

# ---------------- the bath locker ----------------
LK_W, LK_D, LK_H, LK_TALL = 1.1, 0.55, 0.5, 1.6       # the chest (x, game z, the lid's top), the back's top
LK_PEG_Y, LK_PEGS = 1.33, (-0.32, 0.0, 0.32)          # the pegs' height, their x
def drape(name, x0, x1, path, slope=0.0, folds=2, amp=0.01, thick=0.007, nx=8):
    # a hanging cloth: a sheet swept along x through the side-on outline path (y, z), dropping slope per metre off its middle (shoulders),
    # rippled in folds that deepen away from its top, given a little thickness
    xc, top = (x0 + x1) / 2, max(p[1] for p in path)
    bm = bmesh.new(); grid = []
    r = random.Random(len(name))
    ph = r.uniform(0, 6.3)
    for i in range(nx + 1):
        x = x0 + (x1 - x0) * i / nx
        col = []
        for y, z in path:
            t = min(1.0, (top - z) / 0.4)
            fy = amp * t * math.sin(2 * math.pi * folds * i / nx + ph)
            col.append(bm.verts.new((x, y + fy, z - slope * abs(x - xc) * (1 - 0.6 * t))))
        grid.append(col)
    for i in range(nx):
        for j in range(len(path) - 1):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    bm.normal_update()
    ob = link(name, bm, ROPE)
    m = ob.modifiers.new('solid', 'SOLIDIFY'); m.thickness = thick; m.offset = 0
    return ob

def locker():
    fresh()
    # a low plank chest (front and back walls of two boards, board ends, a lid of two boards, flat to sit on), standing between two
    # tall back posts that carry a back of upright boards, a capping rail and a peg rail with three pegs; iron straps round the front
    # corners and over the lid, an iron hasp on a staple at the front. A linen tunic hangs from the left peg, a towel over the right
    r = random.Random(7)
    hx, back, front = LK_W / 2, -LK_D / 2, LK_D / 2
    post = 0.07; lid_lo = LK_H - 0.04
    inner_back = back + post   # (the lid and the ends start in front of the posts)
    # the posts, floor to 1.6, the chest's back corners
    for sx in (-1, 1):
        box(f'post{sx}', (min(sx * hx, sx * (hx - post)), back, 0.0), (max(sx * hx, sx * (hx - post)), inner_back, LK_TALL), WOOD, (0, 0, 1), bevel=0.008)
    # the chest's walls
    for i, (z0, z1) in enumerate(((0.0, 0.225), (0.23, lid_lo))):
        box(f'front{i}', (-hx + r.uniform(0, 0.003), front - 0.03, z0), (hx - r.uniform(0, 0.003), front, z1), WOOD, (1, 0, 0), bevel=0.005)
        box(f'back{i}', (-hx + post, back, z0), (hx - post, back + 0.03, z1), WOOD, (1, 0, 0), bevel=0.004)
        for sx in (-1, 1):
            box(f'end{sx}_{i}', (min(sx * hx, sx * (hx - 0.03)), inner_back + 0.002, z0), (max(sx * hx, sx * (hx - 0.03)), front - 0.03, z1), WOOD, (0, 1, 0), bevel=0.005)
    # the lid: two boards lengthwise from the posts to the front, 4 cm thick, its top at 0.5
    mid = (inner_back + front) / 2
    for i, (y0, y1) in enumerate(((inner_back + 0.004, mid - 0.003), (mid + 0.003, front))):
        box(f'lid{i}', (-hx + r.uniform(0, 0.003), y0, lid_lo + 0.003), (hx - r.uniform(0, 0.003), y1, LK_H), WOOD, (1, 0, 0), bevel=0.006)
    # the back: four upright boards from behind the lid to the capping rail, the capping rail, the peg rail and its pegs
    xs = [-hx + post + (LK_W - 2 * post) * k / 4 for k in range(5)]
    for i, (a, b) in enumerate(zip(xs, xs[1:])):
        box(f'board{i}', (a + 0.002, back, lid_lo), (b - 0.002, back + 0.025, 1.5 + r.uniform(-0.003, 0.0)), WOOD, (0, 0, 1), bevel=0.004)
    box('cap', (-hx + post, back, 1.5), (hx - post, back + 0.04, 1.58), WOOD, (1, 0, 0), bevel=0.008)
    rail = back + 0.025
    box('pegrail', (-hx + post, rail, LK_PEG_Y - 0.05), (hx - post, rail + 0.03, LK_PEG_Y + 0.05), WOOD, (1, 0, 0), bevel=0.006)
    peg_r = 0.016
    for i, x in enumerate(LK_PEGS):
        a = Vector((x, rail + 0.01, LK_PEG_Y)); b = Vector((x, rail + 0.2, LK_PEG_Y + 0.035))
        tube(f'peg{i}', [a, a.lerp(b, 0.5), b], peg_r, WOOD, res=2)
        blob(f'pegknob{i}', b, Vector((0.021, 0.014, 0.021)), WOOD, seed=40 + i, subdiv=1, lumps=0.05)
    # iron: an L of strap round each front corner, two straps over the lid and down its front edge, a hasp from the lid over a staple
    s = 0.004
    for sx in (-1, 1):
        box(f'strap_f{sx}', (min(sx * (hx - 0.07), sx * (hx + s)), front, 0.02), (max(sx * (hx - 0.07), sx * (hx + s)), front + s, lid_lo - 0.01), IRON)
        box(f'strap_s{sx}', (min(sx * hx, sx * (hx + s)), front - 0.07, 0.02), (max(sx * hx, sx * (hx + s)), front, lid_lo - 0.01), IRON)
        lx = sx * 0.36
        box(f'strap_lid{sx}', (lx - 0.025, inner_back + 0.01, LK_H), (lx + 0.025, front + s, LK_H + s), IRON)
        box(f'strap_lip{sx}', (lx - 0.025, front, lid_lo - 0.04), (lx + 0.025, front + s, LK_H + s), IRON)
    box('hasp', (-0.028, front + s, 0.33), (0.028, front + 2 * s, LK_H + s), IRON)
    box('hasp_top', (-0.028, front - 0.08, LK_H), (0.028, front + 2 * s, LK_H + s), IRON)
    stp = 0.36
    tube('staple', smooth_line([Vector((-0.012, front, stp)), Vector((-0.012, front + 0.03, stp)), Vector((0.012, front + 0.03, stp)), Vector((0.012, front, stp))], 1), 0.004, IRON, res=1)
    # the linen: a tunic hung by its neck from the left peg (a layer each side of it, shoulders dropping off it, hem above the lid), a
    # towel folded over the right peg
    py, pz = rail + 0.11, LK_PEG_Y + 0.02 + peg_r   # (the top of the peg where the cloth rests)
    x0 = LK_PEGS[0]
    drape('cloth_tunic', x0 - 0.2, x0 + 0.2,
          [(p.x, p.y) for p in smooth_line([Vector((rail + 0.012, 0.74, 0)), Vector((rail + 0.015, 1.15, 0)), Vector((py - 0.04, pz - 0.015, 0)),
                                            Vector((py, pz + 0.006, 0)), Vector((py + 0.035, pz - 0.02, 0)), Vector((py + 0.03, 1.15, 0)),
                                            Vector((py + 0.012, 0.7, 0))], 1)],
          slope=0.35, folds=3, amp=0.012)
    x2 = LK_PEGS[2]
    py2 = rail + 0.15; pz2 = LK_PEG_Y + 0.025 + peg_r
    drape('cloth_towel', x2 - 0.12, x2 + 0.12,
          [(p.x, p.y) for p in smooth_line([Vector((py2 - 0.05, 1.02, 0)), Vector((py2 - 0.045, 1.25, 0)), Vector((py2 - 0.02, pz2 - 0.006, 0)),
                                            Vector((py2, pz2 + 0.004, 0)), Vector((py2 + 0.022, pz2 - 0.008, 0)), Vector((py2 + 0.03, 1.2, 0)),
                                            Vector((py2 + 0.02, 0.95, 0))], 1)],
          slope=0.0, folds=2, amp=0.008)
    export('locker')
    if 'render' in sys.argv: preview('locker', [(0, 3.2, 1.3, 0.75), (35, 3.2, 1.7, 0.75), (90, 2.6, 1.1, 0.75), (160, 3.2, 1.6, 0.7), (20, 1.5, 0.9, 0.45)])

# ---------------- write it out ----------------
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
        # (a closed solid's signed volume is positive if its faces look out; the water is one face looking up)
        vol = sum(me.vertices[p.vertices[0]].co.dot(me.vertices[p.vertices[1]].co.cross(me.vertices[p.vertices[2]].co)) for p in me.polygons) / 6
        if ob.name == 'water': assert all(p.normal.z > 0.99 for p in me.polygons), 'water faces down'
        elif vol <= 0: print('INSIDE OUT?', ob.name, vol)
        grain = Vector(ob['grain']).normalized() if 'grain' in ob else None
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
                    u, v = co.dot(across), co.dot(grain)   # (wood: the grain along v)
                else: u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + base + '.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    xs, ys, zs = verts[0::3], verts[1::3], verts[2::3]
    print(base, 'verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'x', round(min(xs), 3), round(max(xs), 3), 'y', round(min(ys), 3), round(max(ys), 3), 'z', round(min(zs), 3), round(max(zs), 3))

def preview(base, views):
    # yaw 0 looks at the piece's front (from +y)
    cols = [(0.36, 0.24, 0.14, 1), (0.42, 0.42, 0.4, 1), (0.05, 0.045, 0.04, 1), (0.1, 0.1, 0.11, 1), (0.12, 0.26, 0.32, 1), (1.0, 0.35, 0.05, 1), (0.6, 0.52, 0.38, 1)]
    for m, c in zip(mats, cols):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = c; b.inputs['Roughness'].default_value = 0.9
        if m.name == 'iron': b.inputs['Metallic'].default_value = 0.6; b.inputs['Roughness'].default_value = 0.5
        if m.name == 'water': b.inputs['Roughness'].default_value = 0.05
        if m.name == 'ember': b.inputs['Emission Color'].default_value = (1.0, 0.3, 0.04, 1); b.inputs['Emission Strength'].default_value = 6.0
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH':
            for p in ob.data.polygons: p.use_smooth = True
            ob.data.set_sharp_from_angle(angle=math.radians(80 if ob.name.startswith(SMOOTH) else 35))
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 640, 480
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, -0.002)); g = bpy.context.active_object; gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.36, 0.22, 1); g.data.materials.append(gm)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(150)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 40
    for yaw, d, z, look in views:
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, math.cos(a) * d, z)
        cam.rotation_euler = (Vector((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'{base}_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

which = [a for a in sys.argv if a in ('stove', 'bench', 'bucket', 'plunge', 'locker')] or ['stove', 'bench', 'bucket', 'plunge', 'locker']
for w in which: globals()[w]()
