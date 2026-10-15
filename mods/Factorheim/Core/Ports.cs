using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // Where items go in and come out, one at a time: what an inserter reaches into, what a belt feeds at its end, what a drill drops its ore
    // into. An item is its prefab name ("CopperOre", "FH_IronGear"); quality, durability and the crafter's name don't survive automation.
    //
    // Ownership rule (multiplayer, no duplication): a transfer only happens on the game that owns BOTH ends' ZDOs. Ready says "I own it and
    // it's alive"; whoever moves an item checks Ready on the source and the target first, then Take, then Put. Pieces in one base nearly
    // always share an owner (the zone's); at a boundary the line just waits until ownership settles.
    internal interface IPort
    {
        bool Ready{get;}
        bool CanPut(string prefab);
        bool Put(string prefab);                    // one item; false if it didn't fit (nothing changed)
        string Peek(Func<string,bool> wants=null);  // the item Take would give (wanted, or any), or null
        bool Take(string prefab);                   // removes one; false if there is none
    }

    // Our pieces that hold items (machines, belts, the lab, the silo) are hosts: the port may depend on where you reach in (a belt's
    // spot along its length, a machine's input side). Return null for "nothing here".
    internal interface IPortHost
    {
        IPort Port(Vector3 at);
    }

    internal static class Ports
    {
        private static readonly int PieceMask=LayerMask.GetMask("piece","piece_nonsolid","Default_small","item","Default");
        private static readonly Collider[] Hits=new Collider[24];
        private static readonly HashSet<Component> Seen=new HashSet<Component>();

        // The port at a point in the world, nearest first, not counting `except` (the asker itself). Ours, the game's chests, its smelters,
        // kilns, furnaces and fires (fuel and ore in; what they make falls on the ground), and items lying on the ground (taken from only).
        internal static IPort At(Vector3 point,float radius=0.45f,Component except=null)
        {
            int n=Physics.OverlapSphereNonAlloc(point,radius,Hits,PieceMask,QueryTriggerInteraction.Collide);
            IPort best=null;float bestDistance=float.MaxValue;
            var seen=Seen;seen.Clear();
            for(int i=0;i<n;i++)
            {
                Collider c=Hits[i];
                if(c==null)continue;
                IPort port=null;Component owner=null;
                if(c.GetComponentInParent<IPortHost>() is IPortHost host&&host is Component hc){owner=hc;if(owner!=except&&!seen.Contains(owner))port=host.Port(point);}
                else if(c.GetComponentInParent<Container>() is Container chest){owner=chest;if(!seen.Contains(owner))port=ChestPort.Of(chest);}
                else if(c.GetComponentInParent<Smelter>() is Smelter smelter){owner=smelter;if(!seen.Contains(owner))port=SmelterPort.Of(smelter);}
                else if(c.GetComponentInParent<Fireplace>() is Fireplace fire){owner=fire;if(!seen.Contains(owner))port=FirePort.Of(fire);}
                else if(c.GetComponentInParent<ItemDrop>() is ItemDrop drop){owner=drop;if(!seen.Contains(owner))port=GroundPort.Of(drop);}
                if(owner!=null)seen.Add(owner);
                if(port==null||owner==except)continue;
                float d=(c.ClosestPoint(point)-point).sqrMagnitude;
                if(d<bestDistance){best=port;bestDistance=d;}
            }
            return best;
        }

        // Moves one item from one port to another if both are ready and it fits: what an inserter does. Returns what moved, or null.
        internal static string Move(IPort from,IPort to,Func<string,bool> wants=null)
        {
            if(from==null||to==null||!from.Ready||!to.Ready)return null;
            string item=from.Peek(x=>(wants==null||wants(x))&&to.CanPut(x));
            if(item==null||!from.Take(item))return null;
            if(to.Put(item))return item;
            from.Put(item);   // (it didn't fit after all: back where it came from)
            return null;
        }

        internal static GameObject Prefab(string item)=>ObjectDB.instance!=null?ObjectDB.instance.GetItemPrefab(item):null;
        internal static string NameOf(ItemDrop.ItemData item)=>item?.m_dropPrefab!=null?item.m_dropPrefab.name:null;
        // What players read: "Copper ore".
        internal static string Title(string item)
        {
            ItemDrop d=Prefab(item)?.GetComponent<ItemDrop>();
            string n=d!=null?d.m_itemData.m_shared.m_name:item;
            return Localization.instance!=null?Localization.instance.Localize(n):n;
        }
        internal static Sprite IconOf(string item)
        {
            ItemDrop d=Prefab(item)?.GetComponent<ItemDrop>();
            return d!=null&&d.m_itemData.m_shared.m_icons!=null&&d.m_itemData.m_shared.m_icons.Length>0?d.m_itemData.m_shared.m_icons[0]:null;
        }
        internal static ZNetView View(Component c)=>c!=null?c.GetComponentInParent<ZNetView>():null;
        internal static bool Owned(Component c){ZNetView v=View(c);return v!=null&&v.IsValid()&&v.IsOwner();}
    }

    // ---- the game's own things ------------------------------------------------------------------------------------------------

    // A chest someone built (not a grave, cart, ship or creature; not one a player has open).
    internal sealed class ChestPort:IPort
    {
        private readonly Container _c;
        private ChestPort(Container c){_c=c;}
        internal static IPort Of(Container c)=>ContainerAccess.IsStorage(c)?new ChestPort(c):null;
        public bool Ready=>_c!=null&&Ports.Owned(_c)&&!ContainerAccess.InUse(_c);
        public bool CanPut(string prefab){GameObject p=Ports.Prefab(prefab);return p!=null&&_c.GetInventory().CanAddItem(p,1);}
        public bool Put(string prefab){GameObject p=Ports.Prefab(prefab);return p!=null&&_c.GetInventory().AddItem(p,1);}
        public string Peek(Func<string,bool> wants=null)
        {
            foreach(ItemDrop.ItemData i in _c.GetInventory().GetAllItems())
            {
                string n=Ports.NameOf(i);
                if(n!=null&&(wants==null||wants(n)))return n;
            }
            return null;
        }
        public bool Take(string prefab)
        {
            ItemDrop.ItemData i=_c.GetInventory().GetAllItems().FirstOrDefault(x=>Ports.NameOf(x)==prefab);
            if(i==null)return false;
            _c.GetInventory().RemoveItem(i,1);
            return true;
        }
    }

    // Smelters, blast furnaces, kilns, the windmill, spinning wheel and eitr refinery: ore and fuel in (their output drops on the ground
    // at their output point, where a belt or an inserter picks it up). Nothing to take from them directly.
    internal sealed class SmelterPort:IPort
    {
        private readonly Smelter _s;private readonly ZNetView _v;
        private SmelterPort(Smelter s){_s=s;_v=Ports.View(s);}
        internal static IPort Of(Smelter s)=>s!=null?new SmelterPort(s):null;
        public bool Ready=>_s!=null&&_v!=null&&_v.IsValid()&&_v.IsOwner();
        private bool IsFuel(string prefab)=>_s.m_fuelItem!=null&&_s.m_fuelItem.gameObject.name==prefab;
        private bool IsOre(string prefab)=>_s.m_conversion.Any(c=>c.m_from!=null&&c.m_from.gameObject.name==prefab);
        public bool CanPut(string prefab)
        {
            ZDO z=_v.GetZDO();
            if(IsFuel(prefab))return z.GetFloat(ZDOVars.s_fuel,0f)<_s.m_maxFuel-1;
            return IsOre(prefab)&&z.GetInt(ZDOVars.s_queued,0)<_s.m_maxOre;
        }
        public bool Put(string prefab)
        {
            if(!CanPut(prefab))return false;
            // (owner: the game's own RPCs run straight away on our side and do the bookkeeping, effects and sounds)
            if(IsFuel(prefab))_v.InvokeRPC("RPC_AddFuel");
            else _v.InvokeRPC("RPC_AddOre",prefab);
            return true;
        }
        public string Peek(Func<string,bool> wants=null)=>null;
        public bool Take(string prefab)=>false;
    }

    // Campfires, hearths, braziers, torches: fuel in.
    internal sealed class FirePort:IPort
    {
        private readonly Fireplace _f;private readonly ZNetView _v;
        private FirePort(Fireplace f){_f=f;_v=Ports.View(f);}
        internal static IPort Of(Fireplace f)=>f!=null&&f.m_fuelItem!=null&&!f.m_infiniteFuel?new FirePort(f):null;
        public bool Ready=>_f!=null&&_v!=null&&_v.IsValid()&&_v.IsOwner();
        public bool CanPut(string prefab)=>_f.m_fuelItem.gameObject.name==prefab&&_v.GetZDO().GetFloat(ZDOVars.s_fuel,0f)<_f.m_maxFuel-1;
        public bool Put(string prefab){if(!CanPut(prefab))return false;_v.InvokeRPC("RPC_AddFuel");return true;}
        public string Peek(Func<string,bool> wants=null)=>null;
        public bool Take(string prefab)=>false;
    }

    // Something lying on the ground: taken from only (a stack shrinks one at a time; the last one goes).
    internal sealed class GroundPort:IPort
    {
        private static readonly System.Reflection.MethodInfo SaveDrop=AccessTools.Method(typeof(ItemDrop),"Save");
        private readonly ItemDrop _d;private readonly ZNetView _v;
        private GroundPort(ItemDrop d){_d=d;_v=Ports.View(d);}
        internal static IPort Of(ItemDrop d)=>d!=null&&d.GetComponent<Rigidbody>()!=null&&d.m_itemData!=null&&d.m_itemData.m_stack>0?new GroundPort(d):null;
        public bool Ready=>_d!=null&&_v!=null&&_v.IsValid()&&_v.IsOwner();
        public bool CanPut(string prefab)=>false;
        public bool Put(string prefab)=>false;
        public string Peek(Func<string,bool> wants=null){string n=_d.gameObject.name.Replace("(Clone)","").Trim();return wants==null||wants(n)?n:null;}
        public bool Take(string prefab)
        {
            if(Peek(x=>x==prefab)==null)return false;
            if(_d.m_itemData.m_stack>1){_d.m_itemData.m_stack--;SaveDrop.Invoke(_d,null);}
            else ZNetScene.instance.Destroy(_d.gameObject);
            return true;
        }
    }
}
