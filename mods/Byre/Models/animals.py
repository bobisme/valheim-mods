# Byre's sheep and goat, made in Blender on the boar's own skeleton (byrelib) so the game's boar animations move them.
# Shapes are tapered capsules and ellipsoids fused into one closed skin by a voxel remesh, lightened to the game's low-poly budget,
# drawn flat-shaded as the game's "Poly Art" animals are, coloured by region into a small baked texture (gentle AO), skinned by bone heat.
# The boar's skeleton comes from export_rig.py (read offline from the game's bundle: boar-rig.json is Iron Gate's, never committed).
# Run: blender -b --python animals.py -- <sheep|goat> <out dir>
import sys, os, math, random, json, gzip
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix, Quaternion, Euler
import byrelib as L

RNG = random.Random(7)
def V(*a): return Vector(a)

# ---- shapes: real-sized primitives, fused into one skin by a voxel remesh ---------------------------------------------------
class Shape:
    """Primitives gathered for one skin; .mesh() fuses them (voxel remesh) and smooths the seams a little."""
    def __init__(self, name, voxel=0.011): self.name, self.voxel, self.parts = name, voxel, []
    def _add(self, ob): self.parts.append(ob); return ob
    def ell(self, c, sx, sy, sz, rot=None):
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=1.0)
        me = bpy.data.meshes.new('p'); bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new('p', me); bpy.context.scene.collection.objects.link(ob)
        ob.scale = (sx, sy, sz); ob.location = c
        if rot is not None: ob.rotation_mode = 'QUATERNION'; ob.rotation_quaternion = rot
        return self._add(ob)
    def ball(self, c, r): return self.ell(c, r, r, r)
    def cap(self, a, b, r0, r1=None):
        """A tapered capsule from a (radius r0) to b (r1)."""
        r1 = r0 if r1 is None else r1
        a, b = Vector(a), Vector(b); d = b - a
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=r0, radius2=r1, depth=d.length)
        me = bpy.data.meshes.new('p'); bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new('p', me); bpy.context.scene.collection.objects.link(ob)
        ob.location = (a + b) / 2; ob.rotation_mode = 'QUATERNION'; ob.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
        self._add(ob); self.ball(a, r0); self.ball(b, r1)
    def mesh(self, smooth=0.5):
        bpy.ops.object.select_all(action='DESELECT')
        for o in self.parts: o.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0]
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bpy.ops.object.join(); ob = self.parts[0]; ob.name = self.name
        m = ob.modifiers.new('rm', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = self.voxel; m.adaptivity = 0
        bpy.ops.object.modifier_apply(modifier=m.name)
        # (the remesh can leave crumbs: keep the big pieces only)
        bm = bmesh.new(); bm.from_mesh(ob.data); seen = set(); drop = []
        for v in bm.verts:
            if v in seen: continue
            isle, stack = [], [v]; seen.add(v)
            while stack:
                x = stack.pop(); isle.append(x)
                for e in x.link_edges:
                    o = e.other_vert(x)
                    if o not in seen: seen.add(o); stack.append(o)
            if len(isle) < 60: drop += isle
        bmesh.ops.delete(bm, geom=drop, context='VERTS'); bm.to_mesh(ob.data); bm.free()
        if smooth > 0:
            m = ob.modifiers.new('sm', 'SMOOTH'); m.factor = smooth; m.iterations = 4
            bpy.ops.object.modifier_apply(modifier=m.name)
        return ob

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); objs[0].name = name
    return objs[0]

def lighten(ob, tris):
    """To the budget: a planar-ish collapse decimate (the game's animals are a couple of thousand triangles)."""
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects: o.select_set(False)
    ob.select_set(True)
    now = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if now > tris:
        m = ob.modifiers.new('dec', 'DECIMATE'); m.ratio = tris / now; m.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=m.name)
    m = ob.modifiers.new('tri', 'TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=m.name)
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)

def curve_tube(name, pts, r0, r1, res=6):
    """A tapered tube along points (horns)."""
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'; cu.bevel_depth = 1.0; cu.bevel_resolution = 1; cu.resolution_u = 4
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        t = i / (len(pts) - 1); sp.points[i].co = (*p, 1); sp.points[i].radius = r0 + (r1 - r0) * t
    ob = bpy.data.objects.new(name, cu); bpy.context.scene.collection.objects.link(ob)
    cu.use_fill_caps = True
    dg = bpy.context.evaluated_depsgraph_get(); me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    out = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(out)
    bpy.data.objects.remove(ob, do_unlink=True)
    return out

