using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Demolisher
{
    // Demolisher: a demolition stake in the hammer's build menu. Click the ground for each corner of an area, close it on the first
    // corner, press E: "Demolish N structures?". Yes removes them as the hammer would, top first, a few each frame.
    [BepInPlugin(Guid,Name,Version)]
    public sealed partial class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.demolisher";
        public const string Name="Demolisher";
        public const string Version="0.1.1";
        internal static Plugin Instance;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<int> _perFrame;
        private Harmony _harmony;
        private ZNet _session;
        private readonly List<Vector3> _corners=new List<Vector3>();
        private bool _closed;

        private void Awake()
        {
            Instance=this;
            _enabled=Config.Bind("General","Enabled",true,"Put the demolition stake in the hammer's build menu (Misc).");
            _perFrame=Config.Bind("General","PerFrame",8,new ConfigDescription("Structures removed each frame while demolishing.",new AcceptableValueRange<int>(1,50)));
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded: pick the demolition stake from the hammer's Misc tab.");
        }
        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            if(_asking && UnifiedPopup.IsVisible())UnifiedPopup.Pop();
            Untint();DestroyOutline();DestroyStake();
            if(Instance==this)Instance=null;
        }
        private void Update()
        {
            try{Tick();}
            catch(Exception e){Logger.LogError(e);ClearOutline();}
        }
        private void Tick()
        {
            EnsureStake();
            Player player=Player.m_localPlayer;
            if(_session!=ZNet.instance || player==null){_session=ZNet.instance;if(_corners.Count>0 || _queue.Count>0)ClearOutline();_queue.Clear();return;}
            RunQueue(player);
            bool holding=_enabled.Value && StakeSelected(player) && !player.IsDead();
            if(!holding){ShowOutline(false);Untint();return;}
            ShowOutline(true);
            if(InputFree(player) && _queue.Count==0)
            {
                if(Input.GetKeyDown(KeyCode.Backspace))
                {
                    if(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))ClearOutline();
                    else RemoveLastCorner(player);
                }
                else if(_corners.Count>=3 && ZInput.GetButtonDown("Use") && !HoversSomethingToUse(player))Ask(player);
            }
            UpdateLive(player);
            if(_closed && _queue.Count==0 && Time.unscaledTime>=_nextScan)Scan(player);
        }

        private static bool InputFree(Player player)=>!Menu.IsVisible() && !Console.IsVisible() && !TextInput.IsVisible() && !InventoryGui.IsVisible() &&
            !Minimap.IsOpen() && !Hud.IsPieceSelectionVisible() && !Hud.InRadial() && !UnifiedPopup.IsVisible() && (Chat.instance==null || !Chat.instance.HasFocus());
        // E still opens doors and chests: the outline only takes it when nothing usable is under the crosshair.
        private static bool HoversSomethingToUse(Player player)
        {
            GameObject hover=player.GetHoverObject();
            return hover!=null && hover.GetComponentInParent<Interactable>()!=null;
        }

        private static List<(double x,double z)> Flat(IEnumerable<Vector3> points)=>points.Select(p=>((double)p.x,(double)p.z)).ToList();
        private static Vector3 Ground(Vector3 p)
        {
            p.y=ZoneSystem.instance!=null?ZoneSystem.instance.GetGroundHeight(p):p.y;
            return p;
        }
        private static bool Aim(out Vector3 point)
        {
            point=default;
            Transform camera=GameCamera.instance?.transform;
            if(camera==null || !Physics.Raycast(camera.position,camera.forward,out RaycastHit hit,80,AimLayers,QueryTriggerInteraction.Ignore))return false;
            point=Ground(hit.point);return true;
        }
        private static int AimLayers=>LayerMask.GetMask("terrain","piece","piece_nonsolid","Default","static_solid","Default_small");

        // Where a click would put the next corner: the first corner when it closes the outline.
        private bool NextCorner(out Vector3 point,out bool closes,out string problem)
        {
            closes=false;problem=null;
            if(!Aim(out point))return false;
            var flat=Flat(_corners);
            if(Area.Closes(flat,point.x,point.z)){closes=true;point=_corners[0];problem=Area.Problem(flat,point.x,point.z,true);return true;}
            problem=Area.Problem(flat,point.x,point.z,false);
            return true;
        }

        // A click with the stake (from the build input, in place of building it).
        private void Click(Player player)
        {
            if(_queue.Count>0){player.Message(MessageHud.MessageType.Center,"Still demolishing…");return;}
            if(_closed){player.Message(MessageHud.MessageType.Center,"The outline is closed: E to demolish, Backspace to reopen it.");return;}
            if(!NextCorner(out Vector3 point,out bool closes,out string problem))return;
            if(problem!=null){player.Message(MessageHud.MessageType.Center,problem);return;}
            if(closes){Close(player);return;}
            _corners.Add(point);RedrawOutline();
            player.Message(MessageHud.MessageType.Center,_corners.Count<3?$"Corner {_corners.Count}":$"Corner {_corners.Count}: click the first corner to close, or press E");
        }
        private bool Close(Player player)
        {
            if(!_closed)
            {
                var flat=Flat(_corners);
                string problem=Area.Problem(flat,flat[0].x,flat[0].z,true);
                if(problem!=null){player.Message(MessageHud.MessageType.Center,problem);return false;}
                _closed=true;RedrawOutline();
            }
            Scan(player);
            player.Message(MessageHud.MessageType.Center,_doomed.Count==1?"1 structure inside: E to demolish":$"{_doomed.Count} structures inside: E to demolish");
            return true;
        }
        private void RemoveLastCorner(Player player)
        {
            if(_closed){_closed=false;Untint();}
            else if(_corners.Count>0)_corners.RemoveAt(_corners.Count-1);
            else return;
            RedrawOutline();
            player.Message(MessageHud.MessageType.Center,_corners.Count==0?"Outline cleared":$"{_corners.Count} corners");
        }
        private void ClearOutline()
        {
            _corners.Clear();_closed=false;_doomed.Clear();_skipped.Clear();Untint();RedrawOutline();
        }
    }
}
