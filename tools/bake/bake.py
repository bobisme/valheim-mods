# Bake a detailed Blender model onto a game-style low-poly copy (see docs/model-baking.md).
#   blender -b --python tools/bake/bake.py -- <conf.py> <kind>
# The model's own script builds the detailed scene, which stays the source and is never changed. Each part gets a lighter copy (swept and
# lathed parts lose every other ring and side, solid chunks are collapsed); marks lying on a surface (conf.DECALS) are left out and only
# baked. The copies are joined and unwrapped into one atlas, and the detail is baked into it from the parts pulled apart (so no part bakes
# its neighbour onto itself): colour as the game draws it, a tangent-space normal map and what glows; the creases are darkened by an
# ambient-occlusion bake of the assembled copy. Everything is baked at twice conf.PX and halved.
# Writes into conf.SHIP: <name>.json.gz (v, n, tan, uv, one triangle list, head) and <name>_albedo/_normal[/_glow].png;
# into conf.WORK/view: <name>_high.glb, <name>_low.glb and <name>_view.json for tools/bake/viewer.py; and conf.WORK/<name>.blend.
import sys, os, math, json, gzip, shutil
args = sys.argv[sys.argv.index('--') + 1:]
CONF, KIND = os.path.abspath(args[0]), args[1]
conf = {'__file__': CONF}
exec(open(CONF).read(), conf)
WORK, SHIP, NAME = conf['WORK'], conf['SHIP'], conf['NAME'](KIND)
BUDGET, PX, DECALS = conf.get('BUDGET', 1500), conf.get('PX', 256), tuple(conf.get('DECALS', ('cut',)))
TOL = conf.get('TOL', {})   # (name prefix: how far that part may stray, metres, overriding the shape check's own)
if isinstance(BUDGET, dict): BUDGET = BUDGET[KIND]
if isinstance(PX, dict): PX = PX[KIND]
TEX = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'tex') + '/'
os.makedirs(WORK + '/view', exist_ok=True)
for f in conf.get('COPY', []): shutil.copy(f, WORK)

# --- the detailed scene, from the model's own script ---
sys.argv = sys.argv[:sys.argv.index('--') + 1] + conf['ARGS'](KIND)
scope = {'__name__': '__bake__'}
exec(open(conf['SCRIPT'](KIND) if callable(conf['SCRIPT']) else conf['SCRIPT']).read(), scope)   # (SCRIPT may be per kind)
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import numpy as np
mats, SMOOTH = scope['mats'], scope.get('SMOOTH', set())

