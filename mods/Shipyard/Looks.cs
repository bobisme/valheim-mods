using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Shipyard
{
    // The looks: made in Blender (Models/yard.py), baked low-poly with their own small maps (tools/bake, docs/model-baking.md), built
    // into the DLL as <name>.json.gz and <name>_albedo/_normal.png, and drawn in a copy of the game's own piece material.
    internal static class Looks
    {
        internal static readonly List<Object> Owned = new List<Object>();
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material[]> Mats = new Dictionary<string, Material[]>();

        internal static Mesh Model(string name)
        {
            if (Meshes.TryGetValue(name, out Mesh had) && had != null) return had;
            try
            {
                using (var raw = typeof(Plugin).Assembly.GetManifestResourceStream("Shipyard." + name + ".json.gz"))
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
                        var mesh = new Mesh { name = "Shipyard " + name };
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
            Material baked = SharedBaked.Baked.Material(typeof(Plugin).Assembly, "Shipyard." + name, PieceMat(scene, "wood_pole"), "Shipyard " + name + " baked", Owned);
            Material[] mats = baked != null ? new[] { baked } : null; // (only baked models ship)
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

        internal static Material PieceMat(ZNetScene scene, string prefab) => scene?.GetPrefab(prefab)?.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
        internal static void Forget() { foreach (Object o in Owned) if (o != null) Object.Destroy(o); Owned.Clear(); Meshes.Clear(); Mats.Clear(); }
    }
}
