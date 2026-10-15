# Factorheim rocket silo: a 9x9 stone launch pad with a flame pit, rune stones at its corners and an iron gantry at the back, and on it
# the rocket: a longship stood on end. Clinker planks and a ring of shields low down, iron and gold bands above, glowing runes, four
# plank fins, and a gold dragon prow on the nose looking forward (+Y, the piece's front).
# Parts (each its own mesh, so the game can show the rocket section by section as parts are delivered, and fly it):
#   pad              the launch pad and gantry (never moves)
#   sec0 .. sec7     the rocket, bottom (engines and fins) to top (dragon prow); all share the origin
#   flame, flamecore the exhaust, pivot at the engines' mouths (scaled and flickered in game)
# Run: blender -b --python <this file> -- render
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from wulib import *

STONE = 'c_6b665e_m02_g18'; STONE_DK = 'c_4a4640_m02_g15'; PIT = 'c_1d1b19_m05_g10'
IRON = 'c_2f3033_m45_g40'; IRON_LT = 'c_4b4d52_m55_g45'; GOLD = 'c_d9a93c_m20_g55'; WOOD = 'c_6a4527_m00_g25'; WOOD_DK = 'c_4a2f1a_m00_g22'
RUNE = 'emit_cyan'
R = 1.5        # hull radius
PAD = 0.8      # pad top

def sphere(name, r, at, material, verts=14, rings=7):
    return lathe(name, [(r * math.sin(math.pi * k / rings), -r * math.cos(math.pi * k / rings)) for k in range(rings + 1)], at, material, 'z', verts)

# A box on the hull's surface at angle a (degrees, 0 = +X, 90 = +Y): u along the surface, v up, out = distance from the axis.
def on_hull(name, a, out, z, size, material, u=0.0, tilt=0.0, bevel=0.0):
    t = math.radians(a)
    radial = (math.cos(t), math.sin(t)); tangent = (-math.sin(t), math.cos(t))
    at = (radial[0] * out + tangent[0] * u, radial[1] * out + tangent[1] * u, z)
    return box(name, size, at, material, bevel=bevel, rot=(0, tilt, a - 90 + 0))

# Elder Futhark-ish runes as strokes (u0, v0, u1, v1) in a 0..1 tall cell.
GLYPHS = [
    [(0, 0, 0, 1), (0, .55, .3, .8), (0, .8, .3, 1.0)],                       # fehu
    [(0, 0, 0, 1), (0, 1, .3, .72), (.3, .72, .3, 0)],                        # uruz
    [(0, 0, 0, 1), (0, .72, .26, .5), (0, .28, .26, .5)],                     # thurisaz
    [(0, 0, 0, 1), (0, 1, .3, .8), (0, .75, .3, .55)],                        # ansuz
    [(0, 0, 0, 1), (0, 1, .26, .8), (.26, .8, 0, .56), (0, .56, .28, 0)],     # raido
    [(.3, 1, 0, .5), (0, .5, .3, 0)],                                         # kaunan
    [(0, 0, 0, 1), (0, 1, -.26, .74), (0, 1, .26, .74)],                      # tiwaz
    [(0, 0, 0, 1), (0, 1, .26, .78), (.26, .78, 0, .52), (0, .52, .26, .26), (.26, .26, 0, 0)],   # berkanan
    [(0, 0, 0, 1), (0, .58, -.26, 1), (0, .58, .26, 1)],                      # algiz
    [(-.2, 0, -.2, 1), (.2, 0, .2, 1), (-.2, 1, .2, .55), (.2, 1, -.2, .55)], # mannaz
]

def rune(part, k, a, z, height=0.55, out=R + 0.05):
    for s, (u0, v0, u1, v1) in enumerate(GLYPHS[k % len(GLYPHS)]):
        du, dv = (u1 - u0) * height, (v1 - v0) * height
        length = math.hypot(du, dv) + 0.06
        tilt = math.degrees(math.atan2(du, dv))
        on_hull(f'{part}.rune{k}_{s}', a, out, z + (v0 + v1) / 2 * height, (0.07, 0.05, length), RUNE, u=(u0 + u1) / 2 * height - 0.04, tilt=tilt)

def ring_of_runes(part, z, count, start=0, height=0.55, out=R + 0.05):
    for i in range(count): rune(part, start + i * 3, 360 * i / count + 9, z, height, out)

def band(name, z, h=0.28, r=R + 0.06, material=GOLD):
    return cyl(name, r, h, (0, 0, z), material, axis='z', verts=48, bevel=0.03)

