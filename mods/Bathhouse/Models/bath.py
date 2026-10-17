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

def rock(name, centre, radius, seed, mat=STONE, segs=(10, 7)):
    # a well-rounded stone off a beach or a stream bed: an egg, flattened, one end fuller than the other, swelling and dipping gently
    # (no sharp lumps), turned its own way
    r = random.Random(seed)
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=segs[0], v_segments=segs[1], radius=1.0)
    sx, sy, sz = r.uniform(1.0, 1.4), r.uniform(0.75, 1.0), r.uniform(0.55, 0.8)
    egg = r.uniform(-0.18, 0.18)
    waves = [(Vector((r.gauss(0, 1), r.gauss(0, 1), r.gauss(0, 1))).normalized() * r.uniform(1.5, 2.8), r.uniform(0, 6.3), r.uniform(0.05, 0.1)) for _ in range(3)]
    turn = Matrix.Rotation(r.uniform(0, 2 * math.pi), 4, 'Z') @ Matrix.Rotation(r.uniform(-0.35, 0.35), 4, 'X') @ Matrix.Rotation(r.uniform(-0.3, 0.3), 4, 'Y')
    for v in bm.verts:
        d = v.co.normalized()
        k = (1 + sum(a * math.sin(d.dot(w) + p) for w, p, a in waves)) * (1 + egg * d.x)
        v.co = turn @ (Vector((d.x * sx, d.y * sy, d.z * sz)) * k * radius) + centre
    return link(name, bm, mat)

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
ST_T = 0.15   # the walls' thickness at the corners, where one wall's end stone runs through to the other's face
def fieldstone(name, lo, hi, out, seed, bulge=0.007, skew=0.02, soot=None, keep_out=None, cuts=()):
    # a big flattish field stone laid dry in a wall: a box from lo to hi, its edges rounded off, each corner pushed about on its own (no
    # two stones square), its face (out, a unit axis) swelling a little and tipped a few degrees. soot(centre, normal) picks the faces
    # the fire has blackened, cuts (points, normals) split the faces first so soot can end along them; no corner is pushed into
    # keep_out (lo, hi: the firebox mouth)
    r = random.Random(seed)
    lo, hi, out = Vector(lo), Vector(hi), Vector(out)
    size, c = hi - lo, (lo + hi) / 2
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts); bmesh.ops.translate(bm, vec=c, verts=bm.verts)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(0.022, min(size) * 0.18), segments=2, affect='EDGES', profile=0.5)
    face = max((f for f in bm.faces if f.normal.dot(out) > 0.99), key=lambda f: f.calc_area())
    across = min(size[i] for i in range(3) if abs(out[i]) < 0.5)
    bmesh.ops.inset_individual(bm, faces=[face], thickness=min(0.04, across * 0.25), depth=bulge)
    fc = face.calc_center_median()
    tip = Matrix.Rotation(r.uniform(-0.07, 0.07), 3, out.orthogonal().normalized()) @ Matrix.Rotation(r.uniform(-0.07, 0.07), 3, out.cross(out.orthogonal()).normalized())
    for v in face.verts: v.co = tip @ (v.co - fc) + fc
    push = {}
    for v in bm.verts:
        o = tuple(1 if v.co[i] > c[i] else -1 for i in range(3))
        if o not in push: push[o] = Vector((r.uniform(-skew, skew), r.uniform(-skew, skew), r.uniform(-skew, skew) * 0.6))
        v.co += push[o]
        if keep_out:
            klo, khi = keep_out
            if all(klo[i] < v.co[i] < khi[i] for i in range(3)):
                # (out of the mouth the shortest way, sideways or up/down)
                moves = [(v.co.x - klo.x, 0, klo.x), (khi.x - v.co.x, 0, khi.x), (v.co.z - klo.z, 2, klo.z), (khi.z - v.co.z, 2, khi.z)]
                _, ax, to = min(moves); v.co[ax] = to
        v.co += Vector((r.uniform(-0.002, 0.002), r.uniform(-0.002, 0.002), r.uniform(-0.002, 0.002)))
    for (a, b), no in cuts:
        # (only the faces near the cut's own stretch of line)
        lo_, hi_ = Vector((min(a.x, b.x) - 0.03, -9, min(a.z, b.z) - 0.03)), Vector((max(a.x, b.x) + 0.03, 9, max(a.z, b.z) + 0.03))
        near = [f for f in bm.faces if any(all(lo_[i] <= v.co[i] <= hi_[i] for i in range(3)) for v in f.verts)]
        if near: bmesh.ops.bisect_plane(bm, geom=list({e for f in near for e in f.edges}) + list({v for f in near for v in f.verts}) + near,
                                        dist=0.0005, plane_co=a, plane_no=no)
    bm.normal_update()
    for f in bm.faces: f.material_index = 1 if soot and soot(f.calc_center_median(), f.normal) else 0
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[STONE]); me.materials.append(mats[SOOT])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob

