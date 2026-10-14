# The gods' altars (Jarldom 0.1.85+), one look each, made from scratch. The face (where the worshipper kneels) is +y.
# Run: blender -b --python ~/.cache/jarldom-altars/altars.py -- <god> [render]   (writes ~/.cache/jarldom-altars/altar_<god>.json.gz)
import sys, math
sys.path.insert(0, '/home/bob/.cache/jarldom-altars')
from lib import *

TABLE = 0.9   # (the offering surface's height: the offering's sparkle shows a metre up)

def table_stone(name, w, d, h, at=Vector(), tilt=0.0):
    """A thick flat slab of rough stone, its top dressed flat."""
    pts = []
    for x in (-1, 1):
        for y in (-1, 1):
            for z in (0, 1):
                pts.append(Vector(at) + Vector((x * w / 2 * rng.uniform(0.93, 1.0), y * d / 2 * rng.uniform(0.93, 1.0), z * h + (x * tilt if z else 0))))
    for _ in range(10):
        pts.append(Vector(at) + Vector((rng.uniform(-w, w) / 2 * 1.03, rng.uniform(-d, d) / 2 * 1.03, rng.uniform(0.15, 0.85) * h)))
    ob = hull(name, pts, STONE, 4)
    weather(ob, voxel=0.015, big=0.03, small=0.008, ratio=0.2, seed_=len(name))
    flatten(ob, 2, Vector(at).z + h, 0.85)
    return ob

def odin():
    """A hörgr: a heap of flat stones under a capstone table; behind it a tall standing slab cut with the valknut, two ravens on its
    shoulders; Gungnir leaning on it; a drinking horn on a little stand and a scatter of coins on the table."""
    seed(11)
    # the heap: courses of flat stones, uneven, each course laid over the joints of the one below
    def course(z, w, d, h):
        for y0, y1 in ((-d / 2, -0.02), (0.02, d / 2)):
            x = -w / 2
            while x < w / 2 - 0.05:
                l = min(w / 2 - x, rng.uniform(0.22, 0.4))
                q = Matrix.Rotation(rng.uniform(-0.08, 0.08), 3, 'Z')
                c = Vector((x + l / 2, (y0 + y1) / 2 + rng.uniform(-0.02, 0.02), z))
                pts = [c + q @ Vector((sx * l / 2 * rng.uniform(0.85, 1.0), sy * (y1 - y0) / 2 * rng.uniform(0.85, 1.08), sz * h * rng.uniform(0.85, 1.0))) for sx in (-1, 1) for sy in (-1, 1) for sz in (0, 1)]
                pts += [c + q @ Vector((rng.uniform(-l / 2, l / 2) * 1.05, rng.uniform(-1, 1) * (y1 - y0) / 2 * 1.08, rng.uniform(0.2, 0.8) * h)) for _ in range(5)]
                hull('heap', pts, STONE, 6)
                x += l + 0.008
    for z, w, d, h in ((0.0, 1.12, 0.82, 0.22), (0.215, 1.04, 0.76, 0.19), (0.4, 0.98, 0.72, 0.21), (0.605, 0.92, 0.68, 0.21)):
        course(z, w, d, h)
    table_stone('table', 1.16, 0.84, 0.1, Vector((0, 0.02, 0.81)))
    # the standing slab behind, a little apart: rough, its face dressed flat for the cutting
    W, Y, TOPZ = 0.36, -0.52, 2.05
    pts = []
    for k in range(10):
        z = k / 9 * TOPZ
        ww = W * (1 - 0.15 * k / 9) * rng.uniform(0.95, 1.05)
        if k == 9: ww *= 0.65
        for sx in (-1, 1):
            for sy in (-1, 1): pts.append(Vector((sx * ww + rng.uniform(-0.02, 0.02), Y + sy * 0.1 * rng.uniform(0.9, 1.1), z)))
    slab = hull('slab', pts, STONE, 2)
    weather(slab, voxel=0.02, big=0.05, small=0.012, ratio=0.15, seed_=3)
    flatten(slab, 1, Y + 0.105, 0.75, 1)
    face = bvh(slab)
    # the valknut: three interlocked triangles, cut high on the face
    C = Vector((0, Y + 0.3, 1.55)); R = 0.2
    for k in range(3):
        cen = C + Vector((math.cos(math.pi / 2 + 2 * math.pi * k / 3), 0, math.sin(math.pi / 2 + 2 * math.pi * k / 3))) * R * 0.32
        tri = [cen + Vector((math.cos(math.pi / 2 + 2 * math.pi * j / 3), 0, math.sin(math.pi / 2 + 2 * math.pi * j / 3))) * R for j in range(3)]
        carve(face, tri, (0, -1, 0), w=0.012, cyclic=True)
    # a band of runes down the face under it
    for k in range(6):
        z = 1.22 - k * 0.11; x = rng.uniform(-0.02, 0.02)
        a, b = Vector((x, Y + 0.3, z + 0.04)), Vector((x, Y + 0.3, z - 0.04))
        carve(face, [a, b], (0, -1, 0), w=0.009)
        m = rng.random(); mid = a.lerp(b, rng.uniform(0.2, 0.6))
        carve(face, [mid, mid + Vector((rng.choice((-1, 1)) * 0.035, 0, -0.03))], (0, -1, 0), w=0.009)
        if m < 0.5: carve(face, [a, a + Vector((0.03, 0, -0.025))], (0, -1, 0), w=0.009)
    # Huginn and Muninn on its shoulders, looking out and down at the worshipper
    def perch(x, y):  # (stood on the slab's top, found by a ray down)
        hit = face.ray_cast(Vector((x, y, 3.0)), Vector((0, 0, -1)))
        return Vector((x, y, (hit[0].z if hit[0] is not None else TOPZ) - 0.01))
    raven(perch(-0.15, Y + 0.0), math.radians(-18), 1.25, head_turn=math.radians(40))
    raven(perch(0.15, Y + 0.0), math.radians(20), 1.25, head_turn=math.radians(-25))
    # Gungnir leaning on the slab's right edge
    foot, top = Vector((0.66, -0.22, 0.0)), Vector((0.42, -0.44, 1.78))
    cyl('spear', foot, top, 0.02, DARK if False else WOOD, n=8, smooth=True)
    d = (top - foot).normalized(); side = d.cross(Vector((0, 1, 0))).normalized()
    head = top + d * 0.32
    hull('spearhead', [top + side * x for x in (-0.018, 0.018)] + [top + d * 0.12 + side * x + Vector((0, y, 0)) for x in (-0.05, 0.05) for y in (-0.008, 0.008)] + [head], IRON, 1)
    for k in range(3):
        cyl('bind', top - d * (0.03 + 0.03 * k), top - d * (0.05 + 0.03 * k), 0.024, GOLD, n=8, smooth=True)
    # a drinking horn on a little stand, rim of gold; coins on the table
    T = TABLE + 0.01
    hull('stand', [Vector((x, y, T + z)) for x in (-0.08, 0.08) for y in (0.1, 0.16) for z in (0, 0.07)], WOOD, 1)
    horn = [Vector((-0.17 + 0.34 * t, 0.13, T + 0.07 + 0.12 * (1 - (2 * t - 1) ** 2) * 0.6 + (0.1 if t > 0.85 else 0) * (t - 0.85) / 0.15)) for t in [i / 14 for i in range(15)]]
    horn = [Vector((-0.2 + 0.36 * t, 0.13, T + 0.075 + 0.06 * math.sin(math.pi * t * 0.9) + 0.14 * t ** 3)) for t in [i / 14 for i in range(15)]]
    tube('horn', horn, 0.05, BONE, taper=lambda t: 0.12 + 0.88 * t ** 1.3)
    tube('rim', [horn[-1] + Vector((0.006, 0.05 * math.cos(a), 0.05 * math.sin(a))) for a in [i * 2 * math.pi / 14 for i in range(14)]], 0.006, GOLD, cyclic=True)
    for k in range(7):
        a = rng.uniform(0, 2 * math.pi)
        at = Vector((0.22 + rng.uniform(-0.12, 0.12), 0.05 + rng.uniform(-0.12, 0.12), T + 0.003 + 0.004 * (k % 3)))
        cyl('coin', at, at + Vector((0.0, 0.0, 0.004)), 0.018, GOLD, n=8)
    export('altar_odin')
    return dict(height=2.0)


