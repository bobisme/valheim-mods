# Orbital Uplink (Wrong Universe): a rugged handheld designator. Held by the grip at the origin; stands along +Z (the game's up),
# its screen and buttons face +Y (the game's forward), the emitter dish on top, a flip-up antenna at the back.
# Run: blender -b --python ~/.cache/wu-models/wm-sky/uplink.py -- render
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from wulib import *

def sphere(name, r, at, material, axis='z', verts=16, rings=8):
    return lathe(name, [(r * math.sin(math.pi * k / rings), -r * math.cos(math.pi * k / rings)) for k in range(rings + 1)], at, material, axis, verts)

reset()
# --- grip: rubber with finger grooves on the front, a gunmetal pommel and a lanyard loop --------------------------------------
grip = [(-0.022, -0.074), (0.020, -0.074), (0.025, -0.060), (0.020, -0.046), (0.026, -0.032), (0.020, -0.018), (0.026, -0.004),
        (0.021, 0.010), (0.026, 0.024), (0.023, 0.040), (0.024, 0.066), (-0.025, 0.066), (-0.027, 0.020), (-0.025, -0.040)]
extrude('body.grip', grip, 0.042, (0, 0, 0), 'rubber', plane='yz', bevel=0.006, segments=2)
for k in range(5):
    box(f'body.ridge{k}', (0.044, 0.004, 0.003), (0, -0.026, -0.05 + k * 0.022), 'black', bevel=0)
box('body.pommel', (0.05, 0.056, 0.014), (0, -0.001, -0.08), 'gunmetal', bevel=0.004)
torus('body.lanyard', 0.009, 0.0022, (0, -0.012, -0.092), 'chrome', axis='x', verts=20, ring=6)
# trigger and guard on the front, under the housing
extrude('body.trigger', [(0.0, 0.0), (0.012, 0.004), (0.016, 0.016), (0.01, 0.026), (0.0, 0.03)], 0.012, (0, 0.024, 0.026), 'red', plane='yz', bevel=0.002)
extrude('body.guard', [(0.0, -0.004), (0.036, -0.004), (0.04, 0.0), (0.04, 0.05), (0.034, 0.05), (0.034, 0.002), (0.0, 0.002)], 0.01, (0, 0.012, 0.012), 'gunmetal', plane='yz', bevel=0.0015)

# --- housing: a white shell with a sunken screen, framed by gunmetal side rails ----------------------------------------------
shell = box('body.shell', (0.086, 0.06, 0.17), (0, 0, 0.152), 'white', bevel=0.012, segments=3)
cut(shell, box('screencut', (0.062, 0.02, 0.07), (0, 0.037, 0.182), 'white', bevel=0.004))
cut(shell, box('keycut', (0.066, 0.02, 0.024), (0, 0.037, 0.118), 'white', bevel=0.003))
box('body.screen', (0.06, 0.003, 0.068), (0, 0.0285, 0.182), 'emit_cyan', bevel=0.002)
# screen grid lines: a crosshair and a ring on the screen, sitting a hair proud
box('body.cross1', (0.04, 0.0015, 0.0018), (0, 0.0305, 0.182), 'c_e8fbff_m00_g60_e300', bevel=0)
box('body.cross2', (0.0018, 0.0015, 0.04), (0, 0.0305, 0.182), 'c_e8fbff_m00_g60_e300', bevel=0)
torus('body.reticle', 0.012, 0.0009, (0, 0.0303, 0.182), 'c_e8fbff_m00_g60_e300', axis='y', verts=28, ring=4)
box('body.keypad', (0.064, 0.004, 0.022), (0, 0.028, 0.118), 'black', bevel=0.002)
for k, c in enumerate(('red', 'hazard', 'emit_green')):
    cyl(f'body.key{k}', 0.0065, 0.008, (-0.02 + k * 0.02, 0.031, 0.118), c, axis='y', verts=16, bevel=0.0015)
for sx in (1, -1):
    box(f'body.rail{sx}', (0.008, 0.066, 0.178), (sx * 0.044, 0, 0.152), 'gunmetal', bevel=0.003)
    for k in range(4):
        box(f'body.vent{sx}_{k}', (0.004, 0.034, 0.004), (sx * 0.0485, -0.006, 0.12 + k * 0.016), 'black', bevel=0)
    box(f'body.bumper{sx}', (0.014, 0.07, 0.03), (sx * 0.042, 0, 0.074), 'rubber', bevel=0.006)
