# The camping kit's looks (Camping 0.1.0): a bedroll, a lean-to and a tent, made from scratch.
# Run: blender -b --python ~/.cache/camping/camp.py -- [render] [bedroll|leanto|tent]   (writes ~/.cache/camping/<name>.json.gz;
# previews with "render")
# Blender frame: z up, front -y (the game's -z). Written in the game's frame (x, z, y): per corner position/normal/uv (uvs in metres),
# triangles per material: 0 wood, 1 hide, 2 fur, 3 wool, 4 leather, 5 rope, 6 red wool, 7 stone.
import bpy, bmesh, json, math, random, sys, gzip
from mathutils import Vector, Matrix

OUT = '/home/bob/.cache/camping/'
WOOD, HIDE, FUR, WOOL, LEATHER, ROPE, RED, STONE = range(8)
NAMES = ('wood', 'hide', 'fur', 'wool', 'leather', 'rope', 'red', 'stone')
SMOOTH = ('pole', 'roll', 'strap', 'rope', 'peg', 'post', 'rafter', 'ridge', 'fork', 'stone')
rng = random.Random(7)
mats = []

def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats.clear()
    mats.extend(bpy.data.materials.new(n) for n in NAMES)

def link(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mats[mat])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob

def tube(name, pts, radius, mat, cyclic=False, res=2):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = radius; cu.bevel_resolution = res; cu.use_fill_caps = True
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1); sp.use_cyclic_u = cyclic
    for k, p in enumerate(pts): sp.points[k].co = (p.x, p.y, p.z, 1)
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob); me.materials.clear(); me.materials.append(mats[mat])
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

def pole(name, a, b, r, mat=WOOD, bend=0.0, n=6):
    # a slightly crooked pole from a to b
    pts = []
    side = (b - a).cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
    for i in range(n + 1):
        t = i / n
        pts.append(a.lerp(b, t) + side * (math.sin(t * math.pi) * bend + rng.uniform(-0.006, 0.006)))
    return tube(name, pts, r, mat)

def sheet(name, corner, u, v, nu, nv, thick, mat, sag=lambda s, t: 0.0, edge=lambda s, t: 0.0, normal=None):
    # a cloth or hide panel over corner + s*u + t*v (s, t in 0..1), pushed along its normal by sag(s, t), its edges pulled in by
    # edge(s, t), given thickness
    bm = bmesh.new()
    nrm = (normal or u.cross(v)).normalized()
    grid = []
    for j in range(nv + 1):
        row = []
        for i in range(nu + 1):
            s, t = i / nu, j / nv
            p = corner + u * s + v * t + nrm * sag(s, t)
            e = edge(s, t)
            if e:
                c = corner + u * 0.5 + v * 0.5
                p = p + (c - p).normalized() * e
            row.append(bm.verts.new(p))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    bm.normal_update()
    ob = link(name, bm, mat)
    mod = ob.modifiers.new('solid', 'SOLIDIFY'); mod.thickness = thick; mod.offset = 0
    return ob

def blob(name, centre, size, mat, seed=0, subdiv=2):
    # a lumpy rounded lump (a stone holding a hide down)
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    r = random.Random(seed)
    for v in bm.verts:
        v.co = Vector((v.co.x * size.x, v.co.y * size.y, v.co.z * size.z)) * (1 + r.uniform(-0.12, 0.12)) + centre
    return link(name, bm, mat)

def roll(name, centre, radius, length, mat):
    # a rolled blanket lying across (along x), the outer turn's lip a little proud
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=radius, radius2=radius, depth=length)
    for v in bm.verts:
        a = math.atan2(v.co.y, v.co.x)
        k = 1 + 0.06 * ((a % (2 * math.pi)) / (2 * math.pi))
        v.co.x *= k; v.co.y *= k
    bmesh.ops.transform(bm, matrix=Matrix.Rotation(math.pi / 2, 4, 'Y'), verts=bm.verts)
    bmesh.ops.translate(bm, vec=centre, verts=bm.verts)
    return link(name, bm, mat)