def beam(name, a, b, w, h, mat, up=Vector((0, 0, 1))):
    """A squared timber from a to b, w wide and h deep (h along up, as near as it can be)."""
    a, b = Vector(a), Vector(b); d = (b - a).normalized()
    s = d.cross(Vector(up)); s = s.normalized() if s.length > 1e-4 else d.cross(Vector((1, 0, 0))).normalized()
    u = s.cross(d).normalized()
    return hull(name, [p + s * x + u * y for p in (a, b) for x in (-w / 2, w / 2) for y in (-h / 2, h / 2)], mat, 1)

def log(name, a, b, r, mat=WOOD, n=9, jit=0.05):
    a, b = Vector(a), Vector(b); d = (b - a).normalized(); q = d.to_track_quat('Z', 'Y').to_matrix()
    pts = []
    for t in (0.0, 0.5, 1.0):
        for i in range(n):
            ang = 2 * math.pi * i / n; rr = r * (1 + rng.uniform(-jit, jit))
            pts.append(a.lerp(b, t) + q @ Vector((math.cos(ang) * rr, math.sin(ang) * rr, 0)))
    return hull(name, pts, mat, 2)

def roots(R, n, mat, seed_=0, reach=0.24, high=0.28):
    for k in range(n):
        a = 2 * math.pi * k / n + rng.uniform(-0.25, 0.25)
        d = Vector((math.cos(a), math.sin(a), 0)); s = Vector((-d.y, d.x, 0))
        w = rng.uniform(0.07, 0.1)
        hull('root', [d * R * 0.8 + s * w + Vector((0, 0, high * rng.uniform(0.8, 1.1))), d * R * 0.8 - s * w + Vector((0, 0, high)),
                      d * R * 0.8 + s * w * 1.3, d * R * 0.8 - s * w * 1.3,
                      d * (R + reach * rng.uniform(0.8, 1.15)) + s * w * 0.5 + Vector((0, 0, 0.02)), d * (R + reach) - s * w * 0.5,
                      d * (R + reach) + Vector((0, 0, 0.0))], mat, 3)

def fish(at, yaw, L, mat):
    q = Matrix.Rotation(yaw, 3, 'Z')
    ball('fish', at, L / 2, mat, squash=(0.18, 1.0, 0.3), n=10, turn=q)
    tail = Vector(at) + q @ Vector((0, -L * 0.48, 0))
    hull('fish', [tail, Vector(at) + q @ Vector((0, -L * 0.68, L * 0.14)), Vector(at) + q @ Vector((0, -L * 0.68, -L * 0.1)),
                  Vector(at) + q @ Vector((0.006, -L * 0.6, 0)), Vector(at) + q @ Vector((-0.006, -L * 0.6, 0))], mat, 1)
    hull('fish', [Vector(at) + q @ Vector((x, y * L, L * 0.13 + z * L)) for x in (-0.004, 0.004) for y, z in ((0.05, 0), (-0.2, 0), (-0.12, 0.08))], mat, 1)

def skull(at, yaw, s, mat=BONE, eye=DARK, tilt=0.0):
    q = Matrix.Rotation(yaw, 3, 'Z') @ Matrix.Rotation(tilt, 3, 'X')
    def P(x, y, z): return Vector(at) + q @ Vector((x * s, y * s, z * s))
    ball('skull', P(0, -0.01, 0.085), 0.075 * s, mat, squash=(0.88, 1.08, 0.9), n=10, turn=q)
    hull('skull', [P(x, y, z) for x in (-0.048, 0.048) for y, z in ((0.03, 0.06), (0.07, 0.03), (0.075, -0.0), (0.03, -0.005))] + [P(0, 0.085, 0.015)], mat, 2)
    hull('skull', [P(x, y, z) for x in (-0.042, 0.042) for y, z in ((0.0, -0.01), (0.06, -0.03), (0.07, -0.005), (-0.01, 0.01))], mat, 2)  # (the jaw)
    for sx in (-1, 1): ball('eye', P(sx * 0.024, 0.062, 0.052), 0.018 * s, eye, squash=(1, 0.5, 0.9), n=8)
    hull('eye', [P(x, 0.083, z) for x, z in ((0, 0.035), (-0.008, 0.018), (0.008, 0.018))] + [P(0, 0.07, 0.025)], eye, 1)

def candle(at, h, r=0.024):
    at = Vector(at)
    cyl('candle', at, at + Vector((0, 0, h)), r, BONE, n=10, smooth=True)
    for k in range(3):
        a = rng.uniform(0, 2 * math.pi); z = rng.uniform(0.3, 0.9) * h
        ball('drip', at + Vector((math.cos(a) * r * 0.9, math.sin(a) * r * 0.9, z)), r * 0.35, BONE, squash=(1, 1, 2.2), n=6)
    cyl('wick', at + Vector((0, 0, h)), at + Vector((0, 0, h + 0.012)), 0.003, DARK, n=4)
    ball('flame', at + Vector((0, 0, h + 0.035)), 0.016, GLOW, squash=(0.8, 0.8, 1.9), n=8)

