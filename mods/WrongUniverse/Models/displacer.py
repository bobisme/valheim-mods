# Wrong Universe: the Displacer, a handheld blink device. Blender frame (+Y forward along the barrel, +Z up), metres; barrel axis at
# z 0.05, the grip below the origin, as the earlier primitive model (the game turns it into the hand).
# Run: blender -b --python ~/.cache/wu-models/wm-gadgets/displacer.py -- render
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from gadlib import *

AX = 0.05
reset()

# ---- a sleek white shell over a black chassis ----
shell = extrude('dsp.shell', [(-0.155, 0.03), (-0.14, 0.085), (-0.09, 0.103), (0.06, 0.1), (0.14, 0.078), (0.165, 0.05), (0.15, 0.022),
                              (-0.12, 0.02)], 0.072, material='white', plane='yz', bevel=0.012, segments=3)
for sx in (1, -1):
    cut(shell, box('cutter', (0.01, 0.2, 0.008), (sx * 0.037, 0.0, 0.07), 'black', bevel=0))
    box('glow.strip' + ('' if sx > 0 else 'b'), (0.003, 0.195, 0.004), (sx * 0.0335, 0.0, 0.07), 'glow_violet', bevel=0)
extrude('dsp.chassis', [(-0.15, 0.0), (0.16, 0.0), (0.168, 0.03), (-0.155, 0.035)], 0.064, material='gunmetal', plane='yz', bevel=0.006)
box('dsp.screen', (0.05, 0.004, 0.032), (0, -0.157, 0.062), 'black', bevel=0.002, rot=(-10, 0, 0))
box('dsp.screenglow', (0.04, 0.002, 0.022), (0, -0.1592, 0.0618), 'emit_violet', bevel=0, rot=(-10, 0, 0))
for i, c in enumerate(('emit_green', 'emit_orange', 'emit_red')):
    cyl('dsp.led', 0.0035, 0.004, (0.022 - i * 0.011, 0.06, 0.1015), c, axis='z', verts=10, bevel=0)

# ---- swept fin on top ----
extrude('dsp.fin', [(-0.11, 0.095), (0.02, 0.1), (-0.02, 0.128), (-0.1, 0.135), (-0.125, 0.125)], 0.012, material='chrome', plane='yz', bevel=0.003)

# ---- barrel: chrome rod with hazard collar, violet coils between gunmetal spacers ----
lathe('dsp.collar', [(0.0, 0.15), (0.028, 0.15), (0.03, 0.16), (0.03, 0.18), (0.026, 0.19), (0.0, 0.19)], (0, 0, AX), 'hazard', axis='y', verts=28)
lathe('dsp.rod', [(0.0, 0.18), (0.017, 0.18), (0.017, 0.43), (0.0, 0.43)], (0, 0, AX), 'chrome', axis='y', verts=24)
for i in range(4):
    y = 0.215 + i * 0.055
    torus('glow.coil', 0.025, 0.0048, (0, y, AX), 'glow_violet', axis='y', verts=24, ring=5)
    lathe('dsp.spacer', [(0.0, y + 0.02), (0.022, y + 0.02), (0.024, y + 0.025), (0.024, y + 0.032), (0.022, y + 0.036), (0.0, y + 0.036)],
          (0, 0, AX), 'gunmetal', axis='y', verts=20)
for k in range(3):   # three rails from the body to the emitter ring
    a = math.radians(90 + 120 * k)
    box('dsp.rail', (0.006, 0.27, 0.006), (math.cos(a) * 0.043, 0.3, AX + math.sin(a) * 0.043), 'gunmetal', bevel=0.0015, rot=(0, -math.degrees(a), 0))

# ---- emitter: a chrome ring round a violet ring and a white-hot tip ----
torus('dsp.ring', 0.046, 0.007, (0, 0.44, AX), 'chrome', axis='y', verts=36, ring=8)
torus('glow.ring', 0.036, 0.004, (0, 0.445, AX), 'glow_violet', axis='y', verts=32, ring=5)
lathe('glow.tip', [(0, 0.445), (0.013, 0.452), (0.02, 0.465), (0.016, 0.478), (0, 0.485)], (0, 0, AX), 'glow_white', axis='y', verts=20)
lathe('dsp.tipmount', [(0.0, 0.425), (0.02, 0.425), (0.014, 0.448), (0.0, 0.448)], (0, 0, AX), 'gunmetal', axis='y', verts=20)

# ---- compact grip, trigger and guard ----
extrude('dsp.grip', [(0.03, 0.004), (0.026, -0.03), (0.031, -0.048), (0.023, -0.068), (0.028, -0.086), (0.016, -0.128), (0.008, -0.14),
                     (-0.036, -0.142), (-0.044, -0.13), (-0.03, -0.05), (-0.03, 0.004)], 0.04, material='rubber', plane='yz', bevel=0.007, segments=3)
box('dsp.gripcap', (0.042, 0.05, 0.01), (0, -0.014, -0.143), 'gunmetal', bevel=0.003, rot=(-12, 0, 0))
extrude('dsp.trigger', [(0.045, 0.002), (0.057, 0.002), (0.055, -0.018), (0.047, -0.03), (0.041, -0.031), (0.045, -0.016)], 0.01, material='chrome', plane='yz', bevel=0.002)
box('dsp.guard', (0.012, 0.06, 0.007), (0, 0.062, -0.045), 'gunmetal', bevel=0.003)
box('dsp.guardpost', (0.012, 0.008, 0.05), (0, 0.089, -0.022), 'gunmetal', bevel=0.003)

report(20)
finish('displacer')
