# Byre's goods (Byre 0.1.1), made from scratch in place of props.py's placeholders: the bow-spring shears, the stave milk pail, the jug of
# goat's milk, a rolled shorn fleece (the wool) and the feeding trough.
# Run: blender -b --python ~/.cache/byre-goods/goods.py -- [render] [shears|pail|milk|wool|trough]
# (writes ~/.cache/byre-goods/<name>.json.gz; previews with "render"). What ships is the bake of these (bake_conf.py, docs/model-baking.md).
# Blender frame: z up, front +y. The trough stands on z 0 (1.8 m along x, its rim at 0.45); the shears stand on their bow, blades up
# along z, the fist round the leather grips at z 0.078; the pail stands on z 0, its handle bar along x at z 0.29; the jug stands on
# z 0; the fleece lies about the origin. (Where the hand holds them: Items.cs's Grip.) Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres; wood's grain runs along v),
# triangles per material: 0 wood, 1 iron, 2 leather, 3 milk, 4 wool, 5 clay, 6 hay, 7 rope.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/byre-goods/'
WOOD, IRON, LEATHER, MILK, WOOL, CLAY, HAY, ROPE = range(8)
NAMES = ('wood', 'iron', 'leather', 'milk', 'wool', 'clay', 'hay', 'rope')
SMOOTH = ('fleece', 'band', 'tail', 'grip', 'jug', 'lip', 'loop', 'cord', 'hay', 'straw', 'bar')
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

def box(name, lo, hi, mat, grain=None, bevel=0.0, jitter=0.0, seed=0):
    # a box from corner lo to corner hi, its edges chamfered, its corners pushed about a little (a hewn block)
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    lo, hi = V(lo), V(hi)
    bmesh.ops.scale(bm, vec=hi - lo, verts=bm.verts); bmesh.ops.translate(bm, vec=(lo + hi) / 2, verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES', profile=0.5)
    if jitter:
        r = random.Random(seed)
        for v in bm.verts: v.co += V((r.uniform(-jitter, jitter), r.uniform(-jitter, jitter), r.uniform(-jitter, jitter)))
    return link(name, bm, mat, grain)

def lathe(name, a, b, profile, mat, segs=14, flute=0.0, lobes=5, twist=0.0, seed=0, grain=True, step=0.04):
    # a solid of turning from a to b: profile(t) is its radius t of the way along; flute ripples it round, twist turns the ripples along
    # it (lobes 1 and a big twist: a strip wound round it). Capped at both ends
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
    # a torus round axis; squash flattens its section along the axis
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

def cyl(name, a, b, r0, r1, mat, segs=12):
    return lathe(name, a, b, lambda t: r0 + (r1 - r0) * t, mat, segs=segs)

def cut(ob, cutter, op='DIFFERENCE'):
    # a boolean, applied at once (the cut's faces take the cutter's material), the cutter thrown away
    mod = ob.modifiers.new('cut', 'BOOLEAN'); mod.object = cutter; mod.operation = op; mod.solver = 'EXACT'; mod.material_mode = 'TRANSFER'
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    ob.modifiers.clear(); ob.data = me
    bpy.data.objects.remove(cutter)
    return ob

def staves(name, r0, r1, thick, z0, z1, n, seed, gap=0.0025, tall=None, uneven=0.003):
    # a ring of n staves standing from z0 to z1 (as Bathhouse's bath.py), the ring's outer radius r0 at its foot and r1 at its top, each
    # stave its own width and its top a few mm off the others'; tall(stave, from angle, to angle) gives ear staves their extra height
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
        ring_ = []
        for z in (z0, top):
            ro = rad(z); ri = ro - thick
            ring_.append(([bm.verts.new((math.cos(t) * ro, math.sin(t) * ro, z)) for t in (a0, (a0 + a1) / 2, a1)],
                          [bm.verts.new((math.cos(t) * ri, math.sin(t) * ri, z)) for t in (a0, (a0 + a1) / 2, a1)]))
        (ob_, ib), (ot, it) = ring_
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
    # a round floor set in the staves (thick), or a milk surface (flat, facing up)
    bm = bmesh.new()
    if thick:
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=thick)
        bmesh.ops.translate(bm, vec=(0, 0, z + thick / 2), verts=bm.verts)
    else:
        f = bm.faces.new([bm.verts.new((math.cos(2 * math.pi * i / segs) * radius, math.sin(2 * math.pi * i / segs) * radius, z)) for i in range(segs)])
        f.normal_update()
        if f.normal.z < 0: f.normal_flip()
    return link(name, bm, mat, grain)

