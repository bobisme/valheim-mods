# Gungnir (Godforged), Odin's spear, made by the sons of Ivaldi: a dwarf's work, not a man's. Its head is huge, as Odin's is on the
# Vendel helmet plates (a long broad leaf, a strong midrib, nearly a quarter of the spear), forged with a diamond section so its edges are
# keen and its rib stands proud, runes glowing gold along the rib, lugs at its foot; a long eight-sided socket; and an iron-bound shaft:
# dark ash under iron langets, an iron band wound down its length, a wrapped grip and an iron spike at the butt.
# Blender frame: the shaft along Z with the hand at the origin, the point at the bottom (-Z) (the game frame: along y, point at -y;
# Gungnir.cs turns it into the hand).
# Run: blender -b --python $PWD/mods/Godforged/Models/gungnir.py -- render
import sys, os, math; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wulib import *
from wulib import _obj, _bevel
from gadlib import *

reset()
ASH = 'c_2e2219_m00_g30'       # dark, near-black ash
IRON = 'c_3b3e44_m85_g45'      # dark forged iron
EDGE = 'c_c3c9cf_m95_g85'      # the head's ground edges, bright
BLADE = 'c_6d747c_m90_g65'     # the head's faces: dark blued steel, lighter than the iron so its facets read
GOLD = 'c_c99a35_m90_g70'
LEATHER = 'c_2a1c12_m00_g25'
BUTT, SOCKET_TOP, SOCKET_END = 0.62, -1.0, -1.28
HEAD_FOOT, TIP = -1.22, -1.97

# --- the head: angular and barbed, as on the Vendel plates. A long kite with straight edges from hard shoulders to the point, each
# station a diamond (edge, rib, edge, rib) so its edges are keen; barbs swept back from the shoulders; and the shaft running on up
# through it as a round iron spine, tapering out four-fifths of the way to the point ---
L = HEAD_FOOT - TIP
SHOULDER = 0.13                                            # how far up the head (as a share of it) the shoulders are
def half_at(f):
    if f <= SHOULDER: return 0.035 + (0.118 - 0.035) * f / SHOULDER
    if f <= 0.62: return 0.118 + (0.062 - 0.118) * (f - SHOULDER) / (0.62 - SHOULDER)   # (a second, steeper taper: angular, not a leaf)
    return 0.062 * (1 - f) / (1 - 0.62)
# The head is one forged piece: its whole outline (the neck, barbs swept back from the shoulders, straight edges to the point) filled
# and raised from its edges to a ridge, so the barbs have the same keen bevel as the rest and nothing looks set on.
from mathutils import geometry
ZS = HEAD_FOOT - L * SHOULDER
RIGHT = [(0.03, HEAD_FOOT - 0.005), (0.072, HEAD_FOOT - 0.062), (0.146, HEAD_FOOT + 0.02), (0.118, ZS),
         (0.062, HEAD_FOOT - L * 0.62)]
def chaikin(pts, rounds=3):
    """Round off a line's corners (its ends stay put): 'almost barbed', hooks swept back in smooth curves, not points."""
    for _ in range(rounds):
        out = [pts[0]]
        for (ax, az), (bx, bz) in zip(pts, pts[1:]):
            out += [(0.75 * ax + 0.25 * bx, 0.75 * az + 0.25 * bz), (0.25 * ax + 0.75 * bx, 0.25 * az + 0.75 * bz)]
        out.append(pts[-1]); pts = out
    return pts
RIGHT = chaikin(RIGHT + [(0.0, TIP)])[:-1]
OUTLINE = RIGHT + [(0.0, TIP)] + [(-x, z) for x, z in reversed(RIGHT)]
def seg_dist(p, a, b):
    ax, az = a; bx, bz = b; px, pz = p
    dx, dz = bx - ax, bz - az; t = max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / (dx * dx + dz * dz)))
    return math.hypot(px - ax - t * dx, pz - az - t * dz)
def edge_dist(p):
    return min(seg_dist(p, OUTLINE[i], OUTLINE[(i + 1) % len(OUTLINE)]) for i in range(len(OUTLINE)))
