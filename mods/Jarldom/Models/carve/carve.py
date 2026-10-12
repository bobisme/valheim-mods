import sys, json, math; sys.path.insert(0, '/home/bob/.cache/jarldom-carve')
from common import *
from mathutils.bvhtree import BVHTree

# Runes in the younger futhark, strokes in a unit box (x1,y1,x2,y2), y up.
UR=[(.2,0,.2,1),(.2,1,.8,.72),(.8,.72,.8,0)]
THURS=[(.25,0,.25,1),(.25,.78,.78,.5),(.78,.5,.25,.22)]
ISA=[(.5,0,.5,1)]
NAUD=[(.5,0,.5,1),(.18,.68,.82,.32)]
REID=[(.22,0,.22,1),(.22,1,.78,.78),(.78,.78,.22,.52),(.22,.52,.8,0)]
WORDS={'odin':[UR,THURS,ISA,NAUD], 'thor':[THURS,UR,REID]}
H,W,GAP,STROKE,DEPTH=0.26,0.16,0.1,0.04,0.022

def bake(ob, scale, yaw_deg):
    ob.scale = scale; ob.rotation_euler = (0,0,math.radians(-yaw_deg))  # unity yaw about y = -yaw about blender z (mirror)
    bpy.context.view_layer.update(); ob.data.transform(ob.matrix_world); ob.matrix_world = Matrix(); ob.data.update()

def flat_front(ob):
    # turn so the facet nearest the front faces it squarely (-y)
    best=None
    for f in ob.data.polygons:
        if abs(f.normal.z) > 0.2 or f.area < 0.01: continue
        a = math.atan2(f.normal.y, f.normal.x); d = (a - (-math.pi/2) + math.pi) % (2*math.pi) - math.pi
        if best is None or abs(d) < abs(best): best = d
    if best: ob.data.transform(Matrix.Rotation(-best, 4, 'Z')); ob.data.update()

def bounds(ob):
    vs=[v.co for v in ob.data.vertices]
    return Vector((min(v.x for v in vs),min(v.y for v in vs),min(v.z for v in vs))), Vector((max(v.x for v in vs),max(v.y for v in vs),max(v.z for v in vs)))

def box(name, centre, size, rot_y=0.0, mat_index=1, uv_k=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(rot_y, 3, 'Y'))
    bmesh.ops.translate(bm, vec=centre, verts=bm.verts)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    if uv_k:
        uvl = me.uv_layers.new(name='UV')
        for p in me.polygons:
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co; n = p.normal
                u,v = (co.x, co.z) if abs(n.y) > 0.5 else ((co.y, co.z) if abs(n.x) > 0.5 else (co.x, co.y))
                uvl.data[li].uv = (u*uv_k, v*uv_k)
    for p in me.polygons: p.material_index = 0
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob

