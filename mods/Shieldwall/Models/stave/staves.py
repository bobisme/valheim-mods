# Shieldwall's five war staves, each its own (Shieldwall 0.4.x): blender -b --python staves.py -- <kind> [render]
# Built at the size it stands in its socket: z up along the staff, its foot at z 0 (the socket swallows the bottom 0.35 m), about 4.4 m tall.
# Slots (lib.SLOTS): wood (tinted per stave), dark (charred), iron, gold (copper/bronze), bone, hide, rope, glow and crystal (the stave's
# own colours), shade, paint (clay, for the Blast stave's pots).
import sys, math
sys.path.insert(0, '/home/bob/.cache/staves')
exec(open('/home/bob/.cache/staves/lib.py').read())
args = sys.argv[sys.argv.index('--') + 1:]
kind = args[0]

def centre(z, s):
    """Where the shaft's middle wanders, at a height (a staff is never a dowel)."""
    w = min(1.0, max(0.0, (z - 0.3) / 0.9)); w = w * w * (3 - 2 * w)   # (straight and centred where the socket's ring holds it)
    return Vector((w * (0.018 * (math.sin(z * 1.3 + s) - math.sin(0.3 * 1.3 + s)) + 0.008 * math.sin(z * 3.1 + 2 * s)), w * (0.016 * (math.sin(z * 1.1 + 3 * s) - math.sin(0.3 * 1.1 + 3 * s)) + 0.007 * math.sin(z * 2.7 + s)), z))

def shaft(name, z0, z1, r0, r1, mat, s=1.0, sides=9, knots=6, n=40, flare=None):
    """The shaft: an irregular, knotted, tapering limb along centre(), from z0 to z1. flare(t) widens it (t 0..1)."""
    seed(int(s * 100))
    bumps = [(rng.uniform(0.1, 0.95), rng.uniform(0, 2 * math.pi), rng.uniform(0.012, 0.022)) for _ in range(knots)]
    wob = [rng.uniform(0.92, 1.08) for _ in range(sides)]
    def prof(t):
        z = z0 + (z1 - z0) * t
        r = r0 + (r1 - r0) * t
        if flare: r *= flare(t)
        pts = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            rr = r * wob[k] * (1 + 0.04 * math.sin(z * 9 + k * 1.7))
            for bt, ba, bh in bumps:
                d = (t - bt) * (z1 - z0) / 0.07; da = math.atan2(math.sin(a - ba), math.cos(a - ba))
                rr += bh * math.exp(-d * d) * max(0, math.cos(da)) ** 2
            pts.append((math.cos(a) * rr, math.sin(a) * rr))
        return pts
    return sweep(name, [centre(z0 + (z1 - z0) * i / n, s) for i in range(n + 1)], prof, mat, smooth=True, fixed=(1, 0, 0))

def radius_at(z, z0, z1, r0, r1):
    return r0 + (r1 - r0) * (z - z0) / (z1 - z0)

def band(z, r, h, mat, s, n=14, lip=0.012):
    c = centre(z, s)
    return cyl('band', c - Vector((0, 0, h / 2)), c + Vector((0, 0, h / 2)), r + lip, mat, n=n)

def helix(name, z0, z1, r, turns, mat, s, wire=0.01, phase=0.0, res=1):
    n = max(8, int(turns * 18))
    pts = []
    for i in range(n + 1):
        t = i / n; z = z0 + (z1 - z0) * t; a = phase + 2 * math.pi * turns * t
        pts.append(centre(z, s) + Vector((math.cos(a) * r, math.sin(a) * r, 0)))
    return tube(name, pts, wire, mat, res=res)

def wrap(z0, z1, r, mat, s, turns=None, wire=0.016):
    """A leather or rope wrap: a tight helix of a thick strap round the shaft, and a binding at each end."""
    turns = turns or (z1 - z0) / (wire * 1.7)
    helix('wrap', z0, z1, r + wire * 0.4, turns, mat, s, wire=wire, res=1)

