# The Hover Speeder (Wrong Universe): a speeder bike that hovers on four repulsor pads.
# Run: blender -b --python ~/.cache/wu-models/wm-speeder/speeder.py -- render   (copy wulib.py next to it)
# Blender frame: +Y forward, +Z up; the hull's underside near z 0 (the game's frame: x, y=z, z=y). Game points kept from the old model:
# seat (0, 0.8, -0.4), passenger (0, 0.85, -1.05), pads (+-0.32, 0.08, +-0.9), thrust (0, 0.45, -1.3), headlight (0, 0.5, 1.62).
# Parts: "body" (everything fixed), "repulsor" (one pad, placed four times by the game under its pad0..3 probes), "flame" (the rear
# thrust cone, stretched with speed under the "thrust" group).
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from wulib import *
import bmesh, math
from wulib import _bevel, _apply
from mathutils import Vector

def loft(name, sections, mats, at=(0, 0, 0), ring=48, tip=None, tail_tip=None, bevel=0.0):
    """A smooth body through cross-sections (y, half-width, z-bottom, z-top, roundness exponent), each segment painted mats[i]
    (one name for all). tip/tail_tip: (y, z) points closing the front/back ends; otherwise flat caps."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    names = mats if isinstance(mats, list) else [mats] * (len(sections) - 1)
    order = []
    for n in names + ['_end']:
        if n != '_end' and n not in order: order.append(n)
    rings = []
    for (y, hw, zb, zt, e) in sections:
        zc, hh = (zb + zt) / 2, (zt - zb) / 2; pts = []
        for i in range(ring):
            a = 2 * math.pi * i / ring; c, s = math.cos(a), math.sin(a)
            x = hw * math.copysign(abs(c) ** (2 / e), c); z = zc + hh * math.copysign(abs(s) ** (2 / e), s)
            pts.append(bm.verts.new((x, y, z)))
        rings.append(pts)
    faces = []
    for k, (a, b) in enumerate(zip(rings, rings[1:])):
        for i in range(ring):
            j = (i + 1) % ring
            f = bm.faces.new((a[i], a[j], b[j], b[i])); f.material_index = order.index(names[k])
    def close(r, point, idx):
        if point is None:
            f = bm.faces.new(r if idx == 0 else list(reversed(r))); f.material_index = order.index(names[0 if idx == 0 else -1])
        else:
            c = bm.verts.new((0, point[0], point[1]))
            for i in range(ring):
                j = (i + 1) % ring
                f = bm.faces.new((r[j], r[i], c) if idx == 0 else (r[i], r[j], c)); f.material_index = order.index(names[0 if idx == 0 else -1])
    close(rings[0], tail_tip, 0); close(rings[-1], tip, 1)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob); ob.location = at
    for n in order: ob.data.materials.append(mat(n))
    if bevel: _bevel(ob, bevel)
    return ob

def cutT(target, cutter):
    """A boolean cut whose new faces take the cutter's material (dark slots and seams)."""

    _apply(target)
    m = target.modifiers.new('cut', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.object = cutter; m.solver = 'EXACT'; m.material_mode = 'TRANSFER'
    _apply(target); bpy.data.objects.remove(cutter, do_unlink=True)
    return target

FAIR = [
    (-1.18, 0.14, 0.44, 0.60, 2.4),
    (-1.05, 0.20, 0.34, 0.68, 2.6),
    (-0.80, 0.24, 0.26, 0.66, 2.8),
    (-0.45, 0.255, 0.22, 0.64, 2.8),
    (-0.10, 0.27, 0.20, 0.68, 2.8),
    (0.20, 0.285, 0.20, 0.77, 2.6),
    (0.50, 0.275, 0.22, 0.79, 2.6),
    (0.80, 0.24, 0.25, 0.73, 2.5),
    (0.98, 0.215, 0.27, 0.69, 2.5),
    (1.06, 0.205, 0.28, 0.675, 2.5),
    (1.30, 0.15, 0.33, 0.61, 2.3),
    (1.50, 0.075, 0.39, 0.53, 2.2),
]
def at_y(y, inset):
    for a, b in zip(FAIR, FAIR[1:]):
        if a[0] <= y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0]); s = [a[i] + (b[i] - a[i]) * t for i in range(5)]
            return (y, s[1] - inset, s[2] + inset, s[3] - inset, s[4])

