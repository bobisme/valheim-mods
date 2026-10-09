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
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.bobisme.buildshapes";
        public const string Name = "BuildShapes";
        public const string Version = "0.5.5";
        internal static Plugin Instance;
        private static readonly FieldInfo RightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly FieldInfo PlacementGhost = AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly MethodInfo TakeInput = AccessTools.Method(typeof(Player), "TakeInput");
        private enum Tool { None, Curve, Mirror, Repeat, Arch, Hall }
        private Tool _tool;
        private ConfigEntry<bool> _enabled, _follow;
        private ConfigEntry<float> _spacing;
        private ConfigEntry<KeyCode> _toggle, _modifier, _mark, _plan, _undo, _back;
        private Harmony _harmony;
        private readonly Planner _planner = new Planner();
        private readonly List<Vector3> _markers = new List<Vector3>();
        private readonly List<PiecePose> _sources = new List<PiecePose>();
        private readonly List<PiecePose> _output = new List<PiecePose>();
        private readonly List<GameObject> _visuals = new List<GameObject>();
        private readonly Dictionary<string, Bounds> _bounds = new Dictionary<string, Bounds>();
        private readonly Dictionary<string, MirrorProfile> _mirrorProfiles = new Dictionary<string, MirrorProfile>();
        private Material _material, _anchorMaterial;
        private string _selected, _lastPlan, _previewError;
        private PiecePose _seed;
        private Vector3 _localStart, _localEnd;
        private float _yaw, _pitch, _roll;
        private float _previewSpacing;
        private bool _previewFollow;
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
        private bool ShapeActive => _tool != Tool.None || _modeMenu;
        internal bool ReservesHammer => ShapeActive && _enabled.Value && HoldingHammer(Player.m_localPlayer);
        internal static bool OptionsMenuOpen => Instance != null && (Instance._modeMenu || Instance._repeatMenu || Instance._archMenu || Instance._hallMenu) && Instance.ReservesHammer;
        private int RequiredMarkers => _tool == Tool.Mirror || _tool == Tool.Arch ? 2 : 3;
        internal static bool ProbingInput => Instance?._probingInput == true;
        internal static bool ReservesEscape => _escapeFrame == Time.frameCount || _escapeFrame == Time.frameCount - 1 ||
            OptionsMenuOpen ||
            (Instance?.ReservesHammer == true && Instance.Ready(Player.m_localPlayer) && Instance._planner.Available(Player.m_localPlayer) && Input.GetKeyDown(KeyCode.Escape));

        private void Awake()
        {
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "Enable Curve, Arch, Mirror, Repeat, and Hallwright tools.");
            // Keep the original config key so existing custom F4 bindings survive the update.
            _toggle = Config.Bind("Controls", "ToggleCurve", KeyCode.F4, "Open/close the shape-mode picker: Curve, Arch, Mirror, Repeat, or Hallwright.");
            _modifier = Config.Bind("Controls", "MarkerModifier", KeyCode.LeftShift, "Hold with PlaceMarker to mark curve points, arch endpoints, or a mirror line.");
            _mark = Config.Bind("Controls", "PlaceMarker", KeyCode.Mouse0, "Mark points; Left Ctrl selects pieces in Mirror/Repeat or marks entrances in Hallwright.");
            _plan = Config.Bind("Controls", "PlanCurve", KeyCode.L, "Submit the current shape as shared BuildOrders ghosts.");
            _undo = Config.Bind("Controls", "UndoCurve", KeyCode.U, "Remove the last shape's unbuilt ghosts in this session. Built pieces stay.");
            _back = Config.Bind("Controls", "RemoveMarker", KeyCode.Backspace, "Remove last marker; Left Ctrl also removes the last Mirror selection.");
            _spacing = Config.Bind("Repeat", "Spacing", 2f, new ConfigDescription("Maximum repeat spacing in metres; adjusted evenly to meet both ends. [ and ] adjust by 0.25 m.", new AcceptableValueRange<float>(0.25f, 16f)));
            _follow = Config.Bind("Repeat", "FollowCurve", true, "Turn pieces around world up to follow the curve, keeping their original tilt. Home toggles this.");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded; hammer + {_toggle.Value}: choose Hallwright, Curve, Arch, Mirror, or Repeat from the mode picker.");
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

        private void Update()
        {
            Player player = Player.m_localPlayer;
            long world = ZNet.World?.m_uid ?? 0;
            if (_player != player || _session != ZNet.instance || _world != world)
            { Stop(); DestroyHall(); _lastPlan = null; _bounds.Clear(); _player = player; _session = ZNet.instance; _world = world; }
            UpdateHallCommands();
            if (!_enabled.Value || !HoldingHammer(player) || player.IsDead())
            { if (ShapeActive) Stop(); if (player != null && player.IsDead()) _lastPlan = null; return; }
            if (!_planner.Ready()) { if (ShapeActive) Stop(); return; }
            if ((_tool == Tool.Mirror || _tool == Tool.Repeat) && !_planner.Extended)
            { Stop(); Say("Mirror/Repeat canceled: update BuildOrders with the ghost-selection API."); return; }
            if (!_planner.Available(player))
            { if (ShapeActive) { Stop(); Say("Finish or cancel the planner's blueprint/bridge first."); } return; }
            if (!Ready(player)) { CloseModeMenu(); CloseRepeatMenu(); CloseArchMenu(); CloseHallMenu(); return; }
            if (_tool == Tool.Hall) UpdateHall();
            if (Input.GetKeyDown(_toggle.Value))
            {
                if (_modeMenu) CloseModeMenu(); else OpenModeMenu();
                return;
            }
            if (_modeMenu)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { _escapeFrame = Time.frameCount; CloseModeMenu(); }
                return; // Picker clicks and keys must never mark, select, or submit a shape.
            }
            if (_tool == Tool.None) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            { _escapeFrame = Time.frameCount; if (_repeatMenu) CloseRepeatMenu(); else if (_archMenu) CloseArchMenu(); else if (_hallMenu) CloseHallMenu(); else Stop(); return; }
            Piece selected = player.GetSelectedPiece();
            if (_tool != Tool.Hall && (selected == null || Utils.GetPrefabName(selected.gameObject) != _selected))
            { Stop(); Say("Shape canceled: hammer selection changed."); return; }
            if (_tool==Tool.Repeat && (_previewSpacing!=SafeSpacing() || _previewFollow!=_follow.Value)) Preview();
            if (_repeatMenu || _archMenu || _hallMenu)
            {
                if (!_editingNumber && Time.frameCount > _menuOpenedFrame+1 && Input.GetKeyDown(_plan.Value))
                { if (_archMenu) ConfirmArch(player); else if (_hallMenu) ConfirmHall(player); else ConfirmRepeat(player); }
                else if (!_editingNumber && Time.unscaledTime-_lastAction>0.5f && Input.GetKeyDown(_undo.Value))
                {_lastAction=Time.unscaledTime;UndoShape(player);}
                return; // Menu mouse/keyboard input must never select pieces or mark the world.
            }
            if (_tool == Tool.Hall && Input.GetKeyDown(KeyCode.Delete)) { _markers.Clear(); _hallDoorPoints.Clear(); BuildHallPreview(); return; }
            if (_tool == Tool.Repeat)
            {
                bool changed = _previewSpacing != SafeSpacing() || _previewFollow != _follow.Value;
                if (Input.GetKeyDown(KeyCode.LeftBracket)) { _spacing.Value = Mathf.Max(0.25f, SafeSpacing() - 0.25f); changed = true; }
                if (Input.GetKeyDown(KeyCode.RightBracket)) { _spacing.Value = Mathf.Min(16f, SafeSpacing() + 0.25f); changed = true; }
                if (Input.GetKeyDown(KeyCode.PageUp)) { _yaw = Mathf.Repeat(_yaw + 15, 360); changed = true; }
                if (Input.GetKeyDown(KeyCode.PageDown)) { _yaw = Mathf.Repeat(_yaw - 15, 360); changed = true; }
                if (Input.GetKeyDown(KeyCode.Home)) { _follow.Value = !_follow.Value; changed = true; }
                if (changed) Preview();
            }
            if (Input.GetKeyDown(_back.Value))
            {
                if (_tool == Tool.Hall && Input.GetKey(KeyCode.LeftControl)) { if(_hallDoorPoints.Count>0)_hallDoorPoints.RemoveAt(_hallDoorPoints.Count-1); }
                else if (_tool == Tool.Mirror && Input.GetKey(KeyCode.LeftControl) && _sources.Count > 0) _sources.RemoveAt(_sources.Count - 1);
                else if (_markers.Count > 0) _markers.RemoveAt(_markers.Count - 1);
                Preview();
            }
            else if (_tool == Tool.Hall && Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(_mark.Value)) MarkHallDoor(player);
            else if ((_tool == Tool.Mirror || _tool == Tool.Repeat) && Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(_mark.Value)) SelectSource(player);
            else if (Input.GetKey(_modifier.Value) && Input.GetKeyDown(_mark.Value)) Mark(player);
            else if (Time.unscaledTime - _lastAction > 0.5f && Input.GetKeyDown(_plan.Value))
            { _lastAction = Time.unscaledTime; if (_tool==Tool.Repeat && _markers.Count==3) OpenRepeatMenu(); else if (_tool==Tool.Arch && _markers.Count==2) OpenArchMenu(); else if (_tool==Tool.Hall) { BuildHallPreview(); if (_markers.Count>=3 || System.IO.File.Exists(HallGroundFile)) OpenHallMenu(); } else Submit(player); }
            else if (Time.unscaledTime - _lastAction > 0.5f && Input.GetKeyDown(_undo.Value))
            {
                _lastAction = Time.unscaledTime;
                UndoShape(player);
            }
        }
        private void UndoShape(Player player)
        {
            if (_lastPlan == null) Say("No shape to undo in this session.");
            else if (_planner.Remove(player,_lastPlan,out int removed,out string error))
            {_lastPlan=null;Say($"Removed {removed} unbuilt shape pieces. Built pieces stay.");}
            else Say(error);
        }

        private void Begin(Player player, Tool requested)
        {
            Stop();
            if (requested == Tool.Hall) { _tool=Tool.Hall; if(System.IO.File.Exists(HallGroundFile))OpenHallMenu(); Say("Hallwright: Shift+click square corners; the first edge sets the grid. Ctrl+click entrances; L opens settings; Backspace edits; Delete clears."); return; }
            if ((requested == Tool.Mirror || requested == Tool.Repeat) && !_planner.Extended)
            { Say("Update BuildOrders with the ghost-selection API for Mirror/Repeat."); return; }
            Piece piece = player.GetSelectedPiece();
            string name = piece != null ? Utils.GetPrefabName(piece.gameObject) : "";
            if (!UsablePrefab(name, out _)) return;
            _selected = name;
            if ((requested == Tool.Curve || requested == Tool.Arch) && !SelectBeam(name)) return;
            if (requested == Tool.Repeat)
            {
                GameObject ghost = PlacementGhost.GetValue(player) as GameObject;
                _seed = new PiecePose(null, name, Vector3.zero, ghost != null ? ghost.transform.rotation : Quaternion.identity);
                SetRepeatAnchors(name);
            }
            _tool = requested;
            Say(requested == Tool.Mirror ? "Mirror: Shift+click two points for the line; Ctrl+click pieces/ghosts to select." :
                requested == Tool.Repeat ? "Repeat: Shift+click start, bend, end; [ / ] spacing; L plans." :
                requested == Tool.Arch ? "Arch: Shift+click start and end, then adjust the center height." : "Curve: Shift+click start, bend, end; L plans.");
        }

        private bool UsablePrefab(string name, out GameObject prefab)
        {
            prefab = ZNetScene.instance?.GetPrefab(name);
            if (prefab == null || prefab.GetComponent<Piece>() == null || prefab.GetComponentInChildren<TerrainOp>(true) != null ||
                prefab.GetComponentInChildren<TerrainModifier>(true) != null || name == "piece_bo_bridge")
            { Say("Choose an ordinary building piece."); return false; }
            if ((prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            { Say("Choose a piece with its original prefab scale."); return false; }
            return true;
        }
        private bool SelectBeam(string name)
        {
            if (!name.Contains("beam") && !name.Contains("pole")) { Say("Select a beam or pole in the hammer first."); return false; }
            Piece piece = ZNetScene.instance.GetPrefab(name).GetComponent<Piece>();
            var snaps = new List<Transform>(); piece.GetSnapPoints(snaps);
            if (snaps.Count != 2) { Say("Choose a beam or pole with two endpoint snap points."); return false; }
            _localStart = piece.transform.InverseTransformPoint(snaps[0].position);
            _localEnd = piece.transform.InverseTransformPoint(snaps[1].position);
            float length = (_localEnd - _localStart).magnitude;
            if (length < 0.25f || length > 8.1f) { Say("Choose a beam between 0.25 and 8 metres long."); return false; }
            return true;
        }
        private static bool CameraRay(out Ray ray)
        {
            Transform camera = GameCamera.instance?.transform;
            ray = camera != null ? new Ray(camera.position, camera.forward) : default;
            return camera != null;
        }
        private static int BuildLayers => LayerMask.GetMask("terrain", "piece", "piece_nonsolid", "Default", "static_solid", "Default_small");
        private void SelectSource(Player player)
        {
            if (!CameraRay(out Ray ray)) return;
            bool hitReal = Physics.Raycast(ray, out RaycastHit hit, 80f, BuildLayers, QueryTriggerInteraction.Ignore);
            PiecePose pose;
            if (_planner.AtRay(player, ray.origin, ray.direction, out string id, out string name, out Vector3 pos, out Quaternion rot, out float distance) &&
                (!hitReal || distance < hit.distance + 0.05f)) pose = new PiecePose("ghost:" + id, name, pos, rot);
            else
            {
                Piece piece = hitReal ? hit.collider.GetComponentInParent<Piece>() : null;
                ZNetView view = piece?.GetComponent<ZNetView>();
                if (piece == null || view == null || !view.IsValid()) { Say("Aim at a built piece or visible planner ghost."); return; }
                pose = new PiecePose("piece:" + piece.GetInstanceID(), Utils.GetPrefabName(piece.gameObject), piece.transform.position, piece.transform.rotation);
                if ((piece.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f) { Say("Scaled pieces cannot be copied."); return; }
            }
            if (Vector3.Distance(pose.Position, player.transform.position) > 40f) { Say("Move within 40 metres of that piece."); return; }
            if (!UsablePrefab(pose.Prefab, out _)) return;
            if (_tool == Tool.Repeat) { _seed = pose; SetRepeatAnchors(pose.Prefab); Say("Repeat piece and orientation copied."); }
            else
            {
                int at = _sources.FindIndex(p => p.Id == pose.Id);
                if (at >= 0) _sources.RemoveAt(at);
                else if (_sources.Count < Curve.MaximumPieces) _sources.Add(pose);
                else { Say("Select at most 256 pieces."); return; }
            }
            Preview();
            if (_tool==Tool.Repeat && _markers.Count==3) OpenRepeatMenu();
        }
        private void Mark(Player player)
        {
            if (_tool==Tool.Hall) { MarkHall(player); return; }
            int needed = RequiredMarkers;
            if (_markers.Count == needed) { Say($"Markers set. {_plan.Value} plans; {_back.Value} changes the last point."); return; }
            if (!CameraRay(out Ray ray) || !Physics.Raycast(ray, out RaycastHit hit, 80f, BuildLayers, QueryTriggerInteraction.Ignore)) return;
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
            _markers.Add(point);
            if (_tool == Tool.Arch && _markers.Count == 2 && !_archRiseSet)
            { _archRise = (float)Arch.DefaultRise(V(_markers[0]), V(_markers[1])); _archRiseSet = true; }
            Preview();
            if (_tool==Tool.Repeat && _markers.Count==3) OpenRepeatMenu();
            else if (_tool==Tool.Arch && _markers.Count==2) OpenArchMenu();
        }
        private static V3 V(Vector3 p) => new V3(p.x, p.y, p.z);
        private static Vector3 V(V3 p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        private float SafeSpacing() => float.IsNaN(_spacing.Value) || float.IsInfinity(_spacing.Value) ? 2 : Mathf.Clamp(_spacing.Value, 0.25f, 16f);

        private void Preview()
        {
            if (_tool==Tool.Hall) { BuildHallPreview(); return; }
            ClearVisuals(); _output.Clear(); _previewError = null;
            _previewSpacing = SafeSpacing(); _previewFollow = _follow.Value;
            try
            {
                if ((_tool == Tool.Curve && _markers.Count == 3) || (_tool == Tool.Arch && _markers.Count == 2))
                {
                    V3 start = V(_markers[0]), end = V(_markers[_markers.Count-1]);
                    V3 middle = _tool == Tool.Arch ? Arch.Middle(start, end, _archRise) : V(_markers[1]);
                    if (_tool == Tool.Arch)
                    {
                        Vector3 center = V(middle), baseline = (_markers[0] + _markers[1]) * 0.5f;
                        Line(new[] { baseline, center }, 0.035f, true);
                        Line(new[] { center-Vector3.right*0.15f, center+Vector3.right*0.15f, center,
                            center-Vector3.forward*0.15f, center+Vector3.forward*0.15f }, 0.055f, true);
                    }
                    foreach (Segment segment in Curve.Plan(start, middle, end, (_localEnd - _localStart).magnitude))
                    {
                        Quaternion rotation = Quaternion.FromToRotation(_localEnd - _localStart, V(segment.End - segment.Start));
                        _output.Add(new PiecePose(null, _selected, V(segment.Start) - rotation * _localStart, rotation));
                        Line(new[] { V(segment.Start), V(segment.End) }, 0.12f);
                    }
                }
                else if (_tool == Tool.Mirror && _markers.Count == 2)
                {
                    var mirror = new Mirror(V(_markers[0]), V(_markers[1]));
                    foreach (PiecePose source in _sources)
                    {
                        MirrorProfile profile = Profile(source.Prefab);
                        Vector3 forward = V(mirror.Forward(V(source.Rotation * Vector3.forward),profile.FlipZ));
                        Vector3 up = V(mirror.Up(V(source.Rotation * Vector3.up)));
                        Vector3 axis=source.Rotation*(profile.FlipZ?Vector3.forward:Vector3.right);
                        Vector3 position = V(mirror.Origin(V(source.Position), V(axis), profile.Centre));
                        Quaternion rotation = Quaternion.LookRotation(forward, up);
                        if ((position-source.Position).sqrMagnitude > 0.000001f || Quaternion.Angle(rotation, source.Rotation) > 0.01f)
                            _output.Add(new PiecePose(null, source.Prefab, position, rotation));
                    }
                    Vector3 a = _markers[0], b = new Vector3(_markers[1].x, a.y, _markers[1].z);
                    Line(new[] { a-Vector3.up*3, b-Vector3.up*3, b+Vector3.up*3, a+Vector3.up*3, a-Vector3.up*3 }, 0.04f);
                }
                else if (_tool == Tool.Repeat && _markers.Count == 3)
                {
                    List<Curve.Station> stations = Curve.Repeat(V(_markers[0]), V(_markers[1]), V(_markers[2]), SafeSpacing());
                    foreach (Curve.Station station in stations)
                    {
                        float turn = _yaw + (_follow.Value ? (float)(station.Yaw - stations[0].Yaw) : 0);
                        Quaternion tilt=Quaternion.AngleAxis(_pitch,Vector3.right)*Quaternion.AngleAxis(_roll,Vector3.forward);
                        Quaternion rotation=Quaternion.AngleAxis(turn, Vector3.up) * _seed.Rotation * tilt;
                        Vector3 anchor=RepeatAnchor;
                        Vector3 position=V(Curve.AnchoredOrigin(station.Position,V(anchor),V(rotation*Vector3.right),V(rotation*Vector3.up),V(rotation*Vector3.forward)));
                        _output.Add(new PiecePose(null, _seed.Prefab, position, rotation));
                        Vector3 point=V(station.Position);
                        const float size=0.10f;
                        Line(new[]{point-Vector3.right*size,point+Vector3.right*size,point,
                            point-Vector3.up*size,point+Vector3.up*size,point,
                            point-Vector3.forward*size,point+Vector3.forward*size},0.035f,true);
                    }
                }
            }
            catch (ArgumentException ex) { _output.Clear(); _previewError = ex.Message; Say(_previewError); }
            if (_tool != Tool.Curve && _tool != Tool.Arch) foreach (PiecePose pose in _output) Box(pose, 0.045f);
            foreach (PiecePose pose in _sources) Box(pose, 0.018f);
            if (_markers.Count > 1 && _tool != Tool.Mirror && _output.Count == 0) Line(_markers.ToArray(), 0.04f);
            foreach (Vector3 marker in _markers)
            {
                Line(new[] { marker - Vector3.right * 0.15f, marker + Vector3.right * 0.15f }, 0.06f);
                Line(new[] { marker - Vector3.up * 0.15f, marker + Vector3.up * 0.15f }, 0.06f);
                Line(new[] { marker - Vector3.forward * 0.15f, marker + Vector3.forward * 0.15f }, 0.06f);
            }
        }
        private void Box(PiecePose pose, float width)
        {
            Bounds box = LocalBounds(pose.Prefab);
            Vector3[] corners = Corners(box);
            int[] path = { 0,1,3,2,0,4,5,1,5,7,3,7,6,2,6,4 };
            Line(path.Select(i => pose.Position + pose.Rotation * corners[i]).ToArray(), width);
        }
        private Bounds LocalBounds(string name)
        {
            if (_bounds.TryGetValue(name, out Bounds box)) return box;
            GameObject prefab = ZNetScene.instance?.GetPrefab(name);
            box = new Bounds(Vector3.zero, Vector3.one * 0.3f);
            if (prefab == null) return box;
            bool any = false;
            void Include(Bounds mesh, Transform transform)
            {
                foreach (Vector3 point in Corners(mesh))
                {
                    Vector3 local = prefab.transform.InverseTransformPoint(transform.TransformPoint(point));
                    if (!any) { box = new Bounds(local, Vector3.zero); any = true; } else box.Encapsulate(local);
                }
            }
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) Include(filter.sharedMesh.bounds, filter.transform);
            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Include(renderer.localBounds, renderer.transform);
            _bounds[name] = box;
            return box;
        }
        private MirrorProfile Profile(string name)
        {
            if (_mirrorProfiles.TryGetValue(name,out MirrorProfile profile)) return profile;
            Piece piece=ZNetScene.instance?.GetPrefab(name)?.GetComponent<Piece>();
            var snaps=new List<Transform>();piece?.GetSnapPoints(snaps);
            profile=MirrorProfile.Choose(snaps.Where(s=>s!=null).Select(s=>V(piece.transform.InverseTransformPoint(s.position))).ToArray(),V(LocalBounds(name).center));
            _mirrorProfiles[name]=profile;return profile;
        }
        private static Vector3[] Corners(Bounds box)
        {
            var result = new Vector3[8];
            for (int i = 0; i < 8; i++) result[i] = box.center + Vector3.Scale(box.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            return result;
        }
        private void Line(Vector3[] points, float width, bool anchor=false)
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader == null) return;
                _material = new Material(shader) { color = new Color(0.3f, 0.95f, 0.9f) };
            }
            if(anchor && _anchorMaterial==null)_anchorMaterial=new Material(_material){color=new Color(1f,0.75f,0.2f)};
            GameObject go = new GameObject("BuildShapes local preview"); go.layer = LayerMask.NameToLayer("Ignore Raycast");
            LineRenderer line = go.AddComponent<LineRenderer>(); line.useWorldSpace = true;
            line.sharedMaterial = anchor?_anchorMaterial:_material; line.startWidth = line.endWidth = width;
            line.positionCount = points.Length; line.SetPositions(points); _visuals.Add(go);
        }
        private void Submit(Player player)
        {
            if (_output.Count == 0) { Say(_previewError ?? (_tool == Tool.Mirror ? "Mark a mirror line and Ctrl+click pieces first." : _tool == Tool.Arch ? "Mark start and end, then adjust the height." : "Mark start, bend, and end first.")); return; }
            if (!_planner.Create(player, _tool.ToString(), _output.Select(p => p.Prefab).ToArray(), _output.Select(p => p.Position).ToArray(),
                _output.Select(p => p.Rotation).ToArray(), out string key, out string error)) { Say(error); return; }
            _lastPlan = key; Say($"{_tool} submitted. Build with E; {_undo.Value} removes unbuilt ghosts.");
            ClearShape();
        }
        private void OnGUI()
        {
            if (!ShapeActive || !Ready(Player.m_localPlayer) || !_planner.Available(Player.m_localPlayer)) return;
            if (_modeMenu) { DrawModeMenu(); return; }
            if (_repeatMenu) { DrawRepeatMenu(); return; }
            if (_archMenu) { DrawArchMenu(); return; }
            if (_hallMenu) { DrawHallMenu(); return; }
            if (_tool==Tool.Hall)
            {
                Matrix4x4 savedHall=GUI.matrix;
                try { Theme(); float hs=Mathf.Max(0.6f,Screen.height/1080f); GUI.matrix=Matrix4x4.Scale(new Vector3(hs,hs,1));
                    string hint="Hallwright: Shift+click corners · Ctrl+click entrances · Ctrl+Backspace: last entrance · L: settings · Delete: clear · F4: modes · Esc: exit\n"+(_hallProblem??$"{_markers.Count} corners · {_output.Count} pieces · support estimate passed");
                    GUI.Box(new Rect(20,Screen.height/hs-160,Mathf.Min(1000,Screen.width/hs-40),95),hint,_hud);
                } finally { GUI.matrix=savedHall; } return;
            }
            string detail = _tool == Tool.Mirror ? $"{_sources.Count} selected; Ctrl+click toggles pieces/ghosts; Ctrl+Backspace removes last selection." :
                _tool == Tool.Repeat ? $"Spacing {SafeSpacing():0.##} m ([ / ]); yaw {_yaw:0}° (PgUp/PgDn); {(_follow.Value ? "follow curve" : "fixed orientation")} (Home); Ctrl+click copies a piece." : _tool == Tool.Arch ? "Two endpoints; L opens center-height options. Native-length beams; joints overlap." : "Native-length beams; joints overlap.";
            string status = _previewError ?? (_output.Count > 0 ? $"{_output.Count} ghosts ready." : $"{_markers.Count}/{RequiredMarkers} markers.");
            string text = $"BuildShapes {_tool}: {_modifier.Value}+click: marker | {_plan.Value}: plan | {_undo.Value}: undo | {_back.Value}: remove marker | {_toggle.Value}: modes | Escape: exit\n{detail}\n{status}";
            Matrix4x4 saved = GUI.matrix;
            try
            {
                Theme();
                float scale = Mathf.Max(0.6f, Screen.height / 1080f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                float width = Mathf.Min(980, Screen.width / scale - 40), height = _hud.CalcHeight(new GUIContent(text), width);
                GUI.Box(new Rect(20, Screen.height / scale - 85 - height, width, height), text, _hud);
            }
            finally { GUI.matrix = saved; }
        }
        private void ClearVisuals() { foreach (GameObject go in _visuals) if (go != null) Destroy(go); _visuals.Clear(); }
        private void ClearShape() { CloseModeMenu(); CloseRepeatMenu(); CloseArchMenu(); ClearHall(); _archRiseSet = false; _markers.Clear(); _sources.Clear(); _output.Clear(); _previewError = null; ClearVisuals(); }
        private void Stop() { _tool = Tool.None; _yaw = _pitch = _roll = 0; _seed = default; ResetRepeatAnchors(); _bounds.Clear(); _mirrorProfiles.Clear(); ClearShape(); }
        private static void Say(string text) { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "BuildShapes: " + (text ?? "Planner unavailable.")); }
        private void OnDestroy()
        {
            Stop(); _harmony?.UnpatchSelf(); if (_material != null) Destroy(_material);
            if(_anchorMaterial!=null)Destroy(_anchorMaterial);
            DestroyHall(); UnregisterHallCommands(); DestroyTheme();
            if (Instance == this) Instance = null;
        }
    }
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class ReserveShapeControls
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance, ref bool takeInput)
        { if (__instance == Player.m_localPlayer && Plugin.Instance?.ReservesHammer == true) takeInput = false; }
    }
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class ReserveShapeEscape
    {
        private static bool Prefix() => !Plugin.ReservesEscape;
    }
}