# ---- the animals ------------------------------------------------------------------------------------------------------
# Leg joints follow the boar's (they're what the walk moves): hind thigh->calf->hock->fetlock->hoof, fore shoulder->knee->fetlock->hoof.
def hind(s):  return [V(s*0.12, -0.36, 0.70), V(s*0.150, -0.34, 0.47), V(s*0.156, -0.47, 0.295), V(s*0.160, -0.475, 0.11), V(s*0.161, -0.455, 0.02)]
def fore(s):  return [V(s*0.13, 0.25, 0.64), V(s*0.182, 0.245, 0.33), V(s*0.184, 0.243, 0.10), V(s*0.183, 0.262, 0.02)]

def legs(sh, upper, lower, hoof):
    for k in (-1, 1):
        h = hind(k); f = fore(k)
        sh.cap(h[0], h[1], upper, upper * 0.8); sh.cap(h[1], h[2], upper * 0.62, lower * 1.1); sh.cap(h[2], h[3], lower, lower * 0.9); sh.cap(h[3], h[4], hoof * 0.95, hoof)
        sh.cap(f[0], f[1], upper * 0.95, lower * 1.15); sh.cap(f[1], f[2], lower, lower * 0.9); sh.cap(f[2], f[3], hoof * 0.95, hoof)

def sheep():
    """A Norse short-tailed sheep: a woolly body on slim tan legs, a pale face with dark muzzle; the fleece is its own skin."""
    sh = Shape('sheep')
    # (the boar's hips and shoulders are fixed at 0.8 m: short legs are a deep body over them, the belly at 0.37 m)
    sh.ell(V(0, -0.07, 0.62), 0.175, 0.40, 0.25)                        # the body, shorn
    sh.cap(V(0, 0.20, 0.70), V(0, 0.46, 0.79), 0.12, 0.085)             # neck
    sh.ell(V(0, 0.565, 0.795), 0.088, 0.095, 0.092)                      # skull
    sh.cap(V(0, 0.60, 0.77), V(0, 0.705, 0.685), 0.07, 0.05)             # muzzle
    for k in (-1, 1):
        sh.ell(V(k*0.112, 0.555, 0.81), 0.066, 0.024, 0.03, rot=Euler((0, math.radians(k*-22), math.radians(k*-8))).to_quaternion())  # ears
    sh.cap(V(0, -0.47, 0.76), V(0, -0.57, 0.61), 0.042, 0.03)            # short tail
    legs(sh, 0.085, 0.043, 0.045)
    body = sh.mesh()
    # The fleece: a lumpy shell over body, neck and upper legs (separate, so shearing takes it off).
    fl = Shape('fleece', voxel=0.016)
    fl.ell(V(0, -0.07, 0.635), 0.24, 0.45, 0.32)
    fl.cap(V(0, 0.14, 0.71), V(0, 0.45, 0.815), 0.20, 0.125)
    fl.ball(V(0, 0.545, 0.87), 0.065)                                    # the poll's tuft
    for i in range(110):
        u = RNG.uniform(0, math.tau); t = RNG.uniform(-1, 1)
        y = -0.07 + 0.44 * t
        rr = math.sqrt(max(0.0, 1 - t * t))
        x = 0.24 * rr * math.cos(u); z = 0.635 + 0.32 * rr * math.sin(u)
        if z < 0.40 and abs(x) < 0.08: continue                          # (the belly: thinner there)
        fl.ball(V(x, y, z), RNG.uniform(0.05, 0.078))
    for i in range(24):                                                   # the neck's ruff
        t = RNG.uniform(0, 1); u = RNG.uniform(0, math.tau)
        c = V(0, 0.14, 0.71).lerp(V(0, 0.45, 0.815), t); r = 0.20 + (0.125 - 0.20) * t
        fl.ball(c + V(r * math.cos(u), 0, r * math.sin(u)) * 0.95, RNG.uniform(0.045, 0.065))
    for k in (-1, 1):
        fl.cap(V(k*0.12, -0.36, 0.62), V(k*0.155, -0.35, 0.36), 0.12, 0.09)  # haunch
        fl.cap(V(k*0.13, 0.24, 0.60), V(k*0.18, 0.245, 0.36), 0.11, 0.085)  # shoulder
    fleece = fl.mesh(smooth=0.3)
    return body, fleece, []