def crystal(name, base, d, r, length, mat, sides=6, tip=0.25, spin=0.0):
    """A jagged crystal: a prism along d from base, sharpened to a point, its faces a little uneven."""
    d = Vector(d).normalized(); base = Vector(base)
    q = d.to_track_quat('Z', 'Y').to_matrix()
    pts = []
    for k in range(sides):
        a = spin + 2 * math.pi * k / sides
        o = q @ Vector((math.cos(a) * r * rng.uniform(0.85, 1.1), math.sin(a) * r * rng.uniform(0.85, 1.1), 0))
        pts += [base + o * 0.8, base + o + d * length * (1 - tip) * rng.uniform(0.9, 1.0)]
    pts.append(base + d * length)
    return hull(name, pts, mat, 1)

def spike(name, a, b, r, mat, n=6):
    a, b = Vector(a), Vector(b)
    return hull(name, [a + (b - a).to_track_quat('Z', 'Y').to_matrix() @ Vector((math.cos(2 * math.pi * k / n) * r, math.sin(2 * math.pi * k / n) * r, 0)) for k in range(n)] + [b], mat, 1)

def foot(s, r=0.085):
    """The iron shoe at its foot (mostly in the socket) and a band just above where the socket's lip will be."""
    c = centre(0.0, s)
    hull('shoe', [c + Vector((math.cos(a) * (r + 0.01), math.sin(a) * (r + 0.01), 0.22)) for a in [k * math.pi / 4 for k in range(8)]] +
         [c + Vector((math.cos(a) * r * 0.55, math.sin(a) * r * 0.55, 0.0)) for a in [k * math.pi / 4 for k in range(8)]], IRON, 1)
    band(0.24, r, 0.05, IRON, s)
    band(0.42, r, 0.035, IRON, s)

def vein(name, z0, z1, r0, r1, s, a0, mat, wiggle=0.6, wire=0.007, n=40, sunk=0.004):
    """A line wandering up the shaft's skin (a glowing crack, an inlay)."""
    seed(int(a0 * 1000) + 7)
    pts = []; a = a0
    for i in range(n + 1):
        t = i / n; z = z0 + (z1 - z0) * t; r = r0 + (r1 - r0) * t - sunk
        a += rng.uniform(-wiggle, wiggle) * 0.2
        pts.append(centre(z, s) + Vector((math.cos(a) * r, math.sin(a) * r, 0)))
    return tube(name, pts, wire, mat, res=1)


# ---------------------------------------------------------------------------------------------------------------------------------
def ember():
    """Charred black oak cracked through with fire; an iron cage of curling flame-tines round a great smouldering coal."""
    s = 1.0; Z1 = 3.38
    shaft('shaft', 0.0, Z1, 0.085, 0.07, DARK, s, knots=8)
    foot(s)
    for k in range(5):                                                                                     # its cracks, glowing
        vein('crack', 0.5 + 0.17 * k, Z1 - 0.1 - 0.13 * (k % 3), 0.087, 0.072, s, k * 2 * math.pi / 5 + 0.3, GLOW, wiggle=1.4, wire=0.0065)
    band(2.05, 0.077, 0.06, IRON, s); band(1.15, 0.081, 0.05, IRON, s)
    c = centre(Z1, s)
    cyl('collar', c + Vector((0, 0, -0.18)), c + Vector((0, 0, 0.02)), 0.095, IRON, n=10, r2=0.115)
    for k in range(8):                                                                                     # spurs under the collar
        a = k * math.pi / 4 + 0.2
        spike('spur', c + Vector((math.cos(a) * 0.09, math.sin(a) * 0.09, -0.12)), c + Vector((math.cos(a) * 0.17, math.sin(a) * 0.17, -0.3)), 0.016, IRON)
    # the flame-tines: up from the collar, out round the coal, in over it and licking up past it, each with a twist
    for k in range(6):
        a0 = k * math.pi / 3; pts = []
        for i in range(25):
            t = i / 24
            z = c.z + 0.02 + 1.0 * t
            rr = 0.1 + 0.27 * math.sin(math.pi * min(1, t / 0.8)) ** 0.8 if t < 0.8 else 0.1 - 0.05 * math.sin(math.pi * (t - 0.8) / 0.4) + (0.1 if k % 2 else 0.05) * ((t - 0.8) / 0.2) ** 2
            a = a0 + 0.9 * t
            pts.append(Vector((c.x + math.cos(a) * rr, c.y + math.sin(a) * rr, z + (0.15 if k % 2 else 0) * t ** 3)))
        tube('tine', pts, 0.024, IRON, taper=lambda t: 1.0 - 0.75 * t, res=1)
    # the coal: a great faceted ember, crusted black over its glow
    seed(11)
    core = c + Vector((0, 0, 0.48))
    coal = hull('coal', [core + Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1) * 1.15)).normalized() * 0.25 * rng.uniform(0.82, 1.05) for _ in range(26)], DARK, 1)
    weather(coal, voxel=0.012, big=0.035, small=0.012, ratio=0.35, seed_=3)
    tree = bvh(coal)
    def on(d, lift):
        hit = tree.ray_cast(core + d * 0.6, -d)
        return (hit[0] if hit[0] is not None else core + d * 0.2) + d * lift
    for k in range(19):                                                                                   # its cracks, glowing: forking walks over it
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        heading = d.orthogonal().normalized(); walk = [d]
        for _ in range(rng.randint(5, 9)):
            heading = (Matrix.Rotation(rng.uniform(-0.7, 0.7), 3, walk[-1]) @ heading).normalized()
            nd = (walk[-1] + heading * 0.2).normalized(); heading = (heading - nd * heading.dot(nd)).normalized(); walk.append(nd)
        for a, b in zip(walk, walk[1:]):
            side = (b - a).cross(a).normalized() * rng.uniform(0.01, 0.02)
            hull('crack', [on((x + o).normalized(), l) for x in (a, b) for o in (side, -side) for l in (-0.012, 0.004)], GLOW, 1)