reset()

# ---- the fairing: tail, seat deck, tank hump in front of the rider, a long nose. Hazard band behind the nose.
fair = loft('body.fairing', FAIR, ['white'] * 8 + ['hazard'] + ['white'] * 2, tip=(1.60, 0.465), bevel=0.012)

# side vents (three slanted slots each side, cut into the fairing ahead of the rider's knees), dark inside
for side in (-1, 1):
    for k in range(3):
        c = box('cut', (0.08, 0.032, 0.012), (side * 0.30, 0.62 + k * 0.07, 0.47 - k * 0.015), 'black', bevel=0, rot=(0, side * 12, 0))
        cutT(fair, c)
# panel seams: shallow dark grooves round the hull (a slab minus the hull shrunk by the groove's depth)
for y in (-0.70, 0.05, 0.86):
    g = box('cut', (0.8, 0.005, 1.0), (0, y, 0.5), 'black', bevel=0)
    inner = loft('keep', [at_y(y - 0.01, 0.004), at_y(y + 0.01, 0.004)], 'white')
    cut(g, inner)
    cutT(fair, g)

# ---- the belly keel (gunmetal), with the four pad struts
keel = loft('body.keel', [(-0.95, 0.12, 0.13, 0.30, 2.6), (-0.70, 0.18, 0.11, 0.32, 2.8), (0.75, 0.18, 0.11, 0.32, 2.8), (1.10, 0.12, 0.16, 0.34, 2.6)],
            'gunmetal', tail_tip=None, bevel=0.01)
for x in (-0.32, 0.32):
    for y in (-0.9, 0.9):
        if x > 0: box('body.strutbar', (0.64, 0.08, 0.05), (0, y, 0.17), 'gunmetal', bevel=0.012)
        cyl('body.strut', 0.035, 0.12, (x, y, 0.18), 'steel', axis='z', bevel=0.004)

# ---- saddle (rider) and pillion, padded black leather with contrast piping
seat = loft('body.saddle', [(-0.80, 0.13, 0.60, 0.72, 2.6), (-0.70, 0.17, 0.60, 0.77, 3.0), (-0.40, 0.18, 0.60, 0.765, 3.2),
                            (-0.15, 0.17, 0.60, 0.79, 3.0), (-0.02, 0.12, 0.62, 0.80, 2.6)], 'c_221d1a_m00_g25', bevel=0.012)
pill = loft('body.pillion', [(-1.24, 0.11, 0.66, 0.78, 2.6), (-1.18, 0.15, 0.66, 0.83, 3.0), (-0.90, 0.16, 0.66, 0.84, 3.0),
                             (-0.84, 0.12, 0.66, 0.80, 2.6)], 'c_221d1a_m00_g25', bevel=0.012)
for k, (y0, y1, zt) in enumerate(((-0.70, -0.12, 0.775), (-1.17, -0.90, 0.835))):
    for side in (-1, 1):
        w = 0.165 if k == 0 else 0.145
        cyl('body.piping', 0.006, y1 - y0, (side * w, (y0 + y1) / 2, zt - 0.03), 'hazard', axis='y', verts=8, bevel=0)
    # stitched pleats across the seat top: grooves
    for i in range(4 if k == 0 else 2):
        yy = y0 + (y1 - y0) * (i + 1) / (5 if k == 0 else 3)
        g = box('cut', (0.40, 0.008, 0.02), (0, yy, zt + 0.004), 'black', bevel=0)
        cut(seat if k == 0 else pill, g)
