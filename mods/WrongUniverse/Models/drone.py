# Combat Drone (Wrong Universe): one model for the flying drone (WU_DroneBot, scale 1) and the item (scale 0.8).
# Blender frame: +Y forward (the eye), +Z up. Parts: "body" and "rotor0".."rotor3" (each turns about its hub).
# Run: blender -b --python ~/.cache/wu-models/wm-sky/drone.py -- render
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); from wulib import *

def sphere(name, r, at, material, axis='y', verts=18, rings=9):
    return lathe(name, [(r * math.sin(math.pi * k / rings), -r * math.cos(math.pi * k / rings)) for k in range(rings + 1)], at, material, axis, verts)

reset()
# --- hull: a white top shell over a gunmetal belly, seen from above a long octagon -------------------------------------------
extrude('body.shell', [(x, y) for x, y in [(0.06, -0.13), (0.10, -0.075), (0.11, 0.0), (0.10, 0.075), (0.06, 0.13), (-0.06, 0.13), (-0.10, 0.075), (-0.11, 0.0), (-0.10, -0.075), (-0.06, -0.13)]],
        0.05, (0, 0, 0.004), 'white', plane='xy', bevel=0.014, segments=2)
extrude('body.belly', [(x * 0.86, y * 0.9) for x, y in [(0.06, -0.13), (0.10, -0.075), (0.11, 0.0), (0.10, 0.075), (0.06, 0.13), (-0.06, 0.13), (-0.10, 0.075), (-0.11, 0.0), (-0.10, -0.075), (-0.06, -0.13)]],
        0.03, (0, 0, -0.03), 'gunmetal', plane='xy', bevel=0.008)
# seam between shell and belly: a dark inset band
extrude('body.seam', [(x * 0.95, y * 0.96) for x, y in [(0.06, -0.13), (0.10, -0.075), (0.11, 0.0), (0.10, 0.075), (0.06, 0.13), (-0.06, 0.13), (-0.10, 0.075), (-0.11, 0.0), (-0.10, -0.075), (-0.06, -0.13)]],
        0.012, (0, 0, -0.016), 'black', plane='xy', bevel=0.003)
# canopy: a low carbon dome, stretched front to back, with a white collar
can = lathe('body.canopy', [(0.07, 0), (0.069, 0.01), (0.062, 0.021), (0.046, 0.032), (0.024, 0.039), (0.0, 0.041)], (0, 0.012, 0.024), 'carbon', axis='z', verts=32)
can.scale = (1, 1.28, 1)
col = torus('body.collar', 0.07, 0.005, (0, 0.012, 0.027), 'offwhite', axis='z', verts=36, ring=7); col.scale = (1, 1.28, 1)
# sensor stripe on the canopy's nose
box('body.sensor', (0.03, 0.006, 0.008), (0, 0.098, 0.036), 'emit_cyan', bevel=0.002, rot=(-28, 0, 0))
# heat sink fins behind the canopy
for k in range(5):
    box(f'body.fin{k}', (0.004, 0.034, 0.014), (-0.032 + k * 0.016, -0.098, 0.034), 'gunmetal', bevel=0)
# hazard stripes along the flanks, sunk a little into the shell
for sx in (1, -1):
    box(f'body.stripe{sx}', (0.004, 0.11, 0.011), (sx * 0.1105, 0.0, 0.004), 'hazard', bevel=0.0015)
    box(f'body.vent{sx}', (0.004, 0.03, 0.006), (sx * 0.093, -0.1, 0.004), 'black', bevel=0.001, rot=(0, 0, sx * -35))
# status LEDs on the tail
for k, c in enumerate(('emit_green', 'emit_cyan', 'emit_red')):
    box(f'body.led{k}', (0.009, 0.004, 0.005), (-0.014 + k * 0.014, -0.1335, 0.006), c, bevel=0.0012)
# battery pack underneath
box('body.battery', (0.085, 0.13, 0.022), (0, -0.015, -0.054), 'black', bevel=0.006)
box('body.batlabel', (0.06, 0.03, 0.002), (0, -0.04, -0.0655), 'hazard', bevel=0.0006)
# antenna
cyl('body.antenna', 0.0026, 0.075, (-0.045, -0.105, 0.068), 'chrome', axis='z', verts=12, rot=(-12, 0, 0))
cyl('body.antbase', 0.006, 0.01, (-0.045, -0.1, 0.03), 'gunmetal', axis='z', verts=16)
sphere('body.anttip', 0.0045, (-0.045, -0.113, 0.105), 'emit_red', axis='z', verts=12, rings=6)

# --- the gun eye: a ball in a yoke under the nose, red lens, twin emitter barrels ---------------------------------------
box('body.yoke', (0.074, 0.03, 0.012), (0, 0.118, -0.016), 'gunmetal', bevel=0.004)
for sx in (1, -1):
    box(f'body.yokeplate{sx}', (0.008, 0.032, 0.042), (sx * 0.035, 0.132, -0.036), 'gunmetal', bevel=0.003)
    cyl(f'body.pin{sx}', 0.007, 0.006, (sx * 0.0395, 0.138, -0.042), 'chrome', axis='x', verts=16)
