# Wrong Universe: the Stim Pack, an auto-injector lying along Blender +Y (the needle end forward), centred on the origin, about 0.2 m.
# Run: blender -b --python ~/.cache/wu-models/wm-gadgets/stimpack.py -- render
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from gadlib import *

reset()
V = 32
# white body in two halves either side of the dose window
lathe('stim.rear', [(0.0, -0.075), (0.019, -0.075), (0.0225, -0.071), (0.0235, -0.06), (0.0235, -0.04), (0.021, -0.036), (0.0, -0.036)],
      material='white', axis='y', verts=V)
lathe('stim.front', [(0.0, 0.036), (0.021, 0.036), (0.0235, 0.04), (0.0235, 0.058), (0.02, 0.066), (0.0, 0.066)], material='white', axis='y', verts=V)
for i in range(4):   # rubber grip rings round the back
    torus('stim.grip', 0.0235, 0.0022, (0, -0.066 + i * 0.0075, 0), 'rubber', axis='y', verts=V, ring=5)
lathe('stim.band', [(0.0, 0.041), (0.0242, 0.041), (0.0245, 0.043), (0.0245, 0.054), (0.0242, 0.056), (0.0, 0.056)], material='hazard', axis='y', verts=V)
chev = box('stim.chev', (0.0045, 0.011, 0.0016), (0, 0.0485, 0.0247), 'black', bevel=0, rot=(0, 0, 35))   # black slashes round the hazard band
for i in range(1, 8): spin_copy([chev], 45 * i, (0, 0, 0), 'Y', '_%d' % i)

# the dose window: a glowing green ampoule behind chrome bars, a chrome ring at each end
lathe('glow.dose', [(0.0, -0.038), (0.016, -0.036), (0.0175, -0.03), (0.0175, 0.03), (0.016, 0.036), (0.0, 0.038)], material='glow_green', axis='y', verts=24)
lathe('stim.core', [(0.0, -0.037), (0.008, -0.037), (0.008, 0.037), (0.0, 0.037)], material='c_1a3a1e_m10_g90', axis='y', verts=12)
for y in (-0.036, 0.036):
    torus('stim.ring', 0.0215, 0.003, (0, y, 0), 'chrome', axis='y', verts=V, ring=6)
for k in range(4):
    a = math.radians(45 + 90 * k)
    cyl('stim.bar', 0.0022, 0.074, (math.cos(a) * 0.0205, 0, math.sin(a) * 0.0205), 'chrome', axis='y', verts=8, bevel=0)
# graduation ticks on one bar's side of the window
for i in range(5):
    box('stim.tick', (0.004, 0.0012, 0.0012), (0.0, -0.024 + i * 0.012, 0.0195), 'black', bevel=0)

# the thumb plunger behind: a gunmetal collar and a ridged orange button
lathe('stim.collar', [(0.0, -0.09), (0.014, -0.09), (0.016, -0.085), (0.016, -0.074), (0.0, -0.074)], material='gunmetal', axis='y', verts=24)
lathe('stim.button', [(0.0, -0.112), (0.013, -0.112), (0.0155, -0.109), (0.0155, -0.096), (0.012, -0.09), (0.0, -0.09)], material='hazard', axis='y', verts=24)
for i in range(3):
    torus('stim.buttonrib', 0.0156, 0.0012, (0, -0.106 + i * 0.0045, 0), 'c_b8640a_m10_g50', axis='y', verts=24, ring=4)

# the needle end: a red safety ring, a tapered gunmetal cap and a chrome nose
lathe('stim.safety', [(0.0, 0.066), (0.018, 0.066), (0.0195, 0.069), (0.0195, 0.074), (0.017, 0.076), (0.0, 0.076)], material='red', axis='y', verts=V)
lathe('stim.cap', [(0.0, 0.076), (0.016, 0.076), (0.0165, 0.09), (0.012, 0.104), (0.0085, 0.108), (0.0, 0.108)], material='gunmetal', axis='y', verts=24)
lathe('stim.nose', [(0.0, 0.107), (0.006, 0.107), (0.0045, 0.114), (0.0, 0.116)], material='chrome', axis='y', verts=16)

# a pocket clip along the top
box('stim.clip', (0.007, 0.07, 0.0025), (0, -0.035, 0.0262), 'chrome', bevel=0.001)
box('stim.clipfoot', (0.007, 0.006, 0.004), (0, -0.068, 0.0245), 'chrome', bevel=0.001)
cyl('stim.led', 0.0022, 0.002, (0.0, 0.01, 0.0), 'emit_green', axis='z', verts=8, bevel=0, rot=(0, 0, 0))
bpy.data.objects['stim.led'].location = (0.0, 0.062, 0.0205)

report(15)
finish('stimpack')