reset()

# ---------------- pad ----------------
slab = box('pad.slab', (9, 9, PAD), (0, 0, PAD / 2), STONE, bevel=0.06)
cut(slab, box('pitcut', (3.6, 3.6, 1.2), (0, 0, PAD), STONE, bevel=0))
box('pad.pitfloor', (3.6, 3.6, 0.12), (0, 0, 0.18), PIT, bevel=0)
for sx in (-1, 1):   # the pit's walls in dark stone, and iron grates across it (the rocket stands on them)
    box(f'pad.pitwall{sx}x', (0.12, 3.6, PAD - 0.2), (sx * 1.74, 0, PAD / 2 + 0.1), STONE_DK, bevel=0)
    box(f'pad.pitwall{sx}y', (3.6, 0.12, PAD - 0.2), (0, sx * 1.74, PAD / 2 + 0.1), STONE_DK, bevel=0)
for k in range(-2, 3):
    box(f'pad.grate{k}', (3.7, 0.1, 0.1), (0, k * 0.8, PAD - 0.06), IRON, bevel=0.01)
# iron rim round the pad, gold studs at the corners
for sx in (-1, 1):
    box(f'pad.rimx{sx}', (9.1, 0.18, 0.2), (0, sx * 4.5, PAD + 0.02), IRON, bevel=0.03)
    box(f'pad.rimy{sx}', (0.18, 9.1, 0.2), (sx * 4.5, 0, PAD + 0.02), IRON, bevel=0.03)
# steps up at the front (+Y)
for k in range(3):
    box(f'pad.step{k}', (3.0, 0.45, PAD * (k + 1) / 3), (0, 4.5 + 0.45 * (3 - k) - 0.2, PAD * (k + 1) / 6), STONE_DK, bevel=0.03)
# rune stones on the four corners
for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
    x, y = sx * 3.9, sy * 3.9
    box(f'pad.stone{i}', (0.7, 0.45, 2.0), (x, y, PAD + 1.0), STONE_DK, bevel=0.12, rot=(0, 0, 45 * sx * sy))
    box(f'pad.stonecap{i}', (0.5, 0.32, 0.2), (x, y, PAD + 2.05), STONE, bevel=0.06, rot=(0, 0, 45 * sx * sy))
    a = math.degrees(math.atan2(y, x))
    for s, (u0, v0, u1, v1) in enumerate(GLYPHS[(i * 3 + 1) % len(GLYPHS)]):
        du, dv = (u1 - u0) * 0.8, (v1 - v0) * 0.8
        t = math.radians(a); out = math.hypot(x, y) + 0.24
        ox, oy = math.cos(t) * out + -math.sin(t) * ((u0 + u1) * 0.4 - 0.06), math.sin(t) * out + math.cos(t) * ((u0 + u1) * 0.4 - 0.06)
        box(f'pad.stonerune{i}_{s}', (0.07, 0.04, math.hypot(du, dv) + 0.06), (ox, oy, PAD + 0.6 + (v0 + v1) * 0.4), RUNE, bevel=0,
            rot=(0, math.degrees(math.atan2(du, dv)), a - 90))
# the gantry: an iron lattice tower at the back with three arms reaching to the rocket
GX, GY, GW, GTOP = 0.0, -3.4, 1.4, 22.0
for sx in (-1, 1):
    for sy in (-1, 1):
        box(f'pad.leg{sx}{sy}', (0.2, 0.2, GTOP - PAD), (GX + sx * GW / 2, GY + sy * GW / 2, (GTOP + PAD) / 2), IRON, bevel=0.02)
h = PAD + 0.3
lvl = 0
while h < GTOP - 0.5:
    for sy in (-1, 1):
        box(f'pad.railx{lvl}{sy}', (GW, 0.12, 0.12), (GX, GY + sy * GW / 2, h), IRON_LT, bevel=0.01)
        box(f'pad.raily{lvl}{sy}', (0.12, GW, 0.12), (GX + sy * GW / 2, GY, h), IRON_LT, bevel=0.01)
    diag = math.degrees(math.atan2(GW, 2.4))
    for sy in (-1, 1):   # X braces on the two sides you see most
        box(f'pad.bracex{lvl}{sy}', (0.08, 0.08, math.hypot(GW, 2.4)), (GX + sy * GW / 2, GY, h + 1.2), IRON, bevel=0, rot=(diag * (1 if lvl % 2 else -1), 0, 0))
        box(f'pad.bracey{lvl}{sy}', (0.08, 0.08, math.hypot(GW, 2.4)), (GX, GY + sy * GW / 2, h + 1.2), IRON, bevel=0, rot=(0, diag * (1 if lvl % 2 else -1), 0))
    h += 2.4; lvl += 1
