using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BobsPipes
{
    internal static class Model
    {
        internal sealed class Parts { internal Transform Root, Bowl; internal GameObject Ember; internal Material Wood; }
        internal static Parts Build(Transform parent, Material basis, int layer, List<Object> owned)
        {
            var root = new GameObject("PipeModel") { layer = layer }; root.transform.SetParent(parent, false);
            Material wood = Tint(basis, new Color(0.60f,0.32f,0.14f), true, owned);
            Material dark = Tint(basis, new Color(0.055f,0.038f,0.027f), false, owned);
            Material leather = Tint(basis, new Color(0.19f,0.11f,0.065f), false, owned);
            var points = new[] { Vector3.zero, new Vector3(0,-0.004f,0.035f), new Vector3(0,-0.022f,0.09f), new Vector3(0,-0.032f,0.15f), new Vector3(0,-0.025f,0.18f) };
            Tube(root.transform,"BentStem",points,new[]{0.006f,0.007f,0.009f,0.011f,0.013f},wood,layer,owned);
            Tube(root.transform,"Mouthpiece",new[]{new Vector3(0,0,-0.012f),Vector3.zero,new Vector3(0,-0.002f,0.02f)},new[]{0.005f,0.006f,0.007f},dark,layer,owned);
            Tube(root.transform,"LeatherWrap",new[]{new Vector3(0,-0.021f,0.086f),new Vector3(0,-0.026f,0.112f)},new[]{0.0105f,0.0115f},leather,layer,owned);
            // A hollow cup: four rings trace the outside, rim and inner wall. No glowing solid cap.
            var cup = new GameObject("CarvedBowl") { layer=layer }; cup.transform.SetParent(root.transform,false);
            cup.transform.localPosition=new Vector3(0,-0.028f,0.18f);
            var vertices=new List<Vector3>(); var triangles=new List<int>(); var uv=new List<Vector2>(); const int n=16;
            float[] heights={-0.032f,0.028f,0.028f,-0.016f}; float[] radii={0.021f,0.034f,0.025f,0.019f};
            for(int ring=0;ring<4;ring++)for(int i=0;i<=n;i++)
            { float a=i*2*Mathf.PI/n;vertices.Add(new Vector3(Mathf.Cos(a)*radii[ring],heights[ring],Mathf.Sin(a)*radii[ring]));uv.Add(new Vector2((float)i/n,(heights[ring]+0.032f)/0.06f)); }
            for(int ring=0;ring<3;ring++)for(int i=0;i<n;i++)
            { int a=ring*(n+1)+i,b=a+n+1; triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1}); }
            Mesh mesh=new Mesh{name="HollowPipeBowl"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
            cup.AddComponent<MeshFilter>().sharedMesh=mesh;cup.AddComponent<MeshRenderer>().sharedMaterial=wood;
            Cylinder(cup.transform,"BowlBase",new Vector3(0,-0.031f,0),new Vector3(0.042f,0.001f,0.042f),wood,layer);
            Cylinder(cup.transform,"Tobacco",new Vector3(0,-0.014f,0),new Vector3(0.038f,0.003f,0.038f),dark,layer);
            Material ember=Tint(basis,new Color(0.42f,0.09f,0.015f),false,owned);
            Shader emberShader=Shader.Find("Unlit/Color");if(emberShader!=null)ember.shader=emberShader;
            GameObject hot=Cylinder(cup.transform,"Ember",new Vector3(0.005f,-0.0105f,0.003f),new Vector3(0.010f,0.001f,0.007f),ember,layer);hot.SetActive(false);
            return new Parts{Root=root.transform,Bowl=cup.transform,Ember=hot,Wood=wood};
        }
        internal static Transform Tin(Transform parent,Material basis,int layer,int blend,List<Object> owned)
        {
            var root=new GameObject("TobaccoTin"){layer=layer};root.transform.SetParent(parent,false);
            Material body=Tint(basis,new Color(0.39f,0.27f,0.16f),true,owned);
            Material lid=Tint(basis,new Color(0.55f,0.42f,0.25f),false,owned);
            Material band=Tint(basis,new[]{new Color(0.25f,0.32f,0.14f),new Color(0.45f,0.25f,0.08f),new Color(0.53f,0.31f,0.12f)}[blend],false,owned);
            Cylinder(root.transform,"Tin",Vector3.zero,new Vector3(0.10f,0.025f,0.10f),body,layer);
            Cylinder(root.transform,"Band",Vector3.zero,new Vector3(0.102f,0.010f,0.102f),band,layer);
            Cylinder(root.transform,"Lid",new Vector3(0,0.027f,0),new Vector3(0.108f,0.003f,0.108f),lid,layer);
            return root.transform;
        }
        private static Material Tint(Material basis,Color color,bool texture,List<Object> owned)
        {
            var material=new Material(basis){color=color};
            material.DisableKeyword("_EMISSION");if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",Color.black);
            if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",0.06f);
            if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",0);
            if(!texture&&material.HasProperty("_MainTex"))material.mainTexture=null;
            owned.Add(material);return material;
        }
        private static GameObject Cylinder(Transform parent,string name,Vector3 position,Vector3 scale,Material material,int layer)
        {
            GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.layer=layer;
            Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;return go;
        }
        private static void Tube(Transform parent,string name,Vector3[] points,float[] radii,Material material,int layer,List<Object> owned)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();const int n=10;
            for(int j=0;j<points.Length;j++)
            {
                Vector3 direction=(points[Mathf.Min(j+1,points.Length-1)]-points[Mathf.Max(0,j-1)]).normalized;
                Vector3 right=Vector3.Cross(Vector3.up,direction).normalized,up=Vector3.Cross(direction,right);
                for(int i=0;i<=n;i++){float a=i*2*Mathf.PI/n;vertices.Add(points[j]+radii[j]*(Mathf.Cos(a)*right+Mathf.Sin(a)*up));uv.Add(new Vector2((float)i/n,(float)j/(points.Length-1)));}
                if(j==0)continue;
                for(int i=0;i<n;i++){int a=(j-1)*(n+1)+i,b=a+n+1;triangles.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
            }
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
            var go=new GameObject(name){layer=layer};go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
    }
}