# grab handle behind the pillion
torus('body.grab', 0.11, 0.012, (0, -1.22, 0.80), 'chrome', axis='x', verts=32, ring=10, rot=(0, 0, 0))

# ---- tank top: dashboard with a screen and indicator lights, the stem and bars
dash = loft('body.dash', [(0.30, 0.10, 0.74, 0.82, 2.4), (0.44, 0.13, 0.76, 0.86, 2.6), (0.52, 0.10, 0.77, 0.83, 2.4)], 'gunmetal', bevel=0.008)
box('body.screen', (0.12, 0.004, 0.05), (0, 0.375, 0.835), 'c_0c3a44_m10_g90_e70', bevel=0.001, rot=(-30, 0, 0))
for i, c in enumerate(('emit_green', 'emit_yellow', 'emit_red')):
    cyl('body.lamp', 0.007, 0.008, (-0.07 + i * 0.07, 0.47, 0.857), c, axis='z', verts=12, bevel=0.002)
cyl('body.stem', 0.022, 0.20, (0, 0.40, 0.88), 'chrome', axis='z', rot=(-20, 0, 0))
box('body.clamp', (0.09, 0.05, 0.045), (0, 0.38, 0.95), 'gunmetal', bevel=0.01)
# bars sweep back a little toward the rider
for side in (-1, 1):
    cyl('body.bar', 0.017, 0.24, (side * 0.15, 0.37, 0.955), 'chrome', axis='x', rot=(0, 0, side * -10))
    lathe('body.grip', [(0.0, 0), (0.026, 0), (0.028, 0.01)] + [(0.028 if i % 2 == 0 else 0.025, 0.02 + i * 0.008) for i in range(12)] + [(0.028, 0.12), (0, 0.12)],
          (side * 0.27, 0.355, 0.955), 'rubber', axis='x', verts=24, rot=(0, 0, side * -10) if side > 0 else (0, 180, side * -10))
    cyl('body.barend', 0.022, 0.02, (side * 0.405, 0.335, 0.955), 'chrome', axis='x', rot=(0, 0, side * -10))
    box('body.lever', (0.12, 0.012, 0.008), (side * 0.29, 0.40, 0.945), 'black', bevel=0.003, rot=(0, 0, side * -18))

# ---- windscreen: tinted glass, raked back, framed in gunmetal
def shell(name, rows, material, thick=0.008, cols=16):
    """A curved sheet: rows of (y, z, half-width, bulge) from bottom to top, bowed forward by bulge at the middle, made solid."""
    me = bpy.data.meshes.new(name); bm = bmesh.new(); grid = []
    for (y, z, hw, bulge) in rows:
        grid.append([bm.verts.new((hw * (2 * i / cols - 1), y + bulge * (1 - (2 * i / cols - 1) ** 2), z)) for i in range(cols + 1)])
    for a, b in zip(grid, grid[1:]):
        for i in range(cols): bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob); ob.data.materials.append(mat(material))
    m = ob.modifiers.new('solid', 'SOLIDIFY'); m.thickness = thick; m.offset = 0
    return ob
shell('body.windscreen', [(0.62, 0.79, 0.20, 0.04), (0.585, 0.86, 0.19, 0.045), (0.545, 0.93, 0.16, 0.045), (0.51, 0.99, 0.12, 0.04), (0.495, 1.015, 0.08, 0.03)], 'glass')
shell('body.wsframe', [(0.635, 0.765, 0.215, 0.04), (0.625, 0.795, 0.212, 0.042)], 'gunmetal', thick=0.03)

