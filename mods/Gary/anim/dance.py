# Gary's petting dance, keyed over the idle pose captured from the game. Keys are turns in Gary's own axes (Blender: x his right,
# y his forward, z up) about each bone's head, applied parent first, so "sway the hips left" means what it says.
import bpy, sys, math, json
from mathutils import Matrix, Vector, Quaternion
sys.path.insert(0, '/home/bob/.cache/gary-anim')
import rig

FPS = 30
def idle_pose(arm, bones, world):
    order = [b for b in arm.pose.bones]
    order.sort(key=lambda pb: len(pb.parent_recursive))
    idx = {b['name']: i for i, b in enumerate(bones)}
    for pb in order:
        m = rig.to_b(world[idx[pb.name]])
        loc, rot, _ = m.decompose()
        pb.matrix = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
        bpy.context.view_layer.update()

def apply(arm, key):
    order = sorted(arm.pose.bones, key=lambda pb: len(pb.parent_recursive))
    for pb in order:
        turns = key.get(pb.name)
        if not turns: continue
        m = pb.matrix.copy(); head = m.to_translation()
        r = Matrix.Identity(4)
        for axis, deg in turns.get('turn', []):
            r = Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ r
        move = Vector(turns.get('move', (0, 0, 0)))
        pb.matrix = Matrix.Translation(head + move) @ r @ Matrix.Translation(-head) @ m
        bpy.context.view_layer.update()

def keyframe(arm, frame):
    for pb in arm.pose.bones:
        pb.keyframe_insert('rotation_quaternion', frame=frame); pb.keyframe_insert('location', frame=frame)

X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)
def T(*turns, move=None):
    d = {'turn': list(turns)}
    if move: d['move'] = move
    return d

def build_keys():
    # (frame, pose). Beats every 12 frames (0.4 s) after a short wind-up.
    keys = []
    keys.append((0, {}))
    # wind-up: a little crouch, arms tucked
    keys.append((8, {'root': T(move=(0, 0, -0.06)), 'spine2': T((X, 8)), 'l_arm1': T((Y, 15)), 'r_arm1': T((Y, -15))}))
    def sway(side, f, up_arm):
        s = 1 if side == 'r' else -1
        pose = {
            'root': T((Y, 7 * s), move=(0.05 * s, 0, -0.03)),
            'spine1': T((Y, -5 * s), (X, -6)),
            'spine3': T((Z, -10 * s)),
            'head': T((Y, -10 * s), (X, -6)),
            'l_arm1': T((Y, 105 if up_arm == 'l' else 40), (X, 15)),
            'r_arm1': T((Y, -105 if up_arm == 'r' else -40), (X, 15)),
            'l_arm2': T((Y, 35 if up_arm == 'l' else 10)),
            'r_arm2': T((Y, -35 if up_arm == 'r' else -10)),
        }
        lift = 'l_leg1' if side == 'r' else 'r_leg1'
        pose[lift] = T((X, 12))
        pose[lift.replace('1', '2')] = T((X, -18))
        keys.append((f, pose))
        # the bounce between beats: up on the off-beat
        mid = {'root': T((Y, 2 * s), move=(0.02 * s, 0, 0.03)), 'head': T((X, 6)),
               'spine1': T((X, -4)),
               'l_arm1': T((Y, 70), (X, 15)), 'r_arm1': T((Y, -70), (X, 15)), 'l_arm2': T((Y, -20)), 'r_arm2': T((Y, 20))}
        keys.append((f + 6, mid))
    f = 16
    for i, side in enumerate(['r', 'l', 'r', 'l']):
        sway(side, f, 'r' if side == 'r' else 'l'); f += 12
    # a hop with a quarter wiggle, then ta-da
    keys.append((f, {'root': T(move=(0, 0, -0.08)), 'spine2': T((X, 10)), 'l_arm1': T((Y, 20)), 'r_arm1': T((Y, -20))}))
    keys.append((f + 6, {'root': T((Z, 25), move=(0, 0, 0.18)), 'l_arm1': T((Y, 110)), 'r_arm1': T((Y, -110)), 'head': T((X, -15)),
                         'l_leg1': T((X, 20)), 'r_leg1': T((X, 20)), 'l_leg2': T((X, -30)), 'r_leg2': T((X, -30))}))
    keys.append((f + 12, {'root': T(move=(0, 0, -0.04)), 'l_arm1': T((Y, 95), (X, 10)), 'r_arm1': T((Y, -95), (X, 10)), 'head': T((X, -12))}))
    keys.append((f + 22, {'l_arm1': T((Y, 95), (X, 10)), 'r_arm1': T((Y, -95), (X, 10)), 'head': T((X, -12), (Y, 8))}))
    keys.append((f + 30, {}))
    return keys

if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:]
    arm = rig.build(argv[0])
    d, bones, world = rig.load(argv[0])
    sc = bpy.context.scene; sc.render.fps = FPS
    bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='POSE')
    for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
    keys = build_keys()
    for frame, key in keys:
        idle_pose(arm, bones, world); apply(arm, key); keyframe(arm, frame + 1)
    for fc in (arm.animation_data.action.fcurves if hasattr(arm.animation_data.action, 'fcurves') else []):
        for k in fc.keyframe_points: k.interpolation = 'BEZIER'; k.easing = 'AUTO'
    sc.frame_start = 1; sc.frame_end = keys[-1][0] + 1
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.wm.save_as_mainfile(filepath=argv[1])
    print('DANCE frames', sc.frame_end)
