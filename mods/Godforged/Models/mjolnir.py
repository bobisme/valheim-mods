# Mjölnir (Godforged). Blender frame: the handle stands along Z with the hand at the origin, the head at the bottom (-Z), as Wrong
# Universe's saber (the game frame: along y, head at -y; Mjolnir.cs turns it into the hand). The head's long axis is X.
# The handle is short: Loki, as a fly, bit Brokkr's eyelid while he worked the bellows (Skáldskaparmál 35).
# Run: blender -b --python mods/Godforged/Models/mjolnir.py -- render
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from gadlib import *

reset()
IRON = 'c_44474c_m80_g45'      # dark forged iron
SILVER = 'c_c9ccd2_m90_g70'    # inlaid knotwork
LEATHER = 'c_3a2416_m00_g30'
WOOD = 'c_5a4129_m00_g30'
HEAD_Z = -0.25

# the short handle: oak under a leather wrap, an iron collar into the head, a pommel with a thong loop
lathe('hammer.handle', [(0.0, 0.105), (0.017, 0.105), (0.018, -0.16), (0.0, -0.16)], material=WOOD, axis='z', verts=20)
for i in range(9):
    z = 0.075 - i * 0.026
    torus('hammer.wrap', 0.0195, 0.0042, (0, 0, z), LEATHER, axis='z', verts=20, ring=6, rot=(8 if i % 2 else -8, 0, 0))
lathe('hammer.collar', [(0.0, -0.13), (0.021, -0.13), (0.026, -0.15), (0.03, -0.175), (0.0, -0.175)], material=IRON, axis='z', verts=24)
lathe('hammer.pommel', [(0.0, 0.095), (0.022, 0.095), (0.027, 0.11), (0.024, 0.128), (0.012, 0.136), (0.0, 0.137)], material=IRON, axis='z', verts=20)
torus('hammer.loop', 0.016, 0.004, (0, 0, 0.152), IRON, axis='y', verts=16, ring=6)
tube_path('hammer.thong', [(0, 0, 0.165), (0.01, 0.006, 0.19), (0.004, 0.01, 0.225), (-0.008, 0.004, 0.255)], 0.0035, LEATHER, ring=6)

# the head: an iron block with flared, eight-sided striking faces at both ends
box('hammer.head', (0.22, 0.13, 0.14), (0, 0, HEAD_Z), IRON, bevel=0.012, segments=3)
for side in (-1, 1):
    # the striking faces: a waist, then a block a little bigger than the head, its edges well rounded (flared ends, as the pendants)
    box('hammer.neck', (0.02, 0.122, 0.132), (side * 0.118, 0, HEAD_Z), IRON, bevel=0.008)
    box('hammer.face', (0.05, 0.158, 0.168), (side * 0.15, 0, HEAD_Z), IRON, bevel=0.016, segments=3)
    # a silver band where each face meets the block
    box('hammer.band', (0.012, 0.142, 0.152), (side * 0.096, 0, HEAD_Z), SILVER, bevel=0.004)

# knotwork on the broad sides: interlaced silver rings and a glowing rune row (the hammer is never quite cold)
for y in (-0.066, 0.066):
    torus('hammer.knot', 0.036, 0.0032, (0, y, HEAD_Z + 0.006), SILVER, axis='y', verts=32, ring=5)   # a sun ring with four studs
    for a in range(4):
        r = math.radians(45 + 90 * a)
        cyl('hammer.stud', 0.007, 0.006, (math.cos(r) * 0.036, y, HEAD_Z + 0.006 + math.sin(r) * 0.036), SILVER, axis='y', verts=12, bevel=0.002)
    for k, x in enumerate((-0.07, -0.035, 0.0, 0.035, 0.07)):
        box('glow.rune', (0.0035, 0.002, 0.026), (x, y * 1.04, HEAD_Z - 0.048), 'glow_cyan', bevel=0, rot=(0, 18 if k % 2 else -18, 0))
# a ridge over the top and under the bottom, where the handle goes through
box('hammer.ridge', (0.16, 0.05, 0.012), (0, 0, HEAD_Z + 0.072), IRON, bevel=0.004)
box('hammer.ridge', (0.16, 0.05, 0.012), (0, 0, HEAD_Z - 0.072), IRON, bevel=0.004)
lathe('hammer.wedge', [(0.0, HEAD_Z - 0.078), (0.016, HEAD_Z - 0.078), (0.016, HEAD_Z - 0.084), (0.0, HEAD_Z - 0.086)], material=SILVER, axis='z', verts=16)

report(12)
finish('mjolnir')