box('pad.gantrytop', (GW + 0.4, GW + 0.4, 0.3), (GX, GY, GTOP), IRON, bevel=0.04)
lathe('pad.gantryspire', [(0.35, 0), (0.3, 0.4), (0.08, 1.6), (0, 1.9)], (GX, GY, GTOP + 0.15), GOLD, 'z', 16)
for k, z in enumerate((6.0, 12.0, 18.0)):
    box(f'pad.arm{k}', (0.5, GY * -1 - GW / 2 - R - 0.12, 0.35), (GX, (GY + GW / 2 - R - 0.12) / 2, z), IRON_LT, bevel=0.03)
    box(f'pad.armclamp{k}', (1.2, 0.2, 0.5), (GX, -R - 0.15, z), GOLD, bevel=0.04)

# ---------------- rocket ----------------
# sec0: engines, thrust block and fins
for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
    lathe(f'sec0.bell{i}', [(0.62, 0), (0.6, 0.12), (0.45, 0.6), (0.3, 1.0), (0.32, 1.3), (0, 1.3)], (sx * 0.62, sy * 0.62, PAD + 0.05), IRON_LT, 'z', 28)
    lathe(f'sec0.bellin{i}', [(0.5, 0), (0, 0.4)], (sx * 0.62, sy * 0.62, PAD + 0.12), PIT, 'z', 28)
cyl('sec0.block', R, 0.9, (0, 0, PAD + 1.75), IRON, axis='z', verts=48, bevel=0.05)
band('sec0.band', PAD + 2.2)
for i, a in enumerate((45, 135, 225, 315)):
    fin = [(1.2, PAD + 1.3), (2.6, PAD + 0.2), (3.0, PAD + 0.35), (2.9, PAD + 1.6), (1.4, PAD + 5.2), (1.2, PAD + 5.2)]
    extrude(f'sec0.fin{i}', fin, 0.16, (0, 0, 0), WOOD, plane='yz', bevel=0.03, rot=(0, 0, a - 90))
    extrude(f'sec0.finedge{i}', [(2.6, PAD + 0.2), (3.0, PAD + 0.35), (2.9, PAD + 1.6), (2.75, PAD + 1.9), (2.75, PAD + 0.42)], 0.2, (0, 0, 0), GOLD, plane='yz', bevel=0.02, rot=(0, 0, a - 90))

# sec1, sec2: clinker planks (a longship's hull) with a ring of shields round sec2
def clinker(part, z0, z1):
    z = z0; k = 0
    while z < z1 - 0.01:
        h = min(0.42, z1 - z)
        cyl(f'{part}.strake{k}', R + 0.04, h, (0, 0, z + h / 2), WOOD if k % 2 == 0 else WOOD_DK, axis='z', verts=48, bevel=0.0, radius2=R)
        z += h; k += 1
clinker('sec1', PAD + 2.35, PAD + 5.2)
for i in range(16):   # iron rivet lines between strakes
    on_hull(f'sec1.rib{i}', 360 * i / 16, R + 0.03, PAD + 3.8, (0.08, 0.06, 2.8), IRON, bevel=0.01)
clinker('sec2', PAD + 5.2, PAD + 8.0)
band('sec2.band', PAD + 8.0, 0.3)
SHIELDS = ('c_a8261c_m05_g30', 'c_d8b23a_m05_g30', 'c_24447e_m05_g30', 'c_e4ddd0_m05_g30')
for i in range(12):
    a = 360 * i / 12 + 15; t = math.radians(a)
    at = (math.cos(t) * (R + 0.12), math.sin(t) * (R + 0.12), PAD + 6.6)
    cyl(f'sec2.shield{i}', 0.52, 0.08, at, SHIELDS[i % 4], axis='y', verts=28, bevel=0.02, rot=(0, 0, a - 90))
    at2 = (math.cos(t) * (R + 0.18), math.sin(t) * (R + 0.18), PAD + 6.6)
    sphere(f'sec2.boss{i}', 0.13, at2, IRON_LT)
    torus(f'sec2.shieldrim{i}', 0.5, 0.035, at, IRON, axis='y', verts=28, ring=6, rot=(0, 0, a - 90))

