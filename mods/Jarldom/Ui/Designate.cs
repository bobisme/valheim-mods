using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Marking rooms and zones: with the hammer's "Mark rooms and zones" chosen, a click marks a corner where the building ghost stands;
    // clicking the first corner again closes the outline and asks what it is. Backspace takes back the last corner. Ctrl+Shift+click on
    // a room's floor finds the room from its walls at once. Shift+E on a chest chooses what it's for (Ui.ChestPicker).
    internal static class Designate
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> Ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly List<Vector3> Corners = new List<Vector3>();
        private static LineRenderer _line; private static readonly List<GameObject> _posts = new List<GameObject>();

        public static bool Active
        {
            get
            {
                Player p = Player.m_localPlayer;
                if (p == null || !p.InPlaceMode()) return false;
                Piece sel = p.GetSelectedPiece();
                return sel != null && sel.gameObject.name == Pieces.DesignateName;
            }
        }
        private static float _leakUntil;
        public static void Tick()
        {
            if (_leakUntil > 0 && Time.time > _leakUntil && Corners.Count == 0) { _leakUntil = 0; Draw(); } // (the red post where a found room leaked, gone after a while)
            if (!Active) { if (Corners.Count > 0 && !Ui.RoomPicker.IsOpen) Clear(); Ui.ChestPicker.Hint(null); Ui.ChestPicker.FireHint(null); return; }
            // A chest under the crosshair: what it's for, and Shift+E to choose.
            Container looked = Corners.Count == 0 && !Ui.Windows.AnyOpen && Player.m_localPlayer is Player lp ? Ui.ChestPicker.Looked(lp) : null;
            Ui.ChestPicker.Hint(looked);
            if (looked != null)
            {
                Ui.ChestPicker.FireHint(null);
                if (!GameUi.Typing && ZInput.GetButtonDown("Use") && (ZInput.GetButton("AltPlace") || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) Ui.ChestPicker.Open(looked);
                return; // (E on a chest isn't E on the room it stands in)
            }
            // A fire under the crosshair: Shift+E makes it the colony's beacon (or not).
            Fireplace fire = Corners.Count == 0 && !Ui.Windows.AnyOpen && Player.m_localPlayer is Player fp ? Ui.ChestPicker.LookedFire(fp) : null;
            Ui.ChestPicker.FireHint(fire);
            if (fire != null)
            {
                if (!GameUi.Typing && ZInput.GetButtonDown("Use") && (ZInput.GetButton("AltPlace") || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Colony.Nearest(fire.transform.position, Renown.ZoneReach + 20) is Colony fc)
                    Net.Act(fc, "beaconmark", Colony.IdOf(fire.GetComponent<ZNetView>()));
                return;
            }
            // Shift+E on open ground, with a great work chosen at the Thing and not yet placed: it's planned there.
            if (Corners.Count == 0 && !GameUi.Typing && !Ui.Windows.AnyOpen && ZInput.GetButtonDown("Use") && (ZInput.GetButton("AltPlace") || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                && Player.m_localPlayer is Player wp && Ghost(wp) is GameObject wg && Colony.Nearest(wg.transform.position, Renown.ZoneReach + 20) is Colony wc && GreatWorks.Stage(wc.R.Flags, GreatWorks.Lighthouse) == GreatWorks.Promised)
            { GreatWork.RaiseHere(wc, GreatWorks.Lighthouse, wg.transform.position); return; }
            if (Input.GetKeyDown(KeyCode.Backspace) && Corners.Count > 0 && !GameUi.Typing) { Corners.RemoveAt(Corners.Count - 1); Draw(); }
            // E on a marked room or zone (no outline being drawn): what it is, and change or remove it.
            if (Corners.Count == 0 && !GameUi.Typing && !Ui.Windows.AnyOpen && ZInput.GetButtonDown("Use") && Player.m_localPlayer is Player p && Ghost(p) is GameObject g)
            {
                Vector3 at = g.transform.position;
                Colony c = Colony.Nearest(at, Renown.ZoneReach + 20);
                if (c == null) return;
                Room room = c.RoomAt(at + Vector3.up * 0.5f);
                Zone zone = room == null ? c.R.Zones.Where(z => Zones.Inside(z, at)).OrderBy(z => Geometry.Area(z.Xz)).FirstOrDefault() : null;
                if (room != null || zone != null) Ui.RoomEditor.Open(c, room, zone);
                else p.Message(MessageHud.MessageType.Center, "No room or zone marked here.");
            }
        }
        private static string Sig(Colony c)
        {
            var sb = new System.Text.StringBuilder(c.Id);
            foreach (Room r in c.R.Rooms) sb.Append('|').Append(r.Id).Append(r.Type).Append(r.Ok).Append(r.Grade).Append(r.OpenEdges).Append(r.Floor).Append(string.Join(",", r.Xz));
            foreach (Zone z in c.R.Zones) sb.Append('|').Append(z.Id).Append(z.Type).Append(string.Join(",", z.Xz));
            return sb.ToString();
        }
        public static void Clear() { Corners.Clear(); Draw(); }
        public const float RoundRadius = 15f;
        public static List<float> Circle(Vector3 at, float r)
        {
            var xz = new List<float>();
            for (int i = 0; i < 16; i++) { float a = i * Mathf.PI * 2 / 16; xz.Add((float)System.Math.Round(at.x + Mathf.Sin(a) * r, 2)); xz.Add((float)System.Math.Round(at.z + Mathf.Cos(a) * r, 2)); }
            return xz;
        }
        public static void Stop() { Clear(); if (_line != null) Object.Destroy(_line.gameObject); _line = null; }

        private static void Mark(Player p)
        {
            GameObject ghost = Ghost(p);
            if (ghost == null) return;
            Vector3 at = ghost.transform.position;
            Colony c = Colony.Nearest(at, Renown.ZoneReach + 20);
            if (c == null) { p.Message(MessageHud.MessageType.Center, "Rooms and zones belong to a colony: raise High-seat pillars first."); return; }
            // Ctrl+Shift+click on a room's floor: the room found from its walls in one go.
            if (Corners.Count == 0 && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                RoomFind.Result found = RoomFind.Find(at);
                if (found.Xz == null)
                {
                    p.Message(MessageHud.MessageType.Center, $"No room found here: {found.Why}." + (found.Leak != null ? " The red post is where it got out." : ""));
                    if (found.Leak is Vector3 leak) { foreach (GameObject g in _posts) if (g != null) Object.Destroy(g); _posts.Clear(); _posts.Add(Overlay_.Post(leak, new Color(1f, 0.25f, 0.2f, 0.95f), 4f)); _leakUntil = Time.time + 20f; }
                    return;
                }
                p.Message(MessageHud.MessageType.TopLeft, $"Found a room of {Geometry.Area(found.Xz):0} m², {found.Doors} door{(found.Doors == 1 ? "" : "s")}: what is it?");
                Ui.RoomPicker.Open(c, found.Xz, found.Floor);
                return;
            }
            // The same spot twice: a round work zone about it (a woodlot, hunting grounds: work that has a middle more than edges).
            if (Corners.Count == 1 && Utils.DistanceXZ(at, Corners[0]) < 1.2f)
            {
                Ui.RoomPicker.Open(c, Circle(Corners[0], RoundRadius), Corners[0].y, null, zonesOnly: true);
                return;
            }
            if (Corners.Count >= 3 && Utils.DistanceXZ(at, Corners[0]) < 1.2f)
            {
                var xz = new List<float>(); foreach (Vector3 v in Corners) { xz.Add(v.x); xz.Add(v.z); }
                if (!Geometry.Simple(xz)) { p.Message(MessageHud.MessageType.Center, "The outline crosses itself: Backspace to take corners back."); return; }
                float floor = Corners.Min(v => v.y);
                Ui.RoomPicker.Open(c, xz, floor);
                return;
            }
            if (Corners.Count >= 24) { p.Message(MessageHud.MessageType.Center, "That's enough corners: click the first one to close it."); return; }
            Corners.Add(at);
            p.Message(MessageHud.MessageType.TopLeft, Corners.Count < 3 ? $"Corner {Corners.Count}" + (Corners.Count == 1 ? $": click the next corner, or here again for a round work zone {RoundRadius * 2:0} m across" : "") : $"Corner {Corners.Count}: click the first corner to close it");
            Draw();
        }

        private static void Draw()
        {
            foreach (GameObject g in _posts) if (g != null) Object.Destroy(g);
            _posts.Clear();
            if (Corners.Count == 0) { if (_line != null) _line.gameObject.SetActive(false); return; }
            if (_line == null) _line = Overlay_.NewLine("JarldomDesignate", new Color(1f, 0.8f, 0.3f, 0.9f), 0.08f);
            _line.gameObject.SetActive(true);
            _line.positionCount = Corners.Count;
            _line.SetPositions(Corners.Select(v => v + Vector3.up * 0.15f).ToArray());
            foreach (Vector3 v in Corners) _posts.Add(Overlay_.Post(v, new Color(1f, 0.8f, 0.3f, 0.9f), 2.5f));
        }

        // A click with the marker chosen marks a corner instead of building anything (and so costs nothing).
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class Click
        {
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                if (piece == null) return true;
                if (piece.gameObject.name == Pieces.DesignateName) { __result = false; if (__instance == Player.m_localPlayer) Plugin.Guard(() => Mark(__instance)); return false; }
                // Our pieces only where the host knows them (a host without Jarldom deletes them, and their cost, as soon as it loads them).
                if (piece.gameObject.name.StartsWith("Jarldom") && !Net.HostHasJarldom) { __instance.Message(MessageHud.MessageType.Center, "This world's host doesn't have Jarldom: it would delete this."); __result = false; return false; }
                if (piece.gameObject.name == Pieces.PillarsName)
                {
                    GameObject ghost = Ghost(__instance);
                    Vector3 at = ghost != null ? ghost.transform.position : __instance.transform.position;
                    Colony near = Colony.Nearest(at, Renown.MinApart);
                    if (near != null) { __instance.Message(MessageHud.MessageType.Center, $"{near.R.Name} is {Mathf.RoundToInt(Utils.DistanceXZ(near.Position, at))} m away: colonies stand {Renown.MinApart} m apart."); __result = false; return false; }
                    if (__instance == Player.m_localPlayer) Relic.Placing(__instance, at); // (carrying a colony's heart: these pillars take it)
                }
                return true;
            }
        }
    }

    // With the hammer out: the colony's rooms and zones drawn on the ground, gaps in walls marked red.
    internal static class Overlay_
    {
        private static readonly List<GameObject> Drawn = new List<GameObject>();
        private static string _shown = ""; private static float _next;
        private static Material _mat;
        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            Player p = Player.m_localPlayer;
            bool show = p != null && p.InPlaceMode() && Plugin.Instance.Overlay.Value;
            Colony c = show ? Colony.Nearest(p.transform.position, Renown.ZoneReach + 40) : null;
            string sig = c == null ? "" : Sig(c); // (what's drawn, not the colony's every save: villagers' doings change that by the second)
            if (sig == _shown) return;
            Clear();
            _shown = sig; // (after Clear, which forgets what was shown)
            if (c == null) Strays(); // (put away: whatever another copy of the mod left drawn goes too)
            if (c == null) return;
            foreach (Room r in c.R.Rooms)
            {
                Color col = r.Ok ? new Color(0.6f, 0.9f, 0.55f, 0.8f) : new Color(1f, 0.55f, 0.35f, 0.8f);
                var open = new HashSet<int>(r.OpenEdges.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out int i) ? i : -1));
                for (int i = 0; i < r.Corners; i++)
                {
                    var (ax, az) = r.Corner(i); var (bx, bz) = r.Corner((i + 1) % r.Corners);
                    LineRenderer l = NewLine("JarldomRoomEdge", open.Contains(i) ? new Color(1f, 0.15f, 0.1f, 0.95f) : col, open.Contains(i) ? 0.14f : 0.07f);
                    l.positionCount = 2; l.SetPositions(new[] { new Vector3(ax, r.Floor + 0.12f, az), new Vector3(bx, r.Floor + 0.12f, bz) });
                    Drawn.Add(l.gameObject);
                }
                var (cx, cz) = Geometry.Centre(r.Xz);
                Drawn.Add(Label(new Vector3(cx, r.Floor + 2.2f, cz), $"{Tables.Of(r.Type).Name}{(r.Ok ? " " + r.Grade : "")}", col));
            }
            foreach (Zone z in c.R.Zones)
            {
                LineRenderer l = NewLine("JarldomZoneEdge", new Color(0.55f, 0.75f, 1f, 0.7f), 0.06f);
                // Following the ground (as a workbench's circle does): a point every half metre along each edge, on the terrain.
                var pts = new List<Vector3>();
                for (int i = 0; i < z.Corners; i++)
                {
                    var (ax, az) = z.Corner(i); var (bx, bz) = z.Corner((i + 1) % z.Corners);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) / 0.5f));
                    for (int k = 0; k < steps; k++)
                    {
                        float t = (float)k / steps, x = Mathf.Lerp(ax, bx, t), zz = Mathf.Lerp(az, bz, t);
                        pts.Add(new Vector3(x, ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0, zz)) + 0.1f, zz));
                    }
                }
                l.loop = true; l.positionCount = pts.Count;
                l.SetPositions(pts.ToArray());
                Drawn.Add(l.gameObject);
                Vector3 centre = Zones.Centre(z); centre.y = ZoneSystem.instance.GetGroundHeight(centre) + 2f;
                Drawn.Add(Label(centre, Tables.Of(z.Type).Name, new Color(0.55f, 0.75f, 1f, 0.9f)));
            }
        }
        private static string Sig(Colony c)
        {
            var sb = new System.Text.StringBuilder(c.Id);
            foreach (Room r in c.R.Rooms) sb.Append('|').Append(r.Id).Append(r.Type).Append(r.Ok).Append(r.Grade).Append(r.OpenEdges).Append(r.Floor).Append(string.Join(",", r.Xz));
            foreach (Zone z in c.R.Zones) sb.Append('|').Append(z.Id).Append(z.Type).Append(string.Join(",", z.Xz));
            return sb.ToString();
        }
        public static void Clear() { foreach (GameObject g in Drawn) if (g != null) Object.Destroy(g); Drawn.Clear(); _shown = ""; }
        public static void Stop() { Clear(); if (_root != null) Object.Destroy(_root); _root = null; }

        // Everything drawn hangs under one object, so a reloaded copy of the mod can sweep away what the old copy left on screen.
        private const string RootName = "JarldomOverlay";
        private static GameObject _root;
        public static Transform Root { get { if (_root == null) _root = new GameObject(RootName); return _root.transform; } }
        public static void SweepOld()
        {
            Strays();
            // (once, at load: copies before 0.1.0's overlay root drew loose lines and labels)
            foreach (LineRenderer l in Object.FindObjectsOfType<LineRenderer>()) if (l != null && l.name.StartsWith("Jarldom") && l.transform.parent == null) Object.Destroy(l.gameObject);
            foreach (TMPro.TextMeshPro t in Object.FindObjectsOfType<TMPro.TextMeshPro>()) if (t != null && t.name == "JarldomRoomLabel" && t.transform.parent == null) Object.Destroy(t.gameObject);
        }
        // Every overlay root but this copy's (a reloaded copy's, each one: GameObject.Find only ever finds the first).
        public static int Strays(bool remove = true)
        {
            int n = 0;
            for (int k = 0; k < UnityEngine.SceneManagement.SceneManager.sceneCount; k++)
                foreach (GameObject g in UnityEngine.SceneManagement.SceneManager.GetSceneAt(k).GetRootGameObjects())
                    if (g != null && g.name == RootName && g != _root) { n++; if (remove) Object.Destroy(g); }
            return n;
        }
        public static string State() => $"shown {(_shown == "" ? "nothing" : "a colony")}, {Drawn.Count} drawn, own root {(_root == null ? "none" : _root.transform.childCount + " children")}, strays {Strays(false)}";
        public static LineRenderer NewLine(string name, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            LineRenderer l = go.AddComponent<LineRenderer>();
            l.material = Mat(); l.startColor = l.endColor = color; l.startWidth = l.endWidth = width;
            l.useWorldSpace = true; l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; l.receiveShadows = false;
            return l;
        }
        public static GameObject Post(Vector3 at, Color color, float height)
        {
            LineRenderer l = NewLine("JarldomPost", color, 0.1f);
            l.positionCount = 2; l.SetPositions(new[] { at, at + Vector3.up * height });
            return l.gameObject;
        }
        private static Material Mat()
        {
            if (_mat != null) return _mat;
            // A game material that draws plain colour (the sprite shader the game's own line effects use), never Shader.Find.
            Shader s = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(x => x.name == "Sprites/Default") ?? Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(x => x.name.Contains("Unlit"));
            _mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            return _mat;
        }
        internal static GameObject Label(Vector3 at, string text, Color color, float size = 4f)
        {
            var go = new GameObject("JarldomRoomLabel");
            go.SetActive(false); // (the font given before the text wakes: it looks for a default font otherwise, and warns there's none)
            go.transform.SetParent(Root, false);
            go.transform.position = at;
            var t = go.AddComponent<TMPro.TextMeshPro>();
            TMPro.TMP_FontAsset font = GameUi.Norse ?? WorldFont();
            if (font != null) t.font = font;
            t.text = text; t.fontSize = size; t.color = color; t.alignment = TMPro.TextAlignmentOptions.Center;
            go.AddComponent<Billboard>();
            go.SetActive(true);
            return go;
        }
        private static TMPro.TMP_FontAsset _font;
        // The game's Norse font for words in the world (once: not on a timer), whether or not a window has been opened yet.
        private static TMPro.TMP_FontAsset WorldFont()
        {
            if (_font != null) return _font;
            var all = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>();
            _font = all.FirstOrDefault(f => f.name.StartsWith("Norse")) ?? all.FirstOrDefault(f => f.name.Contains("Averia")) ?? all.FirstOrDefault();
            return _font;
        }
        private sealed class Billboard : MonoBehaviour
        {
            private void LateUpdate() { Camera cam = Utils.GetMainCamera(); if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position); }
        }
    }
}
