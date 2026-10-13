# Builds a character captured by `anim rig` (claude/shots/anim-rig.json) in Blender: an armature from its skin's bind poses, posed as it
# was captured, and its skins weighted to it, for previews. Unity (x,y,z) maps to Blender (x,z,y): a mirror, so matrices convert as M*A*M
# and stay proper rotations; export.py reverses it exactly. The character faces +y in Blender (Unity's +z), its right is +x.
import bpy, json, sys
from mathutils import Matrix, Vector, Quaternion

M = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
def to_b(m): return M @ m @ M
def trs(p, q, s):
    return Matrix.Translation(Vector(p)) @ Quaternion((q[3], q[0], q[1], q[2])).to_matrix().to_4x4() @ Matrix.Diagonal((s[0], s[1], s[2], 1))
def mat16(a): return Matrix([[a[c * 4 + r] for c in range(4)] for r in range(4)])

def load(path):
    d = json.load(open(path))
    bones = d['bones']
    world = {}
    def W(i):
        if i in world: return world[i]
        b = bones[i]; l = b['local']
        m = trs(l['p'], l['r'], l['s'])
        if b['parent'] >= 0: m = W(b['parent']) @ m
        world[i] = m; return m
    for i in range(len(bones)): W(i)
    return d, bones, world

COLOURS = {'body': (0.75, 0.55, 0.42, 1), 'beard': (0.35, 0.22, 0.12, 1), 'hair': (0.35, 0.22, 0.12, 1)}
def build(path):
    d, bones, world = load(path)
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    body = d['skins'][0]
    skin_w = world[body['transform']]
    # Rest pose of every bone the body is skinned to (in the character's space), from its bind poses.
    rest = {}
    for k, bi in enumerate(body['bones']):
        rest[bi] = skin_w @ mat16(body['bind'][k])
    arm_data = bpy.data.armatures.new('Rig')
    arm = bpy.data.objects.new('Rig', arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for bi, m in rest.items():
        name = bones[bi]['name']
        loc, rot, sc = to_b(m).decompose()
        b = arm_data.edit_bones.new(name)
        kids = [rest[c].to_translation() for c in rest if bones[c]['parent'] == bi]
        length = max(0.03, min((to_b(Matrix.Translation(k)).to_translation() - loc).length for k in kids)) if kids else 0.08
        b.head = (0, 0, 0); b.tail = (0, length, 0)
        b.matrix = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
        eb[bi] = b
    for bi, b in eb.items():
        p = bones[bi]['parent']
        while p >= 0 and p not in eb: p = bones[p]['parent']
        if p in eb: b.parent = eb[p]
    bpy.ops.object.mode_set(mode='OBJECT')
    for s in d['skins']:
        if 'vertices' not in s: continue
        sw = world[s['transform']]
        verts = [tuple(to_b(sw @ Matrix.Translation(Vector(v))).to_translation()) for v in s['vertices']]
        t = s['triangles']; faces = [(t[i], t[i + 1], t[i + 2]) for i in range(0, len(t), 3)]
        me = bpy.data.meshes.new(s['name']); me.from_pydata(verts, [], faces); me.update()
        ob = bpy.data.objects.new(s['name'], me); bpy.context.scene.collection.objects.link(ob)
        colour = next((c for k, c in COLOURS.items() if k in s['name'].lower()), (0.55, 0.5, 0.25, 1))
        mat = bpy.data.materials.new(s['name']); mat.diffuse_color = colour; mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        if bsdf: bsdf.inputs['Base Color'].default_value = colour; bsdf.inputs['Roughness'].default_value = 0.9
        ob.data.materials.append(mat)
        # A worn skin's bones are the body's (the game puts them on the body's skeleton): by name.
        for k, bi in enumerate(s['bones']):
            if bones[bi]['name'] not in ob.vertex_groups: ob.vertex_groups.new(name=bones[bi]['name'])
        for vi, w in enumerate(s['weights']):
            for j in range(4):
                bi, wt = w[j * 2], w[j * 2 + 1]
                if wt > 0: ob.vertex_groups[bones[s['bones'][bi]]['name']].add([vi], wt, 'ADD')
        mod = ob.modifiers.new('rig', 'ARMATURE'); mod.object = arm; ob.parent = arm
    return arm

# Bone indices by name, the first of each (the body's own skeleton comes before the copies worn clothes carry, which nothing moves).
def first_index(bones):
    idx = {}
    for i, b in enumerate(bones): idx.setdefault(b['name'], i)
    return idx

# The captured pose (each bone's pose now, from the capture's local transforms), set on the armature parent first.
def captured_pose(arm, bones, world):
    order = sorted(arm.pose.bones, key=lambda pb: len(pb.parent_recursive))
    idx = first_index(bones)
    for pb in order:
        loc, rot, _ = to_b(world[idx[pb.name]]).decompose()
        pb.matrix = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
        bpy.context.view_layer.update()
