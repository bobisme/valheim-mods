# sit_drink: seated, a tankard in the right hand. Lift it off the thigh, bring it up and tip it to the lips, a long pull with the head
# going back, lower it, a satisfied settle, and back to resting it on the thigh. Starts and ends holding the tankard on the thigh, so it
# can follow itself; the game's own seated pose shows through the spine.
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clip import Clip, hold_item, X, Y, Z
from mathutils import Vector
import math

argv = sys.argv[sys.argv.index('--') + 1:]
rig_path, out = argv[0], argv[1]
c = Clip('sit_drink', rig_path)
MUG = hold_item((0.0001, 0.0021, 0.0008), (-1, 0, 0), 85.5)  # (anim hold: the Tankard on RightHand_Attach)

def tilt(deg): return Vector((0, -math.sin(math.radians(deg)), math.cos(math.radians(deg))))  # the mug's up, tipped toward the face
MOUTH = Vector((0.0, 0.19, 1.28))
def at_lips(deg):  # the mug's centre with its rim's near edge at the lips
    return MOUTH + Vector((0.02, 0.05, 0)) - tilt(deg) * 0.1
REST, CHEST = Vector((0.2, 0.25, 0.76)), Vector((0.13, 0.36, 1.02))
SIDE, IN = Vector((-1, 0.4, 0)), Vector((-1, 0.1, 0))

c.hold(1, 'Right', MUG, REST, Z, SIDE)
c.hold(16, 'Right', MUG, REST + Vector((0, 0.02, 0.04)), Z, SIDE)          # lifted off the thigh
c.hold(32, 'Right', MUG, CHEST, tilt(10), IN)                             # up before the chest
c.hold(44, 'Right', MUG, at_lips(45), tilt(45), IN)                       # to the lips
c.hold(56, 'Right', MUG, at_lips(70), tilt(70), IN)                       # tipped up
c.hold(80, 'Right', MUG, at_lips(78), tilt(78), IN)                       # a long pull
c.hold(94, 'Right', MUG, CHEST + Vector((0, 0.02, 0.02)), tilt(5), IN)    # down from the lips
c.hold(110, 'Right', MUG, REST + Vector((0, 0.02, 0.05)), Z, SIDE)
c.hold(126, 'Right', MUG, REST, Z, SIDE)

c.turn(1, {})
c.turn(44, {'Neck': [(X, 4)], 'Head': [(X, 6)], 'RightShoulder': [(Y, -6)]})
c.turn(56, {'Neck': [(X, 10)], 'Head': [(X, 14)], 'Spine2': [(X, 3)], 'RightShoulder': [(Y, -10)]})
c.turn(80, {'Neck': [(X, 13)], 'Head': [(X, 18)], 'Spine2': [(X, 5)], 'RightShoulder': [(Y, -10)]})
c.turn(94, {'Neck': [(X, 2)], 'Head': [(X, -4)], 'Spine2': [(X, -2)]})                    # a breath out, chin down
c.turn(108, {'Spine2': [(X, 4)], 'Spine1': [(X, 2)], 'Head': [(X, 3), (Z, -6)]})           # a satisfied settle back
c.turn(126, {})
c.curl(1, 'RightHand', 0.85); c.curl(126, 'RightHand', 0.85)
c.prop('Tankard', 'RightHand_Attach', 0.25)

c.bake()
c.export(out, add=['Spine1', 'Spine2', 'Neck', 'Head', 'RightShoulder'],
         sets=['RightArm', 'RightForeArm', 'RightHand'] + [b.name for b in c.arm.pose.bones if b.name.startswith('RightHand') and b.name != 'RightHand'],
         blend_in=0.6, blend_out=0.6)
if 'render' in argv:
    c.render(out, [1, 32, 56, 80, 108], stand_ins=[('RightHand', MUG, (0.065, 0.17))])
