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
        public const string WatchPostName = "JarldomWatchPost", OreClaimName = "JarldomOreClaim", MemorialName = "JarldomMemorial", FuneralKarveName = "JarldomFuneralKarve";
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
            if (Get(WatchPostName) == null) WatchPost();
            if (Get(OreClaimName) == null) OreClaim();
            if (Get(MemorialName) == null) MemorialStone();
            if (Get(FuneralKarveName) == null) FuneralKarve();
            if (Get(GildedSeatName) == null) Gilded(GildedSeatName, "piece_throne02", "Gilded high seat", "A high seat for the jarl's hall, its carving picked out in gold: the hall's pride and a great hall's grade S.", 60, ("FineWood", 10), ("Coins", 100));
            if (Get(GildedBannerName) == null) Gilded(GildedBannerName, "piece_banner02", "Gilded banner", "A banner worked with gold thread. Halls and temples grade higher for it.", 0, ("LinenCloth", 2), ("Coins", 20), ("Wood", 2));
            if (Get(PyreName) == null) Pyre();
            if (Get(DesignateName) == null) Designator();
        }

        // ---- the pieces ----
        // The pillars grow with the colony: a look for each rank, from two lashed logs at a camp to gilded carvings lit by fire at a hold
        // (HighSeat shows the colony's rank's; the prefab, its ghost and its icon are the camp's). The colliders are the same for every look.
        public const int Tiers = 6;
        public static readonly string LookStamp = "stamp_" + DateTime.UtcNow.Ticks;
        private const float Apart = 1.3f; // each pillar's centre from the middle
        // The space they take, the same at every rank (so a hall built round a camp's logs still fits a hold's): each pillar a box this
        // wide and deep and this tall, the colliders exactly that. Every look is made to stand inside it (flames aside).
        public const float Width = 0.8f, Height = 4.2f;
        private static void Pillars()
        {
            GameObject shaftSrc = Assets.Find("darkwood_pole4"), stone = Assets.Find("stone_pillar"), raven = Assets.Find("darkwood_raven"), wolf = Assets.Find("darkwood_wolf"),
                log = Assets.Find("wood_pole_log_4"), pole = Assets.Find("wood_pole2"), beam = Assets.Find("wood_beam"), hide = Assets.Find("rug_deer"), dragon = Assets.Find("wood_dragon1"), brazier = Assets.Find("piece_brazierfloor01");
            if (shaftSrc == null || log == null) return;
            GameObject go = Base("stave_pole_4m", PillarsName);
            if (go == null) return;
            Material wood = shaftSrc.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.HasProperty("_Color"));
            Material iron = Plain(wood, new Color(0.16f, 0.155f, 0.15f)), rope = Plain(wood, new Color(0.45f, 0.36f, 0.22f)), gold = Plain(wood, new Color(1f, 0.76f, 0.26f));
            Material ochre = Plain(wood, new Color(0.55f, 0.1f, 0.06f)), glowGold = Plain(wood, new Color(1f, 0.8f, 0.3f));
            if (glowGold != null && glowGold.HasProperty("_EmissionColor")) { glowGold.EnableKeyword("_EMISSION"); glowGold.SetColor("_EmissionColor", new Color(0.55f, 0.38f, 0.08f)); }
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            new GameObject(LookStamp).transform.SetParent(look.transform, false); // (which build of the looks: standing pillars of an older one are refitted)
            for (int t = 0; t < Tiers; t++)
            {
                var tier = new GameObject("tier" + t); tier.transform.SetParent(look.transform, false);
                float top = 0;
                // The pair is mirrored about x = 0: s is the side (-1 left, +1 right).
                foreach (float s in new[] { -1f, 1f })
                {
                    var col = new GameObject(s < 0 ? "pillar_left" : "pillar_right"); col.transform.SetParent(tier.transform, false); col.transform.localPosition = new Vector3(s * Apart, 0, 0);
                    var body = new GameObject("body"); body.transform.SetParent(col.transform, false);
                    // (each shaft has its god's runes cut into its front: the left Óðinn, the right Þórr; painted red ochre, gilded at a hold)
                    string word = s < 0 ? "odin" : "thor"; Material paint = t >= 5 ? glowGold : ochre;
                    switch (t)
                    {
                        case 0: top = Logs(body.transform, log, rope, word, paint); break;               // camp: two logs, bark on, lashed with rope
                        case 1: top = Poles(body.transform, pole ?? log, iron, word, paint); break;      // homestead: squared poles, a beam and a hide between
                        case 2: top = Footed(body.transform, col.transform, log, stone, iron, dragon, s, word, paint); break; // hamlet: on stone, banded, a beam across
                        default: top = Carved(body.transform, col.transform, shaftSrc, stone, t >= 4 ? wolf : null, raven, t >= 5 ? gold : iron, t >= 5, s, word, paint); break; // village (Huginn and Muninn), jarldom (Geri and Freki), hold
                    }
                    if (t == 5 && brazier != null) Fire(col.transform, brazier, top); // hold: fire burning in each capital
                }
                if ((t == 1 || t == 2) && beam != null) Crossbeam(tier.transform, beam, t == 1 ? hide : null, top);
                if (t > 0) tier.SetActive(false);
                Bounds tb = Assets.MeshBounds(look.transform, tier);
                if (tb.max.y > Height + 0.02f || Mathf.Abs(tb.max.z) > Width / 2 + 0.02f || Mathf.Abs(tb.min.z) > Width / 2 + 0.02f || tb.max.x > Apart + Width / 2 + 0.02f)
                    Plugin.Warn($"Jarldom: the pillars' look {t} goes outside their space: {tb.min} to {tb.max}");
            }
            Finish(go, look, "High-seat pillars",
                "Raise them to found a hall, as the settlers of old threw their high-seat pillars ashore and built where they landed. Odin sends the fallen to a hall worth noticing. " +
                "Use them (E) to hold council: the colony's rank, its renown, its people, and the newcomers who wait for your word. They start as two lashed logs and are raised anew as the colony ranks up.", Piece.PieceCategory.Misc, false,
                ("Wood", 6), ("Resin", 2));
            // One box per pillar, their whole space, so the way between them is open.
            int layer = LayerMask.NameToLayer("piece");
            foreach (float side in new[] { -1f, 1f })
            {
                var box = new GameObject("collider_" + (side < 0 ? "pillar_left" : "pillar_right")) { layer = layer }; box.transform.SetParent(go.transform, false);
                BoxCollider c = box.AddComponent<BoxCollider>(); c.center = new Vector3(side * Apart, Height / 2f - 0.025f, 0); c.size = new Vector3(Width, Height + 0.05f, Width);
            }
            // The way between them: where anyone looks to use them. A trigger, so it's hovered (and E works) but walked through.
            {
                var gap = new GameObject("use_between") { layer = LayerMask.NameToLayer("piece_nonsolid") }; gap.transform.SetParent(go.transform, false);
                BoxCollider g = gap.AddComponent<BoxCollider>(); g.isTrigger = true;
                g.center = new Vector3(0, 1.4f, 0); g.size = new Vector3(2 * Apart - Width, 2.4f, 0.4f);
            }
            go.AddComponent<HighSeat>();
        }
        // A shaft with runes cut into its front (made in Blender from the game's own mesh: Models/carve): the source's material on the
        // wood, the paint in the grooves; null when the mesh isn't there (the plain shaft is used).
        private static GameObject Cut(Transform parent, GameObject source, string name, Material paint)
        {
            Mesh mesh = CarvedMesh(name);
            Material wood = source?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
            if (mesh == null || wood == null) return null;
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { wood, paint ?? wood, wood };
            return go;
        }
        private static readonly Dictionary<string, Mesh> Carvings = new Dictionary<string, Mesh>();
        private static Mesh CarvedMesh(string name, string folder = "carve")
        {
            if (Carvings.TryGetValue(folder + "." + name, out Mesh had) && had != null) return had;
            try
            {
                using (var raw = typeof(Pieces).Assembly.GetManifestResourceStream("Jarldom." + folder + "." + name + ".json.gz"))
                {
                    if (raw == null) return null;
                    using (var gz = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress))
                    using (var text = new System.IO.StreamReader(gz))
                    {
                        var d = Newtonsoft.Json.Linq.JObject.Parse(text.ReadToEnd());
                        float[] v = d["v"].ToObject<float[]>(), n = d["n"].ToObject<float[]>(), uv = d["uv"].ToObject<float[]>();
                        int[][] t = d["t"].ToObject<int[][]>();
                        var mesh = new Mesh { name = "Jarldom_" + name };
                        if (v.Length / 3 > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        var verts = new Vector3[v.Length / 3]; var norms = new Vector3[v.Length / 3]; var uvs = new Vector2[v.Length / 3];
                        for (int i = 0; i < verts.Length; i++) { verts[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]); norms[i] = new Vector3(n[3 * i], n[3 * i + 1], n[3 * i + 2]); uvs[i] = new Vector2(uv[2 * i], uv[2 * i + 1]); }
                        mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.colors = Enumerable.Repeat(Color.white, verts.Length).ToArray(); // (the game's piece shader reads the vertex colour)
                        mesh.subMeshCount = t.Length;
                        for (int k = 0; k < t.Length; k++) mesh.SetTriangles(t[k], k);
                        mesh.RecalculateBounds(); mesh.RecalculateTangents();
                        Carvings[folder + "." + name] = mesh;
                        return mesh;
                    }
                }
            }
            catch (Exception e) { Plugin.Warn("Jarldom: carved shaft " + name + ": " + e.Message); return null; }
        }
        // A model stood on the floor of its parent (its lowest point at y = from).
        private static Bounds Stand(Transform parent, GameObject m, float from = 0)
        {
            Bounds b = Assets.MeshBounds(parent, m); m.transform.localPosition += Vector3.up * (from - b.min.y);
            return Assets.MeshBounds(parent, m);
        }
        private static float Logs(Transform body, GameObject log, Material rope, string word, Material paint)
        {
            GameObject l = Cut(body, log, "log_" + word, paint);
            if (l == null) { l = Assets.Model(log, body, Vector3.zero, Quaternion.identity, 1f); l.transform.localScale = new Vector3(1.15f, 0.947f, 1.15f); }
            Bounds b = Stand(body, l);
            float w = Mathf.Max(b.size.x, b.size.z) + 0.05f, h = b.size.y;
            foreach (float at in new[] { 0.1f, 0.14f, 0.8f, 0.84f }) Block(body, rope, new Vector3(0, h * at, 0), new Vector3(w, 0.06f, w), true);
            return h;
        }
        private static float Poles(Transform body, GameObject pole, Material iron, string word, Material paint)
        {
            GameObject p = Cut(body, pole, "pole_" + word, paint);
            if (p == null) { p = Assets.Model(pole, body, Vector3.zero, Quaternion.identity, 1f); p.transform.localScale = new Vector3(1.25f, 2.1f, 1.25f); }
            Bounds b = Stand(body, p);
            float w = Mathf.Max(b.size.x, b.size.z) + 0.04f;
            Block(body, iron, new Vector3(0, 0.15f, 0), new Vector3(w, 0.08f, w));
            return b.size.y;
        }
        private static float Footed(Transform body, Transform col, GameObject log, GameObject stone, Material iron, GameObject dragon, float s, string word, Material paint)
        {
            float foot = 0;
            if (stone != null)
            {
                GameObject plinth = Assets.Model(stone, body, Vector3.zero, Quaternion.identity, 1f);
                plinth.transform.localScale = new Vector3(0.72f, 0.28f, 0.72f);
                foot = Stand(body, plinth).size.y;
            }
            GameObject l = Cut(body, log, "log2_" + word, paint);
            if (l == null) { l = Assets.Model(log, body, Vector3.zero, Quaternion.identity, 1f); l.transform.localScale = new Vector3(1.2f, 0.82f, 1.2f); }
            Bounds b = Stand(body, l, foot);
            float w = Mathf.Max(b.size.x, b.size.z) + 0.02f, head = b.max.y;
            foreach (float at in new[] { 0.08f, 0.9f }) Block(body, iron, new Vector3(0, Mathf.Lerp(foot, head, at), 0), new Vector3(w, 0.1f, w), true);
            return head;
        }
        private static float Carved(Transform body, Transform col, GameObject shaftSrc, GameObject stone, GameObject wolf, GameObject raven, Material bands, bool gild, float s, string word, Material paint)
        {
            Quaternion Turn(float yaw) => Quaternion.Euler(0, -s * yaw, 0); // yaw for the left pillar (0 = facing the front, -z), mirrored for the right
            float top = 0;
            if (stone != null)
            {
                GameObject plinth = Assets.Model(stone, body, Vector3.zero, Turn(s < 0 ? 0 : 180), 1f);
                plinth.transform.localScale = new Vector3(0.68f, 0.3f, 0.68f);
                top = Stand(body, plinth).size.y;
            }
            // The carved darkwood shaft.
            GameObject shaft = Cut(body, shaftSrc, "dark_" + word, paint); // (baked turned and scaled as below)
            if (shaft == null) { shaft = Assets.Model(shaftSrc, body, Vector3.zero, Turn(s < 0 ? 0 : 180), 1f); shaft.transform.localScale = new Vector3(1f, 0.757f, 1f); }
            Bounds sb = Stand(body, shaft, top);
            float foot = top, head = sb.max.y, width = Mathf.Max(sb.size.x, sb.size.z);
            // Bands: at the foot, one above it and one below the head (the runes between), and a broad one under the head.
            foreach ((float at, float h) in new[] { (0.06f, 0.1f), (0.16f, 0.06f), (0.8f, 0.06f), (0.93f, 0.12f) }) // (clear of the runes, cut 0.9 to 2.25 m up the shaft)
                Block(body, bands, new Vector3(0, Mathf.Lerp(foot, head, at), 0), new Vector3(width + 0.06f, h, width + 0.06f));
            // A darkwood capital: a squat, wider block of the same carving.
            GameObject cap = Assets.Model(shaftSrc, body, Vector3.zero, Turn(s < 0 ? 0 : 180), 1f);
            cap.transform.localScale = new Vector3(1.45f, 0.05f, 1.45f);
            head = Stand(body, cap, head).max.y;
            Block(body, bands, new Vector3(0, head + 0.02f, 0), new Vector3(width * 1.45f + 0.04f, 0.04f, width * 1.45f + 0.04f));
            head += 0.04f;
            // Geri and Freki: a carved wolf's head on each pillar's face under the capital, looking out to the front and a little away from the middle.
            if (wolf != null)
            {
                GameObject w = Assets.Model(wolf, col, Vector3.zero, Turn(185), 0.24f);
                Stand(col, w, head - 0.7f);
                Bounds wb = Assets.MeshBounds(col, w); if (wb.min.z < -Width / 2 + 0.02f) w.transform.localPosition += Vector3.forward * (-Width / 2 + 0.02f - wb.min.z); // (inside the pillar's space)
                if (gild) Assets.Tint(w, new Color(1.25f, 1.05f, 0.55f), new Color(0.18f, 0.12f, 0.02f));
            }
            // Huginn and Muninn: each raven's branch reaches in from its pillar's top, the two birds facing each other over the way between.
            if (raven != null)
            {
                GameObject r = Assets.Model(raven, col, Vector3.zero, Turn(90), 0.3f);
                Stand(col, r, head - 0.12f);
                if (gild) Assets.Tint(r, new Color(1.25f, 1.05f, 0.55f), new Color(0.18f, 0.12f, 0.02f));
            }
            return head;
        }
        // A beam across the tops (and at a homestead a hide hung from it).
        private static void Crossbeam(Transform tier, GameObject beam, GameObject hide, float top)
        {
            GameObject b = Assets.Model(beam, tier, Vector3.zero, Quaternion.identity, 1f);
            Bounds bb = Assets.MeshBounds(tier, b);
            b.transform.localScale = new Vector3((2 * Apart + 0.5f) / Mathf.Max(0.1f, bb.size.x), 1f, 1f);
            bb = Stand(tier, b, top - 0.4f); // (its top flush with the poles')
            b.transform.localPosition += new Vector3(-bb.center.x, 0, -bb.center.z);
            if (hide == null) return;
            GameObject h = Assets.Model(hide, tier, Vector3.zero, Quaternion.Euler(90, 0, 0), 0.36f);
            Bounds hb = Assets.MeshBounds(tier, h);
            h.transform.localPosition += new Vector3(-hb.center.x, (top - 0.35f) - hb.max.y, -0.3f - hb.center.z);
        }
        // Fire burning in the capital: the brazier's coals and flames (without their burning or their scripts), on the pillar's head.
        private static void Fire(Transform col, GameObject brazier, float head)
        {
            var bowl = new GameObject("fire"); bowl.transform.SetParent(col, false); bowl.transform.localPosition = new Vector3(0, head, 0); bowl.transform.localScale = Vector3.one * 1.1f;
            Vector3 seat = Vector3.zero; // (the coals' height in the brazier: they're set on the head)
            Fireplace fp = brazier.GetComponent<Fireplace>();
            foreach (GameObject flames in new[] { fp?.m_enabledObject, fp?.m_enabledObjectHigh }) // (the coals, and the flames over them)
            {
                if (flames == null) continue;
                GameObject f = Object.Instantiate(flames, bowl.transform, false);
                Vector3 at = brazier.transform.InverseTransformPoint(flames.transform.position);
                if (flames == fp.m_enabledObject) seat = at;
                bool coals = flames == fp.m_enabledObject;
                f.transform.localPosition = new Vector3(0, coals ? -0.05f : at.y - seat.y, 0);
                if (coals) f.transform.localScale = Vector3.one * 0.62f; // (the coals within the capital's width; the flames over them made bigger)
                else
                {
                    f.transform.localScale = Vector3.one * 1.6f;
                    foreach (ParticleSystem ps in f.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                }
                f.transform.localRotation = Quaternion.Inverse(brazier.transform.rotation) * flames.transform.rotation;
                f.SetActive(true);
                foreach (Collider c in f.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (MonoBehaviour m in f.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && !(m is LightFlicker) && !(m is LightLod)).ToList()) Object.DestroyImmediate(m);
            }
        }
        // A plain block (a Unity cube without its collider) for bands and fittings.
        private static void Block(Transform parent, Material material, Vector3 centre, Vector3 size, bool round = false)
        {
            var b = GameObject.CreatePrimitive(round ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            if (round) size.y /= 2f; // (a Unity cylinder is two units tall)
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
            // Each god's own altar, made in Blender (Models/altar/altars.py), its face to the piece's forward where the worshipper kneels.
            // The stone pillar with the god's token on it, if the model can't be had.
            if (!AltarModel(god, look.transform)) PlinthAltar(look.transform, plinth, emblem, scale, offset, turn);
            GodInfo info = Tables.God(god);
            Finish(go, look, $"Altar of {info.Name}",
                $"An altar to {info.Name}. Designate a {(god == "odin" ? "shrine" : "temple")} room round it. {info.Name} wants {info.Offerings}; the blessing: {info.Blessing}. Use it to make an offering from your bag.",
                Piece.PieceCategory.Misc, ("Stone", 12), (god == "odin" ? "TrophyEikthyr" : "Coins", god == "odin" ? 0 : 10), ("Resin", 2));
            go.AddComponent<Altar>().God = god;
        }

        // The colours that are each god's own in their altar's model: cloth, flowers, paint.
        private static readonly Dictionary<string, (Color cloth, Color bloom, Color paint)> AltarColours = new Dictionary<string, (Color, Color, Color)>
        {
            ["odin"] = (new Color(0.3f, 0.32f, 0.45f), new Color(0.7f, 0.3f, 0.35f), new Color(0.56f, 0.1f, 0.06f)),
            ["thor"] = (new Color(0.5f, 0.12f, 0.08f), new Color(0.7f, 0.3f, 0.35f), new Color(0.6f, 0.11f, 0.06f)),
            ["freyja"] = (new Color(0.55f, 0.3f, 0.35f), new Color(0.86f, 0.4f, 0.5f), new Color(0.62f, 0.14f, 0.08f)),
            ["njord"] = (new Color(0.25f, 0.35f, 0.42f), new Color(0.7f, 0.3f, 0.35f), new Color(0.45f, 0.56f, 0.62f)),
            ["frigg"] = (new Color(0.17f, 0.26f, 0.5f), new Color(0.7f, 0.3f, 0.35f), new Color(0.78f, 0.62f, 0.3f)),
            ["tyr"] = (new Color(0.5f, 0.12f, 0.08f), new Color(0.7f, 0.3f, 0.35f), new Color(0.58f, 0.1f, 0.06f)),
            ["ullr"] = (new Color(0.3f, 0.35f, 0.3f), new Color(0.7f, 0.3f, 0.35f), new Color(0.6f, 0.12f, 0.08f)),
            ["hel"] = (new Color(0.33f, 0.34f, 0.32f), new Color(0.5f, 0.45f, 0.5f), new Color(0.3f, 0.3f, 0.32f)),
        };

        private static bool AltarModel(string god, Transform look)
        {
            Mesh model = CarvedMesh("altar_" + god, "altar");
            Material hewn = Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
            Material rock = Assets.Find("rock4_forest")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            if (model == null || hewn == null) return false;
            if (!AltarColours.TryGetValue(god, out var own)) own = AltarColours["odin"];
            Material grain = new Material(hewn) { name = "JarldomAltarWood", hideFlags = HideFlags.HideAndDontSave }; grain.mainTextureScale = Vector2.one * StakeGrain; Assets.Owned.Add(grain);
            Material stone = Plain(hewn, new Color(0.45f, 0.44f, 0.42f)), cut = Plain(hewn, new Color(0.08f, 0.075f, 0.07f));
            if (rock != null)
            {
                // The rock's own textures in the pieces' shader: the rock shader lays its moss (a small, busy tile) on every upward face
                // whatever its settings. The carving, and Hel's black stone, in the same shader, so the stone is never drawn over them.
                Material Rock(string n, Color c)
                {
                    var m = new Material(hewn) { name = n, hideFlags = HideFlags.HideAndDontSave, mainTexture = rock.mainTexture, color = c };
                    m.mainTextureScale = Vector2.one * 0.4f;
                    if (m.HasProperty("_BumpMap") && rock.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", rock.GetTexture("_BumpMap")); m.SetTextureScale("_BumpMap", Vector2.one * 0.4f); }
                    foreach (string k in new[] { "_Glossiness", "_Metallic", "_Smoothness" }) if (m.HasProperty(k)) m.SetFloat(k, 0.05f);
                    Assets.Owned.Add(m);
                    return m;
                }
                stone = Rock("JarldomAltarStone", new Color(0.85f, 0.85f, 0.86f));
                cut = Rock("JarldomAltarCut", new Color(0.08f, 0.075f, 0.07f));
            }
            Material Matte(Color c) { Material m = Plain(hewn, c); foreach (string k in new[] { "_Glossiness", "_Metallic", "_Smoothness" }) if (m.HasProperty(k)) m.SetFloat(k, 0); return m; }
            Material gold = Plain(hewn, new Color(0.9f, 0.66f, 0.24f)); // (barely metallic: a mirror of gold shows the dark hall, black)
            if (gold.HasProperty("_Metallic")) gold.SetFloat("_Metallic", 0.2f);
            if (gold.HasProperty("_Glossiness")) gold.SetFloat("_Glossiness", 0.4f);
            Material glow = Plain(hewn, new Color(1f, 0.78f, 0.42f));
            if (glow.HasProperty("_EmissionColor")) { glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.22f) * 1.6f); }
            var m_ = new GameObject("altar"); m_.transform.SetParent(look, false);
            m_.AddComponent<MeshFilter>().sharedMesh = model;
            // (stone, cut, wood, dark, iron, gold, bone, cloth, rope, glow, leaf, bloom, paint, hide, shade: as the model's materials are numbered)
            m_.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                stone, cut, grain, Matte(new Color(0.09f, 0.085f, 0.085f)), Plain(hewn, new Color(0.2f, 0.195f, 0.19f)), gold,
                Matte(new Color(0.66f, 0.63f, 0.56f)), Matte(own.cloth), Matte(new Color(0.55f, 0.46f, 0.3f)), glow, Matte(new Color(0.2f, 0.36f, 0.12f)),
                Matte(own.bloom), Matte(own.paint), Matte(new Color(0.42f, 0.3f, 0.19f)), Matte(new Color(0.24f, 0.21f, 0.17f)),
            }.Take(model.subMeshCount).ToArray(); // (a model made before a slot was added has fewer)
            if (god == "hel") HelKit(look);
            return true;
        }

        // Hel's bones, the game's own: a beast's ribcage rising behind her table like claws, skulls on it, bones and a skull at its foot.
        private static void HelKit(Transform look)
        {
            Kit(look, "UndeadBjornRibcage", new Vector3(0, 0, -0.42f), new Vector3(0, 90, 0), 0.8f, 0.4f); // (its length squeezed: it stands close behind)
            Kit(look, "Skull1", new Vector3(0, 0.86f, -0.1f), new Vector3(0, -90, 0), 0.85f);
            Kit(look, "Skull1", new Vector3(-0.26f, 0.86f, -0.04f), new Vector3(0, -70, 0), 0.68f);
            Kit(look, "Skull1", new Vector3(0.27f, 0.86f, -0.1f), new Vector3(0, -112, 0), 0.68f);
            Kit(look, "bone_stack", new Vector3(-0.42f, 0, 0.5f), new Vector3(0, 20, 0), 0.32f);
            Kit(look, "LargeBone", new Vector3(0.22f, 0, 0.52f), new Vector3(0, 30, 90), 0.3f);
            Kit(look, "LargeBone", new Vector3(0.42f, 0, 0.42f), new Vector3(0, -55, 90), 0.26f);
            Kit(look, "Skull1", new Vector3(0.5f, 0, 0.6f), new Vector3(8, -40, 0), 0.62f);
        }
        // A game prefab's look, turned and scaled (squeezed along its own x, if asked), set down with its foot's middle at a point.
        private static void Kit(Transform look, string prefab, Vector3 at, Vector3 euler, float scale, float squeeze = 1f)
        {
            GameObject source = Assets.Find(prefab);
            if (source == null) return;
            GameObject m = Assets.Model(source, look, Vector3.zero, Quaternion.Euler(euler), scale);
            m.transform.localScale = new Vector3(scale * squeeze, scale, scale); // (squeeze: along its own length, x)
            Bounds b = Assets.MeshBounds(look, m);
            m.transform.localPosition += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        }

        // The look the altars had before their models: the stone pillar, the god's token on it.
        private static void PlinthAltar(Transform look, GameObject plinth, GameObject emblem, float scale, Vector3 offset, Quaternion turn)
        {
            GameObject stone = Assets.Model(plinth, look, Vector3.zero, Quaternion.identity, 1f);
            stone.transform.localScale = new Vector3(0.75f, 0.5f, 0.75f);
            Bounds sb = Assets.MeshBounds(look, stone); stone.transform.localPosition += Vector3.up * -sb.min.y;
            if (emblem != null)
            {
                GameObject e = Assets.Model(emblem, look, Vector3.zero, turn, scale, r => !(r.name.ToLower().Contains("broken")));
                Bounds eb = Assets.MeshBounds(look, e);
                float dz = offset.z > 50 ? -(eb.max.z - 0.35f) : offset.z - eb.center.z;
                e.transform.localPosition += new Vector3(offset.x - eb.center.x, sb.size.y - eb.min.y + 0.02f, dz);
            }
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

        // A watch post: a stake with a red pennant. Guards walk the colony's posts in turn, in the order they were raised.
        private static void WatchPost()
        {
            GameObject pole = Assets.Find("wood_pole_log");
            GameObject go = Base("wood_pole_log", WatchPostName);
            if (go == null || pole == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            Material wood = pole.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.HasProperty("_Color"));
            // A hewn stake wedged in a little cairn (made in Blender: Models/marker/post.py), an iron band under its turned head, the othala
            // rune on its face, a red pennant on a lashed crossbar, a knotted cord with a plumb bob. The plain pole and flag, if it can't be had.
            Mesh post = CarvedMesh("post", "marker");
            Material hewn = Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
            Material rock = Assets.Find("rock4_forest")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            if (post != null && hewn != null)
            {
                Material grain = new Material(hewn) { name = "JarldomPostWood", hideFlags = HideFlags.HideAndDontSave }; grain.mainTextureScale = Vector2.one * StakeGrain; Assets.Owned.Add(grain);
                Material stone = rock != null ? new Material(rock) { name = "JarldomPostStone", hideFlags = HideFlags.HideAndDontSave } : Plain(hewn, new Color(0.42f, 0.42f, 0.4f));
                if (rock != null) { stone.mainTextureScale = Vector2.one * 0.35f; stone.color *= new Color(0.75f, 0.75f, 0.78f); Assets.Owned.Add(stone); }
                Material cloth = Plain(hewn, new Color(0.56f, 0.1f, 0.06f)), stripe = Plain(hewn, new Color(0.86f, 0.64f, 0.24f));
                foreach (Material c in new[] { cloth, stripe }) foreach (string k in new[] { "_Glossiness", "_Metallic", "_Smoothness" }) if (c != null && c.HasProperty(k)) c.SetFloat(k, 0); // wool, not lacquer
                var m = new GameObject("post"); m.transform.SetParent(look.transform, false);
                m.AddComponent<MeshFilter>().sharedMesh = post;
                // (wood, rope, iron, cloth, paint, stone: as the model's materials are numbered)
                m.AddComponent<MeshRenderer>().sharedMaterials = new[] { grain, Plain(hewn, new Color(0.62f, 0.52f, 0.34f)), Plain(hewn, new Color(0.16f, 0.155f, 0.15f)), cloth, stripe, stone };
            }
            else
            {
                GameObject p = Assets.Model(pole, look.transform, Vector3.zero, Quaternion.identity, 1f);
                p.transform.localScale = new Vector3(0.6f, 0.75f, 0.6f);
                Bounds pb = Assets.MeshBounds(look.transform, p); p.transform.localPosition += Vector3.up * -pb.min.y;
                Material red = Plain(wood, new Color(0.62f, 0.12f, 0.08f));
                var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(flag.GetComponent<Collider>());
                flag.transform.SetParent(look.transform, false);
                flag.transform.localScale = new Vector3(0.04f, 0.32f, 0.5f);
                flag.transform.localPosition = new Vector3(0, pb.size.y * 0.86f, 0.27f);
                flag.GetComponent<MeshRenderer>().sharedMaterial = red;
            }
            Finish(go, look, "Watch post", "A waypoint for the colony's guards: they walk its watch posts in turn, in the order they were raised, and look about at each. Raise two or more round what's to be watched.",
                Piece.PieceCategory.Misc, ("Wood", 2), ("LeatherScraps", 1));
            go.AddComponent<WatchPost>();
        }

        // An ore claim: a prospector's marker, driven in beside a deposit found in the wilds.
        private static void OreClaim()
        {
            GameObject pole = Assets.Find("wood_pole_log");
            GameObject go = Base("wood_pole_log", OreClaimName);
            if (go == null || pole == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            Material wood = pole.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.HasProperty("_Color"));
            // A stout log in a heap of rubble and ore, a pick and a sledge crossed and lashed on its front, an iron lantern hung from a bracket
            // at its head, a hooped bucket of ore beside it (made in Blender: Models/marker/claim.py). The pole, pennant and pick's head it
            // was before, if the model can't be had.
            Mesh claim = CarvedMesh("claim", "marker");
            Material hewn = Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
            Material rock = Assets.Find("rock4_forest")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            if (claim != null && hewn != null)
            {
                Material grain = new Material(hewn) { name = "JarldomClaimWood", hideFlags = HideFlags.HideAndDontSave }; grain.mainTextureScale = Vector2.one * StakeGrain; Assets.Owned.Add(grain);
                Material stone = rock != null ? new Material(rock) { name = "JarldomClaimStone", hideFlags = HideFlags.HideAndDontSave } : Plain(hewn, new Color(0.42f, 0.42f, 0.4f));
                if (rock != null) { stone.mainTextureScale = Vector2.one * 0.35f; stone.color *= new Color(0.75f, 0.75f, 0.78f); Assets.Owned.Add(stone); }
                Material ore = Plain(hewn, new Color(0.6f, 0.32f, 0.14f)); // (copper-bright lumps among the grey)
                Material glow = Plain(hewn, new Color(1f, 0.78f, 0.42f)); // the lantern's panes, alight
                if (glow.HasProperty("_EmissionColor")) { glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", new Color(1f, 0.62f, 0.25f) * 1.6f); }
                var m = new GameObject("claim"); m.transform.SetParent(look.transform, false);
                m.AddComponent<MeshFilter>().sharedMesh = claim;
                // (wood, rope, iron, stone, ore, glow: as the model's materials are numbered)
                m.AddComponent<MeshRenderer>().sharedMaterials = new[] { grain, Plain(hewn, new Color(0.62f, 0.52f, 0.34f)), Plain(hewn, new Color(0.16f, 0.155f, 0.15f)), stone, ore, glow };
            }
            else
            {
                GameObject p = Assets.Model(pole, look.transform, Vector3.zero, Quaternion.identity, 1f);
                p.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                Bounds pb = Assets.MeshBounds(look.transform, p); p.transform.localPosition += Vector3.up * -pb.min.y;
                var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(flag.GetComponent<Collider>());
                flag.transform.SetParent(look.transform, false);
                flag.transform.localScale = new Vector3(0.04f, 0.3f, 0.46f);
                flag.transform.localPosition = new Vector3(0, pb.size.y * 0.84f, 0.25f);
                flag.GetComponent<MeshRenderer>().sharedMaterial = Plain(wood, new Color(0.78f, 0.5f, 0.16f));
                var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(head.GetComponent<Collider>());
                head.transform.SetParent(look.transform, false);
                head.transform.localScale = new Vector3(0.5f, 0.07f, 0.07f);
                head.transform.localPosition = new Vector3(0, pb.size.y * 0.55f, 0f);
                head.GetComponent<MeshRenderer>().sharedMaterial = Plain(wood, new Color(0.35f, 0.35f, 0.37f));
            }
            Finish(go, look, "Ore claim", "Drive it in beside a deposit you've found (copper, tin, silver; by a sunken crypt's door for its iron): the colony's miners go out to it and bring the ore home. Build a camp round it (a bed, a chest and a fire) and they work everything near, more of them, staying longer.",
                Piece.PieceCategory.Misc, ("Wood", 2), ("Stone", 1));
            go.GetComponent<Piece>().m_craftingStation = null; // (driven in out in the wilds: no workbench there)
            go.AddComponent<OreClaim>();
        }

        // The memorial stone: a rough runestone for the colony's dead, a serpent band of runes cut round its face and Odin's valknut within
        // (made in Blender: Models/marker/memorial.py). E reads the names on it.
        private static void MemorialStone()
        {
            Mesh model = CarvedMesh("memorial", "marker");
            Material rock = Assets.Find("rock4_forest")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            Material hewn = Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
            if (model == null || hewn == null) return;
            GameObject go = Base("stone_pillar", MemorialName);
            if (go == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            Material stone = rock != null ? new Material(rock) { name = "JarldomMemorialStone", hideFlags = HideFlags.HideAndDontSave } : Plain(hewn, new Color(0.5f, 0.5f, 0.48f));
            if (rock != null) { stone.mainTextureScale = Vector2.one * 0.5f; stone.color *= new Color(0.95f, 0.93f, 0.9f); Assets.Owned.Add(stone); }
            var m = new GameObject("memorial"); m.transform.SetParent(look.transform, false);
            m.AddComponent<MeshFilter>().sharedMesh = model;
            // The carving in the rock's own shader, black (in any other, the rock's surface is drawn over it where the face slopes away).
            Material cut = Plain(hewn, new Color(0.1f, 0.09f, 0.085f));
            if (rock != null)
            {
                cut = new Material(rock) { name = "JarldomMemorialCut", hideFlags = HideFlags.HideAndDontSave, color = new Color(0.07f, 0.065f, 0.06f) };
                cut.SetFloat("_AddSnow", 0f); cut.DisableKeyword("_ADDSNOW_ON"); cut.SetFloat("_MossAlpha", 0f); cut.SetFloat("_Glossiness", 0.1f);
                Assets.Owned.Add(cut);
            }
            // (stone, paint, rune: as the model's materials are numbered; nothing is painted now)
            m.AddComponent<MeshRenderer>().sharedMaterials = new[] { stone, cut, cut };
            Finish(go, look, "Memorial stone", "A raised stone for the colony's dead. Read it (E) for the names: who went to Valhalla and the deeds that took them there, who was lost to Hel and laid to rest, and who walks still, unburied.",
                Piece.PieceCategory.Misc, ("Stone", 20));
            go.AddComponent<MemorialStone>();
        }

        // The funeral ship: a karve drawn up on the beach for a soul's last voyage (the game's own karve: hull, mast, its water mask), its
        // hold for grave goods. Lit at dusk with the soul aboard, it goes out burning and sinks (World/ShipBurial.cs). A Hold's honour.
        public static GameObject KarveLook(Transform parent)
        {
            GameObject karve = Assets.Find("Karve");
            return karve == null ? null : Assets.Model(karve, parent, Vector3.zero, Quaternion.identity, 1f, r => !HasParent(r.transform, "piece_chest"));
        }
        private static bool HasParent(Transform t, string name) { for (; t != null; t = t.parent) if (t.name == name) return true; return false; }
        private static void FuneralKarve()
        {
            GameObject go = Base("wood_pole_log", FuneralKarveName);
            if (go == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            if (KarveLook(look.transform) == null) { Object.DestroyImmediate(go); return; }
            Finish(go, look, "Funeral ship", "A karve drawn up on the beach for a soul's last voyage (a Hold's honour; build it at the shore). Put their grave goods aboard; when a worthy soul is sent to Valhalla they go by ship instead of the pyre: at dusk they board, it goes out burning and sinks, and Odin takes more note than of any pyre. The ship goes with them.",
                Piece.PieceCategory.Misc, ("FineWood", 30), ("Resin", 20), ("DeerHide", 10), ("BronzeNails", 40));
            Container goods = go.AddComponent<Container>();
            goods.m_name = "Grave goods"; goods.m_width = 6; goods.m_height = 2; goods.m_privacy = Container.PrivacySetting.Public;
            Container chest = Assets.Find("piece_chest_wood")?.GetComponent<Container>();
            if (chest != null) { goods.m_bkg = chest.m_bkg; goods.m_openEffects = chest.m_openEffects; goods.m_closeEffects = chest.m_closeEffects; }
            go.AddComponent<FuneralShip>();
        }
        // Where it may be built: in a colony of Hold rank, on the beach (water within 8 m). Why not, or "".
        public static string KarveWhy(Vector3 at)
        {
            Colony c = Colony.At(at, 30);
            if (c == null) return "a funeral ship is built at a colony's shore";
            if (c.R.Rank < Rank.Hold) return "a funeral ship is a Hold's honour (the colony must be a Hold)";
            float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            for (int k = 0; k < 16; k++)
                for (float d = 2f; d <= 8f; d += 2f)
                {
                    Vector3 p = at + Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward * d;
                    if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p) < water - 0.3f) return "";
                }
            return "build it on the beach, where the water comes within a few steps";
        }
        [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class KarvePlace
        {
            private static readonly HarmonyLib.AccessTools.FieldRef<Player, GameObject> Ghost = HarmonyLib.AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                if (piece == null || piece.name != FuneralKarveName) return true;
                GameObject g = Ghost(__instance); if (g == null) return true;
                string why = KarveWhy(g.transform.position);
                if (why == "") return true;
                __instance.Message(MessageHud.MessageType.Center, "Not here: " + why + ".");
                __result = false; return false;
            }
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
            Assets.Icon(go, name, Quaternion.Euler(0, -30, 0), icon => piece.m_icon = icon);
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

        internal static float StakeGrain = 0.6f; // (the stake's wood texture repeats per metre)
        // Not built: choosing it in the hammer turns clicks into room and zone corners (Designate).
        private static void Designator()
        {
            GameObject go = Base("wood_pole", DesignateName);
            if (go == null) return;
            var look = new GameObject("new"); look.transform.SetParent(go.transform, false);
            // A marking staff (made in Blender: Models/marker): a slim shaft shod with iron, a leather grip, red and gold cloth ribbons tied
            // under its head, and a knotted measuring cord with a plumb bob. The banner it was before, if the model can't be had.
            Mesh stake = CarvedMesh("stake", "marker");
            Material wood = Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
            if (stake != null && wood != null)
            {
                Material grain = new Material(wood) { name = "JarldomStakeWood", hideFlags = HideFlags.HideAndDontSave }; grain.mainTextureScale = Vector2.one * StakeGrain; Assets.Owned.Add(grain);
                Material cloth = Plain(wood, new Color(0.56f, 0.1f, 0.06f)), stripe = Plain(wood, new Color(0.86f, 0.64f, 0.24f)); // (the red ribbons and the gold)
                foreach (Material c in new[] { cloth, stripe }) foreach (string k in new[] { "_Glossiness", "_Metallic", "_Smoothness" }) if (c != null && c.HasProperty(k)) c.SetFloat(k, 0); // wool, not lacquer
                var m = new GameObject("stake"); m.transform.SetParent(look.transform, false);
                m.AddComponent<MeshFilter>().sharedMesh = stake;
                // (wood, rope, iron, cloth, paint, leather: as the model's materials are numbered)
                m.AddComponent<MeshRenderer>().sharedMaterials = new[] { grain, Plain(wood, new Color(0.62f, 0.52f, 0.34f)), Plain(wood, new Color(0.16f, 0.155f, 0.15f)), cloth, stripe, Plain(wood, new Color(0.26f, 0.15f, 0.09f)) };
            }
            else
            {
                GameObject flag = Assets.Find("piece_banner01");
                if (flag != null) Assets.Model(flag, look.transform, Vector3.zero, Quaternion.identity, 0.6f);
            }
            Finish(go, look, "Mark rooms and zones", "Not built. With this chosen, Ctrl+Shift+click on a room's floor and the room is found from its walls in one go. Or click the corners of a room or a work zone on the ground (click the first corner again to close it), then choose what it is; click one spot twice for a round work zone. Backspace takes back the last corner. E on a marked room or zone checks, changes, widens or removes it. Look at a chest and Shift+E to choose what it's for (the treasury, a kind of stores); look at a fire and Shift+E to make it the colony's beacon, lit by the watch when a jarl sails home at night. With the hammer out, marked rooms and zones show on the ground (green when they work, red gaps in walls).",
                Piece.PieceCategory.Misc, ("Wood", 1));
            Piece marker = go.GetComponent<Piece>();
            marker.m_resources = new Piece.Requirement[0]; // free: nothing is ever built (Designate refuses placement)
            marker.m_craftingStation = null;               // and usable anywhere, not only by a workbench
        }

        // ---- plumbing ----
        // A game material in a plain colour (its texture taken away): for small made-up parts.
        internal static Material Plain(Material from, Color color)
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
            if (wear != null) { wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_health = Math.Max(wear.m_health, 600); wear.m_fragmentRoots = null; } // (the source's fragment parts are gone: it breaks into our look's own pieces)
            Piece piece = go.GetComponent<Piece>();
            piece.m_name = title; piece.m_description = text; piece.m_category = category;
            piece.m_resources = Assets.Needs(cost.Where(c => c.Item2 > 0).ToArray());
            piece.m_craftingStation = Assets.Find("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_comfort = 0;
            Assets.Icon(look, go.name, Quaternion.Euler(0, -30, 0), icon => piece.m_icon = icon);
            Made[go.name] = go;
        }

        public static void AddToHammer(ObjectDB db)
        {
            PieceTable table = db.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if (table == null) return;
            var ours = All.ToList(); var names = new HashSet<string>(ours.Select(o => o.name));
            table.m_pieces.RemoveAll(p => p == null || names.Contains(p.name) && !ours.Contains(p)); // an old copy's
            foreach (GameObject piece in ours) if (!table.m_pieces.Contains(piece)) table.m_pieces.Add(piece);
            if (Player.m_localPlayer != null) HarmonyLib.AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList")?.Invoke(Player.m_localPlayer, null); // (the build menu's own lists, or it offers an old copy's piece that's gone)
        }
        public static void RemoveFromHammer(ObjectDB db)
        {
            PieceTable table = db.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            table?.m_pieces.RemoveAll(p => p == null || Made.Values.Contains(p));
        }
        public static void Forget() => Made.Clear();
    }
}
