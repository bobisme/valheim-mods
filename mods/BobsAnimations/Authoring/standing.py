# The standing set, over a villager captured standing idle (rig-standing.json): blender -b --python standing.py -- <rig json> <out dir> <clip> [render]
# Fishing (a loop holding the rod out, a cast and a reel-in that start and end there), carrying in both arms while walking, sowing and
# harvesting crouched, keeping watch with a hand over the eyes, drumming (a loop) and reciting.
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from clip import Clip, hold_item, X, Y, Z
from mathutils import Vector, Matrix
import math

# How the game holds these (`anim hold`, the hand bone's scale 85.5): each one's centre, and its long or up axis, in the hand's frame.
DRUM = hold_item((-0.0001, 0.0021, -0.0002), (1, 0, 0), 85.5)   # JarldomDrum on LeftHand_Attach
FISH = hold_item((0.0007, 0.0012, -0.0004), (0, 0, 1), 85.5)     # Fish1 on LeftHand_Attach: its length runs across the palm
DOWN_HAND = ((0, 1, -0.4), (0, 0.2, -1))

def grip(c, frame, side, wrist, along, fingers):
    """A fist round something long (a rod, a fish by the tail): the thing runs along the thumb (the game holds them so), pointing along."""
    a = Vector(along).normalized(); x = -a if side == 'Right' else a
    f = Vector(fingers); z = (f - f.dot(x) * x).normalized(); y = z.cross(x)
    c.hand(frame, side, Matrix((x, y, z)).transposed(), Vector(wrist))

# ---- fishing: the rod held out, its tip up and forward ----
HOLD = ((0.24, 0.40, 1.20), (0.1, 0.85, 0.5), (-1, 0, -0.2))
def rod_hold(c, f, dz=0.0, lift=0.0):
    w, a, fi = HOLD
    grip(c, f, 'Right', (w[0], w[1], w[2] + dz), (a[0], a[1], a[2] + lift), fi)
LOOK_LINE = {'Neck': [(X, -3)], 'Head': [(X, -6)]}

def fish(c):
    """Waiting for a bite: the rod held out, breathing, the tip nodding, a twitch. Loops."""
    for f, dz, lift in ((1, 0, 0), (40, 0.01, 0), (60, 0.02, 0.15), (66, 0, 0), (120, 0, 0)): rod_hold(c, f, dz, lift)
    c.curl(1, 'RightHand', 0.9, Thumb=0.5); c.curl(120, 'RightHand', 0.9, Thumb=0.5)
    for f in (1, 60, 120): c.turn(f, LOOK_LINE)

def fish_cast(c):
    """The cast: the rod back over the shoulder, a snap forward, and settling into the hold."""
    rod_hold(c, 1)
    grip(c, 12, 'Right', (0.24, -0.05, 1.55), (0, -0.4, 0.9), (-1, 0, -0.2))
    grip(c, 20, 'Right', (0.22, 0.42, 1.30), (0.1, 0.95, 0.1), (-1, 0, -0.2))
    rod_hold(c, 34); rod_hold(c, 60)
    c.curl(1, 'RightHand', 0.9, Thumb=0.5); c.curl(60, 'RightHand', 0.9, Thumb=0.5)
    c.turn(1, LOOK_LINE); c.turn(12, {'Spine2': [(X, 4), (Z, -8)], 'Head': [(X, 2)]}); c.turn(20, {'Spine2': [(X, -6)], 'Head': [(X, -8)]})
    c.turn(34, LOOK_LINE); c.turn(60, LOOK_LINE)

