using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Wayfinder
{
    // Sails of Homespun's wool (its Api, version 3, found by name: no hard reference). Used on a ship's helm, a sail is rigged on her:
    // the game's sail gives way to plain wool in the sail's colour (the karve's white sail, dyed), seen by everyone; the sail she had,
    // if it was one of these, comes back to the bag. Shipyard's boats rig their own at the slipway.
    internal static class Rigging
    {
        internal const string Key="bob_wayfinder_sail",Rpc="bob_wayfinder_rig",Own="-"; // (her sail's colour: "" undyed wool; none or "-": the game's own)
        private static float _looked=-100;
        private static Func<string,bool> _isSail;private static Func<string,string> _colourOf;private static Func<string,(float r,float g,float b)?> _rgb;private static string[] _sails;
        internal static bool On{get{Bind();return _isSail!=null;}}
        private static void Bind()
        {
            if(Time.realtimeSinceStartup-_looked<10)return;
            _looked=Time.realtimeSinceStartup;
            _isSail=null;_colourOf=null;_rgb=null;_sails=null;
            Type api=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Homespun.Api",false)).LastOrDefault(t=>t!=null);
            if(api==null||!(api.GetField("Version",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) is int v)||v<3)return;
            try
            {
                _isSail=(Func<string,bool>)Delegate.CreateDelegate(typeof(Func<string,bool>),api.GetMethod("IsSail",new[]{typeof(string)}));
                _colourOf=(Func<string,string>)Delegate.CreateDelegate(typeof(Func<string,string>),api.GetMethod("ColourOf",new[]{typeof(string)}));
                _rgb=(Func<string,(float r,float g,float b)?>)Delegate.CreateDelegate(typeof(Func<string,(float r,float g,float b)?>),api.GetMethod("Rgb",new[]{typeof(string)}));
                _sails=(string[])api.GetProperty("Sails").GetValue(null);
            }
            catch(Exception e){Plugin.Log("Homespun's sails: "+e.Message);_isSail=null;}
        }
        internal static bool IsSail(ItemDrop.ItemData item)=>On&&item?.m_dropPrefab!=null&&_isSail(item.m_dropPrefab.name);
        private static string ColourOf(string prefab)=>_colourOf(prefab)??""; // ("" undyed)
        private static string SailOf(string colour)=>_sails?.FirstOrDefault(s=>ColourOf(s)==colour);
        private static bool Ours(Ship ship)=>ship!=null&&!Utils.GetPrefabName(ship.gameObject).StartsWith("Shipyard_");
        private static SkinnedMeshRenderer SailOn(Ship ship)=>ship.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r=>r.sharedMaterial!=null&&r.sharedMaterial.name.StartsWith("sail"));

        // On the owner's game: the sail's colour kept on the ship.
        internal static void Register(ZNetView view)
        {
            view.Unregister(Rpc);
            view.Register<string>(Rpc,(sender,colour)=>{if(view.IsOwner())view.GetZDO().Set(Key,colour??"");});
            if(view.GetComponent<Ship>() is Ship ship&&Ours(ship)&&ship.GetComponent<Dyed>()==null)ship.gameObject.AddComponent<Dyed>();
        }

        // A sail used on the helm: rigged; the one she had back to the bag.
        [HarmonyPatch(typeof(ShipControlls),nameof(ShipControlls.UseItem))]
        private static class UseOnHelm
        {
            private static bool Prefix(ShipControlls __instance,Humanoid user,ItemDrop.ItemData item,ref bool __result)
            {
                if(!(user is Player p)||p!=Player.m_localPlayer||!IsSail(item))return true;
                Ship ship=__instance.m_ship;ZNetView view=ship!=null?ship.GetComponent<ZNetView>():null;
                if(view==null||!view.IsValid())return true;
                __result=true;
                if(!Ours(ship)){p.Message(MessageHud.MessageType.Center,"Her sail is the shipwright's: rig her on the slipway");return false;}
                string colour=ColourOf(item.m_dropPrefab.name),had=view.GetZDO().GetString(Key,Own);
                // (the same sail again: taken down, and she has the game's own back)
                bool down=had==colour;
                if(!down)p.GetInventory().RemoveItem(item,1);
                if(had!=Own&&SailOf(had) is string old&&ObjectDB.instance.GetItemPrefab(old) is GameObject back&&!p.GetInventory().AddItem(back,1))
                    ItemDrop.DropItem(back.GetComponent<ItemDrop>().m_itemData.Clone(),1,p.transform.position+Vector3.up,Quaternion.identity);
                view.InvokeRPC(Rpc,down?Own:colour);
                p.Message(MessageHud.MessageType.Center,down?$"The {Localization.instance.Localize(item.m_shared.m_name).ToLower()} taken down: {Ships.NameOf(ship)} has her own again"
                    :$"Rigged: {Ships.NameOf(ship)} sails under the {Localization.instance.Localize(item.m_shared.m_name).ToLower()}");
                return false;
            }
        }

        // Each ship: her sail painted as her ZDO says (looked at every few seconds; a material per colour, shared).
        private static readonly Dictionary<string,Material> Mats=new Dictionary<string,Material>();
        private static Material Plain(string colour)
        {
            if(Mats.TryGetValue(colour,out Material m)&&m!=null)return m;
            Material white=ZNetScene.instance?.GetPrefab("Karve")?.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(x=>x!=null&&x.name.StartsWith("sail"));
            if(white==null)return null;
            var c=colour==""?(0.86f,0.82f,0.72f):_rgb?.Invoke(colour)??(1f,1f,1f);
            m=new Material(white){name="Wayfinder sail "+(colour==""?"undyed":colour)};
            m.color=new Color(c.Item1,c.Item2,c.Item3,1);
            Mats[colour]=m;return m;
        }
        internal sealed class Dyed:MonoBehaviour
        {
            private ZNetView _view;private SkinnedMeshRenderer _sail;private Material _own;private string _shown;private float _next;
            private void Awake(){_view=GetComponent<ZNetView>();}
            private void Update()
            {
                if(Time.time<_next||_view==null||!_view.IsValid())return;
                _next=Time.time+2f;
                if(_sail==null){_sail=SailOn(GetComponent<Ship>());if(_sail==null)return;_own=_sail.sharedMaterial;}
                string colour=On?_view.GetZDO().GetString(Key,Own):Own;
                if(colour==_shown)return;
                _shown=colour;
                _sail.sharedMaterial=colour!=Own&&Plain(colour) is Material m?m:_own;
            }
            internal void Undo(){if(_sail!=null&&_own!=null)_sail.sharedMaterial=_own;}
        }

        internal static void Stop()
        {
            foreach(Dyed d in Object.FindObjectsByType<Dyed>(FindObjectsSortMode.None)){d.Undo();Object.Destroy(d);}
            foreach(Material m in Mats.Values)if(m!=null)Object.Destroy(m);
            Mats.Clear();
        }
    }
}
