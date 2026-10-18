# Byre's Blender kit: the boar's skeleton (exported offline from the game bundle by export_rig.py) as an armature, its mesh as a
# reference, and helpers to make, skin and export our animals on that same skeleton, in the boar mesh's own space, so the game's
# boar animations drive them unchanged. Unity (x,y,z) -> Blender (x,z,y): a mirror; matrices convert as M*A*M.
import bpy, bmesh, json, math, random
from mathutils import Matrix, Vector, Quaternion

M = Matrix(((1,0,0,0),(0,0,1,0),(0,1,0,0),(0,0,0,1)))
def to_b(m): return M @ m @ M
def v_b(v): return Vector((v[0], v[2], v[1]))
def rows(a): return Matrix([a[0:4], a[4:8], a[8:12], a[12:16]])

RIG = None
def load(path):
    global RIG
    d = json.load(open(path)); RIG = d
    return d

def clear():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)
    for m in list(bpy.data.materials): bpy.data.materials.remove(m)

def trs(p, q, s):
    return Matrix.Translation(Vector(p)) @ Quaternion((q[3], q[0], q[1], q[2])).to_matrix().to_4x4() @ Matrix.Diagonal((s[0], s[1], s[2], 1))
def own():
    """The skinned mesh's transform in the Visual (Unity): the boar's mesh is stored Z-up and turned -90 about x."""
    l = RIG['bones'][RIG['skin']['owner']]['local']
    return trs(l['p'], l['r'], l['s'])
def to_model(v):
    """A mesh-space point (Unity) to the modelling frame: the Visual's space in Blender axes (x right, y forward, z up)."""
    return v_b(own() @ Vector(v))
def from_model(v):
    """Back: a modelling-frame point (Blender) to the boar mesh's own space (Unity), as the game's bind poses expect."""
    return own().inverted() @ Vector((v[0], v[2], v[1]))
def rest():
    """Each skinned bone's rest matrix in the modelling frame, from the bind poses: index into skin.bones -> matrix."""
    s = RIG['skin']; O = own()
    return {k: to_b(O @ rows(s['bind'][k]).inverted()) for k in range(len(s['bones']))}

def bone_name(k): return RIG['bones'][RIG['skin']['bones'][k]]['name']

def parents():
    s = RIG['skin']; idx = {bi: k for k, bi in enumerate(s['bones'])}; out = {}
    for k, bi in enumerate(s['bones']):
        p = RIG['bones'][bi]['parent']
        while p >= 0 and p not in idx: p = RIG['bones'][p]['parent']
        out[k] = idx.get(p)
    return out

def armature(name='Rig'):
    R = rest(); names = {k: bone_name(k) for k in R}; parent_of = parents()
    ad = bpy.data.armatures.new(name); arm = bpy.data.objects.new(name, ad)
    bpy.context.scene.collection.objects.link(arm); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for k, m in R.items():
        loc, rot, _ = m.decompose()
        kids = [R[c].to_translation() for c in R if parent_of[c] == k]
        length = max(0.03, min((kc - loc).length for kc in kids)) if kids else 0.06
        b = ad.edit_bones.new(names[k]); b.head = (0, 0, 0); b.tail = (0, length, 0)
        b.matrix = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
        eb[k] = b
    for k, b in eb.items():
        if parent_of[k] is not None: b.parent = eb[parent_of[k]]
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm

def joint(name):
    """A bone's rest head in mesh space (Blender frame)."""
    R = rest()
    for k in R:
        if bone_name(k) == name: return R[k].to_translation()
    raise KeyError(name)

def reference(arm, colour=(0.45, 0.35, 0.28, 1)):
    s = RIG['skin']
    verts = [to_model(v) for v in s['vertices']]
    faces = []
    for t in s['triangles']:
        faces += [(t[i], t[i+2], t[i+1]) for i in range(0, len(t), 3)]
    me = bpy.data.meshes.new('boar'); me.from_pydata(verts, [], faces); me.update()
    ob = bpy.data.objects.new('boar', me); bpy.context.scene.collection.objects.link(ob)
    mat = bpy.data.materials.new('boarref'); mat.diffuse_color = colour; ob.data.materials.append(mat)
    for k in range(len(s['bones'])): ob.vertex_groups.new(name=bone_name(k))
    for vi, w in enumerate(s['weights']):
        for bi, wt in w: ob.vertex_groups[bone_name(bi)].add([vi], wt, 'REPLACE')
    mod = ob.modifiers.new('rig', 'ARMATURE'); mod.object = arm; ob.parent = arm
    return ob
