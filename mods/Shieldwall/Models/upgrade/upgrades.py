# Shieldwall's Warstone upgrades, each its own (Shieldwall 0.4.19): blender -b --python upgrades.py -- <banner|totem|brazier|obelisk> [render]
# z up, foot on the ground at z 0, the front toward +y (the piece's forward). Slots (lib.SLOTS): wood, dark, iron, gold (bronze), bone, hide,
# rope, glow (embers), cloth (the banner's red), paint (black), shade, rock and rune (the Warstone's own rock and glowing rune).
import sys, math
sys.path.insert(0, '/home/bob/.cache/upgrades')
exec(open('/home/bob/.cache/upgrades/lib.py').read())
args = sys.argv[sys.argv.index('--') + 1:]
kind = args[0]

def pole(name, z0, z1, r0, r1, mat, s=1.0, sides=10, n=16, at=(0, 0)):
    """A hewn pole: a slightly irregular, tapering limb, standing up z."""
    seed(int(s * 100))
    wob = [rng.uniform(0.93, 1.07) for _ in range(sides)]
    def prof(t):
        r = r0 + (r1 - r0) * t
        return [(math.cos(2 * math.pi * k / sides) * r * wob[k], math.sin(2 * math.pi * k / sides) * r * wob[k]) for k in range(sides)]
    path = [Vector((at[0] + 0.012 * math.sin(z0 + (z1 - z0) * i / n * 1.7 + s), at[1] + 0.01 * math.sin((z0 + (z1 - z0) * i / n) * 1.3 + 2 * s), z0 + (z1 - z0) * i / n)) for i in range(n + 1)]
    return sweep(name, path, prof, mat, smooth=True, fixed=(1, 0, 0))

def spike(name, a, b, r, mat, n=6):
    a, b = Vector(a), Vector(b)
    q = (b - a).to_track_quat('Z', 'Y').to_matrix()
    return hull(name, [a + q @ Vector((math.cos(2 * math.pi * k / n) * r, math.sin(2 * math.pi * k / n) * r, 0)) for k in range(n)] + [b], mat, 1)

def cairn(r, h, count, s, mat=ROCK):
    """Rough stones heaped round a foot."""
    seed(s)
    for k in range(count):
        a = k * 2 * math.pi / count + rng.uniform(-0.2, 0.2); rr = r * rng.uniform(0.75, 1.0)
        lump('stone', Vector((math.cos(a) * rr, math.sin(a) * rr, h * rng.uniform(0.3, 0.6))), rng.uniform(0.14, 0.22) * (r / 0.4), mat, squash=0.6, n=14)

def wraps(c, r, z0, z1, mat=ROPE, wire=0.012):
    turns = (z1 - z0) / (wire * 1.8); n = int(turns * 14)
    tube('wrap', [Vector((c[0] + math.cos(2 * math.pi * turns * i / n) * (r + wire * 0.5), c[1] + math.sin(2 * math.pi * turns * i / n) * (r + wire * 0.5), z0 + (z1 - z0) * i / n)) for i in range(n + 1)], wire, mat, res=1)

# Elder futhark, as strokes in a cell 0.5 wide by 1 high (x across, y up)
RUNES = {
    't': [((0.25, 0), (0.25, 1)), ((0.25, 1), (0, 0.72)), ((0.25, 1), (0.5, 0.72))],
    'f': [((0.1, 0), (0.1, 1)), ((0.1, 0.62), (0.45, 0.88)), ((0.1, 0.36), (0.45, 0.62))],
    'u': [((0.08, 0), (0.08, 1)), ((0.08, 1), (0.42, 0.7)), ((0.42, 0.7), (0.42, 0))],
    'th': [((0.1, 0), (0.1, 1)), ((0.1, 0.72), (0.42, 0.5)), ((0.42, 0.5), (0.1, 0.28))],
    'a': [((0.1, 0), (0.1, 1)), ((0.1, 1), (0.45, 0.76)), ((0.1, 0.72), (0.45, 0.48))],
    'r': [((0.1, 0), (0.1, 1)), ((0.1, 1), (0.42, 0.78)), ((0.42, 0.78), (0.1, 0.52)), ((0.1, 0.52), (0.42, 0))],
    'k': [((0.38, 1), (0.1, 0.5)), ((0.1, 0.5), (0.38, 0))],
    'g': [((0.05, 0.05), (0.45, 0.95)), ((0.45, 0.05), (0.05, 0.95))],
    'h': [((0.08, 0), (0.08, 1)), ((0.42, 0), (0.42, 1)), ((0.08, 0.65), (0.42, 0.35))],
    'n': [((0.25, 0), (0.25, 1)), ((0.08, 0.66), (0.42, 0.4))],
    'i': [((0.25, 0), (0.25, 1))],
    'z': [((0.25, 0), (0.25, 1)), ((0.25, 0.55), (0.02, 0.92)), ((0.25, 0.55), (0.48, 0.92))],
    's': [((0.38, 1), (0.1, 0.62)), ((0.1, 0.62), (0.4, 0.38)), ((0.4, 0.38), (0.12, 0))],
    'b': [((0.1, 0), (0.1, 1)), ((0.1, 1), (0.42, 0.76)), ((0.42, 0.76), (0.1, 0.5)), ((0.1, 0.5), (0.42, 0.24)), ((0.42, 0.24), (0.1, 0))],
    'e': [((0.05, 0), (0.05, 1)), ((0.45, 0), (0.45, 1)), ((0.05, 1), (0.25, 0.7)), ((0.25, 0.7), (0.45, 1))],
    'm': [((0.05, 0), (0.05, 1)), ((0.45, 0), (0.45, 1)), ((0.05, 1), (0.45, 0.6)), ((0.45, 1), (0.05, 0.6))],
    'l': [((0.12, 0), (0.12, 1)), ((0.12, 1), (0.42, 0.72))],
    'd': [((0.05, 0.05), (0.05, 0.95)), ((0.45, 0.05), (0.45, 0.95)), ((0.05, 0.95), (0.45, 0.05)), ((0.05, 0.05), (0.45, 0.95))],
    'o': [((0.25, 1), (0.05, 0.62)), ((0.05, 0.62), (0.25, 0.3)), ((0.25, 1), (0.45, 0.62)), ((0.45, 0.62), (0.25, 0.3)), ((0.25, 0.3), (0.02, 0)), ((0.25, 0.3), (0.48, 0))],
}

