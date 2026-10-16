using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Camping
{
    // The kit's pieces, made from the game's own (the bed for the bedroll and the tent: a spawn point that sleeps; a plank wall for the
    // lean-to: something that stands and wears), each with its own look made in Blender (Models/camp.py) and colliders of its own shape.
    internal static class Kit
    {
        public const string Bedroll = "Camping_Bedroll", LeanTo = "Camping_LeanTo", Tent = "Camping_Tent";
        internal sealed class Def
        {
            public string Name, From, Title, Model, Description; public (string item, int n)[] Cost;
        }
        internal static readonly Def[] All =
        {
            new Def { Name = Bedroll, From = "bed", Title = "Bedroll", Model = "bedroll", Cost = new[] { ("DeerHide", 3), ("LeatherScraps", 4) },
                Description = "A pile of furs and hides to lie on and a blanket to roll up in. Sleep anywhere with a fire near: no roof needed, but not in the rain (put it under a lean-to). Sets your spawn point while it's laid out. Shift+E rolls it up again: what it cost goes back in your bag." },
            new Def { Name = LeanTo, From = "woodwall", Title = "Lean-to", Model = "leanto", Cost = new[] { ("Wood", 10), ("DeerHide", 4) },
                Description = "Hides lashed over a frame of poles, open at the front. Keeps the rain off whatever's under it: a fire, a bedroll, you. Built from what's at hand: no workbench needed." },
            new Def { Name = Tent, From = "bed", Title = "Tent", Model = "tent", Cost = new[] { ("WolfPelt", 4), ("DeerHide", 6), ("LeatherScraps", 6), ("Wood", 8) },
                Description = "A wool-walled tent on carved boards, as the old ones were. Inside, it's dry and warm enough to sleep without a fire, and the mountain's cold can't reach you. Sets your spawn point. Shift+E packs it up again: what it cost goes back in your bag." },
        };
        internal static bool Packs(string name) => name == Bedroll || name == Tent;

        private static readonly MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList"), UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        internal static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly List<Object> Owned = new List<Object>();
        private static GameObject _holder; private static ZNetScene _scene; private static float _next, _settleAt = -1; private static bool _broken;
        internal static PieceTable Hammer() => ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        // Once a second: made for this world, and in the hammer's menu (through world loads and reloads of this mod).
        internal static void Ensure()
        {
            if (_broken || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1;
            ZNetScene scene = ZNetScene.instance; PieceTable table = Hammer();
            if (scene == null || table == null || Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (scene != _scene) { Forget(); _scene = scene; }
            table.m_pieces.RemoveAll(p => p == null);
            bool changed = false, every = true;
            foreach (Def d in All)
            {
                if (!Made.TryGetValue(d.Name, out GameObject prefab) || prefab == null)
                {
                    prefab = Make(scene, d);
                    if (prefab == null) { every = false; continue; }
                    Made[d.Name] = prefab;
                }
                if (table.m_pieces.Contains(prefab) && scene.GetPrefab(d.Name) == prefab) continue;
                PieceReload.ReplacePrefab(scene, prefab, table);
                PieceReload.Reattach(scene, prefab);
                changed = true;
            }
            if (changed && Player.m_localPlayer is Player p) { UpdateKnownRecipes?.Invoke(p, null); UpdateAvailablePieces?.Invoke(p, null); }
            if (every) { if (_settleAt < 0) _settleAt = Time.unscaledTime + 3; else if (Time.unscaledTime >= _settleAt) Leftovers.TakeOver(Owned); }
        }
        internal static void Broken() => _broken = true;
        internal static void Unload()
        {
            PieceTable table = Hammer();
            foreach (GameObject prefab in Made.Values) PieceReload.UnloadPiece(prefab, table);
            // (the meshes and materials stay: what's standing still draws with them till the next copy puts its own in their place)
            Leftovers.HandOver(Owned.Concat(new Object[] { _holder }));
        }
        private static void Forget() { Made.Clear(); foreach (Object o in Owned) if (o != null) Object.Destroy(o); Owned.Clear(); _mats = null; }

        private static GameObject Make(ZNetScene scene, Def d)
        {
            GameObject from = scene.GetPrefab(d.From);
            if (from == null) { Plugin.Warn($"No {d.From} to make the {d.Title} from."); return null; }
            Material[] mats = Materials(scene);
            if (mats == null) return null;
            Mesh mesh = Model(d.Model);
            if (mesh == null) return null;
            if (_holder == null) { _holder = new GameObject("Camping prefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
            GameObject go = Object.Instantiate(from, _holder.transform, false); go.name = d.Name;
            Owned.Add(go);
            // The game's look goes (its LODs, its snow, its broken bits), and its colliders and snap points; the kit's in their place.
            WearNTear wear = go.GetComponent<WearNTear>();
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) { GameObject o = r.gameObject; Object.DestroyImmediate(r); if (o.GetComponent<MeshFilter>() is MeshFilter f) Object.DestroyImmediate(f); }
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger)) Object.DestroyImmediate(c);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && (t.name.StartsWith("_snappoint") || t.name.StartsWith("$hud_snappoint") || t.CompareTag("snappoint"))).ToList()) Object.DestroyImmediate(t.gameObject);
            var look = new GameObject("Model"); look.transform.SetParent(go.transform, false);
            look.AddComponent<MeshFilter>().sharedMesh = mesh;
            look.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (wear != null) { wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_fragmentRoots = new GameObject[0]; wear.m_noRoofWear = true; wear.m_noSupportWear = true; wear.m_materialType = WearNTear.MaterialType.Wood; }
            int pieceLayer = LayerMask.NameToLayer("piece");
            switch (d.Name)
            {
                case Bedroll:
                    // (a pile of furs and hides a hand and more deep: one can stand on it, and lie on it)
                    Box(go, pieceLayer, new Vector3(0, 0.08f, 0), new Vector3(1.0f, 0.16f, 2.05f), Quaternion.identity);
                    Spawn(go, new Vector3(0, 0.22f, 0));
                    Ground(go, 1.25f, 2.4f, 1.3f);
                    break;
                case LeanTo:
                    // (its hides: a slab from over the front posts down to the ground at the back; the two posts)
                    Box(go, pieceLayer, new Vector3(0, 0.99f, 0.25f), new Vector3(2.9f, 0.06f, 3.2f), Quaternion.Euler(38.4f, 0, 0));
                    foreach (float x in new[] { -1.25f, 1.25f }) Post(go, pieceLayer, new Vector3(x, 0.9f, -0.9f), 1.95f, 0.07f);
                    break;
                case Tent:
                    // (its two walls from the ridge to the ground, its back; the front left open between its flaps)
                    float half = 1.52f, ridge = 2.46f, wall = Mathf.Sqrt(half * half + ridge * ridge), lean = Mathf.Atan2(half, ridge) * Mathf.Rad2Deg;
                    Box(go, pieceLayer, new Vector3(half / 2, ridge / 2, 0), new Vector3(0.06f, wall, 3.2f), Quaternion.Euler(0, 0, lean));
                    Box(go, pieceLayer, new Vector3(-half / 2, ridge / 2, 0), new Vector3(0.06f, wall, 3.2f), Quaternion.Euler(0, 0, -lean));
                    Box(go, pieceLayer, new Vector3(0, ridge * 0.45f, 1.56f), new Vector3(half * 1.4f, ridge * 0.9f, 0.05f), Quaternion.identity);
                    Box(go, pieceLayer, new Vector3(0, 0.02f, 0), new Vector3(1.4f, 0.04f, 2.3f), Quaternion.identity); // (the pelt on its floor)
                    Spawn(go, new Vector3(0, 0.1f, 0.1f));
                    Warmth(go, new Vector3(0, 0.9f, 0), 1.6f);
                    Ground(go, 1.9f, 3.2f, 1.9f);
                    break;
            }
            var piece = go.GetComponent<Piece>();
            piece.m_name = d.Title; piece.m_description = d.Description;
            piece.m_category = Piece.PieceCategory.Furniture;
            piece.m_craftingStation = null; // (made in the wilds: no workbench)
            piece.m_groundPiece = false; piece.m_groundOnly = false; piece.m_clipEverything = false;
            piece.m_resources = d.Cost.Select(c => Req(c.item, c.n)).Where(r => r != null).ToArray();
            piece.m_enabled = true;
            try { if (Icons.Make(go, d.Name) is Sprite icon) { piece.m_icon = icon; Owned.Add(icon); Owned.Add(icon.texture); } }
            catch (Exception e) { Plugin.Warn($"No icon for the {d.Title}: {e.Message}"); }
            return go;
        }
        private static Piece.Requirement Req(string item, int n)
        {
            ItemDrop drop = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>();
            return drop == null ? null : new Piece.Requirement { m_resItem = drop, m_amount = n, m_recover = true };
        }
        private static void Box(GameObject go, int layer, Vector3 at, Vector3 size, Quaternion turn)
        {
            var o = new GameObject("Collider"); o.layer = layer; o.transform.SetParent(go.transform, false);
            o.transform.localPosition = at; o.transform.localRotation = turn;
            o.AddComponent<BoxCollider>().size = size;
        }
        private static void Post(GameObject go, int layer, Vector3 at, float height, float radius)
        {
            var o = new GameObject("Post"); o.layer = layer; o.transform.SetParent(go.transform, false); o.transform.localPosition = at;
            var c = o.AddComponent<CapsuleCollider>(); c.direction = 1; c.height = height; c.radius = radius;
        }
        // Where one lies (the bed's own spawn point, moved down to lie on the hide).
        private static void Spawn(GameObject go, Vector3 at)
        {
            Bed bed = go.GetComponent<Bed>();
            if (bed == null) return;
            if (bed.m_spawnPoint == null) { var s = new GameObject("spawnpoint"); s.transform.SetParent(go.transform, false); bed.m_spawnPoint = s.transform; }
            // (turned round: the game lays a sleeper with their head toward the spawn point's back, and the pillow is at the model's back)
            bed.m_spawnPoint.localPosition = at; bed.m_spawnPoint.localRotation = Quaternion.Euler(0, 180, 0);
        }
        // The ground under it levelled to where it lies (blended into the slope round it) and trodden to dirt, so the grass doesn't hide
        // it: as the hoe's path does it, while it stands (packed up, the ground is as it was).
        private static void Ground(GameObject go, float level, float smooth, float paint)
        {
            TerrainModifier t = go.GetComponent<TerrainModifier>() ?? go.AddComponent<TerrainModifier>();
            t.m_useTerrainCompiler = false; t.m_playerModifiction = true; t.m_sortOrder = 0;
            t.m_level = true; t.m_levelRadius = level; t.m_levelOffset = -0.02f; t.m_square = false;
            t.m_smooth = true; t.m_smoothRadius = smooth; t.m_smoothPower = 3f;
            t.m_paintCleared = true; t.m_paintType = TerrainModifier.PaintType.Dirt; t.m_paintRadius = paint; t.m_paintStrength = 1f; t.m_paintHeightCheck = false;
        }
        // Warm inside (a heat area, as a fire's: no cold, and a bed in it needs no fire): its own, the bed's base area copied.
        private static void Warmth(GameObject go, Vector3 at, float radius)
        {
            EffectArea basis = go.GetComponentsInChildren<EffectArea>(true).FirstOrDefault();
            var o = new GameObject("Warmth"); o.transform.SetParent(go.transform, false); o.transform.localPosition = at;
            if (basis != null) o.layer = basis.gameObject.layer;
            var sphere = o.AddComponent<SphereCollider>(); sphere.isTrigger = true; sphere.radius = radius;
            o.AddComponent<EffectArea>().m_type = EffectArea.Type.Heat;
        }

        // ---- the looks: the models, and the game's materials they're drawn in ----
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static Mesh Model(string name)
        {
            if (Meshes.TryGetValue(name, out Mesh had) && had != null) return had;
            try
            {
                using (var raw = typeof(Plugin).Assembly.GetManifestResourceStream("Camping." + name + ".json.gz"))
                {
                    if (raw == null) { Plugin.Warn("No model " + name); return null; }
                    using (var gz = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress))
                    using (var text = new System.IO.StreamReader(gz))
                    {
                        var j = Newtonsoft.Json.Linq.JObject.Parse(text.ReadToEnd());
                        float[] v = j["v"].ToObject<float[]>(), n = j["n"].ToObject<float[]>(), uv = j["uv"].ToObject<float[]>();
                        int[][] t = j["t"].ToObject<int[][]>();
                        var verts = new Vector3[v.Length / 3]; var norms = new Vector3[verts.Length]; var uvs = new Vector2[verts.Length];
                        for (int i = 0; i < verts.Length; i++) { verts[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]); norms[i] = new Vector3(n[3 * i], n[3 * i + 1], n[3 * i + 2]); uvs[i] = new Vector2(uv[2 * i], uv[2 * i + 1]); }
                        var mesh = new Mesh { name = "Camping " + name };
                        if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.colors = Enumerable.Repeat(Color.white, verts.Length).ToArray(); // (the piece shader reads the vertex colour)
                        mesh.subMeshCount = t.Length;
                        for (int k = 0; k < t.Length; k++) mesh.SetTriangles(t[k], k);
                        mesh.RecalculateBounds(); mesh.RecalculateTangents();
                        Owned.Add(mesh); Meshes[name] = mesh;
                        return mesh;
                    }
                }
            }
            catch (Exception e) { Plugin.Warn($"The {name} model: {e.Message}"); return null; }
        }
        // (as the models number them: wood, hide, fur, wool, leather, rope, red wool, stone, darker hide, grey fur)
        private static Material[] _mats;
        private static Material[] Materials(ZNetScene scene)
        {
            if (_mats != null && _mats.All(m => m != null)) return _mats;
            Material wood = PieceMat(scene, "wood_pole");
            if (wood == null) { Plugin.Warn("No wood_pole material to draw the kit in."); return null; }
            // (the brown fur rug's and the wolf's fur for furs; the deer rug's hide for hides, each patch its own piece of it: the models
            // give their hides uvs of their own, in the middle of the texture)
            Texture brown = Tex(scene, "rug_fur"), wolf = Tex(scene, "rug_wolf"), deer = Tex(scene, "rug_deer");
            _mats = new[]
            {
                Grain(wood, new Color(0.8f, 0.72f, 0.64f), 0.6f, "wood"),
                deer != null ? Textured(wood, deer, new Color(0.74f, 0.69f, 0.74f), "hide", own: true) : Plain(wood, new Color(0.58f, 0.48f, 0.37f), "hide"),
                brown != null ? Textured(wood, brown, new Color(0.9f, 0.86f, 0.82f), "fur") : Plain(wood, new Color(0.4f, 0.3f, 0.2f), "fur"),
                Plain(wood, new Color(0.55f, 0.5f, 0.43f), "wool"), // (undyed: a warm grey-brown, not a glow in the dark)
                Plain(wood, new Color(0.2f, 0.12f, 0.07f), "leather"),
                Plain(wood, new Color(0.55f, 0.48f, 0.35f), "rope"),
                Plain(wood, new Color(0.48f, 0.09f, 0.06f), "red"),
                Plain(wood, new Color(0.42f, 0.42f, 0.4f), "stone"),
                deer != null ? Textured(wood, deer, new Color(0.6f, 0.54f, 0.58f), "hide2", own: true) : Plain(wood, new Color(0.47f, 0.38f, 0.29f), "hide2"),
                wolf != null ? Textured(wood, wolf, new Color(0.7f, 0.68f, 0.66f), "fur2") : Plain(wood, new Color(0.35f, 0.33f, 0.31f), "fur2"),
            };
            return _mats;
        }
        // (a piece's own material: the one in the game's piece shader, not its snow cap)
        private static Material PieceMat(ZNetScene scene, string prefab) => scene.GetPrefab(prefab)?.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
        private static Texture Tex(ZNetScene scene, string prefab) => scene.GetPrefab(prefab)?.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.mainTexture != null)?.mainTexture;
        private static Material Grain(Material wood, Color tint, float scale, string name)
        {
            var m = new Material(wood) { name = "Camping " + name, color = tint }; m.mainTextureScale = Vector2.one * scale;
            Owned.Add(m); return m;
        }
        private static Material Textured(Material wood, Texture tex, Color tint, string name, bool own = false)
        {
            // (a rug's texture is the rug laid out on black: only its middle is sampled, so the metres the model spans stay on the hide;
            // a model's own uvs already keep to it)
            var m = new Material(wood) { name = "Camping " + name, color = tint, mainTexture = tex };
            if (own) { m.mainTextureScale = Vector2.one; m.mainTextureOffset = Vector2.zero; } else { m.mainTextureScale = Vector2.one * 0.1f; m.mainTextureOffset = Vector2.one * 0.42f; }
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            // (a rug's texture is see-through past the rug's edge: only its colour is wanted, never cut out)
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0f);
            m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON");
            foreach (string k in new[] { "_Glossiness", "_Metallic", "_Smoothness" }) if (m.HasProperty(k)) m.SetFloat(k, 0);
            Owned.Add(m); return m;
        }
        // The game's material in a plain colour, matte (cloth, leather, rope, stone).
        private static Material Plain(Material wood, Color color, string name)
        {
            var m = new Material(wood) { name = "Camping " + name, color = color };
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            foreach (string k in new[] { "_Glossiness", "_Metallic", "_Smoothness" }) if (m.HasProperty(k)) m.SetFloat(k, 0);
            Owned.Add(m); return m;
        }
    }
}
