# Renders Gary from a few sides at chosen frames into one contact sheet per view: render.py -- in.blend outdir frames(1,5,9) [views]
import bpy, sys, math, os
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
out = argv[1]; frames = [int(f) for f in argv[2].split(',')]
views = argv[3].split(',') if len(argv) > 3 else ['front', 'side', 'three']
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 360; sc.render.resolution_y = 420; sc.render.film_transparent = False
sc.world = sc.world or bpy.data.worlds.new('w'); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.62, 0.7, 1); sc.world.node_tree.nodes['Background'].inputs[1].default_value = 0.6
if 'Sun' not in bpy.data.objects:
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 3.5
if 'Ground' not in bpy.data.objects:
    bpy.ops.mesh.primitive_plane_add(size=6); g = bpy.context.object; g.name = 'Ground'
    m = bpy.data.materials.new('ground'); m.diffuse_color = (0.3, 0.35, 0.25, 1); g.data.materials.append(m)
cam = bpy.data.objects.get('Cam') or bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
sc.camera = cam; cam.data.lens = 50
target = Vector((0, 0, 0.75))
places = {'front': (0, 3.6, 0.95), 'side': (3.6, 0, 0.95), 'three': (2.5, 2.5, 1.6), 'back': (0, -3.6, 0.95), 'top': (0.6, 1.6, 3.2)}
os.makedirs(out, exist_ok=True)
for v in views:
    cam.location = Vector(places[v]); cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    for f in frames:
        sc.frame_set(f); sc.render.filepath = os.path.join(out, f'{v}-{f:03d}.png'); bpy.ops.render.render(write_still=True)
