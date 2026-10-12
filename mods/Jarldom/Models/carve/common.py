import bpy, bmesh, json, math
from mathutils import Vector, Matrix

def load_part(path, mat_name):
    d = json.load(open(path))
    part = next(p for p in d['parts'] if p['mats'][0] == mat_name)
    v = part['v']; n = part['n']; uv = part['uv']; tris = part['subs'][0]
    me = bpy.data.meshes.new(mat_name)
    verts = [(v[i], v[i+2], v[i+1]) for i in range(0, len(v), 3)]   # unity (x,y,z) -> blender (x,z,y)
    faces = [(tris[i], tris[i+2], tris[i+1]) for i in range(0, len(tris), 3)]  # mirror: flip winding
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new(name='UV')
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            uvl.data[li].uv = (uv[2*vi], uv[2*vi+1])
    me.update()
    ob = bpy.data.objects.new(mat_name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob, part
