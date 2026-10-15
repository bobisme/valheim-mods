# The infrared goggles (WU_InfraredGoggles), a helmet: built under "attach" on the helmet joint and placed by Wear.PlaceOnJoint (origin
# 6 cm forward of the helmet joint: about between the eyes). Blender frame: +Y forward (out of the face), +Z up.
# Same footprint as the first primitive pair: lens barrels at x +-0.042 pointing forward, red lenses, battery on top, strap round.
# Run: blender -b --python ~/.cache/wu-models/wm-wear/goggles.py -- render
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from wulib import *
import bmesh, math
from mathutils import Vector

def ring(name, profile, at=(0, 0, 0), material='gunmetal', axis='y', verts=28):
    """A closed profile [(radius, along), ...] turned round the axis with no end caps: rims, bands, lips (lathe() caps its end rings)."""
    me = bpy.data.meshes.new(name); bm = bmesh.new(); n = len(profile)
    rings = [[bm.verts.new((r * math.cos(2 * math.pi * i / verts), r * math.sin(2 * math.pi * i / verts), z)) for i in range(verts)] for r, z in profile]
    for k in range(n):
        a, b = rings[k], rings[(k + 1) % n]
        for i in range(verts): bm.faces.new((a[i], a[(i + 1) % verts], b[(i + 1) % verts], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    turn = {'z': (0, 0, 0), 'y': (-90, 0, 0), 'x': (0, 90, 0)}[axis]
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    ob.location = at; ob.rotation_euler = [math.radians(a) for a in turn]; ob.data.materials.append(mat(material))
    return ob

def band(name, pts, width, thick, material):
    """A flat strap along points (in the xy plane), 'width' tall in z, 'thick' across."""
    me = bpy.data.meshes.new(name); bm = bmesh.new(); rows = []
    for i, p in enumerate(pts):
        p = Vector(p); t = (Vector(pts[min(i + 1, len(pts) - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
        nrm = Vector((0, 0, 1)).cross(t).normalized()
        rows.append([bm.verts.new(p + Vector((0, 0, dz)) + nrm * dn) for dz, dn in ((-width / 2, -thick / 2), (-width / 2, thick / 2), (width / 2, thick / 2), (width / 2, -thick / 2))])
    for a, b in zip(rows, rows[1:]):
        for k in range(4): bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bm.faces.new(list(reversed(rows[0]))); bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob); ob.data.materials.append(mat(material))
    return ob

def pipe(name, pts, r, material, verts=8):
    me = bpy.data.meshes.new(name); bm = bmesh.new(); pts = [Vector(p) for p in pts]; rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        a = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        u = t.cross(a).normalized(); v = t.cross(u).normalized()
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / verts) + v * math.sin(2 * math.pi * k / verts)) * r) for k in range(verts)])
    for a, b in zip(rings, rings[1:]):
        for k in range(verts): bm.faces.new((a[k], a[(k + 1) % verts], b[(k + 1) % verts], b[k]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob); ob.data.materials.append(mat(material))
    return ob

reset()
X = 0.042   # lens barrels' centres
# --- the face plate joining the two eyes, and the rubber cushion against the face
plate = [(-0.074, -0.012), (-0.066, -0.024), (-0.02, -0.026), (-0.008, -0.014), (0.008, -0.014), (0.02, -0.026), (0.066, -0.024), (0.074, -0.012),
         (0.074, 0.016), (0.062, 0.027), (-0.062, 0.027), (-0.074, 0.016)]
extrude('body.plate', plate, 0.024, (0, 0.002, 0), 'gunmetal', plane='xz', bevel=0.005, segments=2)
for s in (-1, 1):
    torus(f'body.cushion{s}', 0.025, 0.0075, (s * X, -0.013, 0.0), 'rubber', axis='y', verts=24, ring=8)
# --- lens barrels: stepped housings, a knurled focus ring, a hood, the red lens set back inside
for s in (-1, 1):
    x = s * X
    lathe(f'body.barrel{s}', [(0, 0.008), (0.0225, 0.008), (0.0235, 0.012), (0.0235, 0.024), (0.021, 0.026), (0.021, 0.034), (0.0245, 0.036), (0.026, 0.05),
                                (0.0235, 0.051), (0.0195, 0.041), (0, 0.041)], (x, 0, 0), 'black', axis='y', verts=28)
    for k in range(4):   # knurled focus ring
        torus(f'body.knurl{s}{k}', 0.0218, 0.0012, (x, 0.0275 + k * 0.0018, 0), 'gunmetal', axis='y', verts=24, ring=4)
    lathe(f'body.lens{s}', [(0, 0.0425), (0.0185, 0.0425), (0.0185, 0.0438), (0.012, 0.0455), (0, 0.0462)], (x, 0, 0), 'c_e8231a_m20_g95_e70', axis='y', verts=24)
    torus(f'body.lensring{s}', 0.0192, 0.0012, (x, 0.0428, 0), 'chrome', axis='y', verts=24, ring=4)
    ring(f'body.hoodlip{s}', [(0.0238, 0.0502), (0.0262, 0.0496), (0.0262, 0.0508), (0.024, 0.0514)], (x, 0, 0), 'chrome', axis='y', verts=28)
# --- bridge over the nose, a sensor between the eyes
box('body.bridge', (0.026, 0.03, 0.022), (0, 0.016, 0.008), 'gunmetal', bevel=0.005)
cyl('body.sensor', 0.007, 0.012, (0, 0.034, 0.012), 'black', axis='y', verts=16, bevel=0.0015)
lathe('body.sensoreye', [(0, 0.039), (0.0045, 0.039), (0.0045, 0.0405), (0, 0.0415)], (0, 0, 0.012), 'emit_red', axis='y', verts=12)
# --- battery pack on the forehead: off-white case, hazard stripe, LED, switch; a cable to the right barrel
batt = box('body.battery', (0.068, 0.042, 0.022), (0, -0.002, 0.04), 'offwhite', bevel=0.006, segments=3)
cut(batt, box('cut', (0.05, 0.03, 0.006), (0, -0.002, 0.052), 'black', bevel=0.002))      # a recess in its lid
box('body.cell', (0.046, 0.026, 0.004), (0, -0.002, 0.0475), 'hazard', bevel=0.001)
for k in range(3): box(f'body.chev{k}', (0.004, 0.024, 0.0042), (-0.014 + k * 0.012, -0.002, 0.0476), 'black', bevel=0, rot=(0, 0, 35))
cyl('body.led', 0.0028, 0.004, (0.026, 0.02, 0.044), 'emit_green', axis='y', verts=10, bevel=0)
cyl('body.switch', 0.0035, 0.01, (-0.026, 0.021, 0.042), 'chrome', axis='y', verts=8, bevel=0)
box('body.battfoot', (0.04, 0.02, 0.012), (0, -0.004, 0.026), 'gunmetal', bevel=0.003)
pipe('body.cable', [(0.034, -0.016, 0.038), (0.05, -0.02, 0.03), (0.066, -0.014, 0.016), (0.07, 0.0, 0.008)], 0.0025, 'black')
# --- side module (right): focus/brightness dial and a stub antenna
box('body.side', (0.016, 0.03, 0.03), (0.081, -0.004, 0.002), 'gunmetal', bevel=0.004)
cyl('body.dial', 0.009, 0.006, (0.091, -0.004, 0.002), 'chrome', axis='x', verts=16, bevel=0.001)
for k in range(8):
    a = math.radians(k * 45); box(f'body.grip{k}', (0.0025, 0.002, 0.004), (0.0935, -0.004 + 0.0088 * math.cos(a), 0.002 + 0.0088 * math.sin(a)), 'black', bevel=0, rot=(-k * 45, 0, 0))
cyl('body.antenna', 0.0015, 0.03, (0.083, -0.012, 0.03), 'black', axis='z', verts=6, bevel=0)
lathe('body.antennatip', [(0, 0.044), (0.0028, 0.044), (0.0028, 0.048), (0, 0.05)], (0.083, -0.012, 0), 'emit_red', axis='z', verts=8)
# --- the strap round the head: an oval band from temple to temple round the back, with an adjuster buckle
cy, a, b = -0.065, 0.086, 0.118
pts = []
for i in range(25):
    t = math.radians(20 - 220 * i / 24)
    pts.append((a * math.cos(t), cy + b * math.sin(t), 0.0))
band('body.strap', pts, 0.026, 0.005, 'black')
for s in (-1, 1):
    box(f'body.anchor{s}', (0.012, 0.022, 0.03), (s * 0.08, -0.024, 0.0), 'gunmetal', bevel=0.003, rot=(0, 0, s * -20))
box('body.buckle', (0.03, 0.008, 0.032), (0, cy - b - 0.002, 0.0), 'chrome', bevel=0.002)
for ob in bpy.context.scene.collection.objects:
    if ob.type == 'MESH':
        for p in ob.data.polygons: p.use_smooth = True
        ob.data.set_sharp_from_angle(angle=math.radians(35))
finish('goggles')
