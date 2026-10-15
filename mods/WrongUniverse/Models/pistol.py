# Laser Pistol (Wrong Universe). Blender frame: +Y forward (the barrel), +Z up, the grip hand at the origin.
# The game frame (Lasers.cs PistolModel): barrel along +z at height y 0.03, muzzle at z 0.245; the body spans z -0.05..0.15.
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from wulib import *
reset()
AX = 0.03          # barrel axis height
W = 0.046          # body width

# --- body: a side profile, sloped nose and rounded back, white shell
prof = [(-0.058, 0.004), (-0.050, -0.004), (0.118, -0.004), (0.138, 0.006), (0.150, 0.020), (0.150, 0.044), (0.136, 0.056),
        (-0.030, 0.060), (-0.050, 0.055), (-0.060, 0.040)]
body = extrude('body', prof, W, material='white', plane='yz', bevel=0.005, segments=2)
# panel seams cut into both sides: a vertical one behind the barrel housing, one ahead of the grip, and a long horizontal one
for x in (W / 2, -W / 2):
    cut(body, box('c', (0.003, 0.0025, 0.08), (x, 0.085, 0.03), bevel=0))
    cut(body, box('c', (0.003, 0.0025, 0.08), (x, 0.005, 0.03), bevel=0))
    cut(body, box('c', (0.003, 0.17, 0.0025), (x, 0.045, 0.012), bevel=0))
    # the charge window: a slot in the side, a glowing strip set back inside it
    cut(body, box('c', (0.005, 0.07, 0.009), (x, 0.045, 0.034), bevel=0.0015))
    box('body.charge', (0.004, 0.066, 0.006), (x * 0.94, 0.045, 0.034), 'glow_red', bevel=0)
# vents on the top rear
for i in range(4):
    cut(body, box('c', (0.03, 0.004, 0.006), (0, -0.032 + i * 0.009, 0.061), bevel=0.001))

# --- top rail with teeth, sights, a heat sink behind
box('body.rail', (0.018, 0.13, 0.008), (0, 0.055, 0.064), 'gunmetal', bevel=0.002)
for i in range(9):
    box('body.tooth', (0.022, 0.006, 0.004), (0, 0.0 + i * 0.0135, 0.0695), 'gunmetal', bevel=0.0008, segments=1)
box('body.sight_front', (0.006, 0.01, 0.014), (0, 0.128, 0.072), 'black', bevel=0.0015)
box('body.sight_rear_l', (0.005, 0.008, 0.012), (0.007, -0.012, 0.071), 'black', bevel=0.0015)
box('body.sight_rear_r', (0.005, 0.008, 0.012), (-0.007, -0.012, 0.071), 'black', bevel=0.0015)
for i in range(5):
    box('body.fin', (0.05, 0.003, 0.016), (0, -0.05 + i * 0.006, 0.05), 'gunmetal', bevel=0.0008, segments=1)
box('body.led', (0.006, 0.006, 0.003), (0.012, -0.03, 0.0605), 'emit_red', bevel=0.001)

# --- barrel: ribbed cooling jacket, red emitter coils between the ribs, a chrome shroud and a glowing lens
rib = []
y = 0.160
for i in range(5):
    rib += [(0.017, y), (0.023, y + 0.002), (0.023, y + 0.007), (0.017, y + 0.009)]
    y += 0.011
jacket = lathe('body.jacket', [(0.0, 0.148), (0.017, 0.148)] + rib + [(0.017, y), (0.0, y)], material='gunmetal', axis='y', verts=24)
jacket.location = (0, 0, AX)
for i in range(4):
    torus('body.coil', 0.0185, 0.0022, (0, 0.1695 + i * 0.011, AX), 'glow_red', axis='y', verts=20, ring=5)
lathe('body.shroud', [(0.0, 0.0), (0.020, 0.0), (0.024, 0.004), (0.024, 0.026), (0.021, 0.030), (0.015, 0.030), (0.014, 0.026), (0.0, 0.026)],
      (0, 0.214, AX), 'chrome', axis='y', verts=24)
cyl('body.lens', 0.0135, 0.002, (0, 0.2405, AX), 'glow_red', axis='y', verts=24, bevel=0)
# a ring of hazard orange where the barrel meets the body
lathe('body.collar', [(0.0, 0.0), (0.020, 0.0), (0.022, 0.002), (0.022, 0.008), (0.020, 0.010), (0.0, 0.010)], (0, 0.149, AX), 'hazard', axis='y', verts=24)

# --- power cell under the nose: a gunmetal can with a glowing window and an orange cap
cyl('body.cell', 0.012, 0.075, (0, 0.085, -0.014), 'gunmetal', axis='y', verts=24, bevel=0.002)
cyl('body.cellcap', 0.0135, 0.01, (0, 0.126, -0.014), 'hazard', axis='y', verts=24, bevel=0.002)
box('body.cellwin', (0.004, 0.04, 0.006), (0.0115, 0.085, -0.014), 'emit_red', bevel=0.001)
box('body.cellwin2', (0.004, 0.04, 0.006), (-0.0115, 0.085, -0.014), 'emit_red', bevel=0.001)

# --- grip: raked back, finger grooves on the front, rubber with a ribbed texture, a pommel plate
gp = [(-0.006, 0.0), (-0.010, -0.018), (-0.006, -0.028), (-0.014, -0.040), (-0.010, -0.052), (-0.019, -0.064), (-0.015, -0.076),
      (-0.024, -0.090), (-0.030, -0.112), (-0.072, -0.112), (-0.066, -0.060), (-0.056, -0.004), (-0.050, 0.004)]
grip = extrude('body.grip', gp, 0.034, material='rubber', plane='yz', bevel=0.004, segments=3)
for i in range(0):
    for x in (0.017, -0.017):
        cut(grip, box('c', (0.003, 0.05, 0.0025), (x, -0.045, -0.024 - i * 0.013), bevel=0, rot=(-14, 0, 0)))
extrude('body.pommel', [(-0.029, -0.110), (-0.074, -0.110), (-0.076, -0.122), (-0.028, -0.122)], 0.038, material='gunmetal', plane='yz', bevel=0.003)
# grip side panels: white plates on the rubber
for x in (0.0185, -0.0185):
    extrude('body.gripplate', [(-0.022, -0.012), (-0.030, -0.075), (-0.058, -0.075), (-0.050, -0.012)], 0.003, (x, 0, 0), 'offwhite', plane='yz', bevel=0.001)

# --- trigger guard and trigger
extrude('body.guard', [(-0.008, -0.004), (0.040, -0.004), (0.046, -0.010), (0.044, -0.034), (0.034, -0.040), (-0.016, -0.040),
                       (-0.014, -0.034), (0.030, -0.034), (0.038, -0.030), (0.038, -0.012), (0.034, -0.009), (-0.006, -0.009)],
        0.012, material='black', plane='yz', bevel=0.0015)
extrude('body.trigger', [(0.010, -0.005), (0.018, -0.005), (0.016, -0.016), (0.010, -0.026), (0.006, -0.026), (0.010, -0.016)],
        0.007, material='chrome', plane='yz', bevel=0.001)

finish('pistol')