def moved(by):
    # everything shifted
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH': ob.data.transform(Matrix.Translation(V(by)))

# ---------------- the shears ----------------
# One bar of iron forged out at both ends into blades and bent round at the back into a U spring, as the Viking-age shears found in
# graves: the blades lie side by side (each a wedge, its back thick, its edge bevelled on the outer face and flat where they meet),
# set a few mm apart in y so they shear past each other, open about two fingers at the tips; the bar between blade and bow square, the
# bow a flat strip; each handle wound with a leather strip. 26 cm from point to bow.
def shears():
    fresh()
    Y = V((0, 1, 0))
    def arm(sx):
        pts = []
        for i in range(13):
            t = 1 - i / 12   # 1 at the point, 0 at the blade's foot
            e = 0.0045 + 0.0075 * t   # the edge's x
            W = max(0.0009, 0.021 * (1 - t ** 2.4) ** 0.75)   # the back curving down to the point
            pts.append(((e + W / 2) * sx, 0.075 + 0.135 * t, W, 0.0046 * (1 - 0.72 * t ** 1.5), 0.0005))
        # the shoulder, the square handle bar, the bar flattening into the spring strip
        for z, x, w, tb in ((0.064, 0.0166, 0.0088, 0.007), (0.05, 0.0167, 0.0075, 0.0075), (0.02, 0.0172, 0.0075, 0.0075),
                            (-0.006, 0.0178, 0.0072, 0.0079), (-0.026, 0.0186, 0.0052, 0.0098), (-0.04, 0.019, 0.0035, 0.0115)):
            pts.append((x * sx, z, w, tb, tb))
        return pts
    A, B = arm(1), arm(-1)
    path = [(V((x, 0, z)), w, tb, te, 0.0025, 1) for x, z, w, tb, te in A]
    N = 14
    for k in range(1, N):
        a = -math.pi * k / N
        path.append((V((0.019 * math.cos(a), 0, -0.04 + 0.019 * math.sin(a))), 0.0035, 0.0115, 0.0115, 0.0025 * math.cos(math.pi * k / N), 1))
    path += [(V((x, 0, z)), w, tb, te, -0.0025, -1) for x, z, w, tb, te in reversed(B)]
    bm = bmesh.new(); rings = []
    for i, (p, w, tb, te, yoff, bev) in enumerate(path):
        T = (path[min(i + 1, len(path) - 1)][0] - path[max(i - 1, 0)][0]).normalized()
        n = T.cross(Y).normalized()   # outward: the blade's back, the bow's outside
        lo = (tb - te) * max(bev, 0); hi = (tb - te) * max(-bev, 0)
        sec = [(w / 2, tb / 2), (0.0, tb / 2), (-w / 2, tb / 2 - lo), (-w / 2, -tb / 2 + hi), (0.0, -tb / 2), (w / 2, -tb / 2)]
        rings.append([bm.verts.new(p + n * u + Y * (v + yoff)) for u, v in sec])
    for i in range(len(rings) - 1):
        for k in range(6):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % 6], rings[i + 1][(k + 1) % 6], rings[i + 1][k]))
    bm.faces.new(rings[0]); bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    link('shears', bm, IRON, (0, 0, 1))
    # the leather strips wound round the handles (a helix ridge on a sleeve), a turn bound round each end (texture only: conf.DECALS)
    for sx in (1, -1):
        a, b = V((0.0185 * sx, 0.0025 * sx, -0.022)), V((0.0167 * sx, 0.0025 * sx, 0.056))
        lathe(f'grip{sx}', a, b, lambda t: 0.0064 * (1 - 0.12 * max(0, abs(2 * t - 1) - 0.85) / 0.15), LEATHER, segs=14, flute=0.09, lobes=1,
              twist=2 * math.pi * 9.5 * sx, seed=3 + sx, step=0.0012)
        for k, t in enumerate((0.0, 1.0)):
            ring(f'wrap{sx}_{k}', a.lerp(b, 0.02 + 0.96 * t), 0.0066, 0.0011, b - a, LEATHER, segs=14, rsegs=4)
    moved((0, 0, 0.061))   # (standing on its bow: the fist's middle at z 0.078, Items.Grip)
    finish('shears', [(0, 0.5, 0.13, 0.11), (35, 0.45, 0.33, 0.11), (90, 0.4, 0.13, 0.11), (160, 0.45, 0.38, 0.08), (10, 0.2, 0.16, 0.17)])