def fish_reel(c):
    """A bite: the rod yanked up, the fish taken off the line and held up to look at, then into the bag, and back to the hold."""
    rod_hold(c, 1)
    grip(c, 10, 'Right', (0.24, 0.15, 1.42), (0, 0.3, 0.95), (-1, 0, -0.2))
    grip(c, 40, 'Right', (0.26, 0.12, 1.38), (0.1, 0.3, 0.95), (-1, 0, -0.2))
    rod_hold(c, 66); rod_hold(c, 84)
    c.hand(1, 'Left'); c.hand(14, 'Left')
    c.hold(26, 'Left', FISH, Vector((-0.04, 0.38, 1.22)), Vector((0, 0, 1)), Vector((0, 1, 0)))    # the fish off the line, hanging
    c.hold(42, 'Left', FISH, Vector((-0.10, 0.34, 1.26)), Vector((0.2, 0, 1)), Vector((0.3, 1, 0)))  # held up to look at
    c.hold(60, 'Left', FISH, Vector((-0.28, 0.14, 0.86)), Vector((0, 0, 1)), Vector((0, 1, 0)))      # down to the bag at the hip
    c.hand(76, 'Left')
    c.curl(1, 'RightHand', 0.9, Thumb=0.5); c.curl(84, 'RightHand', 0.9, Thumb=0.5)
    c.curl(1, 'LeftHand', 0); c.curl(24, 'LeftHand', 0.95, Thumb=0.5); c.curl(62, 'LeftHand', 0.95, Thumb=0.5); c.curl(72, 'LeftHand', 0)
    c.prop('Fish1', 'LeftHand_Attach', 0.8, 2.15)
    c.turn(1, LOOK_LINE); c.turn(10, {'Spine2': [(X, 5)], 'Head': [(X, 4)]})
    c.turn(42, {'Neck': [(Z, 6)], 'Head': [(Z, 12), (X, -4)], 'Jaw': [(X, -6)]}); c.turn(60, {'Head': [(X, -10), (Z, 10)]})
    c.turn(84, LOOK_LINE)

def carry(c):
    """Carrying an armful (what a hauler picked up, given when played) on the forearms, palms up, walking. Loops; plays while walking."""
    for f, dz in ((1, 0), (15, 0.006), (30, 0)):
        c.pose(f, 'Right', (0.16, 0.30, 1.08 + dz), (0, 1, 0), (0, 0, 1))
        c.pose(f, 'Left', (-0.16, 0.30, 1.08 + dz), (0, 1, 0), (0, 0, 1))
    for h in ('RightHand', 'LeftHand'): c.curl(1, h, 0.5, Thumb=0.2); c.curl(30, h, 0.5, Thumb=0.2)
    for f in (1, 30): c.turn(f, {'Spine1': [(X, 2)], 'Spine2': [(X, 3)], 'Head': [(X, -2)]})
    c.prop('Wood', 'RightHand_Attach', at=(0, 0, -0.2), swap=True, scale=0.8)  # (moved over to sit between the hands)

def sow(c):
    """Crouching to sow: down, a seed pressed into the soil, another, and up."""
    c.pin_feet()
    c.hand(1, 'Right')
    down = (0, 0.5, -1), (0, 0.3, -1)
    # (as low as a crouch reaches: a hand about knee height, just in front: into the row)
    c.pose(18, 'Right', (0.12, 0.46, 0.48), *down); c.pose(28, 'Right', (0.12, 0.47, 0.43), *down); c.pose(38, 'Right', (0.10, 0.46, 0.50), *down)
    c.pose(50, 'Right', (0.02, 0.48, 0.44), *down); c.pose(60, 'Right', (0.03, 0.46, 0.51), *down)
    c.hand(82, 'Right')
    c.curl(1, 'RightHand', 0); c.curl(18, 'RightHand', 0.6, Index=0.2, Thumb=0.3); c.curl(60, 'RightHand', 0.6, Index=0.2, Thumb=0.3); c.curl(82, 'RightHand', 0)
    crouch = {'Spine': [(X, -18)], 'Spine1': [(X, -24)], 'Spine2': [(X, -18)], 'Neck': [(X, 4)], 'Head': [(X, -6)]}
    c.turn(1, {}, hips=(0, 0, 0)); c.turn(18, crouch, hips=(0, 0.02, -0.44)); c.turn(60, crouch, hips=(0, 0.02, -0.44)); c.turn(82, {}, hips=(0, 0, 0))

def harvest(c):
    """Crouching to pull a crop: both hands down to it, a grip, a pull as they rise, the crop held up a moment."""
    c.pin_feet()
    for s in ('Right', 'Left'): c.hand(1, s); c.hand(76, s)
    def both(f, w, fi, pa):
        c.pose(f, 'Right', w, fi, pa); c.pose(f, 'Left', (-w[0], w[1], w[2]), (-fi[0], fi[1], fi[2]), (-pa[0], pa[1], pa[2]))
    both(16, (0.08, 0.46, 0.48), (-0.3, 0.3, -1), (-1, 0, -0.2))
    both(26, (0.06, 0.46, 0.44), (-0.3, 0.3, -1), (-1, 0, -0.2))
    both(42, (0.05, 0.38, 0.58), (-0.3, 0.2, 1), (-1, 0, 0))
    both(56, (0.06, 0.30, 1.0), (-0.3, 0.2, 1), (-1, 0, 0))
    for h in ('RightHand', 'LeftHand'): c.curl(1, h, 0); c.curl(16, h, 0.2); c.curl(26, h, 1, Thumb=0.6); c.curl(58, h, 1, Thumb=0.6); c.curl(70, h, 0)
    crouch = {'Spine': [(X, -18)], 'Spine1': [(X, -24)], 'Spine2': [(X, -16)], 'Neck': [(X, 4)], 'Head': [(X, -6)]}
    c.turn(1, {}, hips=(0, 0, 0)); c.turn(16, crouch, hips=(0, 0.02, -0.44)); c.turn(26, crouch, hips=(0, 0.02, -0.46))
    c.turn(42, {'Spine': [(X, -6)], 'Spine1': [(X, -8)], 'Spine2': [(X, -4)]}, hips=(0, 0.02, -0.18)); c.turn(56, {'Head': [(X, -6)]}, hips=(0, 0, 0)); c.turn(76, {}, hips=(0, 0, 0))

