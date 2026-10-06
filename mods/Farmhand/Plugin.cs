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
        public const string Version = "0.1.1";

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
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded; {_modifier.Value}+{_rowKey.Value}: row, {_modifier.Value}+{_harvestKey.Value}: harvest/replant.");
        }

        private bool Ready(Player p) => _enabled.Value && p != null && p == Player.m_localPlayer &&
            GameAccess.CanTakeInput(p) && !Hud.IsPieceSelectionVisible() && !Hud.InRadial() &&
            !p.IsSwimming() && p.CanMove() && GameAccess.RightItem(p)?.m_shared.m_name == "$item_cultivator";

        private bool StillWorking(Player p) => Ready(p) && p == _player && GameAccess.RightItem(p) == _tool &&
            p.GetSelectedPiece() == ExpectedPiece && !GameAccess.PlanningActive();

        private void Update()
        {
            Player p = Player.m_localPlayer;
            if (Busy)
            {
                if (!StillWorking(p) || Input.GetKeyDown(KeyCode.Escape)) Cancel();
                return;
            }
            if (!Ready(p)) { _nearbyPlants.Clear(); return; }
            if (Input.GetKey(_modifier.Value))
            {
                if (Input.GetKeyDown(_rowKey.Value)) StartWork(p, PlantRow(p));
                else if (Input.GetKeyDown(_harvestKey.Value))
                    StartWork(p, Harvest(p, !Input.GetKey(_harvestOnlyModifier.Value)));
            }
            if (_healthLabels.Value && Time.unscaledTime >= _nextHealthScan)
            {
                _nextHealthScan = Time.unscaledTime + 0.75f;
                _nearbyPlants.Clear();
                foreach (Plant plant in UnityEngine.Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
                    if (plant != null && !Player.IsPlacementGhost(plant.gameObject) &&
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
            _restorePiece = null;
            ExpectedPiece = null;
            _player = null;
            _tool = null;
        }

        private void OnDestroy()
        {
            Cancel();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }

        private static void Say(Player p, string text) { if (p != null) p.Message(MessageHud.MessageType.Center, "Farmhand: " + text); }

        private void OnGUI()
        {
            Player p = Player.m_localPlayer;
            if (!Ready(p)) return;
            GUI.Label(new Rect(20, Screen.height - 100, 700, 65), Busy ? "Farmhand working… put away the cultivator to cancel." :
                $"Farmhand: hold {_modifier.Value} | {_rowKey.Value}: plant row | {_harvestKey.Value}: harvest/replant | {_harvestOnlyModifier.Value}: harvest only");
            Camera camera = Camera.main;
            if (camera == null) return;
            Color saved = GUI.color;
            try
            {
                if (!Busy && Input.GetKey(_modifier.Value))
                {
                    Piece piece = p.GetSelectedPiece();
                    if (IsCrop(piece))
                        foreach (Vector3 pos in RowPositions(p, piece))
                        {
                            bool reachable = GameAccess.TryGround(p, piece, pos, out RaycastHit hit);
                            Label(camera, reachable ? hit.point : pos, reachable ? "●" : "×", reachable ? Color.green : Color.red);
                        }
                }
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
            GUI.Label(new Rect(screen.x - 70, Screen.height - screen.y, 180, 25), text);
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