# ---------------- the milk pail ----------------
# Seventeen staves widening a little to the rim, two of them standing up as ears with a bar of wood through them for the handle (carved:
# swelling to the grip between two beads, pegged outside each ear), three iron hoops, a floor set in, milk two thirds up.
PL_R0, PL_R1, PL_T, PL_H = 0.112, 0.128, 0.011, 0.25
def pail():
    fresh()
    ring_, mids, rad = staves('staves', PL_R0, PL_R1, PL_T, 0.0, PL_H, 17, seed=11, gap=0.0016,
                              tall=lambda i, a0, a1: 0.068 if i == 0 or a0 <= math.pi <= a1 else 0.0)   # (stave 0 straddles +x)
    hz = PL_H + 0.04
    cut(ring_, cyl('cutter', (-0.25, 0, hz), (0.25, 0, hz), 0.0118, 0.0118, WOOD, segs=16))
    # the ears' tops rounded over the bar (what's above the bar, less a round about its hole, comes off)
    for sx in (1, -1):
        keep = lathe('cutter', (sx * 0.09, 0, hz), (sx * 0.16, 0, hz), lambda t: 0.026, WOOD, segs=24, step=0.2)
        top = box('cutter', (sx * 0.125 - 0.06, -0.06, hz), (sx * 0.125 + 0.06, 0.06, hz + 0.08), WOOD)
        cut(top, keep, 'DIFFERENCE')   # (the box above the bar less the round: what comes off the ear)
        cut(ring_, top)
    disc('floor', PL_R0 - PL_T + 0.004, 0.012, WOOD, thick=0.02, grain=(1, 0, 0))
    for k, (z0, z1) in enumerate(((0.026, 0.044), (0.118, 0.134), (0.206, 0.226))):
        band(f'hoop{k}', rad, z0, z1, 0.0026)
    disc('milk', rad(0.185) - PL_T + 0.003, 0.185, MILK)
    def bar(t):
        x = (t - 0.5) * 0.31
        r = 0.0108 + 0.0032 * max(0, math.cos(math.pi * x / 0.21)) ** 2
        return r + 0.0024 * math.exp(-((abs(x) - 0.098) / 0.0045) ** 2) + 0.002 * math.exp(-((abs(x) - 0.152) / 0.004) ** 2)
    lathe('bar', (-0.155, 0, hz), (0.155, 0, hz), bar, WOOD, segs=12, step=0.003)
    for sx in (1, -1):
        cyl(f'peg{sx}', (sx * 0.141, 0, hz - 0.019), (sx * 0.141, 0, hz + 0.017), 0.0034, 0.0042, WOOD, segs=8)
    finish('pail', [(0, 0.85, 0.44, 0.15), (40, 0.8, 0.74, 0.15), (110, 0.75, 0.34, 0.15), (200, 0.8, 0.79, 0.15), (20, 0.45, 0.69, 0.19)])

