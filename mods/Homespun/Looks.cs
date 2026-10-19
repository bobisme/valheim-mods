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
    // water, flax, linen, wool, iron, red, blue, retted, oat, darkwool, cloakwool, bronze, nettle) and draw in the game's materials, plain or grained.
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

        // A look in its own colour: copies of its materials, their colour times by (retted and line nettle on flax's models, a sickly sapling).
        internal static void Tint(GameObject look, Color by)
        {
            Renderer r = look != null ? look.GetComponent<Renderer>() : null;
            if (r == null) return;
            r.sharedMaterials = r.sharedMaterials.Select(m =>
            {
                if (m == null) return m;
                var c = new Material(m) { name = m.name + " tinted", color = m.color * by };
                Owned.Add(c); return c;
            }).ToArray();
        }

        internal static void Forget() { foreach (Object o in Owned) if (o != null) Object.Destroy(o); Owned.Clear(); Meshes.Clear(); Mats.Clear(); Pictures.Clear(); _slots = null; }

        // (as spun.py numbers them: wood, stone, water, flax, linen, wool, iron, red, blue, retted, oat, darkwool, cloakwool, bronze, nettle)
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
                Plain(wood, new Color(0.72f, 0.62f, 0.48f), "oat"),
                Plain(wood, new Color(0.45f, 0.38f, 0.3f), "darkwool"),
                Plain(wood, new Color(0.33f, 0.28f, 0.23f), "cloakwool"),
                Plain(wood, new Color(0.62f, 0.45f, 0.2f), "bronze", 0.4f),
                Plain(wood, new Color(0.24f, 0.36f, 0.15f), "nettle"),
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

        // A garment's worn look in its own colour. The game draws clothes from pictures, and where the shader takes no colour a tint
        // does nothing (0.1.0's dress was the base's brown), so the pictures themselves are recoloured: the materials of what the body
        // wears (attach_skin: a skirt, a cape) are copied with their picture made ours, and so is what the game paints on the body
        // itself (m_armorMaterial's chest or legs). The picture's light and shade stay, its colour is ours; its alpha is kept.
        internal static void Recolour(GameObject garment, ItemDrop.ItemData.SharedData shared, Color to)
        {
            var done = new Dictionary<Material, Material>();
            foreach (Transform child in garment.transform)
            {
                if (!child.name.StartsWith("attach_")) continue;
                foreach (Renderer r in child.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.GetType().Name == "ParticleSystemRenderer") continue;
                    Material[] mats = r.sharedMaterials; bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        if (!done.TryGetValue(mats[i], out Material m))
                        {
                            m = new Material(mats[i]) { name = mats[i].name + " (Homespun)" };
                            if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") is Texture t) m.SetTexture("_MainTex", Picture(t, to));
                            if (m.HasProperty("_Color")) { Color c = m.color; m.color = new Color(1, 1, 1, c.a); }
                            done[mats[i]] = m; Owned.Add(m);
                        }
                        mats[i] = m; changed = true;
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            }
            if (shared.m_armorMaterial != null)
            {
                var m = new Material(shared.m_armorMaterial) { name = shared.m_armorMaterial.name + " (Homespun)" };
                foreach (string p in new[] { "_ChestTex", "_LegsTex", "_MainTex" }) if (m.HasProperty(p) && m.GetTexture(p) is Texture t) m.SetTexture(p, Picture(t, to));
                Owned.Add(m); shared.m_armorMaterial = m;
            }
        }

        private static readonly Dictionary<(int, Color), Texture2D> Pictures = new Dictionary<(int, Color), Texture2D>();
        // A copy of a picture in a colour: each texel's brightness against the picture's average (of what isn't see-through) scales the
        // colour, a little flattened so stains and seams still show without going black. Read back through a render texture, since the
        // game's own aren't readable. Without a graphics device (a dedicated server draws nothing) the picture stays as it is.
        internal static Texture Picture(Texture src, Color to)
        {
            if (src == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return src;
            if (Pictures.TryGetValue((src.GetInstanceID(), to), out Texture2D had) && had != null) return had;
            try
            {
                int w = src.width, h = src.height;
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB), was = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = src.name + " (Homespun)", wrapMode = src.wrapMode, filterMode = src.filterMode, anisoLevel = src.anisoLevel };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt);
                Color32[] px = tex.GetPixels32();
                double sum = 0; int n = 0;
                foreach (Color32 c in px) if (c.a > 127) { sum += 0.3 * c.r + 0.59 * c.g + 0.11 * c.b; n++; }
                float mean = Mathf.Max(8f, n > 0 ? (float)(sum / n) : 128f);
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 c = px[i];
                    float k = Mathf.Clamp(Mathf.Pow((0.3f * c.r + 0.59f * c.g + 0.11f * c.b) / mean, 0.8f), 0.25f, 1.6f);
                    px[i] = new Color32((byte)(Mathf.Clamp01(to.r * k) * 255), (byte)(Mathf.Clamp01(to.g * k) * 255), (byte)(Mathf.Clamp01(to.b * k) * 255), c.a);
                }
                tex.SetPixels32(px); tex.Apply(true, false);
                Owned.Add(tex); Pictures[(src.GetInstanceID(), to)] = tex;
                return tex;
            }
            catch (Exception e) { Plugin.Warn($"Could not recolour {src.name}: {e.Message}"); return src; }
        }
    }
}