def goat():
    """A Norse goat: stocky, horns swept back, a beard, a short upright tail; brown with black legs and a dorsal stripe."""
    sh = Shape('goat')
    # (the boar's hips and shoulders are fixed at 0.8 m: short legs are a deep body over them, the belly at 0.40 m)
    sh.ell(V(0, -0.06, 0.635), 0.18, 0.40, 0.235)
    sh.ell(V(0, 0.16, 0.60), 0.17, 0.17, 0.20)                           # the deep chest
    sh.cap(V(0, 0.22, 0.70), V(0, 0.46, 0.82), 0.115, 0.075)
    sh.ell(V(0, 0.55, 0.86), 0.07, 0.08, 0.078)
    sh.cap(V(0, 0.59, 0.84), V(0, 0.695, 0.745), 0.058, 0.038)
    for k in (-1, 1):
        sh.ell(V(k*0.085, 0.54, 0.875), 0.058, 0.02, 0.025, rot=Euler((0, math.radians(k*-30), math.radians(k*-25))).to_quaternion())
    sh.cap(V(0, -0.45, 0.78), V(0, -0.53, 0.86), 0.034, 0.024)           # tail, up
    legs(sh, 0.085, 0.042, 0.042)
    sh.cap(V(0, 0.665, 0.71), V(0, 0.655, 0.62), 0.03, 0.012)            # beard
    body = sh.mesh()
    horns = []
    for k in (-1, 1):
        pts = [V(k*0.035, 0.555, 0.915), V(k*0.05, 0.525, 0.98), V(k*0.065, 0.465, 1.025), V(k*0.08, 0.40, 1.035), V(k*0.09, 0.345, 1.01)]
        horns.append(curve_tube('horn', pts, 0.026, 0.006))
    return body, None, horns

# ---- colour, by region -------------------------------------------------------------------------------------------------
def srgb(c): return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
PALETTE = {
    'sheep': dict(body=(0.80, 0.75, 0.65), fleece=(0.90, 0.86, 0.76), face=(0.82, 0.74, 0.62), muzzle=(0.30, 0.24, 0.21),
                  leg=(0.70, 0.60, 0.48), hoof=(0.12, 0.10, 0.09), eye=(0.06, 0.05, 0.05), ear_in=(0.78, 0.60, 0.55)),
    'goat': dict(body=(0.45, 0.32, 0.21), belly=(0.62, 0.52, 0.40), stripe=(0.16, 0.12, 0.10), face=(0.40, 0.29, 0.20),
                 muzzle=(0.22, 0.17, 0.14), leg=(0.15, 0.12, 0.10), hoof=(0.07, 0.06, 0.05), eye=(0.55, 0.42, 0.10),
                 pupil=(0.04, 0.04, 0.04), horn=(0.48, 0.43, 0.36), beard=(0.14, 0.11, 0.09)),
}
def region(kind, part, p, n):
    P = PALETTE[kind]
    if part == 'fleece':
        return P['fleece']
    if part == 'horn': return P['horn']
    x, y, z = p
    eyes = [V(s*0.07, 0.65, 0.80) for s in (-1, 1)] if kind == 'sheep' else [V(s*0.058, 0.60, 0.875) for s in (-1, 1)]
    if any((p - e).length < (0.020 if kind == 'sheep' else 0.014) for e in eyes):
        if kind == 'goat' and any((p - e).length < 0.007 for e in eyes): return P['pupil']
        return P['eye']
    if z < 0.045: return P['hoof']
    if kind == 'sheep':
        if y > 0.69 and z < 0.71: return P['muzzle']
        if y > 0.50 and z > 0.58: return P['face']
        if z < 0.37: return P['leg']
        return P['body']
    if y > 0.62 and z < 0.72 and abs(x) < 0.03 and z < 0.68: return P['beard']
    if y > 0.66 and z < 0.78: return P['muzzle']
    if y > 0.50 and z > 0.72: return P['face']
    if z < 0.37: return P['leg']
    if z > 0.84 and abs(x) < 0.035 and y < 0.30: return P['stripe']
    if z < 0.48 and abs(x) < 0.10: return P['belly']
    return P['body']

