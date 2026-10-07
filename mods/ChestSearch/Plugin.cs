using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
namespace ChestSearch
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.chestsearch";
        public const string Name="ChestSearch";
        public const string Version="0.1.0";
        internal static Plugin Instance;
        internal ConfigEntry<float> Radius;
        private ConfigEntry<KeyboardShortcut> _key;
        private ConfigEntry<bool> _enabled;
        private Harmony _harmony;
        private readonly Window _window=new Window();
        private Player _player;
        private ZNet _session;
        private bool _openedInventory;
        private float _nextRefresh,_messageUntil;
        private int _openFrame;
        private string _message;
        private int _suppressFrame=-10;
        private sealed class Pending
        {
            internal Entry Row;
            internal Vector2i Target;
            internal string Fingerprint;
            internal Lease Lease;
        }
        private Pending _pending;
        private readonly Dictionary<ZDOID,float> _cancelled=new Dictionary<ZDOID,float>();
        internal static bool Open=>Instance!=null&&Instance._window.Visible;
        internal static bool Suppress=>Open&&(Instance._window.Dragging||Instance._pending!=null||Time.frameCount<=Instance._suppressFrame+1);
        internal static bool Blocking=>Open||Instance?.Pressed()==true;
        private bool Pressed()=>_enabled!=null&&_enabled.Value&&_key.Value.IsDown();
        private void Awake()
        {
            Instance=this;
            Radius=Config.Bind("Search","Radius",20f,new ConfigDescription("Search loaded storage containers within this many metres.",new AcceptableValueRange<float>(5,40)));
            _enabled=Config.Bind("Search","Enabled",true,"Enable the nearby chest search window.");
            _key=Config.Bind("Controls","Toggle",new KeyboardShortcut(KeyCode.F,KeyCode.LeftControl),"Open/close nearby chest search alongside your inventory.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("ChestSearch loaded. "+_key.Value+" opens nearby chest search.");
        }
        private void Update()
        {
            try{Tick();}catch(Exception e){Logger.LogError(e);Close(false);}
        }
        private void Tick()
        {
            Player player=Player.m_localPlayer;
            if(Open&&(player==null||player!=_player||_session!=ZNet.instance||!_enabled.Value||player.IsDead()||player.IsTeleporting()||(InventoryGui.instance!=null&&InventoryGui.instance.IsContainerOpen())||(!InventoryGui.IsVisible()&&Time.frameCount>_openFrame+2))){Close(false);return;}
            if(Pressed())
            {
                if(Open){Close(true);return;}
                if(player==null||player.IsDead()||player.IsTeleporting()||Console.IsVisible()||Menu.IsVisible()||Minimap.IsOpen()||
                    TextInput.IsVisible()||(Chat.instance!=null&&Chat.instance.HasFocus()))return;
                InventoryGui gui=InventoryGui.instance;if(gui==null)return;
                _player=player;_session=ZNet.instance;_openedInventory=!InventoryGui.IsVisible();
                if(gui.IsContainerOpen())AccessTools.Method(typeof(InventoryGui),"CloseContainer").Invoke(gui,null);
                gui.Show(null);
                _window.Show(gui,SearchChanged,Drop,CancelDrag);_openFrame=Time.frameCount;_nextRefresh=0;
            }
            if(!Open)return;
            if(Input.GetKeyDown(KeyCode.Escape)){Close(true);return;}
            _window.Tick();
            if(_pending!=null)Progress();
            if(Time.unscaledTime>=_nextRefresh)
            {
                _nextRefresh=Time.unscaledTime+0.5f;
                List<Entry> entries=Chests.Search(player,Radius.Value,_window.Query,out int count,out bool limited);
                _window.Rows(entries,count,Radius.Value,limited);
            }
            _window.Status(_pending!=null?"Opening source chest…":Time.unscaledTime<_messageUntil?_message:null);
            PruneCancelled();
        }
        private void PruneCancelled()
        {
            var expired=new List<ZDOID>();foreach(var pair in _cancelled)if(Time.unscaledTime>=pair.Value)expired.Add(pair.Key);
            foreach(ZDOID id in expired)_cancelled.Remove(id);
        }
        private void SearchChanged()=>_nextRefresh=0;
        private void CancelDrag()=>_suppressFrame=Time.frameCount;
        private void Drop(Entry row,Vector2i target)
        {
            _suppressFrame=Time.frameCount;PruneCancelled();
            if(_pending!=null)return;
            if(!Chests.Accessible(row.Chest,_player,Radius.Value)||Chests.Busy(row.Chest)) {Tell("That chest is unavailable or in use.");return;}
            ZNetView view=Chests.View(row.Chest);
            if(view.GetZDO().m_uid!=row.Id||_cancelled.ContainsKey(row.Id)){Tell("Waiting for the previous chest request to finish. Try again shortly.");return;}
            string fingerprint=Chests.Fingerprint(row.Preview);if(fingerprint==null)return;
            long owner=view.GetZDO().GetOwner();if(owner==0){Tell("That chest has no active owner yet.");return;}
            _pending=new Pending{Row=row,Target=target,Fingerprint=fingerprint,Lease=new Lease(owner,Time.unscaledTime)};
            view.InvokeRPC("RPC_RequestOpen",_player.GetPlayerID());
        }
        internal bool Response(Container chest,long sender,bool granted)
        {
            PruneCancelled();
            ZNetView view=Chests.View(chest);if(view==null||!view.IsValid())return true;
            ZDOID id=view.GetZDO().m_uid;
            if(_pending!=null&&_pending.Row.Id==id)
            {
                if(_pending.Lease.Reply(sender,granted,Time.unscaledTime)&&!granted){Tell("The chest owner denied access or the chest is in use.");CancelPending();}
                return false;
            }
            return !_cancelled.ContainsKey(id);
        }
        private void Progress()
        {
            Pending pending=_pending;
            if(pending.Lease.Expired(Time.unscaledTime)){Tell("No chest reply yet. Nothing was moved.");CancelPending();return;}
            Container chest=pending.Row.Chest;
            if(!Chests.Accessible(chest,_player,Radius.Value)||Chests.View(chest).GetZDO().m_uid!=pending.Row.Id){Tell("The chest is no longer available.");CancelPending();return;}
            if(!pending.Lease.Ready(Time.unscaledTime,Chests.View(chest).IsOwner(),Open,true))return;
            // Authorization granted through the game's own owner. Load BEFORE taking its in-use lock.
            if(Chests.Busy(chest)){Tell("Someone else is using that chest.");CancelPending();return;}
            Chests.Refresh(chest);
            ItemDrop.ItemData item=chest.GetInventory().GetItemAt(pending.Row.Position.x,pending.Row.Position.y);
            if(item==null||Chests.Fingerprint(item)!=pending.Fingerprint){Tell("That stack changed. Search again and select its updated stack.");CancelPending();return;}
            Inventory inventory=_player.GetInventory();Vector2i target=pending.Target;
            if(target.x<0||target.y<0||target.x>=inventory.GetWidth()||target.y>=inventory.GetHeight()){CancelPending();return;}
            ItemDrop.ItemData at=inventory.GetItemAt(target.x,target.y);
            int amount=Policy.Capacity(item.m_stack,pending.Row.Preview.m_stack,item.m_shared.m_maxStackSize,at?.m_stack??0,at!=null&&at.IsSameType(item),at==null);
            if(amount<=0){Tell("Drop onto an empty slot or a matching stack with room.");CancelPending();return;}
            pending.Lease.Finish();_pending=null; // no callback can execute this transfer twice
            bool locked=false;
            try
            {
                chest.SetInUse(true);locked=true;
                if(!Chests.View(chest).IsOwner()||!Chests.Accessible(chest,_player,Radius.Value))return;
                int before=item.m_stack;
                inventory.MoveItemToThis(chest.GetInventory(),item,amount,target.x,target.y);
                int moved=before-item.m_stack;
                Tell(moved>0?"Moved "+moved+" "+pending.Row.Name+" from "+pending.Row.Source+".":"That inventory slot cannot accept this stack.");
                _nextRefresh=0;
            }
            finally{if(locked&&chest!=null&&Chests.View(chest)?.IsOwner()==true)chest.SetInUse(false);}
        }
        internal void BlockTyping()=>_window.BlockTyping();
        private void Tell(string text){_message=text;_messageUntil=Time.unscaledTime+6;_nextRefresh=0;}
        private void CancelPending()
        {
            if(_pending==null)return;
            _pending.Lease.Finish();
            if(_cancelled.Count>=64)_cancelled.Clear();
            _cancelled[_pending.Row.Id]=Time.unscaledTime+15;_pending=null;
        }
        internal void Close(bool hideInventory)
        {
            bool wasOpen=Open;
            _window.Close();CancelPending();_player=null;_session=null;
            if(wasOpen&&hideInventory&&_openedInventory&&InventoryGui.instance!=null)InventoryGui.instance.Hide();
            _openedInventory=false;
        }
        private void OnDestroy()
        {Close(false);_window.Destroy();_harmony?.UnpatchSelf();_cancelled.Clear();if(Instance==this)Instance=null;}
    }
}
