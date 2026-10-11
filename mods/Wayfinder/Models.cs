using System.Collections.Generic;
using UnityEngine;

namespace Wayfinder
{
    // Models built from Unity's primitive shapes and a few meshes made here.
    internal static class Models
    {
        private static readonly Dictionary<PrimitiveType,Mesh> Prims=new Dictionary<PrimitiveType,Mesh>();
        internal static Mesh Prim(PrimitiveType type)
        {
            if(Prims.TryGetValue(type,out Mesh m)&&m!=null)return m;
            GameObject p=GameObject.CreatePrimitive(type);
            m=p.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(p);
            Prims[type]=m;
            return m;
        }
        internal static Transform Part(Transform parent,string name,PrimitiveType type,Material mat,Vector3 pos,Vector3 euler,Vector3 scale,bool shadows=true)
            =>Mesh(parent,name,Prim(type),mat,pos,euler,scale,shadows);
        internal static Transform Mesh(Transform parent,string name,Mesh mesh,Material mat,Vector3 pos,Vector3 euler,Vector3 scale,bool shadows=true)
        {
            var go=new GameObject(name);
            go.transform.SetParent(parent,false);
            go.transform.localPosition=pos;go.transform.localRotation=Quaternion.Euler(euler);go.transform.localScale=scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();
            r.sharedMaterial=mat;
            r.shadowCastingMode=shadows?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
        // A cylinder from a to b (Unity's is 2 tall along Y).
        internal static Transform Rod(Transform parent,string name,Material mat,Vector3 a,Vector3 b,float radius,PrimitiveType type=PrimitiveType.Cylinder)
        {
            Vector3 d=b-a;
            Transform t=Part(parent,name,type,mat,(a+b)/2,Vector3.zero,new Vector3(radius*2,d.magnitude/2,radius*2));
            if(d.sqrMagnitude>1e-8)t.localRotation=Quaternion.FromToRotation(Vector3.up,d);
            return t;
        }

        // The Uunartoq disc: a notched wooden dial with gnomon curves burnt in, a pointed gnomon, a peg handle below.
        internal static void SunCompass(Transform root,Looks l)
        {
            Part(root,"Dial",PrimitiveType.Cylinder,l.Get("Wood"),Vector3.zero,Vector3.zero,new Vector3(0.22f,0.008f,0.22f));
            for(int i=0;i<32;i++)
            {
                float a=i*360f/32;
                Vector3 at=Quaternion.Euler(0,a,0)*new Vector3(0,0.002f,0.108f);
                Part(root,"Notch",PrimitiveType.Cube,l.Get("Burnt"),at,new Vector3(0,a,0),new Vector3(0.006f,0.016f,i%4==0?0.024f:0.012f),false);
            }
            for(int i=0;i<14;i++) // the gnomon's shadow-curve, a hyperbola burnt across the face
            {
                float x=-0.07f+i*0.0108f;
                Part(root,"Curve",PrimitiveType.Cube,l.Get("Burnt"),new Vector3(x,0.0085f,0.025f+x*x*4f),new Vector3(0,Mathf.Atan(8f*x)*Mathf.Rad2Deg,0),new Vector3(0.012f,0.001f,0.003f),false);
            }
            Rod(root,"Gnomon",l.Get("DarkWood"),new Vector3(0,0.006f,0),new Vector3(0,0.07f,0),0.004f);
            Part(root,"GnomonTip",PrimitiveType.Sphere,l.Get("Bronze"),new Vector3(0,0.07f,0),Vector3.zero,Vector3.one*0.012f);
            Rod(root,"Peg",l.Get("DarkWood"),new Vector3(0,-0.006f,0),new Vector3(0,-0.07f,0),0.012f);
        }
        // Iceland spar: a rhombohedron (a cube leaned on two axes) bound with a leather thong.
        internal static void Sunstone(Transform root,Looks l)
        {
            var lean=new GameObject("Lean").transform;
            lean.SetParent(root,false);
            lean.localRotation=Quaternion.Euler(0,0,24);lean.localScale=new Vector3(1f,0.72f,1f);
            var inner=Part(lean,"Spar",PrimitiveType.Cube,l.Get("Crystal"),Vector3.zero,new Vector3(18,0,0),new Vector3(0.06f,0.07f,0.05f));
            inner.localRotation=Quaternion.Euler(18,12,0);
            Part(root,"Wrap",PrimitiveType.Cylinder,l.Get("Leather"),new Vector3(0,0.0f,0),new Vector3(0,0,24),new Vector3(0.075f,0.006f,0.062f));
            Rod(root,"Thong",l.Get("Leather"),new Vector3(-0.02f,0.03f,0),new Vector3(-0.07f,0.12f,0.01f),0.0035f);
            Rod(root,"Thong2",l.Get("Leather"),new Vector3(-0.07f,0.12f,0.01f),new Vector3(-0.02f,0.16f,0.0f),0.0035f);
        }
        // A bell-shaped stone weight with a tallow cup, and a coil of knotted thong.
        internal static void SoundingLine(Transform root,Looks l)
        {
            Part(root,"Weight",PrimitiveType.Cylinder,l.Get("Stone"),new Vector3(0,0.05f,0),Vector3.zero,new Vector3(0.06f,0.05f,0.06f));
            Part(root,"Base",PrimitiveType.Sphere,l.Get("Stone"),new Vector3(0,0.0f,0),Vector3.zero,new Vector3(0.075f,0.04f,0.075f));
            Part(root,"Tallow",PrimitiveType.Cylinder,l.Get("Wicker"),new Vector3(0,-0.018f,0),Vector3.zero,new Vector3(0.04f,0.004f,0.04f),false);
            Part(root,"Eye",PrimitiveType.Cylinder,l.Get("Bronze"),new Vector3(0,0.108f,0),new Vector3(90,0,0),new Vector3(0.022f,0.004f,0.022f));
            Vector3 c=new Vector3(0.13f,0.02f,0);
            for(int ring=0;ring<3;ring++)
            for(int i=0;i<18;i++)
            {
                float a0=i*20*Mathf.Deg2Rad,a1=(i+1)*20*Mathf.Deg2Rad,r=0.07f-ring*0.004f;
                Vector3 p0=c+new Vector3(Mathf.Cos(a0)*r,ring*0.008f,Mathf.Sin(a0)*r),p1=c+new Vector3(Mathf.Cos(a1)*r,ring*0.008f,Mathf.Sin(a1)*r);
                Rod(root,"Coil",l.Get("Leather"),p0,p1,0.005f);
            }
            Rod(root,"Lead",l.Get("Leather"),new Vector3(0,0.11f,0),c+new Vector3(-0.07f,0.016f,0),0.0045f);
        }
        // A flat stave with days notched along both edges and signs burnt into the face.
        internal static void Primstav(Transform root,Looks l)
        {
            Part(root,"Stave",PrimitiveType.Cube,l.Get("Wood"),Vector3.zero,Vector3.zero,new Vector3(0.045f,0.62f,0.014f));
            Part(root,"Grip",PrimitiveType.Cylinder,l.Get("DarkWood"),new Vector3(0,-0.33f,0),Vector3.zero,new Vector3(0.03f,0.04f,0.03f));
            for(int i=0;i<26;i++)
            {
                float y=-0.28f+i*0.022f;
                Part(root,"Day",PrimitiveType.Cube,l.Get("Burnt"),new Vector3(0.019f,y,0.0072f),Vector3.zero,new Vector3(0.01f,0.004f,0.001f),false);
                Part(root,"Day",PrimitiveType.Cube,l.Get("Burnt"),new Vector3(-0.019f,y,0.0072f),Vector3.zero,new Vector3(0.01f,0.004f,0.001f),false);
                if(i%5==2)Part(root,"Sign",PrimitiveType.Cube,l.Get("Burnt"),new Vector3(0,y,0.0072f),new Vector3(0,0,i%2==0?35:-35),new Vector3(0.004f,0.02f,0.001f),false);
                if(i%7==3)Part(root,"Sun",PrimitiveType.Cylinder,l.Get("Burnt"),new Vector3(0,y,0.0072f),new Vector3(90,0,0),new Vector3(0.014f,0.0006f,0.014f),false);
            }
        }
        // A wicker cage of withies, and a raven in it.
        internal static void CagedRaven(Transform root,Looks l)
        {
            Part(root,"Floor",PrimitiveType.Cylinder,l.Get("Wicker"),Vector3.zero,Vector3.zero,new Vector3(0.24f,0.008f,0.24f));
            for(int i=0;i<12;i++)
            {
                float a=i*30*Mathf.Deg2Rad;
                Vector3 b=new Vector3(Mathf.Cos(a)*0.115f,0,Mathf.Sin(a)*0.115f),m=new Vector3(Mathf.Cos(a)*0.11f,0.2f,Mathf.Sin(a)*0.11f);
                Rod(root,"Withy",l.Get("Wicker"),b,m,0.004f);
                Rod(root,"Withy",l.Get("Wicker"),m,new Vector3(0,0.29f,0),0.004f);
            }
            Part(root,"Hoop",PrimitiveType.Cylinder,l.Get("Wicker"),new Vector3(0,0.12f,0),Vector3.zero,new Vector3(0.23f,0.006f,0.23f));
            Raven(root,l,new Vector3(0,0.03f,0),0.9f);
            Rod(root,"Handle",l.Get("Leather"),new Vector3(0,0.29f,0),new Vector3(0,0.33f,0),0.012f);
        }
        // A sitting raven: body, head, beak, tail.
        internal static void Raven(Transform root,Looks l,Vector3 at,float s)
        {
            Part(root,"Body",PrimitiveType.Sphere,l.Get("Raven"),at+new Vector3(0,0.07f,0)*s,new Vector3(-20,0,0),new Vector3(0.08f,0.075f,0.13f)*s);
            Part(root,"Head",PrimitiveType.Sphere,l.Get("Raven"),at+new Vector3(0,0.125f,0.055f)*s,Vector3.zero,Vector3.one*0.05f*s);
            Part(root,"Beak",PrimitiveType.Cube,l.Get("Beak"),at+new Vector3(0,0.12f,0.09f)*s,new Vector3(10,45,0)*1,new Vector3(0.014f,0.014f,0.04f)*s);
            Part(root,"Tail",PrimitiveType.Cube,l.Get("Raven"),at+new Vector3(0,0.05f,-0.08f)*s,new Vector3(-30,0,0),new Vector3(0.05f,0.008f,0.07f)*s);
            Rod(root,"Leg",l.Get("Beak"),at+new Vector3(0.015f,0.03f,0)*s,at+new Vector3(0.015f,0,0.01f)*s,0.003f*s);
            Rod(root,"Leg",l.Get("Beak"),at+new Vector3(-0.015f,0.03f,0)*s,at+new Vector3(-0.015f,0,0.01f)*s,0.003f*s);
        }

        // A quarter-disc plate (the shape of the old ship-vanes), in the XY plane, from the pole edge (x=0) out to +x, flat side up.
        internal static Mesh Quadrant(float radius,float thickness,int steps=16)
        {
            var v=new List<Vector3>();var tris=new List<int>();var uv=new List<Vector2>();
            var outline=new List<Vector2>{new Vector2(0,0)};
            // the right angle at the top of the pole; along the top edge out to x=radius, then the curve sweeps down and back to the pole
            for(int i=0;i<=steps;i++){float a=Mathf.PI/2*i/steps;outline.Add(new Vector2(Mathf.Cos(a)*radius,-Mathf.Sin(a)*radius));}
            int n=outline.Count;
            foreach(float z in new[]{thickness/2,-thickness/2})
            {
                int c=v.Count;
                foreach(Vector2 p in outline){v.Add(new Vector3(p.x,p.y,z));uv.Add(p/radius);}
                for(int i=1;i+1<n;i++){if(z>0){tris.Add(c);tris.Add(c+i+1);tris.Add(c+i);}else{tris.Add(c);tris.Add(c+i);tris.Add(c+i+1);}}
            }
            for(int i=0;i<n;i++)
            {
                int j=(i+1)%n;
                int c=v.Count;
                v.Add(new Vector3(outline[i].x,outline[i].y,thickness/2));v.Add(new Vector3(outline[j].x,outline[j].y,thickness/2));
                v.Add(new Vector3(outline[j].x,outline[j].y,-thickness/2));v.Add(new Vector3(outline[i].x,outline[i].y,-thickness/2));
                for(int k=0;k<4;k++)uv.Add(new Vector2(k*0.25f,0));
                tris.AddRange(new[]{c,c+1,c+2,c,c+2,c+3});
            }
            var mesh=new Mesh{name="WayfinderQuadrant"};
            mesh.SetVertices(v);mesh.SetTriangles(tris,0);mesh.SetUVs(0,uv);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            return mesh;
        }
    }
}
