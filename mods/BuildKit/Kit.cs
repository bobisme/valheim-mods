using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BuildKit
{
    // The kit's pieces, made when a world loads: each a copy of one of the game's own pieces (its colliders, snap points, support and
    // health), with the kit's look in place of the game's.
    internal static class Kit
    {
        internal sealed class Def
        {
            public string Name, From, Title, Text; public Shape Shape; public bool Steep, Hole;
        }
        // Turf roofs: one for each of the game's roof shapes, 26° and 45°.
        internal static readonly Def[] Defs =
        {
            Turf("BK_turf_roof", "wood_roof", Shape.Straight, false, "Turf roof 26°"),
            Turf("BK_turf_roof_top", "wood_roof_top", Shape.Ridge, false, "Turf roof ridge 26°"),
            Turf("BK_turf_roof_ocorner", "wood_roof_ocorner", Shape.OuterCorner, false, "Turf roof outer corner 26°"),
            Turf("BK_turf_roof_icorner", "wood_roof_icorner", Shape.InnerCorner, false, "Turf roof inner corner 26°"),
            Turf("BK_turf_roof_45", "wood_roof_45", Shape.Straight, true, "Turf roof 45°"),
            Turf("BK_turf_roof_top_45", "wood_roof_top_45", Shape.Ridge, true, "Turf roof ridge 45°"),
            Turf("BK_turf_roof_ocorner_45", "wood_roof_ocorner_45", Shape.OuterCorner, true, "Turf roof outer corner 45°"),
            Turf("BK_turf_roof_icorner_45", "wood_roof_icorner_45", Shape.InnerCorner, true, "Turf roof inner corner 45°"),
            // Smoke holes: a straight piece with a vent over the hearth.
            Vent("BK_turf_roof_smoke", "wood_roof", false, "Turf roof smoke hole 26°"),
            Vent("BK_turf_roof_smoke_45", "wood_roof_45", true, "Turf roof smoke hole 45°"),
        };
        private static Def Vent(string name, string from, bool steep, string title) => new Def
        {
            Name = name, From = from, Shape = Shape.Straight, Steep = steep, Hole = true, Title = title,
            Text = "A turf roof with a smoke hole through it and a little hooded vent over the hole, as a longhouse without a chimney let out its hearth's smoke. Put it over the fire: the smoke rises out under the hood, and the hood keeps the rain off the fire.",
        };
        private static Def Turf(string name, string from, Shape s, bool steep, string title) => new Def
        {
            Name = name, From = from, Shape = s, Steep = steep, Title = title,
            Text = "Sod laid on birch bark over boards, as Norse houses were roofed: snaps with the game's roofs of the same pitch. A turf roof keeps the house warm: +1 comfort under it (however many pieces).",
        };
        // All turf pieces count as one comfort piece (the game counts one of each group).
        internal const Piece.ComfortGroup TurfGroup = (Piece.ComfortGroup)71;

        private static readonly MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList"), UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        internal static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly List<Object> Owned = new List<Object>();
        private static GameObject _holder; private static ZNetScene _scene; private static float _next; private static bool _broken;

        internal static PieceTable Hammer() => ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        // Once a second: made for this world, and in the hammer's menu (through world loads and reloads of this mod).
        internal static void Ensure()
        {
            if (_broken || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1;
            ZNetScene scene = ZNetScene.instance; PieceTable table = Hammer();
            if (scene == null || table == null || Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (scene != _scene) { Forget(); _scene = scene; }
            bool changed = false;
            foreach (Def d in Defs)
            {
                if (!Made.TryGetValue(d.Name, out GameObject prefab) || prefab == null)
                {
                    prefab = Make(scene, d);
                    if (prefab == null) continue;
                    Made[d.Name] = prefab;
                }
                if (table.m_pieces.Contains(prefab) && scene.GetPrefab(d.Name) == prefab) continue;
                PieceReload.ReplacePrefab(scene, prefab, table);
                PieceReload.Reattach(scene, prefab);
                changed = true;
            }
            if (changed && Player.m_localPlayer is Player p) { UpdateKnownRecipes?.Invoke(p, null); UpdateAvailablePieces?.Invoke(p, null); }
        }
        internal static void Broken() => _broken = true;
        // Made again (a texture tried with kit look): the pieces standing keep their look until the area reloads.
        internal static void Remake() { Forget(); _next = 0; Ensure(); }
        internal static void Unload()
        {
            PieceTable table = Hammer();
            foreach (GameObject prefab in Made.Values) PieceReload.UnloadPiece(prefab, table);
            // (its meshes and materials are left: the pieces standing in the world still draw with them until the next copy's look
            // takes over, and destroyed they'd show magenta)
        }
        // A new world (or the main menu): made again from that world's own prefabs.
        private static void Forget() { Made.Clear(); Meshes.Clear(); foreach (Object o in Owned) if (o != null) Object.Destroy(o); Owned.Clear(); Mats.Clear(); }

        private static GameObject Make(ZNetScene scene, Def d)
        {
            GameObject from = scene.GetPrefab(d.From);
            if (from == null) { Plugin.Warn($"No {d.From} to make {d.Title} from."); return null; }
            if (_holder == null) { _holder = new GameObject("BuildKit prefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
            GameObject go = Object.Instantiate(from, _holder.transform, false); go.name = d.Name;
            Owned.Add(go);
            // The game's look goes (its LODs, its snow), the turf comes in its place: on each of its wear states, as it wears.
            WearNTear wear = go.GetComponent<WearNTear>();
            var states = new List<GameObject>();
            if (wear != null) foreach (GameObject s in new[] { wear.m_new, wear.m_worn, wear.m_broken }) if (s != null && !states.Contains(s)) states.Add(s);
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                GameObject o = r.gameObject;
                Object.DestroyImmediate(r);
                if (o.GetComponent<MeshFilter>() is MeshFilter f) Object.DestroyImmediate(f);
            }
            if (states.Count == 0) states.Add(go);
            if (wear != null) wear.m_fragmentRoots = new GameObject[0]; // (the game's broken bits were the old look's)
            Mesh mesh = TurfMesh(d, 15, 0);
            Material[] mats = TurfMaterials(scene, from);
            foreach (GameObject s in states)
            {
                var look = new GameObject(TurfLook.LookName); look.transform.SetParent(s.transform, false);
                // (in the piece's own frame: a state object may sit anywhere under it)
                look.transform.position = go.transform.position; look.transform.rotation = go.transform.rotation; look.transform.localScale = Vector3.one;
                if (s.transform.lossyScale != Vector3.one) { var ls = s.transform.lossyScale; look.transform.localScale = new Vector3(1 / ls.x, 1 / ls.y, 1 / ls.z); }
                look.AddComponent<MeshFilter>().sharedMesh = mesh;
                look.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            Piece piece = go.GetComponent<Piece>();
            go.AddComponent<TurfLook>(); // (its open sides torn, the rest left to meet their neighbours)
            if (d.Hole) HoleColliders(go, d);
            piece.m_resources = d.Hole ? Cost(("Wood", 4), ("FineWood", 1)) : Cost(("Wood", 2), ("FineWood", 1)); // (two wood for the boards, as a roof costs, and a fine wood: birch, its bark)
            piece.m_comfort = 1; piece.m_comfortGroup = TurfGroup;
            piece.m_name = d.Title;
            piece.m_description = d.Text;
            piece.m_enabled = true;
            try { if (Icons.Render(go) is Sprite icon) { piece.m_icon = icon; Owned.Add(icon); Owned.Add(icon.texture); } }
            catch (Exception ex) { Plugin.Warn($"No icon for {d.Title}: {ex.Message}"); }
            return go;
        }
        // The roof's solid box split in four round the smoke hole (a smoke ball rises through the middle), and the hood's cap: two
        // sloped boards (the fire under it is under a roof; the smoke slides off them and out).
        private static void HoleColliders(GameObject go, Def d)
        {
            BoxCollider roof = go.GetComponentsInChildren<BoxCollider>(true).OrderByDescending(b => b.size.x * b.transform.localScale.x * b.size.z * b.transform.localScale.z).FirstOrDefault();
            if (roof == null) { Plugin.Warn(d.Title + ": no roof collider to cut"); return; }
            Transform rt = roof.transform;
            Vector3 size = Vector3.Scale(roof.size, rt.localScale), centre = Vector3.Scale(roof.center, rt.localScale);
            var pivot = new GameObject("smokehole collider"); pivot.layer = roof.gameObject.layer;
            pivot.transform.SetParent(rt.parent, false); pivot.transform.localPosition = rt.localPosition; pivot.transform.localRotation = rt.localRotation;
            float half = BuildKit.TurfMesh.HoleHalf, along = half / Mathf.Cos(rt.localEulerAngles.x * Mathf.Deg2Rad), w = size.x / 2, l = size.z / 2;
            void Add(float x0, float x1, float z0, float z1)
            {
                var b = pivot.AddComponent<BoxCollider>();
                b.center = centre + new Vector3((x0 + x1) / 2, 0, (z0 + z1) / 2); b.size = new Vector3(x1 - x0, size.y, z1 - z0);
            }
            Add(-w, -half, -l, l); Add(half, w, -l, l); Add(-half, half, along, l); Add(-half, half, -l, -along);
            Object.DestroyImmediate(roof);
            float baseY = BuildKit.TurfMesh.HoodBase(d.Shape, d.Steep), rise = BuildKit.TurfMesh.HoodRise, reach = BuildKit.TurfMesh.HoodHalf;
            float angle = Mathf.Atan2(rise, reach) * Mathf.Rad2Deg, len = Mathf.Sqrt(rise * rise + reach * reach);
            foreach (int side in new[] { -1, 1 })
            {
                var cap = new GameObject("smokehole cap"); cap.layer = pivot.layer;
                cap.transform.SetParent(go.transform, false);
                cap.transform.localPosition = new Vector3(0, baseY + rise / 2 + 0.02f, side * reach / 2);
                cap.transform.localRotation = Quaternion.Euler(side * angle, 0, 0);
                cap.AddComponent<BoxCollider>().size = new Vector3(reach * 2, 0.04f, len);
            }
        }
        private static Piece.Requirement[] Cost(params (string item, int n)[] items)
        {
            var list = new List<Piece.Requirement>();
            foreach (var (item, n) in items)
            {
                ItemDrop drop = ObjectDB.instance.GetItemPrefab(item)?.GetComponent<ItemDrop>();
                if (drop != null) list.Add(new Piece.Requirement { m_resItem = drop, m_amount = n, m_recover = true });
            }
            return list.ToArray();
        }

        // A turf mesh for a shape, its open sides and one of four variants (its sticks and tufts): made once, shared.
        private static readonly Dictionary<(string, int, int), Mesh> Meshes = new Dictionary<(string, int, int), Mesh>();
        internal static Mesh TurfMesh(Def d, int mask, int variant)
        {
            if (Meshes.TryGetValue((d.Name, mask, variant), out Mesh have) && have != null) return have;
            var t = BuildKit.TurfMesh.Build(d.Shape, d.Steep, 7 + variant * 13, mask, d.Hole);
            var mesh = new Mesh { name = $"{d.Name} {mask} {variant}" };
            var v = new Vector3[t.Count]; var n = new Vector3[t.Count]; var uv = new Vector2[t.Count];
            for (int i = 0; i < t.Count; i++) { v[i] = new Vector3(t.V[3 * i], t.V[3 * i + 1], t.V[3 * i + 2]); n[i] = new Vector3(t.N[3 * i], t.N[3 * i + 1], t.N[3 * i + 2]); uv[i] = new Vector2(t.Uv[2 * i], t.Uv[2 * i + 1]); }
            mesh.vertices = v; mesh.normals = n; mesh.uv = uv; mesh.colors = Enumerable.Repeat(Color.white, t.Count).ToArray(); // (the game's piece shader reads the vertex colour)
            mesh.subMeshCount = BuildKit.TurfMesh.Slots;
            for (int k = 0; k < BuildKit.TurfMesh.Slots; k++) mesh.SetTriangles(t.T[k], k);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Owned.Add(mesh); Meshes[(d.Name, mask, variant)] = mesh;
            return mesh;
        }

        // ---- materials: copies of the game's own, retextured and tinted ----
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        internal static Material[] TurfMaterials(ZNetScene scene, GameObject roof)
        {
            Material[] game = roof.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).ToArray();
            Material wood = game.FirstOrDefault(m => m.name.StartsWith("woodwall")), straw = game.FirstOrDefault(m => m.name.StartsWith("straw_roof") && !m.name.Contains("alpha")),
                strawAlpha = game.FirstOrDefault(m => m.name.StartsWith("straw_roof_alpha"));
            if (wood == null) wood = game.First();
            // The game's own turf (an unused straight piece, turf_roof): its grass mapped in three directions (any shape takes it without
            // seams) and its swaying grass blades.
            Material[] turf = scene.GetPrefab("turf_roof")?.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).ToArray() ?? new Material[0];
            Material turfTop = turf.FirstOrDefault(m => m.name.StartsWith("turfroof") && !m.name.Contains("grass")), turfGrass = turf.FirstOrDefault(m => m.name.StartsWith("turfroof_grass"));
            Material sod = turfTop != null ? Mat("sod", () => new Material(turfTop) { color = Look.SodTint }) : Mat("sod", () => Retexture(straw ?? wood, Find(scene, Look.SodTexture), new Color(0.42f, 0.5f, 0.26f), Look.SodScale));
            // (the cut edge: soil, the mud pile's, mapped the same three ways)
            Material mud = scene.GetPrefab("mudpile")?.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.name.StartsWith("mudpile") && m.shader != null && m.shader.name == "Custom/Trilinearmap");
            Material earth = mud != null ? Mat("earth", () => Matte(new Material(mud) { color = Look.EarthTint })) : turfTop != null ? Mat("earth", () => new Material(turfTop) { color = new Color(0.78f, 0.62f, 0.46f) }) : Mat("earth", () => Retexture(wood, null, new Color(0.33f, 0.25f, 0.17f), 1f));
            Material bark = Mat("bark", () => Matte(new Material(wood) { color = Look.BarkTint })); // (the boards' own wood, darkened: a bark texture with holes in it showed the sky through the edge)
            Material sticks = Mat("sticks", () => Matte(new Material(wood) { color = Look.StickTint }));
            Material boards = Mat("boards", () => new Material(wood) { name = "BuildKit boards" });
            Material grass = turfGrass != null ? Mat("grass", () => new Material(turfGrass)) : Mat("grass", () => Retexture(strawAlpha ?? straw ?? wood, null, new Color(0.45f, 0.58f, 0.24f), 1f));
            return new[] { sod, earth, bark, boards, grass, sticks };
        }
        // No shine (soil and bark don't glint): every gloss, metal and specular setting the shader has, off.
        internal static Material Matte(Material m)
        {
            for (int i = 0; i < m.shader.GetPropertyCount(); i++)
            {
                string n = m.shader.GetPropertyName(i); string l = n.ToLowerInvariant();
                var t = m.shader.GetPropertyType(i);
                if ((t == UnityEngine.Rendering.ShaderPropertyType.Float || t == UnityEngine.Rendering.ShaderPropertyType.Range) && (l.Contains("gloss") || l.Contains("metal") || l.Contains("smooth") || l.Contains("spec"))) m.SetFloat(n, 0);
                if (t == UnityEngine.Rendering.ShaderPropertyType.Color && l.Contains("spec")) m.SetColor(n, Color.black);
            }
            return m;
        }
        // No holes: a texture whose alpha cuts out a shape (a hide's outline, fur tufts) drawn whole.
        internal static Material Solid(Material m)
        {
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            return m;
        }
        private static Material Mat(string key, Func<Material> make)
        {
            if (Mats.TryGetValue(key, out Material m) && m != null) return m;
            m = make(); m.name = "BuildKit " + key; Owned.Add(m); Mats[key] = m;
            return m;
        }
        // A copy of a game material with another texture (its normal map too, if it has one), tinted.
        private static Material Retexture(Material from, (Texture main, Texture bump)? tex, Color tint, float scale)
        {
            var m = new Material(from) { color = tint };
            if (tex is var t && t != null && t.Value.main != null)
            {
                m.SetTexture("_MainTex", t.Value.main);
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", t.Value.bump);
            }
            m.mainTextureScale = Vector2.one * scale;
            return m;
        }
        // A texture from one of the game's prefabs: "prefab/material" (the first material whose name starts so), its main and normal maps.
        internal static (Texture, Texture)? Find(ZNetScene scene, string where)
        {
            if (string.IsNullOrEmpty(where)) return null;
            string[] p = where.Split('/');
            GameObject go = scene.GetPrefab(p[0]);
            Material m = go?.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(x => x != null && (p.Length < 2 || x.name.StartsWith(p[1])));
            if (m == null) { Plugin.Warn("No texture at " + where); return null; }
            return (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null, m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null);
        }
    }

    // Where the turf's textures come from (tried in game: kit mats <filter> lists candidates).
    internal static class Look
    {
        public static string SodTexture = "", EarthTexture = "", BarkTexture = "BirchSeeds/birch_seeds";
        public static float SodScale = 0.9f;
        public static Color SodTint = Color.white, EarthTint = new Color(0.7f, 0.6f, 0.5f), BarkTint = new Color(0.42f, 0.34f, 0.27f), StickTint = new Color(0.6f, 0.6f, 0.58f);
    }
}
