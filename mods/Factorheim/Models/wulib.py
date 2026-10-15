# Model kit for Blender (5.x), copied from Wrong Universe (mods/WrongUniverse/Models/wulib.py). A model script does:
#
#   import sys, os; sys.path.insert(0, os.path.dirname(__file__)); from wulib import *
#   reset()
#   body = box('body', (0.04, 0.2, 0.05), (0, 0, 0), 'white', bevel=0.006)        # parts are named "<part>" or "<part>.<anything>"
#   ...
#   finish('pistol')        # writes <name>.json.gz next to the script, and with "-- render" previews <name>_0..3.png and <name>_sheet.png
#
# Run (the flatpak Blender reads and writes only under ~, not /tmp):
#   blender -b --python ~/.cache/wu-models/pistol.py -- render
#
# Frames: build in Blender's frame, +Y forward, +Z up, X right, metres. The exporter turns it into the game's (+z forward, +y up).
# Parts: every mesh object belongs to the part named before the first '.' of its name ("rotor.blade1" is part "rotor"). A part's
# pivot is (0,0,0) unless pivot('rotor', (x, y, z)) says otherwise; the game puts the part there and turns it about that point.
# Materials: name slots from the palette below (the game's Look.Slot has the same table): 'white', 'gunmetal', 'glow_cyan',
# 'emit_red', or 'c_RRGGBB_m40_g60_e00' (metal, gloss, emission in hundredths).
import bpy, bmesh, math, json, gzip, os, sys
from mathutils import Vector, Matrix, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
PALETTE = {  # name: (rgb, metal, gloss, emit)
    'white': ((0.86, 0.87, 0.88), 0.1, 0.55, 0), 'offwhite': ((0.74, 0.73, 0.70), 0.1, 0.45, 0),
    'gunmetal': ((0.16, 0.17, 0.19), 0.6, 0.5, 0), 'black': ((0.05, 0.05, 0.06), 0.2, 0.35, 0),
    'rubber': ((0.035, 0.035, 0.04), 0.0, 0.12, 0), 'carbon': ((0.09, 0.09, 0.10), 0.3, 0.65, 0),
    'chrome': ((0.72, 0.74, 0.78), 0.9, 0.85, 0), 'steel': ((0.45, 0.47, 0.5), 0.75, 0.55, 0),
    'brass': ((0.66, 0.50, 0.20), 0.8, 0.6, 0), 'copper': ((0.72, 0.42, 0.22), 0.8, 0.6, 0),
    'hazard': ((0.95, 0.62, 0.08), 0.1, 0.5, 0), 'red': ((0.62, 0.08, 0.06), 0.15, 0.5, 0),
    'blue': ((0.10, 0.22, 0.55), 0.2, 0.55, 0), 'olive': ((0.22, 0.26, 0.17), 0.1, 0.35, 0),
    'lens': ((0.12, 0.28, 0.42), 0.3, 0.95, 0), 'glass': ((0.08, 0.12, 0.16), 0.5, 0.95, 0),
}
GLOWS = {'cyan': (0.2, 0.95, 1), 'red': (1, 0.12, 0.08), 'blue': (0.2, 0.55, 1), 'green': (0.25, 1, 0.3), 'violet': (0.7, 0.25, 1),
         'orange': (1, 0.45, 0.08), 'yellow': (1, 0.85, 0.2), 'white': (1, 1, 1)}
_pivots = {}

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _pivots.clear()

def _look(name):
    if name in PALETTE: return PALETTE[name]
    if name.startswith('glow_'): return (GLOWS.get(name[5:], (1, 1, 1)), 0, 0, 6)
    if name.startswith('emit_'): return (GLOWS.get(name[5:], (1, 1, 1)), 0, 0.6, 2.5)
    if name.startswith('c_'):
        h = name[2:8]; rgb = tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)); m, g, e = 0.3, 0.5, 0
        for bit in name[8:].split('_'):
            if len(bit) > 1 and bit[1:].isdigit():
                k = int(bit[1:]) / 100
                if bit[0] == 'm': m = k
                elif bit[0] == 'g': g = k
                elif bit[0] == 'e': e = k
        return (rgb, m, g, e)
    raise ValueError('unknown material slot ' + name)