def lin(c): return tuple(((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c[:3])

# --- its materials as the game draws them (conf.look calls these) ---
view_tints, GRAIN = {}, {}
OFFSET, OWNUV = {}, {}   # (a grained texture's offset; materials drawn on the mesh's own uvs: name -> texture)
def plain(m, colour, emit=None):
    m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*lin(colour), 1); b.inputs['Roughness'].default_value = 0.8
    b.inputs['Emission Color'].default_value = (*lin(emit), 1) if emit else (0, 0, 0, 1)
    b.inputs['Emission Strength'].default_value = 1.0 if emit else 0.0
def grained(m, texture, scale, tint, emit=None, offset=0.0, on_uv=False):
    """A game texture laid on in metres (as Look.Build's uvs, times the material's mainTextureScale), tinted."""
    plain(m, (1, 1, 1), emit); nt = m.node_tree; b = nt.nodes['Principled BSDF']
    tc = nt.nodes.new('ShaderNodeTexCoord'); mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (scale,) * 3
    im = nt.nodes.new('ShaderNodeTexImage'); im.image = bpy.data.images.load(TEX + texture)
    if not on_uv: im.projection = 'BOX'; im.projection_blend = 0.3   # (on_uv: the uvs conf.prepare gave each part, as its game export would)
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1
    nt.links.new(tc.outputs['UV' if on_uv else 'Object'], mp.inputs['Vector']); nt.links.new(mp.outputs['Vector'], im.inputs['Vector'])
    nt.links.new(im.outputs['Color'], mix.inputs['A']); mix.inputs['B'].default_value = (*tint, 1)
    nt.links.new(mix.outputs['Result'], b.inputs['Base Color'])
    GRAIN[m.name] = (texture, scale); view_tints[m.name] = list(tint)
    if offset: mp.inputs['Location'].default_value = (offset,) * 3; OFFSET[m.name] = offset
def uvmapped(m, texture, tint, emit=None):
    """A game texture on the mesh's own uvs (as a model that keeps its own uvs for a part, at scale 1), tinted. (conf.look calls it by name.)"""
    plain(m, (1, 1, 1), emit); nt = m.node_tree; b = nt.nodes['Principled BSDF']
    tc = nt.nodes.new('ShaderNodeTexCoord'); im = nt.nodes.new('ShaderNodeTexImage'); im.image = bpy.data.images.load(TEX + texture)
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1
    nt.links.new(tc.outputs['UV'], im.inputs['Vector']); nt.links.new(im.outputs['Color'], mix.inputs['A']); mix.inputs['B'].default_value = (*tint, 1)
    nt.links.new(mix.outputs['Result'], b.inputs['Base Color'])
    OWNUV[m.name] = texture; view_tints[m.name] = list(tint)
conf['uvmapped'] = uvmapped
conf['look'](mats, KIND, plain, grained)

deps = bpy.context.evaluated_depsgraph_get()
high = [o for o in bpy.context.scene.collection.objects if o.type == 'MESH']
for o in high:
    if o.name in SMOOTH or (isinstance(SMOOTH, tuple) and o.name.startswith(SMOOTH)) or \
            (o.data.materials and o.data.materials[0] in mats and mats.index(o.data.materials[0]) in SMOOTH):   # (a set of names, a tuple of name prefixes, or of slot numbers)
        for p in o.data.polygons: p.use_smooth = True
if 'prepare' in conf: conf['prepare'](high)   # (a conf may give the detailed parts uvs of their own (mark them o['bake_uv'] = 1))
def emissive(o): return any(m and m.node_tree and m.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value > 0 for m in o.data.materials)

# where its fire is: the middle of what glows in the top third (the game hangs a light there); game frame (y up)
top = max(max((o.matrix_world @ Vector(c)).z for c in o.bound_box) for o in high)
glow_z = [(o.matrix_world @ v.co).z for o in high if emissive(o) for v in o.data.vertices]
glow_z = [z for z in glow_z if z > top * 0.66]
head = sum(glow_z) / len(glow_z) if glow_z else None

# --- the low copy ---
decals = [o for o in high if o.name.startswith(DECALS)]
hosts = [o for o in high if o not in decals]
total = sum(len(o.evaluated_get(deps).data.loop_triangles) for o in hosts)
ratio = min(1.0, BUDGET / max(1, total))
parts = []
def islands(mesh):
    bm_ = bmesh.new(); bm_.from_mesh(mesh); bm_.verts.ensure_lookup_table(); seen = set(); k = 0
    for v in bm_.verts:
        if v.index in seen: continue
        k += 1; stack = [v]; seen.add(v.index)
        while stack:
            u = stack.pop()
            for e in u.link_edges:
                w = e.other_vert(u)
                if w.index not in seen: seen.add(w.index); stack.append(w)
    bm_.free(); return k
def grid(mesh):   # (a swept, lathed or sheet part: all quads, one piece)
    return sum(1 for p in mesh.polygons if len(p.vertices) == 4) / max(1, len(mesh.polygons)) > 0.8 and islands(mesh) == 1
def shape_off(lo, me):
    """How far the lightened part (lo, its modifiers evaluated) strays from the detailed mesh me, both ways (metres)."""
    bpy.context.view_layer.update()
    ev = lo.evaluated_get(bpy.context.evaluated_depsgraph_get()).data
    a = BVHTree.FromPolygons([v.co[:] for v in me.vertices], [p.vertices[:] for p in me.polygons])
    b = BVHTree.FromPolygons([v.co[:] for v in ev.vertices], [p.vertices[:] for p in ev.polygons])
    off = max(max(((a.find_nearest(v.co)[3] or 0) for v in ev.vertices), default=0), max(((b.find_nearest(v.co)[3] or 0) for v in me.vertices), default=0))
    # and not folded: a fold lies close to the surface but faces another way (Ullr's skis came out crumpled). By area: along a sheet's
    # thin edges the nearest original face is ambiguous, so a few turned slivers don't count.
    turned = total = 0.0
    for p in ev.polygons:
        total += p.area
        hit = a.find_nearest(p.center)
        if hit[1] is not None and hit[1].dot(p.normal) < 0.5: turned += p.area
    if total > 0 and turned > 0.05 * total: return 1e9
    return off
def tolerance(o, me=None):
    """How far a part may stray: 3% of its length, but never more than a third of its thickness (a thin ring or wire stays round).
    Thickness from volume and area (2V/A), when it's closed: a diagonal board's bounding box says nothing of how thin it is."""
    d = sorted(Vector(o.dimensions)); thick = d[0]
    if me is not None:
        bm = bmesh.new(); bm.from_mesh(me)
        area = sum(f.calc_area() for f in bm.faces); vol = abs(bm.calc_volume()) if all(e.is_manifold for e in bm.edges) else 0
        bm.free()
        if area > 0 and vol > 0: thick = min(thick, 2 * vol / area)
    return min(0.03 * d[2], max(0.008, 0.35 * thick))   # (but 8 mm may always go: tighter kept cloth and wire at full weight)
for gi, o in enumerate(hosts):
    if o.modifiers and len(o.data.polygons) > 24 and grid(o.data):
        # a sheet made thick (or curved) by modifiers: thin the sheet itself first, so its two faces stay apart
        # (as few passes off as keep it where it was: a coarse drape let the block under Hel's shroud poke through it in holes)
        ref = bpy.data.meshes.new_from_object(o.evaluated_get(deps)); ref.transform(o.matrix_world)
        tol = min(0.03 * max(Vector(o.dimensions)), 0.015)   # (a cloth's own thickness says nothing of how far it may move: 1.5 cm at most)
        tol = next((t for k, t in TOL.items() if o.name.startswith(k)), tol)
        nb = len(o.data.polygons); k = max(1, min(6, math.ceil(math.log2(nb / max(12, nb * ratio)) - 0.3)))
        lo = None
        while k >= 1:
            src = o.copy(); src.data = o.data.copy(); bpy.context.scene.collection.objects.link(src)
            d = src.modifiers.new('dec', 'DECIMATE'); d.decimate_type = 'UNSUBDIV'; d.iterations = k
            try: src.modifiers.move(len(src.modifiers) - 1, 0)
            except Exception: pass
            bpy.context.view_layer.update()
            me = bpy.data.meshes.new_from_object(src.evaluated_get(bpy.context.evaluated_depsgraph_get())); me.transform(o.matrix_world)
            bpy.data.objects.remove(src)
            lo = bpy.data.objects.new(o.name + '_lo', me); bpy.context.scene.collection.objects.link(lo)
            off_ = shape_off(lo, ref)
            if os.environ.get('BAKE_TRACE'): print('TRACE sheet try', o.name, k, round(off_, 4), 'tol', round(tol, 4))
            if off_ <= tol: break
            bpy.data.objects.remove(lo); lo = None; k -= 1
        if lo is None: lo = bpy.data.objects.new(o.name + '_lo', ref); bpy.context.scene.collection.objects.link(lo)
        parts.append(lo)
        if os.environ.get('BAKE_TRACE') and o.name.startswith(os.environ['BAKE_TRACE']): print('TRACE sheet', o.name, nb, 'passes', k, 'tol', round(tol, 4))
        continue
    me = bpy.data.meshes.new_from_object(o.evaluated_get(deps)); me.transform(o.matrix_world)
    quads = sum(1 for p in me.polygons if len(p.vertices) == 4) / max(1, len(me.polygons))
    lo = bpy.data.objects.new(o.name + '_lo', me); bpy.context.scene.collection.objects.link(lo)
    n = len(me.polygons)
    if quads > 0.8 and n > 24 and islands(me) == 1:
        # a swept or lathed part (a grid of quads): every other ring and side gone, so it stays a clean tube
        d = lo.modifiers.new('dec', 'DECIMATE'); d.decimate_type = 'UNSUBDIV'; d.iterations = max(1, min(6, math.ceil(math.log2(n / max(24, n * ratio)) - 0.3)))   # (each pass about halves it)
        tol = next((t for k, t in TOL.items() if o.name.startswith(k)), tolerance(o, me))
        # (fewer passes while it strays: a horn's thin ridge rings went flat; none if even one strays (Blender won't set 0 passes))
        k = d.iterations
        while k >= 1:
            d.iterations = k
            if shape_off(lo, me) <= tol: break
            k -= 1
        if k < 1: lo.modifiers.remove(d)
    else:
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(4), verts=bm.verts, edges=bm.edges)
        bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        n = len(me.polygons)
        if n > 24:
            # collapsed only as far as it keeps its shape: every corner of each within a few millimetres of the other (a blind ratio bent
            # the brazier's slab into a wedge and crumpled the totem's horns)
            d = lo.modifiers.new('dec', 'DECIMATE'); d.use_collapse_triangulate = True
            tol = next((t for k, t in TOL.items() if o.name.startswith(k)), tolerance(o, me))
            r = max(ratio, 12 / n)
            while r < 1:
                d.ratio = r
                if shape_off(lo, me) <= tol: break
                r = min(1.0, r * 1.6)
            if r >= 1: lo.modifiers.remove(d)
    parts.append(lo)
    if os.environ.get('BAKE_TRACE') and o.name.startswith(os.environ['BAKE_TRACE']): print('TRACE', o.name, n, 'quads %.2f' % quads, [(m.type, getattr(m, 'decimate_type', ''), getattr(m, 'iterations', ''), round(getattr(m, 'ratio', 0), 3)) for m in lo.modifiers], 'off', shape_off(lo, me))
    if n > 400: print('BAKE big part', o.name, n, 'quads %.2f' % quads, [m.type for m in lo.modifiers])