def colour(ob, kind, part):
    me = ob.data
    attr = me.color_attributes.get('Col') or me.color_attributes.new('Col', 'FLOAT_COLOR', 'CORNER')
    for poly in me.polygons:
        c = Vector((0, 0, 0));
        for li in poly.loop_indices: c += me.vertices[me.loops[li].vertex_index].co
        c /= len(poly.loop_indices)
        col = region(kind, part, c, poly.normal)
        j = RNG.uniform(-0.035, 0.035) if part != 'horn' else RNG.uniform(-0.02, 0.02)
        col = tuple(min(1, max(0, v + j)) for v in col)
        for li in poly.loop_indices: attr.data[li].color = (*srgb(col), 1)

# ---- skin to the boar's bones ------------------------------------------------------------------------------------------
def skin(ob, arm):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    # (heat can leave a stray vertex unweighted: give it its nearest bone)
    R = L.rest(); heads = {L.bone_name(k): R[k].to_translation() for k in R}
    for v in ob.data.vertices:
        if sum(g.weight for g in v.groups) < 1e-4:
            near = min(heads, key=lambda n: (heads[n] - v.co).length)
            ob.vertex_groups[near].add([v.index], 1.0, 'REPLACE')
    tidy(ob)

# Sides: heat weighting lets a left leg's skin take a little of the right leg's bones (in the fleece, up to three quarters), so a
# walk drags one leg's skin toward the other. Every vertex off the middle keeps only its own side's bones (and the middle ones).
# The head: the boar's head bone sits low in its neck; a goat's or sheep's skull is above it, so a full nod swings the skull round
# like a swan's. The skull and horns ride Head a third, Neck two thirds: grazing lowers the head on a straighter neck.
HEAD_SHARE = 0.65
def sides():
    R = L.rest(); return {L.bone_name(k): (1 if R[k].to_translation().x > 0.02 else -1 if R[k].to_translation().x < -0.02 else 0) for k in R}
def tidy(ob):
    side = sides(); names = {g.index: g.name for g in ob.vertex_groups}
    hy = L.joint('Head').y
    for v in ob.data.vertices:
        s = 1 if v.co.x > 0.05 else -1 if v.co.x < -0.05 else 0
        ws = {names[g.group]: g.weight for g in v.groups if g.weight > 0}
        if s: ws = {n: w for n, w in ws.items() if side.get(n, 0) != -s} or ws
        if v.co.y > hy - 0.03 and v.co.z > 0.55:   # (the skull, ears, muzzle)
            fam = ('Head', 'Jaw', 'L Ear', 'R Ear', 'Eye L', 'Eye R')   # (the head's own bones and its children: all ride Head fully)
            head = sum(w for n, w in ws.items() if n in fam)
            if head > 0:
                ws = {n: w for n, w in ws.items() if n not in fam}
                ws['Head'] = head * (1 - HEAD_SHARE); ws['Neck'] = ws.get('Neck', 0) + head * HEAD_SHARE
        top = sorted(ws.items(), key=lambda kv: -kv[1])[:4]; tot = sum(w for _, w in top) or 1
        for g in list(v.groups): ob.vertex_groups[g.group].remove([v.index])
        for n, w in top: ob.vertex_groups[n].add([v.index], w / tot, 'REPLACE')

def rigid(ob, arm, bone):
    """A part that rides one bone (horns on the head)."""
    for k in range(len(L.RIG['skin']['bones'])): ob.vertex_groups.new(name=L.bone_name(k))
    ids = [v.index for v in ob.data.vertices]
    if bone == 'Head':   # (as the skull: mostly on the neck)
        ob.vertex_groups['Head'].add(ids, 1 - HEAD_SHARE, 'REPLACE'); ob.vertex_groups['Neck'].add(ids, HEAD_SHARE, 'REPLACE')
    else: ob.vertex_groups[bone].add(ids, 1.0, 'REPLACE')
    m = ob.modifiers.new('rig', 'ARMATURE'); m.object = arm; ob.parent = arm