def frost():
    """Pale, rime-white wood; a burst of ice crystals clasped in an iron crown, icicles hanging from it, frost creeping down the shaft."""
    s = 2.3; Z1 = 3.25
    shaft('shaft', 0.0, Z1, 0.082, 0.068, WOOD, s, knots=5)
    foot(s, 0.082)
    wrap(1.7, 2.25, 0.076, HIDE, s, wire=0.017)
    band(1.68, 0.077, 0.03, IRON, s); band(2.27, 0.074, 0.03, IRON, s)
    c = centre(Z1, s)
    cyl('crown', c + Vector((0, 0, -0.12)), c + Vector((0, 0, 0.05)), 0.085, IRON, n=8, r2=0.1)
    seed(23)
    # the great crystal and the burst round it
    crystal('ice', c + Vector((0, 0, 0.02)), (0.04, -0.02, 1), 0.15, 1.3, CRYSTAL, tip=0.3)
    for k in range(12):
        a = k * 2 * math.pi / 12 + rng.uniform(-0.2, 0.2); up = rng.uniform(0.6, 2.0)
        d = Vector((math.cos(a), math.sin(a), up)).normalized()
        crystal('ice', c + Vector((math.cos(a) * 0.05, math.sin(a) * 0.05, 0.05 + rng.uniform(0, 0.12))), d, rng.uniform(0.05, 0.085), rng.uniform(0.35, 0.8), CRYSTAL, tip=0.35, spin=rng.uniform(0, 1))
    # the crown's prongs, clasping the crystals, icicles hanging off them
    for k in range(5):
        a = k * 2 * math.pi / 5 + 0.3
        p0 = c + Vector((math.cos(a) * 0.09, math.sin(a) * 0.09, 0.0))
        p1 = c + Vector((math.cos(a) * 0.2, math.sin(a) * 0.2, 0.2)); p2 = c + Vector((math.cos(a) * 0.16, math.sin(a) * 0.16, 0.42))
        tube('prong', [p0, p0.lerp(p1, 0.5) + Vector((0, 0, -0.02)), p1, p2], 0.02, IRON, taper=lambda t: 1 - 0.6 * t, res=1)
        for j, (f, l) in enumerate(((0.55, 0.22), (0.85, 0.14))):
            base = p0.lerp(p1, f) + Vector((math.cos(a) * 0.01, math.sin(a) * 0.01, -0.02))
            crystal('icicle', base, (rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), -1), 0.018, l + rng.uniform(-0.04, 0.05), CRYSTAL, sides=5, tip=0.7)
    # icicles round the crown's rim
    for k in range(11):
        a = k * 2 * math.pi / 11 + 0.15
        base = c + Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, -0.11))
        crystal('icicle', base, (math.cos(a) * 0.15, math.sin(a) * 0.15, -1), 0.014, rng.uniform(0.08, 0.2), CRYSTAL, sides=5, tip=0.7)
    # rime: small crystals growing out of the wood, thick near the head and thinning down the shaft
    for k in range(34):
        z = Z1 - 0.15 - 0.95 * rng.random() ** 1.8
        a = rng.uniform(0, 2 * math.pi); r = radius_at(z, 0, Z1, 0.082, 0.068)
        d = Vector((math.cos(a), math.sin(a), rng.uniform(0.2, 1.2))).normalized()
        crystal('rime', centre(z, s) + Vector((math.cos(a) * (r - 0.01), math.sin(a) * (r - 0.01), 0)), d, rng.uniform(0.01, 0.02), rng.uniform(0.04, 0.12), CRYSTAL, sides=4, tip=0.5)