def thor():
    """An oak stump, iron-banded, Mjölnir standing head-down on it; behind, a red-banded post with a goat's skull and its great curled
    horns at the head, and the oath ring hung on a peg."""
    seed(21)
    R, H = 0.36, 0.8
    pts = []
    for z, rr in ((0, 1.1), (0.12, 1.0), (0.4, 0.97), (H - 0.03, 0.95), (H, 0.9)):
        pts += ring(z, R * rr, 14, jit=0.035)
    hull('stump', pts, WOOD, 2)
    roots(R, 6, WOOD, reach=0.16, high=0.2)
    for z in (0.2, 0.62): tube('band', ring(z, R * 1.03, 28), 0.014, IRON, cyclic=True)
    # Mjölnir: the head on the stump (chamfered, its striking faces flared, gold bands right round it), a short wooden haft up with a
    # banded leather grip, an iron pommel with a gold collar, a leather loop
    C = Vector((0, 0.02, H + 0.105))
    def octo(x, hy, hz, c=0.022): return [C + Vector((x, y, z)) for y, z in ((hy - c, hz), (hy, hz - c), (hy, -hz + c), (hy - c, -hz), (-hy + c, -hz), (-hy, -hz + c), (-hy, hz - c), (-hy + c, hz))]
    hull('mjolnir', octo(-0.12, 0.08, 0.092) + octo(0.12, 0.08, 0.092), IRON, 1)
    for sx in (-1, 1):
        hull('mjolnir', octo(sx * 0.12, 0.087, 0.099, 0.024) + octo(sx * 0.143, 0.087, 0.099, 0.024), GOLD, 1)
        hull('mjolnir', octo(sx * 0.143, 0.09, 0.102, 0.026) + octo(sx * 0.19, 0.1, 0.112, 0.03), IRON, 1)
        hull('mjolnir', octo(sx * 0.19, 0.1, 0.112, 0.03) + octo(sx * 0.198, 0.092, 0.104, 0.03), IRON, 1)   # (the face's bevel)
    a = C + Vector((0, 0, 0.09))
    segs = [(0.0, 0.07, WOOD, 0.03)]
    z = 0.07
    for k in range(5):
        segs += [(z, z + 0.045, HIDE, 0.036), (z + 0.045, z + 0.055, DARK, 0.038)]; z += 0.055
    segs += [(z, z + 0.02, WOOD, 0.03), (z + 0.02, z + 0.035, GOLD, 0.04), (z + 0.035, z + 0.075, IRON, 0.042)]
    for z0, z1, m, r in segs: cyl('haft', a + Vector((0, 0, z0)), a + Vector((0, 0, z1)), r, m, n=12, smooth=True)
    top = a + Vector((0, 0, z + 0.075))
    ball('pommel', top, 0.042, IRON, squash=(1, 1, 0.45), n=12)
    tube('loop', [top + Vector((0.035 + 0.06 * math.sin(t), 0.01 * math.sin(t), -0.03 - 0.1 * (1 - math.cos(t)))) for t in [i * math.pi / 8 for i in range(17)]], 0.007, HIDE, cyclic=True)
    # the post behind, its red bands stacked between the wood (never painted over it), its head flat with an iron cap for the skull
    PY = -0.55; TOP = 1.74
    segs = ((0, 0.95, WOOD), (0.95, 1.03, PAINT), (1.03, 1.1, WOOD), (1.1, 1.18, PAINT), (1.18, TOP - 0.03, WOOD))
    for z0, z1, m in segs: box('post', (0, PY, (z0 + z1) / 2), (0.22, 0.2, z1 - z0), m)
    box('cap', (0, PY, TOP - 0.015), (0.24, 0.22, 0.03), IRON)
    # the goat's skull on it, looking out over the altar, a little down; its horns rising and sweeping back, ridged
    q = Matrix.Rotation(math.radians(-14), 3, 'X')
    O = Vector((0, PY - 0.09, TOP))
    SK = 1.4
    def P(x, y, z): return O + q @ (Vector((x, y, z)) * SK)
    sk = []
    for y, hx, z0, z1 in ((0.0, 0.042, 0.02, 0.075), (0.03, 0.056, 0.0, 0.104), (0.075, 0.062, -0.002, 0.11), (0.115, 0.05, 0.0, 0.088),
                          (0.18, 0.037, 0.002, 0.06), (0.25, 0.027, 0.006, 0.04), (0.3, 0.016, 0.012, 0.028)):
        sk += [P(x, y, z) for x in (-hx, hx) for z in (z0, z1)] + [P(x * 0.7, y, z1 + 0.012) for x in (-hx, hx)] + [P(x * 0.8, y, z0 - 0.006) for x in (-hx, hx)]
    sk += [P(0, y, z + 0.014) for y, z in ((0.03, 0.104), (0.075, 0.11), (0.18, 0.06), (0.25, 0.04))]  # (the nasal ridge and the dome)
    skull = hull('goat', sk, BONE, 1)
    # its eye sockets, cut into its sides, and the nasal opening at the muzzle: hollows, dark within
    bm = bmesh.new(); bm.from_mesh(skull.data); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(skull.data); bm.free()
    for c, r in [((sx * 0.066, 0.088, 0.066), (0.024, 0.026, 0.021)) for sx in (-1, 1)] + [((0, 0.285, 0.045), (0.01, 0.05, 0.014))]:
        cut = ball('socket', P(*c), 1.0 * SK, SHADE, squash=r, n=12, turn=q)
        mod = skull.modifiers.new('cut', 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.object = cut; mod.solver = 'EXACT'; mod.material_mode = 'TRANSFER'
        me = bpy.data.meshes.new_from_object(skull.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        skull.modifiers.clear(); old = skull.data; skull.data = me; bpy.data.meshes.remove(old); bpy.data.objects.remove(cut)
    for sx in (-1, 1):
        for k in range(5):                                                                                                    # (the molars)
            y = 0.12 + 0.024 * k; hx = 0.04 - 0.004 * k
            hull('tooth', [P(sx * (hx + dx), y + dy, z) for dx in (-0.008, 0.002) for dy in (-0.009, 0.009) for z in (-0.002, -0.014)], BONE, 1)
    for sx in (-1, 1):
        base = P(sx * 0.03, 0.065, 0.1); R_ = 0.2; pts = []
        for i in range(25):
            t = i / 24; th = t * 0.82 * math.pi
            pts.append(base + q @ Vector((sx * (0.012 + 0.16 * t ** 1.3), -R_ * (1 - math.cos(th)), R_ * math.sin(th))))
        rad = lambda t: 1 - 0.85 * t
        tube('horn', pts, 0.04, HIDE, taper=rad)
        for i in range(2, 21, 2):   # its ridges
            t = i / 24; d = (pts[i + 1] - pts[i - 1]).normalized(); u = d.cross(Vector((1, 0, 0))).normalized(); w = d.cross(u).normalized()
            r = 0.04 * rad(t) * 1.06
            tube('horn', [pts[i] + (u * math.cos(a) + w * math.sin(a)) * r for a in [k * 2 * math.pi / 10 for k in range(10)]], 0.0045 * (1 - 0.6 * t), HIDE, cyclic=True, res=1)
    # the oath ring on its peg
    peg = Vector((0, PY + 0.1, 1.38))
    cyl('peg', peg, peg + Vector((0, 0.08, 0.01)), 0.012, IRON, n=6)
    O = peg + Vector((0, 0.06, -0.13))
    tube('ring', [O + Vector((0.13 * math.cos(t), 0.012 * math.sin(3 * t), 0.13 * math.sin(t))) for t in [i * 2 * math.pi / 32 for i in range(32)]], 0.016, GOLD, cyclic=True)
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.6
        lump('scorch', Vector((math.cos(a) * 0.62, math.sin(a) * 0.62, 0.03)), 0.08 + 0.02 * k, STONE)
    export('altar_thor')
    return dict(height=1.9)

def freyja():
    """A round stone table on a drum of two stones, a garland of flowers round its edge, sheaves of barley leant on it; behind, an arch
    of withies wound with leaves and flowers, Brísingamen hung in it; her two cats, carved, either side; a bowl of apples on the table."""
    seed(31)
    for z0, z1, r in ((0.0, 0.38, 0.36), (0.38, 0.76, 0.33)):
        ob = hull('drum', ring(z0, r * 1.02, 16, jit=0.03) + ring(z1, r, 16, jit=0.03), STONE, 2)
    top = hull('table', ring(0.76, 0.48, 20, jit=0.02) + ring(0.88, 0.47, 20, jit=0.02), STONE, 2)
    weather(top, voxel=0.015, big=0.02, small=0.006, ratio=0.2, seed_=5); flatten(top, 2, 0.88, 0.85)
    # the garland, swagged between ties round the edge
    pts = []
    for i in range(160):
        a = 2 * math.pi * i / 160; f = (a * 8 / (2 * math.pi)) % 1
        pts.append(Vector((math.cos(a) * 0.5, math.sin(a) * 0.5, 0.855 - 0.075 * math.sin(math.pi * f))))
    tube('garland', pts, 0.02, LEAF, cyclic=True)
    for i in range(0, 160, 5):
        p = pts[i]; out = Vector((p.x, p.y, 0)).normalized()
        ball('bloom', p + out * 0.016 + Vector((0, 0, 0.008)), 0.024 if i % 10 else 0.03, BLOOM if i % 15 else GOLD, squash=(1, 1, 0.7), n=8)
    # sheaves of barley at the sides and back
    for ang in (0, 180, 225, 315):
        a = math.radians(ang); d = Vector((math.cos(a), math.sin(a), 0))
        foot = d * 0.5; topc = d * 0.43 + Vector((0, 0, 0.66))
        ax = (topc - foot).normalized(); q = ax.to_track_quat('Z', 'Y').to_matrix()
        pts = []
        for t, r in ((0, 0.075), (0.15, 0.06), (0.4, 0.045), (0.5, 0.05), (0.75, 0.075), (1.0, 0.09)):
            for i in range(10):
                b = 2 * math.pi * i / 10; rr = r * rng.uniform(0.9, 1.1)
                pts.append(foot.lerp(topc, t) + q @ Vector((math.cos(b) * rr, math.sin(b) * rr, 0)))
        hull('sheaf', pts, ROPE, 2)
        for i in range(26):  # the ears, standing proud of the top
            b = rng.uniform(0, 2 * math.pi); rr = rng.uniform(0, 0.08)
            s = topc + q @ Vector((math.cos(b) * rr, math.sin(b) * rr, 0))
            e = s + (ax + q @ Vector((math.cos(b) * rr * 2, math.sin(b) * rr * 2, 0))).normalized() * rng.uniform(0.08, 0.13)
            cyl('ear', s, e, 0.01, ROPE, n=5, r2=0.004)
        c = foot.lerp(topc, 0.45)
        tube('tie', [c + q @ Vector((math.cos(b) * 0.052, math.sin(b) * 0.052, 0)) for b in [i * 2 * math.pi / 14 for i in range(14)]], 0.009, GOLD if ang == 0 else ROPE, cyclic=True)
    # the arch of withies behind
    AY = -0.5
    def arch(t, off=0.0): return Vector((0.62 * math.cos(math.pi * t), AY + off, 0.02 + 1.86 * math.sin(math.pi * t) ** 0.75))
    for k in range(2):
        pts = [arch(i / 48) + Vector((0, 0.02 * math.sin(i * 0.7 + k * math.pi), 0.02 * math.cos(i * 0.7 + k * math.pi))) for i in range(49)]
        tube('withy', pts, 0.02, WOOD)
    for i in range(3, 46, 2):
        p = arch(i / 48)
        for _ in range(2):
            o = Vector((rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05)))
            q = Matrix.Rotation(rng.uniform(0, 6.3), 3, 'Z') @ Matrix.Rotation(rng.uniform(-0.8, 0.8), 3, 'X')
            ball('leaf', p + o, 0.04, LEAF, squash=(0.45, 1.0, 0.12), n=6, turn=q)
        if i % 4 == 1: ball('bloom', p + Vector((0, 0.04, 0)), 0.028, BLOOM, squash=(1, 0.7, 1), n=8)
    # Brísingamen, hung in the arch on two cords
    L, Rr = Vector((-0.2, AY, 1.48)), Vector((0.2, AY, 1.48))
    for e, t in ((L, 0.605), (Rr, 0.395)):
        tube('cord', [arch(t) + Vector((0, 0, -0.02)), e], 0.004, ROPE)
    beads = [L.lerp(Rr, i / 18) + Vector((0, 0.02 * math.sin(math.pi * i / 18), -0.2 * math.sin(math.pi * i / 18))) for i in range(19)]
    tube('necklace', beads, 0.006, GOLD)
    for i, p in enumerate(beads):
        if 0 < i < 18: ball('bead', p, 0.018 if i % 2 else 0.013, GOLD if i % 3 else GLOW, n=8)
    P = beads[9] + Vector((0, 0.01, -0.05))
    hull('pendant', [P + Vector((0.055 * math.cos(t), y, 0.055 * math.sin(t))) for t in [i * 2 * math.pi / 12 for i in range(12)] for y in (-0.008, 0.008)], GOLD, 1)
    ball('stone', P + Vector((0, 0.012, 0)), 0.026, GLOW, squash=(1, 0.5, 1), n=8)
    # her cats, carved in stone, sitting at the arch's feet
    for sx in (-1, 1):
        c = Vector((sx * 0.68, -0.18, 0.0)); q = Matrix.Rotation(-sx * 0.35, 3, 'Z')
        def P(x, y, z): return c + q @ Vector((x, y, z))
        ball('cat', P(0, -0.02, 0.1), 0.1, STONE, squash=(0.9, 1.15, 0.95), n=10, smooth=False)
        ball('cat', P(0, 0.05, 0.2), 0.075, STONE, squash=(0.85, 0.8, 1.2), n=10, smooth=False)
        ball('cat', P(0, 0.075, 0.33), 0.062, STONE, squash=(1.0, 0.95, 0.9), n=10, smooth=False)
        ball('cat', P(0, 0.125, 0.315), 0.03, STONE, squash=(1.2, 0.9, 0.8), n=8, smooth=False)
        for ex in (-1, 1):
            hull('cat', [P(ex * 0.03, 0.06, 0.37), P(ex * 0.055, 0.07, 0.37), P(ex * 0.045, 0.065, 0.43), P(ex * 0.042, 0.085, 0.375)], STONE, 1)
            cyl('cat', P(ex * 0.035, 0.1, 0.0), P(ex * 0.035, 0.08, 0.17), 0.022, STONE, n=6)
            ball('eye', P(ex * 0.023, 0.128, 0.345), 0.009, GOLD, n=6)
        tube('tail', [P(0.06 * math.sin(t), -0.1 + 0.16 * (1 - math.cos(t)) * 0.9, 0.015) for t in [i * math.pi / 10 for i in range(11)]], 0.018, STONE, taper=lambda t: 1 - 0.4 * t, smooth=False)
    # a wooden bowl of apples on the table
    B = Vector((0.12, 0.12, 0.88))
    hull('bowl', ring(0, 0.07, 14, at=B) + ring(0.07, 0.13, 14, at=B), WOOD, 2)
    for k in range(6):
        a = 2 * math.pi * k / 5; d = 0.07 if k else 0.0
        ball('apple', B + Vector((math.cos(a) * d, math.sin(a) * d, 0.09 + (0.035 if k == 0 else 0))), 0.04, BLOOM if k % 2 else PAINT, n=8)
    export('altar_freyja')
    return dict(height=1.9, bloom=(0.85, 0.35, 0.45), paint=(0.62, 0.15, 0.1))

def njord():
    """A sea-worn boulder for a table among beach stones; behind, a ship's stem post rising and curling forward into a spiral; a net
    draped over the table's front, an anchor leant on its side, an oar on the other; fish, shells and amber on the table."""
    seed(41)
    pts = []
    for _ in range(60):
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        pts.append(Vector((d.x * 0.56, d.y * 0.4, 0.36 + d.z * 0.46)))
    top = hull('boulder', [p if p.z > 0 else Vector((p.x * 0.85, p.y * 0.85, 0)) for p in pts], STONE, 4)
    weather(top, voxel=0.02, big=0.07, small=0.012, ratio=0.15, seed_=4)
    flatten(top, 2, 0.79, 0.8, 1)
    for k in range(8):
        a = 2 * math.pi * k / 8 + 0.2
        lump('pebble', Vector((math.cos(a) * 0.66, math.sin(a) * 0.48, 0.04)), rng.uniform(0.08, 0.13), STONE, squash=0.55)
    face = bvh(top)
    # the stem post: up from the ground behind, over and forward into a curl
    path = [Vector((0, -0.46 - 0.06 * (i / 20), 1.4 * i / 20)) for i in range(20)]
    Cc = Vector((0, -0.34, 1.42))
    for i in range(1, 41):
        u = i / 40; phi = math.pi - u * 1.85 * math.pi; rho = 0.18 * (1 - 0.68 * u)
        path.append(Cc + Vector((0, rho * math.cos(phi), rho * math.sin(phi))))
    sweep('stem', path, lambda t: [(x * (0.05 - 0.02 * t), y * (0.07 - 0.04 * t)) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))], WOOD, fixed=(1, 0, 0))
    for i in range(2, 20, 3):  # rivets down its edges
        for sx in (-1, 1): ball('rivet', path[i] + Vector((sx * 0.05, 0.035, 0)), 0.011, IRON, squash=(0.5, 1, 1), n=6)
    # the net: over the table's front and down, each knot found on the stone
    def onstone(x, v):
        if v < 0.4: q = Vector((x, -0.15 + 0.5 * v / 0.4, 0.8))
        else: q = Vector((x, 0.35, 0.8 - 0.5 * (v - 0.4) / 0.6))
        for _ in range(2):
            hit = face.find_nearest(q)
            if hit[0] is None: break
            q = hit[0] + hit[1] * 0.02
        return q
    xs = [-0.48 + 0.6 * i / 9 for i in range(10)]; vs = [j / 12 for j in range(13)]
    for x in xs: tube('net', [onstone(x + 0.015 * math.sin(v * 9), v) for v in [j / 30 for j in range(31)]], 0.004, ROPE, res=1)
    for v in vs: tube('net', [onstone(x, v) for x in [-0.48 + 0.6 * i / 30 for i in range(31)]], 0.004, ROPE, res=1)
    for x in xs[::3]: ball('float', onstone(x, 1.0), 0.03, WOOD, squash=(1, 1, 0.75), n=8)
    # the anchor, leant on the right
    Cr, T = Vector((0.82, 0.08, 0.06)), Vector((0.58, -0.02, 0.95))
    d = (T - Cr).normalized(); n_ = Vector((0, 1, 0)); n_ = (n_ - d * n_.dot(d)).normalized()
    cyl('anchor', Cr, T, 0.022, IRON, n=8)
    for s in (-1, 1):
        arm = [Cr + n_ * s * 0.24 * math.sin(th) + d * 0.24 * (1 - math.cos(th)) for th in [i * 1.15 / 10 for i in range(11)]]
        tube('anchor', arm, 0.02, IRON, taper=lambda t: 1 - 0.3 * t)
        e = arm[-1]; fd = (arm[-1] - arm[-3]).normalized(); w = d.cross(n_).normalized()
        hull('fluke', [e + fd * 0.07, e - fd * 0.05 + n_ * s * 0.05, e - fd * 0.06 - n_ * s * 0.02] + [e + w * o for o in (-0.012, 0.012)], IRON, 1)
    tube('anchor', [T + d * 0.05 + n_ * 0.05 * math.sin(t) + d * 0.05 * math.cos(t) for t in [i * 2 * math.pi / 14 for i in range(14)]], 0.01, IRON, cyclic=True)
    w = d.cross(n_).normalized(); beam('stock', T - d * 0.1 - w * 0.17, T - d * 0.1 + w * 0.17, 0.05, 0.05, WOOD, up=d)
    # the oar on the left
    a, b = Vector((-0.78, 0.22, 0.0)), Vector((-0.55, -0.18, 1.8))
    cyl('oar', a + (b - a) * 0.3, b, 0.022, WOOD, n=8, smooth=True)
    d = (b - a).normalized(); s = d.cross(Vector((0, 0, 1))).normalized()
    hull('blade', [a + d * t + s * w_ + Vector((0, 0, 0)) + d.cross(s).normalized() * y for t, w_ in ((0.0, 0.07), (0.0, -0.07), (0.45, 0.025), (0.45, -0.025), (0.55, 0.0)) for y in (-0.012, 0.012)], WOOD, 1)
    # on the table: two fish, shells, a lump of amber
    fish(Vector((-0.12, 0.05, 0.815)), math.radians(80), 0.34, PAINT)
    fish(Vector((0.18, -0.08, 0.815)), math.radians(110), 0.28, PAINT)
    for k in range(5):
        p = Vector((rng.uniform(0.1, 0.4), rng.uniform(0.0, 0.25), 0.79))
        ball('shell', p, 0.028, BONE, squash=(1, 0.8, 0.45), n=8)
    lump('amber', Vector((-0.3, -0.18, 0.82)), 0.045, GLOW, squash=0.8)
    export('altar_njord')
    return dict(height=1.8, paint=(0.42, 0.55, 0.62))

