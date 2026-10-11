using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // The mod's building pieces, each a copy of a vanilla piece's working parts (network view, piece, wear and tear) wearing a look made
    // from other game prefabs: the High-Seat Pillars, the gods' altars, the war horn, the gilded pieces and the funeral pyre.
    internal static class Pieces
    {
        public const string PillarsName = "JarldomHighSeatPillars", HornName = "JarldomWarHorn", PyreName = "JarldomPyre", DesignateName = "JarldomDesignate";
        public const string AltarPrefix = "JarldomAltar_", GildedSeatName = "JarldomGildedSeat", GildedBannerName = "JarldomGildedBanner";
        public static readonly int PillarsHash = PillarsName.GetStableHashCode();
        private static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        public static IEnumerable<GameObject> All => Made.Values.Where(v => v != null);
        public static GameObject Get(string name) => Made.TryGetValue(name, out GameObject go) ? go : null;

        // Each god's altar: a stone plinth with the god's sign on it.
        public static readonly (string god, string emblem, float scale, Vector3 offset, Vector3 turn)[] Altars =
        {
            ("odin", "darkwood_raven", 0.4f, new Vector3(0, 1.0f, 99f), new Vector3(0, 0, 0)), // (z 99: the carving's far end, where the raven sits, over the plinth)
            ("thor", "StatueThor", 0.32f, new Vector3(0, 1.0f, 0), Vector3.zero),
            ("freyja", "StatueFreya", 0.32f, new Vector3(0, 1.0f, 0), Vector3.zero),
            ("njord", "Fish1", 1.6f, new Vector3(0, 1.15f, 0), new Vector3(0, 0, 90)),
            ("frigg", "LinenCloth", 2.2f, new Vector3(0, 1.08f, 0), Vector3.zero),
            ("tyr", "ShieldBronzeBuckler", 1.2f, new Vector3(0, 1.45f, 0), new Vector3(0, 0, 0)),
            ("ullr", "BowFineWood", 1.1f, new Vector3(0, 1.4f, 0), new Vector3(0, 0, 0)),
            ("hel", "TrophySkeleton", 1.4f, new Vector3(0, 1.1f, 0), Vector3.zero),
        };

        public static void Ensure(ZNetScene scene)
        {
            if (Get(PillarsName) == null) Pillars();
            foreach (var a in Altars) if (Get(AltarPrefix + a.god) == null) Altar(a.god, a.emblem, a.scale, a.offset, Quaternion.Euler(a.turn));
            if (Get(HornName) == null) Horn();
            if (Get(GildedSeatName) == null) Gilded(GildedSeatName, "piece_throne02", "Gilded high seat", "A high seat for the jarl's hall, its carving picked out in gold: the hall's pride and a great hall's grade S.", 60, ("FineWood", 10), ("Coins", 100));
            if (Get(GildedBannerName) == null) Gilded(GildedBannerName, "piece_banner02", "Gilded banner", "A banner worked with gold thread. Halls and temples grade higher for it.", 0, ("LinenCloth", 2), ("Coins", 20), ("Wood", 2));
            if (Get(PyreName) == null) Pyre();
            if (Get(DesignateName) == null) Designator();
        }

        // ---- the pieces ----
        private static void Pillars()
        {
            GameObject shaftSrc = Assets.Find("darkwood_pole4"), stone = Assets.Find("stone_pillar"), raven = Assets.Find("darkwood_raven"), wolf = Assets.Find("darkwood_wolf");
            if (shaftSrc == null) return;
            GameObject go = Base("stave_pole_4m", PillarsName);
            if (go == null) return;
            Material wood = shaftSrc.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.HasProperty("_Color"));
            Material iron = Plain(wood, new Color(0.16f, 0.155f, 0.15f));
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            const float apart = 1.3f;                                   // each pillar's centre from the middle
            var bodies = new List<GameObject>();
            // The pair is mirrored about x = 0: s is the side (-1 left, +1 right), and every turn about y is mirrored by its sign.
            foreach (float s in new[] { -1f, 1f })
            {
                var col = new GameObject(s < 0 ? "pillar_left" : "pillar_right"); col.transform.SetParent(look.transform, false); col.transform.localPosition = new Vector3(s * apart, 0, 0);
                Quaternion Turn(float yaw) => Quaternion.Euler(0, -s * yaw, 0); // yaw for the left pillar (0 = facing the front, -z)
                var body = new GameObject("body"); body.transform.SetParent(col.transform, false); bodies.Add(body);
                // A stone plinth at the foot.
                float top = 0;
                if (stone != null)
                {
                    GameObject plinth = Assets.Model(stone, body.transform, Vector3.zero, Turn(s < 0 ? 0 : 180), 1f);
                    plinth.transform.localScale = new Vector3(0.68f, 0.3f, 0.68f);
                    Bounds b = Assets.MeshBounds(body.transform, plinth); plinth.transform.localPosition += Vector3.up * -b.min.y;
                    top = b.size.y;
                }
                // The carved darkwood shaft.
                GameObject shaft = Assets.Model(shaftSrc, body.transform, Vector3.zero, Turn(s < 0 ? 0 : 180), 1f);
                shaft.transform.localScale = new Vector3(1f, 0.82f, 1f);
                Bounds sb = Assets.MeshBounds(body.transform, shaft); shaft.transform.localPosition += Vector3.up * (top - sb.min.y);
                float foot = top, head = top + sb.size.y, width = Mathf.Max(sb.size.x, sb.size.z);
                // Iron bands: at the foot, two along the shaft, and a broad one under the head.
                foreach ((float at, float h) in new[] { (0.06f, 0.1f), (0.36f, 0.06f), (0.64f, 0.06f), (0.93f, 0.12f) })
                    Block(body.transform, iron, new Vector3(0, Mathf.Lerp(foot, head, at), 0), new Vector3(width + 0.06f, h, width + 0.06f));
                // A darkwood capital: a squat, wider block of the same carving.
                GameObject cap = Assets.Model(shaftSrc, body.transform, Vector3.zero, Turn(s < 0 ? 0 : 180), 1f);
                cap.transform.localScale = new Vector3(1.45f, 0.05f, 1.45f);
                Bounds cb = Assets.MeshBounds(body.transform, cap); cap.transform.localPosition += Vector3.up * (head - cb.min.y);
                head += cb.size.y;
                Block(body.transform, iron, new Vector3(0, head + 0.02f, 0), new Vector3(width * 1.45f + 0.04f, 0.04f, width * 1.45f + 0.04f));
                head += 0.04f;
                // Geri and Freki: a carved wolf's head on each pillar's face under the capital, looking out to the front and a little away from the middle.
                if (wolf != null)
                {
                    GameObject w = Assets.Model(wolf, col.transform, Vector3.zero, Turn(195), 0.32f);
                    Bounds wb = Assets.MeshBounds(col.transform, w);
                    w.transform.localPosition += new Vector3(0, head - 0.88f - wb.min.y, 0);
                }
                // Huginn and Muninn: each raven's branch reaches in from its pillar's top, the two birds facing each other over the way between.
                if (raven != null)
                {
                    GameObject r = Assets.Model(raven, col.transform, Vector3.zero, Turn(90), 0.3f);
                    Bounds rb = Assets.MeshBounds(col.transform, r);
                    r.transform.localPosition += new Vector3(0, head - 0.12f - rb.min.y, 0);
                }
            }
            Finish(go, look, "High-seat pillars",
                "Raise them to found a hall, as the settlers of old threw their high-seat pillars ashore and built where they landed. Odin sends the fallen to a hall worth noticing. " +
                "Use them to open the colony: its rank, its renown, its people, and the newcomers who wait for your word.", Piece.PieceCategory.Misc, false,
                ("FineWood", 2), ("Wood", 4), ("Resin", 1));
            // One box per pillar (plinth, shaft and capital; the carvings up top stay out of it), so the way between them is open.
            int layer = LayerMask.NameToLayer("piece");
            foreach (GameObject body in bodies)
            {
                Bounds b = Assets.MeshBounds(go.transform, body);
                var box = new GameObject("collider_" + body.transform.parent.name) { layer = layer }; box.transform.SetParent(go.transform, false);
                BoxCollider c = box.AddComponent<BoxCollider>(); c.center = b.center + Vector3.down * 0.025f; c.size = b.size + Vector3.up * 0.05f;
            }
            go.AddComponent<HighSeat>();
        }
        // A plain block (a Unity cube without its collider) for bands and fittings.
        private static void Block(Transform parent, Material material, Vector3 centre, Vector3 size)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(b.GetComponent<Collider>());
            b.name = "band"; b.transform.SetParent(parent, false); b.transform.localPosition = centre; b.transform.localScale = size;
            b.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void Altar(string god, string emblemName, float scale, Vector3 offset, Quaternion turn)
        {
            GameObject plinth = Assets.Find("stone_pillar"), emblem = Assets.Find(emblemName);
            string name = AltarPrefix + god;
            GameObject go = Base("stone_pillar", name);
            if (go == null || plinth == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            GameObject stone = Assets.Model(plinth, look.transform, Vector3.zero, Quaternion.identity, 1f);
            stone.transform.localScale = new Vector3(0.75f, 0.5f, 0.75f);
            Bounds sb = Assets.MeshBounds(look.transform, stone); stone.transform.localPosition += Vector3.up * -sb.min.y;
            if (emblem != null)
            {
                GameObject e = Assets.Model(emblem, look.transform, Vector3.zero, turn, scale, r => !(r.name.ToLower().Contains("broken")));
                Bounds eb = Assets.MeshBounds(look.transform, e);
                float dz = offset.z > 50 ? -(eb.max.z - 0.35f) : offset.z - eb.center.z;
                e.transform.localPosition += new Vector3(offset.x - eb.center.x, sb.size.y - eb.min.y + 0.02f, dz);
            }
            GodInfo info = Tables.God(god);
            Finish(go, look, $"Altar of {info.Name}",
                $"An altar to {info.Name}. Designate a {(god == "odin" ? "shrine" : "temple")} room round it. {info.Name} wants {info.Offerings}; the blessing: {info.Blessing}. Use it to make an offering from your bag.",
                Piece.PieceCategory.Misc, ("Stone", 12), (god == "odin" ? "TrophyEikthyr" : "Coins", god == "odin" ? 0 : 10), ("Resin", 2));
            go.AddComponent<Altar>().God = god;
        }

        private static void Horn()
        {
            GameObject pole = Assets.Find("wood_pole_log"), horn = Assets.Find("TrophyDeer");
            GameObject go = Base("wood_pole_log", HornName);
            if (go == null || pole == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            GameObject p = Assets.Model(pole, look.transform, Vector3.zero, Quaternion.identity, 1f);
            p.transform.localScale = new Vector3(1, 0.85f, 1);
            Bounds pb = Assets.MeshBounds(look.transform, p); p.transform.localPosition += Vector3.up * -pb.min.y;
            // A great curved horn of bone, banded in iron, hung across the post: tapering segments along an arc, in the antler's own material.
            Material wood = pole.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.HasProperty("_Color"));
            Material bone = Plain(wood, new Color(0.62f, 0.55f, 0.42f)), iron = Plain(wood, new Color(0.22f, 0.21f, 0.2f));
            var hornRoot = new GameObject("horn"); hornRoot.transform.SetParent(look.transform, false);
            hornRoot.transform.localPosition = new Vector3(-0.55f, pb.size.y * 0.78f, -0.32f);
            const int segments = 16;
            Vector3 Point(float t) { float ang = Mathf.Lerp(-15f, 80f, t) * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(ang) * 0.7f + t * 0.5f, (1 - Mathf.Cos(ang)) * 0.55f, 0); }
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments, t1 = (i + 1) / (float)segments;
                Vector3 p0 = Point(t0), p1 = Point(t1), dir = p1 - p0;
                float width = Mathf.Lerp(0.045f, 0.2f, t1 * t1);                  // narrow mouthpiece, wide bell
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(seg.GetComponent<Collider>());
                seg.transform.SetParent(hornRoot.transform, false);
                seg.transform.localPosition = (p0 + p1) / 2;
                seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                seg.transform.localScale = new Vector3(width, dir.magnitude * 0.55f, width);   // (a cylinder is 2 long: a little overlap)
                seg.GetComponent<MeshRenderer>().sharedMaterial = i == 3 || i == 11 || i == segments - 1 ? iron : bone;
            }
            Finish(go, look, "War horn", "Sounds by itself when raiders come near the colony, or blow it yourself: those who can't fight run to the great hall and shut the door, guards go to their posts, the rest fall back to the pillars. Blow it again to sound the all-clear.",
                Piece.PieceCategory.Misc, ("Wood", 4), ("TrophyDeer", 1), ("LeatherScraps", 2));
            go.AddComponent<WarHorn>();
        }

        private static void Gilded(string name, string source, string title, string text, int comfort, params (string, int)[] cost)
        {
            GameObject src = Assets.Find(source);
            if (src == null) return;
            GameObject go = Object.Instantiate(src, Assets.Holder);
            go.name = name;
            Assets.Tint(go, new Color(1.25f, 1.05f, 0.55f), new Color(0.18f, 0.12f, 0.02f));
            Piece piece = go.GetComponent<Piece>();
            piece.m_name = title; piece.m_description = text;
            if (comfort > 0) piece.m_comfort = Math.Max(piece.m_comfort, 3);
            piece.m_resources = Assets.Needs(cost);
            piece.m_category = Piece.PieceCategory.Furniture;
            Sprite icon = Assets.Icon(go, name, Quaternion.Euler(0, -30, 0));
            if (icon != null) piece.m_icon = icon;
            Made[name] = go;
        }

        private static void Pyre()
        {
            GameObject logs = Assets.Find("wood_pole_log"), fire = Assets.Find("fire_pit");
            GameObject go = Base("wood_pole_log", PyreName);
            if (go == null || logs == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            for (int layer = 0; layer < 4; layer++)
                for (int i = 0; i < 4; i++)
                {
                    bool across = layer % 2 == 1;
                    Vector3 at = across ? new Vector3(-0.9f + i * 0.6f, 0.15f + layer * 0.28f, 0) : new Vector3(0, 0.15f + layer * 0.28f, -0.9f + i * 0.6f);
                    GameObject l = Assets.Model(logs, look.transform, at, Quaternion.Euler(across ? 90 : 0, 0, across ? 0 : 90), 1f);
                    l.transform.localScale = new Vector3(1.3f, 1.2f, 1.3f);
                }
            Finish(go, look, "Funeral pyre", "Where a worthy soul leaves for Valhalla. Put their grave goods on it, choose who goes from the colony window, and light it at dusk: everything burns, and Odin takes note.",
                Piece.PieceCategory.Misc, ("Wood", 20), ("FineWood", 10), ("Resin", 10));
            Container goods = go.AddComponent<Container>();
            goods.m_name = "Grave goods"; goods.m_width = 5; goods.m_height = 2; goods.m_privacy = Container.PrivacySetting.Public;
            Container chest = Assets.Find("piece_chest_wood")?.GetComponent<Container>();
            if (chest != null) { goods.m_bkg = chest.m_bkg; goods.m_openEffects = chest.m_openEffects; goods.m_closeEffects = chest.m_closeEffects; }
            go.AddComponent<Pyre>();
            if (fire != null)
            {
                GameObject flames = fire.GetComponent<Fireplace>()?.m_enabledObject;
                if (flames != null)
                {
                    var burn = new GameObject("PyreFire"); burn.transform.SetParent(go.transform, false); burn.SetActive(false);
                    for (int i = 0; i < 4; i++) { GameObject f = Object.Instantiate(flames, burn.transform, false); f.SetActive(true); f.transform.localPosition = new Vector3((i % 2 - 0.5f) * 1.0f, 1.1f, (i / 2 - 0.5f) * 1.0f); f.transform.localScale = Vector3.one * 1.8f; }
                }
            }
        }

        // Not built: choosing it in the hammer turns clicks into room and zone corners (Designate).
        private static void Designator()
        {
            GameObject go = Base("wood_pole", DesignateName);
            if (go == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            GameObject flag = Assets.Find("piece_banner01");
            if (flag != null) Assets.Model(flag, look.transform, Vector3.zero, Quaternion.identity, 0.6f);
            Finish(go, look, "Mark rooms and zones", "Not built. With this chosen, click the corners of a room or a work zone on the ground (click the first corner again to close it), then choose what it is. Backspace removes the last corner, right click with nothing marked forgets a room under your aim.",
                Piece.PieceCategory.Misc, ("Wood", 1));
            Piece marker = go.GetComponent<Piece>();
            marker.m_resources = new Piece.Requirement[0]; // free: nothing is ever built (Designate refuses placement)
            marker.m_craftingStation = null;               // and usable anywhere, not only by a workbench
        }

        // ---- plumbing ----
        // A game material in a plain colour (its texture taken away): for small made-up parts.
        private static Material Plain(Material from, Color color)
        {
            if (from == null) return null;
            var m = new Material(from) { name = "JarldomPlain", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.color = color;
            Assets.Owned.Add(m);
            return m;
        }
        private static GameObject Base(string baseName, string name)
        {
            GameObject basePrefab = Assets.Find(baseName);
            if (basePrefab == null) { Plugin.Warn($"Jarldom: cannot make {name} from {baseName}"); return null; }
            GameObject go = Object.Instantiate(basePrefab, Assets.Holder);
            go.name = name;
            foreach (Transform child in go.transform.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject);
            foreach (Collider c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            return go;
        }
        private static void Finish(GameObject go, GameObject look, string title, string text, Piece.PieceCategory category, params (string, int)[] cost)
            => Finish(go, look, title, text, category, true, cost);
        private static void Finish(GameObject go, GameObject look, string title, string text, Piece.PieceCategory category, bool box, params (string, int)[] cost)
        {
            int layer = LayerMask.NameToLayer("piece");
            Bounds raw = Assets.MeshBounds(go.transform, look);
            look.transform.localPosition += Vector3.up * -raw.min.y;
            foreach (Transform t in look.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            Bounds b = Assets.MeshBounds(go.transform, look);
            // A convex box round the look, reaching a little below the base (support needs the touch; the ghost needs a convex collider).
            if (box)
            {
                var holder = new GameObject("collider") { layer = layer }; holder.transform.SetParent(go.transform, false);
                BoxCollider collider = holder.AddComponent<BoxCollider>(); collider.center = b.center + Vector3.down * 0.025f; collider.size = b.size + Vector3.up * 0.05f;
            }
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) { wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_health = Math.Max(wear.m_health, 600); }
            Piece piece = go.GetComponent<Piece>();
            piece.m_name = title; piece.m_description = text; piece.m_category = category;
            piece.m_resources = Assets.Needs(cost.Where(c => c.Item2 > 0).ToArray());
            piece.m_craftingStation = Assets.Find("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_comfort = 0;
            Sprite icon = Assets.Icon(look, go.name, Quaternion.Euler(0, -30, 0));
            if (icon != null) piece.m_icon = icon;
            Made[go.name] = go;
        }

        public static void AddToHammer(ObjectDB db)
        {
            PieceTable table = db.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if (table == null) return;
            var ours = All.ToList(); var names = new HashSet<string>(ours.Select(o => o.name));
            table.m_pieces.RemoveAll(p => p == null || names.Contains(p.name) && !ours.Contains(p)); // an old copy's
            foreach (GameObject piece in ours) if (!table.m_pieces.Contains(piece)) table.m_pieces.Add(piece);
        }
        public static void RemoveFromHammer(ObjectDB db)
        {
            PieceTable table = db.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            table?.m_pieces.RemoveAll(p => p == null || Made.Values.Contains(p));
        }
        public static void Forget() => Made.Clear();
    }
}
