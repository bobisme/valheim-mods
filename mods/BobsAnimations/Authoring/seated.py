# The seated set: blender -b --python seated.py -- <rig json> <out dir> <clip> [render]
# Clips toward a side (talk, point, toast) are written toward the character's right; <name>_left is the mirror image (for talk and
# point: the left hand; a toast is always the right hand, raised toward the other side). All start and end on the game's own seated pose
# (hands on the knees), except the loops (fold_arms, elbows), which hold their pose and breathe.
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clip import Clip, TANKARD, MOUTH, X, Y, Z
from mathutils import Vector
import math

DOWN_HAND = ((0, 1, -0.3), (0, 0.2, -1))  # fingers forward and down, palm down: a hand on its way back to the knee

def talk(c):
    """Talking to someone on the right: the right hand's open-palmed beats toward them, the head turned to them, the jaw working."""
    c.hand(1, 'Right')
    beats = [(14, (0.30, 0.40, 0.95), (-0.2, 1, 0.25), (0.1, 0.15, 1)), (26, (0.36, 0.43, 0.99), (0.35, 1, 0.2), (0.4, 0.1, 1)),
             (36, (0.33, 0.41, 0.94), (0.2, 1, 0.1), (0.2, 0.1, 1)), (50, (0.38, 0.44, 1.01), (0.4, 1, 0.35), (0.6, 0.2, 0.8)),
             (62, (0.32, 0.41, 0.95), (0.15, 1, 0.15), (0.2, 0.1, 1)), (76, (0.35, 0.43, 0.98), (0.3, 1, 0.2), (0.3, 0.1, 1)),
             (92, (0.31, 0.36, 0.82)) + DOWN_HAND]
    for f, w, fi, pa in beats: c.pose(f, 'Right', w, fi, pa)
    c.hand(108, 'Right')
    c.curl(1, 'RightHand', 0.25, Thumb=0.1); c.curl(108, 'RightHand', 0.25, Thumb=0.1)
    c.turn(1, {})
    for f in range(14, 90, 6):
        nod = -4 if f in (26, 50, 74) else 0
        c.turn(f, {'Spine2': [(Z, -6)], 'Neck': [(Z, -6)], 'Head': [(Z, -16), (X, nod)], 'Jaw': [(X, -8 if (f // 6) % 2 else -2)]})
    c.turn(100, {'Head': [(Z, -8)]})
    c.turn(108, {})

def point(c):
    """Pointing out something to the front right, looking where they point."""
    c.hand(1, 'Right')
    aim = ((0.55, 0.85, 0.25), (-0.3, 0, -1))
    c.pose(12, 'Right', (0.40, 0.42, 1.12), *aim)
    c.pose(18, 'Right', (0.43, 0.46, 1.14), *aim)
    c.pose(40, 'Right', (0.43, 0.46, 1.14), *aim)
    c.pose(52, 'Right', (0.33, 0.36, 0.88), *DOWN_HAND)
    c.hand(66, 'Right')
    c.curl(1, 'RightHand', 0); c.curl(10, 'RightHand', 1, Index=0, Thumb=0.5); c.curl(46, 'RightHand', 1, Index=0, Thumb=0.5); c.curl(66, 'RightHand', 0)
    c.turn(1, {})
    look = {'Spine2': [(Z, -8)], 'Neck': [(Z, -6)], 'Head': [(Z, -22), (X, 3)]}
    c.turn(12, look); c.turn(40, look); c.turn(56, {'Head': [(Z, -6)]}); c.turn(66, {})

def toast(c, toward):
    """Skál! The tankard raised high toward someone (toward=+1 the right, -1 the left), held there, then a pull from it."""
    def tilt(deg): return Vector((0, -math.sin(math.radians(deg)), math.cos(math.radians(deg))))
    lips = {54: Vector((0.0, 0.18, 1.305)), 68: Vector((0.0, 0.143, 1.347))}  # the mouth with the head tipped as keyed (as in sit_drink)
    def at_lips(deg, f): return lips[f] + Vector((0.02, 0.035, 0)) - tilt(deg) * 0.1
    REST, CHEST, SIDE, IN = Vector((0.2, 0.25, 0.76)), Vector((0.13, 0.36, 1.02)), Vector((-1, 0.4, 0)), Vector((-1, 0.1, 0))
    up = Vector((0.12 * toward, 0.15, 1))
    c.hold(1, 'Right', TANKARD, REST, Z, SIDE)
    c.hold(18, 'Right', TANKARD, Vector((0.2 + 0.12 * toward, 0.5, 1.38)), up, IN)
    c.hold(26, 'Right', TANKARD, Vector((0.21 + 0.13 * toward, 0.53, 1.44)), up, IN)
    c.hold(40, 'Right', TANKARD, Vector((0.2 + 0.12 * toward, 0.5, 1.38)), up, IN)
    c.hold(54, 'Right', TANKARD, at_lips(40, 54), tilt(40), IN)
    c.hold(68, 'Right', TANKARD, at_lips(68, 68), tilt(68), IN)
    c.hold(80, 'Right', TANKARD, CHEST, tilt(5), IN)
    c.hold(96, 'Right', TANKARD, REST, Z, SIDE)
    c.curl(1, 'RightHand', 0.85); c.curl(96, 'RightHand', 0.85)
    c.prop('Tankard', 'RightHand_Attach', 0.25)
    c.turn(1, {})
    raise_ = {'Spine2': [(Z, -6 * toward), (X, 2)], 'Neck': [(Z, -6 * toward)], 'Head': [(Z, -18 * toward), (X, 6)], 'RightShoulder': [(Y, -10)],
              'Jaw': [(X, -10)]}
    c.turn(18, raise_); c.turn(40, dict(raise_, Jaw=[(X, -2)]))
    c.turn(54, {'Neck': [(X, 4)], 'Head': [(X, 6)], 'RightShoulder': [(Y, -6)]})
    c.turn(68, {'Neck': [(X, 10)], 'Head': [(X, 14)], 'Spine2': [(X, 3)], 'RightShoulder': [(Y, -8)]})
    c.turn(80, {'Head': [(X, -3)]}); c.turn(96, {})

def laugh(c):
    """A belly laugh: back, slapping the right knee twice, then doubled over, shaking, and settling."""
    c.hand(1, 'Right')
    lift = lambda f, z: c.pose(f, 'Right', (0.30, 0.30, z), *DOWN_HAND)
    lift(8, 0.86); c.hand(13, 'Right'); lift(19, 0.84); c.hand(24, 'Right'); c.hand(96, 'Right')
    c.curl(1, 'RightHand', 0.1); c.curl(96, 'RightHand', 0.1)
    c.turn(1, {})
    back = lambda s, h, j: {'Spine1': [(X, s * 0.5)], 'Spine2': [(X, s)], 'Neck': [(X, h * 0.6)], 'Head': [(X, h)], 'Jaw': [(X, j)]}
    c.turn(8, back(7, 10, -14)); c.turn(13, back(7, 10, -10)); c.turn(19, back(5, 8, -16)); c.turn(24, back(5, 8, -8))
    for f, s, j in ((34, -9, -14), (40, -11, -8), (46, -8, -14), (52, -10, -8), (58, -8, -12)):
        c.turn(f, back(s, -6, j))
    c.turn(66, back(-3, 4, -10)); c.turn(78, back(2, 3, -4)); c.turn(96, {})

def think(c):
    """Thinking it over: a fist under the chin, stroking the beard, a glance one way and the other."""
    c.hand(1, 'Right')
    chin = ((-0.35, 0.25, 1), (-0.25, -1, 0.1))
    for f, z in ((20, 1.09), (40, 1.075), (52, 1.09), (64, 1.075), (76, 1.09), (120, 1.09)): c.pose(f, 'Right', (0.08, 0.17, z), *chin)
    c.pose(136, 'Right', (0.25, 0.30, 0.85), *DOWN_HAND)
    c.hand(150, 'Right')
    c.curl(1, 'RightHand', 0)
    for f, a in ((14, 0.65), (40, 0.5), (52, 0.7), (64, 0.5), (76, 0.65), (130, 0.65)): c.curl(f, 'RightHand', a, Thumb=0.3)
    c.curl(150, 'RightHand', 0)
    c.turn(1, {})
    c.turn(20, {'Spine2': [(X, -3)], 'Neck': [(X, -3)], 'Head': [(X, -6), (Z, 4)]})
    c.turn(40, {'Spine2': [(X, -3)], 'Neck': [(X, -3)], 'Head': [(X, -5), (Z, 8)]})
    c.turn(76, {'Spine2': [(X, -3)], 'Neck': [(X, -3)], 'Head': [(X, -4), (Z, 2)]})
    c.turn(100, {'Spine2': [(X, -3)], 'Neck': [(X, -3)], 'Head': [(X, -6), (Z, -8)]})
    c.turn(120, {'Spine2': [(X, -3)], 'Neck': [(X, -3)], 'Head': [(X, -6), (Z, -6)]})
    c.turn(150, {})

def both(c, f, wrist, fingers, palm):
    """Both hands, the left the mirror of the right."""
    fl = lambda v: (-v[0], v[1], v[2])
    c.pose(f, 'Right', wrist, fingers, palm); c.pose(f, 'Left', fl(wrist), fl(fingers), fl(palm))

def cheer(c):
    """Both fists up, pumped twice, a shout."""
    for s in ('Right', 'Left'): c.hand(1, s); c.hand(72, s)
    up = ((0, 0, 1), (-0.4, -1, 0))
    for f, z in ((12, 1.55), (20, 1.48), (28, 1.57), (36, 1.48), (44, 1.55)): both(c, f, (0.28, 0.13, z), *up)
    both(c, 58, (0.3, 0.3, 0.95), *DOWN_HAND)
    for h in ('RightHand', 'LeftHand'): c.curl(1, h, 0); c.curl(8, h, 1, Thumb=0.6); c.curl(58, h, 1, Thumb=0.6); c.curl(72, h, 0)
    c.turn(1, {})
    shout = lambda j, hd: {'Spine2': [(X, 5)], 'Neck': [(X, 5)], 'Head': [(X, hd)], 'Jaw': [(X, j)], 'RightShoulder': [(Y, -12)], 'LeftShoulder': [(Y, 12)]}
    for f, j, hd in ((12, -14, 10), (20, -6, 9), (28, -16, 12), (36, -6, 9), (44, -14, 10)): c.turn(f, shout(j, hd))
    c.turn(58, {'Head': [(X, 3)], 'Jaw': [(X, -3)]}); c.turn(72, {})

def fold_arms(c):
    """Arms folded across the chest, settled back, breathing. Loops."""
    for f, dz in ((1, 0), (45, 0.008), (90, 0)):
        c.pose(f, 'Right', (-0.15, 0.25, 0.98 + dz), (-0.7, -0.5, 0.1), (0.1, -0.5, -0.8))
        c.pose(f, 'Left', (0.15, 0.22, 0.94 + dz), (0.7, -0.5, 0.15), (-0.1, -0.5, 0.8))
    for h in ('RightHand', 'LeftHand'): c.curl(1, h, 0.45); c.curl(90, h, 0.45)
    for f, b in ((1, 0), (45, 1.5), (90, 0)):
        c.turn(f, {'Spine1': [(X, 2)], 'Spine2': [(X, 3 + b)], 'Head': [(X, -2 + b * 0.5)]})

def elbows(c):
    """Leaning forward, elbows on the knees, hands loosely clasped between them, breathing. Loops."""
    for f, dz in ((1, 0), (45, 0.006), (90, 0)):
        c.pose(f, 'Right', (0.06, 0.53, 0.73 + dz), (-1, 0.4, -0.4), (-0.3, -0.2, -1))
        c.pose(f, 'Left', (-0.06, 0.53, 0.73 + dz), (1, 0.4, -0.4), (0.3, -0.2, -1))
    for h in ('RightHand', 'LeftHand'): c.curl(1, h, 0.35); c.curl(90, h, 0.35)
    for f, b in ((1, 0), (45, 1.2), (90, 0)):
        c.turn(f, {'Spine': [(X, -9)], 'Spine1': [(X, -12)], 'Spine2': [(X, -12 + b)], 'Neck': [(X, 10)], 'Head': [(X, 14 - b * 0.5)]})

TABLE = ((0, 0.62, 0.94), (1.0, 0.5))  # a game table's top in front of a seated villager (`anim near`: piece_table's top 0.94 above the seat)

def board_move(c):
    """A move at a board game on the table in front: reach over, pinch a piece, slide it a square or two, set it down, sit back."""
    c.hand(1, 'Right')
    over = ((-0.2, 1, -0.6), (0, 0.3, -1))  # fingers forward and down, palm down
    # (the table top is 0.94 m up; fingers pointing down reach ~8 cm below the wrist, so a touch on the board is the wrist at ~1.06)
    for f, w in ((14, (0.12, 0.42, 1.14)), (22, (0.10, 0.50, 1.06)), (30, (0.10, 0.50, 1.12)), (40, (0.0, 0.56, 1.12)),
                 (48, (0.0, 0.56, 1.06)), (56, (0.0, 0.55, 1.09)), (64, (0.12, 0.42, 1.14))):
        c.pose(f, 'Right', w, *over)
    c.pose(72, 'Right', (0.22, 0.24, 1.06), (0, 1, -0.5), (0, 0.3, -1))  # back toward the body, clear of the table's edge
    c.pose(82, 'Right', (0.28, 0.25, 0.8), *DOWN_HAND)
    c.hand(92, 'Right')
    pinch = dict(Index=0.45, Thumb=0.5)
    c.curl(1, 'RightHand', 0.1); c.curl(14, 'RightHand', 0.3, Index=0.1, Thumb=0.2); c.curl(22, 'RightHand', 0.85, **pinch)
    c.curl(48, 'RightHand', 0.85, **pinch); c.curl(56, 'RightHand', 0.3, Index=0.1, Thumb=0.2); c.curl(92, 'RightHand', 0.1)
    c.turn(1, {})
    look = lambda h: {'Spine1': [(X, -4)], 'Spine2': [(X, -6)], 'Neck': [(X, -6)], 'Head': [(X, h)]}
    c.turn(14, look(-8)); c.turn(30, look(-10)); c.turn(48, look(-10)); c.turn(64, look(-4))
    c.turn(76, {'Head': [(X, 3)]})  # and up, to the other player
    c.turn(92, {})

CLIPS = {
    'sit_talk_right': (talk, False), 'sit_talk_left': (talk, True),
    'sit_point_right': (point, False), 'sit_point_left': (point, True),
    'sit_toast_right': (lambda c: toast(c, 1), False), 'sit_toast_left': (lambda c: toast(c, -1), False),
    'sit_laugh': (laugh, False), 'sit_think': (think, False), 'sit_cheer': (cheer, False),
    'sit_fold_arms': (fold_arms, False), 'sit_elbows': (elbows, False), 'sit_board_move': (board_move, False),
}
LOOPS = {'sit_fold_arms', 'sit_elbows'}
PREVIEW = {'sit_talk_right': [1, 26, 50, 92], 'sit_point_right': [1, 18, 52], 'sit_toast_right': [1, 26, 68, 96], 'sit_toast_left': [26],
           'sit_laugh': [8, 19, 40, 66], 'sit_think': [20, 64, 100, 136], 'sit_cheer': [12, 28, 58], 'sit_fold_arms': [1, 45],
           'sit_elbows': [1, 45], 'sit_talk_left': [26, 50], 'sit_point_left': [18], 'sit_board_move': [14, 22, 40, 48, 76]}

argv = sys.argv[sys.argv.index('--') + 1:]
rig_path, out, name = argv[0], argv[1], argv[2]
make, mirror = CLIPS[name]
c = Clip(name, rig_path, mirror=mirror)
make(c)
c.bake()
loop = name in LOOPS
c.export(out, loop=loop, blend_in=0.8 if loop else 0.5, blend_out=0.8 if loop else 0.5)
if 'render' in argv:
    c.render(out, PREVIEW.get(name, [1]), views=('front', 'lthree' if mirror or name.endswith('left') else 'three', 'side'),
             stand_ins=[('RightHand', TANKARD, (0.065, 0.17))] if 'toast' in name else (), table=TABLE if 'board' in name else None)