def thunder():
    """A storm-split ash limb: forked at the top round a violet storm-stone bound in copper wire, bolts of light between; a zigzag of
    lightning inlaid down the shaft, copper coils."""
    s = 4.1; Z1 = 2.95
    shaft('shaft', 0.0, Z1 + 0.06, 0.088, 0.078, WOOD, s, knots=6)
    foot(s, 0.088)
    c = centre(Z1, s)
    # the fork: two tines, the left the taller, tipped with iron
    tips = []
    for sx, h, lean in ((-1, 1.45, 0.24), (1, 1.2, 0.22)):
        pts = [c + Vector((sx * lean * math.sin(min(1, t * 1.6) * math.pi / 2) - sx * 0.05 * max(0, t - 0.7) / 0.3, 0.02 * math.sin(t * 3 + sx), h * t)) for t in [i / 16 for i in range(17)]]
        pts[0] = c + Vector((sx * 0.01, 0, -0.06))
        tube('tine', pts, 0.07, WOOD, taper=lambda t: 1.0 - 0.6 * t, res=2)
        tip = pts[-1]; tips.append((pts, tip))
        spike('tip', tip - Vector((0, 0, 0.06)), tip + Vector((sx * -0.02, 0, 0.28)), 0.035, IRON, n=7)
        cyl('tipband', tip - Vector((0, 0, 0.1)), tip - Vector((0, 0, 0.02)), 0.038, IRON, n=8)
    # copper coils round the fork's root and the grip
    helix('coil', Z1 - 0.32, Z1 - 0.02, 0.09, 7, GOLD, s, wire=0.009)
    helix('coil', 1.75, 1.98, 0.092, 6, GOLD, s, wire=0.009)
    band(Z1 - 0.34, 0.083, 0.03, IRON, s); band(1.72, 0.086, 0.03, IRON, s); band(2.0, 0.085, 0.03, IRON, s)
    # the storm-stone: a faceted orb held between the tines by copper wire
    orb = c + Vector((0.01, 0.0, 0.72))
    seed(41)
    hull('orb', [orb + Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized() * 0.2 * rng.uniform(0.92, 1.05) for _ in range(34)], GLOW, 1)
    for pts, tip in tips:                                                                                   # its wire cradle
        a = min(pts, key=lambda p: abs(p.z - orb.z))
        for dz in (-0.08, 0.0, 0.08):
            p = Vector((a.x, a.y, orb.z + dz))
            mid = orb.lerp(p, 0.5) + Vector((0, 0.05 if dz > 0 else -0.05, 0))
            tube('wire', [p, mid, orb + (p - orb).normalized() * 0.18], 0.008, GOLD, res=1)
        # bolts of light from the stone to the tine, jagged: few, long legs
        for j, f in enumerate((0.78, 0.97)):
            seed(50 + j + int(tip.x * 100))
            q0 = pts[min(len(pts) - 1, int((len(pts) - 1) * f))]; a0 = orb + (q0 - orb).normalized() * 0.17; zig = [a0]
            n = 5
            for i in range(1, n):
                t = i / n; p = a0.lerp(q0, t); side = (q0 - a0).cross(Vector((0, 1, 0))).normalized()
                zig.append(p + side * (0.05 if i % 2 else -0.05) + Vector((0, rng.uniform(-0.02, 0.02), 0)))
            zig.append(q0)
            for a, b in zip(zig, zig[1:]): cyl('bolt', a, b, 0.009, GLOW, n=5, r2=0.007)
    # a bolt inlaid down the shaft, front and back: a jagged line of light, set in iron
    for side in (0, math.pi):
        seed(int(side * 10) + 3)
        pts = []; z = Z1 - 0.4; a = side + 0.0; k = 0
        while z > 0.55:
            r = radius_at(z, 0, Z1, 0.088, 0.078) + 0.002
            pts.append(centre(z, s) + Vector((math.cos(a) * r, math.sin(a) * r, 0)))
            z -= rng.uniform(0.12, 0.2); a = side + (0.45 if k % 2 == 0 else -0.45); k += 1
        dense = []
        for p, q in zip(pts, pts[1:]):                                                                     # (round the shaft, not through it)
            for i in range(6):
                t = i / 6; m = p.lerp(q, t); cc = centre(m.z, s); o = (m - cc); o.z = 0
                dense.append(cc + o.normalized() * (radius_at(m.z, 0, Z1, 0.088, 0.078) + 0.002))
        tube('inlay', dense, 0.011, IRON, res=1)
        tube('bolt', [p + (p - centre(p.z, s)).normalized() * 0.006 - Vector((0, 0, 0)) for p in dense], 0.006, GLOW, res=1)


