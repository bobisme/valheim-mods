# Builds Gary (the greydwarf rig exported by `gary rig`) in Blender. Unity (x,y,z) maps to Blender (x,z,y): a mirror, so
# matrices convert as M*A*M and stay proper rotations; the export reverses it exactly.
import bpy, json, math, sys
from mathutils import Matrix, Vector, Quaternion

M = Matrix(((1,0,0,0),(0,0,1,0),(0,1,0,0),(0,0,0,1)))
def to_b(m): return M @ m @ M
def trs(p, q, s):
    return Matrix.Translation(Vector(p)) @ Quaternion((q[3], q[0], q[1], q[2])).to_matrix().to_4x4() @ Matrix.Diagonal((s[0], s[1], s[2], 1))
def mat16(a): return Matrix([[a[r*1 + c*4] if False else a[c*4 + r] for c in range(4)] for r in range(4)])

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

def build(path):
    d, bones, world = load(path)
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    body = d['skins'][0]
    skin_w = world[body['transform']]
    # Rest pose of every skinned bone in Gary's space (Unity), from the body's bind poses.
    rest = {}
    for k, bi in enumerate(body['bones']):
        rest[bi] = skin_w @ mat16(body['bind'][k])
    arm_data = bpy.data.armatures.new('GaryRig')
    arm = bpy.data.objects.new('Gary', arm_data)
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
        if p in eb: b.parent = eb[p]
    bpy.ops.object.mode_set(mode='OBJECT')
    # The skins: body, branches, eyes.
    colours = {'greydwarf': (0.33, 0.24, 0.17, 1), 'eye_blue': (0.75, 0.6, 1.0, 1)}
    for s in d['skins']:
        if 'vertices' not in s: continue
        sw = world[s['transform']]
        verts = [tuple(to_b(sw @ Matrix.Translation(Vector(v))).to_translation()) for v in s['vertices']]
        t = s['triangles']; faces = [(t[i], t[i+1], t[i+2]) for i in range(0, len(t), 3)]
        me = bpy.data.meshes.new(s['name']); me.from_pydata(verts, [], faces); me.update()
        ob = bpy.data.objects.new(s['name'], me); bpy.context.scene.collection.objects.link(ob)
        mat = bpy.data.materials.get(s['material']) or bpy.data.materials.new(s['material'])
        mat.diffuse_color = colours.get(s['material'], (0.5, 0.5, 0.5, 1))
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        if bsdf:
            bsdf.inputs['Base Color'].default_value = colours.get(s['material'], (0.5, 0.5, 0.5, 1)); bsdf.inputs['Roughness'].default_value = 0.9
            if s['material'] == 'eye_blue':
                bsdf.inputs['Emission Color'].default_value = (0.8, 0.6, 1, 1); bsdf.inputs['Emission Strength'].default_value = 3
        ob.data.materials.append(mat)
        for k, bi in enumerate(s['bones']):
            if bones[bi]['name'] not in ob.vertex_groups: ob.vertex_groups.new(name=bones[bi]['name'])
        for vi, w in enumerate(s['weights']):
            for j in range(4):
                bi, wt = w[j*2], w[j*2+1]
                if wt > 0: ob.vertex_groups[bones[s['bones'][bi]]['name']].add([vi], wt, 'ADD')
        mod = ob.modifiers.new('rig', 'ARMATURE'); mod.object = arm; ob.parent = arm
        for p in ob.data.polygons: p.use_smooth = False
    return arm

if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:]
    build(argv[0])
    bpy.ops.wm.save_as_mainfile(filepath=argv[1])
