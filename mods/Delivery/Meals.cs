using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Delivery
{
    internal sealed class Meal
    {
        internal string Prefab,Name;
        internal GameObject Item;
        internal float Health,Stamina,Eitr;
        internal int Price;
        internal Note[] Tune;
        internal ItemDrop.ItemData Data=>Item.GetComponent<ItemDrop>().m_itemData;
        internal string Stats
        {
            get
            {
                var parts=new List<string>();
                if(Health>0)parts.Add($"{Health:0} health");
                if(Stamina>0)parts.Add($"{Stamina:0} stamina");
                if(Eitr>0)parts.Add($"{Eitr:0} eitr");
                return string.Join(" · ",parts);
            }
        }
    }

    // What can be ordered: cooked food. Food made in a cauldron or oven, or cooked on a cooking station (the raw things, berries
    // and honey aren't meals). Each with its price and its call; you can order what you know how to make, or have had in hand.
    internal static class Meals
    {
        private static readonly string[] Kitchens={"piece_cauldron","piece_oven"};
        private static List<Meal> _all;
        private static ObjectDB _for;
        private static int _count;

        internal static List<Meal> All
        {
            get
            {
                ObjectDB db=ObjectDB.instance;
                if(db==null)return new List<Meal>();
                if(_all!=null&&_for==db&&_count==db.m_recipes.Count)return _all;
                _for=db;_count=db.m_recipes.Count;
                return _all=Build(db);
            }
        }
        internal static void Forget(){_all=null;_for=null;}
        internal static Meal Find(string prefab)=>All.FirstOrDefault(m=>m.Prefab==prefab);

        private static List<Meal> Build(ObjectDB db)
        {
            var items=new Dictionary<string,GameObject>();
            foreach(Recipe r in db.m_recipes)
            {
                if(r==null||r.m_item==null||r.m_craftingStation==null||!r.m_enabled)continue;
                if(!Kitchens.Contains(Utils.GetPrefabName(r.m_craftingStation.gameObject)))continue;
                if(Food(r.m_item))items[r.m_item.gameObject.name]=r.m_item.gameObject;
            }
            if(ZNetScene.instance!=null)
                foreach(GameObject p in ZNetScene.instance.m_prefabs)
                {
                    CookingStation cs=p!=null?p.GetComponent<CookingStation>():null;
                    if(cs==null)continue;
                    foreach(CookingStation.ItemConversion c in cs.m_conversion)
                        if(c?.m_to!=null&&Food(c.m_to))items[c.m_to.gameObject.name]=c.m_to.gameObject;
                }
            Dictionary<string,Note[]> tunes=Policy.Tunes(items.Keys);
            var meals=new List<Meal>();
            foreach(var kv in items)
            {
                ItemDrop.ItemData.SharedData s=kv.Value.GetComponent<ItemDrop>().m_itemData.m_shared;
                meals.Add(new Meal{Prefab=kv.Key,Item=kv.Value,Name=Localization.instance!=null?Localization.instance.Localize(s.m_name):s.m_name,
                    Health=s.m_food,Stamina=s.m_foodStamina,Eitr=s.m_foodEitr,Price=Policy.Price(s.m_food,s.m_foodStamina,s.m_foodEitr),Tune=tunes[kv.Key]});
            }
            Plugin.Log($"{meals.Count} meals can be ordered.");
            return meals.OrderBy(m=>m.Price).ThenBy(m=>m.Name).ToList();
        }
        private static bool Food(ItemDrop d)
        {
            ItemDrop.ItemData.SharedData s=d.m_itemData.m_shared;
            return s.m_itemType==ItemDrop.ItemData.ItemType.Consumable&&s.m_food+s.m_foodStamina+s.m_foodEitr>0;
        }

        // What this player may order: what they know how to make, or have had in hand (or everything, if the setting says so).
        internal static List<Meal> For(Player p)
        {
            if(p==null)return new List<Meal>();
            if(!Plugin.OnlyKnown.Value)return All;
            return All.Where(m=>p.IsRecipeKnown(m.Data.m_shared.m_name)||p.IsKnownMaterial(m.Data.m_shared.m_name)).ToList();
        }
    }
}