def frigg():
    """A stone step under an iron-bound chest; behind, a warp-weighted loom, its cloth half woven and banded, its weights hanging; a ring
    of keys hung on the chest's lock; a distaff of wool, a spindle and a folded cloth on the lid."""
    seed(51)
    st = table_stone('step', 1.04, 0.66, 0.22, Vector((0, 0.02, 0)))
    # the chest
    Z0, Z1, W, D = 0.22, 0.8, 0.88, 0.5
    box('chest', (0, 0, (Z0 + Z1 - 0.12) / 2), (W, D, Z1 - 0.12 - Z0), WOOD)   # (up to the lid: their tops would fight)
    box('lid', (0, 0, Z1 - 0.06), (W + 0.03, D + 0.03, 0.12), DARK)
    for x in (-0.3, 0.3):
        box('strap', (x, D / 2 + 0.008, (Z0 + Z1 - 0.12) / 2), (0.05, 0.016, Z1 - Z0 - 0.12), IRON)
        box('strap', (x, 0, Z1 + 0.004), (0.05, D + 0.05, 0.012), IRON)
        box('strap', (x, -D / 2 - 0.008, (Z0 + Z1 - 0.12) / 2), (0.05, 0.016, Z1 - Z0 - 0.12), IRON)
    for sx in (-1, 1):
        for sy in (-1, 1): box('corner', (sx * (W / 2 - 0.03), sy * (D / 2 - 0.03), Z0 + 0.05), (0.075, 0.075, 0.1), IRON)
    L = Vector((0, D / 2 + 0.018, Z1 - 0.15))
    box('lock', L, (0.13, 0.02, 0.14), IRON); box('hasp', L + Vector((0, 0.008, 0.09)), (0.04, 0.016, 0.1), IRON)
    # the ring of keys on it
    K = L + Vector((0, 0.03, -0.1))
    tube('keyring', [K + Vector((0.045 * math.cos(t), 0.006 * math.sin(2 * t), -0.045 + 0.045 * math.sin(t))) for t in [i * 2 * math.pi / 18 for i in range(18)]], 0.005, IRON, cyclic=True)
    for k, a in enumerate((-0.6, -0.2, 0.2, 0.6)):
        top = K + Vector((0.045 * math.sin(a), 0.004 * k, -0.045 - 0.045 * math.cos(a)))
        d = Vector((math.sin(a) * 0.4, 0.02, -1)).normalized(); l = 0.13 + 0.03 * (k % 2)
        cyl('key', top, top + d * l, 0.006, IRON, n=6)
        e = top + d * l; s = Vector((1, 0, 0))
        box('bit', e - d * 0.02 + s * 0.015, (0.03, 0.006, 0.035), IRON)
        tube('bow', [top + d * 0.0 + Vector((0.015 * math.cos(t), 0, 0.015 * math.sin(t))) for t in [i * 2 * math.pi / 10 for i in range(10)]], 0.004, IRON, cyclic=True)
    # the loom: two uprights leant back, the cloth beam across their heads
    def lean(z): return -0.34 - 0.3 * z / 2.0
    for sx in (-1, 1): beam('upright', (sx * 0.62, lean(0), 0), (sx * 0.58, lean(2.0), 2.0), 0.08, 0.08, WOOD)
    cyl('clothbeam', (-0.7, lean(1.86) + 0.05, 1.86), (0.7, lean(1.86) + 0.05, 1.86), 0.05, WOOD, n=10, smooth=True)
    for sx in (-1, 1): cyl('peg', (sx * 0.7, lean(1.86) + 0.05, 1.86), (sx * 0.7, lean(1.86) + 0.05 + 0.12, 1.9), 0.012, WOOD, n=6)
    # the cloth, woven down from the beam, in bands
    bands = ((0.0, 0.14, CLOTH), (0.14, 0.2, PAINT), (0.2, 0.24, CLOTH), (0.24, 0.27, GOLD if False else PAINT), (0.27, 1.0, CLOTH))
    ZT, ZB = 1.82, 1.08
    for v0, v1, m in bands:
        sheet('cloth', lambda u, v, v0=v0, v1=v1: (lambda z: Vector((-0.5 + u, lean(z) + 0.1 + 0.008 * math.sin(u * 20), z)))(ZT - (ZT - ZB) * (v0 + (v1 - v0) * v)), 12, 2, m, thick=0.012)
    cyl('rod', (-0.6, lean(ZB) + 0.1, ZB), (0.6, lean(ZB) + 0.1, ZB), 0.014, WOOD, n=6)
    cyl('heddle', (-0.66, lean(0.9) + 0.16, 0.9), (0.66, lean(0.9) + 0.16, 0.9), 0.018, WOOD, n=6)
    # the warp hanging below it to the weights, in two rows
    for k in range(8):
        x = -0.44 + 0.88 * k / 7; row = k % 2
        wz = 0.48 + 0.05 * row; wy = lean(ZB) + 0.1 + (0.05 if row else -0.04)
        Wt = Vector((x, wy, wz))
        for j in (-1, 0, 1):
            tube('warp', [Vector((x + j * 0.025, lean(ZB) + 0.1, ZB)), Wt + Vector((j * 0.008, 0, 0.06))], 0.0025, ROPE, res=1)
        tube('weight', [Wt + Vector((0.038 * math.cos(t), 0, 0.038 * math.sin(t))) for t in [i * 2 * math.pi / 12 for i in range(12)]], 0.024, STONE, cyclic=True, smooth=False)
    # on the lid: a distaff in its stand with wool, a spindle with its whorl, a folded cloth
    S = Vector((-0.24, -0.05, Z1 + 0.01))
    cyl('stand', S, S + Vector((0, 0, 0.05)), 0.06, WOOD, n=10)
    cyl('distaff', S, S + Vector((0.02, -0.03, 0.55)), 0.012, WOOD, n=6)
    for k in range(9):
        ball('wool', S + Vector((0.02, -0.03, 0.55)) * 1 + Vector((rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04), -0.12 + rng.uniform(-0.06, 0.1))) - Vector((0, 0, 0)), rng.uniform(0.04, 0.06), BONE, n=8)
    sp = Vector((0.06, 0.08, Z1 + 0.025))
    cyl('spindle', sp + Vector((-0.12, 0, 0)), sp + Vector((0.14, 0.02, 0)), 0.007, WOOD, n=6)
    cyl('whorl', sp + Vector((-0.06, 0, 0)), sp + Vector((-0.04, 0, 0)), 0.025, STONE, n=10)
    for k, (h, w) in enumerate(((0.035, 0.3), (0.035, 0.27))):
        box('folded', (0.24, 0.0, Z1 + 0.012 + h * (k + 0.5)), (w, 0.24 - 0.02 * k, h), CLOTH if k == 0 else PAINT)
    export('altar_frigg')
    return dict(height=2.0, cloth=(0.2, 0.3, 0.55), paint=(0.75, 0.6, 0.3))

