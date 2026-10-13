# Making a clip in Blender over a captured character (rig.py), for Bob's Animations. A clip script makes a Clip, keys it, and exports it:
#   - turns: small turns of the spine, neck, head or a shoulder, in the character's own axes (x its right, y its front, z up), about each
#     bone's head, parent first; they're exported as turns from the game's own pose ("added"), so the seat's posture shows through;
#   - hands: an IK target for a hand (Blender's IK through hand, forearm and upper arm), placed by where the held thing should be, or the
#     hand as captured; the arm's bones are exported as they come out ("set");
#   - fingers: a curl for a hand's fingers, as a grip.
# Run as: blender -b --python <clip script> -- <rig json> <out dir> [render]
import bpy, sys, math, json, gzip, os
from mathutils import Matrix, Vector, Quaternion
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
FINGERS = ('Index', 'Middle', 'Ring', 'Pinky')

class Clip:
    def __init__(self, name, rig_path, fps=30):
        self.name, self.fps = name, fps
        self.arm = rig.build(rig_path)
        self.d, self.bones, self.world = rig.load(rig_path)
        self.idx = rig.first_index(self.bones)
        sc = bpy.context.scene; sc.render.fps = fps
        bpy.context.view_layer.objects.active = self.arm; bpy.ops.object.mode_set(mode='POSE')
        for pb in self.arm.pose.bones: pb.rotation_mode = 'QUATERNION'
        rig.captured_pose(self.arm, self.bones, self.world)
        self.base = {pb.name: pb.matrix.copy() for pb in self.arm.pose.bones}
        self.base_q = {pb.name: pb.rotation_quaternion.copy() for pb in self.arm.pose.bones}
        self.turned, self.curled, self.targets = set(), set(), {}
        self.turn_keys, self.curl_keys, self.hand_keys = [], [], {}
        self.props, self.last = [], 1

    def P(self, bone, local=Vector()):
        """Where a point of a bone is in the captured pose (local in the bone's own frame, metres)."""
        return self.base[bone] @ local

    # ---- keys ----
    def turn(self, frame, turns):
        """turns: {bone: [(axis, degrees), ...]} at a frame (frames without a bone have it as captured)."""
        self.turn_keys.append((frame, turns)); self.turned |= set(turns); self.last = max(self.last, frame)
    def curl(self, frame, hand, amount):
        """A grip: 0 open as captured, 1 a fist."""
        self.curl_keys.append((frame, hand, amount)); self.curled.add(hand); self.last = max(self.last, frame)
    def hand(self, frame, side, rotation=None, wrist=None):
        """The hand's place: as captured (both None), or its wrist at a point with a rotation (a 3x3 in the armature's frame)."""
        self.hand_keys.setdefault(side, []).append((frame, rotation, wrist)); self.last = max(self.last, frame)
    def hold(self, frame, side, item, centre, up, toward):
        """The hand placed so the held item (its centre and its up and toward-the-item axes on the hand: see hold_item) is at centre, its
        up along up, the hand on its toward side."""
        local_c, local_up, local_side = item
        u = up.normalized(); t = (toward - toward.dot(u) * u).normalized()
        # the hand's rotation R with R @ local_up = u and R @ local_side = t
        a = Matrix((local_up, local_side, local_up.cross(local_side))).transposed()
        b = Matrix((u, t, u.cross(t))).transposed()
        R = b @ a.inverted()
        self.hand(frame, side, R, centre - R @ local_c)
    def prop(self, item, bone, start=0.0, end=None, at=(0, 0, 0), rot=(0, 0, 0)):
        self.props.append({'item': item, 'bone': bone, 'from': start, 'to': end, 'p': list(at), 'r': list(rot)})

    # ---- building the animation ----
    def bake(self):
        arm, sc = self.arm, bpy.context.scene
        sc.frame_start, sc.frame_end = 1, self.last
        # turns and curls: the captured pose with each key's turns, keyed for every turned bone at every key
        frames = sorted(set(f for f, _ in self.turn_keys) | set(f for f, _, _ in self.curl_keys) | {1, self.last})
        def at(keys, f):  # the key in force at f (keys between are interpolated by Blender)
            return next((k for k in keys if k[0] == f), None)
        curl_now = {h: 0.0 for h in self.curled}
        for f in frames:
            rig.captured_pose(arm, self.bones, self.world)
            k = at(self.turn_keys, f); turns = k[1] if k else {}
            for pb in sorted(arm.pose.bones, key=lambda pb: len(pb.parent_recursive)):
                if pb.name not in turns: continue
                m = pb.matrix.copy(); head = m.to_translation(); r = Matrix.Identity(4)
                for axis, deg in turns[pb.name]: r = Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ r
                pb.matrix = Matrix.Translation(head) @ r @ Matrix.Translation(-head) @ m
                bpy.context.view_layer.update()
            for _, hand, amount in [c for c in self.curl_keys if c[0] == f]: curl_now[hand] = amount
            for hand, amount in curl_now.items(): self._curl(hand, amount)
            for n in self.turned | {b.name for h in self.curled for b in arm.pose.bones if b.name.startswith(h) and b.name != h}:
                arm.pose.bones[n].keyframe_insert('rotation_quaternion', frame=f)
        rig.captured_pose(arm, self.bones, self.world)
        # hands: IK on the hand bone (through forearm and upper arm), its target keyed
        for side, keys in self.hand_keys.items():
            hand = arm.pose.bones[side + 'Hand']
            e = bpy.data.objects.new(side + 'Target', None); bpy.context.scene.collection.objects.link(e); e.rotation_mode = 'QUATERNION'
            for f, R, wrist in keys:
                if R is None: m = self.base[hand.name]
                else: m = Matrix.Translation(wrist) @ R.to_4x4()
                tail = m @ Vector((0, hand.length, 0))
                e.location = tail; e.rotation_quaternion = m.to_quaternion()
                e.keyframe_insert('location', frame=f); e.keyframe_insert('rotation_quaternion', frame=f)
            c = hand.constraints.new('IK'); c.target = e; c.chain_count = 3; c.use_rotation = True; c.use_stretch = False
            self.targets[side] = e
        bpy.context.view_layer.update()

    def _curl(self, hand, amount):
        arm = self.arm
        for pb in sorted([b for b in arm.pose.bones if b.name.startswith(hand) and b.name != hand], key=lambda pb: len(pb.parent_recursive)):
            thumb = 'Thumb' in pb.name
            deg = amount * (35 if thumb else 75 if pb.name[-1] == '1' else 85)
            m = pb.matrix.copy()
            pb.matrix = m @ Matrix.Rotation(math.radians(deg), 4, 'X')
            bpy.context.view_layer.update()

    # ---- writing it out: the game's local rotation of each bone per frame; turned bones as turns from the captured pose ----
    def export(self, out, add, sets, loop=False, blend_in=0.5, blend_out=0.5):
        arm, sc = self.arm, bpy.context.scene
        names = list(add) + list(sets)
        def unity_local(pb, mats):
            u = rig.M @ mats[pb.name] @ rig.M
            pu = rig.M @ mats[pb.parent.name] @ rig.M
            return (pu.to_3x3().normalized().inverted() @ u.to_3x3().normalized()).to_quaternion()
        base = {n: unity_local(arm.pose.bones[n], self.base) for n in names}
        rot = []
        for f in range(sc.frame_start, sc.frame_end + 1):
            sc.frame_set(f)
            mats = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
            row = []
            for n in names:
                q = unity_local(arm.pose.bones[n], mats)
                if n in add: q = base[n].inverted() @ q
                row.append([round(q.x, 5), round(q.y, 5), round(q.z, 5), round(q.w, 5)])  # Unity order x, y, z, w
            rot.append(row)
        d = {'name': self.name, 'fps': self.fps, 'loop': loop, 'blendIn': blend_in, 'blendOut': blend_out, 'bones': names,
             'add': [n in add for n in names], 'rot': rot, 'props': [{k: v for k, v in p.items() if v is not None} for p in self.props]}
        gzip.open(os.path.join(out, self.name + '.json.gz'), 'wt').write(json.dumps(d, separators=(',', ':')))
        print('EXPORT', self.name, len(rot), 'frames', len(names), 'bones', round(len(rot) / self.fps, 2), 's')

    # ---- previews: contact sheets, the held item as a stand-in box ----
    def render(self, out, frames, views=('front', 'side', 'three'), stand_ins=()):
        sc = bpy.context.scene
        sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x, sc.render.resolution_y = 360, 420
        w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
        sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(sun)
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
        boxes = []
        for bone, (centre, up, side), size in stand_ins:
            bpy.ops.mesh.primitive_cylinder_add(radius=size[0], depth=size[1], vertices=12)
            o = bpy.context.object; m = bpy.data.materials.new('mug'); m.diffuse_color = (0.6, 0.42, 0.2, 1); o.data.materials.append(m)
            boxes.append((o, bone, centre, up, side))
        bpy.context.view_layer.objects.active = self.arm
        target = self.base['Spine2'].to_translation() + Vector((0, 0.15, -0.25))
        places = {'front': Vector((0, 2.6, 0.2)), 'side': Vector((2.6, 0, 0.2)), 'three': Vector((1.8, 1.9, 0.5)), 'left': Vector((-2.6, 0.4, 0.2))}
        paths = []
        for v in views:
            cam.location = target + places[v]; cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
            for f in frames:
                sc.frame_set(f)
                for o, bone, c, up, s in boxes:
                    m = self.arm.pose.bones[bone].matrix
                    R = m.to_3x3().normalized()
                    o.location = m @ c
                    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = (R @ up).to_track_quat('Z', 'Y')
                p = os.path.join(out, f'{self.name}_{v}_{f:03d}.png'); sc.render.filepath = p; bpy.ops.render.render(write_still=True); paths.append(p)
        return paths

# The game's numbers for an item held in the right hand (BobsAnimations `anim hold`): its centre and its up and toward-it axes, in the
# hand bone's own frame, converted from the game's (x, y, z) to Blender's bone frame (x, z, y). hand_scale: the hand bone's scale there.
def hold_item(centre_unity, up_unity, hand_scale):
    c = Vector(centre_unity) * hand_scale
    to_b = lambda v: Vector((v[0], v[2], v[1]))
    cb = to_b(c); ub = to_b(Vector(up_unity)).normalized()
    side = (cb - cb.dot(ub) * ub).normalized()
    return cb, ub, side