# sec3 .. sec5: iron hull, gold bands, runes
def iron(part, z0, z1, runes_at=None, start=0):
    cyl(f'{part}.hull', R, z1 - z0, (0, 0, (z0 + z1) / 2), IRON, axis='z', verts=48, bevel=0.0)
    band(f'{part}.band', z1 - 0.15, 0.3)
    for i in range(8):
        on_hull(f'{part}.seam{i}', 360 * i / 8 + 22.5, R + 0.015, (z0 + z1) / 2, (0.05, 0.04, z1 - z0 - 0.4), IRON_LT)
    if runes_at is not None: ring_of_runes(part, runes_at, 8, start)
iron('sec3', PAD + 8.15, PAD + 11.0, PAD + 9.2, 0)
iron('sec4', PAD + 11.0, PAD + 13.8, PAD + 12.0, 1)
for i in range(4):   # sec4: round portholes, glowing gold inside
    t = math.radians(90 * i + 45)
    at = (math.cos(t) * (R + 0.02), math.sin(t) * (R + 0.02), PAD + 12.9)
    torus(f'sec4.port{i}', 0.24, 0.05, at, GOLD, axis='y', verts=24, ring=6, rot=(0, 0, 90 * i + 45 - 90))
    cyl(f'sec4.portglass{i}', 0.22, 0.04, at, 'emit_orange', axis='y', verts=24, bevel=0, rot=(0, 0, 90 * i + 45 - 90))
iron('sec5', PAD + 13.8, PAD + 16.6, PAD + 14.8, 2)

# sec6: the nose, an ogive with gold rings
nose = [(R, 0), (R * 0.98, 0.6), (R * 0.9, 1.4), (R * 0.75, 2.4), (R * 0.55, 3.3), (R * 0.32, 4.0), (0.24, 4.3), (0, 4.31)]
lathe('sec6.nose', nose, (0, 0, PAD + 16.6), IRON, 'z', 48)
for k, (r, z) in enumerate(((R * 0.93, 1.2), (R * 0.67, 2.8))):
    torus(f'sec6.ring{k}', r + 0.03, 0.07, (0, 0, PAD + 16.6 + z), GOLD, axis='z', verts=48, ring=8)
lathe('sec6.collar', [(0.42, 0), (0.42, 0.3), (0.3, 0.42), (0, 0.42)], (0, 0, PAD + 20.75), GOLD, 'z', 24)

# sec7: the dragon prow, looking forward (+Y), gold with red eyes
TOP = PAD + 21.05
head = [(-0.3, 0.0), (0.3, 0.0), (0.36, 0.55), (0.55, 1.0), (0.9, 1.25), (1.35, 1.3), (1.8, 1.24), (2.15, 1.36), (1.7, 1.52),
        (2.2, 1.66), (2.05, 1.92), (1.5, 2.06), (1.1, 2.22), (0.95, 2.6), (0.7, 2.18), (0.45, 2.05), (0.15, 1.65), (-0.2, 1.05), (-0.36, 0.5)]
extrude('sec7.head', head, 0.5, (0, 0, TOP), GOLD, plane='yz', bevel=0.05, segments=2)
for sx in (-1, 1):
    sphere(f'sec7.eye{sx}', 0.09, (sx * 0.24, 1.5, TOP + 1.82), 'emit_red')
    extrude(f'sec7.mane{sx}', [(-0.25, 0.4), (-0.6, 0.7), (-0.3, 0.9), (-0.7, 1.25), (-0.25, 1.4), (-0.5, 1.8), (-0.1, 1.6)], 0.12,
            (sx * 0.12, 0, TOP), GOLD, plane='yz', bevel=0.02)
for k in range(3):
    box(f'sec7.tooth{k}', (0.3, 0.08, 0.14), (0, 1.75 + k * 0.14, TOP + 1.42), 'c_eae4d2_m00_g40', bevel=0.01)

# ---------------- exhaust ----------------
pivot('flame', (0, 0, PAD + 0.05)); pivot('flamecore', (0, 0, PAD + 0.05))
lathe('flame.plume', [(1.25, 0), (1.45, -0.8), (1.25, -3.0), (0.8, -5.5), (0, -8.0)], (0, 0, PAD + 0.05), 'glow_orange', 'z', 24)
lathe('flamecore.core', [(0.8, 0), (0.85, -0.6), (0.55, -2.5), (0, -4.2)], (0, 0, PAD + 0.05), 'glow_yellow', 'z', 20)

finish('rocket')
