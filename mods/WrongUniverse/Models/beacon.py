# Wrong Universe: the Recall Beacon, a palm-sized homing puck lying flat (Blender +Z up), about 0.12 m across, centred on the origin.
# Run: blender -b --python ~/.cache/wu-models/wm-gadgets/beacon.py -- render
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from gadlib import *

reset()
V = 40
# a gunmetal base with a chamfered rim, a white upper shell, a dark lens dome
lathe('bcn.base', [(0.0, -0.026), (0.05, -0.026), (0.057, -0.02), (0.06, -0.008), (0.06, 0.0), (0.0, 0.0)], material='gunmetal', axis='z', verts=V)
lathe('bcn.shell', [(0.0, -0.001), (0.058, -0.001), (0.058, 0.006), (0.054, 0.015), (0.044, 0.02), (0.03, 0.022), (0.0, 0.022)], material='white', axis='z', verts=V)
torus('glow.ring', 0.049, 0.0032, (0, 0, 0.0155), 'glow_cyan', axis='z', verts=V, ring=5)   # a glowing ring sunk round the shoulder
lathe('bcn.lensrim', [(0.0, 0.021), (0.024, 0.021), (0.026, 0.025), (0.022, 0.028), (0.0, 0.028)], material='chrome', axis='z', verts=32)
lathe('glow.eye', [(0.0, 0.026), (0.019, 0.026), (0.017, 0.031), (0.011, 0.035), (0.0, 0.0365)], material='glow_cyan', axis='z', verts=24)
lathe('bcn.pupil', [(0.0, 0.0255), (0.008, 0.0255), (0.007, 0.0345), (0.0, 0.0355)], material='glass', axis='z', verts=16)

# hazard pads round the rim, with a grip notch between each pair
pad = box('bcn.pad', (0.006, 0.022, 0.012), (0.0605, 0, -0.012), 'hazard', bevel=0.0015)
for i in range(1, 6): spin_copy([pad], 60 * i, (0, 0, 0), 'Z', '_%d' % i)
nub = box('bcn.nub', (0.004, 0.01, 0.006), (0.061, 0, -0.012), 'rubber', bevel=0.001, rot=(0, 0, 30))
for i in range(1, 6): spin_copy([nub], 60 * i, (0, 0, 0), 'Z', '_%d' % i)
for i in range(6):   # black rays printed on the shell between the pads (raised a hair, never flush)
    a = math.radians(30 + 60 * i)
    box('bcn.ray', (0.012, 0.0025, 0.0012), (math.cos(a) * 0.05, math.sin(a) * 0.05, 0.0072), 'black', bevel=0, rot=(0, -20, math.degrees(a)))

# a fold-out antenna on a hinge at the back, swung up and back
box('bcn.hinge', (0.018, 0.012, 0.01), (0, -0.044, 0.02), 'gunmetal', bevel=0.002)
cyl('bcn.pin', 0.0035, 0.022, (0, -0.044, 0.024), 'chrome', axis='x', verts=10, bevel=0)
tube_path('bcn.mast', [(0, -0.044, 0.024), (0, -0.062, 0.058), (0, -0.079, 0.092)], 0.0022, 'chrome', ring=8)
lathe('bcn.antball', [(0.0, 0.0), (0.004, 0.001), (0.005, 0.004), (0.004, 0.007), (0.0, 0.008)], (0, -0.0805, 0.091), 'emit_red', axis='z', verts=12)
# status LEDs and a tiny speaker grille on the front of the shell
for i, c in enumerate(('emit_green', 'emit_orange')):
    cyl('bcn.led', 0.0025, 0.003, (-0.008 + i * 0.016, 0.046, 0.011), c, axis='z', verts=10, bevel=0, rot=(28, 0, 0))
for i in range(4):
    box('bcn.grille', (0.002, 0.009, 0.0015), (-0.0045 + i * 0.003, 0.034, 0.0185), 'black', bevel=0, rot=(28, 0, 0))

report(12)
finish('beacon')
