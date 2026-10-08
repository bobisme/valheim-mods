using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BobsPipes
{
    internal static class Items
    {
        internal const string PipeName = "BobCarvedPipe", PipeDisplay = "Carved Pipe";
        internal static readonly PipeEffect[] Effects = new PipeEffect[3];
        internal static readonly List<Object> Owned = new List<Object>();
        private static readonly Dictionary<string,GameObject> Prefabs = new Dictionary<string,GameObject>();
        private static readonly Dictionary<string,Recipe> Recipes = new Dictionary<string,Recipe>();
        private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly MethodInfo Registers = AccessTools.Method(typeof(ObjectDB),"UpdateRegisters");
        private static GameObject _holder;
        private static bool _busy;
        internal static bool IsPipe(ItemDrop.ItemData item) => Matches(item,PipeName,PipeDisplay);
        private static bool Matches(ItemDrop.ItemData item,string prefab,string display) => item?.m_shared!=null&&item.m_shared.m_name==display&&
            (item.m_dropPrefab==null || Utils.GetPrefabName(item.m_dropPrefab)==prefab);
        internal static int BlendIndex(ItemDrop.ItemData item)
        { for(int i=0;i<Blend.All.Length;i++)if(Matches(item,Blend.All[i].Prefab,Blend.All[i].Name))return i; return -1; }
        private static GameObject Source(string name,ObjectDB db) =>
            ZNetScene.instance?.GetPrefab(name) ?? db?.m_items.FirstOrDefault(i=>i!=null&&i.name==name);

        private static bool Build(ObjectDB db)
        {
            if(_holder!=null)return true;
            GameObject wood=Source("Wood",db); ItemDrop original=wood?.GetComponent<ItemDrop>();
            Material basis=wood?.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
            if(original==null||basis==null)return false;
            _holder=new GameObject("BobsPipesPrefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder);
            GameObject Make(string name,string display,string description,bool pipe,int index)
            {
                GameObject go=Object.Instantiate(wood,_holder.transform); go.name=name;
                foreach(LODGroup lod in go.GetComponents<LODGroup>())Object.DestroyImmediate(lod);
                for(int i=go.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
                foreach(Renderer r in go.GetComponents<Renderer>())Object.DestroyImmediate(r);
                foreach(MeshFilter m in go.GetComponents<MeshFilter>())Object.DestroyImmediate(m);
                foreach(Collider c in go.GetComponents<Collider>())Object.DestroyImmediate(c);
                ItemDrop drop=go.GetComponent<ItemDrop>(); drop.m_itemData=original.m_itemData.Clone();
                var shared=(ItemDrop.ItemData.SharedData)CloneShared.Invoke(original.m_itemData.m_shared,null);
                shared.m_name=display; shared.m_description=description;
                shared.m_itemType=pipe?ItemDrop.ItemData.ItemType.Misc:ItemDrop.ItemData.ItemType.Material;
                shared.m_maxStackSize=pipe?1:20; shared.m_autoStack=!pipe; shared.m_weight=pipe?0.5f:0.1f;
                shared.m_maxQuality=1; shared.m_useDurability=false; shared.m_teleportable=true; shared.m_value=0;
                shared.m_questItem=false; shared.m_dlc=""; shared.m_variants=0;
                shared.m_consumeStatusEffect=null; shared.m_equipStatusEffect=null; shared.m_buildPieces=null;
                shared.m_food=shared.m_foodStamina=shared.m_foodEitr=0;
                Transform model=pipe?Model.Build(go.transform,basis,go.layer,Owned).Root:Model.Tin(go.transform,basis,go.layer,index,Owned);
                shared.m_icons=new[]{Icon.Make(model,Owned)};
                drop.m_itemData.m_shared=shared; drop.m_itemData.m_dropPrefab=go; drop.m_itemData.m_stack=1;
                drop.m_itemData.m_quality=1; drop.m_itemData.m_customData=new Dictionary<string,string>();
                BoxCollider box=go.AddComponent<BoxCollider>();
                box.center=pipe?new Vector3(0.015f,-0.015f,0.1f):Vector3.zero;
                box.size=pipe?new Vector3(0.10f,0.12f,0.25f):new Vector3(0.13f,0.07f,0.13f);
                Prefabs[name]=go; return go;
            }
            Make(PipeName,PipeDisplay,"A hand-carved corewood pipe with a leather-wrapped stem and a hollow bowl. Use a tobacco tin to pack it, then use the pipe near a fire to light it. Use again to save the unfinished bowl. The pipe is never consumed.",true,0);
            for(int i=0;i<Blend.All.Length;i++)
            {
                Blend blend=Blend.All[i]; GameObject tin=Make(blend.Prefab,blend.Name,blend.Flavour+" One tin fills one empty pipe; use it from inventory or hotbar to choose this blend.",false,i);
                var effect=ScriptableObject.CreateInstance<PipeEffect>(); effect.name=blend.Effect; effect.BlendIndex=i;
                effect.m_name=new[]{"Mellow Pipe","Honeywood Pipe","Cloudberry Pipe"}[i]; effect.m_category="bob_pipe";
                effect.m_icon=tin.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons[0]; effect.m_tooltip=blend.Flavour;
                effect.m_ttl=300; Effects[i]=effect;
            }
            return true;
        }
        internal static void Register(ObjectDB db=null,bool refresh=true)
        {
            if(_busy)return;
            _busy=true;
            try
            {
                db=db??ObjectDB.instance;
                if(!Build(db))return;
                ZNetScene scene=ZNetScene.instance;
                if(scene!=null)
                {
                    var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
                    if(named!=null)foreach(var pair in Prefabs)
                    {
                        scene.m_prefabs.RemoveAll(p=>p==null || p.name==pair.Key&&p!=pair.Value);
                        if(!scene.m_prefabs.Contains(pair.Value))scene.m_prefabs.Add(pair.Value);
                        named[pair.Key.GetStableHashCode()]=pair.Value;
                    }
                }
                if(db==null)return;
                bool changed=false;
                foreach(var pair in Prefabs)
                {
                    db.m_items.RemoveAll(p=>p==null||p.name==pair.Key&&p!=pair.Value);
                    if(!db.m_items.Contains(pair.Value)){db.m_items.Add(pair.Value);changed=true;}
                }
                foreach(PipeEffect effect in Effects)
                {
                    db.m_StatusEffects.RemoveAll(e=>e==null || e.name==effect.name&&e!=effect);
                    if(!db.m_StatusEffects.Contains(effect))db.m_StatusEffects.Add(effect);
                }
                RecipeFor(db,PipeName,"piece_workbench",1,1,new[]{"Wood","RoundLog","LeatherScraps"},new[]{4,2,1});
                for(int i=0;i<Blend.All.Length;i++)
                {
                    Blend blend=Blend.All[i];
                    RecipeFor(db,blend.Prefab,"dh_cigar_table",i==0?1:2,2,
                        blend.Extra.Length==0?new[]{blend.Leaf}:new[]{blend.Leaf,blend.Extra},
                        blend.Extra.Length==0?new[]{2}:new[]{2,blend.ExtraCount});
                }
                if(refresh&&changed)Registers.Invoke(db,null);
            }
            finally{_busy=false;}
        }
        private static void RecipeFor(ObjectDB db,string name,string station,int level,int amount,string[] resources,int[] counts)
        {
            CraftingStation table=Source(station,db)?.GetComponent<CraftingStation>();
            ItemDrop[] items=resources.Select(r=>Source(r,db)?.GetComponent<ItemDrop>()).ToArray();
            string recipeName="Recipe_"+name;
            if(table==null||items.Any(i=>i==null))
            { if(Recipes.TryGetValue(name,out Recipe old))db.m_recipes.Remove(old); return; }
            if(!Recipes.TryGetValue(name,out Recipe recipe))
            { recipe=ScriptableObject.CreateInstance<Recipe>(); recipe.name=recipeName; Recipes[name]=recipe; }
            recipe.m_item=Prefabs[name].GetComponent<ItemDrop>(); recipe.m_amount=amount; recipe.m_enabled=true;
            recipe.m_craftingStation=table; recipe.m_minStationLevel=level;
            recipe.m_resources=items.Select((item,i)=>new Piece.Requirement{m_resItem=item,m_amount=counts[i],m_recover=false}).ToArray();
            db.m_recipes.RemoveAll(r=>r==null||r.name==recipeName&&r!=recipe);
            if(!db.m_recipes.Contains(recipe))db.m_recipes.Add(recipe);
        }
        internal static void Relink(Inventory inventory)
        {
            if(inventory==null)return;
            foreach(ItemDrop.ItemData item in inventory.GetAllItems())
            {
                string name=IsPipe(item)?PipeName:BlendIndex(item)>=0?Blend.All[BlendIndex(item)].Prefab:null;
                if(name!=null&&Prefabs.TryGetValue(name,out GameObject prefab))
                { item.m_shared=prefab.GetComponent<ItemDrop>().m_itemData.m_shared; item.m_dropPrefab=prefab; }
            }
        }
        internal static void Unregister()
        {
            if(ZNetScene.instance!=null)
            {
                var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(ZNetScene.instance);
                foreach(var pair in Prefabs)
                { ZNetScene.instance.m_prefabs.Remove(pair.Value); int hash=pair.Key.GetStableHashCode(); if(named.TryGetValue(hash,out GameObject current)&&current==pair.Value)named.Remove(hash); }
            }
            foreach(ObjectDB db in Resources.FindObjectsOfTypeAll<ObjectDB>())
            {
                bool changed=false;
                foreach(GameObject prefab in Prefabs.Values)changed|=db.m_items.Remove(prefab);
                foreach(Recipe recipe in Recipes.Values)db.m_recipes.Remove(recipe);
                foreach(PipeEffect effect in Effects)db.m_StatusEffects.Remove(effect);
                if(changed&&db==ObjectDB.instance)Registers.Invoke(db,null);
            }
            foreach(Recipe recipe in Recipes.Values)Object.Destroy(recipe);
            foreach(PipeEffect effect in Effects)if(effect!=null)Object.Destroy(effect);
            // Existing drops still reference their meshes and icons until the world unloads.
            Owned.Clear(); Prefabs.Clear(); Recipes.Clear(); Array.Clear(Effects,0,Effects.Length);
            if(_holder!=null)Object.Destroy(_holder); _holder=null;
        }
    }
    [HarmonyPatch(typeof(ObjectDB),"UpdateRegisters")]
    internal static class PipeRegisters { private static void Prefix(ObjectDB __instance)=>Items.Register(__instance,false); }
    [HarmonyPatch(typeof(ObjectDB),"Awake")]
    internal static class PipeDatabase { private static void Postfix(ObjectDB __instance)=>Items.Register(__instance); }
    [HarmonyPatch(typeof(ObjectDB),nameof(ObjectDB.CopyOtherDB))]
    internal static class PipeDatabaseCopy { private static void Postfix(ObjectDB __instance)=>Items.Register(__instance); }
    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class PipeScene { private static void Postfix()=>Items.Register(); }
    [HarmonyPatch(typeof(Player),"Awake")]
    internal static class PipePlayers { private static void Postfix(Player __instance){Plugin.Data(__instance)?.Set(Plugin.EndKey,0L);PipeLook.Attach(__instance);} }
}
