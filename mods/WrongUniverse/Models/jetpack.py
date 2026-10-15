# The jetpack (WU_Jetpack), worn on the back: built under attach_Spine2. Blender frame: +Y is the wearer's front, +Z up, so the
# pack sits behind (-Y), nozzles pointing down. Same size and place as the first primitive pack Bob liked: two white tanks with
# hazard bands at x +-0.10, 0.15 m across, 0.5 m tall; nozzle exits at z -0.355 (the runtime flames start there).
# Run: blender -b --python ~/.cache/wu-models/wm-wear/jetpack.py -- render
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

def pipe(name, pts, r, material, verts=12):
    """A tube swept along points (smooth-ish bends need extra points), capped."""
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

def arc(c, r, a0, a1, n, plane='yz', x=0):
    out = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        out.append((x, c[0] + r * math.cos(a), c[1] + r * math.sin(a)) if plane == 'yz' else (c[0] + r * math.cos(a), x, c[1] + r * math.sin(a)))
    return out

reset()
R, TY = 0.072, -0.13           # tank radius, tanks' centre line (y)
ZB, ZT = -0.205, 0.255         # where the tanks' domes end (bottom, top)

# --- backplate: a contoured gunmetal shell with a rubber cushion against the back
plate = [(-0.15, -0.17), (-0.13, -0.21), (0.13, -0.21), (0.15, -0.17), (0.155, 0.12), (0.12, 0.205), (-0.12, 0.205), (-0.155, 0.12)]
extrude('body.plate', plate, 0.034, (0, -0.036, 0), 'gunmetal', plane='xz', bevel=0.008, segments=3)
cushion = [(-0.125, -0.15), (0.125, -0.15), (0.13, 0.11), (0.1, 0.17), (-0.1, 0.17), (-0.13, 0.11)]
extrude('body.cushion', cushion, 0.02, (0, -0.012, 0), 'rubber', plane='xz', bevel=0.007, segments=3)
for z in (-0.06, 0.04):   # quilting in the cushion
    box(f'body.quilt{z}', (0.2, 0.006, 0.004), (0, 0.0, z), 'black', bevel=0.0015)
