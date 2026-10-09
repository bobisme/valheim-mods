using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MeadowGolf
{
    internal static class Models
    {
        internal static readonly Color[] Colors={new Color(.87f,.44f,.18f),new Color(.24f,.52f,.71f),new Color(.77f,.67f,.26f),
            new Color(.55f,.36f,.61f),new Color(.39f,.63f,.36f),new Color(.76f,.31f,.30f),new Color(.76f,.78f,.70f),new Color(.29f,.67f,.62f)};
        internal static Material Wood,Dark,Cloth;
        internal static readonly List<Material> BallColors=new List<Material>();
        internal static void Init(Material basis)
        {
            if(Wood!=null)return;
            Wood=Tint(basis,new Color(.61f,.42f,.23f),true);Dark=Tint(basis,new Color(.22f,.16f,.10f),false);
            Cloth=Tint(basis,new Color(.87f,.79f,.60f),false);
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
            Part(root,"Carved head",PrimitiveType.Cube,new Vector3(.085f,-.78f,.015f),new Vector3(.24f,.09f,.12f),Wood,Quaternion.Euler(0,0,-8));
            Part(root,"Face",PrimitiveType.Cube,new Vector3(.085f,-.78f,.080f),new Vector3(.21f,.06f,.012f),Dark,Quaternion.Euler(0,0,-8));
            for(int i=0;i<3;i++)Part(root,"Binding",PrimitiveType.Cylinder,new Vector3(0,-.65f-i*.03f,0),new Vector3(.047f,.006f,.047f),Dark);
        }
        internal static void Marker(Transform parent,bool cup)
        {
            Transform root=Root(parent,"GolfModel");
            if(!cup)
            {
                Part(root,"Tee pad",PrimitiveType.Cylinder,new Vector3(0,.025f,0),new Vector3(.75f,.025f,.75f),Wood);
                Part(root,"Tee",PrimitiveType.Cylinder,new Vector3(0,.09f,.50f),new Vector3(.08f,.09f,.08f),Dark);
                Part(root,"Start rune",PrimitiveType.Cube,new Vector3(0,.053f,0),new Vector3(.05f,.006f,.28f),Cloth);
                Part(root,"Start rune",PrimitiveType.Cube,new Vector3(.08f,.053f,.05f),new Vector3(.2f,.006f,.04f),Cloth,Quaternion.Euler(0,-35,0));
                return;
            }
            // A visual cup; its ring never blocks a rolling ball and does not edit the ground.
            Part(root,"Cup shadow",PrimitiveType.Cylinder,new Vector3(0,.012f,0),new Vector3(.62f,.006f,.62f),Dark);
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8;
                Part(root,"Cup rim",PrimitiveType.Cube,new Vector3(Mathf.Cos(a)*.32f,.032f,Mathf.Sin(a)*.32f),
                    new Vector3(.125f,.045f,.06f),Wood,Quaternion.Euler(0,-a*Mathf.Rad2Deg+90,0));
            }
            Part(root,"Flagstaff",PrimitiveType.Cylinder,new Vector3(.43f,.85f,0),new Vector3(.045f,.85f,.045f),Wood);
            Part(root,"Linen flag",PrimitiveType.Cube,new Vector3(.70f,1.47f,0),new Vector3(.5f,.28f,.012f),Cloth);
            Part(root,"Flag border",PrimitiveType.Cube,new Vector3(.70f,1.35f,0),new Vector3(.5f,.025f,.016f),Dark);
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
                    if(kind==2&&x>=45&&x<=91&&y>=83&&y<=108)c=new Color(.91f,.82f,.60f);
                }
                tex.SetPixel(x,y,c);
            }
            tex.Apply();return Sprite.Create(tex,new Rect(0,0,size,size),new Vector2(.5f,.5f));
        }
        private static float Distance(Vector2 p,Vector2 a,Vector2 b)=>Vector2.Distance(p,a+(b-a)*Mathf.Clamp01(Vector2.Dot(p-a,b-a)/(b-a).sqrMagnitude));
    }
}