def tyr():
    """A hewn block cut with the Tiwaz rune, a sword driven down into its top; a chain from a ring-bolt in its side to an open fetter
    on the ground; a wolf's jaw beside the sword; a round shield, painted in halves, leant against it; a kneeling stone in front."""
    seed(61)
    pts = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for z, k in ((0, 1.0), (0.78, 1.0), (0.84, 0.94)):
                pts.append(Vector((sx * 0.42 * k, sy * 0.3 * k, z)))
    blk = hull('block', pts, STONE, 1)
    weather(blk, voxel=0.015, big=0.05, small=0.014, ratio=0.2, seed_=7)
    flatten(blk, 1, 0.3, 0.6, 1); flatten(blk, 2, 0.84, 0.7, 1)
    face = bvh(blk)
    for a, b in (((0, 0.3, 0.12), (0, 0.3, 0.66)), ((0, 0.3, 0.66), (-0.19, 0.3, 0.46)), ((0, 0.3, 0.66), (0.19, 0.3, 0.46))):
        carve(face, [Vector(a), Vector(b)], (0, -1, 0), w=0.017)
    for x in (-0.33, 0.33): carve(face, [Vector((x, 0.3, 0.1)), Vector((x, 0.3, 0.72))], (0, -1, 0), w=0.008)
    table_stone('kneel', 0.7, 0.28, 0.1, Vector((0, 0.58, 0)))
    # the sword, driven down in it, a little off true
    q = Matrix.Rotation(math.radians(6), 3, 'Y') @ Matrix.Rotation(math.radians(-4), 3, 'X')
    B = Vector((0, -0.04, 0.84))
    def P(x, y, z): return B + q @ Vector((x * 1.4, y * 1.4, z * 1.25))
    hull('blade', [P(x, y, z) for z in (-0.2, 0.56) for x, y in ((-0.028, 0), (0.028, 0), (0, 0.007), (0, -0.007))] + [P(0, 0, -0.3)], IRON, 1)
    hull('guard', [P(x, y, z) for x in (-0.11, 0.11) for y in (-0.018, 0.018) for z in (0.56, 0.59)] + [P(x, 0, 0.6) for x in (-0.12, 0.12)], GOLD, 1)
    cyl('grip', P(0, 0, 0.59), P(0, 0, 0.71), 0.017, HIDE, n=8, smooth=True)
    for k in range(5): tube('wire', [P(0.018 * math.cos(t), 0.018 * math.sin(t), 0.6 + 0.02 * k + 0.02 * t / (2 * math.pi)) for t in [i * 0.5 for i in range(13)]], 0.003, GOLD)
    hull('pommel', [P(x, y, z) for x, z in ((-0.045, 0.71), (0.045, 0.71), (-0.035, 0.75), (0.035, 0.75), (0, 0.765)) for y in (-0.02, 0.02)], GOLD, 1)
    # the wolf's jaw beside it
    J = Vector((0.2, 0.06, 0.86))
    jaw = [J + Vector((0.13 * math.cos(t), 0.17 * math.sin(t) - 0.04, 0)) for t in [math.pi * (0.1 + 0.8 * i / 14) for i in range(15)]]
    tube('jaw', jaw, 0.02, BONE, smooth=True)
    for i in range(1, 14, 2):
        cyl('tooth', jaw[i] + Vector((0, 0, 0.01)), jaw[i] + Vector((0, 0, 0.06 if i in (3, 11) else 0.032)), 0.011, BONE, n=5, r2=0.001)
    # a ring-bolt in its left side and a chain from it to an open fetter on the ground
    RB = Vector((-0.43, 0.08, 0.6))
    cyl('bolt', RB + Vector((0.03, 0, 0)), RB - Vector((0.03, 0, 0)), 0.018, IRON, n=6)
    tube('ringbolt', [RB + Vector((-0.04, 0.04 * math.cos(t), -0.04 + 0.04 * math.sin(t))) for t in [i * 2 * math.pi / 12 for i in range(12)]], 0.007, IRON, cyclic=True)
    cp = [RB + Vector((-0.04, 0, -0.08))] + [Vector((-0.52 - 0.05 * t, 0.12 + 0.3 * t, 0.52 * (1 - t) ** 1.6 + 0.02)) for t in [i / 10 for i in range(1, 11)]] + [Vector((-0.5, 0.55, 0.02))]
    chain('chain', cp, link_len=0.055, r=0.016, wire=0.0045)
    F = Vector((-0.42, 0.62, 0.022))
    tube('fetter', [F + Vector((0.07 * math.cos(t), 0.07 * math.sin(t), 0)) for t in [i * 1.6 * math.pi / 16 for i in range(17)]], 0.012, IRON)
    # the shield, leant on its right side
    c = Vector((0.6, 0.08, 0.36)); nrm = Vector((0.85, 0.2, 0.35)).normalized()
    u = nrm.cross(Vector((0, 0, 1))).normalized(); v = u.cross(nrm).normalized()
    Rs = 0.36
    for k in range(2):
        arc = [c + (u * math.cos(t) + v * math.sin(t)) * Rs for t in [math.pi / 2 + math.pi * (k + i / 12) for i in range(13)]]
        hull('shield', [c + nrm * h for h in (-0.012, 0.012)] + [p + nrm * h for p in arc for h in (-0.012, 0.012)], PAINT if k == 0 else BONE, 1)
    tube('rim', [c + (u * math.cos(t) + v * math.sin(t)) * Rs for t in [i * 2 * math.pi / 32 for i in range(32)]], 0.014, IRON, cyclic=True)
    ball('boss', c + nrm * 0.012, 0.07, IRON, squash=(1, 1, 0.7), n=10, turn=nrm.to_track_quat('Z', 'Y').to_matrix())
    export('altar_tyr')
    return dict(height=1.7, paint=(0.6, 0.12, 0.08))

