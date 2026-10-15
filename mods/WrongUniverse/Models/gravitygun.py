# Wrong Universe: the Gravity Gun. Built in Blender's frame (+Y forward along the barrel, +Z up), metres; the barrel's axis runs
# along Y at z 0.06 and the pistol grip hangs below the origin, as the earlier primitive model did (the game turns it into the hand).
# Run: blender -b --python ~/.cache/wu-models/wm-gadgets/gravitygun.py -- render
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from gadlib import *

AX = 0.06   # the barrel's height
reset()

# ---- body: a chunky hazard-orange housing, side profile extruded across ----
body = extrude('gun.body', [(-0.17, 0.0), (0.2, 0.0), (0.25, 0.03), (0.25, 0.1), (0.21, 0.136), (-0.08, 0.14), (-0.17, 0.1)],
               0.15, material='hazard', plane='yz', bevel=0.012, segments=3)
for sx in (1, -1):
    cut(body, box('cutter', (0.012, 0.24, 0.012), (sx * 0.079, 0.02, 0.075), 'black', bevel=0))      # light channel down each side
    cut(body, box('cutter', (0.012, 0.11, 0.006), (sx * 0.079, -0.08, 0.025), 'black', bevel=0))     # a seam low on the side
box('glow.strip', (0.003, 0.235, 0.006), (0.0734, 0.02, 0.075), 'glow_cyan', bevel=0)
box('glow.strip2', (0.003, 0.235, 0.006), (-0.0734, 0.02, 0.075), 'glow_cyan', bevel=0)
for sx in (1, -1):   # gunmetal side plates with chrome bolts and status lights
    box('gun.plate', (0.008, 0.12, 0.045), (sx * 0.077, -0.085, 0.05), 'gunmetal', bevel=0.003)
    for by in (-0.135, -0.035):
        for bz in (0.035, 0.065):
            cyl('gun.bolt', 0.0045, 0.006, (sx * 0.082, by, bz), 'chrome', axis='x', verts=8, bevel=0)
    cyl('gun.led', 0.0045, 0.004, (sx * 0.082, -0.085, 0.05), 'emit_green', axis='x', verts=10, bevel=0)
    cyl('gun.led2', 0.0035, 0.004, (sx * 0.082, -0.065, 0.05), 'emit_orange', axis='x', verts=10, bevel=0)

# cooling ribs on the back of the top, a carry handle in front of them
for i in range(6):
    box('gun.rib', (0.124, 0.007, 0.034), (0, -0.075 + i * 0.02, 0.15), 'gunmetal', bevel=0.002)
for py in (0.075, 0.19):
    box('gun.post', (0.026, 0.016, 0.07), (0, py, 0.17), 'gunmetal', bevel=0.004)
cyl('gun.handle', 0.013, 0.14, (0, 0.1325, 0.205), 'rubber', axis='y', verts=16, bevel=0.003, segments=1)
for i in range(3):
    torus('gun.handlerib', 0.0135, 0.0022, (0, 0.1 + i * 0.0325, 0.205), 'black', axis='y', verts=16, ring=4)

# ---- the emitter: a flared gunmetal cup with a chrome lip, a cyan ring inside and an orange core ----
lathe('gun.cup', [(0.06, 0.225), (0.074, 0.245), (0.084, 0.3), (0.093, 0.332), (0.088, 0.346), (0.072, 0.342), (0.054, 0.3),
                  (0.044, 0.272), (0.0, 0.268)], (0, 0, AX), 'gunmetal', axis='y', verts=32)
torus('gun.lip', 0.089, 0.0055, (0, 0.338, AX), 'chrome', axis='y', verts=32, ring=6)
for i in range(8):   # vents round the cup
    a = 2 * math.pi * i / 8
    box('gun.vent', (0.006, 0.03, 0.012), (math.cos(a) * 0.081, 0.295, AX + math.sin(a) * 0.081), 'black', bevel=0.001,
        rot=(0, -math.degrees(a), 0))
torus('glow.ring', 0.058, 0.0045, (0, 0.305, AX), 'glow_cyan', axis='y', verts=32, ring=5)
lathe('glow.core', [(0, 0.345), (0.022, 0.35), (0.036, 0.365), (0.042, 0.385), (0.036, 0.405), (0.022, 0.42), (0, 0.425)],
      (0, 0, AX), 'glow_orange', axis='y', verts=28)
