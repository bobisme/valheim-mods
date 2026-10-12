using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Delivery
{
    // The navigator's things: each a clone of a plain game item with its own model (built from simple shapes), icon and recipe.
    internal sealed class ItemDef
    {
        internal string Prefab,Name,Description,Base,Station;
        internal int Level=1,Stack=1;
        internal float Weight=0.5f;
        internal (string item,int amount)[] Cost;
        internal Action<Transform,Looks> Model;
        internal Vector3 IconTurn=new Vector3(0,0,0);
        internal GameObject Built;
        internal Recipe Recipe;
    }

    internal static class Items
    {
        internal static readonly ItemDef Horn=new ItemDef{Prefab="BobErrandHorn",Name="Errand Horn",Base="Wood",Station="piece_workbench",Weight=1f,
            Cost=new[]{("Wood",4),("LeatherScraps",2),("Resin",1)},Model=Models.Horn,IconTurn=new Vector3(0,0,15),
            Description="A wooden horn, bound in birch bark and leather, with a call carved beside every meal it knows. Use it to choose a meal: each has its own call, and somewhere a courier hears it, takes the food off the hearth and runs it to you. He wants coin for the food and the run, and remembers who tips."};
        internal static readonly ItemDef[] All={Horn};

        private static GameObject _holder;
        private static readonly List<Object> Owned=new List<Object>();
        private static readonly MethodInfo UpdateRegisters=AccessTools.Method(typeof(ObjectDB),"UpdateRegisters");
        private static readonly MethodInfo CloneShared=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);

        internal static bool Is(ItemDrop.ItemData item,ItemDef def)=>item!=null&&item.m_shared!=null&&item.m_shared.m_name==def.Name;
        internal static bool Has(Player p,ItemDef def)=>p!=null&&p.GetInventory()!=null&&p.GetInventory().GetAllItems().Any(i=>Is(i,def));
        private static bool Particles(Renderer r)=>r.GetType().Name=="ParticleSystemRenderer";

        private static GameObject Source(string name,ObjectDB db)
        {
            GameObject found=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(name):null;
            if(found==null&&db!=null)found=db.m_items.FirstOrDefault(i=>i!=null&&i.name==name);
            return found;
        }
        private static GameObject Ensure(ItemDef def,ObjectDB db)
        {
            if(def.Built!=null)return def.Built;
            GameObject source=Source(def.Base,db);
            ItemDrop sourceDrop=source!=null?source.GetComponent<ItemDrop>():null;
            if(sourceDrop==null)return null;
            var looks=new Looks(db,Owned);
            if(!looks.Ready)return null;
            if(_holder==null)
            {
                _holder=new GameObject("DeliveryItems");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);
            }
            GameObject go=Object.Instantiate(source,_holder.transform);
            go.name=def.Prefab;
            foreach(LODGroup lod in go.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
            foreach(Collider c in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            foreach(Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if(r==null||Particles(r))continue;
                if(r.gameObject!=go){Object.DestroyImmediate(r.gameObject);continue;}
                MeshFilter filter=go.GetComponent<MeshFilter>();
                Object.DestroyImmediate(r);if(filter!=null)Object.DestroyImmediate(filter);
            }
            var model=new GameObject("Model"){layer=go.layer};
            model.transform.SetParent(go.transform,false);
            def.Model(model.transform,looks);
            foreach(Transform t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=go.layer;
            Bounds b=Bounds(model.transform);
            var hitbox=go.AddComponent<BoxCollider>();
            hitbox.center=go.transform.InverseTransformPoint(b.center);hitbox.size=Vector3.Max(b.size,Vector3.one*0.08f);

            ItemDrop drop=go.GetComponent<ItemDrop>();
            var shared=(ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared,null);
            shared.m_name=def.Name;shared.m_description=def.Description;
            shared.m_itemType=ItemDrop.ItemData.ItemType.Misc;
            shared.m_maxStackSize=def.Stack;shared.m_autoStack=false;shared.m_weight=def.Weight;shared.m_value=0;
            shared.m_teleportable=true;shared.m_maxQuality=1;shared.m_useDurability=false;shared.m_questItem=false;
            Sprite icon=Icons.Make(model.transform,def,Owned);
            shared.m_icons=icon!=null?new[]{icon}:sourceDrop.m_itemData.m_shared.m_icons;
            drop.m_itemData.m_shared=shared;
            drop.m_itemData.m_stack=1;drop.m_itemData.m_quality=1;
            drop.m_itemData.m_dropPrefab=go;
            def.Built=go;
            return go;
        }
        internal static Bounds Bounds(Transform root)
        {
            Renderer[] rs=root.GetComponentsInChildren<Renderer>(true);
            if(rs.Length==0)return new Bounds(root.position,Vector3.one*0.1f);
            Bounds b=rs[0].bounds;foreach(Renderer r in rs)b.Encapsulate(r.bounds);
            return b;
        }

        internal static void BeforeRegisters(ObjectDB db)
        {
            foreach(ItemDef def in All)
            {
                db.m_items.RemoveAll(i=>i==null||(i.name==def.Prefab&&i!=def.Built));
                db.m_recipes.RemoveAll(r=>r==null||(r.name=="Recipe_"+def.Prefab&&r!=def.Recipe));
            }
            if(Source("Wood",db)==null)return; // the empty database the main menu makes before copying
            foreach(ItemDef def in All)
            {
                GameObject prefab=Ensure(def,db);
                if(prefab==null)continue;
                if(!db.m_items.Contains(prefab))db.m_items.Add(prefab);
                AddRecipe(def,db);
            }
        }
        private static void AddRecipe(ItemDef def,ObjectDB db)
        {
            CraftingStation station=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(def.Station)?.GetComponent<CraftingStation>():null;
            if(db==null||def.Built==null||station==null)return;
            if(def.Recipe==null)
            {
                var needs=new List<Piece.Requirement>();
                foreach(var (item,amount) in def.Cost)
                {
                    ItemDrop drop=Source(item,db)?.GetComponent<ItemDrop>();
                    if(drop==null){Plugin.Log($"{def.Name}: no {item} in the game; left out of its recipe.");continue;}
                    needs.Add(new Piece.Requirement{m_resItem=drop,m_amount=amount,m_recover=true});
                }
                def.Recipe=ScriptableObject.CreateInstance<Recipe>();
                def.Recipe.name="Recipe_"+def.Prefab;def.Recipe.hideFlags=HideFlags.HideAndDontSave;
                def.Recipe.m_item=def.Built.GetComponent<ItemDrop>();def.Recipe.m_amount=1;def.Recipe.m_enabled=true;
                def.Recipe.m_craftingStation=station;def.Recipe.m_minStationLevel=def.Level;
                def.Recipe.m_resources=needs.ToArray();
            }
            if(!db.m_recipes.Contains(def.Recipe))db.m_recipes.Add(def.Recipe);
        }
        internal static void RegisterScene(ZNetScene scene)
        {
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            foreach(ItemDef def in All)
            {
                GameObject prefab;
                try{prefab=Ensure(def,ObjectDB.instance);}
                catch(Exception e){Debug.LogError($"[Delivery] Could not make the {def.Name}: {e}");continue;}
                if(prefab==null)continue;
                scene.m_prefabs.RemoveAll(x=>x!=prefab&&x!=null&&x.name==prefab.name);
                if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);
                named[prefab.name.GetStableHashCode()]=prefab;
                if(ObjectDB.instance!=null)AddRecipe(def,ObjectDB.instance);
            }
        }
        internal static void RegisterLoaded()
        {
            if(ZNetScene.instance!=null)RegisterScene(ZNetScene.instance);
            if(ObjectDB.instance!=null)UpdateRegisters.Invoke(ObjectDB.instance,null);
            Relink(Player.m_localPlayer?.GetInventory());
        }
        // Items loaded by an earlier copy of the mod still point at its destroyed prefab and icon.
        internal static void Relink(Inventory inventory)
        {
            if(inventory==null)return;
            foreach(ItemDef def in All)
            {
                if(def.Built==null)continue;
                var shared=def.Built.GetComponent<ItemDrop>().m_itemData.m_shared;
                foreach(ItemDrop.ItemData item in inventory.GetAllItems())
                    if(Is(item,def)&&item.m_shared!=shared&&(item.m_dropPrefab==null||item.m_dropPrefab.name==def.Prefab)){item.m_shared=shared;item.m_dropPrefab=def.Built;}
            }
        }
        // Unloading: out of the database and its recipes; known to the scene until the next copy replaces them (dropped ones keep loading).
        internal static void Unregister()
        {
            foreach(ObjectDB db in Resources.FindObjectsOfTypeAll<ObjectDB>())
            {
                bool had=false;
                foreach(ItemDef def in All)
                {
                    if(def.Built!=null)had|=db.m_items.Remove(def.Built);
                    if(def.Recipe!=null)db.m_recipes.Remove(def.Recipe);
                }
                if(had&&db==ObjectDB.instance)UpdateRegisters.Invoke(db,null);
            }
            foreach(ItemDef def in All){if(def.Recipe!=null)Object.Destroy(def.Recipe);def.Recipe=null;def.Built=null;}
            if(ZNetScene.instance==null&&_holder!=null)Object.Destroy(_holder);
            _holder=null;
            Owned.Clear(); // (materials and icons stay: items in chests still draw with them until reloaded)
        }
    }

    // Materials for the models, taken from the game's own items and tinted.
    internal sealed class Looks
    {
        private readonly Dictionary<string,Material> _made=new Dictionary<string,Material>();
        private readonly List<Object> _owned;
        private readonly Material _wood,_bronze,_stone,_crystal,_leather,_feather;
        internal bool Ready=>_wood!=null&&_bronze!=null;
        internal Looks(ObjectDB db,List<Object> owned)
        {
            _owned=owned;
            _wood=Of("FineWood",db)??Of("Wood",db);_bronze=Of("Bronze",db);_stone=Of("Stone",db)??_wood;
            _crystal=Of("Crystal",db)??_bronze;_leather=Of("LeatherScraps",db)??_wood;_feather=Of("Feathers",db)??_leather;
        }
        private static Material Of(string item,ObjectDB db)
        {
            GameObject go=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(item):null;
            if(go==null&&db!=null)go=db.m_items.FirstOrDefault(i=>i!=null&&i.name==item);
            return go?.GetComponentsInChildren<Renderer>(true).Where(r=>r.GetType().Name!="ParticleSystemRenderer").Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
        }
        internal Material Get(string look)
        {
            if(_made.TryGetValue(look,out Material m))return m;
            switch(look)
            {
                case "Wood":m=Tint(_wood,new Color(0.95f,0.85f,0.7f),true);break;
                case "DarkWood":m=Tint(_wood,new Color(0.45f,0.33f,0.22f),true);break;
                case "Burnt":m=Tint(_wood,new Color(0.12f,0.09f,0.07f),false);break;
                case "Bronze":m=Metal(new Color(0.62f,0.4f,0.2f));break;
                case "Gold":m=Metal(new Color(0.95f,0.7f,0.22f));break;
                case "Stone":m=Tint(_stone,new Color(0.75f,0.73f,0.7f),true);break;
                case "Crystal":m=Tint(_crystal,new Color(0.95f,0.98f,1f),true);break;
                case "Leather":m=Tint(_leather,new Color(0.55f,0.38f,0.24f),true);break;
                case "Horn0":case "Horn1":case "Horn2":case "Horn3":case "Horn4":
                {
                    // horn: near black at the tip, through brown, to pale cream at the bell
                    float k=(look[4]-'0')/4f;
                    m=Tint(_wood,Color.Lerp(new Color(0.22f,0.15f,0.1f),new Color(1f,0.9f,0.72f),Mathf.Pow(k,0.8f)),true);
                    break;
                }
                case "Wicker":m=Tint(_wood,new Color(0.8f,0.65f,0.4f),true);break;
                case "Glow":
                    m=Tint(_wood,Color.white,false);
                    if(m.HasProperty("_MainTex"))m.mainTexture=Texture2D.whiteTexture;
                    m.EnableKeyword("_EMISSION");
                    if(m.HasProperty("_EmissionMap"))m.SetTexture("_EmissionMap",Texture2D.whiteTexture);
                    m.SetColor("_EmissionColor",new Color(1f,0.6f,0.25f)*0.05f);
                    m.color=new Color(1f,0.85f,0.6f);
                    break;
                case "Raven":m=Tint(_feather,new Color(0.09f,0.08f,0.11f),false);break;
                case "Beak":m=Tint(_stone,new Color(0.2f,0.18f,0.17f),false);break;
                default:m=Tint(_wood,Color.white,true);break;
            }
            _made[look]=m;
            return m;
        }
        // Untextured, a little shiny: the bronze bar's own texture tints green and silver.
        private Material Metal(Color c)
        {
            Material m=Tint(_bronze,c,false);
            if(m.HasProperty("_MainTex"))m.mainTexture=Texture2D.whiteTexture;
            if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",0.6f);
            if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",0.55f);
            return m;
        }
        private Material Tint(Material basis,Color c,bool keepTexture)
        {
            var m=new Material(basis){color=c,hideFlags=HideFlags.HideAndDontSave};
            if(!keepTexture&&m.HasProperty("_MainTex"))m.mainTexture=null;
            _owned.Add(m);
            return m;
        }
    }

    // The inventory picture: the model drawn alone by a temporary camera (as Spyglass does), or a plain disc if nothing drew.
    internal static class Icons
    {
        private const int Size=128;
        internal static Sprite Make(Transform model,ItemDef def,List<Object> owned)
        {
            Texture2D texture=null;
            try{texture=Render(model,def.IconTurn);}
            catch(Exception e){Debug.LogWarning($"[Delivery] Could not draw the {def.Name} icon: {e.Message}");}
            if(texture==null)texture=Plain();
            texture.hideFlags=HideFlags.HideAndDontSave;owned.Add(texture);
            Sprite sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),100f);
            sprite.name=def.Prefab;sprite.hideFlags=HideFlags.HideAndDontSave;owned.Add(sprite);
            return sprite;
        }
        internal static Texture2D Render(Transform model,Vector3 turn,int size=Size,Vector3? from=null)
        {
            var stage=new GameObject("DeliveryIconStage");
            stage.transform.position=new Vector3(0,-3000,0);
            try
            {
                GameObject copy=Object.Instantiate(model.gameObject,stage.transform);
                copy.transform.localPosition=Vector3.zero;copy.transform.localRotation=Quaternion.Euler(turn);
                copy.SetActive(true);
                int layer=StageLayer();
                foreach(Transform t in copy.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
                Renderer[] renderers=copy.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0)return null;
                Bounds bounds=renderers[0].bounds;foreach(Renderer r in renderers)bounds.Encapsulate(r.bounds);
                var cameraObject=new GameObject("DeliveryIconCamera");
                cameraObject.transform.SetParent(stage.transform,false);
                Camera camera=cameraObject.AddComponent<Camera>();
                camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                camera.fieldOfView=20;camera.nearClipPlane=0.05f;camera.farClipPlane=60;camera.allowHDR=false;camera.allowMSAA=false;
                float radius=bounds.extents.magnitude;
                camera.transform.position=bounds.center+(from??new Vector3(0.1f,0.15f,-1f)).normalized*(radius/Mathf.Sin(10*Mathf.Deg2Rad))*1.02f;
                camera.transform.LookAt(bounds.center);
                var lightObject=new GameObject("DeliveryIconLight");
                lightObject.transform.SetParent(stage.transform,false);
                lightObject.transform.rotation=Quaternion.Euler(40,30,0);
                Light light=lightObject.AddComponent<Light>();
                light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.None;light.cullingMask=1<<layer;
                RenderTexture target=RenderTexture.GetTemporary(size*2,size*2,24,RenderTextureFormat.ARGB32);
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
            finally{Object.DestroyImmediate(stage);}
        }
        private static int StageLayer()
        {
            for(int layer=31;layer>8;layer--)if(string.IsNullOrEmpty(LayerMask.LayerToName(layer)))return layer;
            return 31;
        }
        private static bool Drawn(Texture2D texture)
        {
            Color32[] pixels=texture.GetPixels32();
            int w=texture.width,h=texture.height,edge=Math.Max(2,w/16);
            foreach(var (cx,cy) in new[]{(0,0),(w-edge,0),(0,h-edge),(w-edge,h-edge)})
                for(int y=cy;y<cy+edge;y++)for(int x=cx;x<cx+edge;x++)
                    if(pixels[y*w+x].a>8)return false;
            int covered=0;for(int i=0;i<pixels.Length;i+=7)if(pixels[i].a>40)covered++;
            return covered>pixels.Length/7*0.03f;
        }
        private static Texture2D Plain()
        {
            var t=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
            float c=(Size-1)/2f;
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                float r=Mathf.Sqrt((x-c)*(x-c)+(y-c)*(y-c))/c;
                t.SetPixel(x,y,r<0.8f?new Color(0.7f,0.55f,0.35f,Mathf.Clamp01((0.8f-r)*20)):new Color(0,0,0,0));
            }
            t.Apply();return t;
        }
    }

    [HarmonyPatch(typeof(ObjectDB),"UpdateRegisters")]
    internal static class RegisterItems
    {
        private static void Prefix(ObjectDB __instance)=>Items.BeforeRegisters(__instance);
    }
}