# ---------------- the jug of goat's milk ----------------
# A thrown earthenware jug, its throwing rings showing, a pinched spout, a strap handle at the back, a cord round its neck, full.
JUG = [(0.0, 0.041), (0.03, 0.047), (0.12, 0.058), (0.33, 0.068), (0.52, 0.064), (0.7, 0.047), (0.8, 0.034), (0.87, 0.03), (0.95, 0.032), (1.0, 0.036)]
def milk():
    fresh()
    H = 0.165
    def prof(t):
        for (t0, r0), (t1, r1) in zip(JUG, JUG[1:]):
            if t <= t1:
                s = (t - t0) / (t1 - t0); s = s * s * (3 - 2 * s)
                r = r0 + (r1 - r0) * s; break
        if 0.08 < t < 0.76: r *= 1 + 0.009 * math.sin(2 * math.pi * t * H / 0.009)
        return r
    jug = lathe('jug', (0, 0, 0), (0, 0, H), prof, CLAY, segs=32, step=0.0035)
    # the mouth is milk; the spout pinched out toward +y
    jug.data.materials.append(mats[MILK])
    for p in jug.data.polygons:
        if p.normal.z > 0.99 and p.center.z > H - 0.001: p.material_index = 1
    def pinch(ob, z0):
        for v in ob.data.vertices:
            if v.co.z < z0: continue
            d = math.atan2(v.co.x, v.co.y)
            k = math.exp(-(d / 0.45) ** 2) * min(1, (v.co.z - z0) / (H - z0)) ** 1.5
            v.co.y += 0.011 * k; v.co.z += 0.004 * k
    pinch(jug, H * 0.86)
    lip = ring('lip', (0, 0, H), 0.0355, 0.0042, (0, 0, 1), CLAY, segs=32, rsegs=6)
    pinch(lip, H - 0.01)
    # the strap handle: shoulder to belly at the back, flattened
    path = smooth_line([V((0, -0.033, 0.142)), V((0, -0.06, 0.145)), V((0, -0.083, 0.118)), V((0, -0.083, 0.08)), V((0, -0.062, 0.055))], 2)
    loop = tube('loop', path, 0.0075, CLAY, res=2)
    for v in loop.data.vertices: v.co.x *= 1.5
    ring('cord', (0, 0, 0.142), 0.031, 0.0024, (0, 0, 1), ROPE, segs=24, rsegs=5)
    for k, (dx, dz) in enumerate(((0.012, -0.03), (-0.004, -0.034))):
        tube(f'cord_end{k}', [V((0.0, 0.031, 0.142)), V((dx * 0.5, 0.036, 0.13)), V((dx, 0.04, 0.142 + dz))], 0.002, ROPE, res=1)
    finish('milk', [(0, 0.55, 0.15, 0.08), (40, 0.5, 0.35, 0.08), (130, 0.5, 0.15, 0.08), (250, 0.5, 0.3, 0.08), (20, 0.3, 0.4, 0.1)])

