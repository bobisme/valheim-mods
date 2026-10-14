# Making a clip in Blender over a captured character (rig.py), for Bob's Animations. A clip script makes a Clip, keys it, and exports it:
#   - turn: small turns of the spine, neck, head, jaw or a shoulder, in the character's own axes (X its right, Y its front, Z up), about
#     each bone's head, parent first; exported as turns from the game's own pose ("added"), so the seat's posture shows through;
#   - hands: an IK target for a hand (Blender's IK through hand, forearm and upper arm): the hand as captured, a held item placed (hold),
#     or the wrist at a point with the fingers pointing one way and the palm facing another (pose); the arm is exported as it comes out;
#   - curl: a grip, for the whole hand or finger by finger.
# A Clip made with mirror=True is its own mirror image (left for right): write a clip once, toward the character's right, and make both.
# Run as: blender -b --python <clip script> -- <rig json> <out dir> [render]
import bpy, sys, math, json, gzip, os
from mathutils import Matrix, Vector, Quaternion
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig

X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)
FINGERS = ('Thumb', 'Index', 'Middle', 'Ring', 'Pinky')
SEATS = ('attach_chair', 'attach_throne')

class Clip:
    def __init__(self, name, rig_path, fps=30, mirror=False):
        self.name, self.fps, self.mirror = name, fps, mirror
        self.arm = rig.build(rig_path)
        self.d, self.bones, self.world = rig.load(rig_path)
        self.idx = rig.first_index(self.bones)
        sc = bpy.context.scene; sc.render.fps = fps
        bpy.context.view_layer.objects.active = self.arm; bpy.ops.object.mode_set(mode='POSE')
        for pb in self.arm.pose.bones: pb.rotation_mode = 'QUATERNION'
        rig.captured_pose(self.arm, self.bones, self.world)
        self.base = {pb.name: pb.matrix.copy() for pb in self.arm.pose.bones}
        self.turned, self.curled = set(), set()
        self.hip_moves, self.pinned = {}, False
        self.turn_keys, self.curl_keys, self.hand_keys = [], [], {}
        self.props, self.last = [], 1

    # ---- mirroring: positions and directions across the character's middle, left for right, turns about Y and Z the other way ----
    def m(self, v):
        v = Vector(v); return Vector((-v.x, v.y, v.z)) if self.mirror else v
    def side(self, s):
        if not self.mirror: return s
        return s.replace('Right', '\0').replace('Left', 'Right').replace('\0', 'Left')
    def P(self, bone, local=(0, 0, 0)):
        """Where a point of a bone is in the captured pose (local in the bone's own frame, metres), unmirrored."""
        return self.base[bone] @ Vector(local)

    # ---- keys ----
    def turn(self, frame, turns, hips=None):
        """turns: {bone: [(axis, degrees), ...]} at a frame (frames without a bone have it as captured); hips: the hips moved by
        (x, y, z) metres in the character's axes (crouching: z down), the feet kept where they stand (pin_feet)."""
        if hips is not None: self.hip_moves[frame] = self.m(hips); turns = dict(turns); turns.setdefault('Hips', [])
        t = {}
        for bone, list_ in turns.items():
            t[self.side(bone)] = [(axis, -deg if self.mirror and axis != X else deg) for axis, deg in list_]
        self.turn_keys.append((frame, t)); self.turned |= set(t); self.last = max(self.last, frame)
    def curl(self, frame, hand, amount, **fingers):
        """A grip: amount 0 open as captured, 1 a fist; a finger named (Index=0) gets its own."""
        hand = self.side(hand)
        self.curl_keys.append((frame, hand, {f: fingers.get(f, amount) for f in FINGERS})); self.curled.add(hand); self.last = max(self.last, frame)
    def hand(self, frame, side, rotation=None, wrist=None):
        """The hand's place: as captured (both None), or its wrist at a point with a rotation (a 3x3, already mirrored)."""
        self.hand_keys.setdefault(self.side(side), []).append((frame, rotation, wrist)); self.last = max(self.last, frame)
    def pose(self, frame, side, wrist, fingers, palm):
        """The wrist at a point, the fingers pointing one way, the palm facing another (the character's axes, written for the right)."""
        z = self.m(fingers).normalized(); y = self.m(palm); y = (y - y.dot(z) * z).normalized(); x = y.cross(z)
        self.hand(frame, side, Matrix((x, y, z)).transposed(), self.m(wrist))
    def hold(self, frame, side, item, centre, up, toward):
        """The hand placed so the held item (hold_item: its centre and up and toward-the-item axes on the hand) is at centre, up along up."""
        local_c, local_up, local_side = item
        u = self.m(up).normalized(); t = self.m(toward); t = (t - t.dot(u) * u).normalized()
        a = Matrix((local_up, local_side, local_up.cross(local_side))).transposed()
        b = Matrix((u, t, u.cross(t))).transposed()
        R = b @ a.inverted()
        self.hand(frame, side, R, self.m(centre) - R @ local_c)
    def prop(self, item, bone, start=0.0, end=None, at=(0, 0, 0), rot=(0, 0, 0), swap=False, scale=1.0):
        """An item in a hand while the clip plays; swap: the item given when it's played (PlayWith) takes its place."""
        self.props.append({'item': item, 'bone': self.side(bone), 'from': start, 'to': end, 'p': list(at), 'r': list(rot), 'swap': swap or None,
                           'scale': scale if scale != 1.0 else None})
    def pin_feet(self):
        """The feet stay planted where they were captured (IK through foot, shin and thigh), so moving the hips bends the knees."""
        self.pinned = True

    # ---- building the animation ----
    def bake(self):
        arm, sc = self.arm, bpy.context.scene
        sc.frame_start, sc.frame_end = 1, self.last
        frames = sorted(set(f for f, _ in self.turn_keys) | set(f for f, _, _ in self.curl_keys) | {1, self.last})
        curl_now = {h: {f: 0.0 for f in FINGERS} for h in self.curled}
        finger_bones = [b.name for h in self.curled for b in arm.pose.bones if b.name.startswith(h) and b.name != h]
        for f in frames:
            rig.captured_pose(arm, self.bones, self.world)
            turns = next((t for k, t in self.turn_keys if k == f), {})
            if self.hip_moves:
                hp = arm.pose.bones['Hips']; hp.matrix = Matrix.Translation(self.hip_moves.get(f, Vector())) @ hp.matrix.copy(); bpy.context.view_layer.update()
            for pb in sorted(arm.pose.bones, key=lambda pb: len(pb.parent_recursive)):
                if pb.name not in turns: continue
                m = pb.matrix.copy(); head = m.to_translation(); r = Matrix.Identity(4)
                for axis, deg in turns[pb.name]: r = Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ r
                pb.matrix = Matrix.Translation(head) @ r @ Matrix.Translation(-head) @ m
                bpy.context.view_layer.update()
            for _, hand, amounts in [c for c in self.curl_keys if c[0] == f]: curl_now[hand] = amounts
            for hand, amounts in curl_now.items(): self._curl(hand, amounts)
            # each kind of key touches only its own bones (a grip's key mustn't put the spine back between two of its keys)
            turn_frame = any(k == f for k, _ in self.turn_keys) or f in (1, self.last)
            curl_frame = any(c[0] == f for c in self.curl_keys) or f in (1, self.last)
            for n in (self.turned if turn_frame else set()) | (set(finger_bones) if curl_frame else set()):
                arm.pose.bones[n].keyframe_insert('rotation_quaternion', frame=f)
            if f in self.hip_moves or self.hip_moves and f in (1, self.last): arm.pose.bones['Hips'].keyframe_insert('location', frame=f)
        rig.captured_pose(arm, self.bones, self.world)
        # hands: where the clip wants each hand (an empty, keyed and blended by Blender), then the arm placed by plain two-bone
        # geometry every frame (Blender's IK flips between elbow solutions from frame to frame: smooth hands, arms spinning)
        targets = {}
        for side, keys in self.hand_keys.items():
            hand = arm.pose.bones[side + 'Hand']
            e = bpy.data.objects.new(side + 'Target', None); bpy.context.scene.collection.objects.link(e); e.rotation_mode = 'QUATERNION'
            prev = None
            for f, R, wrist in sorted(keys, key=lambda k: k[0]):
                m = self.base[hand.name] if R is None else Matrix.Translation(wrist) @ R.to_4x4()
                q = m.to_quaternion()
                # (each key on the same side as the last: q and -q are one turn, but keys are blended number by number)
                if prev is not None and prev.dot(q) < 0: q.negate()
                prev = q
                e.location = m.to_translation(); e.rotation_quaternion = q
                e.keyframe_insert('location', frame=f); e.keyframe_insert('rotation_quaternion', frame=f)
            targets[side] = e
        if targets:
            for f in range(sc.frame_start, sc.frame_end + 1):
                sc.frame_set(f)
                for side, e in targets.items(): self._place_arm(side, e.matrix_world.to_translation(), e.matrix_world.to_quaternion())
                for side in targets:
                    for b in ('Arm', 'ForeArm', 'Hand'): arm.pose.bones[side + b].keyframe_insert('rotation_quaternion', frame=f)
        if self.pinned:
            for side in ('Left', 'Right'):
                foot = arm.pose.bones[side + 'Foot']; m = self.base[foot.name]
                e = bpy.data.objects.new(side + 'FootTarget', None); bpy.context.scene.collection.objects.link(e)
                e.matrix_world = Matrix.Translation(m @ Vector((0, foot.length, 0))) @ m.to_quaternion().to_matrix().to_4x4()
                c = foot.constraints.new('IK'); c.target = e; c.chain_count = 3; c.use_rotation = True; c.use_stretch = False
        bpy.context.view_layer.update()

    def _place_arm(self, side, wrist, hand_rot):
        """The arm reaching the wrist with the hand turned as asked: the elbow on the side it was captured on, each bone swung (never
        rolled) from its captured direction, as its parent now carries it."""
        arm = self.arm; up_b, fo_b, ha_b = (arm.pose.bones[side + n] for n in ('Arm', 'ForeArm', 'Hand'))
        B = self.base
        S0, E0, W0 = B[up_b.name].to_translation(), B[fo_b.name].to_translation(), B[ha_b.name].to_translation()
        L1, L2 = (E0 - S0).length, (W0 - E0).length
        # the captured arm as its parent (the shoulder, maybe turned) carries it now
        carry = up_b.parent.matrix @ B[up_b.parent.name].inverted()
        S = carry @ S0
        A0 = carry @ B[up_b.name]
        d = max(abs(L1 - L2) + 1e-3, min(L1 + L2 - 1e-3, (wrist - S).length))
        line = (wrist - S).normalized()
        out = carry.to_3x3() @ (E0 - S0); out = out - out.dot(line) * line
        if out.length < 1e-4: out = Vector((1 if side == 'Right' else -1, -0.3, -0.3)); out = out - out.dot(line) * line
        out.normalize()
        a = (L1 * L1 - L2 * L2 + d * d) / (2 * d); h = math.sqrt(max(0.0, L1 * L1 - a * a))
        E = S + line * a + out * h; W = S + line * d
        def swing(m, frm, to):
            r = frm.normalized().rotation_difference(to.normalized())
            return r.to_matrix() @ m.to_3x3().normalized()
        A_rot = swing(A0, (carry @ E0) - S, E - S)
        up_b.matrix = Matrix.Translation(S) @ A_rot.to_4x4(); bpy.context.view_layer.update()
        F0 = up_b.matrix @ (B[up_b.name].inverted() @ B[fo_b.name])
        F_rot = swing(F0, (up_b.matrix @ (B[up_b.name].inverted() @ W0)) - E, W - E)
        fo_b.matrix = Matrix.Translation(E) @ F_rot.to_4x4(); bpy.context.view_layer.update()
        ha_b.matrix = Matrix.Translation(W) @ hand_rot.to_matrix().to_4x4(); bpy.context.view_layer.update()

    def _curl(self, hand, amounts):
        # From straight fingers (the skeleton's rest: a captured hand may be curled over a knee), then bent toward the palm.
        fingers = sorted([b for b in self.arm.pose.bones if b.name.startswith(hand) and b.name != hand], key=lambda pb: len(pb.parent_recursive))
        for pb in fingers: pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
        for pb in fingers:
            finger = next(f for f in FINGERS if f in pb.name)
            deg = amounts[finger] * (35 if finger == 'Thumb' else 75 if pb.name[-1] == '1' else 85)
            pb.matrix = pb.matrix.copy() @ Matrix.Rotation(math.radians(-deg), 4, 'X')  # (negative: on this skeleton a finger bends toward the palm about -x)
            bpy.context.view_layer.update()

    # ---- writing it out: the game's local rotation of each bone per frame; turned bones as turns from the captured pose ----
    def export(self, out, add=None, sets=None, loop=False, blend_in=0.5, blend_out=0.5, seated=True, still=False, moving=False):
        arm, sc = self.arm, bpy.context.scene
        if add is None: add = sorted(self.turned)
        if sets is None:
            # (the shoulder too, unless turned: walking swings the shoulders, and an arm set from a swung shoulder points wherever it swung)
            sets = [s + b for s in self.hand_keys for b in ('Shoulder', 'Arm', 'ForeArm', 'Hand') if s + b not in self.turned]
            if self.pinned: sets += [s + b for s in ('Left', 'Right') for b in ('UpLeg', 'Leg', 'Foot')]
            sets += sorted(b.name for h in self.curled for b in arm.pose.bones if b.name.startswith(h) and b.name != h)
        names = list(add) + [n for n in sets if n not in add]
        def unity_local(pb, mats):
            u = rig.M @ mats[pb.name] @ rig.M
            # (the hips' parent is the game's Armature, not a bone here: as captured)
            pu = rig.M @ mats[pb.parent.name] @ rig.M if pb.parent else self.world[self.bones[self.idx[pb.name]]['parent']]
            return (pu.to_3x3().normalized().inverted() @ u.to_3x3().normalized()).to_quaternion()
        base = {n: unity_local(arm.pose.bones[n], self.base) for n in names}
        rot, hips = [], []
        # the hips' place as a shift in their parent's own units (the parent, the armature, isn't animated: as captured, scale and all)
        parent = self.world[self.bones[self.idx['Hips']]['parent']]
        hips_base = (rig.M @ self.base['Hips'] @ rig.M).to_translation()
        for f in range(sc.frame_start, sc.frame_end + 1):
            sc.frame_set(f)
            mats = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
            if self.hip_moves:
                d = parent.to_3x3().inverted() @ ((rig.M @ mats['Hips'] @ rig.M).to_translation() - hips_base)
                hips.append([round(d.x, 6), round(d.y, 6), round(d.z, 6)])
            row = []
            for n in names:
                q = unity_local(arm.pose.bones[n], mats)
                if n in add: q = base[n].inverted() @ q
                row.append([round(q.x, 5), round(q.y, 5), round(q.z, 5), round(q.w, 5)])  # Unity order x, y, z, w
            rot.append(row)
        d = {'name': self.name, 'fps': self.fps, 'loop': loop, 'blendIn': blend_in, 'blendOut': blend_out, 'seated': seated, 'still': still, 'moving': moving, 'bones': names,
             'add': [n in add for n in names], 'rot': rot, 'props': [{k: v for k, v in p.items() if v is not None} for p in self.props]}
        if self.hip_moves: d['pos'] = {'Hips': hips}
        gzip.open(os.path.join(out, self.name + '.json.gz'), 'wt').write(json.dumps(d, separators=(',', ':')))
        print('EXPORT', self.name, len(rot), 'frames', len(names), 'bones', round(len(rot) / self.fps, 2), 's')

    # ---- previews: contact sheets, a held item as a stand-in cylinder, an optional table top ----
    def render(self, out, frames, views=('front', 'side', 'three'), stand_ins=(), table=None):
        sc = bpy.context.scene
        sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x, sc.render.resolution_y = 360, 420
        w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1)
        sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(sun)
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 50
        boxes = []
        for bone, (centre, up, side), size in stand_ins:
            bpy.ops.mesh.primitive_cylinder_add(radius=size[0], depth=size[1], vertices=12)
            o = bpy.context.object; mt = bpy.data.materials.new('mug'); mt.diffuse_color = (0.6, 0.42, 0.2, 1); o.data.materials.append(mt)
            boxes.append((o, self.side(bone), centre, up))
        if table:
            (cx, cy, top), (sx, sy) = table
            bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, top - 0.03)); t = bpy.context.object; t.scale = (sx, sy, 0.06)
        bpy.context.view_layer.objects.active = self.arm
        target = self.base['Spine2'].to_translation() + Vector((0, 0.15, -0.25))
        places = {'front': Vector((0, 2.6, 0.2)), 'side': Vector((2.6, 0, 0.2)), 'three': Vector((1.8, 1.9, 0.5)), 'left': Vector((-2.6, 0.4, 0.2)),
                  'lthree': Vector((-1.8, 1.9, 0.5))}
        for v in views:
            cam.location = target + places[v]; cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
            for f in frames:
                sc.frame_set(f)
                for o, bone, c, up in boxes:
                    mb = self.arm.pose.bones[bone].matrix; R = mb.to_3x3().normalized()
                    o.location = mb @ c; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = (R @ up).to_track_quat('Z', 'Y')
                sc.render.filepath = os.path.join(out, f'{self.name}_{v}_{f:03d}.png'); bpy.ops.render.render(write_still=True)

# The game's numbers for an item held in the right hand (`anim hold`): its centre and its up axis in the hand bone's own frame, converted
# from the game's (x, y, z) to Blender's bone frame (x, z, y). hand_scale: the hand bone's scale there.
def hold_item(centre_unity, up_unity, hand_scale):
    c = Vector(centre_unity) * hand_scale
    to_b = lambda v: Vector((v[0], v[2], v[1]))
    cb = to_b(c); ub = to_b(Vector(up_unity)).normalized()
    side = (cb - cb.dot(ub) * ub).normalized()
    return cb, ub, side
TANKARD = hold_item((0.0001, 0.0021, 0.0008), (-1, 0, 0), 85.5)

# Where things are on the captured seated body (Rognvald in a chair; Blender, character's axes, metres).
MOUTH = Vector((0.0, 0.19, 1.28))
CHIN = Vector((0.0, 0.16, 1.21))

def run(make):
    """A clip script's main: blender ... -- <rig json> <out dir> [render] [mirror] calls make(rig_path, out, render, mirror)."""
    argv = sys.argv[sys.argv.index('--') + 1:]
    make(argv[0], argv[1], 'render' in argv, 'mirror' in argv)
