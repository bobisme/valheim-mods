# Exports the dance as Unity local rotations per frame (and the root's local position), the inverse of rig.py's mirror.
import bpy, sys, json
from mathutils import Matrix, Vector
sys.path.insert(0, '/home/bob/.cache/gary-anim')
import rig
argv = sys.argv[sys.argv.index('--') + 1:]
d, bones, world = rig.load(argv[0])
idx = {b['name']: i for i, b in enumerate(bones)}
arm = bpy.data.objects['Gary']; sc = bpy.context.scene
names = [b.name for b in arm.pose.bones if not any(k in b.name for k in ('index', 'middle', 'pinky'))]
parent_full = world[idx['Armature.001']]
def q(m):
    r = m.to_quaternion(); return [round(r.x, 5), round(r.y, 5), round(r.z, 5), round(r.w, 5)]  # Unity order x,y,z,w
rot, root, worst = [], [], 0
for f in range(sc.frame_start, sc.frame_end + 1):
    sc.frame_set(f)
    uw = {pb.name: rig.M @ (arm.matrix_world @ pb.matrix) @ rig.M for pb in arm.pose.bones}
    row = []
    for n in names:
        pb = arm.pose.bones[n]
        pr = uw[pb.parent.name].to_3x3().normalized() if pb.parent else parent_full.to_3x3().normalized()
        local = (pr.inverted() @ uw[n].to_3x3().normalized())
        row.append(q(local))
        if f == sc.frame_start:
            l = bones[idx[n]]['local']['r']; from mathutils import Quaternion
            a = local.to_quaternion().rotation_difference(Quaternion((l[3], l[0], l[1], l[2]))).angle; worst = max(worst, min(a, 6.283185 - a))
    rot.append(row)
    p = parent_full.inverted() @ uw['root'].to_translation()
    root.append([round(p.x, 6), round(p.y, 6), round(p.z, 6)])
json.dump({'name': 'pet_dance', 'fps': sc.render.fps, 'bones': names, 'rot': rot, 'root': root}, open(argv[1], 'w'), separators=(',', ':'))
print('EXPORT', len(rot), 'frames', len(names), 'bones; frame-1 vs captured idle worst', round(worst * 57.2958, 3), 'deg; root0', root[0], 'captured', bones[idx['root']]['local']['p'])