# ---------------- the fleece ----------------
# A fleece as the shearer rolls it: laid out, the sides folded in, rolled up tight from the tail and bound round its waist with a rope
# twisted from its own neck wool. One mesh, displaced: its locks lying round the roll (crimped along their length), its ends showing the roll's spiral; a little flattened where it lies.
def wool():
    fresh()
    X, R, Q = 0.115, 0.068, 0.82
    r = random.Random(5)
    locks = [(r.uniform(-X * 1.05, X * 1.05), r.uniform(0, 2 * math.pi), r.uniform(0.004, 0.009), r.uniform(0.12, 0.26), r.uniform(0.006, 0.011), r.uniform(0, 6.3)) for _ in range(700)]
    NU, NV = 110, 150
    waist = lambda x: 1 - 0.13 * math.exp(-(x / 0.024) ** 2)
    def shape(x, rad, th):
        # the roll's surface before its locks: flattened, flatter still underneath
        z = rad * math.sin(th) * Q
        return V((x, rad * math.cos(th), z * (0.75 if z < 0 else 1)))
    bm = bmesh.new(); grid = []
    for i in range(1, NU):
        phi = math.pi * i / NU; c, s = math.cos(phi), math.sin(phi)
        x = -X * math.copysign(abs(c) ** 0.4, c)
        rad = R * s ** 0.3 * waist(x)
        end = abs(c) ** 6   # (0 along the side, 1 on the end faces)
        row = []
        for k in range(NV):
            th = 2 * math.pi * k / NV
            side = 0.0025
            for xc, tc, amp, sth, sx, ph in locks:
                if abs(x - xc) > 3 * sx: continue
                dt = (th - tc + math.pi) % (2 * math.pi) - math.pi
                if abs(dt) > 3 * sth: continue
                g = math.exp(-(dt / sth) ** 2 - ((x - xc) / sx) ** 2)
                side = max(side, amp * g * (1 + 0.3 * math.sin(dt * R / 0.0018 + ph)))
            side *= 1 - 0.85 * math.exp(-(x / 0.02) ** 2)   # (pressed flat under the band)
            rho = rad / R
            spiral = 0.5 + 0.5 * math.cos(2 * math.pi * rho / 0.17 - th)
            axial = 0.007 * spiral + r.uniform(0, 0.0012)
            radial = V((0, math.cos(th), math.sin(th)))
            p = shape(x, rad, th) + radial * side * (1 - end) + V((math.copysign(1, -c), 0, 0)) * axial * end
            row.append(bm.verts.new(p))
        grid.append(row)
    for i in range(len(grid) - 1):
        for k in range(NV):
            j = (k + 1) % NV
            bm.faces.new((grid[i][k], grid[i][j], grid[i + 1][j], grid[i + 1][k]))
    for row, x in ((grid[0], -X), (grid[-1], X)):
        pole = bm.verts.new((x * 1.01, 0, 0))
        for k in range(NV): bm.faces.new((pole, row[(k + 1) % NV], row[k]))
    bmesh.ops.smooth_vert(bm, verts=bm.verts, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    link('fleece', bm, WOOL)
    # the band: two turns of a two-ply rope of wool round the waist
    bm = bmesh.new(); rows = []
    S, RS = 48, 5
    for i in range(S):
        a = 2 * math.pi * i / S * 2   # two turns
        x = -0.007 + 0.014 * i / S
        rw = R * waist(x)
        core = shape(x, rw + 0.0055, a)
        out = V((0, math.cos(a), math.sin(a) * Q * (0.75 if math.sin(a) < 0 else 1))).normalized()
        along = V((0.014 / (4 * math.pi), -math.sin(a) * rw, math.cos(a) * rw * Q)).normalized()
        side = along.cross(out).normalized()
        row = []
        for k in range(RS):
            b = 2 * math.pi * k / RS
            rr = 0.0058 * (1 + 0.28 * math.sin(2 * b + 22 * a))
            row.append(bm.verts.new(core + (out * math.cos(b) + side * math.sin(b)) * rr))
        rows.append(row)
    for i in range(S - 1):
        for k in range(RS):
            bm.faces.new((rows[i][k], rows[i][(k + 1) % RS], rows[i + 1][(k + 1) % RS], rows[i + 1][k]))
    bm.faces.new(list(reversed(rows[0]))); bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    link('band', bm, WOOL)
    finish('wool', [(0, 0.5, 0.2, 0.0), (40, 0.45, 0.35, 0.0), (90, 0.45, 0.12, 0.0), (210, 0.5, 0.3, 0.0), (75, 0.22, 0.08, 0.0)])

# ---------------- the trough ----------------
# A log split, hewn smooth outside (the adze's facets) and hollowed out (its scallops), its ends left solid; it lies in the notched tops
# of two saddle blocks on four splayed round legs each, all of one wood. Hay heaped in it.
def trough():
    fresh()
    AX, R, TOP, L = 0.42, 0.22, 0.45, 0.9
    log = lathe('log', (-L, 0, AX), (L, 0, AX), lambda t: R * (1 + 0.012 * math.sin(t * 23)), WOOD, segs=44, flute=0.012, lobes=11, seed=3, step=0.06)
    cut(log, box('cutter', (-1, -0.4, TOP), (1, 0.4, 0.8), WOOD))
    hollow = lambda: lathe('cutter', (-0.765, 0, AX + 0.02), (0.765, 0, AX + 0.02), lambda t: 0.176 * max(0.06, 1 - abs(2 * t - 1) ** 7) ** 0.5,
                           WOOD, segs=32, flute=0.018, lobes=7, seed=8, step=0.03)
    cut(log, hollow())
    # the hay: a heap filling the hollow above the rim in the middle, streaked with straws, trimmed to the hollow
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=5, radius=1.0)
    r = random.Random(12)
    dirs = [(V((r.gauss(0, 1), r.gauss(0, 0.6), r.gauss(0, 0.3))).normalized() * r.uniform(90, 220), r.uniform(0, 6.3)) for _ in range(9)]
    for v in bm.verts:
        d = v.co.normalized()
        p = V((d.x * 0.66, d.y * 0.19, d.z * 0.1 * (1 + 0.35 * math.cos(d.x * 4.2)))) + V((0, 0, 0.37))
        streak = sum(abs(math.sin(p.dot(w) + ph)) for w, ph in dirs) / len(dirs)
        v.co = p + d * (0.012 * streak ** 3 + r.uniform(0, 0.002))
    hay = link('hay', bm, HAY)
    cut(hay, hollow(), 'INTERSECT')
    # loose straws lying over the heap (texture only: conf.DECALS)
    top = lambda x, y: 0.37 + 0.1 * (1 + 0.35 * math.cos(x / 0.66 * 4.2)) * max(0, 1 - (x / 0.66) ** 2 - (y / 0.19) ** 2) ** 0.5 + 0.004
    for k in range(60):
        x, y = r.uniform(-0.55, 0.55), r.uniform(-0.13, 0.13); a = r.uniform(0, math.pi); L = r.uniform(0.05, 0.14)
        d = V((math.cos(a), math.sin(a) * 0.6, 0)) * L / 2
        pts = [V((x, y, 0)) + d * (2 * t - 1) for t in (0, 0.5, 1)]
        if min(top(p.x, p.y) for p in pts) < 0.38: continue
        tube(f'wisp{k}', [V((p.x, p.y, top(p.x, p.y))) for p in pts], 0.0018, HAY, res=1)
    for sx in (-0.6, 0.6):
        sad = box('saddle', (sx - 0.05, -0.2, 0.12), (sx + 0.05, 0.2, 0.275), WOOD, grain=(0, 1, 0), bevel=0.008, jitter=0.003, seed=int(sx * 10) + 20)
        cut(sad, lathe('cutter', (sx - 0.1, 0, AX), (sx + 0.1, 0, AX), lambda t: R + 0.002, WOOD, segs=44, step=0.3))
        for lx in (-1, 1):
            for ly in (-1, 1):
                a = V((sx + lx * 0.024, ly * 0.14, 0.2)); b = V((sx + lx * 0.07, ly * 0.255, 0.0))
                mid = a.lerp(b, 0.5) + V((r.uniform(-0.006, 0.006), r.uniform(-0.006, 0.006), 0))
                lathe(f'leg', a, b, lambda t: 0.021 + 0.004 * t, WOOD, segs=9, flute=0.03, lobes=3, seed=int(sx * 10) + lx + 3 * ly, step=0.05)
    finish('trough', [(0, 3.0, 1.2, 0.25), (35, 2.8, 1.9, 0.25), (90, 2.2, 0.8, 0.25), (210, 2.8, 1.6, 0.25), (20, 1.3, 1.4, 0.35)])