def rib_at(z):
    f = max(0.0, min(1.0, (HEAD_FOOT - z) / L))
    return 0.015 * (1 - f) ** 0.7 + 0.002
def height(x, z):
    return min(edge_dist((x, z)) * 0.33, rib_at(z))
def inside(p):
    x, z = p; c = False; n = len(OUTLINE)
    for i in range(n):
        (ax, az), (bx, bz) = OUTLINE[i], OUTLINE[(i + 1) % n]
        if (az > z) != (bz > z) and x < ax + (z - az) * (bx - ax) / (bz - az): c = not c
    return c

def head(name):
    pts, edges = [], []
    # the outline, a point every 5 mm
    for i in range(len(OUTLINE)):
        a, b = OUTLINE[i], OUTLINE[(i + 1) % len(OUTLINE)]
        n = max(1, int(math.hypot(b[0] - a[0], b[1] - a[1]) / 0.005))
        for k in range(n): pts.append((a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n))
    nb = len(pts)
    edges = [(i, (i + 1) % nb) for i in range(nb)]
    # rings following the outline in from it (so the bevel runs parallel to the edge, and the bright ground edge ends on a clean line)
    outline_pts = pts[:nb]
    for d in (0.0045, 0.009, 0.0135, 0.019):
        for i in range(nb):
            (ax, az), (bx, bz) = outline_pts[i - 1], outline_pts[(i + 1) % nb]
            tx, tz = bx - ax, bz - az; ln = math.hypot(tx, tz) or 1.0
            for sgn in (1, -1):
                p = (outline_pts[i][0] - sgn * tz / ln * d, outline_pts[i][1] + sgn * tx / ln * d)
                if inside(p) and edge_dist(p) > d * 0.85: pts.append(p); break
    # inside those, a grid every 6 mm
    zs_ = [HEAD_FOOT + 0.03 - k * 0.006 for k in range(int((HEAD_FOOT + 0.03 - TIP) / 0.006))]
    for z in zs_:
        for k in range(-26, 27):
            p = (k * 0.006, z)
            if inside(p) and edge_dist(p) > 0.023: pts.append(p)
    vco, eout, fout, _, _, _ = geometry.delaunay_2d_cdt([Vector((x, z)) for x, z in pts], edges, [], 2, 1e-6)
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    top, bot = [], []
    for v in vco:
        x, z = v.x, v.y
        h = 0.0 if not inside((x, z)) else height(x, z)
        t = bm.verts.new((x, h, z)); top.append(t)
        bot.append(t if h < 1e-5 else bm.verts.new((x, -h, z)))
    edge_faces, blade_faces = [], []
    for f in fout:
        cx = sum(vco[i].x for i in f) / 3; cz = sum(vco[i].y for i in f) / 3
        if not inside((cx, cz)): continue
        try:
            ft = bm.faces.new([top[i] for i in f]); fb = bm.faces.new([bot[i] for i in reversed(f)])
        except ValueError:
            continue
        (edge_faces if edge_dist((cx, cz)) < 0.009 else blade_faces).extend([ft, fb])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    ob = _obj(name, me, BLADE)
    ob.data.materials.append(mat(EDGE))
    keen = set(f.index for f in [])   # (indices change in to_mesh: mark the ground edge by where each face lies instead)
    vs = ob.data.vertices
    for poly in ob.data.polygons:   # the ground edge: the band between the outline and the first ring in (every corner in it), clean
        if all(edge_dist((vs[i].co.x, vs[i].co.z)) < 0.0052 for i in poly.vertices): poly.material_index = 1
    return ob

blade = head('spear.head')
# runes down the blade either side of the spine, both faces, glowing gold, sitting on its surface
for y in (-1, 1):
    for side in (-1, 1):
        for k in range(8):
            f = 0.16 + k * 0.065; z = HEAD_FOOT - L * f
            x = side * (0.03 + 0.3 * (half_at(f) - 0.03) * 0.5)
            box('glow.rune', (0.005, 0.002, 0.024), (x, y * (height(x, z) + 0.0008), z), 'glow_yellow', bevel=0,
                rot=(0, (25 if k % 2 else -25) * side, 0))