bpy.context.view_layer.update(); deps = bpy.context.evaluated_depsgraph_get()
from collections import Counter
use = Counter()
for o in parts: use[o.name.split('.')[0][:-3]] += len(o.evaluated_get(deps).data.polygons)
print('BAKE parts', use.most_common(10))
lowme = bpy.data.meshes.new('low'); bm = bmesh.new(); grp = bm.verts.layers.int.new('grp')
for gi, o in enumerate(parts):
    m2 = bpy.data.meshes.new_from_object(o.evaluated_get(deps)); start = len(bm.verts); bm.from_mesh(m2); bm.verts.ensure_lookup_table()
    for v in bm.verts[start:]: v[grp] = gi
for o in parts: bpy.data.objects.remove(o)
bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(lowme); bm.free()
for p in lowme.polygons: p.use_smooth = True
low = bpy.data.objects.new('low', lowme); bpy.context.scene.collection.objects.link(low)
bpy.context.view_layer.objects.active = low; low.select_set(True)
bpy.ops.object.shade_auto_smooth(angle=math.radians(40))   # (hard where it turns sharply, soft where it rounds, as the game's own)
for md in list(low.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)
print('BAKE', NAME, 'low tris', len(lowme.polygons), 'from', total + sum(len(o.evaluated_get(deps).data.loop_triangles) for o in decals))

