using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Spyglass
{
    // The spyglass item: a bronze-bar clone with its own model, icon and forge recipe, registered under its own prefab name.
    internal static class Item
    {
        internal const string PrefabName="BobSpyglass",DisplayName="Spyglass",RecipeName="Recipe_BobSpyglass";
        internal const int BronzeCost=3;
        private static GameObject _holder,_prefab;
        private static Recipe _recipe;
        private static Sprite _icon;
        private static readonly List<Object> Owned=new List<Object>();
        private static readonly MethodInfo UpdateRegisters=AccessTools.Method(typeof(ObjectDB),"UpdateRegisters");
        private static readonly MethodInfo CloneShared=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);

        internal static bool Is(ItemDrop.ItemData item)=>item!=null&&item.m_shared!=null&&item.m_shared.m_name==DisplayName;

        // Items are also in ZNetScene, and ObjectDB may wake before or after it, so either can supply the source prefab.
        private static GameObject Source(string name,ObjectDB db)
        {
            GameObject found=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(name):null;
            if(found==null&&db!=null)found=db.m_items.FirstOrDefault(i=>i!=null&&i.name==name);
            return found;
        }
        private static GameObject Ensure(ObjectDB db)
        {
            if(_prefab!=null)return _prefab;
            GameObject source=Source("Bronze",db);
            ItemDrop sourceDrop=source!=null?source.GetComponent<ItemDrop>():null;
            if(sourceDrop==null)return null;
            Material basis=source.GetComponentsInChildren<Renderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
            if(basis==null)return null;

            _holder=new GameObject(PrefabName+"Prefabs");
            _holder.SetActive(false); // keeps the copy from waking up as a real object
            Object.DontDestroyOnLoad(_holder);
            GameObject go=Object.Instantiate(source,_holder.transform);
            go.name=PrefabName;
            foreach(LODGroup lod in go.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
            foreach(Collider c in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            foreach(Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if(r==null)continue; // already gone with a destroyed parent
                if(r.gameObject!=go){Object.DestroyImmediate(r.gameObject);continue;}
                MeshFilter filter=go.GetComponent<MeshFilter>();
                Object.DestroyImmediate(r);if(filter!=null)Object.DestroyImmediate(filter);
            }
            Transform model=Model.Build(go.transform,basis,go.layer,Owned);
            var hitbox=go.AddComponent<CapsuleCollider>();
            hitbox.direction=0;hitbox.radius=0.05f;hitbox.height=0.56f;hitbox.center=new Vector3(0.04f,0,0);

            ItemDrop drop=go.GetComponent<ItemDrop>();
            // A clone of the shared data, so the bronze bar keeps its own name, icon and stack size.
            var shared=(ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared,null);
            shared.m_name=DisplayName;
            shared.m_description="A bronze telescope. Shift+Z raises it to your eye; the mouse wheel changes magnification. Where you gaze, your map fills in a little.";
            shared.m_itemType=ItemDrop.ItemData.ItemType.Misc;
            shared.m_maxStackSize=1;shared.m_autoStack=false;shared.m_weight=1;shared.m_value=0;
            shared.m_teleportable=true;shared.m_maxQuality=1;shared.m_useDurability=false;shared.m_questItem=false;
            _icon=Icon.Make(model,Owned);
            shared.m_icons=_icon!=null?new[]{_icon}:sourceDrop.m_itemData.m_shared.m_icons;
            drop.m_itemData.m_shared=shared;
            drop.m_itemData.m_stack=1;drop.m_itemData.m_quality=1;
            drop.m_itemData.m_dropPrefab=go;
            _prefab=go;
            return go;
        }

        // ObjectDB.Awake and CopyOtherDB both rebuild their lookups here; the main menu's copy shares the prefab asset's list.
        internal static void BeforeRegisters(ObjectDB db)
        {
            // Destroyed entries (an earlier copy of this mod) would throw while the lookups are rebuilt.
            db.m_items.RemoveAll(i=>i==null||(i.name==PrefabName&&i!=_prefab));
            db.m_recipes.RemoveAll(r=>r==null||(r.name==RecipeName&&r!=_recipe));
            if(Source("Bronze",db)==null)return; // the empty database the main menu creates before copying
            GameObject prefab=Ensure(db);
            if(prefab==null)return;
            if(!db.m_items.Contains(prefab))db.m_items.Add(prefab);
            AddRecipe(db);
        }
        private static void AddRecipe(ObjectDB db)
        {
            CraftingStation forge=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("forge")?.GetComponent<CraftingStation>():null;
            ItemDrop bronze=Source("Bronze",db)?.GetComponent<ItemDrop>();
            if(db==null||_prefab==null||forge==null||bronze==null)return; // the main menu has no forge; the world's database gets it
            if(_recipe==null)
            {
                _recipe=ScriptableObject.CreateInstance<Recipe>();
                _recipe.name=RecipeName;_recipe.hideFlags=HideFlags.HideAndDontSave;
                _recipe.m_item=_prefab.GetComponent<ItemDrop>();_recipe.m_amount=1;_recipe.m_enabled=true;
                _recipe.m_craftingStation=forge;_recipe.m_minStationLevel=1;
                _recipe.m_resources=new[]{new Piece.Requirement{m_resItem=bronze,m_amount=BronzeCost,m_recover=true}};
            }
            if(!db.m_recipes.Contains(_recipe))db.m_recipes.Add(_recipe);
        }
        internal static void RegisterScene(ZNetScene scene)
        {
            GameObject prefab=Ensure(ObjectDB.instance);
            if(prefab==null){Debug.LogWarning("[Spyglass] Bronze prefab not found; the spyglass is not registered.");return;}
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);
            named[prefab.name.GetStableHashCode()]=prefab; // lets dropped spyglasses load from saves
            if(ObjectDB.instance!=null)AddRecipe(ObjectDB.instance);
        }
        // Hot reload while in a world: the database already has its lookups, so rebuild them with the item in place.
        internal static void RegisterLoaded()
        {
            if(ZNetScene.instance!=null)RegisterScene(ZNetScene.instance);
            if(ObjectDB.instance!=null)UpdateRegisters.Invoke(ObjectDB.instance,null);
            Relink(Player.m_localPlayer?.GetInventory());
        }
        // Items loaded by an earlier copy of the mod still point at its destroyed prefab and icon.
        internal static void Relink(Inventory inventory)
        {
            if(inventory==null||_prefab==null)return;
            ItemDrop.ItemData.SharedData shared=_prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            foreach(ItemDrop.ItemData item in inventory.GetAllItems())
                if(Is(item)&&item.m_shared!=shared&&(item.m_dropPrefab==null||item.m_dropPrefab.name==PrefabName))
                {item.m_shared=shared;item.m_dropPrefab=_prefab;}
        }
        internal static void Unregister()
        {
            if(_prefab==null)return;
            if(ZNetScene.instance!=null)
            {
                ZNetScene.instance.m_prefabs.Remove(_prefab);
                var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(ZNetScene.instance);
                int hash=_prefab.name.GetStableHashCode();
                if(named.TryGetValue(hash,out GameObject current)&&current==_prefab)named.Remove(hash); // only our own entry
            }
            // The world's database and the main menu's prefab asset both may hold it.
            foreach(ObjectDB db in Resources.FindObjectsOfTypeAll<ObjectDB>())
            {
                bool had=db.m_items.Remove(_prefab);
                if(_recipe!=null)db.m_recipes.Remove(_recipe);
                if(had&&db==ObjectDB.instance)UpdateRegisters.Invoke(db,null);
            }
            if(_recipe!=null)Object.Destroy(_recipe);_recipe=null;
            foreach(Object owned in Owned)if(owned!=null)Object.Destroy(owned);Owned.Clear();
            Object.Destroy(_holder);_holder=null;_prefab=null;_icon=null;
        }
    }

    // Telescoping bronze tubes along local X, from eyepiece (negative) to objective lens (positive).
    internal static class Model
    {
        private static readonly (string part,float radius,float length,float x,string look)[] Parts=
        {
            ("Eyepiece",0.022f,0.05f,-0.235f,"Dark"),
            ("Draw",0.028f,0.13f,-0.145f,"Bright"),
            ("DrawRing",0.036f,0.018f,-0.08f,"Dark"),
            ("Middle",0.035f,0.16f,0f,"Bronze"),
            ("MiddleRing",0.044f,0.02f,0.075f,"Dark"),
            ("Barrel",0.042f,0.2f,0.17f,"Bronze"),
            ("Grip",0.045f,0.08f,0.14f,"Leather"),
            ("Hood",0.048f,0.026f,0.268f,"Dark"),
            ("Lens",0.036f,0.004f,0.282f,"Glass"),
        };
        internal static Transform Build(Transform parent,Material basis,int layer,List<Object> owned)
        {
            var looks=new Dictionary<string,Material>
            {
                {"Bronze",Tint(basis,new Color(0.95f,0.72f,0.45f),true)},{"Bright",Tint(basis,new Color(1f,0.84f,0.58f),true)},
                {"Dark",Tint(basis,new Color(0.55f,0.38f,0.24f),true)},{"Leather",Tint(basis,new Color(0.33f,0.2f,0.11f),false)},
                {"Glass",Tint(basis,new Color(0.16f,0.26f,0.34f),false)},
            };
            owned.AddRange(looks.Values);
            var root=new GameObject("Model"){layer=layer};
            root.transform.SetParent(parent,false);
            Mesh cylinder=null;
            foreach(var p in Parts)
            {
                var go=new GameObject(p.part){layer=layer};
                go.transform.SetParent(root.transform,false);
                if(cylinder==null)
                {
                    GameObject primitive=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    cylinder=primitive.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(primitive);
                }
                go.AddComponent<MeshFilter>().sharedMesh=cylinder;
                go.AddComponent<MeshRenderer>().sharedMaterial=looks[p.look];
                // Unity's cylinder is 2 units tall and 1 wide along Y; lay it along X.
                go.transform.localPosition=new Vector3(p.x,0,0);
                go.transform.localRotation=Quaternion.Euler(0,0,90);
                go.transform.localScale=new Vector3(p.radius*2,p.length/2,p.radius*2);
            }
            return root.transform;
        }
        private static Material Tint(Material basis,Color color,bool keepTexture)
        {
            var material=new Material(basis){color=color,hideFlags=HideFlags.HideAndDontSave};
            if(!keepTexture&&material.HasProperty("_MainTex"))material.mainTexture=null;
            return material;
        }
    }

    // The inventory picture: the model drawn by a temporary camera, or a painted fallback if nothing was drawn.
    internal static class Icon
    {
        private const int Size=128;
        internal static Sprite Make(Transform model,List<Object> owned)
        {
            Texture2D texture=null;
            try{texture=Render(model);}
            catch(Exception e){Debug.LogWarning("[Spyglass] could not draw the icon: "+e.Message);}
            if(texture==null)texture=Paint();
            texture.hideFlags=HideFlags.HideAndDontSave;owned.Add(texture);
            Sprite sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),100f);
            sprite.name=Item.PrefabName;sprite.hideFlags=HideFlags.HideAndDontSave;owned.Add(sprite);
            return sprite;
        }
        private static Texture2D Render(Transform model)
        {
            var stage=new GameObject("SpyglassIconStage");
            stage.transform.position=new Vector3(0,-3000,0); // far below the world where nothing else is
            try
            {
                GameObject copy=Object.Instantiate(model.gameObject,stage.transform);
                copy.transform.localPosition=Vector3.zero;
                copy.transform.localRotation=Quaternion.Euler(0,0,38); // lies diagonally, lens up and right
                copy.SetActive(true);
                Renderer[] renderers=copy.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0)return null;
                Bounds bounds=renderers[0].bounds;
                foreach(Renderer r in renderers)bounds.Encapsulate(r.bounds);

                var cameraObject=new GameObject("SpyglassIconCamera");
                cameraObject.transform.SetParent(stage.transform,false);
                Camera camera=cameraObject.AddComponent<Camera>();
                camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                camera.fieldOfView=20;camera.nearClipPlane=0.05f;camera.farClipPlane=20;camera.allowHDR=false;camera.allowMSAA=false;
                float radius=bounds.extents.magnitude;
                camera.transform.position=bounds.center+new Vector3(0.12f,0.18f,-1f).normalized*(radius/Mathf.Sin(10*Mathf.Deg2Rad))*1.02f;
                camera.transform.LookAt(bounds.center);
                var lightObject=new GameObject("SpyglassIconLight");
                lightObject.transform.SetParent(stage.transform,false);
                lightObject.transform.rotation=Quaternion.Euler(40,30,0);
                Light light=lightObject.AddComponent<Light>();
                light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.None;

                RenderTexture target=RenderTexture.GetTemporary(Size*2,Size*2,24,RenderTextureFormat.ARGB32);
                RenderTexture previous=RenderTexture.active;
                try
                {
                    camera.targetTexture=target;camera.Render();
                    RenderTexture.active=target;
                    var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                    texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
                    if(Drawn(texture))return texture;
                    Object.Destroy(texture);return null;
                }
                finally{RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);}
            }
            finally{Object.DestroyImmediate(stage);} // the stage light must not reach the next rendered frame
        }
        private static bool Drawn(Texture2D texture)
        {
            Color32[] pixels=texture.GetPixels32();
            int covered=0;long brightness=0;
            for(int i=0;i<pixels.Length;i+=7)if(pixels[i].a>40){covered++;brightness+=pixels[i].r+pixels[i].g+pixels[i].b;}
            return covered>pixels.Length/7*0.04f&&brightness/Math.Max(1,covered)>60;
        }
        // A diagonal brass tube with rings and a dark lens, for renderers that produce nothing off-screen.
        private static Texture2D Paint()
        {
            var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
            var a=new Vector2(22,26);var b=new Vector2(108,100);
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                var p=new Vector2(x,y);Vector2 ab=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude);
                float distance=(p-(a+ab*t)).magnitude,radius=Mathf.Lerp(6,12,Mathf.SmoothStep(0,1,t*1.4f));
                Color color=new Color(0,0,0,0);
                if(distance<=radius)
                {
                    float shade=0.65f+0.35f*(1-distance/radius);
                    bool ring=Mathf.Abs(t-0.33f)<0.02f||Mathf.Abs(t-0.62f)<0.02f||t>0.97f;
                    color=t>0.985f?new Color(0.16f,0.26f,0.34f):ring?new Color(0.55f,0.38f,0.24f)*shade:new Color(0.95f,0.72f,0.45f)*shade;
                    color.a=Mathf.Clamp01(radius-distance+0.5f);
                }
                texture.SetPixel(x,y,color);
            }
            texture.Apply();return texture;
        }
    }
}