# shoulder straps (from the plate's top edge forward over the shoulders) and a waist belt
for s in (-1, 1):
    x = s * 0.095
    pts = [(x, -0.03, 0.19)] + [(x, p[1], p[2]) for p in arc((0.02, 0.17), 0.065, 160, 20, 7)] + [(x, 0.105, 0.12)]
    # a flat band 42 mm wide, 6 mm thick: offset along the path's normal
    me = bpy.data.meshes.new(f'body.strap{s}'); bm = bmesh.new(); rows = []
    for i, p in enumerate(pts):
        p = Vector(p); t = (Vector(pts[min(i + 1, len(pts) - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
        nrm = Vector((1, 0, 0)).cross(t).normalized()
        rows.append([bm.verts.new(p + Vector((dx, 0, 0)) + nrm * dn) for dx, dn in ((-0.021, -0.003), (0.021, -0.003), (0.021, 0.003), (-0.021, 0.003))])
    for a, b in zip(rows, rows[1:]):
        for k in range(4): bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bm.faces.new(list(reversed(rows[0]))); bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(f'body.strap{s}', me); bpy.context.scene.collection.objects.link(ob); ob.data.materials.append(mat('black'))
    box(f'body.buckle{s}', (0.05, 0.012, 0.03), (x, 0.112, 0.125), 'chrome', bevel=0.003, rot=(25, 0, 0))
    box(f'body.pad{s}', (0.06, 0.03, 0.022), (x, 0.0, 0.215), 'rubber', bevel=0.008, rot=(-20, 0, 0))
    # waist belt stubs going round the sides
    box(f'body.belt{s}', (0.03, 0.12, 0.045), (s * 0.15, 0.03, -0.17), 'black', bevel=0.005, rot=(0, 0, s * 12))
    box(f'body.beltclip{s}', (0.034, 0.03, 0.05), (s * 0.155, -0.012, -0.17), 'hazard', bevel=0.005)

# --- the tanks: domed pressure vessels, weld seams, hazard bands, clamps to the plate
def tank_profile():
    pts = [(0.0, ZB - 0.0)]
    zb0, zt0 = ZB + R * 0.85, ZT - R * 0.85
    for i in range(1, 9):
        a = math.radians(-90 + 90 * i / 8); pts.append((R * math.cos(a), zb0 + R * 0.85 * math.sin(a)))
    for i in range(0, 9):
        a = math.radians(90 * i / 8); pts.append((R * math.cos(a), zt0 + R * 0.85 * math.sin(a)))
    pts[-1] = (0.0, ZT)
    return pts, zb0, zt0
prof, zb0, zt0 = tank_profile()
for s in (-1, 1):
    x = s * 0.1
    lathe(f'body.tank{s}', prof, (x, TY, 0), 'white', axis='z', verts=28)
    for z in (zb0, zt0):
        torus(f'body.seam{s}{z}', R + 0.0005, 0.0016, (x, TY, z), 'offwhite', axis='z', verts=28, ring=3)
    for z in (0.12, -0.08):   # hazard bands, proud of the shell, chamfered
        ring(f'body.band{s}{z}', [(R - 0.004, z - 0.017), (R + 0.0028, z - 0.016), (R + 0.0035, z - 0.013), (R + 0.0035, z + 0.013), (R + 0.0028, z + 0.016), (R - 0.004, z + 0.017)],
              (x, TY, 0), 'hazard', axis='z', verts=24)
    for z in (0.17, -0.15):   # clamp bands round the tank, bolted to the plate
        ring(f'body.clamp{s}{z}', [(R - 0.003, z - 0.008), (R + 0.004, z - 0.007), (R + 0.0045, z - 0.004), (R + 0.0045, z + 0.004), (R + 0.004, z + 0.007), (R - 0.003, z + 0.008)],
              (x, TY, 0), 'gunmetal', axis='z', verts=20)
        box(f'body.clampfoot{s}{z}', (0.03, 0.032, 0.022), (x - s * 0.02, -0.068, z), 'gunmetal', bevel=0.004)
        cyl(f'body.bolt{s}{z}', 0.004, 0.01, (x - s * 0.02, -0.088, z), 'chrome', axis='y', verts=6, bevel=0)
    # neck, valve and handwheel on top
    lathe(f'body.neck{s}', [(0, ZT - 0.01), (0.022, ZT - 0.01), (0.022, ZT + 0.008), (0.016, ZT + 0.012), (0.016, ZT + 0.03), (0.02, ZT + 0.033), (0.02, ZT + 0.042), (0, ZT + 0.042)],
          (x, TY, 0), 'chrome', axis='z', verts=16)
    torus(f'body.wheel{s}', 0.02, 0.0035, (x, TY, ZT + 0.052), 'red', axis='z', verts=16, ring=6)
    for a in (0, 120, 240):
        box(f'body.spoke{s}{a}', (0.036, 0.004, 0.003), (x, TY, ZT + 0.052), 'red', bevel=0, rot=(0, 0, a))
    cyl(f'body.stem{s}', 0.005, 0.02, (x, TY, ZT + 0.045), 'chrome', axis='z', verts=8, bevel=0)
    # combustion chamber, cooling fins, throat and bell nozzle under the tank
    lathe(f'body.coupling{s}', [(0, ZB + 0.012), (0.03, ZB + 0.012), (0.034, ZB - 0.004), (0.034, ZB - 0.012), (0.03, ZB - 0.016), (0, ZB - 0.016)], (x, TY, 0), 'chrome', axis='z', verts=20)
    lathe(f'body.chamber{s}', [(0, ZB - 0.014), (0.042, ZB - 0.016), (0.046, ZB - 0.022), (0.046, ZB - 0.062), (0.04, ZB - 0.07), (0.026, ZB - 0.078), (0, ZB - 0.078)], (x, TY, 0), 'gunmetal', axis='z', verts=24)
    for k in range(3):
        torus(f'body.fin{s}{k}', 0.047, 0.0024, (x, TY, ZB - 0.033 - k * 0.012), 'steel', axis='z', verts=20, ring=4)
    zt = ZB - 0.078
    bell = [(0, zt + 0.002), (0.024, zt + 0.002), (0.024, zt - 0.008), (0.03, zt - 0.025), (0.045, zt - 0.055), (0.058, zt - 0.072),
            (0.054, zt - 0.074), (0.041, zt - 0.058), (0.026, zt - 0.03), (0.02, zt - 0.012), (0, zt - 0.012)]
    lathe(f'body.bell{s}', bell, (x, TY, 0), 'gunmetal', axis='z', verts=24)
    ring(f'body.bellheat{s}', [(0.0458, zt - 0.0552), (0.0592, zt - 0.0722), (0.0598, zt - 0.0712), (0.0465, zt - 0.054)], (x, TY, 0), 'c_7a4430_m70_g40', axis='z', verts=24)   # heat tint on the outside of the lip
    lathe(f'body.throat{s}', [(0, zt - 0.0135), (0.019, zt - 0.0135), (0.019, zt - 0.016), (0, zt - 0.016)], (x, TY, 0), 'emit_orange', axis='z', verts=20)  # glowing throat deep inside

# --- the core between the tanks: a vented housing with the status lamp, the manifold and a pressure gauge on top
core = box('body.core', (0.05, 0.13, 0.36), (0, -0.125, 0.0), 'offwhite', bevel=0.01, segments=3)
for k in range(6):   # vent slots cut into the back
    cut(core, box(f'cutter{k}', (0.032, 0.03, 0.007), (0, -0.19, -0.13 + k * 0.016), 'black', bevel=0))
box('body.ventback', (0.03, 0.01, 0.1), (0, -0.177, -0.09), 'black', bevel=0.001)   # dark behind the slots
lathe('body.lampbezel', [(0, -0.186), (0.024, -0.186), (0.024, -0.194), (0.019, -0.198), (0.016, -0.196), (0, -0.196)], (0, 0, 0.12), 'chrome', axis='y', verts=24)
lathe('body.lamp', [(0, -0.188), (0.016, -0.188), (0.015, -0.198), (0.011, -0.204), (0.0, -0.206)], (0, 0, 0.12), 'emit_cyan', axis='y', verts=20)
for k, c in enumerate(('emit_green', 'emit_green', 'emit_red')):   # status LEDs
    cyl(f'body.led{k}', 0.0035, 0.004, (-0.012 + k * 0.012, -0.191, 0.065), c, axis='y', verts=10, bevel=0)
box('body.label', (0.034, 0.003, 0.05), (0, -0.1915, 0.0), 'hazard', bevel=0.001)
for k in range(3): box(f'body.chevron{k}', (0.03, 0.003, 0.006), (0, -0.1935, -0.015 + k * 0.014), 'black', bevel=0, rot=(0, 45, 0))
# manifold across the tank tops, gauge in the middle
pipe('body.manifold', [(-0.1, TY, ZT + 0.025), (-0.06, TY, ZT + 0.025), (-0.03, TY, ZT + 0.004), (-0.012, TY, 0.2), (0.012, TY, 0.2), (0.03, TY, ZT + 0.004), (0.06, TY, ZT + 0.025), (0.1, TY, ZT + 0.025)], 0.0065, 'chrome', verts=12)
cyl('body.gaugebody', 0.026, 0.022, (0, -0.15, 0.215), 'gunmetal', axis='y', verts=20, bevel=0.003)
cyl('body.gaugeface', 0.021, 0.004, (0, -0.1625, 0.215), 'white', axis='y', verts=24, bevel=0)
torus('body.gaugerim', 0.022, 0.003, (0, -0.163, 0.215), 'chrome', axis='y', verts=24, ring=6)
box('body.needle', (0.0025, 0.0015, 0.016), (0.004, -0.1655, 0.218), 'red', bevel=0, rot=(0, -35, 0))
ring('body.redzone', [(0.0165, -0.1647), (0.0198, -0.1647), (0.0198, -0.1662), (0.0165, -0.1662)], (0, 0, 0.215), 'red', axis='y', verts=12)
# fuel lines from the core down to each chamber
for s in (-1, 1):
    pipe(f'body.fuel{s}', [(s * 0.02, -0.13, -0.17), (s * 0.03, -0.14, -0.2), (s * 0.06, -0.15, -0.22), (s * 0.085, -0.165, -0.245)], 0.0055, 'black', verts=10)
    cyl(f'body.fuelnut{s}', 0.008, 0.008, (s * 0.085, -0.165, -0.245), 'brass', axis='z', verts=6, bevel=0)
# preview shading as the game will show it (the exporter smooths the same way)
for ob in bpy.context.scene.collection.objects:
    if ob.type == 'MESH':
        for p in ob.data.polygons: p.use_smooth = True
        ob.data.set_sharp_from_angle(angle=math.radians(35))
finish('jetpack')
