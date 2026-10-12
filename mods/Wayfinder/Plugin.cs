using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// Claude Tools may reload it in a world: items and pieces stay registered across a reload and standing ones get the new code.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload","world")]

namespace Wayfinder
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.wayfinder";
        public const string Name="Wayfinder";
        public const string Version="0.1.8";
        internal static Plugin Instance;
        internal static ConfigEntry<float> ArriveRadius,MastheadSight,RavenRange;
        internal static ConfigEntry<bool> ShowOnMap,ShowHud,ShipNames,ShipPins,ShipLanterns,ShipVanes,MastheadClimb,ShowLandmarks;
        internal static ConfigEntry<int> FleetSize;
        internal static ConfigEntry<KeyCode> PlotKey;
        private Harmony _harmony;
        private GUIStyle _hint;

        private void Awake()
        {
            Instance=this;
            PlotKey=Config.Bind("Course","PlotKey",KeyCode.LeftControl,"Hold this on the big map: click to lay a waypoint (on a pin, it takes the pin's name; on a leg, it goes into that leg), right-click to lift one. With it held: Z undoes, R reverses the course for the way home, Backspace clears it. (Either Ctrl key works when this is a Ctrl key.)");
            ArriveRadius=Config.Bind("Course","ArriveRadius",40f,new ConfigDescription("How near a waypoint counts as reached, in metres.",new AcceptableValueRange<float>(10,150)));
            ShowOnMap=Config.Bind("Course","ShowOnMap",true,"Draw the course on the minimap and the big map.");
            ShowHud=Config.Bind("Display","ShowHud",true,"The compass band (with a sun-compass) and the navigator's notes under the minimap.");
            ShipNames=Config.Bind("Ships","Names",true,"Ships have names (Shift+E at the helm to give one).");
            ShipPins=Config.Bind("Ships","MapPins",true,"Pin the ships you have sailed in on your map. Taking a pin off the map forgets the ship.");
            FleetSize=Config.Bind("Ships","FleetSize",8,new ConfigDescription("How many ships you keep track of.",new AcceptableValueRange<int>(1,20)));
            ShipLanterns=Config.Bind("Ships","Lanterns",true,"A lantern hangs on every mast, lit from dusk till dawn.");
            ShipVanes=Config.Bind("Ships","Vanes",true,"A gilded vane at every masthead, swinging to the wind.");
            MastheadClimb=Config.Bind("Ships","Masthead",true,"Shift+E at the mast climbs to the masthead.");
            MastheadSight=Config.Bind("Ships","MastheadSight",3f,new ConfigDescription("How much wider the map fills in round you from the tallest lookout's seat (a drakkar's). Lower masts give less, as the horizon draws in.",new AcceptableValueRange<float>(1,5)));
            RavenRange=Config.Bind("Raven","Range",2000f,new ConfigDescription("How far a raven looks for land, in metres.",new AcceptableValueRange<float>(500,4000)));
            ShowLandmarks=Config.Bind("Map","Landmarks",true,"Beacons and named sea-marks you have seen stay on your map.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Items.RegisterLoaded();
            if(ZNetScene.instance!=null){Pieces.Register(ZNetScene.instance);Pieces.Refresh();Fittings.Refresh();RefreshTables();}
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        // The world's id, for keeping things per world; null while no world is loaded (ZNet without one throws).
        internal static string World()
        {
            if(ZNet.instance==null||ZNet.World==null)return null;
            try{return ZNet.instance.GetWorldUID().ToString();}catch{return null;}
        }

        internal static bool Plotting()
        {
            if(Minimap.instance==null||Minimap.instance.m_mode!=Minimap.MapMode.Large||Minimap.InTextInput())return false;
            KeyCode k=PlotKey.Value;
            if(k==KeyCode.LeftControl||k==KeyCode.RightControl)return Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
            return Input.GetKey(k);
        }
        private void Update()
        {
            Tools.Tick();
            Course.Tick();
            Ships.Tick();
            Sounding.Tick();
            Raven.Tick();
            Landmarks.Tick();
            if(Plotting())
            {
                if(Input.GetKeyDown(KeyCode.Z))Course.UndoLast();
                if(Input.GetKeyDown(KeyCode.R))Course.Reverse();
                if(Input.GetKeyDown(KeyCode.Backspace))Course.Clear();
            }
        }
        private void LateUpdate()
        {
            Fittings.Tick();
            MapDraw.Draw();
            NavHud.Tick();
        }
        private void OnGUI()
        {
            if(Event.current.type!=EventType.Repaint||Minimap.instance==null||Minimap.instance.m_mode!=Minimap.MapMode.Large||Player.m_localPlayer==null)return;
            if(_hint==null)_hint=new GUIStyle(GUI.skin.label){fontSize=Mathf.Max(13,Screen.height/75),alignment=TextAnchor.MiddleCenter,richText=true};
            string key=PlotKey.Value==KeyCode.LeftControl||PlotKey.Value==KeyCode.RightControl?"Ctrl":PlotKey.Value.ToString();
            string course=Course.Points.Count>0?$"<color=#ffd27a>Course: {Course.Points.Count} waypoints, {Policy.Distance(Policy.Length(Course.Flat,0))}</color>   ":"";
            string text=course+$"<color=#e8dcc0>{key}+click lay a waypoint · {key}+right-click lift one · {key}+Z undo · {key}+R reverse · {key}+Backspace clear</color>";
            var r=new Rect(0,Screen.height-Screen.height/22f-28,Screen.width,28);
            GUI.color=new Color(0,0,0,0.8f);GUI.Label(new Rect(r.x+1,r.y+1,r.width,r.height),System.Text.RegularExpressions.Regex.Replace(text,"<.*?>",""),_hint);
            GUI.color=Color.white;GUI.Label(r,text,_hint);
        }
        internal static void RefreshTables()
        {
            foreach(MapTable t in Object.FindObjectsByType<MapTable>(FindObjectsSortMode.None))
                AccessTools.Method(typeof(TableCourseRpc),"Postfix").Invoke(null,new object[]{t});
        }
        private void OnDestroy()
        {
            Tools.Stop();
            Raven.Stop();
            Ships.Stop();
            Landmarks.Stop();
            MapDraw.Stop();
            NavHud.Stop();
            _harmony?.UnpatchSelf();
            Items.Unregister();
            Pieces.Unregister();
            if(Instance==this)Instance=null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class RegisterScene
    {
        private static void Postfix(ZNetScene __instance){Items.RegisterScene(__instance);Pieces.Register(__instance);}
    }
    [HarmonyPatch(typeof(ObjectDB),"Awake")]
    internal static class AddPiecesToHammer
    {
        private static void Postfix()=>Pieces.AddToHammer();
    }

    // For other mods (Spyglass marks a waypoint where you gaze): found by name while the game runs, no reference needed.
    public static class WayfinderApi
    {
        public static void AddWaypoint(Vector3 at,string name)=>Course.Add(at,name);
        public static int Waypoints=>Course.Points.Count;
    }
}