def ring_strap(name, centre, radius, width, mat):
    pts = [centre + Vector((0, math.cos(2 * math.pi * i / 16), math.sin(2 * math.pi * i / 16))) * radius for i in range(16)]
    return tube(name, pts, width, mat, cyclic=True, res=1)

def outline_slab(name, pts2, z0, thick, mat):
    # a flat slab (a hide on the ground) from an outline of (x, y)
    bm = bmesh.new()
    bottom = [bm.verts.new((x, y, z0)) for x, y in pts2]
    top = [bm.verts.new((x, y, z0 + thick)) for x, y in pts2]
    bm.faces.new(top); bm.faces.new(list(reversed(bottom)))
    n = len(pts2)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bmesh.ops.triangulate(bm, faces=bm.faces)
    return link(name, bm, mat)

def hide_outline(w, l, seed):
    # a hide's shape: an oval with four leg lobes, its edge uneven
    r = random.Random(seed); pts = []
    for i in range(48):
        a = 2 * math.pi * i / 48
        x, y = math.cos(a) * w / 2, math.sin(a) * l / 2
        lobe = 0.0
        for c in (math.radians(40), math.radians(140), math.radians(220), math.radians(320)):
            d = math.atan2(math.sin(a - c), math.cos(a - c))
            lobe += 0.16 * math.exp(-(d / 0.18) ** 2)
        k = 1 + lobe + r.uniform(-0.035, 0.035)
        pts.append((x * k, y * k))
    return pts

# ---------------- the bedroll ----------------
def bedroll():
    fresh()
    # a deer hide on the ground, its head end at +y (the back); a wolf pelt over its lower two thirds; a wool blanket rolled at its head,
    # tied with two leather straps
    outline_slab('hide', hide_outline(0.95, 2.05, 3), 0.0, 0.025, HIDE)
    sheet('pelt', Vector((-0.38, -0.95, 0.03)), Vector((0.76, 0, 0)), Vector((0, 1.25, 0)), 10, 12, 0.03, FUR,
          sag=lambda s, t: 0.05 * math.sin(s * math.pi) * (0.6 + 0.4 * math.sin(t * math.pi)) - 0.02 * (1 - math.sin(s * math.pi)),
          edge=lambda s, t: (0.03 * random.Random(int(s * 97 + t * 13)).random()) if s in (0, 1) or t in (0, 1) else 0)
    roll('roll', Vector((0, 0.72, 0.15)), 0.125, 0.72, WOOL)
    for x in (-0.22, 0.22): ring_strap('strap' + str(x), Vector((x, 0.72, 0.15)), 0.132, 0.012, LEATHER)
    export('bedroll')
    if 'render' in sys.argv: preview('bedroll', [(0, 3.2, 1.6, 0.1), (40, 3.2, 1.6, 0.1), (140, 2.4, 1.2, 0.1)])