def mat(name):
    m = bpy.data.materials.get(name)
    if m: return m
    rgb, metal, gloss, emit = _look(name)
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    lin = tuple(c ** 2.2 for c in rgb) + (1,)
    b.inputs['Base Color'].default_value = lin; b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = 1 - gloss
    if emit: b.inputs['Emission Color'].default_value = lin; b.inputs['Emission Strength'].default_value = emit
    return m

def _obj(name, me, material, at=(0, 0, 0), rot=(0, 0, 0)):
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    ob.location = at; ob.rotation_euler = [math.radians(a) for a in rot]
    if material: ob.data.materials.append(mat(material))
    return ob

def _bevel(ob, width, segments=2, angle=40):
    if width <= 0: return ob
    m = ob.modifiers.new('bevel', 'BEVEL'); m.width = width; m.segments = segments; m.limit_method = 'ANGLE'; m.angle_limit = math.radians(angle)
    m.harden_normals = False
    return ob

def box(name, size, at=(0, 0, 0), material='gunmetal', bevel=0.004, rot=(0, 0, 0), segments=2):
    """A box of full size (x, y, z) centred at 'at', edges rounded by 'bevel' metres."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0); bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bm.to_mesh(me); bm.free()
    return _bevel(_obj(name, me, material, at, rot), min(bevel, min(size) * 0.45), segments)

def cyl(name, radius, length, at=(0, 0, 0), material='gunmetal', axis='y', verts=32, bevel=0.002, radius2=None, rot=(0, 0, 0), segments=2):
    """A cylinder (or a cone/frustum with radius2) along an axis, centred at 'at'."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=radius, radius2=radius if radius2 is None else radius2, depth=length)
    bm.to_mesh(me); bm.free()
    turn = {'z': (0, 0, 0), 'y': (-90, 0, 0), 'x': (0, 90, 0)}[axis]
    ob = _obj(name, me, material, at, tuple(a + b for a, b in zip(turn, rot)))
    return _bevel(ob, min(bevel, radius * 0.4), segments)

