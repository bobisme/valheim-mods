# Járngreipr (Godforged), Thor's iron gauntlets. Two parts:
#   cuff: one gauntlet as worn, in a forearm bone's frame: the bone runs along Blender Z (the game's y) from the elbow (0) to the wrist
#         (0.26 m). Jarngreipr.cs hangs one on each forearm.
#   pair: the two lying side by side, for the ground and the icon.
# Fitted to the player's forearm measured in the game (gf cuff measure, 0.2.1): its radius is 0.065-0.082 m from 0 to 0.10 m out from
# the elbow and 0.042-0.064 m near the wrist, so the cuff runs 0.03-0.215 m, flared over the thick of the arm by the elbow and narrowing
# to the wrist, as a gauntlet's cuff does.
# Run: blender -b --python $PWD/mods/Godforged/Models/jarngreipr.py -- render
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from wulib import _obj, _bevel
from gadlib import *
from mathutils import Matrix

reset()
IRON = 'c_3c3e43_m70_g30_f'    # blackened forged iron, with a grain made in the game (_f): mottled, pitted, scratched
BRONZE = 'c_7e6230_m65_g28'      # old, dull bronze
CUT = 'c_121315_m40_g10'        # runes cut into the iron
LEATHER = 'c_38220f_m00_g12_l'
# 0.2.14: moved down onto the wrist and fitted tighter, its wrist end flaring a little over the glove (Glove.cs: leather from the
# wearer's own hand), so cuff and glove are one gauntlet.
T0, T1 = 0.085, 0.27                    # elbow end, wrist end (the hand's bone starts at 0.26)
PROFILE = [(0.081, T0), (0.078, 0.10), (0.074, 0.14), (0.071, 0.18), (0.069, 0.22), (0.066, 0.245), (0.063, 0.26), (0.061, T1)]

def R(t):
    """The cuff's outer radius at t metres from the elbow."""
    for (r0, t0), (r1, t1) in zip(PROFILE, PROFILE[1:]):
        if t0 <= t <= t1: return r0 + (r1 - r0) * (t - t0) / (t1 - t0)
    return PROFILE[0][0] if t < T0 else PROFILE[-1][0]

def shell(name, outer, thick, material, verts=28):
    """An open turned tube (the arm goes through it), its wall 'thick' thick: outer is [(radius, z), ...] from end to end."""
    prof = outer + [(r - thick, z) for r, z in reversed(outer)]
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    rings = [[bm.verts.new((r * math.cos(2 * math.pi * i / verts), r * math.sin(2 * math.pi * i / verts), z)) for i in range(verts)] for r, z in prof]
    for a, b in zip(rings, rings[1:] + rings[:1]):
        for i in range(verts):
            j = (i + 1) % verts; bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return _obj(name, me, material)

# 0.2.36: a splinted vambrace, as the Viking age made them (Birka, Valsgärde): long narrow iron splints riveted to a leather sleeve,
# buckled on with leather straps; runes cut into the iron, not glowing; old bronze. (The smooth tube with gold bands and cyan lights
# looked like something from another world's armoury, not this one's.)
SPLINTS, SPAN, BACK_DEG = 11, 300, -90       # how many, over how much of the arm (degrees), centred on its back (the game's -z)

def splint(name, centre_deg, width, t0, t1):
    """One iron splint: a strip curved round the arm at the sleeve's radius from t0 to t1, its ends rounded, a little thick."""
    me = bpy.data.meshes.new(name); bm = bmesh.new(); rows, cols = 14, 4
    half = math.degrees(width / R((t0 + t1) / 2)) / 2
    grid = []
    for k in range(rows + 1):
        t = t0 + (t1 - t0) * k / rows
        end = min(k, rows - k) / 2.0                                     # (the two ends rounded off)
        shrink = 1.0 if end >= 1 else math.sqrt(max(0.0, 1 - (1 - end) ** 2))
        row = []
        for c in range(cols + 1):
            a = math.radians(centre_deg + (-half + 2 * half * c / cols) * max(shrink, 0.25))
            r = R(t) + 0.0045
            row.append(bm.verts.new((r * math.cos(a), r * math.sin(a), t)))
        grid.append(row)
    for p, q in zip(grid, grid[1:]):
        for c in range(cols): bm.faces.new((p[c], p[c + 1], q[c + 1], q[c]))
    bm.to_mesh(me); bm.free()
    ob = _obj(name, me, IRON)
    m = ob.modifiers.new('solid', 'SOLIDIFY'); m.thickness = 0.003; m.offset = 0.0
    return ob

