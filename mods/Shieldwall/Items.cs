using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // The mod's items (the five staves and the warshard), their recipes at the Warstone, and the Warstone in the hammer's menu.
    internal static class Items
    {
        private static readonly Dictionary<string,GameObject> Prefabs=new Dictionary<string,GameObject>();
        private static readonly Dictionary<string,Recipe> Recipes=new Dictionary<string,Recipe>();
        private static readonly MethodInfo UpdateRegisters=AccessTools.Method(typeof(ObjectDB),"UpdateRegisters");
        internal static readonly Dictionary<StaveKind,(Color tint,Color glow)> Colours=new Dictionary<StaveKind,(Color,Color)>
        {
            [StaveKind.Ember]=(new Color(1f,0.72f,0.6f),new Color(0.9f,0.25f,0.05f)),
            [StaveKind.Frost]=(new Color(0.75f,0.9f,1.1f),new Color(0.2f,0.55f,0.9f)),
            [StaveKind.Thunder]=(new Color(0.85f,0.8f,1.1f),new Color(0.55f,0.35f,1f)),
            [StaveKind.Blast]=(new Color(0.7f,0.6f,0.55f),new Color(0.8f,0.4f,0.1f)),
            [StaveKind.Hearth]=(new Color(0.95f,0.85f,0.65f),new Color(0.35f,0.25f,0.08f)),
        };

        internal static bool IsStave(ItemDrop.ItemData item)=>item?.m_shared!=null&&Policy.Staves.Any(s=>s.Name==item.m_shared.m_name);
        internal static Stave StaveOf(ItemDrop.ItemData item)=>item?.m_shared==null?null:Policy.Staves.FirstOrDefault(s=>s.Name==item.m_shared.m_name);
        // Any staff that throws something can be planted: ours at full power, others borrowed.
        internal static bool Plantable(ItemDrop.ItemData item)=>item?.m_shared!=null&&(IsStave(item)||
            item.m_shared.m_itemType==ItemDrop.ItemData.ItemType.TwoHandedWeapon&&(item.m_shared.m_skillType==Skills.SkillType.ElementalMagic||item.m_shared.m_skillType==Skills.SkillType.BloodMagic)&&
            item.m_shared.m_attack?.m_attackProjectile?.GetComponent<Projectile>()!=null);

        private static GameObject Ensure(string prefab)
        {
            if(Prefabs.TryGetValue(prefab,out GameObject made)&&made!=null)return made;
            Stave stave=Policy.StaveNamed(prefab);
            GameObject source=Assets.Find(stave!=null?stave.Source:"Crystal");
            ItemDrop sourceDrop=source!=null?source.GetComponent<ItemDrop>():null;
            if(sourceDrop==null)return null;
            GameObject go=Object.Instantiate(source,Assets.Holder);
            go.name=prefab;
            var shared=(ItemDrop.ItemData.SharedData)Assets.CloneShared.Invoke(sourceDrop.m_itemData.m_shared,null);
            if(stave!=null)
            {
                var (tint,glow)=Colours[stave.Kind];
                Assets.Tint(go,tint,Color.black); // coloured, not glowing: the light at its head does the glowing
                shared.m_name=stave.Name;
                shared.m_description=stave.Description+"\n\nFar too heavy and thick to swing: it is set in a stave socket by a Warstone (Use on the socket, or drag it there from your hotbar). "+
                    "Strengthen it in its socket with warshards.";
                // A relic to set, not a weapon to wield.
                shared.m_itemType=ItemDrop.ItemData.ItemType.Misc;shared.m_weight=25;shared.m_maxStackSize=1;shared.m_autoStack=false;
                shared.m_maxQuality=Policy.MaxQuality;shared.m_useDurability=false;shared.m_teleportable=true;shared.m_value=0;
                shared.m_damages=new HitData.DamageTypes();shared.m_damagesPerLevel=new HitData.DamageTypes();
                Transform attach=go.transform.Find("attach");
                if(attach!=null)attach.localScale=attach.localScale*1.7f; // big on the ground too
                Sprite icon=Assets.Icon(go,prefab,Quaternion.Euler(0,0,-35));
                if(icon!=null)shared.m_icons=new[]{icon};
            }
            else
            {
                Assets.Tint(go,new Color(1f,0.25f,0.2f),new Color(0.9f,0.08f,0.03f));
                shared.m_name="Warshard";
                shared.m_description="A splinter of the horde's war-luck, taken from those who broke against your Warstone. Used to craft and strengthen staves at the stone.";
                shared.m_itemType=ItemDrop.ItemData.ItemType.Material;shared.m_maxStackSize=50;shared.m_weight=0.3f;shared.m_teleportable=true;shared.m_value=0;
                Sprite icon=Assets.Icon(go,prefab,Quaternion.Euler(0,20,38)); // the long crystal on the diagonal fills the slot
                if(icon!=null)shared.m_icons=new[]{icon};
            }
            ItemDrop drop=go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared=shared;drop.m_itemData.m_stack=1;drop.m_itemData.m_quality=1;drop.m_itemData.m_dropPrefab=go;
            Prefabs[prefab]=go;
            return go;
        }
        internal static IEnumerable<string> All=>Policy.Staves.Select(s=>s.Prefab).Append(Policy.ShardPrefab);

        // ---- registering ----
        internal static void BeforeRegisters(ObjectDB db)
        {
            db.m_items.RemoveAll(i=>i==null||All.Contains(i.name)&&(!Prefabs.TryGetValue(i.name,out GameObject mine)||i!=mine));
            db.m_recipes.RemoveAll(r=>r==null||r.name.StartsWith("Recipe_BobStave")&&!Recipes.Values.Contains(r));
            if(db.GetItemPrefab("Crystal")==null&&db.m_items.All(i=>i==null||i.name!="Crystal"))return; // the empty database the main menu makes first
            foreach(string name in All){GameObject prefab=Ensure(name);if(prefab!=null&&!db.m_items.Contains(prefab))db.m_items.Add(prefab);}
            AddRecipes(db);
            AddPiece(db);
        }
        private static void AddRecipes(ObjectDB db)
        {
            CraftingStation station=Stone.Station;
            if(db==null||station==null)return;
            foreach(Stave stave in Policy.Staves)
            {
                GameObject prefab=Ensure(stave.Prefab);
                if(prefab==null)continue;
                string name="Recipe_"+stave.Prefab;
                if(!Recipes.TryGetValue(name,out Recipe recipe)||recipe==null)
                {
                    var needs=stave.Cost.Select(c=>(drop:(c.prefab==Policy.ShardPrefab?Ensure(c.prefab):Find(db,c.prefab))?.GetComponent<ItemDrop>(),c.amount,c.perLevel)).ToList();
                    if(needs.Any(n=>n.drop==null))continue;
                    recipe=ScriptableObject.CreateInstance<Recipe>();
                    recipe.name=name;recipe.hideFlags=HideFlags.HideAndDontSave;
                    recipe.m_item=prefab.GetComponent<ItemDrop>();recipe.m_amount=1;recipe.m_enabled=true;
                    recipe.m_craftingStation=station;recipe.m_repairStation=station;recipe.m_minStationLevel=stave.StationLevel;
                    recipe.m_resources=needs.Select(n=>new Piece.Requirement{m_resItem=n.drop,m_amount=n.amount,m_amountPerLevel=n.perLevel,m_recover=true}).ToArray();
                    Recipes[name]=recipe;
                }
                if(!db.m_recipes.Contains(recipe))db.m_recipes.Add(recipe);
            }
        }
        private static GameObject Find(ObjectDB db,string name)=>db.GetItemPrefab(name)??db.m_items.FirstOrDefault(i=>i!=null&&i.name==name);
        private static void AddPiece(ObjectDB db)
        {
            GameObject stone=Stone.Prefab;
            PieceTable table=Find(db,"Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if(table==null||stone==null)return;
            Pieces.Ensure();
            var ours=new List<GameObject>{stone};ours.AddRange(Pieces.All);
            var names=new HashSet<string>(ours.Select(o=>o.name));
            table.m_pieces.RemoveAll(p=>p==null||names.Contains(p.name)&&!ours.Contains(p));
            foreach(GameObject piece in ours)if(!table.m_pieces.Contains(piece))table.m_pieces.Add(piece);
        }
        internal static void RegisterScene(ZNetScene scene)
        {
            Stone.Ensure();
            foreach(string name in All){GameObject prefab=Ensure(name);if(prefab!=null)Assets.Register(scene,prefab);}
            Assets.Register(scene,Stone.Prefab);
            Pieces.Ensure();
            foreach(GameObject piece in Pieces.All)Assets.Register(scene,piece);
            Assets.Register(scene,LegacyPlanted.Prefab);
            if(ObjectDB.instance!=null){AddRecipes(ObjectDB.instance);AddPiece(ObjectDB.instance);}
        }
        // Hot reload while in a world: rebuild the database's lookups with our things in place, and point carried items at the new prefabs.
        internal static void RegisterLoaded()
        {
            if(ZNetScene.instance!=null)RegisterScene(ZNetScene.instance);
            if(ObjectDB.instance!=null)UpdateRegisters.Invoke(ObjectDB.instance,null);
            // Our items carried or lying about still point at the unloaded copy's prefab (or at nothing): point them at the new one.
            foreach(ItemDrop drop in AccessTools.StaticFieldRefAccess<List<ItemDrop>>(typeof(ItemDrop),"s_instances").ToList())if(drop!=null)Relink(drop.m_itemData);
            Inventory inventory=Player.m_localPlayer?.GetInventory();
            if(inventory==null)return;
            foreach(ItemDrop.ItemData item in inventory.GetAllItems())Relink(item);
            AccessTools.Method(typeof(Player),"UpdateKnownRecipesList")?.Invoke(Player.m_localPlayer,null);
        }
        private static void Relink(ItemDrop.ItemData item)
        {
            if(item?.m_shared==null)return;
            string prefab=item.m_shared.m_name=="Warshard"?Policy.ShardPrefab:Policy.Staves.FirstOrDefault(st=>st.Name==item.m_shared.m_name)?.Prefab;
            if(prefab==null||!Prefabs.TryGetValue(prefab,out GameObject mine)||mine==null||item.m_dropPrefab==mine)return;
            item.m_shared=mine.GetComponent<ItemDrop>().m_itemData.m_shared;item.m_dropPrefab=mine;
        }
        internal static void Unregister()
        {
            if(ObjectDB.instance!=null)
            {
                ObjectDB db=ObjectDB.instance;
                bool had=db.m_items.RemoveAll(i=>i!=null&&Prefabs.Values.Contains(i))>0;
                db.m_recipes.RemoveAll(r=>r!=null&&Recipes.Values.Contains(r));
                Find(db,"Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces?.m_pieces.RemoveAll(p=>p==null||p==Stone.Prefab||Pieces.All.Contains(p));
                if(had)UpdateRegisters.Invoke(db,null);
            }
            foreach(Recipe r in Recipes.Values)if(r!=null)Object.Destroy(r);
            Recipes.Clear();Prefabs.Clear();
        }
        internal static GameObject Get(string prefab)=>Prefabs.TryGetValue(prefab,out GameObject go)?go:null;
    }

    [HarmonyPatch(typeof(ObjectDB),"UpdateRegisters")]
    internal static class RegisterItems
    {
        private static void Prefix(ObjectDB __instance)=>Items.BeforeRegisters(__instance);
    }
}
