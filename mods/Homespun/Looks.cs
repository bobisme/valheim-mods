using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Homespun
{
    // The looks: models made in Blender (Models/spun.py), baked onto low-poly copies with their own small maps (tools/bake,
    // docs/model-baking.md) and built into the DLL as <name>.json.gz and <name>_albedo/_normal.png, drawn in a copy of the game's own
    // piece material, as Bathhouse's are. Not yet baked, a model's triangles come one list per material slot (spun.py's: wood, stone,
    // water, flax, linen, wool, iron, red, blue, retted) and draw in the game's materials, plain or grained.
    internal static class Looks
    {
        internal static readonly List<Object> Owned = new List<Object>();
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material[]> Mats = new Dictionary<string, Material[]>();
        private static Material[] _slots;

        internal static Mesh Model(string name)
        {
            if (Meshes.TryGetValue(name, out Mesh had) && had != null) return had;
            try
            {
                using (var raw = typeof(Plugin).Assembly.GetManifestResourceStream("Homespun." + name + ".json.gz"))
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
                        var mesh = new Mesh { name = "Homespun " + name };
                        if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.colors = Enumerable.Repeat(Color.white, verts.Length).ToArray(); // (the piece shader reads the vertex colour)
                        mesh.subMeshCount = t.Length;
                        for (int k = 0; k < t.Length; k++) mesh.SetTriangles(t[k], k);
                        mesh.RecalculateBounds();
                        if (!SharedBaked.Baked.Tangents(mesh, j)) mesh.RecalculateTangents(); // (a baked model brings the tangents its normal map was baked against)
                        Owned.Add(mesh); Meshes[name] = mesh;
                        return mesh;
                    }
                }
            }
            catch (Exception e) { Plugin.Warn($"The {name} model: {e.Message}"); return null; }
        }

        // The materials a model draws in: its own baked one, or else the slot materials.
        internal static Material[] For(ZNetScene scene, string name)
        {
            if (Mats.TryGetValue(name, out Material[] had) && had.All(m => m != null)) return had;
            Material baked = SharedBaked.Baked.Material(typeof(Plugin).Assembly, "Homespun." + name, PieceMat(scene, "wood_pole"), "Homespun " + name + " baked", Owned);
            Material[] mats = baked != null ? new[] { baked } : Slots(scene);
            if (mats != null) Mats[name] = mats;
            return mats;
        }
        // A model as a child of a prefab, named "Model" (what the build-menu picture is drawn from) or as given.
        internal static GameObject Add(Transform parent, ZNetScene scene, string name, string child = "Model")
        {
            Mesh mesh = Model(name); Material[] mats = For(scene, name);
            if (mesh == null || mats == null) return null;
            var look = new GameObject(child) { layer = parent.gameObject.layer }; look.transform.SetParent(parent, false);
            look.AddComponent<MeshFilter>().sharedMesh = mesh;
            look.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return look;
        }

        internal static void Forget() { foreach (Object o in Owned) if (o != null) Object.Destroy(o); Owned.Clear(); Meshes.Clear(); Mats.Clear(); _slots = null; }

        // (as spun.py numbers them: wood, stone, water, flax, linen, wool, iron, red, blue, retted)
        private static Material[] Slots(ZNetScene scene)
        {
            if (_slots != null && _slots.All(m => m != null)) return _slots;
            Material wood = PieceMat(scene, "wood_pole");
            if (wood == null) { Plugin.Warn("No wood_pole material to draw in."); return null; }
            _slots = new[]
            {
                Grain(wood, new Color(0.78f, 0.7f, 0.6f), "wood"),
                Plain(wood, new Color(0.62f, 0.6f, 0.56f), "stone"),
                Plain(wood, new Color(0.13f, 0.2f, 0.18f), "water", 0.85f),
                Plain(wood, new Color(0.78f, 0.68f, 0.45f), "flax"),
                Plain(wood, new Color(0.9f, 0.87f, 0.78f), "linen"),
                Plain(wood, new Color(0.66f, 0.6f, 0.52f), "wool"),
                Plain(wood, new Color(0.17f, 0.17f, 0.18f), "iron", 0.35f),
                Plain(wood, new Color(0.55f, 0.13f, 0.1f), "red"),
                Plain(wood, new Color(0.15f, 0.22f, 0.42f), "blue"),
                Plain(wood, new Color(0.62f, 0.6f, 0.55f), "retted"),
            };
            return _slots;
        }
        // (a piece's own material: the one in the game's piece shader, not its snow cap)
        internal static Material PieceMat(ZNetScene scene, string prefab) => scene?.GetPrefab(prefab)?.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
        private static Material Grain(Material basis, Color tint, string name)
        {
            var m = new Material(basis) { name = "Homespun " + name, color = tint }; m.mainTextureScale = Vector2.one * 0.6f;
            Owned.Add(m); return m;
        }
        private static Material Plain(Material basis, Color color, string name, float gloss = 0)
        {
            var m = new Material(basis) { name = "Homespun " + name, color = color };
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            foreach (string k in new[] { "_Glossiness", "_Smoothness" }) if (m.HasProperty(k)) m.SetFloat(k, gloss);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0);
            Owned.Add(m); return m;
        }

        // A garment's look tinted: its own materials (on the ground, and what's worn) copied in the colour of undyed wool or linen.
        // (What the game lays on the body's own texture for some armour, m_armorMaterial, is a texture and can't be tinted: left as it is.)
        internal static void Tint(GameObject garment, Color tint)
        {
            var done = new Dictionary<Material, Material>();
            foreach (Renderer r in garment.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetType().Name == "ParticleSystemRenderer") continue;
                Material[] mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (!done.TryGetValue(mats[i], out Material m))
                    {
                        m = new Material(mats[i]) { name = mats[i].name + " (Homespun)" };
                        if (m.HasProperty("_Color")) m.color = mats[i].color * tint;
                        done[mats[i]] = m; Owned.Add(m);
                    }
                    mats[i] = m; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