# ---- nose: headlamp (glows in game, the light itself is the game's), chin intake, a registration plate on each side
lathe('body.lampbezel', [(0, 0), (0.06, 0), (0.065, 0.015), (0.05, 0.03), (0, 0.03)], (0, 1.535, 0.47), 'chrome', axis='y', verts=32)
lathe('body.lamp', [(0, 0), (0.048, 0), (0.044, 0.012), (0, 0.016)], (0, 1.56, 0.47), 'glow_white', axis='y', verts=32)
box('body.chin', (0.16, 0.18, 0.035), (0, 1.28, 0.33), 'black', bevel=0.012)
for side in (-1, 1):
    box('body.plate', (0.006, 0.17, 0.06), (side * 0.197, 1.13, 0.42), 'offwhite', bevel=0.003)
    for i in range(5):
        box('body.glyph', (0.004, 0.012 if i != 2 else 0.03, 0.03), (side * 0.2015, 1.075 + i * 0.028, 0.42), 'black', bevel=0)

# ---- front steering vanes: swept fins hung under the nose, white with hazard tips
for side in (-1, 1):
    fin = extrude('body.vane', [(-0.30, 0.0), (0.22, 0.12), (0.30, 0.24), (0.18, 0.30), (-0.22, 0.10)], 0.035, (side * 0.24, 0.95, 0.10),
                  'white', plane='yz', bevel=0.012, rot=(0, side * -12, 0))
    extrude('body.vanetip', [(0.21, 0.135), (0.30, 0.24), (0.18, 0.30), (0.14, 0.25)], 0.037, (side * 0.24, 0.95, 0.10), 'hazard',
            plane='yz', bevel=0.006, rot=(0, side * -12, 0))

# ---- engine nacelles on pylons: intake ring with a spinner, ribbed body, exhaust nozzle glowing inside
for side in (-1, 1):
    x = side * 0.42
    lathe('body.nacelle', [(0.0, -0.60), (0.08, -0.60), (0.11, -0.57), (0.125, -0.50), (0.13, -0.30), (0.135, 0.20), (0.14, 0.52), (0.145, 0.60),
                           (0.135, 0.65), (0.10, 0.65), (0.10, 0.60), (0.0, 0.60)], (x, -0.35, 0.38), 'white', axis='y', verts=40)
    torus('body.intake', 0.135, 0.018, (x, 0.30, 0.38), 'hazard', axis='y', verts=40, ring=12)
    lathe('body.spinner', [(0, 0.0), (0.09, 0.0), (0.06, 0.05), (0.02, 0.08), (0, 0.085)], (x, 0.24, 0.38), 'chrome', axis='y', verts=32)
    for i in range(10):   # fan blades behind the spinner
        a = i * 36
        box('body.blade', (0.008, 0.012, 0.09), (x + 0.055 * math.sin(math.radians(a)), 0.26, 0.38 + 0.055 * math.cos(math.radians(a))), 'steel',
            bevel=0, rot=(0, a, 25))
    for i in range(4):   # cooling ribs
        torus('body.rib', 0.132, 0.006, (x, -0.10 - i * 0.07, 0.38), 'gunmetal', axis='y', verts=40, ring=8)
    lathe('body.nozzle', [(0.10, 0.0), (0.12, 0.0), (0.125, -0.06), (0.115, -0.16), (0.10, -0.16), (0.09, -0.04)], (x, -0.95, 0.38), 'gunmetal', axis='y', verts=40)
    lathe('body.exhaust', [(0, 0.0), (0.09, 0.0), (0.075, -0.08), (0, -0.12)], (x, -0.99, 0.38), 'glow_cyan', axis='y', verts=32)
    box('body.stripe', (0.006, 0.40, 0.03), (x + side * 0.137, -0.35, 0.40), 'hazard', bevel=0.002)
    # pylons to the hull, and a footrest on each
    for y in (-0.55, 0.05):
        extrude('body.pylon', [(-0.10, -0.03), (0.10, -0.03), (0.07, 0.03), (-0.07, 0.03)], 0.20, (side * 0.30, y, 0.38), 'gunmetal',
                plane='yz', bevel=0.008, rot=(0, 0, 0))
    cyl('body.peg', 0.018, 0.10, (side * 0.335, -0.18, 0.52), 'rubber', axis='x', verts=16)
    box('body.pegmount', (0.05, 0.05, 0.12), (side * 0.29, -0.18, 0.47), 'gunmetal', bevel=0.01)