def rack():
    # A hay rack standing in the byre: a slatted V-crib on four legs, hay heaped in it, 1.4 m along x, its rails 1.05 m up. Front +y, back -y.
    fresh()
    r = random.Random(31)
    L, TOPZ, BOTZ, W = 0.7, 1.05, 0.55, 0.36
    for sx in (-L, L):
        # the end boards: a V of plank, and the legs down from it
        bm = bmesh.new()
        quad = [(-W, TOPZ + 0.04), (W, TOPZ + 0.04), (0.07, BOTZ - 0.07), (-0.07, BOTZ - 0.07)]
        a = [bm.verts.new((sx - 0.025, y, z)) for y, z in quad]; b = [bm.verts.new((sx + 0.025, y, z)) for y, z in quad]
        bm.faces.new(a); bm.faces.new(list(reversed(b)))
        for k in range(4): j = (k + 1) % 4; bm.faces.new((a[j], a[k], b[k], b[j]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.006, segments=1, affect='EDGES', profile=0.5)
        link('end', bm, WOOD, grain=(0, 0, 1))
        for ly in (-1, 1):
            a = V((sx, ly * W * 0.85, TOPZ - 0.02)); b = V((sx + (0.05 if sx > 0 else -0.05), ly * (W + 0.08), 0.0))
            lathe('leg', a, b, lambda t: 0.032 + 0.005 * t, WOOD, segs=8, flute=0.03, lobes=3, seed=int(sx * 10) + ly, step=0.06)
    for ly in (-1, 1):
        box('rail', (-L - 0.05, ly * W - 0.035, TOPZ - 0.03), (L + 0.05, ly * W + 0.035, TOPZ + 0.04), WOOD, grain=(1, 0, 0), bevel=0.008, jitter=0.003, seed=40 + ly)
        box('brace', (-L + 0.02, ly * (W + 0.02) - 0.02, 0.2), (L - 0.02, ly * (W + 0.02) + 0.02, 0.26), WOOD, grain=(1, 0, 0), bevel=0.006, jitter=0.003, seed=44 + ly)
    box('keel', (-L, -0.07, BOTZ - 0.07), (L, 0.07, BOTZ), WOOD, grain=(1, 0, 0), bevel=0.008, jitter=0.002, seed=48)
    # the slats: from the keel up to each rail, round and a little uneven
    n = 9
    for ly in (-1, 1):
        for k in range(n):
            x = -L + 0.09 + k * (2 * L - 0.18) / (n - 1) + r.uniform(-0.01, 0.01)
            a = V((x, ly * 0.05, BOTZ - 0.02)); b = V((x + r.uniform(-0.015, 0.015), ly * (W - 0.01), TOPZ))
            lathe('slat', a, b, lambda t: 0.017, WOOD, segs=7, flute=0.04, lobes=3, seed=60 + k + 20 * ly, step=0.08)
    # the hay: heaped in the V, standing proud of the rails in the middle, tufts poking through the slats
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=5, radius=1.0)
    dirs = [(V((r.gauss(0, 1), r.gauss(0, 0.6), r.gauss(0, 0.5))).normalized() * r.uniform(80, 200), r.uniform(0, 6.3)) for _ in range(9)]
    for v in bm.verts:
        d = v.co.normalized()
        p = V((d.x * (L - 0.06), d.y * (W - 0.05) * (0.45 + 0.55 * max(0, d.z + 0.3)), d.z * 0.26)) + V((0, 0, 0.86))
        streak = sum(abs(math.sin(p.dot(w) + ph)) for w, ph in dirs) / len(dirs)
        v.co = p + d * (0.016 * streak ** 3 + r.uniform(0, 0.003))
    link('hay', bm, HAY)
    for k in range(50):
        x, y = r.uniform(-0.55, 0.55), r.uniform(-0.18, 0.18); a = r.uniform(0, math.pi); Ls = r.uniform(0.06, 0.16)
        d = V((math.cos(a), math.sin(a) * 0.7, r.uniform(-0.2, 0.2))) * Ls / 2
        c = V((x, y, 1.08 + 0.04 * max(0, 1 - (x / 0.6) ** 2)))
        tube(f'wisp{k}', [c - d, c, c + d], 0.002, HAY, res=1)
    finish('rack', [(0, 3.2, 1.6, 0.7), (35, 3.0, 2.2, 0.7), (90, 2.6, 1.2, 0.7), (200, 3.0, 1.8, 0.7)])