def blast():
    """A great club of knotted oak, its head an iron cage packed with clay fire-pots, each fuse smouldering, an iron spike through it."""
    s = 6.2; Z1 = 3.15
    shaft('shaft', 0.0, Z1, 0.09, 0.1, WOOD, s, knots=10, flare=lambda t: 1 + 0.35 * max(0, t - 0.7) / 0.3)
    foot(s, 0.09)
    for z in (1.1, 2.3): band(z, radius_at(z, 0, Z1, 0.09, 0.1), 0.05, IRON, s)
    wrap(1.45, 2.0, 0.093, ROPE, s, wire=0.013)
    c = centre(Z1, s)
    # the cage: a ring under the pots and one over, straps between them
    lo, hi, R = c.z + 0.02, c.z + 0.5, 0.3
    cyl('seat', c + Vector((0, 0, -0.1)), c + Vector((0, 0, 0.05)), 0.135, IRON, n=10, r2=0.17)
    for z, r in ((lo + 0.03, R * 0.8), (hi, R * 0.9)):
        tube('hoop', [Vector((c.x + math.cos(a) * r, c.y + math.sin(a) * r, z)) for a in [k * 2 * math.pi / 24 for k in range(24)]], 0.02, IRON, cyclic=True, res=1)
    for k in range(6):
        a = k * math.pi / 3 + math.pi / 6
        tube('strap', [Vector((c.x + math.cos(a) * 0.14, c.y + math.sin(a) * 0.14, c.z - 0.02)), Vector((c.x + math.cos(a) * R * 0.8, c.y + math.sin(a) * R * 0.8, lo + 0.03)),
                       Vector((c.x + math.cos(a) * R * 1.02, c.y + math.sin(a) * R * 1.02, (lo + hi) / 2)), Vector((c.x + math.cos(a) * R * 0.9, c.y + math.sin(a) * R * 0.9, hi)),
                       Vector((c.x + math.cos(a) * 0.06, c.y + math.sin(a) * 0.06, hi + 0.12))], 0.017, IRON, res=1)
        # a stud on each strap's belly
        spike('stud', Vector((c.x + math.cos(a) * R * 1.0, c.y + math.sin(a) * R * 1.0, (lo + hi) / 2)), Vector((c.x + math.cos(a) * (R + 0.13), c.y + math.sin(a) * (R + 0.13), (lo + hi) / 2)), 0.028, IRON)
    # the pots: round-bellied clay standing upright in the cage, each stopped with a hide cap tied on, a fuse of tow curling out, its end alight
    seed(61)
    for k in range(6):
        a = k * math.pi / 3
        b = Vector((c.x + math.cos(a) * 0.2, c.y + math.sin(a) * 0.2, lo + 0.17))
        ball('pot', b, 0.125, PAINT, squash=(1, 1, 0.92), n=14)
        cyl('neck', b + Vector((0, 0, 0.08)), b + Vector((0, 0, 0.16)), 0.06, PAINT, n=12, r2=0.045)
        cyl('lip', b + Vector((0, 0, 0.15)), b + Vector((0, 0, 0.18)), 0.058, PAINT, n=12)
        hull('cap', [b + Vector((math.cos(t) * 0.064, math.sin(t) * 0.064, 0.165)) for t in [i * math.pi / 5 for i in range(10)]] +
             [b + Vector((math.cos(t) * 0.05, math.sin(t) * 0.05, 0.205)) for t in [i * math.pi / 5 for i in range(10)]], HIDE, 1)
        cyl('tie', b + Vector((0, 0, 0.16)), b + Vector((0, 0, 0.175)), 0.067, ROPE, n=12)
        out = Vector((math.cos(a), math.sin(a), 0))
        f0 = b + Vector((0, 0, 0.2)); f1 = f0 + out * 0.04 + Vector((0, 0, 0.06)); f2 = f0 + out * 0.09 + Vector((0, 0, 0.07))
        tube('fuse', [f0, f1, f2], 0.008, ROPE, res=1)
        ball('spark', f2 + out * 0.01, 0.017, GLOW, n=6)
    # the spike through the middle, and the iron cap it rises from
    cap = Vector((c.x, c.y, hi + 0.12))
    cyl('cap', cap - Vector((0, 0, 0.06)), cap + Vector((0, 0, 0.06)), 0.1, IRON, n=10, r2=0.06)
    spike('spike', cap, cap + Vector((0, 0, 0.75)), 0.05, IRON, n=8)
    for k in range(4):                                                                                    # its flanges
        a = k * math.pi / 2
        hull('flange', [cap + Vector((0, 0, 0.1)), cap + Vector((0, 0, 0.55)), cap + Vector((math.cos(a) * 0.11, math.sin(a) * 0.11, 0.18)), cap + Vector((math.cos(a) * 0.045, math.sin(a) * 0.045, 0.42))] +
             [cap + Vector((-math.sin(a) * 0.012, math.cos(a) * 0.012, 0.2)), cap + Vector((math.sin(a) * 0.012, -math.cos(a) * 0.012, 0.2))], IRON, 1)
    # scorch: the wood blackened just under the head
    shaft('scorch', Z1 - 0.32, Z1 - 0.08, 0.104, 0.12, DARK, s, knots=3, sides=9)


