using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Omens
{
    // The rune bones: a seer's handful of carved bones, made at the workbench. Use them and the host reads them for you: which way the
    // nearest sign lies and roughly how far, whether it feels warm or cold, how the gods see you, and what tonight holds.
    internal static class Seer
    {
        internal const string PrefabName="BobRuneBones",DisplayName="Rune bones",RecipeName="Recipe_BobRuneBones";
        private const string Source="BoneFragments";
        private static GameObject _holder,_prefab;
        private static Recipe _recipe;
        private static float _nextCast;
        private static readonly List<Object> Owned=new List<Object>();
        private static readonly MethodInfo UpdateRegisters=AccessTools.Method(typeof(ObjectDB),"UpdateRegisters");
        private static readonly MethodInfo CloneShared=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly Color Stain=new Color(0.62f,0.42f,0.33f); // old bone, rubbed with ochre and soot

        internal static bool Is(ItemDrop.ItemData item)=>item!=null&&item.m_shared!=null&&item.m_shared.m_name==DisplayName;
        private static bool Particles(Renderer r)=>r.GetType().Name=="ParticleSystemRenderer";
        private static GameObject Find(string name,ObjectDB db)
        {
            GameObject found=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(name):null;
            if(found==null&&db!=null)found=db.m_items.FirstOrDefault(i=>i!=null&&i.name==name);
            return found;
        }

        // ---- the item ----
        private static GameObject Ensure(ObjectDB db)
        {
            if(_prefab!=null)return _prefab;
            GameObject source=Find(Source,db);
            ItemDrop sourceDrop=source!=null?source.GetComponent<ItemDrop>():null;
            if(sourceDrop==null)return null;
            _holder=new GameObject(PrefabName+"Prefabs");
            _holder.SetActive(false); // keeps the copy from waking up as a real object
            Object.DontDestroyOnLoad(_holder);
            GameObject go=Object.Instantiate(source,_holder.transform);
            go.name=PrefabName;
            foreach(Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if(Particles(r))continue; // the item's own glint
                Material[] stained=r.sharedMaterials.Select(m=>
                {
                    if(m==null)return null;
                    var copy=new Material(m){hideFlags=HideFlags.HideAndDontSave};
                    if(copy.HasProperty("_Color"))copy.color=m.color*Stain;
                    Owned.Add(copy);return copy;
                }).ToArray();
                r.sharedMaterials=stained;
            }
            ItemDrop drop=go.GetComponent<ItemDrop>();
            var shared=(ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared,null);
            shared.m_name=DisplayName;
            shared.m_description="A seer's handful of bones, carved with runes and stained with ochre. Cast them (use them) to learn which way the nearest omen lies, how the gods see you, and what tonight holds.";
            shared.m_itemType=ItemDrop.ItemData.ItemType.Misc;
            shared.m_maxStackSize=1;shared.m_autoStack=false;shared.m_weight=0.5f;shared.m_value=0;
            shared.m_teleportable=true;shared.m_maxQuality=1;shared.m_useDurability=false;shared.m_questItem=false;
            Sprite icon=Icon(go);
            if(icon!=null)shared.m_icons=new[]{icon};
            drop.m_itemData.m_shared=shared;
            drop.m_itemData.m_stack=1;drop.m_itemData.m_quality=1;drop.m_itemData.m_dropPrefab=go;
            _prefab=go;
            return go;
        }
        internal static void BeforeRegisters(ObjectDB db)
        {
            db.m_items.RemoveAll(i=>i==null||(i.name==PrefabName&&i!=_prefab));
            db.m_recipes.RemoveAll(r=>r==null||(r.name==RecipeName&&r!=_recipe));
            if(Find(Source,db)==null)return; // the empty database the main menu makes before copying
            GameObject prefab=Ensure(db);
            if(prefab==null)return;
            if(!db.m_items.Contains(prefab))db.m_items.Add(prefab);
            AddRecipe(db);
        }
        private static void AddRecipe(ObjectDB db)
        {
            CraftingStation bench=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>():null;
            ItemDrop bones=Find("BoneFragments",db)?.GetComponent<ItemDrop>(),resin=Find("Resin",db)?.GetComponent<ItemDrop>();
            if(db==null||_prefab==null||bench==null||bones==null||resin==null)return;
            if(_recipe==null)
            {
                _recipe=ScriptableObject.CreateInstance<Recipe>();
                _recipe.name=RecipeName;_recipe.hideFlags=HideFlags.HideAndDontSave;
                _recipe.m_item=_prefab.GetComponent<ItemDrop>();_recipe.m_amount=1;_recipe.m_enabled=true;
                _recipe.m_craftingStation=bench;_recipe.m_minStationLevel=1;
                _recipe.m_resources=new[]
                {
                    new Piece.Requirement{m_resItem=bones,m_amount=6,m_recover=true},
                    new Piece.Requirement{m_resItem=resin,m_amount=2,m_recover=true},
                };
            }
            if(!db.m_recipes.Contains(_recipe))db.m_recipes.Add(_recipe);
        }
        internal static void RegisterScene(ZNetScene scene)
        {
            GameObject prefab=Ensure(ObjectDB.instance);
            if(prefab==null)return;
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);
            named[prefab.name.GetStableHashCode()]=prefab; // dropped bones load from saves
            if(ObjectDB.instance!=null)AddRecipe(ObjectDB.instance);
        }
        // Hot reload while in a world: rebuild the database's lookups with the item in place, and point carried bones at the new prefab.
        internal static void RegisterLoaded()
        {
            if(ZNetScene.instance!=null)RegisterScene(ZNetScene.instance);
            if(ObjectDB.instance!=null)UpdateRegisters.Invoke(ObjectDB.instance,null);
            Inventory inventory=Player.m_localPlayer?.GetInventory();
            if(inventory==null||_prefab==null)return;
            ItemDrop.ItemData.SharedData shared=_prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            foreach(ItemDrop.ItemData item in inventory.GetAllItems())
                if(Is(item)&&item.m_shared!=shared&&(item.m_dropPrefab==null||item.m_dropPrefab.name==PrefabName)){item.m_shared=shared;item.m_dropPrefab=_prefab;}
        }
        internal static void Unregister()
        {
            if(_prefab==null)return;
            if(ZNetScene.instance!=null)
            {
                ZNetScene.instance.m_prefabs.Remove(_prefab);
                var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(ZNetScene.instance);
                int hash=_prefab.name.GetStableHashCode();
                if(named.TryGetValue(hash,out GameObject current)&&current==_prefab)named.Remove(hash);
            }
            foreach(ObjectDB db in Resources.FindObjectsOfTypeAll<ObjectDB>())
            {
                bool had=db.m_items.Remove(_prefab);
                if(_recipe!=null)db.m_recipes.Remove(_recipe);
                if(had&&db==ObjectDB.instance)UpdateRegisters.Invoke(db,null);
            }
            if(_recipe!=null)Object.Destroy(_recipe);_recipe=null;
            Owned.Clear(); // materials and icon stay alive for bones already in the world until they reload
            Object.Destroy(_holder);_holder=null;_prefab=null;
        }

        // ---- casting ----
        internal static void Cast(Player player)
        {
            if(Time.time<_nextCast){player.Message(MessageHud.MessageType.Center,"The bones are still settling.");return;}
            _nextCast=Time.time+Policy.CastCooldown;
            player.Message(MessageHud.MessageType.TopLeft,"You cast the rune bones...");
            GameObject sound=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("sfx_bones_pick"):null;
            if(sound!=null)Object.Instantiate(sound,player.transform.position,Quaternion.identity);
            Net.Cast(player.transform.position); // the host answers (Net.OnAnswer)
        }

        // ---- the icon: the stained bones drawn by a temporary camera far below the world ----
        private static Sprite Icon(GameObject item)
        {
            try
            {
                var stage=new GameObject("BobRuneBonesIconStage");
                stage.transform.position=new Vector3(0,-3000,0);
                try
                {
                    int layer=31;for(int l=31;l>8;l--)if(string.IsNullOrEmpty(LayerMask.LayerToName(l))){layer=l;break;}
                    var model=new GameObject("Model");model.transform.SetParent(stage.transform,false);
                    // Only the most detailed level of a model that has several.
                    var lod0=new HashSet<Renderer>(item.GetComponentsInChildren<LODGroup>(true).SelectMany(g=>g.GetLODs().Take(1)).SelectMany(l=>l.renderers).Where(r=>r!=null));
                    var lodded=new HashSet<Renderer>(item.GetComponentsInChildren<LODGroup>(true).SelectMany(g=>g.GetLODs()).SelectMany(l=>l.renderers).Where(r=>r!=null));
                    foreach(MeshRenderer r in item.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        MeshFilter f=r.GetComponent<MeshFilter>();
                        if(f==null||f.sharedMesh==null||lodded.Contains(r)&&!lod0.Contains(r))continue;
                        var part=new GameObject(r.name){layer=layer};part.transform.SetParent(model.transform,false);
                        part.transform.localPosition=item.transform.InverseTransformPoint(r.transform.position);
                        part.transform.localRotation=Quaternion.Inverse(item.transform.rotation)*r.transform.rotation;
                        part.transform.localScale=r.transform.lossyScale;
                        part.AddComponent<MeshFilter>().sharedMesh=f.sharedMesh;
                        part.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
                    }
                    model.transform.localRotation=Quaternion.Euler(25,30,0);
                    Renderer[] renderers=model.GetComponentsInChildren<Renderer>();
                    if(renderers.Length==0)return null;
                    Bounds bounds=renderers[0].bounds;foreach(Renderer r in renderers)bounds.Encapsulate(r.bounds);
                    var cameraObject=new GameObject("Camera");cameraObject.transform.SetParent(stage.transform,false);
                    Camera camera=cameraObject.AddComponent<Camera>();
                    camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                    camera.fieldOfView=20;camera.nearClipPlane=0.05f;camera.farClipPlane=50;camera.allowHDR=false;camera.allowMSAA=false;
                    float radius=Mathf.Max(0.05f,bounds.extents.magnitude);
                    camera.transform.position=bounds.center+new Vector3(0.1f,0.6f,-1f).normalized*(radius/Mathf.Sin(10*Mathf.Deg2Rad))*1.05f;
                    camera.transform.LookAt(bounds.center);
                    var lightObject=new GameObject("Light");lightObject.transform.SetParent(stage.transform,false);lightObject.transform.rotation=Quaternion.Euler(45,30,0);
                    Light light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.shadows=LightShadows.None;light.cullingMask=1<<layer;
                    const int size=256;
                    RenderTexture target=RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
                    RenderTexture previous=RenderTexture.active;
                    try
                    {
                        camera.targetTexture=target;camera.Render();
                        RenderTexture.active=target;
                        var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
                        texture.ReadPixels(new Rect(0,0,size,size),0,0);texture.Apply();
                        if(!Drawn(texture)){Object.Destroy(texture);return null;}
                        texture.hideFlags=HideFlags.HideAndDontSave;Owned.Add(texture);
                        Sprite sprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(0.5f,0.5f),100f);
                        sprite.name=PrefabName;sprite.hideFlags=HideFlags.HideAndDontSave;Owned.Add(sprite);
                        return sprite;
                    }
                    finally{RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);}
                }
                finally{Object.DestroyImmediate(stage);}
            }
            catch(Exception e){Debug.LogWarning("[Omens] could not draw the rune bones' icon: "+e.Message);return null;} // the bone fragments' own icon stays
        }
        // Something was drawn, centred, with clear corners.
        private static bool Drawn(Texture2D texture)
        {
            Color32[] pixels=texture.GetPixels32();
            int w=texture.width,h=texture.height,edge=w/16,covered=0;
            foreach(var (cx,cy) in new[]{(0,0),(w-edge,0),(0,h-edge),(w-edge,h-edge)})
                for(int y=cy;y<cy+edge;y++)for(int x=cx;x<cx+edge;x++)if(pixels[y*w+x].a>8)return false;
            for(int i=0;i<pixels.Length;i+=7)if(pixels[i].a>40)covered++;
            return covered>pixels.Length/7*0.03f;
        }
    }

    [HarmonyPatch(typeof(ObjectDB),"UpdateRegisters")]
    internal static class RegisterBones
    {
        private static void Prefix(ObjectDB __instance)=>Seer.BeforeRegisters(__instance);
    }
    [HarmonyPatch(typeof(Humanoid),"UseItem")]
    internal static class CastBones
    {
        private static bool Prefix(Humanoid __instance,ItemDrop.ItemData __1)
        {
            if(!Seer.Is(__1)||!(__instance is Player player)||player!=Player.m_localPlayer)return true;
            Seer.Cast(player);
            return false;
        }
    }
}
