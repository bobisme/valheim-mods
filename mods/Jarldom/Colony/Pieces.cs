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
            ("odin", "darkwood_raven", 0.45f, new Vector3(0, 1.0f, -0.65f), new Vector3(0, 0, 0)),
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
            GameObject pole = Assets.Find("stave_pole_4m"), band = Assets.Find("stave_deco_pole_2m"), raven = Assets.Find("darkwood_raven");
            if (pole == null) return;
            GameObject go = Base("stave_pole_4m", PillarsName);
            if (go == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            foreach (float x in new[] { -1.2f, 1.2f })
            {
                var col = new GameObject("pillar"); col.transform.SetParent(look.transform, false); col.transform.localPosition = new Vector3(x, 0, 0);
                GameObject p = Assets.Model(pole, col.transform, new Vector3(0, 2.0f, 0), Quaternion.identity, 0.85f);
                Bounds pb = Assets.MeshBounds(col.transform, p); p.transform.localPosition += Vector3.up * -pb.min.y;
                if (band != null) { GameObject b = Assets.Model(band, col.transform, new Vector3(0, 1.8f, -0.32f), Quaternion.identity, 0.9f); }
                if (raven != null) Assets.Model(raven, col.transform, new Vector3(0, pb.size.y - 0.1f, -0.15f), Quaternion.Euler(0, x < 0 ? 200 : 160, 0), 0.38f);
            }
            Finish(go, look, "High-seat pillars",
                "Raise them to found a hall, as the settlers of old threw their high-seat pillars ashore and built where they landed. Odin sends the fallen to a hall worth noticing. " +
                "Use them to open the colony: its rank, its renown, its people, and the newcomers who wait for your word.", Piece.PieceCategory.Misc,
                ("FineWood", 2), ("Wood", 4), ("Resin", 1));
            go.AddComponent<HighSeat>();
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
                e.transform.localPosition += new Vector3(offset.x - eb.center.x, sb.size.y - eb.min.y + 0.02f, offset.z - eb.center.z);
            }
            GodInfo info = Tables.God(god);
            Finish(go, look, $"Altar of {info.Name}",
                $"An altar to {info.Name}. Designate a {(god == "odin" ? "shrine" : "temple")} room round it. {info.Name} wants {info.Offerings}; the blessing: {info.Blessing}. Use it to make an offering from your bag.",
                Piece.PieceCategory.Misc, ("Stone", 12), (god == "odin" ? "TrophyEikthyr" : "Coins", god == "odin" ? 0 : 10), ("Resin", 2));
            go.AddComponent<Altar>().God = god;
        }

        private static void Horn()
        {
            GameObject pole = Assets.Find("wood_pole_log"), horn = Assets.Find("Morkhalla_StatuePieceHorn");
            GameObject go = Base("wood_pole_log", HornName);
            if (go == null || pole == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            GameObject p = Assets.Model(pole, look.transform, Vector3.zero, Quaternion.identity, 1f);
            p.transform.localScale = new Vector3(1, 0.85f, 1);
            Bounds pb = Assets.MeshBounds(look.transform, p); p.transform.localPosition += Vector3.up * -pb.min.y;
            if (horn != null)
            {
                GameObject h = Assets.Model(horn, look.transform, Vector3.zero, Quaternion.Euler(0, 90, 0), 0.25f);
                Bounds hb = Assets.MeshBounds(look.transform, h);
                h.transform.localPosition += new Vector3(-hb.center.x, pb.size.y * 0.85f - hb.center.y, -hb.center.z + 0.15f);
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
        {
            int layer = LayerMask.NameToLayer("piece");
            Bounds raw = Assets.MeshBounds(go.transform, look);
            look.transform.localPosition += Vector3.up * -raw.min.y;
            foreach (Transform t in look.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            Bounds b = Assets.MeshBounds(go.transform, look);
            // A convex box round the look, reaching a little below the base (support needs the touch; the ghost needs a convex collider).
            var box = new GameObject("collider") { layer = layer }; box.transform.SetParent(go.transform, false);
            BoxCollider collider = box.AddComponent<BoxCollider>(); collider.center = b.center + Vector3.down * 0.025f; collider.size = b.size + Vector3.up * 0.05f;
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
