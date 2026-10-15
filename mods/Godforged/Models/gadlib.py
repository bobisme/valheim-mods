# Small extras for the gadget models on top of wulib: copies turned about an axis, and tubes swept along a path (cables).
import bpy, bmesh, math
from mathutils import Vector, Matrix
from wulib import mat, _obj, _bevel

def spin_copy(objs, angle_deg, centre, axis='Y', suffix='r'):
    """Copies of objs turned angle about an axis through centre (a claw repeated round a barrel)."""
    out = []
    bpy.context.view_layer.update()   # (a new object's matrix_world is stale until the scene updates)
    M = Matrix.Translation(Vector(centre)) @ Matrix.Rotation(math.radians(angle_deg), 4, axis) @ Matrix.Translation(-Vector(centre))
    for o in objs:
        c = o.copy(); c.data = o.data.copy(); bpy.context.scene.collection.objects.link(c)
        c.matrix_world = M @ o.matrix_world
        c.name = o.name + suffix
        out.append(c)
    return out

def transform(objs, M):
    for o in objs: o.matrix_world = M @ o.matrix_world

def tube_path(name, points, radius, material='rubber', ring=10, caps=True):
    """A round tube swept through points (a cable or hose), rings turned to follow the path."""
    pts = [Vector(p) for p in points]
    me = bpy.data.meshes.new(name); bm = bmesh.new(); rings = []
    prev_n = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        n = prev_n if prev_n is not None else (Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0)))
        n = (n - t * n.dot(t)).normalized(); prev_n = n; b = t.cross(n)
        rings.append([bm.verts.new(p + (n * math.cos(2 * math.pi * k / ring) + b * math.sin(2 * math.pi * k / ring)) * radius) for k in range(ring)])
    for a, c in zip(rings, rings[1:]):
        for k in range(ring):
            j = (k + 1) % ring; bm.faces.new((a[k], a[j], c[j], c[k]))
    if caps: bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return _obj(name, me, material)

def report(top=12):
    """The objects with the most triangles (after modifiers), to see where the budget goes."""
    deps = bpy.context.evaluated_depsgraph_get(); rows = []
    for o in bpy.context.scene.collection.objects:
        if o.type != 'MESH': continue
        me = o.evaluated_get(deps).to_mesh(); rows.append((sum(len(p.vertices) - 2 for p in me.polygons), o.name)); o.evaluated_get(deps).to_mesh_clear()
    rows.sort(reverse=True); total = sum(r[0] for r in rows)
    print('TRIS total', total, ' '.join(f'{n}:{t}' for t, n in rows[:top]))

def bezier(p0, p1, p2, p3, n=12):
    out = []
    for i in range(n + 1):
        t = i / n; u = 1 - t
        out.append(tuple(u ** 3 * a + 3 * u * u * t * b + 3 * u * t * t * c + t ** 3 * d for a, b, c, d in zip(p0, p1, p2, p3)))
    return out
