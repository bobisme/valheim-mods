# Laser Rifle (Wrong Universe). Blender frame: +Y forward (the barrel), +Z up, the grip hand at the origin.
# The game frame (Lasers.cs RifleModel): barrel along +z at height y 0.04, muzzle at z 0.775; stock back to z -0.46; scope at y 0.12.
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from wulib import *
reset()
AX = 0.04
W = 0.058

# --- receiver: a long white shell, sloped at the front, a raised spine on top
prof = [(-0.105, -0.012), (0.235, -0.012), (0.262, 0.004), (0.272, 0.030), (0.272, 0.056), (0.250, 0.072), (0.120, 0.080), (-0.060, 0.080),
        (-0.105, 0.064)]
rec = extrude('body', prof, W, material='white', plane='yz', bevel=0.006, segments=2)
for x in (W / 2, -W / 2):
    for y in (-0.035, 0.07, 0.19):
        cut(rec, box('c', (0.003, 0.0025, 0.1), (x, y, 0.03), bevel=0))
    cut(rec, box('c', (0.003, 0.36, 0.0025), (x, 0.08, 0.008), bevel=0))
    # the charge window: blue glow set back in a slot
    cut(rec, box('c', (0.005, 0.10, 0.012), (x, 0.13, 0.045), bevel=0.002))
    box('body.charge', (0.004, 0.096, 0.008), (x * 0.95, 0.13, 0.045), 'glow_blue', bevel=0)
    # hazard stripe at the front of the receiver
    box('body.stripe', (0.002, 0.012, 0.05), (x * 1.03, 0.222, 0.03), 'hazard', bevel=0.0006, segments=1)
for i in range(6):
    cut(rec, box('c', (0.04, 0.006, 0.008), (0, 0.17 + i * 0.012, 0.081), bevel=0.0015))
box('body.led', (0.006, 0.006, 0.003), (0.016, -0.08, 0.081), 'emit_cyan', bevel=0.001)
box('body.led2', (0.006, 0.006, 0.003), (0.006, -0.08, 0.081), 'emit_green', bevel=0.001)

# --- stock: a skeleton with a thumb hole, white, rubber butt pad, an orange stripe
sp = [(-0.105, 0.064), (-0.105, -0.012), (-0.150, -0.030), (-0.400, -0.060), (-0.440, -0.080), (-0.440, 0.050), (-0.400, 0.062),
      (-0.200, 0.066)]
stock = extrude('body.stock', sp, 0.046, material='white', plane='yz', bevel=0.006, segments=2)
cut(stock, extrude('c', [(-0.175, 0.000), (-0.360, -0.030), (-0.385, -0.030), (-0.395, 0.030), (-0.380, 0.040), (-0.190, 0.040)], 0.08,
                   plane='yz', bevel=0.0))
extrude('body.pad', [(-0.438, -0.086), (-0.462, -0.090), (-0.464, 0.058), (-0.438, 0.056)], 0.05, material='rubber', plane='yz', bevel=0.004)
for x in (0.0235, -0.0235):
    box('body.stockstripe', (0.002, 0.014, 0.10), (x, -0.415, -0.005), 'hazard', bevel=0.0006, segments=1, rot=(-8, 0, 0))
    box('body.cheek', (0.003, 0.12, 0.012), (x, -0.30, 0.052), 'gunmetal', bevel=0.001, segments=1, rot=(-2, 0, 0))

# --- grip, trigger guard, trigger
gp = [(-0.004, -0.010), (-0.010, -0.028), (-0.006, -0.038), (-0.014, -0.050), (-0.010, -0.062), (-0.019, -0.074), (-0.016, -0.086),
      (-0.026, -0.104), (-0.032, -0.124), (-0.076, -0.124), (-0.070, -0.070), (-0.058, -0.012)]
extrude('body.grip', gp, 0.036, material='rubber', plane='yz', bevel=0.004)
extrude('body.gripbase', [(-0.031, -0.122), (-0.078, -0.122), (-0.080, -0.132), (-0.030, -0.132)], 0.04, material='gunmetal', plane='yz', bevel=0.003)
extrude('body.guard', [(-0.006, -0.010), (0.050, -0.010), (0.056, -0.016), (0.054, -0.046), (0.044, -0.052), (-0.016, -0.052),
                       (-0.014, -0.046), (0.040, -0.046), (0.048, -0.042), (0.048, -0.018), (0.044, -0.015), (-0.004, -0.015)],
        0.012, material='black', plane='yz', bevel=0.0015)
