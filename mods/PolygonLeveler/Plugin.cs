using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PolygonLeveler
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.bobisme.polygonleveler";
        public const string Name = "PolygonLeveler";
        public const string Version = "0.1.1";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        private ConfigEntry<KeyCode> _modifier, _markerKey, _levelKey, _removeKey, _clearKey;
        private ConfigEntry<float> _areaLimit, _distance;
        private Harmony _harmony;
        private readonly List<Vector3> _markers = new List<Vector3>();
        private readonly List<GameObject> _visuals = new List<GameObject>();
        private LineRenderer _outline;
        private Material _material;
        private Coroutine _work;
        private List<Point> _hull = new List<Point>();
        internal bool Busy => _work != null;
        internal bool Marking => Input.GetKey(_modifier.Value);
        internal float Distance => Mathf.Clamp(_distance.Value, 5f, 30f);
        internal bool LevelPressed => Input.GetKeyDown(_levelKey.Value);
        private float _lastUse;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Enable polygon leveling, including requests from other players.");
            _modifier = Config.Bind("Controls", "MarkerModifier", KeyCode.LeftShift, "Hold with marker key to place markers instead of using the hoe.");
            _markerKey = Config.Bind("Controls", "PlaceMarker", KeyCode.Mouse0, "Place a marker on the aimed ground.");
            _levelKey = Config.Bind("Controls", "LevelPolygon", KeyCode.L, "Flatten the polygon to the first marker's height while holding a hoe.");
            _removeKey = Config.Bind("Controls", "RemoveMarker", KeyCode.Backspace, "Remove the last marker.");
            _clearKey = Config.Bind("Controls", "ClearMarkers", KeyCode.Delete, "Clear the marked polygon.");
            _areaLimit = Config.Bind("Limits", "MaximumArea", 400f, new ConfigDescription("Maximum polygon area in square metres.", new AcceptableValueRange<float>(1f, 1000f)));
            _distance = Config.Bind("Limits", "MaximumDistance", 30f, new ConfigDescription("Every selected vertex must be within this horizontal distance of the player.", new AcceptableValueRange<float>(5f, 30f)));
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            foreach (TerrainComp comp in UnityEngine.Object.FindObjectsByType<TerrainComp>(FindObjectsSortMode.None)) TerrainAccess.Register(comp);
            Logger.LogInfo($"{Name} {Version} loaded; hold a hoe, {_modifier.Value}+{_markerKey.Value}: marker, {_levelKey.Value}: level.");
        }

        internal static bool HoldingHoe(Player p) => p != null && TerrainAccess.RightItem(p)?.m_shared.m_name == "$item_hoe";
        private bool Ready(Player p) => Enabled.Value && HoldingHoe(p) && TerrainAccess.TakeInput(p) &&
            !Hud.IsPieceSelectionVisible() && !Hud.InRadial() && !p.IsSwimming() && p.CanMove();

        private void Update()
        {
            TerrainAccess.PruneRequests();
            Player p = Player.m_localPlayer;
            if (p == null) { if (_markers.Count > 0) Clear(); Cancel(); return; }
            bool visible = Enabled.Value && HoldingHoe(p);
            if (_outline != null) _outline.gameObject.SetActive(visible);
            foreach (GameObject marker in _visuals) if (marker != null) marker.SetActive(visible);
            if (Busy)
            {
                if (!Ready(p) || Input.GetKeyDown(KeyCode.Escape)) Cancel();
                return;
            }
            if (!Ready(p)) return;
            if (Input.GetKeyDown(_clearKey.Value)) Clear();
            else if (Input.GetKeyDown(_removeKey.Value) && _markers.Count > 0) { _markers.RemoveAt(_markers.Count - 1); Draw(); }
            else if (Marking && Input.GetKeyDown(_markerKey.Value)) Mark(p);
            else if (LevelPressed && Time.unscaledTime - _lastUse > 1f)
            {
                _lastUse = Time.unscaledTime;
                _work = StartCoroutine(Guard(Level(p)));
            }
        }

        private IEnumerator Guard(IEnumerator operation)
        {
            yield return null;
            while (true)
            {
                bool next;
                try { next = operation.MoveNext(); }
                catch (Exception ex) { Logger.LogError(ex); Say("Leveling stopped: " + ex.GetBaseException().Message); break; }
                if (!next) break;
                yield return operation.Current;
            }
            (operation as IDisposable)?.Dispose();
            TerrainAccess.ClearCallbacks();
            _work = null;
        }

        private void Mark(Player p)
        {
            if (_markers.Count >= Geometry.MaxMarkers) { Say("At most 16 markers; remove one first."); return; }
            if (GameCamera.instance == null || !Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward,
                out RaycastHit hit, 80f, LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore)) return;
            Vector3 delta = hit.point - p.transform.position; delta.y = 0;
            if (delta.magnitude > Distance) { Say("That ground is too far away."); return; }
            if (_markers.Any(m => new Vector2(m.x - hit.point.x, m.z - hit.point.z).sqrMagnitude < 0.04f)) return;
            _markers.Add(hit.point);
            Draw();
        }

        private void Draw()
        {
            foreach (GameObject go in _visuals) if (go != null) Destroy(go);
            _visuals.Clear();
            _hull = Geometry.Hull(_markers.Select(m => new Point(m.x, m.z)));
            if (_material == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader != null) _material = new Material(shader) { color = new Color(0.3f, 0.95f, 0.9f) };
            }
            foreach (Vector3 marker in _markers)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "PolygonLeveler marker";
                go.layer = LayerMask.NameToLayer("Ignore Raycast");
                Destroy(go.GetComponent<Collider>());
                go.transform.position = marker + Vector3.up * 0.15f;
                go.transform.localScale = Vector3.one * 0.25f;
                if (_material != null) go.GetComponent<Renderer>().sharedMaterial = _material;
                _visuals.Add(go);
            }
            if (_outline == null)
            {
                _outline = new GameObject("PolygonLeveler outline").AddComponent<LineRenderer>();
                _outline.useWorldSpace = true;
                _outline.startWidth = _outline.endWidth = 0.07f;
                _outline.loop = true;
            }
            if (_material != null) _outline.sharedMaterial = _material;
            _outline.positionCount = _hull.Count;
            for (int i = 0; i < _hull.Count; i++)
                _outline.SetPosition(i, new Vector3((float)_hull[i].X, _markers[0].y + 0.12f, (float)_hull[i].Z));
        }

        private IEnumerator Level(Player p)
        {
            // Assign the coroutine before it can complete synchronously.
            yield return null;
            if (_hull.Count < 3 || Geometry.Area(_hull) < 0.5) { Say("Place at least three non-collinear markers."); _work = null; yield break; }
            if (Geometry.Area(_hull) > Mathf.Clamp(_areaLimit.Value, 1, 1000)) { Say("Polygon exceeds the maximum area."); _work = null; yield break; }
            List<TerrainAccess.Batch> batches;
            try { batches = TerrainAccess.Plan(p, _hull, _markers[0].y, Distance); }
            catch (Exception ex) { Say(ex.Message); Logger.LogWarning(ex); _work = null; yield break; }
            long id = DateTime.UtcNow.Ticks;
            bool done = false;
            string failed = null;
            int vertices = batches.Sum(b => b.Cells.Count);
            foreach (TerrainAccess.Batch batch in batches)
            {
                TerrainAccess.Send(batch, p, id, 1, (ok, text) => { failed = ok ? null : text; done = true; });
                float deadline = Time.unscaledTime + 5f;
                while (!done && Time.unscaledTime < deadline) yield return null;
                if (!done || failed != null) { Say(failed ?? "Terrain owner did not respond. Install PolygonLeveler on the host and other players."); TerrainAccess.Forget(id); _work = null; yield break; }
                done = false;
            }
            if (!Ready(p)) { TerrainAccess.Forget(id); _work = null; yield break; }
            ItemDrop.ItemData hoe = TerrainAccess.RightItem(p);
            float cost = hoe.m_shared.m_attack.m_attackStamina;
            if ((hoe.m_shared.m_useDurability && hoe.m_durability <= 0) || !p.HaveStamina(cost))
            { Say("Repair the hoe or recover stamina first."); TerrainAccess.Forget(id); _work = null; yield break; }
            // One normal hoe use pays for a polygon. The owner applies normal terrain-height limits.
            p.UseStamina(cost);
            if (hoe.m_shared.m_useDurability) hoe.m_durability -= TerrainAccess.Durability(p, hoe) * Game.m_durabilityRate;
            int applied = 0;
            foreach (TerrainAccess.Batch batch in batches)
            {
                if (!Ready(p)) break;
                done = false;
                TerrainAccess.Send(batch, p, id, 2, (ok, text) => { failed = ok ? null : text; done = true; });
                float deadline = Time.unscaledTime + 5f;
                while (!done && Time.unscaledTime < deadline) yield return null;
                if (!done || failed != null) { Say($"Stopped after {applied} terrain tile(s): {failed ?? "owner did not respond"}"); TerrainAccess.Forget(id); _work = null; yield break; }
                applied++;
                yield return null;
            }
            TerrainAccess.Forget(id);
            Say(applied == batches.Count ? $"Leveled {Geometry.Area(_hull):0.#} m² ({vertices} terrain vertices)." : $"Stopped after {applied} terrain tile(s).");
            _work = null;
        }

        private void OnGUI()
        {
            if (!Ready(Player.m_localPlayer)) return;
            string summary = _markers.Count == 0 ? "First marker sets the flat height." :
                $"{_markers.Count} markers | {Geometry.Area(_hull):0.#} m² | height {_markers[0].y:0.00} m";
            GUI.Label(new Rect(20, Screen.height - 155, 900, 80), Busy ? "PolygonLeveler: leveling… Escape stops further tiles." :
                $"PolygonLeveler: {_modifier.Value}+{_markerKey.Value}: marker | {_levelKey.Value}: flatten | {_removeKey.Value}: remove | {_clearKey.Value}: clear\n{summary}");
        }

        private void Cancel()
        {
            if (_work != null) StopCoroutine(_work);
            _work = null;
            TerrainAccess.ClearCallbacks();
        }
        private void Clear()
        {
            _markers.Clear(); _hull.Clear();
            foreach (GameObject go in _visuals) if (go != null) Destroy(go);
            _visuals.Clear();
            if (_outline != null) { Destroy(_outline.gameObject); _outline = null; }
        }
        private void OnDestroy()
        {
            Cancel(); Clear(); TerrainAccess.UnregisterAll(); _harmony?.UnpatchSelf();
            if (_material != null) Destroy(_material);
            if (Instance == this) Instance = null;
        }
        private static void Say(string text) { if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, "PolygonLeveler: " + text); }
    }

    [HarmonyPatch(typeof(TerrainComp), "Awake")]
    internal static class TerrainRegistration
    {
        private static void Postfix(TerrainComp __instance) => TerrainAccess.Register(__instance);
    }

    [HarmonyPatch(typeof(TerrainComp), nameof(TerrainComp.ApplyToHeightmap))]
    internal static class CaptureTerrainBaseline
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(TerrainComp __instance, List<float> heights, float[] baseHeights) =>
            TerrainAccess.Capture(__instance, heights, baseHeights);
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class ReserveHoeControls
    {
        private static void Prefix(Player __instance, ref bool takeInput)
        {
            Plugin plugin = Plugin.Instance;
            if (__instance == Player.m_localPlayer && plugin != null && plugin.Enabled.Value && Plugin.HoldingHoe(__instance) &&
                (plugin.Marking || plugin.Busy || plugin.LevelPressed)) takeInput = false;
        }
    }
}
