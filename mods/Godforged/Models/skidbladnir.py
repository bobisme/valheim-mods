# Skíðblaðnir, Freyr's ship folded small: a miniature karve 42 cm long carved in dark oak, a gilded sheer strake and a gold dragon at
# the stem, painted shields along her sides, and her striped sail furled on the yard (Blender frame: +Z up, +Y forward, metres).
# Run: blender -b --python $PWD/mods/Godforged/Models/skidbladnir.py -- render
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from wulib import _obj
from gadlib import *
from mathutils import Vector

reset()
OAK = 'c_5a3f26_m00_g25'
OAKIN = 'c_3e2b1a_m00_g20'
GOLD = 'c_d9a83e_m95_g70'
RED = 'c_9c2a1e_m00_g30'
CLOTH = 'c_d8ccb0_m00_g20'
BLUE = 'c_2c4a7a_m00_g30'
IRON = 'c_4a4a4a_m70_g40'

L, B, H, K = 0.42, 0.068, 0.048, 10
RAKE = 0.022   # length, half beam, depth at the waist, points across a section

def station(f, inner=False):
    """Section at f (0 stern .. 1 stem): points from the port gunwale down round the keel to starboard."""
    y = (f - 0.5) * L
    half = B * math.sin(math.pi * f) ** 0.75
    top = H + 0.035 * abs(2 * f - 1) ** 3            # the sheer, rising to both ends
    keel = 0.032 * abs(2 * f - 1) ** 3.5            # the keel's rocker, sweeping up into stem and stern
    if inner: half *= 0.86; keel += 0.007; top -= 0.002
    pts = []
    for k in range(K + 1):
        a = math.pi * (-0.5 + k / K)                  # round the bilge: a half ellipse, a little flared at the top
        z = keel + (top - keel) * (1 - math.cos(a))
        rake = RAKE * (2 * f - 1) ** 7 * (z - keel) / max(top - keel, 1e-4)   # the stem and stern lean out as they rise
        pts.append(Vector((half * math.sin(a) * (1 + 0.08 * abs(math.sin(a))), y + rake, z)))
    return pts

def hull():
    me = bpy.data.meshes.new('ship.hull'); bm = bmesh.new()
    N = 16
    outer = [[bm.verts.new(p) for p in station(i / N)] for i in range(N + 1)]
    inner = [[bm.verts.new(p) for p in station(i / N, True)] for i in range(1, N)]
    inner = [outer[0]] + inner + [outer[N]]           # (the ends close on the stem and stern)
    for a, c in zip(outer, outer[1:]):
        for k in range(K): bm.faces.new((a[k], a[k + 1], c[k + 1], c[k]))
    for a, c in zip(inner, inner[1:]):
        for k in range(K):
            try: bm.faces.new((c[k], c[k + 1], a[k + 1], a[k]))
            except ValueError: pass
    for i in range(1, N):                              # the gunwale's top edge, joining outer to inner
        for k in (0, K):
            o0, o1, i0, i1 = outer[i][k], outer[i + 1][k], inner[i][k], inner[i + 1][k]
            try: bm.faces.new((o0, o1, i1, i0) if k == 0 else (o0, i0, i1, o1))
            except ValueError: pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.to_mesh(me); bm.free()
    ob = _obj('ship.hull', me, OAK)
    ob.data.materials.append(mat(OAKIN))
    # (the inside darker: faces whose normal points up into the hull)
    for p in ob.data.polygons:
        c = p.center
        if p.normal.z > 0.3 and abs(c.x) < B * 0.8: p.material_index = 1
    return ob

hull()

# A gilded strake just under the gunwale, both sides.
for side in (-1, 1):
    pts = []
    for i in range(1, 16):
        f = i / 16; s = station(f)[0 if side < 0 else K]; pts.append((s.x * 1.02, s.y, s.z - 0.006))
    sweep_pts = pts
    tube_path('ship.strake' + ('p' if side < 0 else 's'), sweep_pts, 0.0025, GOLD, ring=4)

# Stem and stern posts, curling up and in; a dragon's head on the stem, a curl on the stern.
def post(name, sign, curl):
    end = station(1.0 if sign > 0 else 0.0)[K // 2]
    top = station(1.0 if sign > 0 else 0.0)[0]
    p0 = Vector((0, end.y, end.z)); p1 = Vector((0, end.y + sign * RAKE * 0.6, (end.z + top.z) / 2))
    p2 = Vector((0, end.y + sign * (RAKE + 0.006), top.z + 0.02)); p3 = Vector((0, end.y + sign * (RAKE - curl), top.z + 0.045))
    pts = bezier(p0, p1, p2, p3, n=10)
    tube_path(name, pts, 0.0045, OAK, ring=6)
    return Vector(pts[-1])

head = post('ship.stem', 1, -0.004)
post('ship.sternpost', -1, 0.022)
lathe('ship.dragon', [(0.0, -0.006), (0.0065, -0.004), (0.0075, 0.006), (0.0045, 0.018), (0.0, 0.024)], (0, head.y, head.z), GOLD, axis='y', verts=8)
cyl('ship.horn1', 0.0016, 0.014, (0.004, head.y - 0.002, head.z + 0.008), GOLD, axis='z', verts=4, rot=(-30, 0, 0), bevel=0)
cyl('ship.horn2', 0.0016, 0.014, (-0.004, head.y - 0.002, head.z + 0.008), GOLD, axis='z', verts=4, rot=(-30, 0, 0), bevel=0)

# Shields along her sides, alternating red and blue, each with a gold boss.
for side in (-1, 1):
    for i in range(6):
        f = 0.28 + i * 0.09; s = station(f)[0 if side < 0 else K]
        x = s.x + side * 0.003; col = RED if (i + (side > 0)) % 2 == 0 else BLUE
        cyl(f'ship.shield{side}{i}', 0.011, 0.002, (x, s.y, s.z - 0.011), col, axis='x', verts=10, bevel=0)
        cyl(f'ship.boss{side}{i}', 0.003, 0.003, (x + side * 0.002, s.y, s.z - 0.011), GOLD, axis='x', verts=6, bevel=0)

# Mast, yard and the furled sail: red and white stripes rolled on the yard.
cyl('ship.mast', 0.0035, 0.13, (0, 0.01, 0.07), OAK, axis='z', verts=8, bevel=0)
cyl('ship.yard', 0.0022, 0.13, (0, 0.012, 0.128), OAK, axis='x', verts=6, bevel=0)
for k in range(5):
    cyl(f'ship.sail{k}', 0.009, 0.022, (-0.044 + k * 0.022, 0.012, 0.121), RED if k % 2 == 0 else CLOTH, axis='x', verts=10, bevel=0)
cyl('ship.mastfish', 0.006, 0.01, (0, 0.01, 0.012), IRON, axis='z', verts=8, bevel=0)

# Centre it: bottom of the keel at z -0.05 so it sits about the middle of its bounds.
for o in bpy.context.scene.collection.objects:
    if o.type == 'MESH': o.location.z -= 0.06

report()
finish('skidbladnir')