# ---- texture: one small atlas per animal, its colours baked down --------------------------------------------------------
def bake(objs, px, path):
    img = bpy.data.images.new('atlas', px * 2, px * 2, alpha=False)
    for ob in objs:
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs: ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.012, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    mat = bpy.data.materials.new('bake'); mat.use_nodes = True; nt = mat.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission'); ca = nt.nodes.new('ShaderNodeVertexColor')
    ca.layer_name = 'Col'; nt.links.new(ca.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; nt.nodes.active = tex
    for ob in objs: ob.data.materials.clear(); ob.data.materials.append(mat)
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 4; sc.cycles.device = 'CPU'
    sc.render.bake.margin = 4
    bpy.ops.object.bake(type='EMIT')
    col = list(img.pixels)
    # creases: ambient occlusion, gently, each part alone (the fleece mustn't darken the shorn body under it)
    ao = bpy.data.images.new('ao', px * 2, px * 2, alpha=False); tex.image = ao
    sc.cycles.samples = 64
    a = [1.0] * len(col)
    for ob in objs:
        for o in objs: o.hide_render = (o is not ob)
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.bake(type='AO', use_clear=True)
        px_ = list(ao.pixels)
        # (only this part's own texels: those its uvs cover)
        W = px * 2
        mask = coverage(ob, W)
        for i in mask: a[i * 4] = px_[i * 4]
    for o in objs: o.hide_render = False
    for i in range(0, len(col), 4):
        k = 0.72 + 0.28 * a[i]
        col[i] *= k; col[i+1] *= k; col[i+2] *= k
    img.pixels = col
    img.scale(px, px)
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    return img

def coverage(ob, W):
    """The texels an object's uv triangles cover (with a 2-texel margin), as pixel indices."""
    out = set(); uvl = ob.data.uv_layers.active.data; ob.data.calc_loop_triangles()
    for tri in ob.data.loop_triangles:
        p = [uvl[li].uv * W for li in tri.loops]
        x0, x1 = int(min(q.x for q in p)) - 2, int(max(q.x for q in p)) + 3
        y0, y1 = int(min(q.y for q in p)) - 2, int(max(q.y for q in p)) + 3
        for y in range(max(0, y0), min(W, y1)):
            for x in range(max(0, x0), min(W, x1)):
                out.add(y * W + x)
    return out

# ---- export: the boar mesh's own space, Unity's winding, four bones a vertex ----------------------------------------------
def export(ob, path):
    me = ob.data; me.calc_loop_triangles()
    try: me.split_faces()
    except Exception: pass
    uvl = me.uv_layers.active.data
    names = [L.bone_name(k) for k in range(len(L.RIG['skin']['bones']))]
    gidx = {g.index: names.index(g.name) for g in ob.vertex_groups if g.name in names}
    key = {}; V_, N_, U_, BI, BW, T = [], [], [], [], [], []
    normals = me.corner_normals if hasattr(me, 'corner_normals') else None
    for tri in me.loop_triangles:
        ids = []
        for li in tri.loops:
            vi = me.loops[li].vertex_index
            n = (normals[li].vector if normals is not None else me.loops[li].normal)
            uv = uvl[li].uv
            k = (vi, round(n.x, 3), round(n.y, 3), round(n.z, 3), round(uv.x, 4), round(uv.y, 4))
            if k not in key:
                key[k] = len(V_) // 3
                p = L.from_model(me.vertices[vi].co); nn = (L.own().inverted().to_3x3() @ Vector((n.x, n.z, n.y))).normalized()
                V_ += [round(p.x, 5), round(p.y, 5), round(p.z, 5)]; N_ += [round(nn.x, 4), round(nn.y, 4), round(nn.z, 4)]
                U_ += [round(uv.x, 5), round(uv.y, 5)]
                ws = sorted(((g.weight, gidx[g.group]) for g in me.vertices[vi].groups if g.group in gidx and g.weight > 0), reverse=True)[:4]
                tot = sum(w for w, _ in ws) or 1
                ws = ws + [(0, 0)] * (4 - len(ws))
                BI += [b for _, b in ws]; BW += [round(w / tot, 4) for w, _ in ws]
            ids.append(key[k])
        T += [ids[0], ids[2], ids[1]]   # (the mirror into Unity's frame turns the winding)
    with gzip.open(path, 'wt') as f:
        json.dump({'v': V_, 'n': N_, 'uv': U_, 't': T, 'bi': BI, 'bw': BW}, f, separators=(',', ':'))
    return len(V_) // 3, len(T) // 3

# ---- previews ------------------------------------------------------------------------------------------------------------
def preview_material(img):
    mat = bpy.data.materials.new('view'); mat.use_nodes = True; nt = mat.node_tree
    bsdf = nt.nodes.get('Principled BSDF'); tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; tex.interpolation = 'Closest'
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 0.95
    return mat

def pose(arm, which):
    """Test poses, each bend a pitch about the side axis (as a walk or grazing swings them)."""
    pb = arm.pose.bones
    for b in pb: b.rotation_mode = 'QUATERNION'; b.rotation_quaternion = (1, 0, 0, 0)
    def bend(n, deg):
        if n not in pb: return
        rest = arm.data.bones[n].matrix_local.to_3x3()
        q = Quaternion(Vector((1, 0, 0)), math.radians(deg))
        pb[n].rotation_quaternion = (rest.inverted() @ q.to_matrix() @ rest).to_quaternion()
    if which == 'walk':
        # (a walk's stride: diagonal pairs, the left fore with the right hind. The first version swung 25 degrees with the calf bent
        # too, which put a hind hoof ahead of the fore hoof on its side: crossed legs that came from the test pose, not the skin.)
        for n, a in (('L Clavicle', 16), ('R Clavicle', -14), ('L Thigh', -14), ('R Thigh', 16), ('R UpperArm', -18), ('L Calf', 12), ('Neck', 6)):
            bend(n, a)
    elif which == 'graze':
        for n, a in (('Spine2', -8), ('Neck', -40), ('Head', -25)):
            bend(n, a)

def render(path, objs, yaw=35, h=0.55, dist=2.4, size=640, target=None):
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 32
    sc.render.resolution_x = size; sc.render.resolution_y = int(size * 0.75)
    world = bpy.data.worlds.new('w'); sc.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.55, 0.62, 0.7, 1); world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.6
    for o in [o for o in bpy.data.objects if o.type in ('CAMERA', 'LIGHT') or o.name == 'ground']: bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0)); g = bpy.context.active_object; g.name = 'ground'
    gm = bpy.data.materials.new('ground'); gm.use_nodes = True; gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.25, 0.32, 0.15, 1); g.data.materials.append(gm)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = Euler((math.radians(50), 0, math.radians(30)))
    sc.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    t = target if target is not None else Vector((0, 0.05, 0.55)); a = math.radians(yaw)
    cam.location = t + Vector((math.sin(a) * dist, math.cos(a) * dist, h));
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = 50
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)

