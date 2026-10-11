using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Marking rooms and zones: with the hammer's "Mark rooms and zones" chosen, a click marks a corner where the building ghost stands;
    // clicking the first corner again closes the outline and asks what it is. Backspace takes back the last corner.
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
        public static void Tick()
        {
            if (!Active) { if (Corners.Count > 0 && !Ui.RoomPicker.IsOpen) Clear(); return; }
            if (Input.GetKeyDown(KeyCode.Backspace) && Corners.Count > 0 && !GameUi.Typing) { Corners.RemoveAt(Corners.Count - 1); Draw(); }
        }
        public static void Clear() { Corners.Clear(); Draw(); }
        public static void Stop() { Clear(); if (_line != null) Object.Destroy(_line.gameObject); _line = null; }

        private static void Mark(Player p)
        {
            GameObject ghost = Ghost(p);
            if (ghost == null) return;
            Vector3 at = ghost.transform.position;
            Colony c = Colony.Nearest(at, Renown.ZoneReach + 20);
            if (c == null) { p.Message(MessageHud.MessageType.Center, "Rooms and zones belong to a colony: raise High-seat pillars first."); return; }
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
            p.Message(MessageHud.MessageType.TopLeft, Corners.Count < 3 ? $"Corner {Corners.Count}" : $"Corner {Corners.Count}: click the first corner to close it");
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
            string sig = c == null ? "" : c.Id + c.Z.DataRevision;
            if (sig == _shown) return;
            _shown = sig;
            Clear();
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
        public static void Clear() { foreach (GameObject g in Drawn) if (g != null) Object.Destroy(g); Drawn.Clear(); _shown = ""; }

        public static LineRenderer NewLine(string name, Color color, float width)
        {
            var go = new GameObject(name);
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
        private static GameObject Label(Vector3 at, string text, Color color)
        {
            var go = new GameObject("JarldomRoomLabel");
            go.transform.position = at;
            var t = go.AddComponent<TMPro.TextMeshPro>();
            t.text = text; t.fontSize = 4; t.color = color; t.alignment = TMPro.TextAlignmentOptions.Center;
            TMPro.TMP_FontAsset font = GameUi.Norse ?? WorldFont();
            if (font != null) t.font = font;
            go.AddComponent<Billboard>();
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
