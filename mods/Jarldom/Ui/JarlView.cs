using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // The jarl's seat: sitting in the gilded high seat, J lifts the eye over the colony. WASD or the arrows pan, the wheel zooms. Click a
    // villager to choose them (Shift adds), drag a box to choose many, right-click one for their panel. The bar below sets the chosen
    // ones' profession, marks a zone (drag its rectangle on the ground), or opens the colony window. J, Esc or standing up ends it.
    internal static class JarlView
    {
        public static bool Active { get; private set; }
        private static Colony _c; private static Vector3 _centre; private static float _height = 45f; private static bool _test;
        private static readonly HashSet<string> Chosen = new HashSet<string>();
        private static Vector3? _dragFrom; private static bool _zoneMode;
        private static RectTransform _bar; private static TMP_Text _status, _hover;
        private static readonly List<LineRenderer> Rings = new List<LineRenderer>();
        private static LineRenderer _box;
        private static readonly AccessTools.FieldRef<Player, Transform> AttachPoint = AccessTools.FieldRefAccess<Player, Transform>("m_attachPoint");

        private static bool InHighSeat(Player p)
        {
            if (p == null || !p.IsAttached()) return false;
            Transform at = AttachPoint(p);
            Piece piece = at != null ? at.GetComponentInParent<Piece>() : null;
            return piece != null && Utils.GetPrefabName(piece.gameObject) == Pieces.GildedSeatName;
        }
        public static void Tick()
        {
            Player p = Player.m_localPlayer;
            if (!Active)
            {
                if (InHighSeat(p) && Input.GetKeyDown(KeyCode.J) && !GameUi.Typing && !Windows.AnyOpen)
                {
                    _c = Colony.Nearest(p.transform.position, Renown.ZoneReach + 20);
                    if (_c == null) { p.Message(MessageHud.MessageType.Center, "This high seat stands in no colony."); return; }
                    Begin();
                }
                return;
            }
            if (p == null || !_test && (_c == null || !_c.Valid || !InHighSeat(p)) || Input.GetKeyDown(KeyCode.J) && !GameUi.Typing) { End(); return; }
            if (ColonyWindow.IsOpen || VillagerPanel.IsOpen || RoomPicker.IsOpen) { HideMarks(); return; } // (a window on top has the mouse)
            Steer();
            Pick();
            Marks();
        }
        // For tests: the view over the player, with or without a colony or a high seat (jarldom seatview).
        public static void Test()
        {
            if (Active) { End(); return; }
            _c = Colony.Nearest(Player.m_localPlayer.transform.position, Renown.ZoneReach + 20);
            _test = true; Begin();
        }
        private static void Begin()
        {
            Active = true; Chosen.Clear(); _zoneMode = false; _dragFrom = null;
            _centre = _c != null ? _c.Position : Player.m_localPlayer.transform.position; _height = 45f;
            BuildBar();
        }
        public static void End()
        {
            Active = false; _test = false; Chosen.Clear(); _dragFrom = null;
            if (_bar != null) Object.Destroy(_bar.gameObject);
            _bar = null;
            foreach (LineRenderer l in Rings) if (l != null) Object.Destroy(l.gameObject);
            Rings.Clear();
            if (_box != null) Object.Destroy(_box.gameObject);
            _box = null;
        }

        // ---- the eye ----
        private static void Steer()
        {
            if (GameUi.Typing) return;
            float speed = _height * 0.9f * Time.unscaledDeltaTime;
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move += Vector3.back;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move += Vector3.left;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += Vector3.right;
            _centre += move * speed;
            // Never far past the colony's edge.
            Vector3 home = _c != null ? _c.Position : Player.m_localPlayer.transform.position; float reach = _c != null ? _c.Radius + 30 : 80;
            Vector3 off = _centre - home; off.y = 0;
            if (off.magnitude > reach) _centre = home + off.normalized * reach;
            _height = Mathf.Clamp(_height - Input.mouseScrollDelta.y * 4f, 15f, 120f);
            _centre.y = ZoneSystem.instance.GetGroundHeight(_centre);
        }
        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        private static class Eye
        {
            private static void Postfix(GameCamera __instance)
            {
                if (!Active) return;
                // Looking down at the ground, from a little to the south, so walls and roofs show their faces.
                __instance.transform.position = _centre + new Vector3(0, _height, -_height * 0.35f);
                __instance.transform.rotation = Quaternion.LookRotation(_centre - __instance.transform.position, Vector3.forward);
            }
        }

        // ---- choosing ----
        private static Camera Cam => Utils.GetMainCamera();
        private static bool OverBar() => _bar != null && RectTransformUtility.RectangleContainsScreenPoint(_bar, Input.mousePosition, null);
        private static Humanoid Under()
        {
            Camera cam = Cam; if (cam == null) return null;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Humanoid best = null; float bestD = 1.5f;
            foreach (Humanoid h in Villagers.Bodies())
            {
                if (_c == null || h.GetComponent<ZNetView>()?.GetZDO()?.GetString(Villagers.ColonyKey, "") != _c.Id) continue;
                Vector3 at = h.GetCenterPoint();
                float d = Vector3.Cross(ray.direction, at - ray.origin).magnitude; // distance from the ray
                if (d < bestD) { bestD = d; best = h; }
            }
            return best;
        }
        private static Vector3? Ground()
        {
            Camera cam = Cam; if (cam == null) return null;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 400f, LayerMask.GetMask("terrain", "piece", "Default", "static_solid"), QueryTriggerInteraction.Ignore)) return hit.point;
            return null;
        }
        private static void Pick()
        {
            Humanoid under = Under();
            if (_hover != null) _hover.text = under != null ? Villagers.Brain(under)?.S.FullName ?? under.GetHoverName() : "";
            if (OverBar()) return;
            if (Input.GetMouseButtonDown(1) && under != null) { VillagerPanel.Open(under, true); return; }
            if (Input.GetMouseButtonDown(0)) _dragFrom = Ground();
            if (_dragFrom.HasValue && Input.GetMouseButton(0)) DrawBox(_dragFrom.Value, Ground() ?? _dragFrom.Value);
            if (!Input.GetMouseButtonUp(0) || !_dragFrom.HasValue) return;
            Vector3 a = _dragFrom.Value, b = Ground() ?? a;
            _dragFrom = null;
            if (_box != null) _box.gameObject.SetActive(false);
            bool wide = Utils.DistanceXZ(a, b) > 1.5f;
            if (_zoneMode)
            {
                if (!wide || _c == null) { if (_c == null) { _zoneMode = false; Status(); } return; }
                _zoneMode = false;
                float x1 = Mathf.Min(a.x, b.x), x2 = Mathf.Max(a.x, b.x), z1 = Mathf.Min(a.z, b.z), z2 = Mathf.Max(a.z, b.z);
                RoomPicker.Open(_c, new List<float> { x1, z1, x2, z1, x2, z2, x1, z2 }, Mathf.Min(a.y, b.y));
                Status();
                return;
            }
            bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!add) Chosen.Clear();
            if (!wide) { if (under != null) { string vid = Vid(under); if (!Chosen.Remove(vid)) Chosen.Add(vid); } }
            else
            {
                float x1 = Mathf.Min(a.x, b.x), x2 = Mathf.Max(a.x, b.x), z1 = Mathf.Min(a.z, b.z), z2 = Mathf.Max(a.z, b.z);
                foreach (Humanoid h in Villagers.Bodies())
                {
                    Vector3 p = h.transform.position;
                    if (_c != null && p.x >= x1 && p.x <= x2 && p.z >= z1 && p.z <= z2 && h.GetComponent<ZNetView>()?.GetZDO()?.GetString(Villagers.ColonyKey, "") == _c.Id) Chosen.Add(Vid(h));
                }
            }
            Status();
        }
        private static string Vid(Humanoid h) => h.GetComponent<ZNetView>()?.GetZDO()?.GetString(Villagers.VidKey, "") ?? "";
        private static void DrawBox(Vector3 a, Vector3 b)
        {
            if (_box == null) { _box = Overlay_.NewLine("JarldomSeatBox", _zoneMode ? new Color(0.55f, 0.75f, 1f, 0.9f) : new Color(1f, 0.9f, 0.4f, 0.9f), 0.15f); _box.loop = true; }
            _box.gameObject.SetActive(true);
            float y = Mathf.Max(a.y, b.y) + 0.3f;
            _box.positionCount = 4;
            _box.SetPositions(new[] { new Vector3(a.x, y, a.z), new Vector3(b.x, y, a.z), new Vector3(b.x, y, b.z), new Vector3(a.x, y, b.z) });
        }
        private static void Marks()
        {
            var bodies = Villagers.Bodies().Where(h => Chosen.Contains(Vid(h))).ToList();
            while (Rings.Count < bodies.Count) { LineRenderer l = Overlay_.NewLine("JarldomSeatRing", new Color(1f, 0.9f, 0.4f, 0.95f), 0.08f); l.loop = true; l.positionCount = 16; Rings.Add(l); }
            for (int i = 0; i < Rings.Count; i++)
            {
                bool on = i < bodies.Count;
                Rings[i].gameObject.SetActive(on);
                if (!on) continue;
                Vector3 at = bodies[i].transform.position + Vector3.up * 0.1f;
                for (int k = 0; k < 16; k++) { float t = k * Mathf.PI * 2 / 16; Rings[i].SetPosition(k, at + new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * 0.8f); }
            }
        }
        private static void HideMarks() { foreach (LineRenderer l in Rings) if (l != null) l.gameObject.SetActive(false); }

        // ---- the bar ----
        private static void BuildBar()
        {
            if (!GameUi.Ready()) return;
            _bar = GameUi.Window("JarldomSeatBar", new Vector2(1180, 150));
            _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0f); _bar.pivot = new Vector2(0.5f, 0f); _bar.anchoredPosition = new Vector2(0, 20);
            _status = GameUi.Label(_bar, "", new Vector2(20, -10), new Vector2(700, 26), 18, GameUi.Warm);
            _hover = GameUi.Label(_bar, "", new Vector2(740, -10), new Vector2(420, 26), 16, GameUi.Dim);
            float x = 20, y = 44;
            foreach (ProfessionInfo info in Tables.Professions)
            {
                Prof prof = info.Id;
                if (x + 108 > 1160) { x = 20; y += 36; }
                GameUi.Button(_bar, info.Name, new Vector2(x, -y), new Vector2(104, 32), () => SetProf(prof), true, 13);
                x += 108;
            }
            x = 20; y += 40;
            GameUi.Button(_bar, "Mark a zone", new Vector2(x, -y), new Vector2(150, 32), () => { _zoneMode = true; Status(); }, true, 14); x += 158;
            GameUi.Button(_bar, "Colony window", new Vector2(x, -y), new Vector2(170, 32), () => { if (_c != null) ColonyWindow.Open(_c, "work"); }, _c != null, 14); x += 178;
            GameUi.Button(_bar, "Choose none", new Vector2(x, -y), new Vector2(150, 32), () => { Chosen.Clear(); Status(); }, true, 14); x += 158;
            GameUi.Button(_bar, "Leave the seat's view (J)", new Vector2(x, -y), new Vector2(260, 32), End, true, 14);
            _bar.sizeDelta = new Vector2(1180, y + 46);
            Status();
        }
        private static void SetProf(Prof prof)
        {
            if (_c == null) { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "(a test view: no colony here)"); return; }
            if (Chosen.Count == 0) { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Choose villagers first: click one, or drag a box round several."); return; }
            foreach (string vid in Chosen) Net.Act(_c, "prof", vid + "|" + prof);
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"{Chosen.Count} set to {Tables.Of(prof).Name.ToLower()}");
        }
        private static void Status()
        {
            if (_status == null) return;
            _status.text = _c == null ? "A test of the jarl's seat view (no colony here): WASD or arrows pan, the wheel zooms, J or Esc leaves." : _zoneMode ? "Drag the zone's rectangle on the ground." :
                Chosen.Count == 0 ? $"{_c.R.Name}: click a villager, or drag a box round several (Shift adds). Right-click: their panel." :
                $"{Chosen.Count} chosen: set their profession below.";
        }
    }
}