sphere('body.turret', 0.029, (0, 0.14, -0.042), 'black', axis='y', verts=22, rings=16)
lathe('body.hood', [(0.03, 0), (0.031, 0.006), (0.026, 0.02), (0.02, 0.026), (0.0, 0.026)], (0, 0.15, -0.042), 'gunmetal', axis='y', verts=22)
torus('body.lensrim', 0.0165, 0.0032, (0, 0.177, -0.042), 'chrome', axis='y', verts=22, ring=10)
lathe('body.lens', [(0.0, -0.002), (0.0145, -0.002), (0.0145, 0.002), (0.008, 0.006), (0.0, 0.007)], (0, 0.175, -0.042), 'glow_red', axis='y', verts=18)
for sx in (1, -1):
    cyl(f'body.barrel{sx}', 0.0042, 0.03, (sx * 0.012, 0.172, -0.062), 'gunmetal', axis='y', verts=14)
    cyl(f'body.muzzle{sx}', 0.0026, 0.004, (sx * 0.012, 0.188, -0.062), 'glow_red', axis='y', verts=12)

# --- arms, motor pods, rotors -------------------------------------------------------------------------------------------
R = 0.24
for i in range(4):
    a = math.radians(45 + 90 * i); px, py = R * math.sin(a), R * math.cos(a)
    # arm: tapering from the hull to the pod, white over a gunmetal spine
    extrude(f'body.arm{i}', [(-0.017, 0.07), (0.017, 0.07), (0.011, 0.222), (-0.011, 0.222)], 0.016, (0, 0, 0.0), 'white', plane='xy', bevel=0.005, rot=(0, 0, -math.degrees(a)))
    extrude(f'body.spine{i}', [(-0.009, 0.08), (0.009, 0.08), (0.006, 0.215), (-0.006, 0.215)], 0.01, (0, 0, -0.011), 'gunmetal', plane='xy', bevel=0.003, rot=(0, 0, -math.degrees(a)))
    # motor pod: a turned can with a hazard band and a chrome bell on top
    lathe(f'body.pod{i}', [(0.0, -0.024), (0.016, -0.024), (0.022, -0.019), (0.024, -0.01), (0.024, 0.012), (0.021, 0.019), (0.0, 0.019)], (px, py, 0.004), 'gunmetal', axis='z', verts=22)
    lathe(f'body.band{i}', [(0.0249, -0.004), (0.0252, -0.003), (0.0252, 0.004), (0.0249, 0.005), (0.0, 0.005), (0.0, -0.004)], (px, py, 0.004), 'hazard', axis='z', verts=22)
    lathe(f'body.bell{i}', [(0.0, 0.0), (0.017, 0.0), (0.018, 0.004), (0.016, 0.013), (0.009, 0.017), (0.0, 0.018)], (px, py, 0.023), 'chrome', axis='z', verts=18)
    for k in range(6):
        b = math.radians(k * 60)
        box(f'body.slot{i}_{k}', (0.0025, 0.004, 0.012), (px + 0.0175 * math.cos(b), py + 0.0175 * math.sin(b), 0.031), 'black', bevel=0, rot=(0, 0, math.degrees(b)))
    cyl(f'body.shaft{i}', 0.003, 0.02, (px, py, 0.044), 'steel', axis='z', verts=10, bevel=0)
    sphere(f'body.navlight{i}', 0.0065, (px, py, -0.026), 'glow_green' if i < 2 else 'glow_red', axis='z', verts=12, rings=6)
    # rotor: hub cap and two tapered, twisted blades, turning about the hub
    rp = f'rotor{i}'; hub = (px, py, 0.05); pivot(rp, hub); spin = 37 * i
    lathe(f'{rp}.hub', [(0.0, -0.004), (0.011, -0.004), (0.012, 0.0), (0.009, 0.006), (0.0, 0.008)], hub, 'black', axis='z', verts=24)
    sphere(f'{rp}.cap', 0.0045, (px, py, 0.0575), 'hazard', axis='z', verts=10, rings=5)
    for side in (0, 180):
        blade = [(-0.006, 0.006), (0.009, 0.006), (0.012, 0.03), (0.011, 0.07), (0.007, 0.098), (0.0, 0.104), (-0.006, 0.098), (-0.009, 0.06), (-0.008, 0.03)]
        extrude(f'{rp}.blade{side}', blade, 0.0028, hub, 'carbon', plane='xy', bevel=0.0008, segments=1, rot=(0, 14, spin + side))
        box(f'{rp}.tip{side}', (0.012, 0.012, 0.003), (px + 0.098 * math.sin(math.radians(spin + side)) * -1, py + 0.098 * math.cos(math.radians(spin + side)), 0.05), 'hazard', bevel=0, rot=(0, 0, spin + side))

# --- landing skids ------------------------------------------------------------------------------------------------------
for sx in (1, -1):
    cyl(f'body.skid{sx}', 0.0055, 0.2, (sx * 0.075, -0.005, -0.082), 'rubber', axis='y', verts=16)
    cyl(f'body.skidnose{sx}', 0.0055, 0.04, (sx * 0.075, 0.108, -0.072), 'rubber', axis='y', verts=16, rot=(32, 0, 0))
    sphere(f'body.skidend{sx}', 0.0055, (sx * 0.075, -0.105, -0.082), 'rubber', axis='y', verts=12, rings=6)
    for sy in (0.055, -0.065):
        cyl(f'body.strut{sx}{sy}', 0.004, 0.046, (sx * 0.064, sy, -0.06), 'gunmetal', axis='z', verts=10, bevel=0, rot=(0, sx * -28, 0))
finish('drone')