def watch(c):
    """Keeping watch: a hand over the eyes, looking slowly left and right, and down again."""
    c.hand(1, 'Right')
    shade = ((0.12, 0.16, 1.72), (-1, 0.25, 0.05), (0, -0.1, -1))
    for f in (18, 110): c.pose(f, 'Right', *shade)
    c.hand(128, 'Right')
    c.curl(1, 'RightHand', 0); c.curl(18, 'RightHand', 0.15, Thumb=0.3); c.curl(110, 'RightHand', 0.15, Thumb=0.3); c.curl(128, 'RightHand', 0)
    c.turn(1, {}); c.turn(18, {'Head': [(X, 3)]})
    c.turn(45, {'Spine2': [(Z, 10)], 'Neck': [(Z, 8)], 'Head': [(Z, 26), (X, 3)]})
    c.turn(80, {'Spine2': [(Z, -10)], 'Neck': [(Z, -8)], 'Head': [(Z, -26), (X, 3)]})
    c.turn(110, {'Head': [(X, 2)]}); c.turn(140, {})

def drum(c):
    """Drumming: the drum under the left arm at the hip, the right hand beating it, a double beat, the head nodding. Loops."""
    face = Vector((0.8, 0.4, 0.25))
    for f in (1, 48): c.hold(f, 'Left', DRUM, Vector((-0.16, 0.24, 1.06)), face, Vector((-1, -0.3, 0)))
    up, hit = ((-0.02, 0.27, 1.24), (0.2, 1, 0.6), (-0.6, 0.3, -0.5)), ((-0.04, 0.29, 1.10), (0.2, 1, -0.2), (-0.8, 0.2, -0.3))
    for f, k in ((1, up), (8, hit), (14, up), (20, hit), (26, hit), (36, up), (42, hit), (48, up)): c.pose(f, 'Right', *k)
    c.curl(1, 'RightHand', 0.15); c.curl(48, 'RightHand', 0.15); c.curl(1, 'LeftHand', 0.7, Thumb=0.4); c.curl(48, 'LeftHand', 0.7, Thumb=0.4)
    c.prop('JarldomDrum', 'LeftHand_Attach')
    for f, nod in ((1, 0), (8, -4), (14, 0), (20, -3), (26, -5), (36, 0), (42, -4), (48, 0)): c.turn(f, {'Head': [(X, nod)], 'Spine2': [(X, nod * 0.3)]})

