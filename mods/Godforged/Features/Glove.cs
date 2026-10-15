using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Godforged
{
    // Járngreipr's gloves, made from the wearer's own hands: iron on leather, as the gauntlets of the north were (plates riveted to a
    // leather glove). When they're put on (WornSkin), the body's mesh (the male's or the female's: whichever this player has) gives a
    // skinned mesh of its own bound to the same bones, so it fits exactly and bends with every finger:
    //   leather: every triangle of the hands and fingers, and of the forearm from the cuff down to the wrist, the
    //     skin pushed out 2.5 mm along its normals;
    //   iron: the backs of the finger and thumb joints and of the hand, pushed out 5 mm, and only where a joint's skin is its own (not
    //     shared with the next bone), so the plates stand apart at the knuckles and articulate. Each plate is a thin shell (both faces).
    // The fingers curl towards their bones' +z, so their backs face -z; the thumbs curl towards +x on the right hand and -x on the left
    // (measured in the game, gf cuff hand).
    internal static class Glove
    {
        private const float Leather=0.003f,IronOut=0.0055f,IronIn=0.004f;
        private static readonly Dictionary<Mesh,Mesh> Made=new Dictionary<Mesh,Mesh>();
        internal static void Clear()=>Made.Clear();   // (the meshes are Look's: kept while in a world)

        // A glove for this body, as a new skinned part of the player (destroyed with the gauntlets' other worn parts: it's in their list).
        internal static GameObject Wear(SkinnedMeshRenderer body)
        {
            Mesh mesh=Of(body);
            if(mesh==null)return null;
            var go=new GameObject("gf_glove"){layer=body.gameObject.layer};
            go.transform.SetParent(body.transform.parent,false);
            go.transform.localPosition=body.transform.localPosition;go.transform.localRotation=body.transform.localRotation;go.transform.localScale=body.transform.localScale;
            var r=go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh=mesh;r.bones=body.bones;r.rootBone=body.rootBone;r.updateWhenOffscreen=true;
            r.sharedMaterials=new[]{Look.Grained(new Color(0.22f,0.13f,0.07f),0f,0.12f,false,40f),Look.Grained(new Color(0.235f,0.243f,0.263f),0.7f,0.3f,true,40f)};   // (leather and forged iron with a grain: the body's UVs are a whole-body atlas, so it repeats often)
            return go;
        }

        private static Mesh Of(SkinnedMeshRenderer body)
        {
            Mesh src=body.sharedMesh;
            if(src==null)return null;
            if(Made.TryGetValue(src,out Mesh m)&&m!=null)return m;
            if(!src.isReadable){Plugin.Warn("Járngreipr: the body's mesh can't be read; no gloves");Made[src]=null;return null;}
            m=Make(src,body.bones);
            Made[src]=m;
            return m;
        }

        private static Mesh Make(Mesh src,Transform[] bones)
        {
            Vector3[] v=src.vertices,n=src.normals;BoneWeight[] w=src.boneWeights;Matrix4x4[] bind=src.bindposes;
            int nb=bones.Length;
            string Name(int i)=>i>=0&&i<nb&&bones[i]!=null?bones[i].name:"";
            // Where each bone was in the mesh's space (bind pose), and its axes there.
            Vector3 At(int i)=>bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
            Vector3 Axis(int i,Vector3 local)=>bind[i].inverse.MultiplyVector(local).normalized;
            int Find(string name){for(int i=0;i<nb;i++)if(Name(i)==name)return i;return -1;}

            // The mesh's units against the world's: the forearm is 0.26 m.
            int la=Find("LeftForeArm"),lh=Find("LeftHand"),ra=Find("RightForeArm"),rh=Find("RightHand");
            if(la<0||lh<0||ra<0||rh<0||bind.Length<nb)return null;
            float unit=Vector3.Distance(At(la),At(lh))/0.26f;

            int Top(int i){BoneWeight b=w[i];return b.weight0>=b.weight1?b.boneIndex0:b.boneIndex1;}
            float Own(int i){BoneWeight b=w[i];return Mathf.Max(b.weight0,b.weight1);}
            // In the glove: the hands and fingers, and the forearms' skin from the cuff down to the wrist.
            bool InGlove(int i)
            {
                int t=Top(i);string nm=Name(t);
                if(nm.Contains("Hand"))return true;
                if(t!=la&&t!=ra)return false;
                // the forearm's only from the cuff down (its rings above, by the elbow, stay bare: nothing shows past the cuff)
                Vector3 a=At(t),h=At(t==la?lh:rh);
                return Vector3.Dot(v[i]-a,(h-a).normalized)/Vector3.Distance(a,h)>0.4f;
            }
            // The back of a joint, in the mesh's space: -z of a finger's bone or the hand's, -x of the right thumb's, +x of the left's.
            Vector3 Back(int t)
            {
                string nm=Name(t);
                if(nm.Contains("Thumb"))return Axis(t,nm.StartsWith("Left")?Vector3.right:Vector3.left);
                return Axis(t,Vector3.back);
            }
            bool Plated(int t)   // (not the back of the hand or the fingers' first joints: they have plates of their own, Jarngreipr)
            {
                string nm=Name(t);
                return nm.Contains("Hand")&&!nm.EndsWith("_end")&&!nm.Contains("Attach")&&!nm.EndsWith("Hand")&&!(nm.EndsWith("1")&&!nm.Contains("Thumb"));
            }

            var verts=new List<Vector3>();var norms=new List<Vector3>();var uvs=new List<Vector2>();var weights=new List<BoneWeight>();
            var leather=new List<int>();var iron=new List<int>();
            Vector2[] uv=src.uv;
            var copyAt=new Dictionary<int,int>[3]{new Dictionary<int,int>(),new Dictionary<int,int>(),new Dictionary<int,int>()};
            // Out from the skin: away from the line of the vertex's own bone (its normal can't be trusted: the forearms' point inwards).
            Vector3 Out(int i)
            {
                int t=Top(i);
                if(t<0||t>=nb)return n[i];
                Vector3 a=At(t),axis=Axis(t,Vector3.up),d=v[i]-a;
                Vector3 radial=d-axis*Vector3.Dot(d,axis);
                if(radial.sqrMagnitude<1e-14f)return n[i];
                radial.Normalize();
                return Vector3.Dot(n[i],radial)>0.3f?n[i]:radial;
            }
            int Copy(int i,int layer,float outBy,bool flip)
            {
                if(copyAt[layer].TryGetValue(i,out int k))return k;
                k=verts.Count;
                Vector3 o=Out(i);
                verts.Add(v[i]+o*outBy*unit);norms.Add(flip?-o:o);uvs.Add(uv!=null&&i<uv.Length?uv[i]:Vector2.zero);weights.Add(w[i]);
                copyAt[layer][i]=k;
                return k;
            }
            for(int sm=0;sm<src.subMeshCount;sm++)
            {
                int[] t=src.GetTriangles(sm);
                for(int i=0;i+2<t.Length;i+=3)
                {
                    int a=t[i],b=t[i+1],c=t[i+2];
                    if(!InGlove(a)||!InGlove(b)||!InGlove(c))continue;
                    // Wound to face out (the source's winding faces the way its normals do: inwards on the forearms).
                    Vector3 fn=Vector3.Cross(v[b]-v[a],v[c]-v[a]);
                    bool flipT=Vector3.Dot(fn,Out(a)+Out(b)+Out(c))<0f;
                    leather.Add(Copy(a,0,Leather,false));
                    if(flipT){leather.Add(Copy(c,0,Leather,false));leather.Add(Copy(b,0,Leather,false));}
                    else{leather.Add(Copy(b,0,Leather,false));leather.Add(Copy(c,0,Leather,false));}
                    // Iron: all three corners on one joint's own skin, facing its back.
                    int bone=Top(a);
                    if(!Plated(bone)||Top(b)!=bone||Top(c)!=bone||Own(a)<0.5f||Own(b)<0.5f||Own(c)<0.5f)continue;
                    if(Vector3.Dot((Out(a)+Out(b)+Out(c)).normalized,Back(bone))<0.05f)continue;
                    iron.Add(Copy(a,1,IronOut,false));iron.Add(Copy(b,1,IronOut,false));iron.Add(Copy(c,1,IronOut,false));
                    iron.Add(Copy(a,2,IronIn,true));iron.Add(Copy(c,2,IronIn,true));iron.Add(Copy(b,2,IronIn,true));   // (its underside)
                }
            }
            var glove=new Mesh{name="Járngreipr glove"};
            if(verts.Count>65000)glove.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            glove.SetVertices(verts);glove.SetNormals(norms);glove.SetUVs(0,uvs);
            glove.boneWeights=weights.ToArray();glove.bindposes=bind;
            var cols=new Color[verts.Count];for(int i=0;i<cols.Length;i++)cols[i]=Color.white;glove.colors=cols;   // (the game's shaders read it)
            glove.subMeshCount=2;glove.SetTriangles(leather,0);glove.SetTriangles(iron,1);
            glove.RecalculateBounds();
            Plugin.Log($"Járngreipr: a glove from {src.name}: {leather.Count/3} leather and {iron.Count/6} iron triangles");
            return Look.Own(glove);
        }
    }
}
