using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Village;
using Object = UnityEngine.Object;

namespace Villagewright
{
    // The Villagewright stake: a tall surveyor's pole with a green pennant in the hammer's build menu (Misc). It is never built.
    // Click the ground for the village's centre, then again for its edge (a circle); or Shift+click round a plot, corner by corner.
    // E (or L) opens the settings. Backspace takes back the last corner (or the circle).
    public sealed partial class Plugin
    {
        internal const string StakeName = "VillagewrightStake";
        private static readonly MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList"), UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        private static readonly FieldInfo RightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem"), PlacementGhost = AccessTools.Field(typeof(Player), "m_placementGhost");
        private GameObject _stakePrefab, _stakeHolder;
        private Material _flag;
        private float _stakeCheck, _ringNext;
        private bool _wasStake;

        private static PieceTable HammerTable() => ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        internal static bool IsStake(Piece piece) => piece != null && Utils.GetPrefabName(piece.gameObject) == StakeName;
        private static bool HoldingHammer(Player p) => p != null && (RightItem.GetValue(p) as ItemDrop.ItemData)?.m_shared.m_name == "$item_hammer";
        internal static bool StakeSelected(Player p) => p != null && HoldingHammer(p) && IsStake(p.GetSelectedPiece());

        private void EnsureStake()
        {
            if (Time.unscaledTime < _stakeCheck) return;
            _stakeCheck = Time.unscaledTime + 1;
            ZNetScene scene = ZNetScene.instance; PieceTable table = HammerTable();
            if (scene == null || table == null || !Enabled.Value) return;
            if (_stakePrefab == null)
            {
                try { _stakePrefab = BuildStake(scene); }
                catch (Exception ex) { Warn("Could not make the Villagewright stake: " + ex.Message); _stakeCheck = float.MaxValue; return; }
                if (_stakePrefab == null) return;
            }
            if (table.m_pieces.Contains(_stakePrefab) && scene.GetPrefab(StakeName) == _stakePrefab) return;
            PieceReload.ReplacePrefab(scene, _stakePrefab, table);
            Player player = Player.m_localPlayer;
            if (player != null) { UpdateKnownRecipes?.Invoke(player, null); UpdateAvailablePieces?.Invoke(player, null); }
        }
        private GameObject BuildStake(ZNetScene scene)
        {
            GameObject pole = scene.GetPrefab("wood_pole");
            if (pole == null) return null;
            if (_stakeHolder == null) { _stakeHolder = new GameObject("Villagewright stake"); _stakeHolder.SetActive(false); DontDestroyOnLoad(_stakeHolder); }
            GameObject stake = Instantiate(pole, _stakeHolder.transform, false); stake.name = StakeName;
            var snaps = stake.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("$hud_snappoint", StringComparison.Ordinal)).Select(t => t.localPosition.y).ToList();
            float bottom = snaps.DefaultIfEmpty(0f).Min(), top = snaps.DefaultIfEmpty(1f).Max();
            foreach (Transform child in stake.transform.Cast<Transform>().ToList()) if (child.name.StartsWith("$hud_snappoint", StringComparison.Ordinal)) DestroyImmediate(child.gameObject);
            Material wood = stake.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
            // A surveyor's pole, thin and tall, with a green pennant: the village's own mark (Hallwright's stake has an orange one).
            const float Thin = 0.3f, Tall = 2.2f;
            var model = new GameObject("Model").transform; model.SetParent(stake.transform, false);
            foreach (Transform child in stake.transform.Cast<Transform>().Where(t => t != model).ToList()) child.SetParent(model, false);
            model.localPosition = new Vector3(0, -bottom * Tall, 0); model.localScale = new Vector3(Thin, Tall, Thin);
            top = (top - bottom) * Tall;
            if (wood != null)
            {
                var green = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Villagewright pennant" };
                green.SetPixels(Enumerable.Repeat(new Color(0.25f, 0.75f, 0.3f), 4).ToArray()); green.Apply();
                _flag = new Material(wood) { name = "Villagewright pennant", mainTexture = green, color = Color.white };
                if (_flag.HasProperty("_EmissionColor")) { _flag.EnableKeyword("_EMISSION"); _flag.SetColor("_EmissionColor", new Color(0.05f, 0.3f, 0.08f)); }
            }
            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube); flag.name = "Pennant";
            DestroyImmediate(flag.GetComponent<Collider>());
            flag.transform.SetParent(stake.transform, false);
            flag.transform.localPosition = new Vector3(0.35f, top - 0.3f, 0); flag.transform.localScale = new Vector3(0.7f, 0.45f, 0.02f);
            if (_flag != null) flag.GetComponent<MeshRenderer>().sharedMaterial = _flag;
            var piece = stake.GetComponent<Piece>();
            piece.m_name = "Villagewright stake";
            piece.m_description = "Lay out a whole village. Click the ground for its centre, then its edge; or Shift+click round a plot. E for the settings: the kind of village, what it needs, and Build. The stake isn't built.";
            piece.m_resources = new Piece.Requirement[0];
            piece.m_craftingStation = null;
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_clipEverything = true; piece.m_groundPiece = false; piece.m_groundOnly = false;
            piece.m_enabled = true;
            return stake;
        }
        private void DestroyStake()
        {
            PieceTable table = HammerTable();
            if (_stakePrefab != null && table != null) PieceReload.UnloadPiece(_stakePrefab, table);
            if (_stakeHolder != null) Destroy(_stakeHolder);
            if (_flag != null) { Destroy(_flag.mainTexture); Destroy(_flag); }
        }

        private static bool CameraHit(out Vector3 point)
        {
            point = default;
            Transform cam = GameCamera.instance?.transform; if (cam == null) return false;
            if (!Physics.Raycast(new Ray(cam.position, cam.forward), out RaycastHit hit, 200, LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore)) return false;
            point = hit.point; return true;
        }

        private void TickStake()
        {
            Player player = Player.m_localPlayer;
            bool stake = Enabled.Value && StakeSelected(player) && !player.IsDead();
            if (stake != _wasStake)
            {
                _wasStake = stake;
                if (stake) { ShowPreview = true; DrawPreview(); Say(Site == null ? "click the ground for the village's centre, then for its edge; or Shift+click round a plot. E for the settings." : "E for the settings; click to start a new site."); }
                else if (!VillageWindow.IsOpen && !Building && BuildState != "done") ClearPreview();
            }
            if (!stake || VillageWindow.IsOpen || Hud.IsPieceSelectionVisible() || Hud.InRadial() || global::Menu.IsVisible() || InventoryGui.IsVisible()) return;
            // A circle being drawn: its ring follows the cursor.
            if (CentreMark.HasValue && Site == null && Time.unscaledTime > _ringNext && CameraHit(out Vector3 at))
            {
                _ringNext = Time.unscaledTime + 0.15f;
                ClearPreview();
                P2 c = new P2(CentreMark.Value.x, CentreMark.Value.z); double r = Mathf.Clamp(new Vector2(at.x - CentreMark.Value.x, at.z - CentreMark.Value.z).magnitude, 24, 160);
                Line(Enumerable.Range(0, 72).Select(i => c + P2.Forward(i * 5) * r), new Color(1f, 0.95f, 0.8f, 0.8f), 0.25f, 0.5f, closed: true);
                _labels.Add((new Vector3(at.x, at.y + 2, at.z), $"{r * 2:0} m across"));
            }
            if (Input.GetKeyDown(BackKey.Value))
            {
                if (PlotCorners.Count > 0) { PlotCorners.RemoveAt(PlotCorners.Count - 1); if (PlotCorners.Count >= 3) SetPlot(PlotCorners); else { Site = null; Plan = null; DrawPlotMarks(); } }
                else { CentreMark = null; Site = null; Plan = null; ClearPreview(); }
            }
            else if ((ZInput.GetButtonDown("Use") && player.GetHoverObject() == null) || Input.GetKeyDown(SettingsKey.Value))
            {
                if (Site != null) VillageWindow.Open();
                else Say("mark the site first: click its centre and then its edge, or Shift+click round a plot.");
            }
        }

        // A click with the stake (the game's placing, which never builds it).
        private void StakeClick(Player player)
        {
            if (VillageWindow.IsOpen || !CameraHit(out Vector3 at)) return;
            if (Input.GetKey(PlotModifier.Value))
            {
                if (CentreMark.HasValue) { CentreMark = null; }
                if (Site != null && PlotCorners.Count == 0) Site = null;
                PlotCorners.Add(at);
                if (PlotCorners.Count >= 3) { SetPlot(PlotCorners); Say(Problem ?? $"plot of {PlotCorners.Count} corners: {Plan.Lots.Count} buildings. Shift+click more corners, or E for the settings."); }
                else { DrawPlotMarks(); Say($"corner {PlotCorners.Count}: Shift+click at least {3 - PlotCorners.Count} more."); }
                return;
            }
            if (!CentreMark.HasValue || Site != null)
            {
                // Start a new circle.
                PlotCorners.Clear(); Site = null; Plan = null; CentreMark = at; ClearPreview();
                Say("now click the village's edge.");
                return;
            }
            float r = new Vector2(at.x - CentreMark.Value.x, at.z - CentreMark.Value.z).magnitude;
            if (r < 24) { Say("a village needs at least 24 m from its centre to its edge."); return; }
            SetCircle(CentreMark.Value, r);
            Say(Problem ?? $"{Plan.Brief.Kind} village: {Plan.Lots.Count} buildings, {Plan.Fields.Count} fields. E for the settings.");
        }
        private void DrawPlotMarks()
        {
            ClearPreview();
            var pts = PlotCorners.Select(c => new P2(c.x, c.z)).ToList();
            if (pts.Count >= 2) Line(pts, new Color(1f, 0.95f, 0.8f, 0.8f), 0.25f, 0.5f);
            foreach (var c in PlotCorners) _labels.Add((c + Vector3.up * 2, "corner"));
        }
        private void PlaceStakeGhost(Player player)
        {
            if (!StakeSelected(player) || VillageWindow.IsOpen || !(PlacementGhost?.GetValue(player) is GameObject ghost) || ghost == null || !ghost.activeSelf) return;
            if (CameraHit(out Vector3 at)) ghost.transform.position = at;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class StakeNeverBuilt
        {
            [HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                if (!IsStake(piece)) return true;
                __result = false;
                if (__instance == Player.m_localPlayer && Instance != null && Instance.Enabled.Value) Instance.StakeClick(__instance);
                return false;
            }
        }
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class StakeGhost
        {
            private static void Postfix(Player __instance) { if (__instance == Player.m_localPlayer) Instance?.PlaceStakeGhost(__instance); }
        }
    }
}