def recite(c):
    """A skald reciting without a drum: arms open, a fist to the chest, a sweep out to the hall, both raised, the jaw working."""
    for s in ('Right', 'Left'): c.hand(1, s); c.hand(118, s)
    open_r = ((0.38, 0.25, 1.25), (0.6, 0.8, 0.2), (0.3, 0, 1))
    c.pose(20, 'Right', *open_r); c.pose(20, 'Left', (-0.38, 0.25, 1.25), (-0.6, 0.8, 0.2), (-0.3, 0, 1))
    c.pose(40, 'Right', (0.08, 0.18, 1.32), (-1, 0, 0), (0, -1, 0)); c.hand(40, 'Left')
    c.pose(70, 'Right', (0.50, 0.20, 1.35), (0.7, 0.6, 0.1), (0.3, 1, 0))
    c.pose(95, 'Right', (0.30, 0.15, 1.75), (0.2, 0.3, 1), (0, 1, 0)); c.pose(95, 'Left', (-0.30, 0.15, 1.75), (-0.2, 0.3, 1), (0, 1, 0))
    c.curl(1, 'RightHand', 0); c.curl(32, 'RightHand', 0.1); c.curl(40, 'RightHand', 1, Thumb=0.6); c.curl(52, 'RightHand', 0.1); c.curl(118, 'RightHand', 0)
    c.turn(1, {})
    for f in range(12, 108, 8): c.turn(f, {'Jaw': [(X, -9 if (f // 8) % 2 else -3)], 'Head': [(X, 10 if 90 <= f <= 100 else 2)], 'Spine2': [(X, 3 if 90 <= f <= 100 else 0)]})
    c.turn(118, {})

# ---- glíma: two wrestlers facing 0.75 m apart, belt-hold ----
GRIP_R, GRIP_L = (0.20, 0.50, 0.86), (-0.20, 0.50, 0.86)   # the partner's belt at their hips (they lean in too)
GRIP = ((0, 0.6, -0.8), (0, -1, -0.2))                      # fingers down over the belt, palm in toward the body

def grapple(c):
    """The belt-hold: knees bent, leaning in, head past the partner's right shoulder, both hands on their belt; weight swayed side to
    side, a tug, a shove. Loops; the same clip on both wrestlers fits (each holds the other)."""
    c.pin_feet()
    def hold(f, dx=0.0, dy=0.0, dz=0.0):
        for side, (x, y, z) in (('Right', GRIP_R), ('Left', GRIP_L)):
            fl = (-1 if side == 'Left' else 1)
            c.pose(f, side, (x + dx, y + dy, z + dz), (GRIP[0][0] * fl, GRIP[0][1], GRIP[0][2]), (GRIP[1][0] * fl - 0.6 * fl, GRIP[1][1], GRIP[1][2]))
    lean = lambda z, x=0, t=0: {'Spine': [(X, -8)], 'Spine1': [(X, -10), (Y, z)], 'Spine2': [(X, -8 + x), (Z, t)], 'Neck': [(X, 6)], 'Head': [(X, 6), (Z, -14)]}
    keys = [(1, 0, 0, 0, (0, 0, -0.14), 0, 0), (20, 0.05, 0, 0, (0.05, 0, -0.16), 6, 0), (40, -0.05, 0.02, 0.02, (-0.05, 0.02, -0.15), -6, -3),
            (52, 0.0, -0.05, 0.03, (0, -0.04, -0.13), 0, 5), (64, 0.0, 0.04, -0.02, (0, 0.04, -0.17), 0, -5), (90, 0, 0, 0, (0, 0, -0.14), 0, 0)]
    for f, dx, dy, dz, hips, sway, push in keys:
        hold(f, dx, dy, dz)
        c.turn(f, lean(sway, x=push * 0.6, t=sway * 0.5), hips=hips)
    for h in ('RightHand', 'LeftHand'): c.curl(1, h, 0.95, Thumb=0.6); c.curl(90, h, 0.95, Thumb=0.6)

def throw(c):
    """The winner's throw from the belt-hold: a dip, a hip turn to the left with a heave of both arms up and across, the grip let go,
    and standing tall."""
    c.pin_feet()
    def both(f, r, l, fi=GRIP[0], pa=GRIP[1]):
        c.pose(f, 'Right', r, fi, (pa[0] - 0.6, pa[1], pa[2])); c.pose(f, 'Left', l, (-fi[0], fi[1], fi[2]), (-pa[0] + 0.6, pa[1], pa[2]))
    both(1, GRIP_R, GRIP_L)
    both(10, (0.20, 0.48, 0.78), (-0.20, 0.48, 0.78))
    both(20, (-0.05, 0.42, 1.25), (-0.40, 0.25, 1.15), (-0.6, 0.6, 0.3), (0, -1, 0))
    both(28, (-0.25, 0.30, 1.10), (-0.50, 0.05, 1.0), (-0.8, 0.2, -0.3), (0, -1, 0))
    c.hand(48, 'Right'); c.hand(48, 'Left')
    for h in ('RightHand', 'LeftHand'): c.curl(1, h, 0.95, Thumb=0.6); c.curl(24, h, 0.95, Thumb=0.6); c.curl(30, h, 0.2); c.curl(48, h, 0.1)
    lean = {'Spine': [(X, -8)], 'Spine1': [(X, -10)], 'Spine2': [(X, -8)], 'Neck': [(X, 6)], 'Head': [(X, 6), (Z, -14)]}
    c.turn(1, lean, hips=(0, 0, -0.14)); c.turn(10, lean, hips=(0, 0.02, -0.22))
    c.turn(20, {'Spine': [(Z, 20)], 'Spine1': [(Z, 15), (X, 4)], 'Spine2': [(Z, 10), (X, 6)], 'Head': [(Z, 20)]}, hips=(0, 0, -0.06))
    c.turn(28, {'Spine': [(Z, 28)], 'Spine1': [(Z, 18), (X, -4)], 'Spine2': [(Z, 10)], 'Head': [(Z, 15), (X, -8)]}, hips=(0, 0, -0.10))
    c.turn(48, {'Head': [(X, -4)]}, hips=(0, 0, 0))

def thrown(c):
    """The loser, thrown: the grip torn away, a lurch back and to the side, arms flung out for balance, a stumble down into a crouch,
    and up again."""
    c.pin_feet()
    for s, g in (('Right', GRIP_R), ('Left', GRIP_L)):
        fl = -1 if s == 'Left' else 1
        c.pose(1, s, g, (GRIP[0][0] * fl, GRIP[0][1], GRIP[0][2]), (GRIP[1][0] * fl - 0.6 * fl, GRIP[1][1], GRIP[1][2]))
    c.pose(14, 'Right', (0.55, 0.10, 1.45), (0.9, 0.2, 0.4), (0, 1, 0)); c.pose(14, 'Left', (-0.55, 0.0, 1.30), (-0.9, 0.2, 0.3), (0, 1, 0))
    c.pose(28, 'Right', (0.40, 0.30, 0.50), (0.2, 0.5, -1), (0, 0.3, -1)); c.pose(28, 'Left', (-0.35, 0.25, 0.55), (-0.2, 0.5, -1), (0, 0.3, -1))
    c.pose(44, 'Right', (0.38, 0.28, 0.52), (0.2, 0.5, -1), (0, 0.3, -1)); c.pose(44, 'Left', (-0.35, 0.25, 0.56), (-0.2, 0.5, -1), (0, 0.3, -1))
    c.hand(70, 'Right'); c.hand(70, 'Left')
    for h in ('RightHand', 'LeftHand'): c.curl(1, h, 0.9, Thumb=0.5); c.curl(8, h, 0.1); c.curl(70, h, 0.1)
    c.turn(1, {'Spine': [(X, -8)], 'Spine1': [(X, -10)], 'Spine2': [(X, -8)], 'Head': [(X, 6), (Z, -14)]}, hips=(0, 0, -0.14))
    c.turn(14, {'Spine': [(X, 10), (Y, -12)], 'Spine1': [(X, 12), (Y, -8)], 'Spine2': [(X, 8)], 'Head': [(X, 14)]}, hips=(0.10, -0.12, -0.10))
    c.turn(28, {'Spine': [(X, -16)], 'Spine1': [(X, -18)], 'Spine2': [(X, -10)], 'Head': [(X, -10)]}, hips=(0.05, -0.06, -0.42))
    c.turn(44, {'Spine': [(X, -14)], 'Spine1': [(X, -16)], 'Spine2': [(X, -8)], 'Head': [(X, 4)]}, hips=(0.05, -0.06, -0.40))
    c.turn(70, {}, hips=(0, 0, 0))

CLIPS = {'stand_fish': fish, 'stand_fish_cast': fish_cast, 'stand_fish_reel': fish_reel, 'walk_carry': carry, 'stand_sow': sow,
         'stand_harvest': harvest, 'stand_watch': watch, 'stand_drum': drum, 'stand_recite': recite,
         'stand_grapple': grapple, 'stand_throw': throw, 'stand_thrown': thrown}
LOOPS = {'stand_fish', 'walk_carry', 'stand_drum', 'stand_grapple'}
PREVIEW = {'stand_fish': [1, 60], 'stand_fish_cast': [12, 20, 34], 'stand_fish_reel': [10, 42, 60], 'walk_carry': [1], 'stand_sow': [18, 28, 50],
           'stand_harvest': [26, 42, 56], 'stand_watch': [18, 45, 80], 'stand_drum': [1, 8, 20], 'stand_recite': [20, 40, 70, 95],
           'stand_grapple': [1, 40, 64], 'stand_throw': [10, 20, 28], 'stand_thrown': [14, 28, 60]}

argv = sys.argv[sys.argv.index('--') + 1:]
rig_path, out, name = argv[0], argv[1], argv[2]
c = Clip(name, rig_path)
CLIPS[name](c)
c.bake()
loop = name in LOOPS
c.export(out, loop=loop, blend_in=0.4 if name != 'walk_carry' else 0.3, blend_out=0.4, seated=False, still=name != 'walk_carry', moving=name == 'walk_carry')
if 'render' in argv:
    c.render(out, PREVIEW.get(name, [1]), views=('front', 'three', 'side'),
             stand_ins=[('LeftHand', DRUM, (0.12, 0.18))] if name == 'stand_drum' else ())