def ullr():
    """A crib of logs under a deer hide; behind, a pair of skis crossed and lashed, a stag's skull and antlers at the crossing; a yew
    bow leant on the crib, a quiver of arrows; sprigs of yew on the hide."""
    seed(71)
    for k in range(4):
        z = 0.095 + k * 0.18
        if k % 2 == 0:
            for y in (-0.27, 0.27): log('log', (-0.5, y, z), (0.5, y + rng.uniform(-0.02, 0.02), z), 0.095)
        else:
            for x in (-0.4, 0.4): log('log', (x, -0.4, z), (x + rng.uniform(-0.02, 0.02), 0.4, z), 0.095)
    TZ = 0.095 + 3 * 0.18 + 0.095
    log('log', (-0.48, 0.0, TZ - 0.03), (0.48, 0.0, TZ - 0.03), 0.07)
    def drape(u, v):
        x, y = -0.6 + 1.2 * u, -0.46 + 0.92 * v
        ex, ey = 0.44, 0.32
        over = max(abs(x) - ex, abs(y) - ey, 0)
        cx, cy = max(-ex - 0.03, min(ex + 0.03, x)), max(-ey - 0.03, min(ey + 0.03, y))
        z = TZ + 0.025 + 0.012 * math.sin(u * 9) * math.sin(v * 7)
        if over > 0: z -= over * 1.6 + 0.15 * over * math.sin(u * 13 + v * 5)
        return Vector((cx + (x - cx) * 0.25, cy + (y - cy) * 0.25, z))
    sheet('hide', drape, 24, 18, HIDE, thick=0.014)
    # the skis, crossed behind, their tips turned up and forward
    for s in (-1, 1):
        a, b = Vector((s * 0.5, -0.5, 0.0)), Vector((-s * 0.32, -0.56, 1.75))
        d = (b - a).normalized(); path = [a.lerp(b, t) for t in [i / 16 for i in range(17)]]
        for i in range(1, 7): path.append(b + d * 0.04 * i + Vector((0, 0.022 * i * i / 6, 0)))
        sweep('ski', path, lambda t: [(x * 0.012, y * 0.046) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))], WOOD, fixed=(0, 1, 0))
    X = Vector((0, -0.535, 0.5 / 0.82 * 1.75 * 0.5 / 0.5 * 0.5))
    X = Vector((0, -0.53, 1.75 * 0.5 / 0.82))
    tube('lash', [X + Vector((0.07 * math.cos(t), 0.035 * math.sin(t), 0.07 * math.sin(t) * 0.3)) for t in [i * 2 * math.pi / 16 for i in range(16)]], 0.008, ROPE, cyclic=True)
    tube('lash', [X + Vector((0.07 * math.cos(t) * 0.3, 0.035 * math.sin(t), 0.07 * math.cos(t))) for t in [i * 2 * math.pi / 16 for i in range(16)]], 0.008, ROPE, cyclic=True)
    # the stag's skull and antlers at the crossing
    S = X + Vector((0, 0.07, 0.2))
    hull('skull', [S + Vector((x, y, z)) for x in (-0.05, 0.05) for y, z in ((0, 0.06), (0.02, -0.04), (0.04, -0.2))] + [S + Vector((x, 0.04, -0.21)) for x in (-0.025, 0.025)], BONE, 2)
    for sx in (-1, 1):
        ball('eye', S + Vector((sx * 0.045, 0.02, -0.02)), 0.014, DARK, squash=(0.6, 1, 1))
        base = S + Vector((sx * 0.04, 0, 0.06))
        beam_ = [base + Vector((sx * (0.32 * t + 0.08 * math.sin(math.pi * t)), 0.12 * math.sin(math.pi * t * 0.8), 0.62 * t ** 0.85)) for t in [i / 14 for i in range(15)]]
        tube('antler', beam_, 0.026, BONE, taper=lambda t: 1 - 0.7 * t)
        for i, l in ((3, 0.16), (6, 0.18), (9, 0.15), (12, 0.12), (14, 0.1)):
            p = beam_[i]; d = Vector((sx * 0.25 * (i > 10), 0.6, 0.8 if i < 12 else 0.5)).normalized()
            tube('tine', [p, p + d * l * 0.5 + Vector((0, 0, l * 0.1)), p + d * l + Vector((0, 0, l * 0.25))], 0.016, BONE, taper=lambda t: 1 - 0.75 * t)
    # a yew bow leant on the crib's front right, strung
    a, b = Vector((0.5, 0.42, 0.0)), Vector((0.36, 0.3, 1.5)); d = (b - a).normalized(); n_ = Vector((1, -0.6, 0)).normalized()
    n_ = (n_ - d * n_.dot(d)).normalized()
    bow = [a.lerp(b, t) + n_ * 0.13 * math.sin(math.pi * t) - n_ * 0.03 * (abs(2 * t - 1) ** 6) for t in [i / 20 for i in range(21)]]
    tube('bow', bow, 0.016, DARK if False else WOOD, taper=lambda t: 0.5 + 0.5 * math.sin(math.pi * t))
    tube('string', [bow[0], bow[-1]], 0.0025, ROPE, res=1)
    for t in (0.47, 0.53): tube('grip', [a.lerp(b, t) + n_ * 0.13, a.lerp(b, t + 0.01) + n_ * 0.13], 0.019, HIDE)
    # a quiver of arrows on the left
    qa, qb = Vector((-0.6, 0.36, 0.0)), Vector((-0.52, 0.22, 0.62))
    cyl('quiver', qa, qb, 0.055, HIDE, n=10, r2=0.06, smooth=True)
    tube('quiver', [qb + Vector((0.062 * math.cos(t), 0.062 * math.sin(t), 0)) for t in [i * 2 * math.pi / 14 for i in range(14)]], 0.008, DARK, cyclic=True)
    d = (qb - qa).normalized()
    for k in range(5):
        o = Vector((0.03 * math.cos(k * 1.3), 0.03 * math.sin(k * 1.3), 0))
        s0, s1 = qb + o - d * 0.1, qb + o + d * (0.22 + 0.02 * k) + Vector((0.01 * k - 0.02, 0, 0))
        cyl('arrow', s0, s1, 0.005, WOOD, n=5)
        for f in range(3):
            ang = 2 * math.pi * f / 3; w = Vector((math.cos(ang), math.sin(ang), 0)) * 0.022
            hull('fletch', [s1 - d * 0.01, s1 - d * 0.1, s1 - d * 0.03 + w, s1 - d * 0.09 + w * 0.7], BONE if f else PAINT, 1)
    # yew sprigs on the hide
    for k in range(5):
        p = Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.15, 0.2), TZ + 0.045))
        for j in range(6):
            q = Matrix.Rotation(rng.uniform(0, 6.3), 3, 'Z')
            ball('yew', p + q @ Vector((0.04, 0, 0)), 0.045, LEAF, squash=(1.0, 0.3, 0.12), n=6, turn=q)
    export('altar_ullr')
    return dict(height=1.95, paint=(0.6, 0.12, 0.08))

