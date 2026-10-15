# Laser Sword hilt (Wrong Universe). Blender frame: the hilt stands along Z, the blade comes out of the emitter at the bottom (-Z), the hand
# at the origin. The game frame (Saber.cs Hilt): along y, emitter mouth at y -0.155 (the blade starts at -0.15), pommel up to y 0.18.
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from wulib import *
reset()

# the main body: chrome, turned
lathe('hilt.body', [(0.0, -0.100), (0.0195, -0.100), (0.0195, 0.150), (0.0, 0.150)], material='chrome', axis='z', verts=32)

# emitter shroud: a flared gunmetal cup with a crown of notches, a dark throat
shroud = lathe('hilt.shroud', [(0.0, -0.158), (0.0255, -0.158), (0.0275, -0.154), (0.0275, -0.132), (0.0235, -0.118), (0.0215, -0.104),
                               (0.0, -0.104)], material='gunmetal', axis='z', verts=32)
for i in range(6):
    a = math.radians(i * 60)
    cut(shroud, box('c', (0.012, 0.03, 0.022), (0.028 * math.cos(a), 0.028 * math.sin(a), -0.152), bevel=0.002, rot=(0, 0, i * 60 + 90)))
cut(shroud, cyl('c', 0.0165, 0.04, (0, 0, -0.16), axis='z', verts=32, bevel=0))
cyl('hilt.throat', 0.017, 0.003, (0, 0, -0.140), 'glass', axis='z', verts=24, bevel=0)
cyl('hilt.focus', 0.006, 0.006, (0, 0, -0.143), 'emit_white', axis='z', verts=16, bevel=0)
lathe('hilt.lip', [(0.0, -0.104), (0.0225, -0.104), (0.0235, -0.102), (0.0235, -0.094), (0.0225, -0.092), (0.0, -0.092)], material='hazard', axis='z', verts=32)

# control box: a raised block on one side with the activation button and a power slider
box('hilt.control', (0.012, 0.022, 0.05), (0.020, 0, -0.066), 'gunmetal', bevel=0.003)
cyl('hilt.button', 0.0045, 0.006, (0.0275, 0, -0.078), 'emit_red', axis='x', verts=16, bevel=0.001)
box('hilt.slider', (0.004, 0.008, 0.018), (0.0272, 0, -0.054), 'chrome', bevel=0.001)
box('hilt.track', (0.002, 0.004, 0.024), (0.0262, 0, -0.054), 'black', bevel=0)

# grip: black rubber ridges turned round the body, between two gunmetal rings
ridge = [(0.0, -0.040)]
z = -0.040
for i in range(9):
    ridge += [(0.0210, z), (0.0225, z + 0.003), (0.0225, z + 0.011), (0.0210, z + 0.014)]
    z += 0.016
ridge += [(0.0, z)]
lathe('hilt.grip', ridge, material='rubber', axis='z', verts=28)
for zz in (-0.046, z + 0.002):
    lathe('hilt.ring', [(0.0, 0.0), (0.0225, 0.0), (0.0240, 0.0015), (0.0240, 0.0055), (0.0225, 0.007), (0.0, 0.007)], (0, 0, zz), 'gunmetal', axis='z', verts=28)
# six grip bars round the ridges (the hilt's classic look)
for i in range(6):
    a = math.radians(i * 60 + 30)
    box('hilt.bar', (0.004, 0.006, 0.13), (0.0235 * math.cos(a), 0.0235 * math.sin(a), 0.032), 'black', bevel=0.0015, rot=(0, 0, i * 60 + 30))

# pommel: gunmetal cap with a chrome ring and a belt loop
lathe('hilt.pommel', [(0.0, 0.150), (0.0220, 0.150), (0.0240, 0.153), (0.0240, 0.170), (0.0215, 0.178), (0.0120, 0.182), (0.0, 0.182)],
      material='gunmetal', axis='z', verts=32)
lathe('hilt.pommelring', [(0.0, 0.0), (0.0245, 0.0), (0.0255, 0.0015), (0.0255, 0.0045), (0.0245, 0.006), (0.0, 0.006)], (0, 0, 0.158), 'chrome', axis='z', verts=32)
torus('hilt.loop', 0.008, 0.0018, (0, 0, 0.187), 'chrome', axis='x', verts=20, ring=6)

finish('saber')