def lathe(name, profile, at=(0, 0, 0), material='gunmetal', axis='y', verts=40, rot=(0, 0, 0)):
    """Turned shape: profile is [(radius, along), ...] from one end to the other (radius 0 closes an end)."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r <= 1e-5: rings.append([bm.verts.new((0, 0, z))]); continue
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / verts), r * math.sin(2 * math.pi * i / verts), z)) for i in range(verts)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1: continue
        for i in range(verts):
            j = (i + 1) % verts
            if len(a) == 1: bm.faces.new((a[0], b[i], b[j]))
            elif len(b) == 1: bm.faces.new((a[j], a[i], b[0]))
            else: bm.faces.new((a[i], a[j], b[j], b[i]))
    if len(rings[0]) > 1: bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1: bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    turn = {'z': (0, 0, 0), 'y': (-90, 0, 0), 'x': (0, 90, 0)}[axis]
    return _obj(name, me, material, at, tuple(a + b for a, b in zip(turn, rot)))

def torus(name, major, minor, at=(0, 0, 0), material='gunmetal', axis='y', verts=40, ring=12, rot=(0, 0, 0)):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    grid = [[bm.verts.new(((major + minor * math.cos(2 * math.pi * j / ring)) * math.cos(2 * math.pi * i / verts),
                            (major + minor * math.cos(2 * math.pi * j / ring)) * math.sin(2 * math.pi * i / verts),
                            minor * math.sin(2 * math.pi * j / ring))) for j in range(ring)] for i in range(verts)]
    for i in range(verts):
        for j in range(ring):
            a, b = grid[i][j], grid[(i + 1) % verts][j]; c, d = grid[(i + 1) % verts][(j + 1) % ring], grid[i][(j + 1) % ring]
            bm.faces.new((a, b, c, d))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    turn = {'z': (0, 0, 0), 'y': (-90, 0, 0), 'x': (0, 90, 0)}[axis]
    return _obj(name, me, material, at, tuple(a + b for a, b in zip(turn, rot)))

def extrude(name, outline, depth, at=(0, 0, 0), material='gunmetal', plane='yz', bevel=0.003, rot=(0, 0, 0), segments=2):
    """A flat outline [(u, v), ...] (counter-clockwise) extruded 'depth' through its plane, centred. plane 'yz': u along Y, v along Z,
    extruded along X (a gun's side profile); 'xy': extruded along Z (a plate seen from above); 'xz': extruded along Y."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    def p3(u, v, w):
        return {'yz': (w, u, v), 'xy': (u, v, w), 'xz': (u, w, v)}[plane]
    lo = [bm.verts.new(p3(u, v, -depth / 2)) for u, v in outline]
    hi = [bm.verts.new(p3(u, v, depth / 2)) for u, v in outline]
    bm.faces.new(lo); bm.faces.new(list(reversed(hi)))
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n; bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return _bevel(_obj(name, me, material, at, rot), bevel, segments)

def cut(target, cutter, keep_cutter=False):
    """Boolean difference: cutter carves target (applied now). The cutter is removed unless kept."""
    _apply(target)
    m = target.modifiers.new('cut', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.object = cutter; m.solver = 'EXACT'
    _apply(target)
    if not keep_cutter: bpy.data.objects.remove(cutter, do_unlink=True)
    return target

def join(target, *others):
    """Booleans union of meshes into target (applied), for one smooth solid."""
    for o in others:
        _apply(target)
        m = target.modifiers.new('join', 'BOOLEAN'); m.operation = 'UNION'; m.object = o; m.solver = 'EXACT'
        _apply(target); bpy.data.objects.remove(o, do_unlink=True)
    return target

def _apply(ob):
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    ob.modifiers.clear(); old = ob.data; ob.data = me
    if old.users == 0: bpy.data.meshes.remove(old)

def mirror_x(ob):
    """A copy mirrored across X (the other side of a symmetric model)."""
    c = ob.copy(); c.data = ob.data.copy(); bpy.context.scene.collection.objects.link(c)
    c.location.x = -c.location.x; c.rotation_euler.y = -c.rotation_euler.y; c.rotation_euler.z = -c.rotation_euler.z
    c.scale.x = -c.scale.x; c.name = ob.name + ('_m' if '.' in ob.name else '.m')
    return c

def pivot(part, at):
    """Where the part turns: the game puts the part's origin there (rotors, triggers, a gun's grip)."""
    _pivots[part] = Vector(at)

def finish(name, smooth_angle=35):
    export(name, smooth_angle)
    if '--' in sys.argv and 'render' in sys.argv[sys.argv.index('--'):]: preview(name)

def export(name, smooth_angle=35):
    deps = bpy.context.evaluated_depsgraph_get()
    parts = {}
    for ob in sorted(bpy.context.scene.collection.objects, key=lambda o: o.name):
        if ob.type != 'MESH': continue
        part = ob.name.split('.')[0]
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
        me.transform(ob.matrix_world)
        if ob.matrix_world.determinant() < 0: me.flip_normals()
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(smooth_angle))
        me.update()
        cn = me.corner_normals
        P = parts.setdefault(part, {'v': [], 'n': [], 'uv': [], 'slots': [], 'tris': {}, 'index': {}})
        piv = _pivots.get(part, Vector((0, 0, 0)))
        for poly in me.polygons:
            slot = me.materials[poly.material_index].name if me.materials else 'gunmetal'
            if slot not in P['slots']: P['slots'].append(slot)
            tl = P['tris'].setdefault(slot, [])
            corner = []
            for li in poly.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co - piv; n = cn[li].vector; fn = poly.normal
                u, v = (co.x, co.z) if abs(fn.y) >= max(abs(fn.x), abs(fn.z)) else ((co.y, co.z) if abs(fn.x) >= abs(fn.z) else (co.x, co.y))
                key = (round(co.x, 5), round(co.y, 5), round(co.z, 5), round(n.x, 3), round(n.y, 3), round(n.z, 3))
                if key not in P['index']:
                    P['index'][key] = len(P['v']) // 3
                    P['v'] += [co.x, co.z, co.y]; P['n'] += [n.x, n.z, n.y]; P['uv'] += [u, v]
                corner.append(P['index'][key])
            tl += [corner[0], corner[2], corner[1]]   # (Blender's right-handed frame to the game's left-handed one)
        bpy.data.meshes.remove(me)
    out = {'parts': {}}
    for part, P in parts.items():
        piv = _pivots.get(part, Vector((0, 0, 0)))
        out['parts'][part] = {'v': [round(x, 5) for x in P['v']], 'n': [round(x, 3) for x in P['n']], 'uv': [round(x, 4) for x in P['uv']],
                              'slots': P['slots'], 't': [P['tris'][s] for s in P['slots']], 'pivot': [round(piv.x, 5), round(piv.z, 5), round(piv.y, 5)]}
        vs = P['v']
        lo = [min(vs[i::3]) for i in range(3)]; hi = [max(vs[i::3]) for i in range(3)]
        print(f'{name}/{part}: {len(vs) // 3} verts, {sum(len(t) for t in P["tris"].values()) // 3} tris, slots {P["slots"]}, game-frame box x {lo[0]:.3f}..{hi[0]:.3f} y {lo[1]:.3f}..{hi[1]:.3f} z {lo[2]:.3f}..{hi[2]:.3f}')
    with gzip.open(os.path.join(HERE, name + '.json.gz'), 'wt') as f: f.write(json.dumps(out, separators=(',', ':')))
    print('wrote', os.path.join(HERE, name + '.json.gz'))

def preview(name, size=640, views=((35, 20), (125, 20), (215, 20), (305, 65))):
    """Four Cycles pictures round the model (yaw, pitch in degrees; yaw 0 looks at its front from ahead), on grey, and a 2x2 sheet."""
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 32; sc.cycles.device = 'CPU'; sc.cycles.use_denoising = True
    sc.render.resolution_x = sc.render.resolution_y = size; sc.render.film_transparent = False
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.08, 0.085, 0.095, 1)
    w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
    for rot, energy, col in (((50, 0, -35), 3.5, (1, 1, 1)), ((20, 0, 150), 1.2, (0.75, 0.85, 1)), ((-15, 0, 60), 0.8, (1, 0.9, 0.8))):
        l = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); l.data.energy = energy; l.data.color = col
        l.rotation_euler = [math.radians(a) for a in rot]; sc.collection.objects.link(l)
    obs = [o for o in sc.collection.objects if o.type == 'MESH']
    deps = bpy.context.evaluated_depsgraph_get()
    pts = [o.matrix_world @ Vector(c) for o in obs for c in o.evaluated_get(deps).bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    centre = (lo + hi) / 2; radius = max((hi - lo).length / 2, 0.02)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 70; fov = cam.data.angle; dist = radius / math.sin(fov / 2) * 1.05
    cam.data.clip_start = max(0.001, dist - radius * 3); cam.data.clip_end = dist + radius * 3
    files = []
    for k, (yaw, pitch) in enumerate(views):
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.sin(y) * math.cos(p), math.cos(y) * math.cos(p), math.sin(p)))   # yaw 0: in front (+Y) looking back
        cam.location = centre + d * dist
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(HERE, f'{name}_{k}.png'); bpy.ops.render.render(write_still=True); files.append(sc.render.filepath)
    try:
        sheet = bpy.data.images.new('sheet', size * 2, size * 2); px = [0.0] * (size * size * 16)
        for k, f in enumerate(files):
            im = bpy.data.images.load(f); src = list(im.pixels); ox, oy = (k % 2) * size, (1 - k // 2) * size
            for row in range(size):
                a = (row * size) * 4; b = ((oy + row) * size * 2 + ox) * 4
                px[b:b + size * 4] = src[a:a + size * 4]
        sheet.pixels = px; sheet.filepath_raw = os.path.join(HERE, name + '_sheet.png'); sheet.file_format = 'PNG'; sheet.save()
        print('preview', sheet.filepath_raw)
    except Exception as e: print('no sheet:', e)
