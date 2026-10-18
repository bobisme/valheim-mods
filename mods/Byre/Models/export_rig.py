# Export a creature prefab's skeleton + main skinned mesh (+ albedo) from the game's bundle, offline, for Blender.
import UnityPy, sys, json, os
from UnityPy.helpers.MeshHelper import MeshHandler
B, pid, meshname, out = sys.argv[1], int(sys.argv[2]), sys.argv[3], sys.argv[4]
env=UnityPy.load(B)
objs={o.path_id:o for o in env.objects}
def rd(p):
    try: return p.read()
    except Exception: return None
root=objs[pid].read()
def comps(go): return [rd(c.component if hasattr(c,'component') else c) for c in go.m_Components]
def tr_of(go): return next(c for c in comps(go) if type(c).__name__=='Transform')
# walk from Visual
vis=None
for ch in tr_of(root).m_Children:
    g=ch.read().m_GameObject.read()
    if g.m_Name=='Visual': vis=ch.read()
bones=[]; idx_by_pid={}
smr=None
def walk(t, parent):
    g=t.m_GameObject.read()
    i=len(bones); idx_by_pid[t.object_reader.path_id]=i
    p,r,s=t.m_LocalPosition,t.m_LocalRotation,t.m_LocalScale
    bones.append({"name":g.m_Name,"parent":parent,"local":{"p":[p.x,p.y,p.z],"r":[r.x,r.y,r.z,r.w],"s":[s.x,s.y,s.z]}})
    global smr
    for c in comps(g):
        if type(c).__name__=='SkinnedMeshRenderer':
            m=c.m_Mesh.read()
            if m.m_Name==meshname: smr=(c,m,i)
    for ch in t.m_Children: walk(ch.read(), i)
walk(vis,-1)
c,m,owner=smr
h=MeshHandler(m); h.process()
tris=h.get_triangles()
sub=[[i for t in s for i in t] for s in tris]
bidx=[idx_by_pid[b.path_id] for b in c.m_Bones]
bind=[]
for bp in m.m_BindPose:
    bind.append([bp.e00,bp.e01,bp.e02,bp.e03,bp.e10,bp.e11,bp.e12,bp.e13,bp.e20,bp.e21,bp.e22,bp.e23,bp.e30,bp.e31,bp.e32,bp.e33])
weights=[]
if h.m_BoneIndices:
    for bi,bw in zip(h.m_BoneIndices,h.m_BoneWeights): weights.append([[int(a),float(b)] for a,b in zip(bi,bw) if b>0])
else:
    # skin stored in m_Skin (older)
    for sk in m.m_Skin: weights.append([[sk.boneIndex[k],sk.weight[k]] for k in range(4) if sk.weight[k]>0])
mats=[]
for mp in c.m_Materials:
    mat=mp.read(); tex=None
    for k,v in mat.m_SavedProperties.m_TexEnvs:
        if k=='_MainTex' and v.m_Texture and v.m_Texture.path_id:
            t=v.m_Texture.read(); fn=os.path.join(os.path.dirname(out), f"{meshname.replace(' ','_')}_{t.m_Name}.png"); t.image.save(fn); tex=fn
    sh=None
    try: sh=mat.m_Shader.read().m_ParsedForm.m_Name
    except Exception: pass
    mats.append({"name":mat.m_Name,"texture":tex,"shader":sh})
json.dump({"bones":bones,"skin":{"owner":owner,"bones":bidx,"bind":bind,"vertices":[list(v) for v in h.m_Vertices],"normals":[list(v)[:3] for v in h.m_Normals],"uv":[list(v) for v in h.m_UV0],"triangles":sub,"weights":weights,"materials":mats}},open(out,"w"))
print("bones",len(bones),"verts",len(h.m_Vertices),"tris",[len(s)//3 for s in sub],"mats",mats)
