using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Byre
{
    // The models made in Blender, embedded as json.gz with their baked albedo (<name>_albedo.png): the animals (Models/animals.py), and the
    // goods and the trough (Models/goods.py, baked high to low by tools/bake with Models/bake_conf.py: they bring a normal map and tangents too).
    // The animals are skinned to the boar's own skeleton, in the boar mesh's own space: four bone indices and weights a vertex, indices
    // into the boar's SkinnedMeshRenderer.bones (34 of them), so they take the boar's bind poses and its animations move them unchanged.
    internal static class Models
    {
        internal static readonly List<Object> Owned = new List<Object>();
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        private static JObject Json(string name)
        {
            using (Stream raw = typeof(Plugin).Assembly.GetManifestResourceStream("Byre." + name + ".json.gz"))
            {
                if (raw == null) { Plugin.Warn("No model " + name); return null; }
                using (var gz = new GZipStream(raw, CompressionMode.Decompress))
                using (var text = new StreamReader(gz)) return JObject.Parse(text.ReadToEnd());
            }
        }

        // An animal's mesh: skinned, with the donor mesh's bind poses (the same 34 bones, in the same order).
        internal static Mesh Skinned(string name, Mesh donor)
        {
            if (Meshes.TryGetValue(name, out Mesh had) && had != null) return had;
            JObject j = Json(name);
            if (j == null || donor == null) return null;
            float[] v = j["v"].ToObject<float[]>(), n = j["n"].ToObject<float[]>(), uv = j["uv"].ToObject<float[]>(), bw = j["bw"].ToObject<float[]>();
            int[] t = j["t"].ToObject<int[]>(), bi = j["bi"].ToObject<int[]>();
            int count = v.Length / 3, bones = donor.bindposes.Length;
            var verts = new Vector3[count]; var norms = new Vector3[count]; var uvs = new Vector2[count]; var weights = new BoneWeight[count];
            for (int i = 0; i < count; i++)
            {
                verts[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]); norms[i] = new Vector3(n[3 * i], n[3 * i + 1], n[3 * i + 2]); uvs[i] = new Vector2(uv[2 * i], uv[2 * i + 1]);
                // (sorted heaviest first by the exporter, as Unity wants; an index past the donor's bones would throw at draw)
                int B(int k) => bi[4 * i + k] < bones ? bi[4 * i + k] : 0;
                weights[i] = new BoneWeight { boneIndex0 = B(0), boneIndex1 = B(1), boneIndex2 = B(2), boneIndex3 = B(3), weight0 = bw[4 * i], weight1 = bw[4 * i + 1], weight2 = bw[4 * i + 2], weight3 = bw[4 * i + 3] };
            }
            var mesh = new Mesh { name = "Byre " + name };
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.triangles = t;
            mesh.boneWeights = weights; mesh.bindposes = donor.bindposes;
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Owned.Add(mesh); Meshes[name] = mesh;
            return mesh;
        }

        // A prop's mesh (items, the trough): in the prefab's own frame, one submesh.
        internal static Mesh Static(string name)
        {
            if (Meshes.TryGetValue(name, out Mesh had) && had != null) return had;
            JObject j = Json(name);
            if (j == null) return null;
            float[] v = j["v"].ToObject<float[]>(), n = j["n"].ToObject<float[]>(), uv = j["uv"].ToObject<float[]>();
            int[][] t = j["t"].ToObject<int[][]>();
            int count = v.Length / 3;
            var verts = new Vector3[count]; var norms = new Vector3[count]; var uvs = new Vector2[count]; var colours = new Color[count];
            for (int i = 0; i < count; i++) { verts[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]); norms[i] = new Vector3(n[3 * i], n[3 * i + 1], n[3 * i + 2]); uvs[i] = new Vector2(uv[2 * i], uv[2 * i + 1]); colours[i] = Color.white; }
            var mesh = new Mesh { name = "Byre " + name };
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.colors = colours; // (the piece shader reads the vertex colour)
            mesh.subMeshCount = t.Length;
            for (int k = 0; k < t.Length; k++) mesh.SetTriangles(t[k], k);
            mesh.RecalculateBounds();
            if (!SharedBaked.Baked.Tangents(mesh, j)) mesh.RecalculateTangents(); // (the tangents its normal map was baked against)
            Owned.Add(mesh); Meshes[name] = mesh;
            return mesh;
        }

        // A copy of a game material (the boar's creature material, a piece's) wearing a model's own baked albedo, and its normal map when it
        // has one; no glow, matte. Null if the albedo isn't embedded.
        internal static Material Material(string model, Material from)
        {
            string key = model + "|" + (from != null ? from.shader?.name : "");
            if (Mats.TryGetValue(key, out Material had) && had != null) return had;
            Material m = SharedBaked.Baked.Material(typeof(Plugin).Assembly, "Byre." + model, from, "Byre " + model, Owned);
            if (m == null) return null;
            // (a creature's material has the hue/saturation/value LevelEffects and Ragdoll set: neutral, so the baked colours show as they are)
            foreach (string k in new[] { "_Hue", "_Saturation", "_Value" }) if (m.HasProperty(k)) m.SetFloat(k, 0);
            Mats[key] = m;
            return m;
        }

        // Put a static model in place of what a prefab drew: a child ("Model", or an item's "attach": what the hand holds) with it (the
        // prefab's own renderers and LODs go).
        internal static GameObject Replace(GameObject go, string model, Material from, string child = "Model")
        {
            Mesh mesh = Static(model); Material mat = Material(model, from);
            if (mesh == null || mat == null) return null;
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer) continue; // (an item's shine: kept)
                GameObject o = r.gameObject;
                if (o != go) { Object.DestroyImmediate(o); continue; }
                Object.DestroyImmediate(r); if (go.GetComponent<MeshFilter>() is MeshFilter f) Object.DestroyImmediate(f);
            }
            var look = new GameObject(child) { layer = go.layer }; look.transform.SetParent(go.transform, false);
            look.AddComponent<MeshFilter>().sharedMesh = mesh;
            look.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return look;
        }

        internal static void Forget()
        {
            foreach (Object o in Owned) if (o != null) Object.Destroy(o);
            Owned.Clear(); Meshes.Clear(); Mats.Clear();
        }
    }
}