lathe('glow.heart', [(0, 0.37), (0.014, 0.375), (0.018, 0.385), (0.014, 0.395), (0, 0.4)], (0, 0, AX), 'glow_white', axis='y', verts=20)

# ---- three hinged claws round the core ----
claw = []
claw.append(box('gun.bracket', (0.044, 0.045, 0.03), (0, 0.29, AX + 0.088), 'black', bevel=0.004))
claw.append(cyl('gun.hinge', 0.013, 0.042, (0, 0.305, AX + 0.104), 'chrome', axis='x', verts=14, bevel=0.002, segments=1))
claw.append(extrude('gun.arm', [(0.29, AX + 0.11), (0.33, AX + 0.124), (0.39, AX + 0.12), (0.44, AX + 0.1), (0.475, AX + 0.07),
                                (0.49, AX + 0.046), (0.476, AX + 0.04), (0.458, AX + 0.06), (0.425, AX + 0.085), (0.38, AX + 0.098),
                                (0.33, AX + 0.1), (0.3, AX + 0.093)], 0.032, material='gunmetal', plane='yz', bevel=0.005))
claw.append(extrude('gun.tip', [(0.468, AX + 0.072), (0.5, AX + 0.046), (0.497, AX + 0.03), (0.484, AX + 0.026), (0.452, AX + 0.052)],
                    0.038, material='hazard', plane='yz', bevel=0.004))
claw.append(tube_path('gun.piston', [(0, 0.27, AX + 0.07), (0, 0.33, AX + 0.085), (0, 0.37, AX + 0.098)], 0.005, 'chrome', ring=6))
claw.append(cyl('gun.clawled', 0.004, 0.024, (0, 0.40, AX + 0.115), 'emit_cyan', axis='x', verts=8, bevel=0))
spin_copy(claw, 120, (0, 0, AX), 'Y', '_b'); spin_copy(claw, 240, (0, 0, AX), 'Y', '_c')

# ---- grips: a rubber pistol grip with finger grooves, trigger and guard, a ribbed foregrip ----
extrude('gun.grip', [(0.045, 0.004), (0.038, -0.035), (0.043, -0.055), (0.034, -0.077), (0.039, -0.097), (0.028, -0.14),
                     (0.02, -0.155), (-0.035, -0.158), (-0.045, -0.145), (-0.03, -0.06), (-0.025, 0.004)],
        0.046, material='rubber', plane='yz', bevel=0.008, segments=3)
extrude('gun.trigger', [(0.06, 0.004), (0.074, 0.004), (0.072, -0.02), (0.064, -0.036), (0.056, -0.038), (0.06, -0.02)],
        0.012, material='black', plane='yz', bevel=0.002)
box('gun.guard', (0.014, 0.075, 0.008), (0, 0.083, -0.056), 'gunmetal', bevel=0.003)
box('gun.guardpost', (0.014, 0.009, 0.06), (0, 0.117, -0.03), 'gunmetal', bevel=0.003)
lathe('gun.fore', [(0.023, 0.002), (0.025, -0.02), (0.021, -0.03), (0.025, -0.045), (0.021, -0.055), (0.025, -0.07), (0.021, -0.08),
                   (0.025, -0.095), (0.02, -0.112), (0.0, -0.116)], (0, 0.175, 0), 'rubber', axis='z', verts=16)

# ---- skeleton stock with a rubber butt pad ----
stock = extrude('gun.stock', [(-0.165, 0.115), (-0.355, 0.095), (-0.365, -0.005), (-0.335, -0.035), (-0.165, 0.02)],
                0.07, material='gunmetal', plane='yz', bevel=0.008, segments=2)
cut(stock, extrude('cutter', [(-0.2, 0.08), (-0.325, 0.068), (-0.33, 0.01), (-0.2, 0.035)], 0.1, material='black', plane='yz', bevel=0))
box('gun.butt', (0.078, 0.022, 0.13), (0, -0.372, 0.03), 'rubber', bevel=0.007, segments=3)
box('gun.stockband', (0.074, 0.018, 0.1), (0, -0.18, 0.065), 'black', bevel=0.004)

# ---- cables from the side plates to the back of the emitter ----
for sx in (1, -1):
    tube_path('gun.cable', bezier((sx * 0.06, -0.12, 0.004), (sx * 0.1, -0.06, -0.035), (sx * 0.1, 0.16, -0.03), (sx * 0.05, 0.232, AX - 0.04), 12), 0.0075, 'rubber', ring=8)

report(25)
finish('gravitygun')