def hel():
    """Half and half, as she is: a table of two stones, one pale and one black, guttering candles on it, a grey shroud hung over its
    front. The game's own bones join it in the game (Pieces.HelKit): a ribcage rising behind, skulls on it, bones at its foot."""
    seed(81)
    for sx, m in ((-1, STONE), (1, CUT)):
        pts = [Vector((sx * x, y, z)) for x in (0.01, 0.5) for y in (-0.31, 0.31) for z in (0, 0.86)]
        pts += [Vector((sx * rng.uniform(0.05, 0.5), rng.uniform(-0.32, 0.32), rng.uniform(0.1, 0.8))) for _ in range(6)]
        ob = hull('half', pts, m, 2)
        weather(ob, voxel=0.015, big=0.025, small=0.008, ratio=0.2, seed_=9 + sx)
        flatten(ob, 2, 0.86, 0.85, 1)
        flatten(ob, 0, sx * 0.005, 0.95, -sx)   # (the two meet close)
    # (the spine and ribs behind, the skulls on it and the bones at its foot are the game's own: Pieces.HelKit)
    for x, y, h in ((-0.44, 0.2, 0.2), (-0.13, 0.22, 0.13), (0.15, 0.22, 0.24), (0.44, 0.12, 0.15), (-0.44, -0.22, 0.3), (0.44, -0.24, 0.22)):
        candle(Vector((x, y, 0.86)), h)
    # a grey shroud over the front, ragged at the hem
    def shroud(u, v):
        x = -0.24 + 0.48 * u
        if v < 0.35: return Vector((x, -0.05 + 0.36 * v / 0.35, 0.875))
        hem = 0.32 + 0.08 * math.sin(u * 17) + 0.05 * math.sin(u * 41)
        z = 0.875 - (0.875 - hem) * (v - 0.35) / 0.65
        return Vector((x * (1 + 0.06 * (v - 0.35)), 0.322 + 0.01 * math.sin(u * 11) * (v - 0.35), z))
    sheet('shroud', shroud, 14, 14, CLOTH, thick=0.01)
    export('altar_hel')
    return dict(height=1.8, cloth=(0.35, 0.36, 0.34))


GODS = {'odin': odin, 'thor': thor, 'freyja': freyja, 'njord': njord, 'frigg': frigg, 'tyr': tyr, 'ullr': ullr, 'hel': hel}

god = sys.argv[sys.argv.index('--') + 1]
info = GODS[god]()
if 'render' in sys.argv: preview('altar_' + god, height=info.get('height', 1.0), **{k: v for k, v in info.items() if k != 'height'})
