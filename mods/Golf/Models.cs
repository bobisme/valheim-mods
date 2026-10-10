using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MeadowGolf
{
    internal static class Models
    {
        internal static readonly Color[] Colors={new Color(.87f,.44f,.18f),new Color(.24f,.52f,.71f),new Color(.77f,.67f,.26f),
            new Color(.55f,.36f,.61f),new Color(.39f,.63f,.36f),new Color(.76f,.31f,.30f),new Color(.76f,.78f,.70f),new Color(.29f,.67f,.62f)};
        internal const float FlagHeight=3.3f;
        internal static Material Wood,Dark,Cloth,Red,Cavity;
        internal static readonly List<Material> BallColors=new List<Material>();
        internal static void Init(Material basis)
        {
            if(Wood!=null)return;
            Wood=Tint(basis,new Color(.61f,.42f,.23f),true);Dark=Tint(basis,new Color(.22f,.16f,.10f),false);
            Cloth=Tint(basis,new Color(.87f,.79f,.60f),false);
            Red=Tint(basis,new Color(.76f,.095f,.065f),false);
            Cavity=Tint(basis,new Color(.025f,.018f,.012f),false);
            foreach(Color color in Colors)BallColors.Add(Tint(basis,color,false));
        }
        private static Material Tint(Material basis,Color color,bool texture)
        {
            var m=new Material(basis){hideFlags=HideFlags.HideAndDontSave};
            if(m.HasProperty("_Color"))m.color=color;
            if(!texture&&m.HasProperty("_MainTex"))m.mainTexture=null;
            if(m.HasProperty("_EmissionColor"))m.SetColor("_EmissionColor",Color.black);
            m.DisableKeyword("_EMISSION");return m;
        }
        internal static Transform Root(Transform parent,string name)
        {var go=new GameObject(name){layer=parent.gameObject.layer};go.transform.SetParent(parent,false);return go.transform;}
        internal static GameObject Part(Transform parent,string name,PrimitiveType type,Vector3 p,Vector3 size,Material mat,Quaternion? rot=null)
        {
            GameObject go=GameObject.CreatePrimitive(type);go.name=name;go.layer=parent.gameObject.layer;
            go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=size;go.transform.localRotation=rot??Quaternion.identity;
            Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=mat;return go;
        }
        internal static void Club(Transform parent)
        {
            Transform root=Root(parent,"attach");
            Part(root,"Ash shaft",PrimitiveType.Cylinder,new Vector3(0,-.35f,0),new Vector3(.035f,.46f,.035f),Wood);
            Part(root,"Leather grip",PrimitiveType.Cylinder,new Vector3(0,.03f,0),new Vector3(.045f,.11f,.045f),Dark);
            Part(root,"Carved head",PrimitiveType.Capsule,new Vector3(.085f,-.78f,.015f),new Vector3(.10f,.14f,.13f),Wood,Quaternion.Euler(0,0,82));
            Part(root,"Face",PrimitiveType.Cube,new Vector3(.085f,-.78f,.080f),new Vector3(.21f,.06f,.012f),Dark,Quaternion.Euler(0,0,-8));
            for(int i=0;i<3;i++)Part(root,"Head binding",PrimitiveType.Cylinder,new Vector3(0,-.65f-i*.03f,0),new Vector3(.047f,.006f,.047f),Dark);
            for(int i=0;i<8;i++)Part(root,"Grip winding",PrimitiveType.Cylinder,new Vector3(0,-.065f+i*.027f,0),new Vector3(.050f,.004f,.050f),Wood);
            Part(root,"Grip pommel",PrimitiveType.Sphere,new Vector3(0,.15f,0),new Vector3(.055f,.03f,.055f),Dark);
        }
        internal static void Marker(Transform parent,bool cup)
        {
            Transform root=Root(parent,"GolfModel");
            Root(root,"Golf visuals "+Plugin.Version);
            if(!cup)
            {
                Part(root,"Tee rim",PrimitiveType.Cylinder,new Vector3(0,.018f,0),new Vector3(.79f,.018f,.79f),Dark);
                Part(root,"Tee pad",PrimitiveType.Cylinder,new Vector3(0,.031f,0),new Vector3(.75f,.025f,.75f),Wood);
                for(int i=0;i<4;i++)
                {float a=i*Mathf.PI/2+Mathf.PI/4;Part(root,"Wooden peg",PrimitiveType.Sphere,new Vector3(Mathf.Cos(a)*.28f,.057f,Mathf.Sin(a)*.28f),new Vector3(.023f,.009f,.023f),Dark);}
                Part(root,"Tee",PrimitiveType.Cylinder,new Vector3(0,.09f,.50f),new Vector3(.08f,.09f,.08f),Dark);
                Part(root,"Start rune",PrimitiveType.Cube,new Vector3(0,.058f,0),new Vector3(.05f,.006f,.28f),Cloth);
                Part(root,"Start rune",PrimitiveType.Cube,new Vector3(.08f,.058f,.05f),new Vector3(.2f,.006f,.04f),Cloth,Quaternion.Euler(0,-35,0));
                return;
            }
            // A visual cup; its ring never blocks a rolling ball and does not edit the ground.
            Part(root,"Cup shadow",PrimitiveType.Cylinder,new Vector3(0,.012f,0),new Vector3(.62f,.006f,.62f),Cavity);
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8;
                Part(root,"Cup rim",PrimitiveType.Cube,new Vector3(Mathf.Cos(a)*.32f,.032f,Mathf.Sin(a)*.32f),
                    new Vector3(.125f,.045f,.06f),Wood,Quaternion.Euler(0,-a*Mathf.Rad2Deg+90,0));
            }
            Part(root,"Flagstaff",PrimitiveType.Cylinder,new Vector3(.43f,FlagHeight/2,0),new Vector3(.07f,FlagHeight/2,.07f),Wood);
            Part(root,"Staff foot",PrimitiveType.Cylinder,new Vector3(.43f,.11f,0),new Vector3(.105f,.11f,.105f),Dark);
            Part(root,"Staff finial",PrimitiveType.Sphere,new Vector3(.43f,FlagHeight,0),new Vector3(.14f,.19f,.14f),Wood);
            Banner(root);
            foreach(float y in new[]{2.34f,3.08f})
                Part(root,"Banner binding",PrimitiveType.Cylinder,new Vector3(.43f,y,0),new Vector3(.085f,.018f,.085f),Cloth);
        }
        // A low-poly folded cloth silhouette, with two independently lit sides. No Cloth
        // solver, colliders, glow or per-frame mesh updates are needed for distant flags.
        private static Vector3 ClothPoint(float x,float y,float offset=0)
        {
            float u=x/1.4f,v=(y-2.3f)/.82f;
            return new Vector3(.43f+x,y,.11f*Mathf.Sin(u*Mathf.PI*2-.6f*v)*u+offset);
        }
        private static void MeshPart(Transform parent,string name,Vector3[] vertices,int[] triangles,Material material)
        {
            Transform t=Root(parent,name);
            var mesh=new Mesh{name="Meadow Golf "+name,hideFlags=HideFlags.HideAndDontSave};
            mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
            t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        private static void Banner(Transform parent)
        {
            const int columns=16,rows=6,sideVertices=(columns+1)*(rows+1);
            var vertices=new Vector3[sideVertices*2];var triangles=new List<int>();
            for(int side=0;side<2;side++)for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
            {
                float u=x/(float)columns,v=y/(float)rows;
                float width=1.4f-.32f*(1-Mathf.Abs(v*2-1))*Mathf.Pow(u,6);
                int i=side*sideVertices+y*(columns+1)+x;
                vertices[i]=ClothPoint(u*width,2.3f+.82f*v,side==0?.003f:-.003f);
                if(x==columns||y==rows)continue;
                int b=i+1,c=i+columns+1,d=c+1;
                if(side==0)triangles.AddRange(new[]{i,b,c,b,d,c});
                else triangles.AddRange(new[]{i,c,b,b,c,d});
            }
            MeshPart(parent,"Red swallowtail banner",vertices,triangles.ToArray(),Red);
            // An ivory diamond rune sits on both sides of the red cloth.
            var rune=new[]{new Vector2(.36f,2.7f),new Vector2(.60f,2.94f),new Vector2(.84f,2.7f),new Vector2(.60f,2.46f),new Vector2(.36f,2.7f)};
            var ink=new List<Vector3>();var faces=new List<int>();
            for(int side=0;side<2;side++)for(int i=0;i<rune.Length-1;i++)
            {
                Vector2 a=rune[i],b=rune[i+1],n=new Vector2(-(b-a).y,(b-a).x).normalized*.018f;
                int first=ink.Count;float offset=side==0?.012f:-.012f;
                foreach(Vector2 point in new[]{a-n,b-n,a+n,b+n})ink.Add(ClothPoint(point.x,point.y,offset));
                if(side==0)faces.AddRange(new[]{first,first+1,first+2,first+1,first+3,first+2});
                else faces.AddRange(new[]{first,first+2,first+1,first+1,first+2,first+3});
            }
            MeshPart(parent,"Ivory banner rune",ink.ToArray(),faces.ToArray(),Cloth);
        }
        internal static void RefreshMarker(Transform parent,bool cup)
        {
            GameObject prefab=cup?Prefabs.CupPrefab:Prefabs.TeePrefab;
            Transform source=prefab?.transform.Find("GolfModel");
            if(source==null)return;
            Piece piece=parent.GetComponent<Piece>();
            if(piece!=null)piece.m_icon=prefab.GetComponent<Piece>().m_icon;
            Transform previous=parent.Find("GolfModel");
            if(previous!=null&&previous.Find("Golf visuals "+Plugin.Version)!=null)return;
            if(previous!=null)Object.DestroyImmediate(previous.gameObject);
            // Clone the prefab model so every standing cup shares its mesh assets.
            Transform model=Object.Instantiate(source,parent,false);model.name="GolfModel";
            foreach(LODGroup lod in parent.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
            WearNTear wear=parent.GetComponent<WearNTear>();
            if(wear!=null)
            {
                wear.m_fragmentRoots=new[]{model.gameObject};
                // Native highlights/cache must not retain the destroyed old renderers.
                AccessTools.Field(typeof(WearNTear),"m_renderers").SetValue(wear,new List<Renderer>(parent.GetComponentsInChildren<Renderer>(true)));
            }
            if(cup)
            {
                // This interaction collider remains a narrow pole, never a solid flag;
                // GolfBall ignores marker colliders, including these refreshed ones.
                BoxCollider box=parent.GetComponent<BoxCollider>();
                if(box!=null){box.center=new Vector3(.43f,FlagHeight/2,0);box.size=new Vector3(.20f,FlagHeight,.20f);}
            }
        }
        internal static void Ball(Transform parent,Material mat)
        {
            Transform root=Root(parent,"GolfModel");
            Part(root,"Painted wooden ball",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.24f,mat);
        }
        internal static Sprite Icon(int kind)
        {
            const int size=128;var tex=new Texture2D(size,size,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                Color c=Color.clear;
                if(kind==0)
                {
                    float dist=Distance(new Vector2(x,y),new Vector2(28,108),new Vector2(84,30));
                    if(dist<4)c=new Color(.65f,.43f,.22f);
                    if(x>=75&&x<=112&&y>=19&&y<=34)c=new Color(.75f,.52f,.28f);
                    if(dist<5&&y>86)c=new Color(.30f,.20f,.12f);
                }
                else
                {
                    float r=new Vector2((x-64)/1f,(y-29)*2.4f).magnitude;
                    if(r<38)c=r>28?new Color(.63f,.43f,.24f):new Color(.23f,.17f,.11f);
                    if(kind==1&&new Vector2(x-64,y-53).magnitude<13)c=Colors[0];
                    if(kind==2&&x>=91&&x<=96&&y>=29&&y<=111)c=new Color(.64f,.44f,.25f);
                    if(kind==2&&x>=38&&x<=91&&y>=75&&y<=108&&!(x<50&&Mathf.Abs(y-91)<(50-x)*.5f))c=new Color(.76f,.095f,.065f);
                    if(kind==2&&Mathf.Abs(x-65)+Mathf.Abs(y-91)>=10&&Mathf.Abs(x-65)+Mathf.Abs(y-91)<=12)c=new Color(.87f,.79f,.60f);
                }
                tex.SetPixel(x,y,c);
            }
            tex.Apply();return Sprite.Create(tex,new Rect(0,0,size,size),new Vector2(.5f,.5f));
        }
        private static float Distance(Vector2 p,Vector2 a,Vector2 b)=>Vector2.Distance(p,a+(b-a)*Mathf.Clamp01(Vector2.Dot(p-a,b-a)/(b-a).sqrMagnitude));
    }
}