# ---------------- the lean-to ----------------
LT_W, LT_FRONT, LT_HIGH, LT_BACK = 2.8, -0.9, 1.75, 1.35
def leanto():
    fresh()
    # two forked posts at the front, a ridge pole lashed across their forks, four rafters from it back to the ground, three hides over
    # the rafters (sagging between them, their lower edges uneven and held down by stones), rope at every lashing
    for sx in (-1, 1):
        x = sx * (LT_W / 2 - 0.15)
        foot, top = Vector((x + sx * 0.04, LT_FRONT - 0.03, -0.15)), Vector((x, LT_FRONT, LT_HIGH))
        pole('post' + str(sx), foot, top, 0.055, bend=0.015)
        pole('fork' + str(sx), top + Vector((0, 0, -0.18)), top + Vector((sx * 0.12, -0.03, 0.14)), 0.03)
        tube('rope' + str(sx), [top + Vector((math.cos(a) * 0.075, math.sin(a) * 0.075, -0.06 + 0.012 * a)) for a in [i * 0.6 for i in range(22)]], 0.009, ROPE)
    ridge = LT_HIGH + 0.06
    pole('ridge', Vector((-LT_W / 2 - 0.1, LT_FRONT, ridge)), Vector((LT_W / 2 + 0.12, LT_FRONT, ridge)), 0.05, bend=0.02)
    xs = [-1.2, -0.4, 0.4, 1.2]
    for i, x in enumerate(xs):
        a, b = Vector((x, LT_FRONT - 0.12, ridge + 0.1)), Vector((x + rng.uniform(-0.05, 0.05), LT_BACK + 0.12, -0.1))
        pole('rafter' + str(i), a, b, 0.038, bend=0.01)
    # the hides: three panels across, each lapping the next, from just over the ridge down to the ground
    run = Vector((0, LT_BACK - LT_FRONT + 0.15, -ridge - 0.05))
    nrm = Vector((0, -run.z, run.y)).normalized()
    if nrm.z < 0: nrm = -nrm
    for k, (x0, x1) in enumerate(((-1.45, -0.42), (-0.48, 0.48), (0.42, 1.45))):
        corner = Vector((x0, LT_FRONT - 0.1, ridge + 0.17)) + nrm * (0.045 + 0.006 * k)
        def sag(s, t, x0=x0, x1=x1, k=k):
            x = x0 + (x1 - x0) * s
            near = min(abs(x - r) for r in xs)
            return -0.05 * min(1, near / 0.4) + 0.012 * math.sin(t * 9 + k)
        def edge(s, t, seed=11 + k):
            return 0.05 * random.Random(int(s * 211) + seed).random() if t > 0.97 else 0  # the lower edge, uneven
        sheet('hide' + str(k), corner, Vector((x1 - x0, 0, 0)), run, 8, 10, 0.018, HIDE, sag=sag, edge=edge, normal=nrm)
    for i, x in enumerate((-1.1, 0.0, 1.05)):
        blob('stone' + str(i), Vector((x, LT_BACK + 0.12, 0.06)), Vector((0.14, 0.11, 0.08)), STONE, seed=i)
    for i, x in enumerate(xs):
        tube('rope_r' + str(i), [Vector((x + math.cos(a) * 0.06, LT_FRONT - 0.04 + math.sin(a) * 0.06, ridge + 0.02 + 0.01 * a)) for a in [j * 0.7 for j in range(14)]], 0.008, ROPE)
    export('leanto')
    if 'render' in sys.argv: preview('leanto', [(0, 6.0, 2.0, 0.9), (35, 6.0, 2.4, 0.9), (150, 6.0, 2.2, 0.8)])

