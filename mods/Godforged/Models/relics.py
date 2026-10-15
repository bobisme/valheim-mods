# Godforged's carried relics, a model file each (draupnir, brisingamen, gjallarhorn) (Blender frame: +Z up, +Y forward, metres; each part centred on the origin):
#   draupnir: Odin's gold ring, a heavy arm ring of twisted gold rods with a plain engraved band, 9 cm across.
#   brisingamen: Freyja's necklace, a gold collar of linked plaques set with glowing amber (the Brísings' fire), lying open.
#   gjallarhorn: Heimdall's horn, a great curved aurochs horn bound in bronze, a gold mouthpiece and a knotwork rim at the bell.
# Run: blender -b --python $PWD/mods/Godforged/Models/relics.py -- render
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from wulib import _obj
from gadlib import *
from mathutils import Vector

reset()
GOLD = 'c_d9a83e_m95_g70'
DARKGOLD = 'c_9c7322_m90_g55'
AMBER = 'c_ff9a1f_m00_g85_e60'
HORN = 'c_d6c7a6_m00_g45'
HORNTIP = 'c_3a2c20_m00_g40'
BRONZE = 'c_8a6a2e_m75_g40'
CUT = 'c_3a2a10_m40_g20'

def sweep(name, path, radius, material, ring=16, caps=True, inward=False):
    """A tube along path, its radius radius(f) for f 0..1 along it, rings turned to follow the path (capped)."""
    pts = [Vector(p) for p in path]
    me = bpy.data.meshes.new(name); bm = bmesh.new(); rings = []; prev = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        n = prev if prev is not None else (Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0)))
        n = (n - t * n.dot(t)).normalized(); prev = n; b = t.cross(n); r = radius(i / (len(pts) - 1))
        rings.append([bm.verts.new(p + (n * math.cos(2 * math.pi * k / ring) + b * math.sin(2 * math.pi * k / ring)) * r) for k in range(ring)])
    for a, c in zip(rings, rings[1:]):
        for k in range(ring):
            j = (k + 1) % ring; bm.faces.new((a[k], a[j], c[j], c[k]))
    if caps: bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    elif inward: bmesh.ops.reverse_faces(bm, faces=bm.faces)   # (the inside of a hollow: seen from within)
    bm.to_mesh(me); bm.free()
    return _obj(name, me, material)

finish_args = []
# --- Draupnir: three gold rods twisted round each other, closed into a ring, and a plain band with runes ---
R0 = 0.045
for s in range(3):
    path = []
    for k in range(241):
        a = 2 * math.pi * k / 240; tw = a * 9 + s * 2 * math.pi / 3
        rr = R0 + 0.0055 * math.cos(tw)
        path.append((rr * math.cos(a), rr * math.sin(a), 0.0055 * math.sin(tw)))
    sweep('draupnir.rod', path, lambda f: 0.0042, GOLD, ring=8)
