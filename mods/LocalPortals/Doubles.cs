using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace LocalPortals
{
    // Bodies partway through a portal. The real body would poke out of the back of the mirror it is walking into, so while
    // someone straddles a glass their own model is drawn only for its shadow, and in its place go two copies of it, baked
    // each frame from its pose: one cut off at the glass it is entering (the part still on this side), and one carried through
    // to the partner and cut off at that glass (the part that has gone in, coming out of the other mirror). Whatever is past
    // a glass is pressed flat into the gap between the glass and the board behind it, where nothing can see it.
    internal static class Doubles
    {
        private const float Gap=0.03f;  // where the cut-off part is pressed to: just behind the glass, well before the board

        private sealed class Part
        {
            internal Renderer Source;
            internal ShadowCastingMode Shadows;    // the source's own, given back afterwards
            internal Mesh Baked;                    // the skinned pose this frame (null for a rigid mesh)
            internal Vector3[] Points,Normals;      // a rigid mesh's, read once
            internal Vector4[] Tangents;
            internal Mesh Near,Far;                 // the part on this side, the part through the other mirror
            internal GameObject NearObject,FarObject;
            internal MeshRenderer NearRenderer,FarRenderer;
            internal bool Cutting;                  // false while the part is wholly on this side: then it is just itself
        }
        private sealed class Double
        {
            internal Player Who;
            internal LocalPortal At;           // the portal whose glass the body is passing
            internal readonly Dictionary<Renderer,Part> Parts=new Dictionary<Renderer,Part>();
        }
        private static readonly List<Double> All=new List<Double>();
        private static readonly List<Renderer> Renderers=new List<Renderer>();
        private static readonly HashSet<Renderer> Kept=new HashSet<Renderer>();
        private static readonly List<Renderer> Gone=new List<Renderer>();
        private static readonly List<Vector3> P=new List<Vector3>(),N=new List<Vector3>(),NearP=new List<Vector3>(),NearN=new List<Vector3>(),FarP=new List<Vector3>(),FarN=new List<Vector3>();
        private static readonly List<Vector4> T=new List<Vector4>(),NearT=new List<Vector4>(),FarT=new List<Vector4>();
        private static readonly MaterialPropertyBlock Block=new MaterialPropertyBlock();
        private static GameObject _root;
        internal static int Count=>All.Count;
        internal static int PartCount{get{int n=0;foreach(Double d in All)foreach(Part p in d.Parts.Values)if(p.Cutting)n++;return n;}}

        // Called just before the game camera draws (after all animation): cuts and places every straddling body.
        internal static double Ms;      // for the lportal command: the last frame's cutting time while anyone straddles
        internal static void Sync()
        {
            if(!Plugin.BodyDoubles.Value){Clear();return;}
            if(All.Count>0)Ms=_watch.Elapsed.TotalMilliseconds;
            _watch.Restart();
            try{SyncAll();}finally{_watch.Stop();}
        }
        private static readonly System.Diagnostics.Stopwatch _watch=new System.Diagnostics.Stopwatch();
        private static void SyncAll()
        {
            foreach(Player who in Player.GetAllPlayers())
            {
                if(who==null)continue;
                LocalPortal at=who.IsDead()?null:Straddled(who);
                Double d=All.Find(x=>x.Who==who);
                if(at==null){if(d!=null){Drop(d);All.Remove(d);}continue;}
                if(d==null){d=new Double{Who=who};All.Add(d);}
                if(d.At!=at){Drop(d);d.At=at;}
                Cut(d);
            }
            for(int i=All.Count-1;i>=0;i--)
            {
                Double d=All[i];
                if(d.Who==null||d.At==null||d.At.Partner==null){Drop(d);All.RemoveAt(i);}
            }
        }

        // The portal whose glass a body is passing through, if any: its middle just in front of the glass, inside the opening.
        // (Once the middle passes the glass the body is moved to the other mirror, where it is again just in front.)
        private static LocalPortal Straddled(Player who)
        {
            Vector3 middle=who.transform.position+Vector3.up*(float)Policy.BodyHeight;
            LocalPortal best=null;
            float bestDepth=0.75f;
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||p.Partner==null||(p.transform.position-middle).sqrMagnitude>16)continue;
                Vector3 l=p.transform.InverseTransformPoint(middle);
                if(l.z>-0.05f&&l.z<bestDepth&&Policy.InOpening(l.x,l.y)){best=p;bestDepth=l.z;}
            }
            return best;
        }

        private static void Cut(Double d)
        {
            GameObject visual=d.Who.GetVisual();
            if(visual==null){Drop(d);return;}
            Transform a=d.At.transform,b=d.At.Partner.transform;
            visual.GetComponentsInChildren(false,Renderers);
            Kept.Clear();
            foreach(Renderer r in Renderers)
            {
                if(!(r is SkinnedMeshRenderer||r is MeshRenderer)||!r.enabled)continue;
                if(!d.Parts.TryGetValue(r,out Part part))
                {
                    if(r.shadowCastingMode==ShadowCastingMode.ShadowsOnly)continue; // (the game's own shadow-only pieces)
                    part=Make(r);
                    if(part==null)continue;
                    d.Parts[r]=part;
                }
                Kept.Add(r);
                bool across=Across(r.bounds,a);
                Show(part,across);
                if(across)Pose(part,a,b);
            }
            Gone.Clear();
            foreach(Renderer r in d.Parts.Keys)if(!Kept.Contains(r))Gone.Add(r);
            foreach(Renderer r in Gone){Free(d.Parts[r]);d.Parts.Remove(r);}
        }

        private static Part Make(Renderer r)
        {
            var part=new Part{Source=r,Shadows=r.shadowCastingMode};
            Mesh shape;
            if(r is SkinnedMeshRenderer skin)
            {
                if(skin.sharedMesh==null)return null;
                part.Baked=new Mesh{name="LocalPortalBaked"};
                skin.BakeMesh(part.Baked,true);
                if(part.Baked.vertexCount==0){Object.Destroy(part.Baked);return null;}
                shape=part.Baked;
            }
            else
            {
                Mesh mesh=r.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh==null||!mesh.isReadable||mesh.vertexCount==0)return null; // (left as it is: rarely more than a held tool)
                part.Points=mesh.vertices;part.Normals=mesh.normals;part.Tangents=mesh.tangents;
                shape=mesh;
            }
            if(_root==null){_root=new GameObject("LocalPortalBodies");Object.DontDestroyOnLoad(_root);}
            part.Near=Object.Instantiate(shape);part.Near.name="LocalPortalNear";part.Near.MarkDynamic();
            part.Far=Object.Instantiate(shape);part.Far.name="LocalPortalFar";part.Far.MarkDynamic();
            part.NearRenderer=Copy(r,part.Near,"Near",ShadowCastingMode.Off,out part.NearObject); // the source still casts the shadow here
            part.FarRenderer=Copy(r,part.Far,"Far",r.shadowCastingMode,out part.FarObject);
            part.NearRenderer.enabled=part.FarRenderer.enabled=false;
            return part;
        }

        // Whether any of a part's box reaches the glass (or near it): the rest are drawn as they are.
        private static bool Across(Bounds box,Transform a)
        {
            Vector3 c=box.center,e=box.extents;
            for(int i=0;i<8;i++)
            {
                Vector3 corner=c+new Vector3((i&1)==0?-e.x:e.x,(i&2)==0?-e.y:e.y,(i&4)==0?-e.z:e.z);
                if(a.InverseTransformPoint(corner).z<Gap)return true;
            }
            return false;
        }

        // A part being cut is drawn by its two copies, and itself only for its shadow; otherwise it is drawn as it is.
        private static void Show(Part part,bool cutting)
        {
            if(part.Cutting==cutting)return;
            part.Cutting=cutting;
            Renderer r=part.Source;
            if(!cutting){r.shadowCastingMode=part.Shadows;r.forceRenderingOff=false;}
            else if(part.Shadows==ShadowCastingMode.Off)r.forceRenderingOff=true; // (no shadow of it to keep)
            else r.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            part.NearRenderer.enabled=part.FarRenderer.enabled=cutting;
        }

        private static MeshRenderer Copy(Renderer r,Mesh mesh,string name,ShadowCastingMode shadows,out GameObject go)
        {
            go=new GameObject("LocalPortalBody"+name){layer=r.gameObject.layer};
            go.transform.SetParent(_root.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var copy=go.AddComponent<MeshRenderer>();
            copy.sharedMaterials=r.sharedMaterials;
            copy.shadowCastingMode=shadows;
            copy.receiveShadows=r.receiveShadows;
            copy.lightProbeUsage=r.lightProbeUsage;
            copy.reflectionProbeUsage=r.reflectionProbeUsage;
            return copy;
        }

        // Bakes the pose, then writes it twice in the two portals' frames: kept in front of the entry glass, and carried
        // through (x,y,z to -x,y,-z) to stand in front of the exit glass. Anything past a glass is pressed flat behind it.
        private static void Pose(Part part,Transform a,Transform b)
        {
            Renderer r=part.Source;
            P.Clear();N.Clear();T.Clear();
            if(part.Baked!=null)
            {
                ((SkinnedMeshRenderer)r).BakeMesh(part.Baked,true);
                part.Baked.GetVertices(P);part.Baked.GetNormals(N);part.Baked.GetTangents(T);
            }
            else{P.AddRange(part.Points);N.AddRange(part.Normals);T.AddRange(part.Tangents);}
            if(P.Count!=part.Near.vertexCount)return; // (a skinned mesh swapped under the renderer: dropped and remade next change)
            Matrix4x4 into=a.worldToLocalMatrix*r.localToWorldMatrix;
            NearP.Clear();NearN.Clear();NearT.Clear();FarP.Clear();FarN.Clear();FarT.Clear();
            for(int i=0;i<P.Count;i++)
            {
                Vector3 l=into.MultiplyPoint3x4(P[i]);
                NearP.Add(new Vector3(l.x,l.y,Mathf.Max(l.z,-Gap)));
                FarP.Add(new Vector3(-l.x,l.y,Mathf.Max(-l.z,-Gap)));
            }
            bool normals=N.Count==P.Count,tangents=T.Count==P.Count;
            if(normals)
                for(int i=0;i<N.Count;i++)
                {
                    Vector3 n=into.MultiplyVector(N[i]).normalized;
                    NearN.Add(n);FarN.Add(new Vector3(-n.x,n.y,-n.z));
                }
            if(tangents)
                for(int i=0;i<T.Count;i++)
                {
                    Vector4 t=T[i];
                    Vector3 v=into.MultiplyVector(new Vector3(t.x,t.y,t.z)).normalized;
                    NearT.Add(new Vector4(v.x,v.y,v.z,t.w));FarT.Add(new Vector4(-v.x,v.y,-v.z,t.w));
                }
            Write(part.Near,NearP,NearN,NearT,normals,tangents);
            Write(part.Far,FarP,FarN,FarT,normals,tangents);
            part.NearObject.transform.SetPositionAndRotation(a.position,a.rotation);
            part.FarObject.transform.SetPositionAndRotation(b.position,b.rotation);
            r.GetPropertyBlock(Block);
            part.NearRenderer.SetPropertyBlock(Block);part.FarRenderer.SetPropertyBlock(Block);
            if(part.NearRenderer.sharedMaterial!=r.sharedMaterial)part.NearRenderer.sharedMaterials=part.FarRenderer.sharedMaterials=r.sharedMaterials;
        }

        private static void Write(Mesh mesh,List<Vector3> points,List<Vector3> normals,List<Vector4> tangents,bool withNormals,bool withTangents)
        {
            mesh.SetVertices(points);
            if(withNormals)mesh.SetNormals(normals);
            if(withTangents)mesh.SetTangents(tangents);
            mesh.RecalculateBounds();
        }

        private static void Free(Part part)
        {
            if(part.Source!=null&&part.Cutting){part.Source.shadowCastingMode=part.Shadows;part.Source.forceRenderingOff=false;}
            if(part.NearObject!=null)Object.Destroy(part.NearObject);
            if(part.FarObject!=null)Object.Destroy(part.FarObject);
            if(part.Near!=null)Object.Destroy(part.Near);
            if(part.Far!=null)Object.Destroy(part.Far);
            if(part.Baked!=null)Object.Destroy(part.Baked);
        }
        private static void Drop(Double d)
        {
            foreach(Part part in d.Parts.Values)Free(part);
            d.Parts.Clear();
        }
        internal static void Clear()
        {
            foreach(Double d in All)Drop(d);
            All.Clear();
        }
        internal static void Stop()
        {
            Clear();
            if(_root!=null)Object.Destroy(_root);
            _root=null;
        }
    }
}