def stall():
    # A stall divider: two posts and three planks between them with a cap rail, 2 m along x, 1.3 m high, 0.12 thick: a byre's stalls
    # are rows of these off a wall, closed with the game's fence gate.
    fresh()
    r = random.Random(57)
    H, L = 1.3, 1.0
    for sx in (-L, L):
        box('post', (sx - 0.07, -0.07, 0.0), (sx + 0.07, 0.07, H + 0.06), WOOD, grain=(0, 0, 1), bevel=0.012, jitter=0.004, seed=int(sx * 10) + 3)
        box('cap', (sx - 0.08, -0.08, H + 0.06), (sx + 0.08, 0.08, H + 0.1), WOOD, grain=(1, 0, 0), bevel=0.012, jitter=0.003, seed=int(sx * 10) + 5)
    for k, (z0, z1) in enumerate(((0.12, 0.42), (0.47, 0.77), (0.82, 1.12))):
        box('plank', (-L + 0.06, -0.025 + r.uniform(-0.004, 0.004), z0 + r.uniform(-0.01, 0.01)), (L - 0.06, 0.025 + r.uniform(-0.004, 0.004), z1 + r.uniform(-0.01, 0.01)),
            WOOD, grain=(1, 0, 0), bevel=0.01, jitter=0.004, seed=70 + k)
    box('rail', (-L + 0.05, -0.05, 1.14), (L - 0.05, 0.05, 1.24), WOOD, grain=(1, 0, 0), bevel=0.012, jitter=0.003, seed=80)
    # iron nails holding the planks to the posts
    for sx in (-L + 0.1, L - 0.1):
        for z in (0.27, 0.62, 0.97):
            for sy in (-1, 1):
                lathe('nail', V((sx, sy * 0.025, z)), V((sx, sy * 0.032, z)), lambda t: 0.011, IRON, segs=8, step=0.5)
    finish('stall', [(0, 3.6, 1.4, 0.65), (35, 3.4, 2.0, 0.65), (90, 2.6, 1.2, 0.65)])

