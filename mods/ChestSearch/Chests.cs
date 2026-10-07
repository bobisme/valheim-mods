using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace ChestSearch
{
    internal sealed class Entry
    {
        internal Container Chest;
        internal ZDOID Id;
        internal Vector2i Position;
        internal ItemDrop.ItemData Preview;
        internal string Name,Source;
    }
    internal static class Chests
    {
        private static readonly AccessTools.FieldRef<Container,ZNetView> ViewField=AccessTools.FieldRefAccess<Container,ZNetView>("m_nview");
        private static readonly System.Reflection.MethodInfo Check=AccessTools.Method(typeof(Container),"CheckAccess"),Load=AccessTools.Method(typeof(Container),"Load");
        internal static ZNetView View(Container c)=>c!=null?ViewField(c):null;
        internal static bool Storage(Container c) => c!=null&&c.isActiveAndEnabled&&c.GetInventory()!=null&&
            c.GetComponentInParent<Character>()==null&&c.GetComponentInParent<TombStone>()==null&&
            !c.gameObject.name.StartsWith("piece_recycler",StringComparison.Ordinal);
        internal static bool Accessible(Container c,Player p,float radius)
        {
            ZNetView view=View(c);
            return Storage(c)&&p!=null&&!p.IsDead()&&!p.IsTeleporting()&&view!=null&&view.IsValid()&&
                Policy.InRange(Vector3.Distance(c.transform.position,p.transform.position),radius)&&
                (!c.m_checkGuardStone||PrivateArea.CheckAccess(c.transform.position,0,false))&&
                (c.m_privacy!=Container.PrivacySetting.Private||c.GetComponent<Piece>()!=null)&&
                (bool)Check.Invoke(c,new object[]{p.GetPlayerID()});
        }
        internal static bool Busy(Container c)
        {
            ZNetView view=View(c);
            return c.IsInUse()||(c.m_wagon!=null&&c.m_wagon.InUse())||view==null||!view.IsValid()||view.GetZDO().GetInt(ZDOVars.s_inUse,0)==1;
        }
        internal static void Refresh(Container c)=>Load.Invoke(c,null);
        internal static List<Entry> Search(Player p,float radius,string query,out int count,out bool limited)
        {
            var chests=new List<Container>();
            foreach(Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                if(Accessible(c,p,radius)&&!Busy(c))chests.Add(c);
            chests.Sort((a,b)=>(a.transform.position-p.transform.position).sqrMagnitude.CompareTo((b.transform.position-p.transform.position).sqrMagnitude));
            limited=chests.Count>Policy.MaxChests;count=Math.Min(chests.Count,Policy.MaxChests);
            var rows=new List<Entry>();
            foreach(Container c in chests.Take(Policy.MaxChests))
            {
                // Existing game inventories are sufficient for searching. Refresh owner state before any actual transfer.
                foreach(ItemDrop.ItemData item in c.GetInventory().GetAllItems())
                {
                    if(!Bounded(item)||item.m_shared.m_questItem)continue;
                    string name=Localization.instance.Localize(item.m_shared.m_name);
                    if(!Policy.Matches(name,query))continue;
                    if(rows.Count==Policy.MaxResults){limited=true;break;}
                    rows.Add(new Entry{Chest=c,Id=View(c).GetZDO().m_uid,Position=item.m_gridPos,Preview=item.Clone(),Name=name,
                        Source=Localization.instance.Localize(c.m_name)+" · "+Vector3.Distance(c.transform.position,p.transform.position).ToString("0.0")+" m"});
                }
                if(rows.Count==Policy.MaxResults)break;
            }
            return rows;
        }
        private static bool Bounded(ItemDrop.ItemData item)
        {
            if(item==null||item.m_shared==null||item.m_dropPrefab==null||item.m_stack<=0||item.m_stack>65535||
                item.m_quality<1||item.m_quality>65535||item.m_worldLevel<0||item.m_worldLevel>255||
                item.m_gridPos.x<0||item.m_gridPos.x>255||item.m_gridPos.y<0||item.m_gridPos.y>255||float.IsNaN(item.m_durability)||float.IsInfinity(item.m_durability)||
                item.m_customData==null||item.m_customData.Count>64||item.m_crafterName==null||item.m_crafterName.Length>1024)return false;
            int chars=0;
            foreach(var pair in item.m_customData)
            {if(pair.Key==null||pair.Value==null||pair.Key.Length>4096||pair.Value.Length>4096)return false;chars+=pair.Key.Length+pair.Value.Length;if(chars>16384)return false;}
            return true;
        }
        internal static string Fingerprint(ItemDrop.ItemData item)
        {
            if(!Bounded(item))return null;
            ItemDrop.ItemData copy=item.Clone();
            copy.m_customData=new Dictionary<string,string>();
            foreach(var pair in item.m_customData.OrderBy(p=>p.Key,StringComparer.Ordinal))copy.m_customData.Add(pair.Key,pair.Value);
            var package=new ZPackage();copy.Save(package);
            return item.m_dropPrefab.name+"|"+Convert.ToBase64String(package.GetArray());
        }
    }
}
