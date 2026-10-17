using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Hallwright
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Planner.Guid)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.hallwright";
        public const string Name = "Hallwright";
        public const string Version = "0.12.3";
        internal static Plugin Instance;
        private static readonly FieldInfo RightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly FieldInfo PlacementGhost = AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly MethodInfo TakeInput = AccessTools.Method(typeof(Player), "TakeInput");
        // A draft is open: picked up with the stake, or started by a Claude Tools hall command.
        private bool _active;
        private ConfigEntry<bool> _enabled, _hallFreeAngle;
        private ConfigEntry<KeyCode> _modifier, _plan, _undo, _back;
        private Harmony _harmony;
        private readonly Planner _planner = new Planner();
        private readonly List<Vector3> _markers = new List<Vector3>();
        private readonly List<PiecePose> _output = new List<PiecePose>();
        private readonly List<GameObject> _visuals = new List<GameObject>();
        private Material _material, _anchorMaterial;
        private string _lastPlan, _previewError;
        private Player _player;
        private ZNet _session;
        private long _world;
        private float _lastAction;
        private static int _escapeFrame = -1;

        private readonly struct PiecePose
        {
            internal readonly string Id, Prefab;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            internal PiecePose(string id, string prefab, Vector3 position, Quaternion rotation)
            { Id = id; Prefab = prefab; Position = position; Rotation = rotation; }
        }
        // The stake places as the game does (its ghost, the build menu); the hammer is only taken while the settings are open.
        internal bool ReservesHammer => _active && _hallMenu && _enabled.Value && HoldingHammer(Player.m_localPlayer) && _planner.Available(Player.m_localPlayer);
        internal static bool OptionsMenuOpen => Instance != null && Instance._hallMenu && Instance.ReservesHammer;
        internal static bool ProbingInput => Instance?._probingInput == true;
        internal static bool ReservesEscape => _escapeFrame == Time.frameCount || _escapeFrame == Time.frameCount - 1 || OptionsMenuOpen;

        private void Awake()
        {
            Instance = this;
            bool fresh = !File.Exists(Config.ConfigFilePath);
            ConfigureSharedPreviews();
            _enabled = Config.Bind("General", "Enabled", true, "Enable the Hallwright stake: timber halls, longhouses and stave temples.");
            _modifier = Config.Bind("Controls", "MarkerModifier", KeyCode.LeftShift, "Hold while clicking with the stake to mark an entrance (Left Ctrl marks a smoke hole or chimney).");
            _plan = Config.Bind("Controls", "PlanCurve", KeyCode.L, "Open the settings; with the settings open, plan the shell as shared BuildOrders ghosts.");
            _undo = Config.Bind("Controls", "UndoCurve", KeyCode.U, "Remove the last planned hall's unbuilt ghosts in this session. Built pieces stay.");
            _back = Config.Bind("Controls", "RemoveMarker", KeyCode.Backspace, "Remove the last corner; with Shift the last entrance, with Left Ctrl the last chimney.");
            _hallFreeAngle = Config.Bind("Hallwright", "FreeAngle", false, "Let the first edge of a Hallwright or Stave temple outline set any angle. Off: the outline turns in the game's own 22.5° build steps, so it lines up with pieces placed by hand.");
            if (fresh) CarryOverBuildShapesSettings();
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded; pick the Hallwright stake in the hammer's build menu (Misc).");
        }
        // Hallwright was part of BuildShapes until 0.9.0: the first time, take its Hallwright, key and shared-draft settings along.
        private void CarryOverBuildShapesSettings()
        {
            string old = Path.Combine(Paths.ConfigPath, "com.bobisme.buildshapes.cfg");
            if (!File.Exists(old)) return;
            try
            {
                string section = null; int carried = 0;
                foreach (string raw in File.ReadAllLines(old))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                    int equals = line.IndexOf('=');
                    if (section == null || line.StartsWith("#") || equals < 0) continue;
                    var key = new ConfigDefinition(section, line.Substring(0, equals).Trim());
                    if (key.Section == "General" || !Config.ContainsKey(key)) continue;
                    Config[key].SetSerializedValue(line.Substring(equals + 1).Trim()); carried++;
                }
                if (carried > 0) Logger.LogInfo($"Carried {carried} settings over from BuildShapes.");
            }
            catch (Exception ex) { Logger.LogWarning("Could not read BuildShapes' settings: " + ex.Message); }
        }

        private static bool HoldingHammer(Player player) => player != null &&
            (RightItem.GetValue(player) as ItemDrop.ItemData)?.m_shared.m_name == "$item_hammer";
        private bool _probingInput;
        private bool Ready(Player player)
        {
            if (!_enabled.Value || !HoldingHammer(player) || player.IsDead() || Hud.IsPieceSelectionVisible() || Hud.InRadial()) return false;
            try { _probingInput=true; return (bool)TakeInput.Invoke(player,null); }
            finally { _probingInput=false; }
        }
        // BuildShapes before 0.9.0 has Hallwright too: two stakes and two drafts on the same save file.
        private bool _warnedOldBuildShapes;
        private void WarnOldBuildShapes(Player player)
        {
            if (_warnedOldBuildShapes || player == null) return;
            _warnedOldBuildShapes = true;
            if (!Chainloader.PluginInfos.TryGetValue("com.bobisme.buildshapes", out var info) || info.Metadata.Version >= new System.Version(0, 9, 0)) return;
            Logger.LogWarning($"BuildShapes {info.Metadata.Version} still has its own Hallwright. Update BuildShapes to 0.9.0 or newer.");
            Say($"update BuildShapes ({info.Metadata.Version} still has its own Hallwright stake).");
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            long world = ZNet.World?.m_uid ?? 0;
            if (_player != player || _session != ZNet.instance || _world != world)
            { Stop(); DestroyHall(); _lastPlan = null; _player = player; _session = ZNet.instance; _world = world; }
            UpdateHallCommands();UpdateSharedPreviews();EnsureStake();HookPlanRemoval();FollowHallGround(player);WarnOldBuildShapes(player);
            if (!_enabled.Value || !HoldingHammer(player) || player.IsDead())
            { if (_active) Stop(); if (player != null && player.IsDead()) _lastPlan = null; return; }
            if (!_planner.Ready() || !_planner.Available(player) || !Ready(player)) { CloseHallMenu(); return; }
            // Picking the stake turns Hallwright on; picking another piece turns it off again (the draft is saved).
            bool stake = StakeSelected(player);
            if (stake && !_active)
            {
                Begin();
                // Carry on with the saved draft, if there is one for this character and world.
                if (File.Exists(CurrentHallDraftPath)) ResumeHallDraft(false);
            }
            else if (!stake && _active && _hallViaStake) { Stop(); return; }
            if (!_active) return;
            UpdateHall(); TickHallWindow(player);
            if (Input.GetKeyDown(KeyCode.Escape) && (_hallMenu || !stake))
            { _escapeFrame = Time.frameCount; if (_hallMenu) CloseHallMenu(); else Stop(); return; }
            if (_hallMenu)
            {
                if (!_editingNumber && Time.frameCount > _menuOpenedFrame+1 && Input.GetKeyDown(_plan.Value)) ConfirmHall(player);
                else if (!_editingNumber && Time.unscaledTime-_lastAction>0.5f && Input.GetKeyDown(_undo.Value))
                {_lastAction=Time.unscaledTime;UndoShape(player);}
                return; // Menu mouse/keyboard input must never mark the world.
            }
            if (Input.GetKeyDown(KeyCode.Delete)) { ClearHallDraft(); return; }
            if (Input.GetKeyDown(_back.Value))
            {
                // Shift+Backspace removes the last entrance, Ctrl+Backspace the last chimney, as Shift/Ctrl+click mark them.
                if (Input.GetKey(KeyCode.LeftControl)) { if(_hallChimneyPoints.Count>0)_hallChimneyPoints.RemoveAt(_hallChimneyPoints.Count-1); }
                else if (Input.GetKey(_modifier.Value)) { if(_hallDoorPoints.Count>0)_hallDoorPoints.RemoveAt(_hallDoorPoints.Count-1); }
                else if (_markers.Count > 0) _markers.RemoveAt(_markers.Count - 1);
                if(_markers.Count==0)DiscardHallDraft();
                BuildHallPreview();
            }
            // Clicks reach Hallwright through the game's own placing (StakeClick); E inside the outline opens the settings.
            else if (stake && ZInput.GetButtonDown("Use") && StakeLooksInside(player)) { BuildHallPreview(); OpenHallMenu(); }
            else if (Time.unscaledTime - _lastAction > 0.5f && Input.GetKeyDown(_plan.Value))
            { _lastAction = Time.unscaledTime; BuildHallPreview(); if (_markers.Count>=3 || File.Exists(HallGroundFile)) OpenHallMenu(); }
            else if (Time.unscaledTime - _lastAction > 0.5f && Input.GetKeyDown(_undo.Value))
            { _lastAction = Time.unscaledTime; UndoShape(player); }
        }
        private void UndoShape(Player player)
        {
            if (_lastPlan == null) Say("No hall to undo in this session.");
            else if (_planner.Remove(player,_lastPlan,out int removed,out string error))
            {HallPlanRemoved(_lastPlan);_lastPlan=null;Say($"Removed {removed} unbuilt hall pieces. Built pieces stay.");}
            else Say(error);
        }
        private void Begin()
        {
            Stop();
            _hallViaStake = true; _active = true; StartHallDraft();
            Say("click the ground for each corner; the first edge sets the grid. Shift+click entrances, Ctrl+click smoke holes or chimneys, E inside the outline for settings. Backspace undoes a corner (Shift+ the last entrance, Ctrl+ the last chimney); Delete clears.");
        }

        private static bool CameraRay(out Ray ray)
        {
            Transform camera = GameCamera.instance?.transform;
            ray = camera != null ? new Ray(camera.position, camera.forward) : default;
            return camera != null;
        }
        private static int BuildLayers => LayerMask.GetMask("terrain", "piece", "piece_nonsolid", "Default", "static_solid", "Default_small");
        private static V3 V(Vector3 p) => new V3(p.x, p.y, p.z);
        private static Vector3 V(V3 p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);

        private void Line(Vector3[] points, float width, bool anchor=false)
        {
            DirtySharedPreview();
            if (_material == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader == null) return;
                _material = new Material(shader) { color = new Color(0.3f, 0.95f, 0.9f) };
            }
            if(anchor && _anchorMaterial==null)_anchorMaterial=new Material(_material){color=new Color(1f,0.75f,0.2f)};
            GameObject go = new GameObject("Hallwright local preview"); go.layer = LayerMask.NameToLayer("Ignore Raycast");
            LineRenderer line = go.AddComponent<LineRenderer>(); line.useWorldSpace = true;
            line.sharedMaterial = anchor?_anchorMaterial:_material; line.startWidth = line.endWidth = width;
            line.positionCount = points.Length; line.SetPositions(points); _visuals.Add(go);
        }
        private void OnGUI()
        {
            DrawSharedLabels();
            if (!_active || !Ready(Player.m_localPlayer) || !_planner.Available(Player.m_localPlayer)) return;
            if (_hallMenu) { if (_hallWindowFallback) DrawHallMenu(); return; }
            DrawHallLabels();
            Matrix4x4 saved=GUI.matrix;
            try { Theme(); float scale=Mathf.Max(0.6f,Screen.height/1080f); GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1)); DrawHallDrawingProblem(scale); }
            finally { GUI.matrix=saved; }
        }
        private void ClearVisuals() { DirtySharedPreview(); foreach (GameObject go in _visuals) if (go != null) Destroy(go); _visuals.Clear(); ClearSketchMarks(); }
        private void Stop()
        {
            SaveHallDraft(); _hallDraftWorld=_hallDraftPlayer=0; _hallDraftSubmitted=false; _hallDraftLast=null; _active = false; _hallViaStake = false;
            ClearHall(); _markers.Clear(); _output.Clear(); _previewError = null; ClearVisuals();
        }
        private static void Say(string text) { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Hallwright: " + (text ?? "Planner unavailable.")); }
        private void OnDestroy()
        {
            DestroySharedPreviews();Stop(); _harmony?.UnpatchSelf(); if (_material != null) Destroy(_material);
            if(_anchorMaterial!=null)Destroy(_anchorMaterial);
            CloseHallWindow(); GameUi.Destroy(); DestroyHall(); DestroyHallDrawing(); DestroyStake(); UnregisterHallCommands(); DestroyTheme();
            if (Instance == this) Instance = null;
        }
    }
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class ReserveHallControls
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance, ref bool takeInput)
        { if (__instance == Player.m_localPlayer && Plugin.Instance?.ReservesHammer == true) takeInput = false; }
    }
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class ReserveHallEscape
    {
        private static bool Prefix() => !Plugin.ReservesEscape;
    }
}