extrude('body.trigger', [(0.012, -0.011), (0.020, -0.011), (0.018, -0.024), (0.012, -0.036), (0.008, -0.036), (0.012, -0.024)],
        0.008, material='chrome', plane='yz', bevel=0.001)

# --- power cell: a magazine ahead of the grip, gunmetal, a blue gauge window, orange base
cell = extrude('body.cell', [(0.120, -0.010), (0.200, -0.010), (0.192, -0.090), (0.130, -0.090)], 0.040, material='gunmetal', plane='yz', bevel=0.004)
for x in (0.0205, -0.0205):
    box('body.gauge', (0.003, 0.012, 0.055), (x, 0.162, -0.048), 'emit_blue', bevel=0.0008, segments=1)
extrude('body.cellbase', [(0.128, -0.088), (0.194, -0.088), (0.196, -0.100), (0.126, -0.100)], 0.044, material='hazard', plane='yz', bevel=0.003)

# --- barrel: heat-sink fins out of the receiver, then glowing coil rings, a shroud and the emitter
fins = []
y = 0.27
for i in range(8):
    fins += [(0.020, y), (0.034, y + 0.002), (0.034, y + 0.008), (0.020, y + 0.010)]
    y += 0.016
lathe('body.sink', [(0.0, 0.26), (0.020, 0.26)] + fins + [(0.020, y), (0.0, y)], (0, 0, AX), 'gunmetal', axis='y', verts=24)
lathe('body.collar', [(0.0, 0.0), (0.026, 0.0), (0.028, 0.002), (0.028, 0.010), (0.026, 0.012), (0.0, 0.012)], (0, y, AX), 'hazard', axis='y', verts=24)
lathe('body.barrel', [(0.0, 0.0), (0.016, 0.0), (0.016, 0.72 - y - 0.01), (0.0, 0.72 - y - 0.01)], (0, y + 0.01, AX), 'steel', axis='y', verts=20)
for i in range(6):
    torus('body.coil', 0.024, 0.0045, (0, y + 0.04 + i * 0.038, AX), 'glow_blue', axis='y', verts=24, ring=6)
    lathe('body.coilseat', [(0.0, 0.0), (0.019, 0.0), (0.020, 0.002), (0.020, 0.012), (0.019, 0.014), (0.0, 0.014)],
          (0, y + 0.033 + i * 0.038, AX), 'gunmetal', axis='y', verts=20)
lathe('body.shroud', [(0.0, 0.0), (0.022, 0.0), (0.028, 0.006), (0.028, 0.050), (0.024, 0.058), (0.017, 0.058), (0.016, 0.052), (0.0, 0.052)],
      (0, 0.712, AX), 'gunmetal', axis='y', verts=24)
for i in range(4):
    a = i * 90 + 45
    import math as _m
    box('body.port', (0.006, 0.026, 0.006), (0.027 * _m.cos(_m.radians(a)), 0.738, AX + 0.027 * _m.sin(_m.radians(a))), 'black', bevel=0.0015, rot=(0, -a, 0))
cyl('body.lens', 0.015, 0.002, (0, 0.768, AX), 'glow_cyan', axis='y', verts=20, bevel=0)

# --- scope: a black tube with flared ends, gunmetal rings and knobs, a lens that catches the light
lathe('body.scope', [(0.0, -0.035), (0.022, -0.035), (0.024, -0.028), (0.018, -0.010), (0.018, 0.140), (0.025, 0.170), (0.025, 0.196),
                     (0.021, 0.198), (0.0, 0.198)], (0, 0, 0.122), 'black', axis='y', verts=24)
cyl('body.scopelens', 0.021, 0.002, (0, 0.1985, 0.122), 'lens', axis='y', verts=20, bevel=0)
cyl('body.eyecup', 0.019, 0.004, (0, -0.0365, 0.122), 'glass', axis='y', verts=20, bevel=0)
for yy in (0.02, 0.11):
    lathe('body.ring', [(0.0, 0.0), (0.021, 0.0), (0.022, 0.002), (0.022, 0.012), (0.021, 0.014), (0.0, 0.014)], (0, yy, 0.122), 'gunmetal', axis='y', verts=24)
    box('body.mount', (0.016, 0.014, 0.03), (0, yy + 0.007, 0.093), 'gunmetal', bevel=0.002)
cyl('body.knob', 0.008, 0.012, (0, 0.065, 0.146), 'gunmetal', axis='z', verts=16, bevel=0.0015)
cyl('body.knob2', 0.008, 0.012, (0.026, 0.065, 0.122), 'gunmetal', axis='x', verts=16, bevel=0.0015)

finish('rifle')