def stove():
    fresh()
    # a dry-stone firebox: three rough courses of big flat field stones round a core, the joints staggered and the corners bonded (each
    # course's corner stones run through the other way from the course below's); the front two upright jamb stones on the ground with
    # one long lintel across them over the mouth, a low sill stone under it, a course above; soot only inside the firebox and in a band
    # round its mouth. A bed of embers and two charred brands in it, a flat stone closing the top with a heap of rounded sauna stones on
    # it (some fire-blackened), a flat soot-black hearth stone in front. No chimney: a smoke sauna
    hx, hy, T = ST_W / 2, ST_D / 2, ST_T
    r = random.Random(3); k = [0]
    mouth = (Vector((-FB_W / 2, hy - FB_IN, FB_LO)), Vector((FB_W / 2, hy + 0.1, FB_HI)))
    # the soot's edge on the front (x, height): a ragged line 3-6 cm out from the mouth, higher over it where the smoke rolls out; the
    # stones round the mouth are split along it, and their faces inside it (looking out of the front or into the mouth) are soot
    w = FB_W / 2
    stain = [(-w - 0.045, 0.06), (-w - 0.035, 0.2), (-w - 0.055, 0.33), (-w - 0.045, FB_HI + 0.03), (-0.12, FB_HI + 0.07), (-0.03, FB_HI + 0.055),
             (0.06, FB_HI + 0.085), (w - 0.01, FB_HI + 0.06), (w + 0.05, FB_HI + 0.02), (w + 0.04, 0.3), (w + 0.05, 0.17), (w + 0.03, 0.06),
             (0.1, FB_LO - 0.03), (-0.08, FB_LO - 0.025)]
    stain_cuts = [((Vector((a[0], 0, a[1])), Vector((b[0], 0, b[1]))), Vector((b[1] - a[1], 0, a[0] - b[0])).normalized()) for a, b in zip(stain, stain[1:] + stain[:1])]
    def stained(x, z):
        inside = False
        for (x0, z0), (x1, z1) in zip(stain, stain[1:] + stain[:1]):
            if (z0 > z) != (z1 > z) and x < x0 + (z - z0) * (x1 - x0) / (z1 - z0): inside = not inside
        return inside
    def sooty(c, n):
        return n.y > -0.3 and c.y > hy - 0.22 and stained(c.x, c.z)
    def lay(lo, hi, out, **kw):
        k[0] += 1
        ob = fieldstone(f'stone{k[0]}', lo, hi, out, seed=100 + k[0], **kw)
        for v in ob.data.vertices:
            # (no stone more than a centimetre proud of the body's 0.9 x 0.8 or above 0.75)
            v.co.x = max(-hx - 0.01, min(hx + 0.01, v.co.x)); v.co.y = max(-hy - 0.01, min(hy + 0.01, v.co.y)); v.co.z = max(0.0, min(ST_H, v.co.z))
        return ob
    def wall_stone(face, a, e, z0, z1, top):
        # one stone of a course on a face, from a to e along it, 4 mm joints round it, its depth into the wall its own
        dp = r.uniform(T, T + 0.035)
        a, e = a + 0.004, e - 0.004
        z0 = z0 + 0.003 if z0 > 0 else 0.0
        z1 = z1 - (r.uniform(0.0, 0.006) if top else r.uniform(0.003, 0.014))
        if e - a > 0.24 and r.random() < 0.35:
            # (now and then two thin slabs one on the other in the place of one stone, the upper one shifted along)
            zm = z0 + (z1 - z0) * r.uniform(0.4, 0.6); s = r.uniform(-0.03, 0.03)
            wall_piece(face, a, e, z0, zm - 0.003, dp); wall_piece(face, a + max(0, s), e + min(0, s), zm + 0.003, z1, r.uniform(T, T + 0.035))
        else: wall_piece(face, a, e, z0, z1, dp)
    def wall_piece(face, a, e, z0, z1, dp):
        if face == 'front': lay((a, hy - dp, z0), (e, hy, z1), (0, 1, 0), soot=sooty)
        if face == 'back': lay((a, -hy, z0), (e, -hy + dp, z1), (0, -1, 0))
        if face == 'right': lay((hx - dp, a, z0), (hx, e, z1), (1, 0, 0))
        if face == 'left': lay((-hx, a, z0), (-hx + dp, e, z1), (-1, 0, 0))
    def course(face, a0, a1, z0, z1, below=(), top=False):
        # stones from a0 to a1, each 26-42 cm long, no joint within 7 cm of one in the course below, no sliver at the end
        joints, a = [], a0
        while a1 - a > 1e-4:
            for _ in range(30):
                e = a + r.uniform(0.2, 0.44)
                if a1 - e < 0.17: e = a1
                if e >= a1 or all(abs(e - j) > 0.07 for j in below): break
            wall_stone(face, a, min(e, a1), z0, z1, top)
            if e < a1: joints.append(e)
            a = e
        return joints
    # the core the stones are laid round (soot: the joints look deep, the firebox is cut from it) and the flat stone closing its top
    core = box('core', (-hx + 0.12, -hy + 0.12, 0.0), (hx - 0.12, hy - 0.1, 0.72), SOOT)
    cut(core, box('cutter', (-FB_W / 2, hy - FB_IN, FB_LO), (FB_W / 2, hy + 0.2, FB_HI), SOOT))
    box('capstone', (-hx + 0.1, -hy + 0.1, 0.7), (hx - 0.1, hy - 0.1, ST_H - 0.006), STONE, bevel=0.01, jitter=0.004, seed=6)
    # the front: the jambs from the ground to the lintel, out to the corners; a sill under the mouth; the lintel the whole width;
    # a course of two or three stones above
    for sx in (-1, 1):
        x0, x1 = sorted((sx * (FB_W / 2), sx * hx))
        lay((x0, hy - r.uniform(0.16, 0.19), 0.0), (x1, hy, FB_HI - 0.003), (0, 1, 0), soot=sooty, keep_out=mouth, cuts=stain_cuts, bulge=0.01)
    lay((-FB_W / 2 + 0.004, hy - 0.16, 0.0), (FB_W / 2 - 0.004, hy, FB_LO), (0, 1, 0), soot=sooty, keep_out=mouth, cuts=stain_cuts, bulge=0.004, skew=0.006)
    lay((-hx + 0.006, hy - 0.19, FB_HI), (hx - 0.004, hy + 0.008, 0.6), (0, 1, 0), soot=sooty, keep_out=mouth, cuts=stain_cuts, bulge=0.006, skew=0.012)
    course('front', -hx, hx, 0.603, ST_H, below=(), top=True)
    # the back and the sides: three courses, the back's end stones running through the corners in the first and the third, the
    # sides' in the second; the front's stones always own the front corners
    zs = [0.0, 0.25 + r.uniform(-0.02, 0.02), 0.5 + r.uniform(-0.02, 0.02), ST_H]
    below = {'back': (), 'left': (), 'right': ()}
    for i, (z0, z1) in enumerate(zip(zs, zs[1:])):
        back_owns = i % 2 == 0
        top = i == 2
        below['back'] = course('back', -hx if back_owns else -hx + T, hx if back_owns else hx - T, z0, z1, below['back'], top)
        for face in ('left', 'right'):
            below[face] = course(face, -hy + T if back_owns else -hy, hy - T, z0, z1, below[face], top)
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
    # the heap: round stones, fist to head size, dropped one by one onto a hidden low mound on the capstone (each resting on the mound
    # or on stones already there, laid at the lowest of a dozen spots), none higher than a dome rising to about 1.05 in the middle and
    # none out past the walls; about a third of them fire-blackened
    mound_r = (0.22, 0.18, 0.1)
    blob('mound', Vector((0, 0, ST_H - 0.01)), Vector(mound_r), STONE, seed=9, subdiv=2, lumps=0.03)
    def ground(x, y):
        q = 1 - (x / mound_r[0]) ** 2 - (y / mound_r[1]) ** 2
        return max(ST_H, ST_H - 0.01 + mound_r[2] * math.sqrt(q) if q > 0 else 0)
    def dome(x, y):
        q = 1 - (x / (hx - 0.02)) ** 2 - (y / (hy - 0.02)) ** 2
        return ST_H + 0.28 * max(0.0, q) ** 0.5
    placed = []
    def settle(x, y, rr):
        z = ground(x, y) + rr * 0.4
        for px, py, pz, pr in placed:
            d2 = (x - px) ** 2 + (y - py) ** 2; reach = (rr + pr) * 0.85
            if d2 < reach ** 2: z = max(z, pz + math.sqrt(reach ** 2 - d2) * 0.75)
        return z
    for i in range(600):
        if len(placed) >= 56: break
        rr = r.uniform(0.075, 0.11) if len(placed) < 18 else r.uniform(0.05, 0.08)
        spots = [(r.uniform(-hx + 0.05 + rr, hx - 0.05 - rr), r.uniform(-hy + 0.05 + rr, hy - 0.05 - rr)) for _ in range(16)]
        fits = [(x, y, z) for x, y in spots for z in [settle(x, y, rr)] if z + rr * 0.7 <= dome(x, y) + 0.02]
        if not fits: continue
        placed.append(min(fits, key=lambda t: t[2]) + (rr,))
    print('heap stones', len(placed), 'top', round(max(z + rr * 0.7 for x, y, z, rr in placed), 3))
    for i, (x, y, z, rr) in enumerate(placed): rock(f'rock{i}', Vector((x, y, z)), rr, 300 + i, SOOT if r.random() < 0.35 else STONE, segs=(9, 6))
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