# hazard band round the base of the housing, chevrons on the back
box('body.band', (0.088, 0.062, 0.012), (0, 0, 0.088), 'hazard', bevel=0.003)
for k in range(3):
    box(f'body.chev{k}', (0.05, 0.003, 0.008), (0, -0.0315, 0.13 + k * 0.03), 'hazard', bevel=0.001)
box('body.serial', (0.03, 0.003, 0.008), (0.012, -0.0315, 0.215), 'black', bevel=0)
# a turn knob on the side and status lights on the top shoulder
cyl('body.knob', 0.011, 0.012, (-0.052, 0.0, 0.2), 'gunmetal', axis='x', verts=18, bevel=0.002)
cyl('body.knobcap', 0.006, 0.004, (-0.059, 0.0, 0.2), 'hazard', axis='x', verts=14, bevel=0.001)
for k, c in enumerate(('emit_red', 'emit_yellow', 'emit_green')):
    sphere(f'body.led{k}', 0.0035, (0.026 - k * 0.009, 0.024, 0.236), c, verts=10, rings=5)

# --- emitter head: a collar, a white dish, a chrome prong with a glowing focus, a glow ring in the dish --------------------
lathe('body.collar', [(0.0, 0.0), (0.03, 0.0), (0.034, 0.004), (0.034, 0.016), (0.03, 0.02), (0.0, 0.02)], (0, 0, 0.236), 'gunmetal', axis='z', verts=32)
for k in range(8):
    b = math.radians(k * 45)
    box(f'body.grip{k}', (0.004, 0.006, 0.012), (0.035 * math.cos(b), 0.035 * math.sin(b), 0.246), 'black', bevel=0, rot=(0, 0, k * 45))
lathe('body.dish', [(0.0, 0.0), (0.031, 0.0), (0.045, 0.012), (0.058, 0.034), (0.06, 0.037), (0.055, 0.038), (0.042, 0.022), (0.028, 0.011), (0.012, 0.008), (0.0, 0.008)],
      (0, 0, 0.256), 'white', axis='z', verts=40)
torus('body.dishrim', 0.0585, 0.0028, (0, 0, 0.2935), 'gunmetal', axis='z', verts=40, ring=6)
torus('body.dishglow', 0.028, 0.0022, (0, 0, 0.274), 'glow_cyan', axis='z', verts=32, ring=6)
cyl('body.prong', 0.0042, 0.05, (0, 0, 0.289), 'chrome', axis='z', verts=12, bevel=0)
for k in range(3):
    b = math.radians(k * 120)
    cyl(f'body.strut{k}', 0.0018, 0.06, (0.026 * math.cos(b), 0.026 * math.sin(b), 0.296), 'steel', axis='z', verts=8, bevel=0,
        rot=(math.degrees(math.sin(b)) * 0.0 + 38 * math.sin(b), -38 * math.cos(b), 0))
sphere('body.focus', 0.0095, (0, 0, 0.318), 'glow_cyan', verts=18, rings=9)
torus('body.focusring', 0.012, 0.0018, (0, 0, 0.318), 'chrome', axis='z', verts=20, ring=6)

# --- flip-up antenna at the back: hinge block, rubber sheath, chrome whip, red tip -------------------------------------------
box('body.hinge', (0.014, 0.016, 0.022), (0.053, -0.018, 0.212), 'gunmetal', bevel=0.003)
cyl('body.hingepin', 0.0042, 0.018, (0.053, -0.018, 0.216), 'chrome', axis='y', verts=12, bevel=0)
t = math.radians(16); d = (math.sin(t), 0, math.cos(t)); base = (0.054, -0.018, 0.222)
at = lambda k: (base[0] + d[0] * k, base[1], base[2] + d[2] * k)
cyl('body.sheath', 0.0055, 0.05, at(0.025), 'rubber', axis='z', verts=12, bevel=0.0015, rot=(0, 16, 0))
cyl('body.whip', 0.0026, 0.17, at(0.13), 'chrome', axis='z', verts=10, bevel=0, rot=(0, 16, 0))
sphere('body.tip', 0.0055, at(0.218), 'glow_red', verts=12, rings=6)
finish('uplink')
