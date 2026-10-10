using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Farmhand
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.bobisme.farmhand";
        public const string Name = "Farmhand";
        public const string Version = "0.2.2";

        internal static Plugin Instance;
        internal bool Busy => _routine != null;
        internal Piece ExpectedPiece;
        private Harmony _harmony;
        private Coroutine _routine;
        private Piece _restorePiece;
        private ItemDrop.ItemData _tool;
        private Player _player;
        private ConfigEntry<bool> _enabled, _healthLabels;
        private ConfigEntry<KeyCode> _modifier, _rowKey, _harvestKey, _harvestOnlyModifier;
        private ConfigEntry<int> _rowCount, _batchLimit;
        private ConfigEntry<float> _spacing, _harvestRadius;
        private readonly List<Plant> _nearbyPlants = new List<Plant>();
        private float _nextHealthScan;

        private void Awake()
        {
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "Enable Farmhand.");
            _modifier = Config.Bind("Controls", "Modifier", KeyCode.LeftShift, "Hold to preview and use Farmhand controls.");
            _rowKey = Config.Bind("Controls", "PlantRow", KeyCode.J, "With modifier held, plant a row of the selected crop.");
            _harvestKey = Config.Bind("Controls", "Harvest", KeyCode.U, "With modifier held, harvest and replant nearby crops.");
            _harvestOnlyModifier = Config.Bind("Controls", "HarvestOnlyModifier", KeyCode.LeftControl, "Also hold this key to harvest without replanting.");
            _rowCount = Config.Bind("Planting", "RowCount", 5, new ConfigDescription("Crops in a row, centered on your placement ghost.", new AcceptableValueRange<int>(1, 9)));
            _spacing = Config.Bind("Planting", "Spacing", 1.8f, new ConfigDescription("Minimum spacing in metres; crop growth radius may increase it.", new AcceptableValueRange<float>(0.5f, 5f)));
            _harvestRadius = Config.Bind("Harvest", "Radius", 3f, new ConfigDescription("Harvest within this radius of the player, subject to normal placement reach for replanting.", new AcceptableValueRange<float>(1f, 5f)));
            _batchLimit = Config.Bind("Harvest", "BatchLimit", 40, new ConfigDescription("Maximum crops per key press.", new AcceptableValueRange<int>(1, 100)));
            _healthLabels = Config.Bind("Display", "HealthLabels", true, "While using the cultivator, label unhealthy crops nearby.");
            SetupPlot();
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded; {_modifier.Value}+{_markerKey.Value}: plot corner, {_modifier.Value}+{_plotKey.Value}: plant plot, {_modifier.Value}+{_harvestKey.Value}: harvest/replant.");
        }

        private bool Ready(Player p) => _enabled.Value && p != null && p == Player.m_localPlayer &&
            GameAccess.CanTakeInput(p) && !Hud.IsPieceSelectionVisible() && !Hud.InRadial() &&
            !p.IsSwimming() && p.CanMove() && GameAccess.RightItem(p)?.m_shared.m_name == "$item_cultivator";

        private bool StillWorking(Player p) => Ready(p) && p == _player && GameAccess.RightItem(p) == _tool &&
            p.GetSelectedPiece() == ExpectedPiece && !GameAccess.PlanningActive();

        private void Update()
        {
            Player p = Player.m_localPlayer;
            UpdatePlot(p);
            if (Busy)
            {
                if (!StillWorking(p) || Input.GetKeyDown(KeyCode.Escape)) Cancel();
                return;
            }
            if (!Ready(p)) { _nearbyPlants.Clear(); return; }
            if (Input.GetKeyDown(_clearPlot.Value)) ClearPlot();
            else if (Input.GetKeyDown(_removeMarker.Value) && _corners.Count>0)
            { _corners.RemoveAt(_corners.Count-1); if(_corners.Count==0)ClearPlot();else _plotDirty=true; }
            else if (Input.GetKey(_modifier.Value))
            {
                if (Input.GetKeyDown(_markerKey.Value)) MarkPlot(p);
                else if (_corners.Count>0 && Input.GetKeyDown(_plotKey.Value)) StartPlot(p);
                else if (Input.GetKeyDown(_rowKey.Value) && _corners.Count==0) StartWork(p, PlantRow(p));
                else if (Input.GetKeyDown(_harvestKey.Value))
                    StartWork(p, Harvest(p, !Input.GetKey(_harvestOnlyModifier.Value)));
            }
            if (_healthLabels.Value && Time.unscaledTime >= _nextHealthScan)
            {
                _nextHealthScan = Time.unscaledTime + 0.75f;
                _nearbyPlants.Clear();
                // The game's own list of slow-updating objects holds every loaded plant: no search of every loaded object.
                foreach (SlowUpdate slow in SlowUpdate.GetAllInstaces())
                    if (slow is Plant plant && plant != null && !Player.IsPlacementGhost(plant.gameObject) &&
                        (plant.transform.position - p.transform.position).sqrMagnitude <= 100f &&
                        plant.GetStatus() != Plant.Status.Healthy)
                    {
                        _nearbyPlants.Add(plant);
                        if (_nearbyPlants.Count >= 30) break;
                    }
            }
        }

        private void StartWork(Player p, IEnumerator work)
        {
            if (GameAccess.PlanningActive()) { Say(p, "Turn off BuildOrders plan mode first."); return; }
            _player = p;
            _tool = GameAccess.RightItem(p);
            _restorePiece = p.GetSelectedPiece();
            ExpectedPiece = _restorePiece;
            // Yield once before completing: StartCoroutine can otherwise finish synchronously before assignment.
            _routine = StartCoroutine(Run(work));
        }

        private IEnumerator Run(IEnumerator work)
        {
            yield return null;
            while (true)
            {
                bool next;
                try { next = work.MoveNext(); }
                catch (Exception ex) { Logger.LogError(ex); Say(_player, "Farmhand stopped; see the BepInEx log."); break; }
                if (!next) break;
                yield return work.Current;
            }
            (work as IDisposable)?.Dispose();
            Finish();
        }

        private void Cancel()
        {
            if (_routine != null) StopCoroutine(_routine);
            Finish();
        }

        private void Finish()
        {
            GameAccess.Target = null;
            GameAccess.ValidatedPosition = null;
            if (_player != null && _player == Player.m_localPlayer && GameAccess.RightItem(_player) == _tool &&
                _restorePiece != null && _player.GetBuildTool() != null && _player.GetSelectedPiece() == ExpectedPiece)
                _player.SetSelectedPiece(_restorePiece);
            _routine = null;
            _plantingPlot = false;
            _restorePiece = null;
            ExpectedPiece = null;
            _player = null;
            _tool = null;
        }

        private void OnDestroy()
        {
            Cancel(); DestroyPlot();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }

        private static void Say(Player p, string text) { if (p != null) p.Message(MessageHud.MessageType.Center, "Farmhand: " + text); }

        private void OnGUI()
        {
            Player p = Player.m_localPlayer;
            if (!Ready(p)) return;
            string controls=_corners.Count>0
                ? $"{_modifier.Value}+{_markerKey.Value}: corner | {_modifier.Value}+{_plotKey.Value}: plant plot | {_removeMarker.Value}: remove | {_clearPlot.Value}: clear"
                : $"{_modifier.Value}+{_markerKey.Value}: plot corner | {_modifier.Value}+{_rowKey.Value}: row | {_modifier.Value}+{_harvestKey.Value}: harvest/replant";
            string activity=Busy?(_plantingPlot?"Planting your plot as you walk. Escape pauses.":"Working… Escape cancels."):controls;
            GUI.Label(new Rect(20, Screen.height - 130, 1100, 90),"Farmhand: "+activity+"\n"+(_corners.Count>0?PlotSummary():$"Select a crop, then mark a boundary or plant a row. Hold {_harvestOnlyModifier.Value} with harvest for harvest only."));
            Camera camera = Camera.main;
            if (camera == null) return;
            Color saved = GUI.color;
            try
            {
                if (!Busy && _corners.Count==0 && Input.GetKey(_modifier.Value))
                {
                    Piece piece = p.GetSelectedPiece();
                    if (IsCrop(piece))
                        foreach (Vector3 pos in RowPositions(p, piece))
                        {
                            bool reachable = GameAccess.TryGround(p, piece, pos, out RaycastHit hit);
                            Label(camera, reachable ? hit.point : pos, reachable ? "●" : "×", reachable ? Color.green : Color.red);
                        }
                }
                for(int i=0;i<_corners.Count;i++)Label(camera,_corners[i]+Vector3.up*0.22f,(i+1).ToString(),new Color(1,0.85f,0.5f));
                if (_healthLabels.Value)
                    foreach (Plant plant in _nearbyPlants)
                        if (plant != null) Label(camera, plant.transform.position + Vector3.up * 0.6f,
                            HealthReason(plant.GetStatus()), new Color(1f, 0.75f, 0.25f));
            }
            finally { GUI.color = saved; }
        }

        private static void Label(Camera camera, Vector3 pos, string text, Color color)
        {
            Vector3 screen = camera.WorldToScreenPoint(pos);
            if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) return;
            GUI.color = color;
            Vector2 size=GUI.skin.label.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(screen.x-size.x*0.5f,Screen.height-screen.y,size.x+6,25),text);
        }

        private static string HealthReason(Plant.Status status)
        {
            switch (status)
            {
                case Plant.Status.NoSpace: return "Needs more space";
                case Plant.Status.NoSun: return "Needs sunlight";
                case Plant.Status.NotCultivated: return "Needs cultivated soil";
                case Plant.Status.WrongBiome: return "Wrong biome";
                case Plant.Status.TooHot: return "Too hot";
                case Plant.Status.TooCold: return "Too cold";
                case Plant.Status.NoAttachPiece: return "Needs a support";
                default: return "";
            }
        }
    }
}