# the spine: the shaft's line carried on up through the head, round and dark, ringed in gold where it leaves the socket
lathe('spear.spine', [(0.0, HEAD_FOOT + 0.03), (0.021, HEAD_FOOT + 0.03), (0.019, HEAD_FOOT - 0.1), (0.014, HEAD_FOOT - 0.35),
                      (0.008, HEAD_FOOT - L * 0.8), (0.0, HEAD_FOOT - L * 0.82)], material=IRON, axis='z', verts=12)
torus('spear.band', 0.022, 0.0035, (0, 0, HEAD_FOOT + 0.01), GOLD, axis='z', verts=12, ring=4)
# --- the socket: long and eight-sided, banded, riveted ---
lathe('spear.socket', [(0.0, SOCKET_TOP), (0.028, SOCKET_TOP), (0.031, SOCKET_TOP - 0.02), (0.027, SOCKET_TOP - 0.14), (0.024, SOCKET_END + 0.02),
                       (0.03, SOCKET_END), (0.0, SOCKET_END)], material=IRON, axis='z', verts=8)
for z in (SOCKET_TOP - 0.012, SOCKET_TOP - 0.15, SOCKET_END + 0.01):
    torus('spear.band', 0.031 if z > SOCKET_TOP - 0.05 else 0.028, 0.0045, (0, 0, z), GOLD, axis='z', verts=8, ring=4, rot=(0, 0, 22.5))
for a in (0, 180):
    r = math.radians(a)
    cyl('spear.rivet', 0.006, 0.01, (math.cos(r) * 0.029, math.sin(r) * 0.029, SOCKET_TOP - 0.08), IRON, axis='x', verts=10, bevel=0.002, rot=(0, 0, a))

# --- the shaft: thick dark ash, iron-bound ---
lathe('spear.shaft', [(0.0, BUTT), (0.021, BUTT), (0.022, 0.0), (0.024, SOCKET_TOP), (0.0, SOCKET_TOP)], material=ASH, axis='z', verts=16)
# four iron langets down from the socket, riveted
for a in range(4):
    r = math.radians(45 + 90 * a); c, s = math.cos(r), math.sin(r)
    box('spear.langet', (0.012, 0.004, 0.42), (c * 0.0245, s * 0.0245, SOCKET_TOP + 0.21), IRON, bevel=0.0015, rot=(0, 0, math.degrees(r) + 90))
    for z in (SOCKET_TOP + 0.06, SOCKET_TOP + 0.2, SOCKET_TOP + 0.36):
        cyl('spear.rivet', 0.0032, 0.004, (c * 0.0275, s * 0.0275, z), IRON, axis='z', verts=8, bevel=0.001).rotation_euler = (math.pi / 2, 0, r + math.pi / 2)
# an iron band wound down the shaft from the langets' ends to the grip
helix = []
for k in range(160):
    f = k / 159; z = SOCKET_TOP + 0.43 + f * (-0.12 - SOCKET_TOP - 0.43); a = f * 2 * math.pi * 7
    rr = 0.0235 - 0.001 * f
    helix.append((math.cos(a) * rr, math.sin(a) * rr, z))
tube_path('spear.wind', helix, 0.0026, IRON, ring=6)
# the grip: leather wound between two iron collars
for z in (-0.12, 0.13):
    lathe('spear.collar', [(0.0, z + 0.012), (0.026, z + 0.012), (0.027, z), (0.026, z - 0.012), (0.0, z - 0.012)], material=IRON, axis='z', verts=12)
for i in range(11):
    torus('spear.grip', 0.0225, 0.0035, (0, 0, 0.105 - i * 0.021), LEATHER, axis='z', verts=16, ring=5, rot=(8, 0, 0))
# the butt: an iron shoe and a four-sided spike
lathe('spear.shoe', [(0.0, BUTT - 0.06), (0.024, BUTT - 0.06), (0.026, BUTT), (0.02, BUTT + 0.03), (0.0, BUTT + 0.03)], material=IRON, axis='z', verts=12)
lathe('spear.spike', [(0.0, BUTT + 0.03), (0.016, BUTT + 0.03), (0.0, BUTT + 0.14)], material=IRON, axis='z', verts=4)

report(12)
finish('gungnir')