# ---------------- the tent ----------------
T_HALF, T_LEN, T_RIDGE = 1.4, 3.0, 2.4
def tent():
    fresh()
    # an A-frame of crossed boards at each end (their tops carved into beast heads, as the Oseberg ship's tent), a ridge pole between the
    # crossings, wool walls in strips (red bands at the ridge and near the ground) from the ridge to the ground, the back closed, the
    # front's two flaps leaving a doorway, guy ropes to pegs, a pelt on the floor inside
    ends = (-T_LEN / 2, T_LEN / 2)
    for y in ends:
        for sx in (-1, 1):
            foot = Vector((sx * (T_HALF + 0.05), y, -0.1))
            cross = Vector((0, y, T_RIDGE + 0.05))
            top = cross + (cross - foot).normalized() * 0.55
            board(f'board{sx}{y}', foot, top, 0.17, 0.045, WOOD)
            head(f'head{sx}{y}', top, (cross - foot).normalized())
    pole('ridge', Vector((0, ends[0] - 0.25, T_RIDGE + 0.12)), Vector((0, ends[1] + 0.25, T_RIDGE + 0.12)), 0.05, bend=0.01)
    bands = [(0.0, 0.08, RED), (0.08, 0.78, WOOL), (0.78, 0.86, RED), (0.86, 0.9, WOOL), (0.9, 0.96, RED), (0.96, 1.0, WOOL)]
    for sx in (-1, 1):
        top, foot = Vector((0, 0, T_RIDGE + 0.06)), Vector((sx * (T_HALF + 0.12), 0, 0.0))
        slope = foot - top
        nrm = Vector((-slope.z * sx, 0, slope.x * sx)).normalized()
        if nrm.z < 0: nrm = -nrm
        for b0, b1, mat in bands:
            corner = Vector((top.x, ends[0] - 0.08, top.z)) + slope * b0 + nrm * 0.03
            sheet(f'wall{sx}_{b0}', corner, Vector((0, T_LEN + 0.16, 0)), slope * (b1 - b0), 12, max(1, int((b1 - b0) * 8)), 0.012, mat,
                  sag=lambda s, t: -0.035 * math.sin(s * math.pi * 3) ** 2, normal=nrm)
    gable('back', ends[1] + 0.06)
    flap('flapL', -1, ends[0] - 0.06); flap('flapR', 1, ends[0] - 0.06)
    for y, d in ((ends[0], -1), (ends[1], 1)):
        a, b = Vector((0, y + d * 0.28, T_RIDGE + 0.1)), Vector((0, y + d * 1.4, 0.05))
        tube('rope' + str(y), [a.lerp(b, i / 8) + Vector((0, 0, -0.08 * math.sin(i / 8 * math.pi))) for i in range(9)], 0.01, ROPE)
        pole('peg' + str(y), b + Vector((0, d * 0.02, -0.2)), b + Vector((0, -d * 0.04, 0.12)), 0.025)
    outline_slab('pelt', hide_outline(1.3, 2.2, 5), 0.0, 0.03, FUR)
    export('tent')
    if 'render' in sys.argv: preview('tent', [(0, 6.5, 2.2, 1.0), (35, 6.5, 2.6, 1.0), (150, 6.5, 2.4, 1.0), (0, 2.6, 1.1, 0.6)])

def board(name, a, b, w, t, mat):
    # a split plank from a to b, narrowing a little toward its top, its broad face toward the tent's end
    d = (b - a); l = d.length; d.normalize()
    side = d.cross(Vector((0, 1, 0))).normalized()
    bm = bmesh.new(); pts = []
    for k in range(5):
        p = a + d * (l * k / 4)
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            pts.append(p + side * (sx * w / 2 * (1 - 0.15 * k / 4)) + Vector((0, sy * t / 2, 0)))
    vs = [bm.verts.new(p) for p in pts]
    for k in range(4):
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((vs[k * 4 + i], vs[k * 4 + j], vs[(k + 1) * 4 + j], vs[(k + 1) * 4 + i]))
    bm.faces.new([vs[3], vs[2], vs[1], vs[0]]); bm.faces.new([vs[16], vs[17], vs[18], vs[19]])
    bm.normal_update()
    return link(name, bm, mat)

def head(name, at, d):
    # a beast's head carved on a board's end: a snout reaching out and down, a jaw, an ear
    out = Vector((d.x, 0, d.z)).normalized()
    tube(name + 'snout', [at, at + out * 0.12 + Vector((0, 0, 0.04)), at + out * 0.25 + Vector((0, 0, -0.02)), at + out * 0.32 + Vector((0, 0, -0.08))], 0.05, WOOD, res=1)
    tube(name + 'jaw', [at + out * 0.05 + Vector((0, 0, -0.06)), at + out * 0.2 + Vector((0, 0, -0.12))], 0.03, WOOD, res=1)
    tube(name + 'ear', [at + Vector((0, 0, 0.03)), at + Vector((-out.x * 0.08, 0, 0.16))], 0.022, WOOD, res=1)

def gable(name, y):
    # a triangle closing an end, in strips down from the ridge (a red band near its foot)
    for k, (t0, t1, mat) in enumerate(((0.0, 0.86, WOOL), (0.86, 0.94, RED), (0.94, 1.0, WOOL))):
        bm = bmesh.new()
        def at(t, s):
            return Vector((s * (T_HALF + 0.1) * t, y, (T_RIDGE + 0.05) * (1 - t)))
        rows = [[bm.verts.new(at(t0 + (t1 - t0) * j / 4, -1 + 2 * i / 8)) for i in range(9)] for j in range(5)]
        for j in range(4):
            for i in range(8):
                bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
        ob = link(f'{name}{k}', bm, mat)
        mod = ob.modifiers.new('solid', 'SOLIDIFY'); mod.thickness = 0.012; mod.offset = 0