# --- one atlas ---
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=2.5 / PX, scale_to_bounds=False)
def thicken():
    """Islands thinner than a few texels (a thread's sides) get nothing baked into them but the margin's black: widen them across."""
    bm = bmesh.from_edit_mesh(lowme); uvl = bm.loops.layers.uv.active; bm.faces.ensure_lookup_table()
    parent = list(range(len(bm.faces)))
    def find(i):
        while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for e in bm.edges:
        fs = e.link_faces
        if len(fs) != 2: continue
        def uvs(f): return {l.vert.index: l[uvl].uv.copy() for l in f.loops if l.vert in e.verts}
        a, b = uvs(fs[0]), uvs(fs[1])
        if all((a[k] - b[k]).length < 1e-5 for k in a): parent[find(fs[0].index)] = find(fs[1].index)
    islands = {}
    for f in bm.faces: islands.setdefault(find(f.index), []).append(f)
    area = sum(abs((f.loops[1][uvl].uv - f.loops[0][uvl].uv).cross(f.loops[2][uvl].uv - f.loops[0][uvl].uv)) / 2 for f in bm.faces)
    T = 3.0 / (2 * PX * math.sqrt(0.6 / max(area, 1e-9)))   # (3 baked texels, the atlas filled about 60%)
    widened = 0
    for fs in islands.values():
        ls = [l for f in fs for l in f.loops]
        us = [l[uvl].uv.x for l in ls]; vs = [l[uvl].uv.y for l in ls]
        w, h = max(us) - min(us), max(vs) - min(vs)
        cu, cv = (max(us) + min(us)) / 2, (max(vs) + min(vs)) / 2
        if min(w, h) >= T: continue
        widened += 1
        for l in ls:
            if w < T: l[uvl].uv.x = cu + (l[uvl].uv.x - cu) * (T / max(w, T * 0.05))
            if h < T: l[uvl].uv.y = cv + (l[uvl].uv.y - cv) * (T / max(h, T * 0.05))
    bmesh.update_edit_mesh(lowme)
    print('BAKE', NAME, 'islands', len(islands), 'widened', widened)
thicken()
bpy.ops.uv.pack_islands(margin=2.5 / PX, rotate=True)
bpy.ops.object.mode_set(mode='OBJECT')