def finish(base, views):
    if __name__ == '__bake__':
        # (each part in triangles: the bake then collapses it as far as it keeps its shape, rather than halving a lathe's rings and sides,
        # which a thin grip or a 17-sided hoop can't take more than once)
        for ob in bpy.context.scene.collection.objects:
            if ob.type == 'MESH':
                bm = bmesh.new(); bm.from_mesh(ob.data); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(ob.data); bm.free()
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
        if ob.name.startswith('milk'): assert all(p.normal.z > 0.99 for p in me.polygons), 'milk faces down'
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

COLS = [(0.36, 0.24, 0.14, 1), (0.12, 0.12, 0.13, 1), (0.22, 0.12, 0.06, 1), (0.85, 0.83, 0.75, 1), (0.68, 0.62, 0.5, 1),
        (0.42, 0.24, 0.15, 1), (0.62, 0.52, 0.28, 1), (0.42, 0.34, 0.22, 1)]
def preview(base, views):
    # yaw 0 looks at the front (from +y); the ground under the lowest point
    for m, c in zip(mats, COLS):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = c; b.inputs['Roughness'].default_value = 0.85
        if m.name == 'iron': b.inputs['Metallic'].default_value = 0.7; b.inputs['Roughness'].default_value = 0.45
        if m.name == 'milk': b.inputs['Roughness'].default_value = 0.2
    low = min((ob.matrix_world @ V(c)).z for ob in bpy.context.scene.collection.objects if ob.type == 'MESH' for c in ob.bound_box)
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH':
            for p in ob.data.polygons: p.use_smooth = True
            ob.data.set_sharp_from_angle(angle=math.radians(80 if ob.name.startswith(SMOOTH) else 35))
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 640, 480
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, low - 0.001)); g = bpy.context.active_object; gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.36, 0.22, 1); g.data.materials.append(gm)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 2.6; sun.rotation_euler = (math.radians(50), 0, math.radians(150)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 40
    for yaw, d, z, look in views:
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, math.cos(a) * d, z)
        cam.rotation_euler = (V((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'{base}_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

KINDS = ('shears', 'pail', 'milk', 'wool', 'trough', 'rack', 'stall')
which = [a for a in sys.argv if a in KINDS] or list(KINDS)
for w in which: globals()[w]()