def flap(name, sx, y):
    # a front flap: the outer part of the front's triangle, from its corner up to the ridge, its inner edge drawn back a little;
    # the two leave a doorway 1.9 m wide at the ground, tall enough to walk in
    bm = bmesh.new(); rows = []
    for j in range(7):
        t = j / 6
        half, inner, z = (T_HALF + 0.1) * t, 0.95 * t, (T_RIDGE + 0.05) * (1 - t)
        rows.append([bm.verts.new(Vector((sx * (inner + (half - inner) * i / 4), y - 0.12 * (1 - i / 4) * t, z))) for i in range(5)])
    for j in range(6):
        for i in range(4):
            q = (rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i])
            bm.faces.new(q if sx > 0 else tuple(reversed(q)))
    ob = link(name, bm, WOOL)
    mod = ob.modifiers.new('solid', 'SOLIDIFY'); mod.thickness = 0.012; mod.offset = 0

# ---------------- write it out ----------------
def export(base):
    deps = bpy.context.evaluated_depsgraph_get()
    verts, norms, uvs, tris, index = [], [], [], [[] for _ in mats], {}
    for ob in list(bpy.context.scene.collection.objects):
        if ob.type != 'MESH': continue
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        me.update()
        cn = me.corner_normals
        for p in me.polygons:
            slot = mats.index(me.materials[p.material_index]) if me.materials else WOOD
            corner = []
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                n = cn[li].vector if ob.name.startswith(SMOOTH) else p.normal
                fn = p.normal
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u, 4), round(v, 4))
                if key not in index:
                    index[key] = len(verts) // 3; verts += [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [u, v]
                corner.append(index[key])
            tris[slot] += [corner[0], corner[2], corner[1]]
    gzip.open(OUT + base + '.json.gz', 'wt').write(json.dumps({'v': [round(x, 4) for x in verts], 'n': [round(x, 3) for x in norms], 'uv': [round(x, 4) for x in uvs], 't': tris}, separators=(',', ':')))
    xs, ys, zs = verts[0::3], verts[1::3], verts[2::3]
    print(base, 'verts', len(verts) // 3, 'tris', [len(t) // 3 for t in tris], 'size', round(max(xs) - min(xs), 2), round(max(ys) - min(ys), 2), round(max(zs) - min(zs), 2))

def preview(base, views):
    cols = [(0.33, 0.22, 0.13, 1), (0.55, 0.4, 0.26, 1), (0.32, 0.3, 0.28, 1), (0.78, 0.72, 0.6, 1), (0.22, 0.13, 0.08, 1), (0.6, 0.52, 0.38, 1), (0.5, 0.08, 0.06, 1), (0.45, 0.45, 0.43, 1)]
    for m, c in zip(mats, cols):
        m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = c; b.inputs['Roughness'].default_value = 0.9
    for ob in bpy.context.scene.collection.objects:
        if ob.type == 'MESH' and ob.name.startswith(SMOOTH):
            for p in ob.data.polygons: p.use_smooth = True
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 16; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 640, 480
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, -0.005)); g = bpy.context.active_object; gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.36, 0.22, 1); g.data.materials.append(gm)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 40
    for yaw, d, z, look in views:
        a = math.radians(yaw)
        cam.location = (math.sin(a) * d, -math.cos(a) * d, z)
        cam.rotation_euler = (Vector((0, 0, look)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = OUT + f'{base}_{yaw}_{d}.png'; bpy.ops.render.render(write_still=True)

which = [a for a in sys.argv if a in ('bedroll', 'leanto', 'tent')] or ['bedroll', 'leanto', 'tent']
for w in which: globals()[w]()