def groove(a, b, w, deep, out, cutters, cap=0.3):
    """A cutter for one stroke of a carving: a bar from a to b (on the surface), w wide, cutting deep in along -out."""
    a, b, out = Vector(a), Vector(b), Vector(out).normalized()
    d = (b - a).normalized(); side = d.cross(out).normalized() * w / 2
    pts = [p + o + out * h for p in (a - d * w * cap, b + d * w * cap) for o in (side, -side) for h in (-deep, 0.06)]
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    me = bpy.data.meshes.new('cut'); bm.to_mesh(me); bm.free(); me.materials.append(mats[RUNE])
    ob = bpy.data.objects.new('cut', me); bpy.context.scene.collection.objects.link(ob)
    cutters.append(ob)

def cut_all(target, cutters, inside=None):
    """Cut every groove into a target, one at a time (stacked booleans eat a tidy hull), the cut faces in the rune's paint."""
    bm = bmesh.new(); bm.from_mesh(target.data)
    planes = [(f.normal.copy(), f.normal.dot(f.calc_center_median())) for f in bm.faces]   # (its outer faces, before any cut)
    bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(target.data); bm.free()
    import bmesh as _b
    def volume(ob):
        bm = _b.new(); bm.from_mesh(ob.data); v = bm.calc_volume(); bm.free(); return v
    skipped = 0
    for c in cutters:
        for attempt in range(4):
            before = volume(target); keep = target.data.copy()
            if attempt:   # (a cut gone wrong is usually edges lining up exactly: nudge the cutter a hair and try again)
                c.location = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.0005 * attempt
            m = target.modifiers.new('rune', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.object = c; m.solver = 'EXACT'; m.material_mode = 'TRANSFER'
            bpy.context.view_layer.objects.active = target; bpy.ops.object.modifier_apply(modifier=m.name)
            after = volume(target)
            proud = inside is not None and any(not inside(v.co) for v in target.data.vertices)
            if after < before * 0.97 or after > before or proud:   # (keep the stone as it was)
                target.data = keep
                if attempt == 3: skipped += 1
                continue
            break
        bpy.data.objects.remove(c)
    # whatever still lies on the outer surface is stone, whatever the cuts handed it (the cuts' paint only in the grooves)
    stone = target.data.materials[:].index(mats[ROCK]) if mats[ROCK] in target.data.materials[:] else 0
    fixed = 0
    for f in target.data.polygons:
        c = f.center
        if any(f.normal.dot(n) > 0.999 and abs(n.dot(c) - d) < 0.002 for n, d in planes) and f.material_index != stone:
            f.material_index = stone; fixed += 1
    print('cuts', len(cutters), 'skipped', skipped, 'repainted', fixed)



def skull(O, SK, horns='curl', tilt=-12, yaw=0.0, horn_mat=HIDE):
    """A horned beast's skull (the goat's, from Jarldom's Thor altar), muzzle toward +y turned by yaw: its eye sockets and nasal opening cut
    and dark within, its molars; horns 'curl' (a goat's, up and back) or 'long' (an aurochs', out to the sides and up)."""
    q = Matrix.Rotation(yaw, 3, 'Z') @ Matrix.Rotation(math.radians(tilt), 3, 'X')
    O = Vector(O)
    def P(x, y, z): return O + q @ (Vector((x, y, z)) * SK)
    sk = []
    for y, hx, z0, z1 in ((0.0, 0.042, 0.02, 0.075), (0.03, 0.056, 0.0, 0.104), (0.075, 0.062, -0.002, 0.11), (0.115, 0.05, 0.0, 0.088),
                          (0.18, 0.037, 0.002, 0.06), (0.25, 0.027, 0.006, 0.04), (0.3, 0.016, 0.012, 0.028)):
        sk += [P(x, y, z) for x in (-hx, hx) for z in (z0, z1)] + [P(x * 0.7, y, z1 + 0.012) for x in (-hx, hx)] + [P(x * 0.8, y, z0 - 0.006) for x in (-hx, hx)]
    sk += [P(0, y, z + 0.014) for y, z in ((0.03, 0.104), (0.075, 0.11), (0.18, 0.06), (0.25, 0.04))]
    ob = hull('skull', sk, BONE, 1)
    bm = bmesh.new(); bm.from_mesh(ob.data); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(ob.data); bm.free()
    for c, r in [((sx * 0.066, 0.088, 0.066), (0.024, 0.026, 0.021)) for sx in (-1, 1)] + [((0, 0.285, 0.045), (0.01, 0.05, 0.014))]:
        cut = ball('socket', P(*c), 1.0 * SK, SHADE, squash=r, n=12, turn=q)
        mod = ob.modifiers.new('cut', 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.object = cut; mod.solver = 'EXACT'; mod.material_mode = 'TRANSFER'
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        ob.modifiers.clear(); old = ob.data; ob.data = me; bpy.data.meshes.remove(old); bpy.data.objects.remove(cut)
    for sx in (-1, 1):
        for k in range(5):
            y = 0.12 + 0.024 * k; hx = 0.04 - 0.004 * k
            hull('tooth', [P(sx * (hx + dx), y + dy, z) for dx in (-0.008, 0.002) for dy in (-0.009, 0.009) for z in (-0.002, -0.014)], BONE, 1)
    for sx in (-1, 1):
        base = P(sx * 0.03, 0.065, 0.1); pts = []
        if horns == 'curl':
            R_ = 0.2
            for i in range(25):
                t = i / 24; th = t * 0.82 * math.pi
                pts.append(base + q @ (Vector((sx * (0.012 + 0.16 * t ** 1.3), -R_ * (1 - math.cos(th)), R_ * math.sin(th))) * SK / 1.4))
            r0 = 0.04 * SK / 1.4
        else:
            for i in range(25):
                t = i / 24
                pts.append(base + q @ (Vector((sx * (0.04 + 0.3 * math.sin(t * math.pi * 0.55)), -0.04 * t - 0.03 * math.sin(t * math.pi), 0.02 + 0.24 * t ** 1.8)) * SK))
            r0 = 0.045 * SK
        rad = lambda t: 1 - 0.85 * t
        tube('horn', pts, r0, horn_mat, taper=rad)
        for i in range(2, 21, 2):
            t = i / 24; d = (pts[i + 1] - pts[i - 1]).normalized(); u = d.cross(Vector((1, 0, 0))).normalized()
            if u.length < 0.1: u = d.cross(Vector((0, 0, 1))).normalized()
            w = d.cross(u).normalized(); r = r0 * rad(t) * 1.06
            tube('ridge', [pts[i] + (u * math.cos(a) + w * math.sin(a)) * r for a in [k * 2 * math.pi / 10 for k in range(10)]], 0.0045 * SK / 1.4 * (1 - 0.6 * t), horn_mat, cyclic=True, res=1)

def shield(c, r, out, mat_a=WOOD, mat_b=CLOTH, tilt=0.0, seed_=1):
    """A captured round shield, battered: planks, half painted, an iron rim with a bite out of it, the boss, nails."""
    seed(seed_)
    c = Vector(c); out = Vector(out).normalized()
    up = Vector((0, 0, 1)); right = out.cross(up).normalized(); up = right.cross(out).normalized()
    rot = Matrix.Rotation(tilt, 3, out); right = rot @ right; up = rot @ up
    n = 28; th = 0.022
    for k in range(5):   # five planks across it
        x0, x1 = -r + 2 * r * k / 5 + 0.004, -r + 2 * r * (k + 1) / 5 - 0.004
        pts = []
        for i in range(n + 1):
            a = 2 * math.pi * i / n; x, y = math.cos(a) * r, math.sin(a) * r
            if x0 <= x <= x1: pts.append((x, y))
        for x in (x0, x1):
            yy = math.sqrt(max(0.0, r * r - x * x)); pts += [(x, yy), (x, -yy)]
        mat = mat_b if k in (0, 1) else mat_a
        dz = rng.uniform(-0.004, 0.004)
        hull('plank', [c + right * x + up * y + out * (h + dz) for x, y in pts for h in (-th / 2, th / 2)], mat, 1)
    rim = [c + (right * math.cos(a) + up * math.sin(a)) * (r + 0.008) + out * 0.004 for a in [i * 2 * math.pi / 40 for i in range(40) if not 7 <= i <= 10]]
    tube('rim', rim, 0.014, IRON, res=1)
    boss = c + out * th / 2
    hull('boss', [boss + (right * math.cos(a) + up * math.sin(a)) * 0.09 for a in [i * 2 * math.pi / 12 for i in range(12)]] +
         [boss + (right * math.cos(a) + up * math.sin(a)) * 0.06 + out * 0.05 for a in [i * 2 * math.pi / 12 for i in range(12)]] + [boss + out * 0.075], IRON, 1)
    for k in range(10):
        a = k * 2 * math.pi / 10 + 0.3
        ball('nail', c + (right * math.cos(a) + up * math.sin(a)) * (r - 0.04) + out * (th / 2 + 0.003), 0.011, IRON, n=5)

# ---------------------------------------------------------------------------------------------------------------------------------
def obelisk():
    """A true obelisk of the Warstone's dark rock: four tapering faces to a pyramidion, war runes cut down each face between framing lines,
    a great Tiwaz at the head of the front, set on a stepped plinth and girt with iron at its foot."""
    seed(5)
    # the plinth: two stepped blocks and a footing course of rough stones
    for z0, h, w in ((0.0, 0.22, 1.25), (0.22, 0.2, 0.98)):
        box('step', (0, 0, z0 + h / 2), (w, w, h), ROCK)
    B, T, H, P = 0.31, 0.2, 3.55, 0.42   # half-width at foot and head, shaft height, pyramidion height
    Z0 = 0.42
    shaft = hull('obelisk', [Vector((x * B, y * B, Z0)) for x in (-1, 1) for y in (-1, 1)] + [Vector((x * T, y * T, Z0 + H)) for x in (-1, 1) for y in (-1, 1)] +
                 [Vector((0, 0, Z0 + H + P))], ROCK, 1)
    cutters = []
    # each face: a frame of lines and a column of runes, read top down
    texts = {0: 'tyrsigr', 1: 'futark', 2: 'sigrstafr', 3: 'tiwazalu'}   # (only runes without crossing strokes: those cut badly)
    letters = {'y': 'z', 'c': 'k', 'w': 'u', 'v': 'f', 'p': 'b', 'j': 'i', 'q': 'k', 'x': 'k'}
    for face in range(4):
        yaw = face * math.pi / 2          # (face 0: the front, +y)
        out_flat = Vector((math.sin(yaw), math.cos(yaw), 0)); right = Vector((math.cos(yaw), -math.sin(yaw), 0))
        def at(x, h):
            """A point on the face: x across (from its middle, in metres at the foot's scale), h up the shaft from its foot."""
            half = B + (T - B) * h / H
            return out_flat * half + right * x * (half / B) + Vector((0, 0, Z0 + h))
        lean = (B - T) / H
        out = (out_flat + Vector((0, 0, lean))).normalized()
        # the frame: two lines down the face's edges, joined at the top and the foot
        x0 = 0.2
        for xa, ha, xb, hb in ((-x0, 0.25, -x0, H - 0.4), (x0, 0.25, x0, H - 0.4), (-x0, H - 0.4, x0, H - 0.4), (-x0, 0.25, x0, 0.25)):
            groove(at(xa, ha), at(xb, hb), 0.022, 0.014, out, cutters)
        if face == 0:   # the front: a great Tiwaz at its head
            groove(at(0, H - 0.45 - 0.62), at(0, H - 0.5), 0.04, 0.02, out, cutters)
            groove(at(-0.03, H - 0.53), at(-0.14, H - 0.69), 0.04, 0.02, out, cutters, cap=0)   # (its arms stop short of the stem)
            groove(at(0.03, H - 0.53), at(0.14, H - 0.69), 0.04, 0.02, out, cutters, cap=0)
            top_h = H - 1.25
        else:
            top_h = H - 0.5
        rh, gap = 0.2, 0.08
        h = top_h
        for ch in texts[face]:
            ch = letters.get(ch, ch)
            if h - rh < 0.33: break
            for (ax, ay), (bx, by) in RUNES.get(ch, RUNES['i']):
                pa, pb = at((ax - 0.25) * rh * 0.9, h - rh + ay * rh), at((bx - 0.25) * rh * 0.9, h - rh + by * rh)
                dd = (pb - pa).normalized() * 0.013   # (each stroke stops short of where it meets another: a sharp wedge between two cuts breaks away)
                groove(pa + dd, pb - dd, 0.018, 0.012, out, cutters, cap=0)
            h -= rh + gap
    def inside(p):   # (nothing may stand proud of the obelisk's faces)
        if p.z < Z0 - 1e-4 or p.z > Z0 + H + P + 1e-4: return False
        half = B + (T - B) * min(1.0, (p.z - Z0) / H) if p.z <= Z0 + H else T * (1 - (p.z - Z0 - H) / P)
        return max(abs(p.x), abs(p.y)) <= half + 0.0015
    cut_all(shaft, cutters, inside)
    # the iron band round its foot, riveted, and an iron cap on the pyramidion's point
    for z, rr in ((Z0 + 0.06, B + 0.012), (Z0 + 0.15, B - 0.004)):
        hb = 0.07 if z < Z0 + 0.1 else 0.03
        box('band', (0, 0, z), (2 * rr, 2 * rr, hb), IRON)
    for face in range(4):
        yaw = face * math.pi / 2
        for k in (-1, 0, 1):
            p = Vector((math.sin(yaw), math.cos(yaw), 0)) * (B + 0.02) + Vector((math.cos(yaw), -math.sin(yaw), 0)) * k * 0.2 + Vector((0, 0, Z0 + 0.06))
            ball('rivet', p, 0.018, IRON, n=6)
    hull('cap', [Vector((x * 0.085, y * 0.085, Z0 + H + P - 0.11)) for x in (-1, 1) for y in (-1, 1)] + [Vector((0, 0, Z0 + H + P + 0.02))], IRON, 1)


def banner():
    """A war banner on a tall ash pole: a long blood-red cloth hung from a crossbar, its foot cut in tongues, a black border and Tiwaz on
    it; streamers from the bar's ends, an iron spearhead over all; the pole wedged in a cairn and steadied by three guy ropes to iron pegs."""
    TOP = 6.1
    pole('pole', 0.0, TOP, 0.075, 0.055, WOOD, s=3)
    spike('head', (0, 0, TOP - 0.05), (0, 0, TOP + 0.55), 0.06, IRON, n=4)
    cyl('socket', (0, 0, TOP - 0.18), (0, 0, TOP + 0.02), 0.065, IRON, n=10, r2=0.05)
    for k in range(2):  # the spearhead's wings
        a = k * math.pi
        hull('wing', [Vector((0, 0, TOP + 0.05)), Vector((0, 0, TOP + 0.4)), Vector((math.cos(a) * 0.12, math.sin(a) * 0.12, TOP + 0.13)),
                      Vector((math.cos(a) * 0.016, math.sin(a) * 0.016 + 0.01, TOP + 0.2)), Vector((math.cos(a) * 0.016, math.sin(a) * 0.016 - 0.01, TOP + 0.2))], IRON, 1)
    # the crossbar, lashed on, capped in iron
    BZ, BW = 5.45, 0.78
    cyl('bar', (-BW, 0.11, BZ), (BW, 0.11, BZ), 0.042, WOOD, n=10)
    for sx in (-1, 1):
        cyl('cap', (sx * (BW - 0.04), 0.11, BZ), (sx * (BW + 0.03), 0.11, BZ), 0.05, IRON, n=10)
    for k in range(10):  # (the lashing: crossed turns round the joint)
        a = k * 2 * math.pi / 10
        tube('lash', [Vector((-0.09, 0.05 + math.cos(a) * 0.12, BZ + math.sin(a) * 0.07)), Vector((0.09, 0.05 + math.cos(a + 1.2) * 0.12, BZ + math.sin(a + 1.2) * 0.07))], 0.009, ROPE, res=1)
    # the cloth: hung from the bar on its front side, rippling, its foot cut into three tongues; the emblem and border painted in it
    W, L = 1.3, 2.75
    nu, nv = 52, 110
    def length(u):
        return L - 0.42 * abs(math.sin(2 * math.pi * u * 1.5)) ** 0.7
    def f(u, v):
        x = (u - 0.5) * W
        z = BZ - 0.05 - v * length(u)
        y = 0.17 + 0.05 * math.sin(u * 7.0 + v * 2.0) * (0.3 + v) + 0.03 * v * v
        return Vector((x, y, z))
    bm = bmesh.new()
    vs = [[bm.verts.new(f(i / nu, j / nv)) for j in range(nv + 1)] for i in range(nu + 1)]
    def painted(u, v):
        x = (u - 0.5) * W; d = v * length(u)
        if abs(x) > W / 2 - 0.08 or d < 0.12: return True               # (the border)
        # Tiwaz, big in its middle: a stem and two arms
        cx, top, foot = 0.0, 0.55, 1.95
        if abs(x - cx) < 0.06 and top < d < foot: return True
        for sx in (-1, 1):
            t = (d - top) / 0.42
            if 0 <= t <= 1 and abs(x - (cx + sx * 0.42 * t)) < 0.06 * 1.2: return True
        return False
    for i in range(nu):
        for j in range(nv):
            fc = bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
            fc.material_index = 1 if painted((i + 0.5) / nu, (j + 0.5) / nv) else 0
    me = bpy.data.meshes.new('cloth'); bm.to_mesh(me); bm.free()
    me.materials.append(mats[CLOTH]); me.materials.append(mats[PAINT])
    ob = bpy.data.objects.new('cloth', me); bpy.context.scene.collection.objects.link(ob); SMOOTH.add(ob.name)
    so = ob.modifiers.new('solid', 'SOLIDIFY'); so.thickness = 0.014; so.offset = 0
    # its top folded over the bar (a sleeve) and the ties
    tube('sleeve', [Vector((x, 0.12, BZ)) for x in (-W / 2, W / 2)], 0.06, CLOTH, res=2)
    for x in (-0.5, -0.17, 0.17, 0.5):
        tube('tie', [Vector((x, 0.12 + math.cos(a) * 0.07, BZ + math.sin(a) * 0.07)) for a in [i * 2 * math.pi / 12 for i in range(12)]], 0.01, ROPE, cyclic=True, res=1)
    # streamers off the bar's ends, and black ribbons under the spearhead
    for sx in (-1, 1):
        x0 = sx * (BW - 0.08)
        sheet('streamer', lambda u, v, x0=x0, sx=sx: Vector((x0 + (u - 0.5) * 0.11 + sx * 0.05 * v, 0.11 + 0.02 * math.sin(v * 6 + sx), BZ - 0.04 - v * 1.35)), 3, 20, CLOTH, thick=0.008)
    for k, (dx, l) in enumerate(((0.04, 0.9), (-0.04, 0.7))):
        sheet('ribbon', lambda u, v, dx=dx, l=l: Vector((dx + (u - 0.5) * 0.06 + 0.06 * v * (1 if dx > 0 else -1), 0.03 * math.sin(v * 5), TOP - 0.2 - v * l)), 2, 16, PAINT, thick=0.007)
    # the foot: a cairn of rocks round it, a wedge of shims, three guy ropes from high on the pole to iron pegs
    cairn(0.33, 0.3, 9, 11)
    cairn(0.2, 0.55, 5, 12)
    for k in range(3):
        a = k * 2 * math.pi / 3 - math.pi / 2
        peg = Vector((math.cos(a) * 1.5, math.sin(a) * 1.5, 0.12))
        top = Vector((math.cos(a) * 0.07, math.sin(a) * 0.07, 2.35))
        tube('guy', [top.lerp(peg, t) + Vector((0, 0, -0.18 * math.sin(math.pi * t))) for t in [i / 12 for i in range(13)]], 0.011, ROPE, res=1)
        cyl('peg', peg + Vector((0, 0, -0.25)), peg + Vector((0, 0, 0.1)), 0.025, IRON, n=6)
        tube('pegring', [peg + Vector((math.cos(b) * 0.05, math.sin(b) * 0.05, 0.08)) for b in [i * 2 * math.pi / 10 for i in range(10)]], 0.008, IRON, cyclic=True, res=1)
    wraps((0, 0), 0.07, 2.27, 2.43)


def totem():
    """A trophy totem: a stout carved post, painted bands and a grim face cut in it, a crossarm with iron spikes where trophies hang (the game's
    own heads are hung on it by the mod), bone strings and hide strips swinging from the arm, the post wedged in a stone ring."""
    H = 3.3
    pole('post', 0.0, H, 0.2, 0.17, WOOD, s=7, sides=12)
    # a cap: the post's top cut to a point
    cyl('top', (0, 0, H - 0.04), (0, 0, H + 0.02), 0.185, IRON, n=12)
    skull((0, -0.12, H + 0.02), 2.2, 'long', tilt=-8, horn_mat=BONE)                                   # (an aurochs' skull crowning it)
    # painted bands and carved rings
    for z, mat, h in ((0.55, PAINT, 0.1), (1.25, CLOTH, 0.08), (2.85, CLOTH, 0.08), (2.4, PAINT, 0.05)):
        cyl('band', (0, 0, z - h / 2), (0, 0, z + h / 2), 0.2 - 0.03 * z / H + 0.008, mat, n=14)
    # a face carved in its front under the arm: brow, eyes, nose, a bared mouth, in black
    F = 1.75; R = 0.2 - 0.03 * F / H
    def onpost(x, z, lift=0.0):
        a = x / R
        return Vector((math.sin(a) * (R + lift), math.cos(a) * (R + lift), z))
    hull('brow', [onpost(x, F + 0.2 + dz, l) for x in (-0.13, 0.13) for dz in (0, 0.05) for l in (-0.01, 0.035)], WOOD, 1)
    for sx in (-1, 1):
        hull('eye', [onpost(sx * 0.07 + dx, F + 0.12 + dz, l) for dx in (-0.035, 0.035) for dz in (-0.025, 0.025) for l in (-0.01, 0.006)], PAINT, 1)
    hull('nose', [onpost(dx, F + dz, l) for dx in (-0.03, 0.03) for dz in (-0.03, 0.15) for l in (-0.01, 0.0)] + [onpost(0, F - 0.02, 0.06)], WOOD, 1)
    hull('mouth', [onpost(dx, F - 0.13 + dz, l) for dx in (-0.1, 0.1) for dz in (-0.04, 0.04) for l in (-0.01, 0.006)], PAINT, 1)
    for k in range(5):
        x = -0.08 + 0.04 * k
        hull('tooth', [onpost(x + dx, F - 0.09, 0.008) for dx in (-0.012, 0.012)] + [onpost(x, F - 0.14, 0.008), onpost(x, F - 0.1, 0.02)], BONE, 1)
    # the crossarm, through the post, with spikes up at its ends and the lashing
    AZ, AW = 2.55, 0.85
    cyl('arm', (-AW, 0.0, AZ), (AW, 0.0, AZ), 0.065, WOOD, n=10)
    for sx in (-1, 1):
        spike('armspike', (sx * (AW - 0.08), 0, AZ + 0.05), (sx * (AW - 0.08), 0, AZ + 0.42), 0.022, IRON)
        skull((sx * (AW - 0.08), -0.08, AZ + 0.2), 1.35, 'curl', tilt=-18, yaw=-sx * 0.35)              # (goats' skulls, spitted)
        cyl('armcap', (sx * (AW - 0.02), 0, AZ), (sx * (AW + 0.04), 0, AZ), 0.072, IRON, n=10)
        # strings of bones and teeth from the arm
        seed(31 + sx)
        for j, x in enumerate((sx * 0.3, sx * 0.68)):
            top = Vector((x, 0.0, AZ - 0.06)); l = 0.55 + 0.25 * j
            tube('cord', [top, top + Vector((0, 0.01, -l))], 0.006, ROPE, res=1)
            for b in range(4):
                p = top + Vector((0, 0.01, -0.15 - b * (l - 0.15) / 3.5))
                if b % 2: cyl('bone', p + Vector((0, 0, 0.05)), p - Vector((0, 0, 0.05)), 0.018, BONE, n=6)
                else: cyl('tooth', p + Vector((0, 0, 0.03)), p - Vector((rng.uniform(-0.02, 0.02), 0, 0.07)), 0.016, BONE, n=6, r2=0.004)
        # a hide strip
        sheet('hide', lambda u, v, sx=sx: Vector((sx * 0.5 + (u - 0.5) * 0.16, 0.0 + 0.03 * math.sin(v * 4), AZ - 0.06 - v * 0.7)), 3, 10, HIDE, thick=0.01)
    for k in range(8):
        a = k * 2 * math.pi / 8
        tube('lash', [Vector((-0.24, math.cos(a) * 0.21, AZ + math.sin(a) * 0.11)), Vector((0.24, math.cos(a + 1.4) * 0.21, AZ + math.sin(a + 1.4) * 0.11))], 0.012, ROPE, res=1)
    # spikes for the trophies on its front: one under the face's chin, and the top's own point (the mod hangs the game's heads on them)
    spike('frontspike', (0, 0.16, 1.0), (0, 0.42, 1.02), 0.03, IRON)
    shield((0, 0.24, 1.0), 0.4, (0, 1, 0.08), tilt=0.15, seed_=3)                                      # (a shield taken from a beaten foe)
    # the foot: a ring of stones, and shims driven in
    cairn(0.38, 0.28, 10, 4)


def brazier():
    """A horned brazier: a deep iron bowl heaped with embers on three clawed iron legs, two great bone horns sweeping up from its sides banded
    in bronze, on an eight-sided stone (the game's fire burns in it, added by the mod)."""
    # the stone it stands on
    hull('slab', [Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in [k * math.pi / 4 + math.pi / 8 for k in range(8)] for r, z in ((0.62, 0.0), (0.58, 0.16))], ROCK, 1)
    RIM, R = 1.12, 0.46
    # the legs: stout iron, splayed from under the bowl to clawed feet, braced by a ring
    for k in range(3):
        a = k * 2 * math.pi / 3 + math.pi / 2
        pts = []
        for i in range(9):
            t = i / 8
            rr = 0.3 + 0.2 * t + 0.04 * math.sin(math.pi * t)
            pts.append(Vector((math.cos(a) * rr, math.sin(a) * rr, (RIM - 0.2) * (1 - t) + 0.19 * t)))
        tube('leg', pts, 0.045, IRON, taper=lambda t: 1 - 0.25 * t, res=1)
        foot = pts[-1]
        ball('knee', pts[0], 0.055, IRON, n=8)
        for d in (-0.45, 0, 0.45):
            b = a + d
            spike('claw', foot + Vector((0, 0, 0.01)), foot + Vector((math.cos(b) * 0.14, math.sin(b) * 0.14, -0.02)), 0.025, IRON, n=5)
    tube('brace', [Vector((math.cos(a) * 0.43, math.sin(a) * 0.43, 0.5)) for a in [i * 2 * math.pi / 30 for i in range(30)]], 0.02, IRON, cyclic=True, res=1)
    cyl('hub', (0, 0, RIM - 0.38), (0, 0, RIM - 0.2), 0.1, IRON, n=10, r2=0.16)
    # the bowl: a lathe of a profile, made thick
    prof = [(0.0, RIM - 0.3), (0.2, RIM - 0.28), (0.34, RIM - 0.2), (0.43, RIM - 0.09), (R, RIM)]
    def lathe(u, v):
        f = v * (len(prof) - 1); k = min(int(f), len(prof) - 2); w = f - k
        r = prof[k][0] + (prof[k + 1][0] - prof[k][0]) * w; z = prof[k][1] + (prof[k + 1][1] - prof[k][1]) * w
        a = u * 2 * math.pi
        return Vector((math.cos(a) * r, math.sin(a) * r, z))
    sheet('bowl', lathe, 32, 12, IRON, thick=0.035)
    tube('rim', [Vector((math.cos(a) * (R + 0.01), math.sin(a) * (R + 0.01), RIM)) for a in [i * 2 * math.pi / 32 for i in range(32)]], 0.025, GOLD, cyclic=True, res=1)
    for k in range(12):  # studs round the bowl's belly
        a = k * 2 * math.pi / 12
        ball('stud', Vector((math.cos(a) * 0.405, math.sin(a) * 0.405, RIM - 0.12)), 0.025, GOLD, n=6)
    # the embers: black and glowing lumps heaped in it
    seed(9)
    hull('bed', [Vector((math.cos(a) * 0.4, math.sin(a) * 0.4, RIM - 0.08)) for a in [k * math.pi / 8 for k in range(16)]] + [Vector((0, 0, RIM - 0.25))], GLOW, 1)
    for k in range(26):
        a = rng.uniform(0, 2 * math.pi); rr = 0.36 * math.sqrt(rng.random())
        p = Vector((math.cos(a) * rr, math.sin(a) * rr, RIM - 0.07 + 0.08 * (1 - rr / 0.4)))
        lump('coal', p, rng.uniform(0.05, 0.085), DARK if rng.random() < 0.65 else GLOW, squash=0.7, n=10)
    # the horns: from the bowl's sides out, then up and in, ridged, banded in bronze at the root, tipped in iron
    for sx in (-1, 1):
        pts = []
        for i in range(25):
            t = i / 24
            x = sx * (R - 0.02 + 0.55 * math.sin(math.pi * 0.62 * t) - 0.18 * t * t)
            z = RIM - 0.12 + 1.15 * t ** 1.3
            y = -0.12 * math.sin(math.pi * t)
            pts.append(Vector((x, y, z)))
        tube('horn', pts, 0.11, BONE, taper=lambda t: (1 - t) ** 0.8 * 0.95 + 0.05, res=2)
        for t in (0.06, 0.16):
            i = int(t * 24); p, q = pts[i], pts[i + 1]; d = (q - p).normalized(); r = 0.11 * ((1 - t) ** 0.8 * 0.95 + 0.05) + 0.012
            cyl('hornband', p - d * 0.03, p + d * 0.03, r, GOLD, n=12)
        for j in range(5, 21, 2):  # its ridges
            t = j / 24; p, q = pts[j], pts[j + 1]; d = (q - p).normalized(); r = 0.11 * ((1 - t) ** 0.8 * 0.95 + 0.05) + 0.004
            cyl('ridge', p - d * 0.008, p + d * 0.008, r, BONE, n=10)
        tip = pts[-1]; d = (pts[-1] - pts[-3]).normalized()
        spike('tip', tip - d * 0.05, tip + d * 0.14, 0.022, IRON)


{'banner': banner, 'totem': totem, 'brazier': brazier, 'obelisk': obelisk}[kind]()
export('up_' + kind)
if 'render' in args:
    H = {'banner': 3.2, 'totem': 1.8, 'brazier': 0.9, 'obelisk': 2.1}[kind]; D = {'banner': 10, 'totem': 6, 'brazier': 4.2, 'obelisk': 7}[kind]
    preview('prev_' + kind, cloth=(0.5, 0.06, 0.04), views=((25, D, H, H), (150, D, H, H), (260, D, H + 1, H)))
