# Byre's small things, made in Blender: shears, milk pail, a hank of wool, a jug of goat's milk, the feeding trough.
# Low-poly parts in flat colours (the game's own props are a few hundred triangles with a small texture), baked into one small
# atlas each with gentle ambient occlusion, exported in the game's frame (Unity x, y up, z forward; triangles rewound for the mirror).
# Run: blender -b --python props.py -- <out dir> [name...]
import sys, os, math, json, gzip, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix, Quaternion, Euler
import animals as A

RNG = random.Random(11)
V = A.V
srgb = A.srgb

def clear():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)
    for m in list(bpy.data.materials): bpy.data.materials.remove(m)
    for i in list(bpy.data.images): bpy.data.images.remove(i)

def obj(bm, name):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob); return ob

def box(c, size, colour, rot=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if rot is not None:
        for v in bm.verts: v.co = rot @ v.co
    for v in bm.verts: v.co += Vector(c)
    ob = obj(bm, 'box'); ob['col'] = colour; return ob

def cyl(a, b, r0, r1, colour, seg=10, cap=True):
    a, b = Vector(a), Vector(b); d = b - a
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=cap, segments=seg, radius1=r0, radius2=r1, depth=d.length)
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    for v in bm.verts: v.co = q @ v.co + (a + b) / 2
    ob = obj(bm, 'cyl'); ob['col'] = colour; return ob

def blob(c, s, colour, seg=8, ring=6):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=ring, radius=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2])) + Vector(c)
    ob = obj(bm, 'blob'); ob['col'] = colour; return ob

def lathe(profile, colour, seg=12, z0=0.0, cap=True):
    """A turned shape from (radius, height) pairs, bottom to top (a jug)."""
    bm = bmesh.new(); rings = []
    for r, h in profile:
        ring = [bm.verts.new((r * math.cos(t), r * math.sin(t), h + z0)) for t in [i * math.tau / seg for i in range(seg)]]
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    if cap: bm.faces.new(list(reversed(rings[0])))
    ob = obj(bm, 'lathe'); ob['col'] = colour; return ob

def colour_parts(parts):
    for ob in parts:
        me = ob.data; attr = me.color_attributes.new('Col', 'FLOAT_COLOR', 'CORNER'); base = ob['col']
        for poly in me.polygons:
            j = RNG.uniform(-0.03, 0.03)
            col = srgb(tuple(min(1, max(0, x + j)) for x in base))
            for li in poly.loop_indices: attr.data[li].color = (*col, 1)

WOOD = (0.55, 0.40, 0.25); DARKWOOD = (0.40, 0.28, 0.17); IRON = (0.33, 0.33, 0.34); WOOL = (0.90, 0.86, 0.76)
MILK = (0.93, 0.92, 0.86); CLAY = (0.62, 0.42, 0.30); ROPE = (0.60, 0.52, 0.38); LEATHER = (0.30, 0.20, 0.12); HAY = (0.78, 0.68, 0.38)

def shears():
    """Norse spring shears: one bow of iron bent round, two blades, a leather wrap where the hand goes."""
    parts = []
    for s in (-1, 1):
        # (the blades, flat and tapering, crossing a little at their tips; the leather grips below them; the bow of iron joins the grips)
        parts.append(box(V(s * 0.014, 0.0, 0.125), (0.007, 0.022, 0.11), IRON, rot=Euler((0, math.radians(s * 5), 0)).to_matrix()))
        parts.append(box(V(s * 0.02, 0.0, 0.036), (0.009, 0.014, 0.075), LEATHER))
    for i in range(8):
        t0 = math.pi * i / 8; t1 = math.pi * (i + 1) / 8
        a = V(0.02 * math.cos(t0), 0, 0.002 - 0.022 * math.sin(t0)); b = V(0.02 * math.cos(t1), 0, 0.002 - 0.022 * math.sin(t1))
        parts.append(cyl(a, b, 0.0045, 0.0045, IRON, seg=5))
    return parts

def pail():
    """A stave-built milking pail, bound with two hoops, a rope handle; a little milk in it."""
    parts = [lathe([(0.10, 0.0), (0.108, 0.02), (0.118, 0.20), (0.122, 0.22)], DARKWOOD, seg=14)]
    inner = lathe([(0.112, 0.215), (0.104, 0.03)], WOOD, seg=14, cap=False)   # (its inside wall, facing in)
    parts.append(inner)
    parts.append(lathe([(0.0, 0.14), (0.110, 0.14)], MILK, seg=14))   # (milk)
    for h in (0.05, 0.17):
        parts.append(lathe([(0.112 + (h - 0.05) * 0.05, h - 0.01), (0.115 + (h - 0.05) * 0.05, h + 0.01)], IRON, seg=14))
    for i in range(6):
        t0 = math.pi * i / 6; t1 = math.pi * (i + 1) / 6
        parts.append(cyl(V(0.115 * math.cos(t0), 0, 0.22 + 0.10 * math.sin(t0)), V(0.115 * math.cos(t1), 0, 0.22 + 0.10 * math.sin(t1)), 0.006, 0.006, ROPE, seg=5))
    return parts

