using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid="com.bobisme.gary";
        public const string Name="Gary";
        public const string Version="0.2.5";
        internal static Plugin Instance;
        internal ConfigEntry<float> Health,GiftSeconds,GuideRange;
        internal ConfigEntry<bool> Gifts,Guiding,Reactions,Warnings,Campfires,Building,ShowMapMarker,Boats;
        private ConfigEntry<KeyboardShortcut> _call,_wait;
        private Harmony _harmony;
        private ZRoutedRpc _rpc;
        private object _commandHandler,_replyHandler;
        private float _nextScan,_pendingUntil;
        private readonly Dictionary<long,float> _requests=new Dictionary<long,float>();
        private const string Command="bob_gary_command_v1",Reply="bob_gary_reply_v1";

        private void Awake()
        {
            Instance=this;
            _call=Config.Bind("Controls","CallGary",new KeyboardShortcut(KeyCode.F3),"Summon your Gary, or recall the same Gary beside you. Does not interrupt his healing retreat.");
            _wait=Config.Bind("Controls","WaitHere",new KeyboardShortcut(KeyCode.F3,KeyCode.LeftShift),"Tell Gary to wait here. Use CallGary to resume following.");
            // Ctrl+F3 is hardcoded in Hud.Update to hide the entire UI root, including menus.
            // Bind reads existing config values: changing only the defaults would leave everyone on the broken keys.
            bool migrated=false;
            foreach(ConfigEntry<KeyboardShortcut> key in new[]{_call,_wait})
                if(key.Value.MainKey==KeyCode.F3 && key.Value.Modifiers.Contains(KeyCode.LeftControl))
                {key.Value=key==_call?new KeyboardShortcut(KeyCode.F3):new KeyboardShortcut(KeyCode.F3,KeyCode.LeftShift);migrated=true;}
            if(migrated)
            {
                Config.Save();
                if(Hud.instance!=null)Hud.instance.m_userHidden=false;
                Logger.LogInfo("Moved Gary's Ctrl+F3 bindings to F3 / Shift+F3 and restored hidden UI.");
            }
            Health=Config.Bind("Companion","Health",150f,new ConfigDescription("Gary's injury buffer. He retreats at 20% and returns at 90%; damage never kills him.",new AcceptableValueRange<float>(40,500)));
            Boats=Config.Bind("Companion","RideBoats",true,"Gary boards a nearby boat with you, rides on a clear deck spot, and follows onto dry ground when you get off. He waits aboard if you fall into the water and rests aboard if injured.");
            ShowMapMarker=Config.Bind("Companion","MapMarker",true,"Show your Gary as a purple moving pin on the minimap and full map. Unloaded known positions are labeled last seen.");
            Gifts=Config.Bind("Forest","FoodGifts",true,"Gather real wild berries/mushrooms and loose feathers into a six-item stash and occasionally toss one near your feet, outside combat.");
            GiftSeconds=Config.Bind("Forest","FoodInterval",240f,new ConfigDescription("Average seconds between forest gifts (randomized 0.75–1.25 times this).",new AcceptableValueRange<float>(60,1800)));
            Guiding=Config.Bind("Forest","DungeonGuiding",true,"Notice nearby loaded crypt/cave entrances and skip dungeons confirmed fully looted for your player/world. Unknown or unfinished interiors remain eligible.");
            GuideRange=Config.Bind("Forest","NoticeRange",90f,new ConfigDescription("Distance at which Gary notices a loaded dungeon entrance.",new AcceptableValueRange<float>(20,120)));
            Reactions=Config.Bind("Personality","Reactions",true,"Happy chirps and short native dances for petting, reunions, victories, and relaxing.");
            Warnings=Config.Bind("Personality","DangerWarnings",true,"Occasional chirp and glance at a nearby visible hostile creature; no map markers or proactive attacks.");
            Campfires=Config.Bind("Personality","CampfireBuddy",true,"Relax beside a burning fire when you settle down nearby.");
            Building=Config.Bind("Personality","BuildingBuddy",true,"Watch from the side while you build, away from your placement ghost.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"Gary {Version} loaded. {_call.Value} calls Gary; {_wait.Value} tells him to wait.");
        }
        private void Update()
        {
            if(_rpc!=ZRoutedRpc.instance){Unregister();Register();}
            if(Time.unscaledTime>=_nextScan)
            {
                _nextScan=Time.unscaledTime+1;
                Companion.Scan();PlayerCollision.Scan();
            }
            if(_pendingUntil>0 && Time.unscaledTime>_pendingUntil)
            {_pendingUntil=0;Tell("No reply yet. Gary must be installed on the host and participating players.");}
            DungeonLoot.Tick();MapMarker.Tick();
            Player p=Player.m_localPlayer;
            if(p==null||p.IsDead()||p.IsTeleporting()||Busy()||_rpc==null)return;
            bool wait=_wait.Value.IsDown();
            if(!wait&&!_call.Value.IsDown())return;
            if(_pendingUntil>0)return;
            if(p.InInterior()){Tell("I'll wait outside. Call me when you're back outside the dungeon.");return;}
            Vector3 spot=p.transform.position;
            if(!wait && !BoatRide.CallSpot(p,out spot))
            {Tell("I need clear ground or a free spot on your boat. Move away from walls or the mast.");return;}
            _pendingUntil=Time.unscaledTime+8;
            _rpc.InvokeRoutedRPC(Command,wait,spot);
        }
        private void LateUpdate()=>BoatRide.LateUpdate();
        private static bool Busy() => Console.IsVisible()||TextInput.IsVisible()||Menu.IsVisible()||InventoryGui.IsVisible()||Minimap.IsOpen()||
            StoreGui.IsVisible()||Hud.IsPieceSelectionVisible()||(Chat.instance!=null&&Chat.instance.HasFocus());
        private void Register()
        {
            _rpc=ZRoutedRpc.instance;if(_rpc==null)return;
            _rpc.Register<bool,Vector3>(Command,OnCommand);_rpc.Register<string>(Reply,OnReply);Petting.Register(_rpc);
            IDictionary table=AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) as IDictionary;
            _commandHandler=table?[Command.GetStableHashCode()];_replyHandler=table?[Reply.GetStableHashCode()];
        }
        private void Unregister()
        {
            Petting.Unregister();
            if(_rpc!=null)
            {
                IDictionary table=AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) as IDictionary;
                foreach(var entry in new[]{new KeyValuePair<string,object>(Command,_commandHandler),new KeyValuePair<string,object>(Reply,_replyHandler)})
                    if(table!=null&&ReferenceEquals(table[entry.Key.GetStableHashCode()],entry.Value))table.Remove(entry.Key.GetStableHashCode());
            }
            _rpc=null;_commandHandler=_replyHandler=null;_requests.Clear();_pendingUntil=0;
        }
        private void OnCommand(long sender,bool wait,Vector3 spot)
        {
            if(ZNet.instance==null||!ZNet.instance.IsServer()||ZDOMan.instance==null)return;
            ZDOID id=sender==ZNet.GetUID()?ZNet.instance.LocalPlayerCharacterID:ZNet.instance.GetPeer(sender)?.m_characterID??ZDOID.None;
            ZDO player=ZDOMan.instance.GetZDO(id);
            if(player==null||player.GetLong(ZDOVars.s_playerID,0)==0)return;
            if(_requests.TryGetValue(sender,out float last)&&Time.unscaledTime-last<2)return;
            _requests[sender]=Time.unscaledTime;
            if(_requests.Count>64)_requests.Clear();
            string message;
            try {message=Companion.Command(player,wait,spot);}
            catch(Exception e){Logger.LogError("Gary command failed: "+e);message="Couldn't call Gary. See BepInEx's log.";}
            _rpc.InvokeRoutedRPC(sender,Reply,message);
        }
        private void OnReply(long sender,string message)
        {
            // Only accept the server's response to our own outstanding command.
            if(_pendingUntil==0||ZNet.instance==null)return;
            bool server=ZNet.instance.IsServer()?sender==ZNet.GetUID():ZNet.instance.GetPeer(sender)?.m_server==true;
            if(!server)return;
            _pendingUntil=0;Tell(message);
        }
        internal static void Tell(string message) => Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft,"Gary: "+message);
        internal void Error(Exception error) => Logger.LogError(error);
        private void OnDestroy()
        {Unregister();_harmony?.UnpatchSelf();MapMarker.Clear();BoatRide.Clear();PlayerCollision.Clear();DungeonLoot.Clear();Companion.Clear();if(Instance==this)Instance=null;}
    }
}