# --- each surface mark goes with the part it lies on ---
trees = []
for o in hosts:
    b2 = bmesh.new(); b2.from_mesh(o.evaluated_get(deps).to_mesh()); b2.transform(o.matrix_world); trees.append(BVHTree.FromBMesh(b2)); b2.free()
owner = {}
for d in decals:   # (the host nearest the mark's own surface, over points along it: a wrap's middle is inside the shaft, not on it)
    vs = d.evaluated_get(deps).data.vertices; step = max(1, len(vs) // 40)
    pts = [d.matrix_world @ vs[i].co for i in range(0, len(vs), step)]
    owner[d.name] = min(range(len(hosts)), key=lambda i: sum((lambda h: h[3] if h[0] is not None else 1e3)(trees[i].find_nearest(p)) for p in pts))

# --- the bakes, the parts pulled apart (40 m between them) ---
SPACE = 40.0
def explode(sign):
    for gi, o in enumerate(hosts): o.location.x += sign * gi * SPACE
    for d in decals: d.location.x += sign * owner[d.name] * SPACE
    a = lowme.attributes['grp'].data
    for v in lowme.vertices: v.co.x += sign * a[v.index].value * SPACE
    lowme.update(); bpy.context.view_layer.update()
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 16
lowmat = bpy.data.materials.new('baked'); lowmat.use_nodes = True; lowme.materials.clear(); lowme.materials.append(lowmat)
imgs = {}
def target(kind, colour):
    im = bpy.data.images.new(kind, PX * 2, PX * 2, alpha=False, float_buffer=kind == 'normal'); im.colorspace_settings.name = colour
    nd = lowmat.node_tree.nodes.new('ShaderNodeTexImage'); nd.image = im
    for n in lowmat.node_tree.nodes: n.select = False
    nd.select = True; lowmat.node_tree.nodes.active = nd
    imgs[kind] = im
bk = sc.render.bake; bk.cage_extrusion = 0.06; bk.max_ray_distance = 0.2; bk.margin = 4   # (safe that far: no neighbour is near)
for o in bpy.context.scene.objects: o.select_set(False)
for o in high: o.select_set(True)
low.select_set(True); bpy.context.view_layer.objects.active = low
explode(1)
bk.use_selected_to_active = True
for kind, typ, cs, extra in (('albedo', 'DIFFUSE', 'sRGB', {'pass_filter': {'COLOR'}}), ('normal', 'NORMAL', 'Non-Color', {}), ('glow', 'EMIT', 'sRGB', {})):
    target(kind, cs); bpy.ops.object.bake(type=typ, **extra)
explode(-1)
bk.use_selected_to_active = False
for o in high: o.select_set(False); o.hide_render = True   # (the low copy's own creases only)
if sc.world is None: sc.world = bpy.data.worlds.new('bake')
sc.world.light_settings.distance = 0.15   # (creases only: Blender's default 10 m let whole parts shade each other in blotches)
target('ao', 'Non-Color'); bpy.ops.object.bake(type='AO')
for o in high: o.hide_render = False

# --- out: albedo darkened in the creases, all halved to the shipped size ---
def arr(im): return np.array(im.pixels[:], dtype=np.float32).reshape(im.size[1], im.size[0], 4)
def half(a): return a.reshape(a.shape[0] // 2, 2, a.shape[1] // 2, 2, 4).mean(axis=(1, 3))
def save(a, name, cs):
    im = bpy.data.images.new(name, a.shape[1], a.shape[0], alpha=False); im.colorspace_settings.name = cs
    im.pixels[:] = a.ravel(); im.filepath_raw = SHIP + '/' + name + '.png'; im.file_format = 'PNG'; im.save()
    return SHIP + '/' + name + '.png'
alb = arr(imgs['albedo']); ao = arr(imgs['ao'])
alb[..., :3] *= (0.75 + 0.25 * ao[..., :1])   # (lightly: where parts of the low copy pass through each other its AO is black in spots)
maps = {'albedo': save(half(alb), NAME + '_albedo', 'sRGB')}
nrm = half(arr(imgs['normal'])); nrm[..., 3] = 1; maps['normal'] = save(nrm, NAME + '_normal', 'Non-Color')
glow = half(arr(imgs['glow']))
if glow[..., :3].max() > 0.01: maps['glow'] = save(glow, NAME + '_glow', 'sRGB')
elif os.path.exists(SHIP + '/' + NAME + '_glow.png'): os.remove(SHIP + '/' + NAME + '_glow.png')

# --- the low mesh, in the game's frame (x, z, y: which mirrors, so triangles turn and the bitangent's sign flips), with Blender's tangents ---
lowme.calc_tangents()
uvl = lowme.uv_layers.active.data
V, N, T, UV, TRI, idx = [], [], [], [], [], {}
for p in lowme.polygons:
    cs = []
    for li in p.loop_indices:
        L = lowme.loops[li]; co = lowme.vertices[L.vertex_index].co; n = L.normal; t = L.tangent; u = uvl[li].uv
        key = (round(co.x, 4), round(co.y, 4), round(co.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3), round(u.x, 5), round(u.y, 5), round(t.x, 3), round(t.y, 3), round(t.z, 3), L.bitangent_sign)
        if key not in idx:
            idx[key] = len(V) // 3; V += [co.x, co.z, co.y]; N += [n.x, n.z, n.y]; T += [t.x, t.z, t.y, -L.bitangent_sign]; UV += [u.x, u.y]
        cs.append(idx[key])
    TRI += [cs[0], cs[2], cs[1]]
out = {'v': [round(x, 4) for x in V], 'n': [round(x, 3) for x in N], 'tan': [round(x, 3) for x in T], 'uv': [round(x, 5) for x in UV], 't': [TRI]}
if head is not None: out['head'] = round(head, 3)
gzip.open(SHIP + '/' + NAME + '.json.gz', 'wt').write(json.dumps(out, separators=(',', ':')))
print('BAKE', NAME, 'shipped verts', len(V) // 3, 'tris', len(TRI) // 3, 'maps', sorted(maps), 'head', out.get('head'))

# --- for the viewer: the detailed model (game grain on metre uvs) and the baked one ---
for o in high:
    me = o.data
    if (o.get('bake_uv') or any(m is not None and m.name in OWNUV for m in me.materials)) and me.uv_layers: continue   # (its own uvs stay)
    if not me.uv_layers: me.uv_layers.new(name='uv')
    if me.uv_layers.active is None: continue   # (a mesh without faces takes no uvs)
    uv = me.uv_layers.active.data
    for p in me.polygons:
        n = p.normal
        for li in p.loop_indices:
            co = o.matrix_world @ me.vertices[me.loops[li].vertex_index].co
            u, v = (co.x, co.z) if abs(n.y) >= max(abs(n.x), abs(n.z)) else ((co.y, co.z) if abs(n.x) >= abs(n.z) else (co.x, co.y))
            uv[li].uv = (u, v)
for m in mats:
    if m.name not in GRAIN: continue
    nt = m.node_tree; b = nt.nodes['Principled BSDF']; tex, scale = GRAIN[m.name]
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX + tex)
    uvn = nt.nodes.new('ShaderNodeUVMap'); mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (scale,) * 3
    if m.name in OFFSET: mp.inputs['Location'].default_value = (OFFSET[m.name],) * 3
    nt.links.new(uvn.outputs['UV'], mp.inputs['Vector']); nt.links.new(mp.outputs['Vector'], t.inputs['Vector'])
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
for m in mats:
    if m.name not in OWNUV: continue
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX + OWNUV[m.name]); nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
nt = lowmat.node_tree; b = nt.nodes['Principled BSDF']
for n in list(nt.nodes):
    if n.type == 'TEX_IMAGE': nt.nodes.remove(n)
def img(kind, cs):
    i = bpy.data.images.load(maps[kind]); i.colorspace_settings.name = cs
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = i; return t
nt.links.new(img('albedo', 'sRGB').outputs['Color'], b.inputs['Base Color'])
nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(img('normal', 'Non-Color').outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
if 'glow' in maps: nt.links.new(img('glow', 'sRGB').outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = 1
b.inputs['Roughness'].default_value = 0.8
for name, objs in (('high', high), ('low', [low])):
    for o in sc.objects: o.select_set(o in objs)
    bpy.ops.export_scene.gltf(filepath=f'{WORK}/view/{NAME}_{name}.glb', use_selection=True, export_format='GLB', export_yup=True, export_apply=True, export_tangents=name == 'low')
json.dump({'name': NAME, 'tints': view_tints, 'px': PX, 'head': head, 'glow': 'glow' in maps}, open(f'{WORK}/view/{NAME}_view.json', 'w'))
bpy.ops.wm.save_as_mainfile(filepath=f'{WORK}/{NAME}.blend')