def hearth():
    """A herder's crook of warm oak, banded in bronze, an iron lantern hung from its hook with a steady fire in it; charms of bone and amber
    tied below the bend, a woven wrap for the hand."""
    s = 8.4; Z1 = 3.3
    shaft('shaft', 0.0, Z1 + 0.04, 0.08, 0.07, WOOD, s, knots=5)
    foot(s, 0.08)
    wrap(1.6, 2.15, 0.074, HIDE, s, wire=0.015)
    for z in (1.58, 2.17, 2.9): band(z, radius_at(z, 0, Z1, 0.08, 0.07), 0.03, GOLD, s)
    c = centre(Z1, s)
    # the crook: up, over and down, tapering, banded at the turn
    hook = []
    for i in range(29):
        t = i / 28; a = math.pi * 1.15 * t
        hook.append(c + Vector((0.26 - 0.26 * math.cos(a), 0.0, 0.26 * math.sin(a) + 0.18 * min(1, t * 3))))
    tube('crook', hook, 0.07, WOOD, taper=lambda t: 1 - 0.4 * t, res=2)
    end = hook[-1]
    ball('knob', end, 0.05, WOOD, n=10)
    for t in (0.3, 0.62):
        i = int(t * 28); p, q = hook[i], hook[i + 1]
        d = (q - p).normalized()
        cyl('turnband', p - d * 0.025, p + d * 0.025, 0.06 * (1 - 0.4 * t) + 0.012, GOLD, n=12)
    # the lantern: hung on a chain from the hook's end
    chain('chain', [end - Vector((0, 0, 0.04)), end - Vector((0, 0, 0.3))], link_len=0.06, r=0.016, wire=0.005, mat=IRON)
    top = end - Vector((0, 0, 0.32)); H, W = 0.36, 0.11
    tube('bail', [top + Vector((-0.06, 0, -0.06)), top + Vector((-0.04, 0, 0.0)), top + Vector((0.04, 0, 0.0)), top + Vector((0.06, 0, -0.06))], 0.008, IRON, res=1)
    hull('roof', [top + Vector((x * (W + 0.03), y * (W + 0.03), -0.09)) for x in (-1, 1) for y in (-1, 1)] + [top + Vector((0, 0, -0.01))], IRON, 1)
    box('base', top + Vector((0, 0, -0.09 - H)), (2 * W + 0.04, 2 * W + 0.04, 0.04), IRON)
    for x in (-1, 1):
        for y in (-1, 1):
            cyl('post', top + Vector((x * W, y * W, -0.09)), top + Vector((x * W, y * W, -0.09 - H)), 0.012, IRON, n=6)
    for z in (-0.09 - H * 0.35, -0.09 - H * 0.7):                                                       # its bars
        for x, y, xx, yy in ((-1, -1, 1, -1), (1, -1, 1, 1), (1, 1, -1, 1), (-1, 1, -1, -1)):
            cyl('bar', top + Vector((x * W, y * W, z)), top + Vector((xx * W, yy * W, z)), 0.007, IRON, n=5)
    box('pane', top + Vector((0, 0, -0.09 - H / 2)), (2 * W - 0.025, 2 * W - 0.025, H - 0.03), CRYSTAL)          # (horn panes, lit from within)
    ball('flame', top + Vector((0, 0, -0.09 - H * 0.55)), 0.07, GLOW, squash=(1, 1, 1.5), n=10)
    # charms on cords, tied under the bend
    seed(77)
    knot = c + Vector((0.0, 0.0, 0.1))
    for k, (dx, dy, l, what) in enumerate(((0.05, 0.07, 0.42, 'amber'), (-0.06, 0.05, 0.3, 'bone'), (0.07, -0.05, 0.36, 'disc'), (-0.04, -0.07, 0.48, 'amber'))):
        p0 = knot + Vector((dx * 0.9, dy * 0.9, 0)); p1 = p0 + Vector((dx * 0.5, dy * 0.5, -l))
        tube('cord', [p0, p0.lerp(p1, 0.5) + Vector((dx * 0.2, dy * 0.2, 0)), p1], 0.005, ROPE, res=1)
        if what == 'amber':
            for j in range(3): ball('bead', p0.lerp(p1, 0.55 + 0.17 * j), 0.022, GOLD, n=8)
            hull('drop', [p1 + Vector((rng.uniform(-1, 1) * 0.03, rng.uniform(-1, 1) * 0.03, -rng.uniform(0, 0.07))) for _ in range(10)], GLOW, 1)
        elif what == 'bone':
            cyl('tooth', p1, p1 + Vector((0.01, 0.0, -0.13)), 0.018, BONE, n=7, r2=0.004)
        else:
            cyl('disc', p1 + Vector((0, -0.006, -0.05)), p1 + Vector((0, 0.006, -0.05)), 0.05, BONE, n=12)
    tube('lash', [knot + Vector((math.cos(a) * 0.078, math.sin(a) * 0.078, 0.01 * a)) for a in [i * 0.4 for i in range(40)]], 0.007, ROPE, res=1)


{'ember': ember, 'frost': frost, 'thunder': thunder, 'blast': blast, 'hearth': hearth}[kind]()
export('stave_' + kind)
if 'render' in args:
    glows = {'ember': ((1.0, 0.35, 0.06), None), 'frost': ((0.4, 0.75, 1.0), (0.7, 0.88, 1.0)), 'thunder': ((0.7, 0.45, 1.0), None),
             'blast': ((1.0, 0.45, 0.1), None), 'hearth': ((1.0, 0.7, 0.3), (1.0, 0.8, 0.5))}
    g, cr = glows[kind]
    preview('prev_' + kind, glow=g, crystal=cr, paint=(0.55, 0.3, 0.18) if kind == 'blast' else None,
            views=((25, 7.5, 2.4, 2.3), (115, 7.5, 2.4, 2.3), (30, 2.4, 3.9, 3.7)))