def cuff(prefix):
    made = [shell(prefix + '.sleeve', PROFILE, 0.004, LEATHER)]               # the leather sleeve the splints are riveted to
    made.append(shell(prefix + '.lining', [(R(T0) + 0.001, T0 - 0.006), (R(T0) + 0.002, T0 + 0.006)], 0.005, LEATHER))   # a rolled edge
    # the wrist end closed with a leather skirt from the rim in to 0.04 m (both faces), so nothing shows inside it when the wrist bends
    me = bpy.data.meshes.new(prefix + '.skirt'); bm = bmesh.new(); V = 28
    def ring(r, z): return [bm.verts.new((r * math.cos(2 * math.pi * i / V), r * math.sin(2 * math.pi * i / V), z)) for i in range(V)]
    outer, inner = ring(R(T1) - 0.002, T1 - 0.002), ring(0.04, T1 + 0.004)
    outer2, inner2 = ring(R(T1) - 0.002, T1 - 0.0025), ring(0.04, T1 + 0.0035)
    for i in range(V):
        j = (i + 1) % V
        bm.faces.new((outer[i], outer[j], inner[j], inner[i])); bm.faces.new((inner2[i], inner2[j], outer2[j], outer2[i]))
    bm.to_mesh(me); bm.free()
    made.append(_obj(prefix + '.skirt', me, LEATHER))
    # the splints, each riveted top and bottom, and the middle three with Thor's rune (thurisaz) cut in
    step = SPAN / SPLINTS
    for i in range(SPLINTS):
        deg = BACK_DEG - SPAN / 2 + step * (i + 0.5)
        t0, t1 = T0 + 0.012 + (0.006 if i % 2 else 0), T1 - 0.016 - (0.006 if i % 2 else 0)   # (staggered ends: forged by hand)
        made.append(splint(prefix + '.splint', deg, 0.024, t0, t1))
        a = math.radians(deg)
        for t in (t0 + 0.012, t1 - 0.012):
            r = R(t) + 0.0062
            dome = lathe(prefix + '.rivet', [(0.0, 0.0), (0.0034, 0.0), (0.003, 0.0018), (0.0, 0.0028)], (0, 0, 0), BRONZE, axis='z', verts=8)
            dome.location = (r * math.cos(a), r * math.sin(a), t); dome.rotation_euler = (0, math.pi / 2, a)
            made.append(dome)
        if abs(i - SPLINTS // 2) <= 1:
            tm, r = (t0 + t1) / 2, R((t0 + t1) / 2) + 0.0062
            tang = (-math.sin(a), math.cos(a))
            def stroke(u0, z0, u1, z1):   # a cut on the splint's face: u across it (m), z along the arm
                mx, mz = (u0 + u1) / 2, (z0 + z1) / 2; ln = math.hypot(u1 - u0, z1 - z0)
                ang = math.degrees(math.atan2(u1 - u0, z1 - z0))
                o = box(prefix + '.rune', (0.0016, 0.0012, ln), (r * math.cos(a) + tang[0] * mx, r * math.sin(a) + tang[1] * mx, tm + mz), CUT,
                        bevel=0, rot=(0, 0, 0))
                o.rotation_euler = (0, 0, a)
                o.rotation_mode = 'XYZ'
                bpy.context.view_layer.update()
                o.matrix_world = o.matrix_world @ Matrix.Rotation(math.radians(-ang), 4, 'X')
                return o
            # thurisaz: a staff and a thorn
            made += [stroke(-0.004, -0.02, -0.004, 0.02), stroke(-0.004, 0.008, 0.005, 0.0), stroke(0.005, 0.0, -0.004, -0.008)]
    # two leather straps round it, each with a bronze buckle on the inner side of the arm
    for t in (T0 + 0.045, T1 - 0.05):
        made.append(shell(prefix + '.strap', [(R(t) + 0.009, t - 0.008), (R(t) + 0.009, t + 0.008)], 0.003, LEATHER))
        a = math.radians(BACK_DEG + 180)
        made.append(box(prefix + '.buckle', (0.004, 0.014, 0.022), ((R(t) + 0.0105) * math.cos(a), (R(t) + 0.0105) * math.sin(a), t), BRONZE,
                        bevel=0.0012, rot=(0, 0, math.degrees(a))))
    return made

worn = cuff('cuff')

# The fingers' plates (0.2.10), fitted to the finger bones as measured (gf cuff hand): every finger joint runs along its bone's +y
# (Blender Z) and curls towards +z (Blender +Y: the palm), so each plate wraps the back of its joint (Blender -Y), the palm side left
# open for the grip; the thumb curls towards +x on the right hand, so its plates wrap -x (Jarngreipr.cs turns the left's about the bone).
# Each sits on its own bone, so the fingers bend in them. The back of the hand is the game's own (the Protector's plate, borrowed).
def lame(name, r, length, centre_deg, tip=False, span=230, rows=8, cols=14):
    """A curved iron plate round the back of one finger joint: radius r round the bone, from just behind the joint to its end (a
    tip closes over the fingertip), centred on centre_deg round the bone (Blender: -90 is -Y, 180 is -X), 'span' degrees wide."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    zs = [-0.004 + (length + 0.004) * k / rows for k in range(rows + 1)]
    rr = [r * (1.0 + 0.06 * (k / rows)) for k in range(rows + 1)]          # flaring a little towards its end, over the next joint's
    if tip:   # round over the fingertip
        for k in range(1, 5):
            a = math.radians(22.5 * k)
            zs.append(length + r * 0.9 * math.sin(a)); rr.append(r * math.cos(a) + 0.002)
    grid = []
    for z, rad in zip(zs, rr):
        row = []
        for c in range(cols + 1):
            a = math.radians(centre_deg - span / 2 + span * c / cols)
            row.append(bm.verts.new((rad * math.cos(a), rad * math.sin(a), z)))
        grid.append(row)
    for a, b in zip(grid, grid[1:]):
        for c in range(cols):
            bm.faces.new((a[c], a[c + 1], b[c + 1], b[c]))
    bm.to_mesh(me); bm.free()
    ob = _obj(name + '.plate', me, IRON)
    m = ob.modifiers.new('solid', 'SOLIDIFY'); m.thickness = 0.0028; m.offset = 1.0
    _bevel(ob, 0.0008, 1)
    # a bronze rivet on the back at the joint, and a ridge down the middle
    ca = math.radians(centre_deg)
    cyl(name + '.rivet', 0.0028, 0.004, (math.cos(ca) * (r + 0.003), math.sin(ca) * (r + 0.003), 0.004), BRONZE, axis='z', verts=8, bevel=0.0008,
        rot=(0, 0, 0)).rotation_euler = (math.radians(90), 0, ca + math.radians(90))
    box(name + '.ridge', (0.003, 0.003, length * 0.8), (math.cos(ca) * (r + 0.002), math.sin(ca) * (r + 0.002), length * 0.5), IRON,
        bevel=0.001, rot=(0, 0, math.degrees(ca)))

BACK = -90      # the back of a finger: -Y (the game's -z)
THUMB = 180     # the back of the right thumb: -X
lame('seg1', 0.024, 0.05, BACK); lame('seg2', 0.02, 0.031, BACK); lame('tip', 0.017, 0.028, BACK, tip=True)
lame('thumb1', 0.027, 0.05, THUMB); lame('thumb2', 0.022, 0.04, THUMB); lame('thumbtip', 0.019, 0.03, THUMB, tip=True)
lame('mid1', 0.023, 0.05, BACK, span=170)   # (0.2.37: the middle finger's first joint, whose skin is too coarse to make a plate from)

# 0.2.37: the back of the hand, in the hand bone's frame (along Blender Z from the wrist, 0, to the knuckles, 0.11; the back is -Y): a
# riveted iron plate arched over the skin (0.04 m out), from under the cuff to a ridge over the knuckles. Made, not taken from the
# skin, which is too coarse there (14 points for the whole back of the hand).
def back(z, u=0.0):
    """How far the plate sits from the hand bone (Blender -Y) at z along the hand and u across it (-1..1): the back of the hand as
    measured in the game (gf cuff backhand, 0.2.38: 0.043 m at the wrist, 0.037 mid-hand, 0.034 at the knuckles), over the glove's
    leather (3 mm), a little less at the sides as the hand rounds off."""
    base = 0.043 - 0.009 * max(0.0, min(1.0, z / 0.12))
    return base + 0.005 - 0.009 * u * u

def hand_plate(name):
    me = bpy.data.meshes.new(name); bm = bmesh.new(); rows, cols = 12, 12
    grid = []
    for k in range(rows + 1):
        z = -0.005 + 0.11 * k / rows
        w = 0.043 + 0.01 * min(1.0, max(0.0, z) / 0.06)                 # narrow at the wrist, the knuckles' width at the end
        row = []
        for c in range(cols + 1):
            u = -1 + 2 * c / cols
            row.append(bm.verts.new((u * w, -back(z, u), z)))
        grid.append(row)
    for p, q in zip(grid, grid[1:]):
        for c in range(cols): bm.faces.new((p[c], p[c + 1], q[c + 1], q[c]))
    bm.to_mesh(me); bm.free()
    ob = _obj(name + '.plate', me, IRON)
    s = ob.modifiers.new('solid', 'SOLIDIFY'); s.thickness = 0.003; s.offset = 0.0
    _bevel(ob, 0.001, 1)
    box(name + '.ridge', (0.1, 0.006, 0.01), (0, -(back(0.1) + 0.003), 0.1), IRON, bevel=0.0025)      # over the knuckles
    for x, z in ((-0.032, 0.012), (0.032, 0.012), (-0.04, 0.078), (0.04, 0.078)):
        d = lathe(name + '.rivet', [(0.0, 0.0), (0.0034, 0.0), (0.003, 0.0018), (0.0, 0.0028)], (0, 0, 0), BRONZE, axis='z', verts=8)
        u = x / 0.05
        d.location = (x, -(back(z, u) + 0.0015), z); d.rotation_euler = (math.pi / 2, 0, 0)
    for z0, z1, u0, u1 in ((0.03, 0.07, 0.0, 0.0), (0.06, 0.05, 0.0, 0.012), (0.05, 0.04, 0.012, 0.0)):   # Thor's rune, cut in
        ln = math.hypot(u1 - u0, z1 - z0); zm = (z0 + z1) / 2
        o = box(name + '.rune', (0.0018, 0.0012, ln), ((u0 + u1) / 2, -(back(zm) + 0.0016), zm), CUT, bevel=0)
        o.rotation_euler = (0, math.atan2(u1 - u0, z1 - z0), 0)
hand_plate('backhand')

# the pair: copies laid flat (along Y) side by side, centred on the origin
bpy.context.view_layer.update()
for side in (-1, 1):
    M = Matrix.Translation((side * 0.1, 0, 0)) @ Matrix.Rotation(math.radians(90), 4, 'X') @ Matrix.Translation((0, 0, -(T0 + T1) / 2))
    for o in worn:
        c = o.copy(); c.data = o.data.copy(); bpy.context.scene.collection.objects.link(c)
        bpy.context.view_layer.update()
        c.matrix_world = M @ o.matrix_world
        c.name = ('pair' if not o.name.startswith('glow') else 'pairglow') + o.name[o.name.index('.'):] + ('l' if side < 0 else 'r')

report(12)
finish('jarngreipr')
