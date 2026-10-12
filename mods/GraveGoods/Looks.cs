using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GraveGoods
{
    // How grave goods show: their names in their rarity's colour, their powers under the game's own lines, a tint behind their
    // slot, a glow where they lie, and a sound when a chest is opened on something rare.
    internal static class Looks
    {
        internal const string FrameName="GraveGoodsTint",GlowName="GraveGoodsGlow";
        internal static string BaseName(ItemDrop.ItemData item)=>Localization.instance!=null?Localization.instance.Localize(item.m_shared.m_name):item.m_shared.m_name;
        internal static string Title(ItemDrop.ItemData item,Goods g)=>$"<color={Policy.Color(g.Rarity)}>{Policy.Title(g,BaseName(item))}</color>";
        internal static Color Tint(Rarity r){ColorUtility.TryParseHtmlString(Policy.Color(r),out Color c);return c;}

        internal static void Stop()
        {
            foreach(InventoryElement e in Object.FindObjectsByType<InventoryElement>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                Transform f=e.transform.Find(FrameName);
                if(f!=null)Object.Destroy(f.gameObject);
            }
            foreach(ItemDrop d in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                Transform g=d.transform.Find(GlowName);
                if(g!=null)Object.Destroy(g.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetTooltip),new[]{typeof(ItemDrop.ItemData),typeof(int),typeof(bool),typeof(float),typeof(int),typeof(bool)})]
    internal static class Tooltip
    {
        private static void Postfix(ItemDrop.ItemData item,ref string __result)
        {
            Goods g=Effects.Of(item);
            if(g==null)return;
            string c=Policy.Color(g.Rarity);
            var sb=new StringBuilder();
            sb.Append($"<color={c}>{Policy.RarityNames[(int)g.Rarity]}</color> <color=#8a8070>grave goods · tier {g.Tier}</color>");
            if(g.Name!="")sb.Append($"\n<color=#8a8070>{Looks.BaseName(item)}</color>");
            if(g.Saga!="")sb.Append($"\n<i><color=#c8b48a>{g.Saga}</color></i>");
            foreach(Affix a in g.Affixes){string l=Policy.Line(a);if(l!="")sb.Append($"\n<color={(a.Value<0?"#e05050":c)}>• {l}</color>");}
            sb.Append("\n\n").Append(__result);
            __result=sb.ToString();
        }
    }
    [HarmonyPatch(typeof(InventoryGrid),"CreateItemTooltip")]
    internal static class TooltipTitle
    {
        private static void Postfix(ItemDrop.ItemData item,UITooltip tooltip)
        {
            Goods g=Effects.Of(item);
            if(g!=null&&tooltip!=null)tooltip.m_topic=Looks.Title(item,g);
        }
    }
    [HarmonyPatch(typeof(ItemDrop),nameof(ItemDrop.GetHoverText))]
    internal static class HoverName
    {
        private static void Postfix(ItemDrop __instance,ref string __result)
        {
            Goods g=Effects.Of(__instance.m_itemData);
            if(g==null||Localization.instance==null)return;
            string plain=Localization.instance.Localize(__instance.m_itemData.m_shared.m_name);
            int at=__result.IndexOf(plain,System.StringComparison.Ordinal);
            if(at>=0)__result=__result.Substring(0,at)+Looks.Title(__instance.m_itemData,g)+__result.Substring(at+plain.Length);
        }
    }

    // A tint behind each grave goods slot, in its rarity's colour.
    [HarmonyPatch(typeof(InventoryGrid),"UpdateGui")]
    internal static class SlotTint
    {
        private static readonly AccessTools.FieldRef<InventoryGrid,List<InventoryElement>> Elements=AccessTools.FieldRefAccess<InventoryGrid,List<InventoryElement>>("m_elements");
        private static readonly AccessTools.FieldRef<InventoryGrid,Inventory> Inv=AccessTools.FieldRefAccess<InventoryGrid,Inventory>("m_inventory");
        private static void Postfix(InventoryGrid __instance)
        {
            Inventory inv=Inv(__instance);
            List<InventoryElement> elements=Elements(__instance);
            if(inv==null||elements==null)return;
            foreach(InventoryElement e in elements)
            {
                if(e==null)continue;
                ItemDrop.ItemData item=Plugin.SlotTint.Value?inv.GetItemAt(e.Position.x,e.Position.y):null;
                Goods g=Effects.Of(item);
                Transform f=e.transform.Find(Looks.FrameName);
                if(g==null||g.Rarity==Rarity.Plain){if(f!=null&&f.gameObject.activeSelf)f.gameObject.SetActive(false);continue;}
                if(f==null)f=Make(e);
                if(!f.gameObject.activeSelf)f.gameObject.SetActive(true);
                Color c=Looks.Tint(g.Rarity);c.a=g.Rarity>=Rarity.Heirloom?0.5f:0.35f;
                f.GetComponent<Image>().color=c;
            }
        }
        private static Transform Make(InventoryElement e)
        {
            var go=new GameObject(Looks.FrameName,typeof(RectTransform),typeof(Image));
            var rt=(RectTransform)go.transform;
            rt.SetParent(e.transform,false);
            rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=new Vector2(2,2);rt.offsetMax=new Vector2(-2,-2);
            var img=go.GetComponent<Image>();
            img.raycastTarget=false;
            if(e.m_equiped!=null&&e.m_equiped.sprite!=null){img.sprite=e.m_equiped.sprite;img.type=e.m_equiped.type;}
            rt.SetSiblingIndex(e.m_icon!=null?e.m_icon.transform.GetSiblingIndex():0); // (behind the icon)
            return rt;
        }
    }

    // Where runed and rarer things lie, a small light in their colour.
    [HarmonyPatch(typeof(ItemDrop),"Start")]
    internal static class GroundGlow
    {
        private static void Postfix(ItemDrop __instance)
        {
            if(!Plugin.GroundGlow.Value||__instance.transform.Find(Looks.GlowName)!=null)return;
            Goods g=Effects.Of(__instance.m_itemData);
            if(g==null||g.Rarity<Rarity.Runed)return;
            var go=new GameObject(Looks.GlowName);
            go.transform.SetParent(__instance.transform,false);
            go.transform.localPosition=Vector3.up*0.4f;
            Light l=go.AddComponent<Light>();
            l.type=LightType.Point;l.color=Looks.Tint(g.Rarity);l.range=g.Rarity==Rarity.Saga?5:3.5f;l.intensity=g.Rarity==Rarity.Saga?2.2f:1.4f;l.shadows=LightShadows.None;
        }
    }

    // Opening a chest on something rare: the find is named, and a sound marks it.
    [HarmonyPatch(typeof(InventoryGui),nameof(InventoryGui.Show))]
    internal static class Found
    {
        private static readonly HashSet<string> Told=new HashSet<string>();
        private static void Postfix(Container container)
        {
            Player me=Player.m_localPlayer;
            if(me==null||container==null)return;
            ItemDrop.ItemData best=null;Goods top=null;
            foreach(ItemDrop.ItemData item in container.GetInventory().GetAllItems())
            {
                Goods g=Effects.Of(item);
                if(g==null||g.Rarity<Rarity.Fine||top!=null&&g.Rarity<=top.Rarity)continue;
                best=item;top=g;
            }
            if(top==null)return;
            ZNetView nv=container.GetComponentInParent<ZNetView>();
            string key=(nv!=null&&nv.IsValid()?nv.GetZDO().m_uid.ToString():container.GetInstanceID().ToString())+"/"+best.m_gridPos;
            if(!Told.Add(key))return;
            me.Message(top.Rarity>=Rarity.Heirloom?MessageHud.MessageType.Center:MessageHud.MessageType.TopLeft,$"Grave goods: {Looks.Title(best,top)}");
            Sound(top.Rarity>=Rarity.Heirloom?"sfx_offering":"sfx_coins_placed",me.transform.position+Vector3.up);
        }
        private static void Sound(string prefab,Vector3 at)
        {
            GameObject p=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(prefab):null;
            if(p!=null)Object.Instantiate(p,at,Quaternion.identity);
        }
    }
}
