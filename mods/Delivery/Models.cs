using System.Collections.Generic;
using UnityEngine;

namespace Delivery
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

        // The errand horn: a curved wooden horn widening to its bell, bound with bronze at the mouthpiece, the middle and the bell's rim,
        // with a leather carrying strap from end to end.
        internal static void Horn(Transform root,Looks l)
        {
            const int steps=12;
            var c=new Vector3(0,0.3f,0);
            const float r=0.3f;
            Vector3 At(float t){float a=Mathf.Lerp(-100f,-15f,t)*Mathf.Deg2Rad;return c+new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);}
            float Thick(float t)=>Mathf.Lerp(0.014f,0.05f,Mathf.Pow(t,1.4f));
            for(int i=0;i<steps;i++)
            {
                float t0=i/(float)steps,t1=(i+1)/(float)steps;
                string shade="Horn"+Mathf.Min(4,i*5/steps); // (dark at the tip, pale towards the bell, as a horn is)
                Rod(root,"horn",l.Get(shade),At(t0),At(t1),Thick((t0+t1)/2));
                Part(root,"joint",PrimitiveType.Sphere,l.Get(shade),At(t1),Vector3.zero,Vector3.one*Thick(t1)*2);
            }
            Vector3 end=At(1),dir=(At(1)-At(0.95f)).normalized;
            for(int k=0;k<4;k++)Rod(root,"bell",l.Get("Horn4"),end+dir*(0.012f*k),end+dir*(0.012f*(k+1)),Mathf.Lerp(0.052f,0.066f,k/3f)); // (flaring out)
            Rod(root,"throat",l.Get("Burnt"),end+dir*0.04f,end+dir*0.0485f,0.058f);
            Rod(root,"rim",l.Get("Bronze"),end+dir*0.042f,end+dir*0.052f,0.069f);
            Vector3 mouth=At(0),mdir=(At(0.05f)-At(0)).normalized;
            Rod(root,"mouthpiece",l.Get("Bronze"),mouth-mdir*0.025f,mouth+mdir*0.02f,0.017f);
            foreach(float t in new[]{0.5f,0.8f})Rod(root,"band",l.Get("Bronze"),At(t)-(At(t+0.02f)-At(t)).normalized*0.008f,At(t)+(At(t+0.02f)-At(t)).normalized*0.008f,Thick(t)+0.004f);
            Vector3 s0=At(0.12f)+new Vector3(0,0,0.012f),s1=At(0.86f)+new Vector3(0,0,0.03f),sag=(s0+s1)/2+new Vector3(0,0.09f,0.02f);
            Rod(root,"strap",l.Get("Leather"),s0,sag,0.011f);
            Rod(root,"strap",l.Get("Leather"),sag,s1,0.011f);
        }
    }
}
