using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // An item: a copy of a game item (so hands, animations, skill and slot are the game's), with its own model, words, stats and recipe.
    internal sealed class ItemDef
    {
        internal string Prefab,Name,Description;
        internal string Base;                     // the game item it is copied from: "SwordIron", "CrossbowArbalest", "BeltStrength", "HelmetTrollLeather"...
        internal string Cost="Iron:5";            // "Item:amount, Item:amount" (a setting: Costs section)
        internal string Station="forge";          // the crafting station's prefab and level (a setting too)
        internal int Level=1;
        internal int Amount=1;                    // how many one hand craft makes
        // KeepLook: keep the copied item's own model (and colliders), tinted by Tint if set, instead of building one with Model.
        internal bool KeepLook;internal Color? Tint;
        internal int Tier;                        // research tier that enables its hand recipe (Cost "": made by machines only, no hand recipe)
        internal float Weight=2f;
        internal int Stack=1;
        // The model is built under a child of this name: "attach" (held in the hand, or the helmet), "attach_Spine2" (worn on the back) ...
        // (VisEquipment hangs an item's attach_<bone> children on that bone of every player wearing it: everyone sees it, no networking.)
        internal string Attach="attach";
        internal Vector3 AttachEuler,AttachPosition;
        // How it sits in the hand: the game parents the attach child to the hand joint with no turn of its own, then applies an
        // "equipoffset" child's position and rotation (so AttachEuler only turns it on the ground and in the icon).
        internal Vector3? HandEuler,HandPosition;
        internal Action<Transform> Model;         // builds the model under the attach child (Look.Builder helps)
        internal Action<ItemDrop.ItemData.SharedData> Stats;   // damage, durability, block, item type: anything on the shared data
        internal Quaternion? IconLook;            // the icon camera's turn (null: automatic)
        // What it does. Primary/Secondary: the attack buttons with it in hand (called every physics step while held, as the game asks;
        // return true to say "attacking" so the game doesn't also swing). Use: used from the inventory or hotbar (consumables, deployables;
        // return true when handled). Null: the game's own behaviour.
        internal Func<Player,ItemDrop.ItemData,bool> Primary,Secondary,Use;

        internal GameObject Built;
        internal Recipe Recipe;
        internal ConfigEntry<string> CCost,CStation;
        internal ItemDrop.ItemData.SharedData Shared=>Built!=null?Built.GetComponent<ItemDrop>().m_itemData.m_shared:null;
        internal bool Is(ItemDrop.ItemData item)=>item!=null&&item.m_shared!=null&&(item.m_shared.m_name==Name||item.m_dropPrefab!=null&&item.m_dropPrefab.name==Prefab);
    }

    internal static class Things
    {
        private static GameObject _holder;
        private static readonly MethodInfo CloneShared=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly FieldInfo NamedField=AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs");
        private static bool _registering;

        internal static IEnumerable<ItemDef> All=>Features.All.SelectMany(f=>f.Items);
        internal static ItemDef Find(string prefabOrName)=>All.FirstOrDefault(d=>string.Equals(d.Prefab,prefabOrName,StringComparison.OrdinalIgnoreCase)||string.Equals(d.Name,prefabOrName,StringComparison.OrdinalIgnoreCase));
        internal static ItemDef Of(ItemDrop.ItemData item)=>item==null?null:All.FirstOrDefault(d=>d.Is(item));

        // A switched-off holder for every prefab this mod makes, so the copies never wake up as real objects.
        internal static Transform Holder
        {
            get
            {
                if(_holder==null){_holder=new GameObject("FactorheimPrefabs");_holder.SetActive(false);Object.DontDestroyOnLoad(_holder);}
                return _holder.transform;
            }
        }
        internal static GameObject Game(string name)
        {
            GameObject found=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(name):null;
            if(found==null&&ObjectDB.instance!=null)found=ObjectDB.instance.GetItemPrefab(name);
            return found;
        }

        // The item the player holds in the right hand (or both), and which of ours it is.
        internal static ItemDef Held(Player p,out ItemDrop.ItemData item)
        {
            item=p!=null?p.GetCurrentWeapon():null;
            return Of(item);
        }
        internal static bool Wears(Humanoid h,ItemDef def)=>h!=null&&h.GetInventory()!=null&&h.GetInventory().GetEquippedItems().Any(def.Is);

        internal static void BindCosts(ConfigFile config)
        {
            foreach(ItemDef d in All)
            {
                d.CCost=config.Bind("9 - Costs",d.Prefab,d.Cost,$"What the {d.Name} takes to make: \"Item:amount, Item:amount\" (game prefab names). Needs a restart.");
                d.CStation=config.Bind("9 - Costs",d.Prefab+"Station",d.Station+":"+d.Level,$"Where the {d.Name} is made, and the station's level: \"forge:1\". Needs a restart.");
            }
        }

        // Builds everything once and puts it (back) where the game looks: ZNetScene, ObjectDB, recipes. ZNetScene and ObjectDB wake in either
        // order and a world load swaps ObjectDB's lists, so this is called from all of them and must be safe to repeat.
        internal static void Register()
        {
            if(_registering)return;
            ZNetScene scene=ZNetScene.instance;
            if(scene==null||scene.GetPrefab("Wood")==null)return;
            _registering=true;
            try
            {
                Look.Init(scene);
                ObjectDB db=ObjectDB.instance;
                var items=new List<GameObject>();
                foreach(ItemDef d in All)
                {
                    try{if(Build(d,scene)!=null)items.Add(d.Built);}
                    catch(Exception e){Plugin.Warn($"Could not make the {d.Name}: {e}");}
                }
                ItemReload.Replace(scene,db!=null&&db.GetItemPrefab("Wood")!=null?db:null,items);
                Plugin.Safe("pieces",()=>Pieces.Register(scene));
                foreach(Feature f in Features.All)Plugin.Safe(f.Name+" prefabs",()=>f.Register(scene));
                if(db!=null&&db.GetItemPrefab("Wood")!=null)AddRecipes(scene,db);
            }
            finally{_registering=false;}
        }

        private static GameObject Build(ItemDef d,ZNetScene scene)
        {
            if(d.Built!=null)return d.Built;
            GameObject source=scene.GetPrefab(d.Base);
            ItemDrop sourceDrop=source!=null?source.GetComponent<ItemDrop>():null;
            if(sourceDrop==null){Plugin.Warn($"{d.Name}: the game has no {d.Base} to copy");return null;}
            GameObject go=Object.Instantiate(source,Holder);
            go.name=d.Prefab;
            if(d.KeepLook)
            {
                if(d.Tint is Color tint)
                    foreach(Renderer r in go.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials=r.sharedMaterials.Select(m=>m==null?null:Look.Own(new Material(m){color=m.color*tint})).ToArray();
            }
            else
            {
                // Out with the game's model (every child: meshes, attach points, cloth), the colliders and the LOD group.
                foreach(LODGroup lod in go.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(lod);
                for(int i=go.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
                foreach(Collider c in go.GetComponents<Collider>())Object.DestroyImmediate(c);
                foreach(ParticleSystem ps in go.GetComponents<ParticleSystem>())Object.DestroyImmediate(ps);   // (before its renderer: it depends on it)
                foreach(Light l in go.GetComponents<Light>())Object.DestroyImmediate(l);
                foreach(Renderer r in go.GetComponents<Renderer>())Object.DestroyImmediate(r);
                foreach(MeshFilter m in go.GetComponents<MeshFilter>())Object.DestroyImmediate(m);

                var attach=new GameObject(d.Attach){layer=go.layer};
                attach.transform.SetParent(go.transform,false);
                attach.transform.localPosition=d.AttachPosition;
                attach.transform.localRotation=Quaternion.Euler(d.AttachEuler);
                if(d.HandEuler.HasValue||d.HandPosition.HasValue)
                {
                    var grip=new GameObject("equipoffset"){layer=go.layer};
                    grip.transform.SetParent(go.transform,false);
                    grip.transform.localPosition=d.HandPosition??Vector3.zero;grip.transform.localRotation=Quaternion.Euler(d.HandEuler??Vector3.zero);
                }
                try{d.Model?.Invoke(attach.transform);}
                catch(Exception e){Plugin.Warn($"Could not build the {d.Name}'s model: {e}");}
                foreach(Transform t in attach.GetComponentsInChildren<Transform>(true))t.gameObject.layer=go.layer;
                Bounds b=Look.Bounds(attach.transform,go.transform);
                var box=go.AddComponent<BoxCollider>();   // (what you pick it up by on the ground)
                box.center=b.center;box.size=Vector3.Max(b.size,Vector3.one*0.1f);
            }

            ItemDrop drop=go.GetComponent<ItemDrop>();
            var s=(ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared,null);   // (its own copy: never write into the game item's)
            s.m_name=d.Name;s.m_description=d.Description;
            s.m_weight=d.Weight;s.m_maxStackSize=d.Stack;s.m_value=0;   // (0: the trader never buys it)
            s.m_maxQuality=1;s.m_variants=0;s.m_dlc="";s.m_questItem=false;s.m_teleportable=true;
            s.m_damages=CopyDamage(s.m_damages);s.m_damagesPerLevel=new HitData.DamageTypes();
            s.m_attack=s.m_attack!=null?s.m_attack.Clone():null;s.m_secondaryAttack=s.m_secondaryAttack!=null?s.m_secondaryAttack.Clone():null;
            s.m_setName="";s.m_setSize=0;s.m_setStatusEffect=null;s.m_equipStatusEffect=null;
            drop.m_itemData.m_shared=s;
            drop.m_itemData.m_dropPrefab=go;   // (without it the item is saved with no name and lost)
            drop.m_itemData.m_stack=1;drop.m_itemData.m_quality=1;drop.m_itemData.m_durability=s.m_maxDurability;
            d.Stats?.Invoke(s);
            d.Built=go;
            Sprite icon=Icon.MakeItem(go,d.Attach,d.IconLook);
            if(icon!=null)s.m_icons=new[]{icon};
            return go;
        }
        private static HitData.DamageTypes CopyDamage(HitData.DamageTypes d)=>d.Clone();

        // A networked prefab of a feature's own (a drone, an effect others must see): put in ZNetScene by name, over any older copy's.
        internal static void AddPrefab(ZNetScene scene,GameObject prefab)
        {
            if(scene==null||prefab==null)return;
            scene.m_prefabs.RemoveAll(p=>p==null||p!=prefab&&p.name==prefab.name);
            if(!scene.m_prefabs.Contains(prefab))scene.m_prefabs.Add(prefab);
            ((Dictionary<int,GameObject>)NamedField.GetValue(scene))[prefab.name.GetStableHashCode()]=prefab;
        }

        private static void AddRecipes(ZNetScene scene,ObjectDB db)
        {
            foreach(ItemDef d in All)
            {
                db.m_recipes.RemoveAll(r=>r==null||r.name=="Recipe_"+d.Prefab&&r!=d.Recipe);
                if(d.Built==null)continue;
                if(d.Recipe==null&&string.IsNullOrWhiteSpace(d.CCost?.Value??d.Cost))continue;
                if(d.Recipe==null)
                {
                    string[] where=(d.CStation?.Value??d.Station).Split(':');
                    CraftingStation station=scene.GetPrefab(where[0].Trim())?.GetComponent<CraftingStation>();
                    int level=where.Length>1&&int.TryParse(where[1].Trim(),out int l)?l:d.Level;
                    var needs=new List<Piece.Requirement>();
                    foreach(string part in (d.CCost?.Value??d.Cost).Split(','))
                    {
                        string[] kv=part.Split(':');
                        if(kv[0].Trim()=="")continue;
                        ItemDrop res=db.GetItemPrefab(kv[0].Trim())?.GetComponent<ItemDrop>();
                        int amount=kv.Length>1&&int.TryParse(kv[1].Trim(),out int a)?a:1;
                        if(res==null){Plugin.Warn($"{d.Name}: the game has no {kv[0].Trim()}; left out of its recipe");continue;}
                        needs.Add(new Piece.Requirement{m_resItem=res,m_amount=amount,m_recover=true});
                    }
                    d.Recipe=ScriptableObject.CreateInstance<Recipe>();
                    d.Recipe.name="Recipe_"+d.Prefab;d.Recipe.hideFlags=HideFlags.HideAndDontSave;
                    d.Recipe.m_item=d.Built.GetComponent<ItemDrop>();d.Recipe.m_amount=Mathf.Max(1,d.Amount);d.Recipe.m_enabled=Tech.Unlocked(d.Tier);
                    d.Recipe.m_craftingStation=station;d.Recipe.m_minStationLevel=Mathf.Max(1,level);
                    d.Recipe.m_resources=needs.ToArray();
                }
                d.Recipe.m_enabled=Tech.Unlocked(d.Tier);
                if(!db.m_recipes.Contains(d.Recipe))db.m_recipes.Add(d.Recipe);
            }
        }

        // Research changed: hand recipes of the newly unlocked tier switch on.
        internal static void Retier(){foreach(ItemDef d in All)if(d.Recipe!=null)d.Recipe.m_enabled=Tech.Unlocked(d.Tier);}

        // Unloading: recipes out; the items stay registered inside a world (ItemReload: the next copy replaces them by name).
        internal static void Unload()
        {
            foreach(ObjectDB db in new[]{ObjectDB.instance})
                if(db!=null)foreach(ItemDef d in All)if(d.Recipe!=null)db.m_recipes.Remove(d.Recipe);
            foreach(ItemDef d in All)
            {
                if(d.Recipe!=null)Object.Destroy(d.Recipe);
                d.Recipe=null;d.Built=null;
            }
            if(!ItemReload.KeepOnUnload&&_holder!=null)Object.Destroy(_holder);
            _holder=null;
            Look.Clear();
            Meshes.Clear();
        }

        // After a hot reload: items in bags, chests and on the ground point at this copy's prefabs.
        internal static void Repoint()=>ItemReload.Repoint(name=>All.FirstOrDefault(d=>d.Prefab==name)?.Built);
    }

    // Our items' attack buttons and use go to their feature instead of the game.
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]
    internal static class AttackInput
    {
        private static bool Prefix(Humanoid __instance,bool secondaryAttack,ref bool __result)
        {
            if(!(__instance is Player p)||p!=Player.m_localPlayer)return true;
            ItemDef d=Things.Held(p,out ItemDrop.ItemData item);
            var handler=d==null?null:secondaryAttack?d.Secondary:d.Primary;
            if(handler==null)return true;
            __result=handler(p,item);
            return false;
        }
    }
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.UseItem))]
    internal static class UseInput
    {
        private static bool Prefix(Humanoid __instance,ItemDrop.ItemData item)
        {
            if(!(__instance is Player p)||p!=Player.m_localPlayer)return true;
            ItemDef d=Things.Of(item);
            if(d?.Use==null)return true;
            return !d.Use(p,item);
        }
    }
}
