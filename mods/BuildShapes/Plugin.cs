using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BuildShapes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Planner.Guid)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.bobisme.buildshapes";
        public const string Name = "BuildShapes";
        public const string Version = "0.1.0";
        internal static Plugin Instance;
        private static readonly FieldInfo RightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly MethodInfo TakeInput = AccessTools.Method(typeof(Player), "TakeInput");
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyCode> _toggle, _modifier, _mark, _plan, _undo, _back;
        private Harmony _harmony;
        private readonly Planner _planner = new Planner();
        private readonly List<Vector3> _markers = new List<Vector3>();
        private readonly List<GameObject> _visuals = new List<GameObject>();
        private Material _material;
        private bool _active;
        private string _prefab, _lastPlan, _previewError;
        private Vector3 _localStart, _localEnd;
        private List<Segment> _segments;
        private Player _player;
        private ZNet _session;
        private long _world;
        private float _lastAction;
        private static int _escapeFrame = -1;

        internal bool ReservesHammer => _active && _enabled.Value && HoldingHammer(Player.m_localPlayer);
        internal static bool ReservesEscape => _escapeFrame == Time.frameCount || _escapeFrame == Time.frameCount - 1 ||
            (Instance?.ReservesHammer == true && Instance.Ready(Player.m_localPlayer) && Input.GetKeyDown(KeyCode.Escape));

        private void Awake()
        {
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "Enable the Curve tool.");
            _toggle = Config.Bind("Controls", "ToggleCurve", KeyCode.F4, "Toggle Curve mode with a beam or pole selected in your hammer.");
            _modifier = Config.Bind("Controls", "MarkerModifier", KeyCode.LeftShift, "Hold with PlaceMarker to mark the curve.");
            _mark = Config.Bind("Controls", "PlaceMarker", KeyCode.Mouse0, "Mark start, bend, then end on terrain or building pieces.");
            _plan = Config.Bind("Controls", "PlanCurve", KeyCode.L, "Submit the preview as shared BuildOrders ghosts.");
            _undo = Config.Bind("Controls", "UndoCurve", KeyCode.U, "Remove the unbuilt ghosts from your last curve in this session. Built pieces stay.");
            _back = Config.Bind("Controls", "RemoveMarker", KeyCode.Backspace, "Remove the last curve marker.");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded; hammer + {_toggle.Value}: Curve mode.");
        }

        private static bool HoldingHammer(Player player) => player != null &&
            (RightItem.GetValue(player) as ItemDrop.ItemData)?.m_shared.m_name == "$item_hammer";
        private bool Ready(Player player) => _enabled.Value && HoldingHammer(player) && !player.IsDead() &&
            !Hud.IsPieceSelectionVisible() && !Hud.InRadial() && (bool)TakeInput.Invoke(player, null);

        private void Update()
        {
            Player player = Player.m_localPlayer;
            long world = ZNet.World?.m_uid ?? 0;
            if (_player != player || _session != ZNet.instance || _world != world)
            { Stop(); _lastPlan = null; _player = player; _session = ZNet.instance; _world = world; }
            if (!_enabled.Value || !HoldingHammer(player) || player.IsDead()) { if (_active) Stop(); return; }
            // The planner's own window can temporarily take input without losing the preview.
            if (!Ready(player)) return;
            if (Input.GetKeyDown(_toggle.Value))
            {
                if (_active) Stop();
                else if (!_planner.Ready()) Say(_planner.Status);
                else if (SelectBeam(player)) { _active = true; Say($"Curve: {_modifier.Value}+click start, bend, end; {_plan.Value} plans it."); }
                return;
            }
            if (!_active) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { _escapeFrame = Time.frameCount; Stop(); return; }
            Piece selected = player.GetSelectedPiece();
            if (selected == null || Utils.GetPrefabName(selected.gameObject) != _prefab)
            { Stop(); Say("Curve canceled: selected piece changed."); return; }
            if (!_planner.Ready()) { if (_visuals.Count > 0) ClearMarkers(); return; }
            if (Input.GetKeyDown(_back.Value) && _markers.Count > 0)
            { _markers.RemoveAt(_markers.Count - 1); Preview(); }
            else if (Input.GetKey(_modifier.Value) && Input.GetKeyDown(_mark.Value)) Mark(player);
            else if (Time.unscaledTime - _lastAction > 0.5f && Input.GetKeyDown(_plan.Value))
            { _lastAction = Time.unscaledTime; Submit(player); }
            else if (Time.unscaledTime - _lastAction > 0.5f && Input.GetKeyDown(_undo.Value))
            {
                _lastAction = Time.unscaledTime;
                if (_lastPlan == null) Say("No curve to undo in this session.");
                else if (_planner.Remove(player, _lastPlan, out int removed, out string error))
                { _lastPlan = null; Say($"Removed {removed} unbuilt curve pieces. Built pieces stay."); }
                else Say(error);
            }
        }

        private bool SelectBeam(Player player)
        {
            Piece selected = player.GetSelectedPiece();
            string name = selected != null ? Utils.GetPrefabName(selected.gameObject) : "";
            if (!name.Contains("beam") && !name.Contains("pole")) { Say("Select a beam or pole in the hammer first."); return false; }
            GameObject prefab = ZNetScene.instance?.GetPrefab(name);
            Piece piece = prefab?.GetComponent<Piece>();
            if (piece == null) { Say("Selected piece is unavailable."); return false; }
            if ((prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            { Say("Choose a beam or pole with its original prefab scale."); return false; }
            var snaps = new List<Transform>(); piece.GetSnapPoints(snaps);
            if (snaps.Count != 2) { Say("Choose a beam or pole with two endpoint snap points."); return false; }
            float longest = 0;
            for (int i = 0; i < snaps.Count; i++) for (int j = i + 1; j < snaps.Count; j++)
            {
                Vector3 a = prefab.transform.InverseTransformPoint(snaps[i].position), b = prefab.transform.InverseTransformPoint(snaps[j].position);
                float squared = (b - a).sqrMagnitude;
                if (squared > longest) { longest = squared; _localStart = a; _localEnd = b; }
            }
            if (longest < 0.25f * 0.25f || longest > 8.1f * 8.1f) { Say("Choose a beam between 0.25 and 8 metres long."); return false; }
            _prefab = name; return true;
        }

        private void Mark(Player player)
        {
            if (_markers.Count == 3) { Say($"Three markers set. {_plan.Value} plans; {_back.Value} changes the end."); return; }
            Transform camera = GameCamera.instance?.transform;
            if (camera == null || !Physics.Raycast(camera.position, camera.forward, out RaycastHit hit, 80f,
                LayerMask.GetMask("terrain", "piece", "piece_nonsolid", "Default", "static_solid", "Default_small"), QueryTriggerInteraction.Ignore)) return;
            Vector3 point = hit.point;
            Piece piece = hit.collider.GetComponentInParent<Piece>();
            if (piece != null)
            {
                var snaps = new List<Transform>(); piece.GetSnapPoints(snaps);
                Transform nearest = snaps.Where(s => s != null && (s.position - point).sqrMagnitude < 0.25f).OrderBy(s => (s.position - point).sqrMagnitude).FirstOrDefault();
                if (nearest != null) point = nearest.position;
            }
            if (Vector3.Distance(point, player.transform.position) > 40f) { Say("Move within 40 metres of that point."); return; }
            if (_markers.Any(p => Vector3.Distance(p, point) < 0.1f)) { Say("Place distinct markers."); return; }
            _markers.Add(point); Preview();
        }

        private static V3 V(Vector3 p) => new V3(p.x, p.y, p.z);
        private static Vector3 V(V3 p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);

        private void Preview()
        {
            ClearVisuals(); _segments = null; _previewError = null;
            if (_markers.Count == 3)
            {
                try { _segments = Curve.Plan(V(_markers[0]), V(_markers[1]), V(_markers[2]), (_localEnd - _localStart).magnitude); }
                catch (Exception ex) { _previewError = ex.Message; Say(_previewError); }
            }
            if (_segments != null) foreach (Segment segment in _segments) Line(new[] { V(segment.Start), V(segment.End) }, 0.12f);
            else if (_markers.Count > 1) Line(_markers.ToArray(), 0.04f);
            foreach (Vector3 marker in _markers)
            {
                Line(new[] { marker - Vector3.right * 0.15f, marker + Vector3.right * 0.15f }, 0.06f);
                Line(new[] { marker - Vector3.up * 0.15f, marker + Vector3.up * 0.15f }, 0.06f);
                Line(new[] { marker - Vector3.forward * 0.15f, marker + Vector3.forward * 0.15f }, 0.06f);
            }
        }

        private void Line(Vector3[] points, float width)
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader == null) return;
                _material = new Material(shader) { color = new Color(0.3f, 0.95f, 0.9f) };
            }
            GameObject go = new GameObject("BuildShapes local preview"); go.layer = LayerMask.NameToLayer("Ignore Raycast");
            LineRenderer line = go.AddComponent<LineRenderer>(); line.useWorldSpace = true;
            line.sharedMaterial = _material; line.startWidth = line.endWidth = width;
            line.positionCount = points.Length; line.SetPositions(points); _visuals.Add(go);
        }

        private void Submit(Player player)
        {
            if (_segments == null) { Say(_previewError ?? "Mark start, bend, and end first."); return; }
            var positions = new Vector3[_segments.Count]; var rotations = new Quaternion[_segments.Count];
            for (int i = 0; i < _segments.Count; i++)
            {
                Segment s = _segments[i];
                rotations[i] = Quaternion.FromToRotation(_localEnd - _localStart, V(s.End - s.Start));
                positions[i] = V(s.Start) - rotations[i] * _localStart;
            }
            if (!_planner.Create(player, _prefab, positions, rotations, out string key, out string error)) { Say(error); return; }
            _lastPlan = key; Say($"Curve submitted. Build with E; {_undo.Value} removes unbuilt ghosts.");
            ClearMarkers();
        }

        private void OnGUI()
        {
            if (!_active || !Ready(Player.m_localPlayer)) return;
            string status = !_planner.Ready() ? _planner.Status : _previewError ??
                (_segments != null ? $"{_segments.Count} native-length pieces; joints overlap." : $"{_markers.Count}/3 markers: start, bend, end.");
            GUI.Label(new Rect(20, Screen.height - 155, 1000, 80),
                $"BuildShapes: {_modifier.Value}+click: marker | {_plan.Value}: plan | {_undo.Value}: undo | {_back.Value}: remove marker | {_toggle.Value}/Escape: exit\n{status}");
        }
        private void ClearVisuals() { foreach (GameObject go in _visuals) if (go != null) Destroy(go); _visuals.Clear(); }
        private void ClearMarkers() { _markers.Clear(); _segments = null; _previewError = null; ClearVisuals(); }
        private void Stop() { _active = false; ClearMarkers(); }
        private static void Say(string text) { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "BuildShapes: " + (text ?? "Planner unavailable.")); }
        private void OnDestroy()
        {
            Stop(); _harmony?.UnpatchSelf(); if (_material != null) Destroy(_material);
            if (Instance == this) Instance = null;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class ReserveCurveControls
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance, ref bool takeInput)
        { if (__instance == Player.m_localPlayer && Plugin.Instance?.ReservesHammer == true) takeInput = false; }
    }
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class ReserveCurveEscape
    {
        private static bool Prefix() => !Plugin.ReservesEscape;
    }
}