def wool():
    """A hank of fleece: a few lumps pressed together."""
    parts = []
    for i in range(7):
        c = V(RNG.uniform(-0.06, 0.06), RNG.uniform(-0.05, 0.05), RNG.uniform(0.03, 0.07))
        parts.append(blob(c, (RNG.uniform(0.04, 0.06), RNG.uniform(0.04, 0.055), RNG.uniform(0.03, 0.045)), WOOL))
    return parts

def milk():
    """A clay jug of goat's milk, stoppered."""
    parts = [lathe([(0.045, 0.0), (0.06, 0.02), (0.07, 0.07), (0.06, 0.12), (0.032, 0.15), (0.03, 0.17), (0.036, 0.18)], CLAY, seg=12)]
    parts.append(cyl(V(0, 0, 0.17), V(0, 0, 0.195), 0.026, 0.024, DARKWOOD, seg=8))
    parts.append(lathe([(0.0, 0.181), (0.03, 0.181)], MILK, seg=12))
    return parts

def trough():
    """A feeding trough: a hollowed half-log on two crossed-leg trestles, hay in it. 1.8 m long, knee high."""
    parts = []
    L, R = 0.9, 0.24
    # the trough: an outer half-round shell and its hollow, as a lathe turned on its side
    seg = 10
    bm = bmesh.new(); rows = []
    for x in (-L, L):
        outer = [bm.verts.new((x, R * math.cos(t), 0.42 - R * math.sin(t))) for t in [math.pi * i / seg for i in range(seg + 1)]]
        inner = [bm.verts.new((x * 0.96, (R - 0.04) * math.cos(t), 0.44 - (R - 0.04) * math.sin(t))) for t in [math.pi * i / seg for i in range(seg + 1)]]
        rows.append((outer, inner))
    (o0, i0), (o1, i1) = rows
    for k in range(seg):
        bm.faces.new((o0[k], o1[k], o1[k + 1], o0[k + 1]))          # outside
        bm.faces.new((i0[k + 1], i1[k + 1], i1[k], i0[k]))          # inside
    for k in (0, seg):
        bm.faces.new((o0[k], i0[k], i1[k], o1[k]))                   # rims
    for o, i in rows:
        for k in range(seg):
            bm.faces.new((o[k], o[k + 1], i[k + 1], i[k]) if o is o1 else (i[k], i[k + 1], o[k + 1], o[k]))
    tr = obj(bm, 'trough'); tr['col'] = DARKWOOD; parts.append(tr)
    # hay in it, in tufts
    for i in range(9):
        parts.append(blob(V(RNG.uniform(-0.7, 0.7), RNG.uniform(-0.1, 0.1), 0.34), (RNG.uniform(0.12, 0.2), 0.12, 0.05), HAY, seg=6, ring=4))
    # trestles: crossed legs at each end, a foot plank between
    for x in (-0.7, 0.7):
        for s in (-1, 1):
            parts.append(cyl(V(x, s * 0.24, 0.0), V(x, -s * 0.10, 0.36), 0.035, 0.03, WOOD, seg=6))
    parts.append(box(V(0, 0, 0.06), (0.75, 0.04, 0.025), WOOD))
    return parts

MAKERS = {'shears': (shears, 64), 'pail': (pail, 128), 'wool': (wool, 64), 'milk': (milk, 64), 'trough': (trough, 128)}

def export_static(ob, path):
    me = ob.data; me.calc_loop_triangles()
    uvl = me.uv_layers.active.data; normals = me.corner_normals
    key = {}; V_, N_, U_, T = [], [], [], []
    for tri in me.loop_triangles:
        ids = []
        for li in tri.loops:
            vi = me.loops[li].vertex_index; n = normals[li].vector; uv = uvl[li].uv
            k = (vi, round(n.x, 3), round(n.y, 3), round(n.z, 3), round(uv.x, 4), round(uv.y, 4))
            if k not in key:
                key[k] = len(V_) // 3
                p = me.vertices[vi].co
                V_ += [round(p.x, 5), round(p.z, 5), round(p.y, 5)]; N_ += [round(n.x, 4), round(n.z, 4), round(n.y, 4)]; U_ += [round(uv.x, 5), round(uv.y, 5)]
            ids.append(key[k])
        T += [ids[0], ids[2], ids[1]]
    with gzip.open(path, 'wt') as f:
        json.dump({'v': V_, 'n': N_, 'uv': U_, 't': [T]}, f, separators=(',', ':'))
    return len(V_) // 3, len(T) // 3

def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    out = argv[0]; names = argv[1:] or list(MAKERS)
    os.makedirs(out, exist_ok=True)
    for name in names:
        clear()
        make, px = MAKERS[name]
        parts = make()
        colour_parts(parts)
        ob = A.join(parts, name)
        for p in ob.data.polygons: p.use_smooth = False
        img = A.bake([ob], px, os.path.join(out, f'{name}_albedo.png'))
        print('EXPORT', name, export_static(ob, os.path.join(out, f'{name}.json.gz')))
        vm = A.preview_material(img); ob.data.materials.clear(); ob.data.materials.append(vm)
        bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
        size = max((max(c[i] for c in bb) - min(c[i] for c in bb)) for i in range(3))
        ob.location.z = -min(c.z for c in bb)
        A.render(os.path.join(out, f'{name}_view.png'), [ob], yaw=40, h=size * 1.2, dist=size * 2.6 + 0.2, size=420, target=Vector((0, 0, size * 0.35)))

if __name__ == '__main__':
    main()