def cut(target, cutters, mat):
    # all cutters as one object with the given material, then a boolean difference that hands the cutter's material to the new faces
    for c in cutters:
        c.data.materials.append(mat)
    cutter = cutters[0]
    # the strokes made one clean solid first (they cross each other), so the wood itself needn't be checked against itself
    for c in cutters[1:]:
        u = cutter.modifiers.new('u', 'BOOLEAN'); u.operation = 'UNION'; u.solver = 'EXACT'; u.object = c
        bpy.context.view_layer.objects.active = cutter; bpy.ops.object.modifier_apply(modifier='u')
        bpy.data.objects.remove(c)
    m = target.modifiers.new('cut', 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.solver = 'EXACT'; m.object = cutter
    m.use_hole_tolerant = True; m.material_mode = 'TRANSFER'
    bpy.context.view_layer.objects.active = target; bpy.ops.object.modifier_apply(modifier='cut')
    bpy.data.objects.remove(cutter)

def density(ob):
    # texture units per metre on the original faces (for the panel's own uvs)
    me=ob.data; uvl=me.uv_layers[0].data; wa=ua=0
    for p in me.polygons:
        if len(p.loop_indices)!=3: continue
        l=list(p.loop_indices); a,b,c=[me.vertices[me.loops[i].vertex_index].co for i in l]; ua_,ub,uc=[Vector((*uvl[i].uv,0)) for i in l]
        wa+=((b-a).cross(c-a)).length/2; ua+=((ub-ua_).cross(uc-ua_)).length/2
    return math.sqrt(ua/wa) if wa>0 else 1

def carve(src, mat_name, scale, yaw, word, out, panel=False, facet=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ob, part = load_part('/home/bob/.cache/jarldom-carve/'+src, mat_name)
    wood = bpy.data.materials.new('wood'); paint = bpy.data.materials.new('paint'); floor = bpy.data.materials.new('floor')
    ob.data.materials.append(wood)
    bake(ob, scale, yaw)
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)   # (welded at the uv seams: one closed skin)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    if facet: flat_front(ob)
    mn, mx = bounds(ob)
    k = density(ob)
    bm = bmesh.new(); bm.from_mesh(ob.data); tree = BVHTree.FromBMesh(bm); bm.free()
    def surface(x, z):
        hit = tree.ray_cast(Vector((x, mn.y-1, z)), Vector((0,1,0)))
        return hit[0].y if hit[0] else mn.y
    glyphs = WORDS[word]; length = len(glyphs)*H + (len(glyphs)-1)*GAP
    top = mn.z + 2.25
    if panel:
        fy = -0.2  # the panel's floor: below all the carving's relief
        pw, ph = W + 0.12, length + 0.22
        cutters=[box('panel', Vector((0, (fy + mn.y - 0.1)/2, top - length/2)), Vector((pw, abs(fy - (mn.y - 0.1)), ph)), uv_k=k)]
        cut(ob, cutters, floor)
    strokes=[]
    for i, g in enumerate(glyphs):
        y0 = top - i*(H+GAP) - H
        for (x1,v1,x2,v2) in g:
            a = Vector(((x1-.5)*W, 0, y0 + v1*H)); b = Vector(((x2-.5)*W, 0, y0 + v2*H))
            mid = (a+b)/2; s = (-0.2) if panel else surface(mid.x, mid.z)
            d = b - a; L = d.length + STROKE
            ang = math.atan2(d.z, d.x)
            out_y = s - 0.06
            c = box('s', Vector((mid.x, (out_y + s + DEPTH)/2, mid.z)), Vector((L, (s + DEPTH) - out_y, STROKE)), rot_y=-ang)
            strokes.append(c)
    cut(ob, strokes, paint)
    # out: unity frame, per corner position/normal/uv, triangles per material (0 wood, 1 paint, 2 floor)
    me = ob.data
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    me.update()
    names = [m.name for m in me.materials]
    slot = lambda i: {'wood':0,'paint':1,'floor':2}.get(names[i] if i < len(names) else 'wood', 0)
    uvl = me.uv_layers.active.data if me.uv_layers else None
    cn = me.corner_normals
    verts=[]; norms=[]; uvs=[]; tris=[[],[],[]]; index={}
    for p in me.polygons:
        corner=[]
        flat = slot(p.material_index) != 0
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            n = p.normal if flat else cn[li].vector
            uv = tuple(uvl[li].uv) if uvl else (0,0)
            key=(round(co.x,5),round(co.y,5),round(co.z,5),round(n.x,3),round(n.y,3),round(n.z,3),round(uv[0],4),round(uv[1],4))
            if key not in index:
                index[key]=len(verts)//3; verts+= [co.x, co.z, co.y]; norms += [n.x, n.z, n.y]; uvs += [uv[0], uv[1]]
            corner.append(index[key])
        tris[slot(p.material_index)] += [corner[0], corner[2], corner[1]]
    import gzip
    gzip.open('/home/bob/.cache/jarldom-carve/'+out+'.gz','wt').write(json.dumps({'v':[round(x,5) for x in verts],'n':[round(x,4) for x in norms],'uv':[round(x,5) for x in uvs],'t':tris}, separators=(',',':')))
    print(out, 'verts', len(verts)//3, 'tris', [len(t)//3 for t in tris])

# (scales: each shaft as tall as the pillars' fixed height needs: 4.2 m to the top of everything at every rank)
for word in ['odin','thor']:
    carve('log.json','logwall',(1.15,1.15,0.947),0,word,f'log_{word}.json',facet=True)
    carve('pole.json','woodpole2',(1.25,1.25,2.1),0,word,f'pole_{word}.json')
    carve('log.json','logwall',(1.2,1.2,0.82),0,word,f'log2_{word}.json',facet=True)
    carve('dark.json','DarkWood_mat',(1,1,0.757),0 if word=='odin' else 180,word,f'dark_{word}.json',panel=True)