for side in (-1, 1):
    x = side * 0.42
    fin = extrude('body.nfin', [(-0.20, 0.0), (0.05, 0.0), (-0.08, 0.13), (-0.20, 0.15)], 0.022, (x, -0.80, 0.50), 'white', plane='yz', bevel=0.008, rot=(0, side * 20, 0))
    extrude('body.nfintip', [(-0.155, 0.13), (-0.08, 0.13), (-0.095, 0.145), (-0.20, 0.155), (-0.20, 0.12)], 0.024, (x, -0.80, 0.50), 'hazard', plane='yz', bevel=0.004, rot=(0, side * 20, 0))
loft('body.tailcowl', [(-1.17, 0.13, 0.33, 0.57, 2.4), (-1.12, 0.15, 0.32, 0.58, 2.6)], 'gunmetal', bevel=0.008)

# ---- tail: a red tail light strip and the central thrust ring (its flame is the "flame" part)
box('body.taillight', (0.18, 0.012, 0.025), (0, -1.185, 0.55), 'emit_red', bevel=0.004)
lathe('body.thrustring', [(0.09, 0.0), (0.13, 0.0), (0.13, -0.07), (0.10, -0.07)], (0, -1.16, 0.45), 'gunmetal', axis='y', verts=32)
lathe('body.thrustcore', [(0, 0), (0.095, 0), (0.08, -0.04), (0, -0.05)], (0, -1.18, 0.45), 'glow_cyan', axis='y', verts=32)

# ---- one repulsor pad (the game puts four), built at the origin: a dish with a glowing ring and an emitter
lathe('repulsor.dish', [(0, 0.06), (0.17, 0.06), (0.19, 0.045), (0.19, 0.02), (0.17, 0.0), (0.12, 0.0), (0.11, 0.015), (0, 0.015)],
      (0, 0, 0), 'gunmetal', axis='z', verts=40)
torus('repulsor.ring', 0.145, 0.012, (0, 0, 0.005), 'glow_cyan', axis='z', verts=40, ring=10)
lathe('repulsor.emitter', [(0, -0.01), (0.05, -0.01), (0.06, 0.015), (0, 0.015)], (0, 0, 0), 'glow_cyan', axis='z', verts=24)
for i in range(6):
    a = math.radians(i * 60)
    box('repulsor.fin', (0.05, 0.012, 0.012), (0.085 * math.cos(a), 0.085 * math.sin(a), 0.008), 'steel', bevel=0.002, rot=(0, 0, i * 60))
pivot('repulsor', (0, 0, 0))

# ---- the thrust flame (stretched along the game's z by speed), built pointing back from its origin
lathe('flame.cone', [(0, 0.0), (0.10, 0.0), (0.08, -0.12), (0.04, -0.30), (0, -0.40)], (0, 0, 0), 'glow_cyan', axis='y', verts=24)
pivot('flame', (0, 0, 0))

export('speeder')
if 'render' in sys.argv:
    # (preview only) the game's copies: a repulsor under each pad probe, the flame at the thrust point
    reps = [o for o in bpy.context.scene.collection.objects if o.name.startswith('repulsor')]
    for (px, py) in ((-0.32, -0.9), (0.32, -0.9), (-0.32, 0.9), (0.32, 0.9)):
        for o in reps:
            c = o.copy(); bpy.context.scene.collection.objects.link(c); c.location = o.location + Vector((px, py, 0.08))
    for o in [o for o in bpy.context.scene.collection.objects if o.name.startswith('flame')]: o.location += Vector((0, -1.3, 0.45))
    for o in reps: bpy.data.objects.remove(o, do_unlink=True)
    preview('speeder', size=900, views=((40, 15), (160, 10), (250, -35), (90, 5)))