def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    kind, out = argv[0], argv[1]; os.makedirs(out, exist_ok=True)
    L.load(os.environ.get('BYRE_RIG', os.path.expanduser('~/.cache/byre-rig/boar-rig.json'))); L.clear()
    arm = L.armature()
    body, fleece, extra = sheep() if kind == 'sheep' else goat()
    budget = {'sheep': (1500, 900), 'goat': (1700, 0)}[kind]
    tb = lighten(body, budget[0]); colour(body, kind, 'body')
    parts = [body]
    if fleece is not None:
        tf = lighten(fleece, budget[1]); colour(fleece, kind, 'fleece'); parts.append(fleece)
    for h in extra:
        lighten(h, 160); colour(h, kind, 'horn')
    if extra:
        horns = join(extra, 'horns'); parts.append(horns)
    for o in parts:
        for p in o.data.polygons: p.use_smooth = False   # (flat, as the game's Poly Art animals)
    img = bake(parts, 256, os.path.join(out, f'{kind}_albedo.png'))   # (the body and fleece share one atlas)
    # the rest by heat; horns ride the head
    skin(body, arm)
    if fleece is not None: skin(fleece, arm)
    if extra:
        rigid(horns, arm, 'Head')
        body = join([body, horns], kind); parts = [body] + ([fleece] if fleece else [])
    nb = export(body, os.path.join(out, f'{kind}.json.gz'))
    print('EXPORT', kind, 'body verts/tris', nb)
    if fleece is not None: print('EXPORT fleece verts/tris', export(fleece, os.path.join(out, f'{kind}_fleece.json.gz')))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, f'{kind}.blend'))
    vm = preview_material(img)
    for o in parts: o.data.materials.clear(); o.data.materials.append(vm)
    ref = L.reference(arm); ref.location.x = 1.1; ref.hide_render = True
    for which in ('rest', 'walk', 'graze'):
        pose(arm, which); bpy.context.view_layer.update()
        render(os.path.join(out, f'{kind}_{which}.png'), parts)
    if fleece is not None:
        fleece.hide_render = True; pose(arm, 'rest'); render(os.path.join(out, f'{kind}_shorn.png'), parts)
        fleece.hide_render = False
    pose(arm, 'rest'); render(os.path.join(out, f'{kind}_side.png'), parts, yaw=90, h=0.2)
    render(os.path.join(out, f'{kind}_front.png'), parts, yaw=170, h=0.35)

if __name__ == '__main__':
    main()