def arc_band(name, profile, a0, a1, material, steps=24):
    """A band turned from a closed profile [(radius, z), ...] over the angles a0..a1 (degrees), its two ends closed."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    rings = []
    for k in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * k / steps)
        rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z)) for r, z in profile])
    n = len(profile)
    for p, q in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n; bm.faces.new((p[i], p[j], q[j], q[i]))
    bm.faces.new(rings[0]); bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return _obj(name, me, material)
arc_band('draupnir.band', [(R0 - 0.006, -0.0085), (R0 + 0.0085, -0.0085), (R0 + 0.0095, 0.0), (R0 + 0.0085, 0.0085), (R0 - 0.006, 0.0085)],
         8, 82, DARKGOLD)
for k in range(5):   # runes cut round the band
    a = math.radians(17 + k * 14)
    box('draupnir.rune', (0.0018, 0.0016, 0.01), ((R0 + 0.0097) * math.cos(a), (R0 + 0.0097) * math.sin(a), 0), CUT, bevel=0,
        rot=(0, 15 if k % 2 else -15, math.degrees(a)))

finish('draupnir'); reset()
# --- Brísingamen: a gold collar, open at the back, of eleven linked plaques each set with amber ---
def collar_point(f):   # f 0..1 round the open collar
    a = math.radians(-130 + 260 * f)
    return (0.08 * math.sin(a), 0.07 * math.cos(a), 0.0)
sweep('brisingamen.wire', [collar_point(k / 60) for k in range(61)], lambda f: 0.003, GOLD, ring=8)
for i in range(11):
    f = (i + 0.5) / 11; x, y, z = collar_point(f); big = 1.0 - 0.45 * abs(f - 0.5) * 2
    a = math.atan2(x, y)
    plaque = lathe('brisingamen.plaque', [(0.0, -0.002), (0.012 * big, -0.002), (0.014 * big, 0.0), (0.012 * big, 0.003), (0.0, 0.004)],
                   (x * 1.0, y * 1.0, 0.0), GOLD, axis='z', verts=10)
    plaque.rotation_euler = (0, 0, -a)
    gem = lathe('brisingamen.amber', [(0.0, 0.003), (0.008 * big, 0.003), (0.006 * big, 0.008), (0.0, 0.0095)], (x, y, 0.0), AMBER, axis='z', verts=12)
    torus('brisingamen.setting', 0.0085 * big, 0.0012, (x, y, 0.0035), DARKGOLD, axis='z', verts=14, ring=4)
# the clasp ends: two little gold boar heads, stood for by knobs
for f in (0.0, 1.0):
    x, y, z = collar_point(f)
    lathe('brisingamen.clasp', [(0.0, -0.004), (0.006, -0.004), (0.007, 0.0), (0.005, 0.005), (0.0, 0.006)], (x, y, 0), GOLD, axis='z', verts=10)

finish('brisingamen'); reset()
# --- Gjallarhorn: a great curved horn ---
HP = [(0.0, 0.0, 0.0)]
for k in range(1, 61):
    f = k / 60
    HP.append((0.0, -0.62 * f + 0.0, 0.20 * math.sin(math.pi * f * 0.85) + 0.06 * f * f))
def horn_r(f): return 0.012 + 0.07 * f ** 1.6
horn = sweep('gjallarhorn.horn', HP, horn_r, HORN, ring=24, caps=False)
# hollow: its dark inside, seen down the bell (and a cap far down the throat)
sweep('gjallarhorn.inside', HP[30:], lambda f: horn_r(0.5 + 0.5 * f) - 0.004, HORNTIP, ring=24, caps=False, inward=True)
# the dark tip and a gold mouthpiece at the narrow end
sweep('gjallarhorn.tip', HP[:9], lambda f: horn_r(f * 8 / 60) + 0.0012, HORNTIP, ring=24)
lathe('gjallarhorn.mouth', [(0.0, -0.03), (0.016, -0.03), (0.02, -0.026), (0.012, -0.018), (0.012, 0.004), (0.0, 0.004)], (0, 0.0, 0.0), GOLD, axis='y', verts=20)
# bronze bands along it, and a broad knotwork rim at the bell
for f in (0.2, 0.42, 0.66):
    i = int(f * 60); p = Vector(HP[i]); t = (Vector(HP[i + 1]) - Vector(HP[i - 1])).normalized()
    o = torus('gjallarhorn.band', horn_r(f) + 0.002, 0.004, tuple(p), BRONZE, axis='z', verts=28, ring=6)
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(t)
pe = Vector(HP[-1]); te = (Vector(HP[-1]) - Vector(HP[-2])).normalized()
for d, rr in ((-0.03, 0.0), (0.0, 0.004)):
    o = torus('gjallarhorn.rim', horn_r(1.0) + 0.003 + rr, 0.006, tuple(pe + te * d), GOLD, axis='z', verts=40, ring=6)
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(te)
# centre the horn on its middle
for o in list(bpy.context.scene.collection.objects):
    if o.name.startswith('gjallarhorn'): o.location.y += 0.31; o.location.z -= 0.12

report(12)
finish('gjallarhorn')
