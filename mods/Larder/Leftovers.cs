using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Larder
{
    // Hot reloads: what an older copy of the kit made (its prefabs, meshes, materials, icons) is handed to the next copy. Once that one
    // has made its own and put them in the build menu, it puts its meshes and materials in their place on the pieces standing, and
    // destroys the rest. They used to be left as they were (destroyed, standing roofs would draw magenta), so every reload kept a full
    // set until the game restarted. One still in use with nothing of this copy's like it is kept, and offered again.
    internal static class Leftovers
    {
        private const string Key = "Larder.Leftovers"; // (the process-wide slot one copy hands over through: copies share no statics)
        private static readonly List<Object> Pending = new List<Object>();
        private static bool _taken;

        // The old copy, unloading: hand over what it made, and whatever it was still holding from before.
        internal static void HandOver(IEnumerable<Object> owned)
        {
            var all = AppDomain.CurrentDomain.GetData(Key) as List<Object> ?? new List<Object>();
            all.AddRange(owned.Concat(Pending).Where(o => o != null).Distinct());
            Pending.Clear();
            AppDomain.CurrentDomain.SetData(Key, all);
        }

        // This copy, its pieces made and in the menu: take what the last one handed over, and settle it (once).
        internal static void TakeOver(List<Object> mine)
        {
            if (_taken) return;
            _taken = true;
            if (AppDomain.CurrentDomain.GetData(Key) is List<Object> handed) Pending.AddRange(handed.Where(o => o != null));
            AppDomain.CurrentDomain.SetData(Key, null);
            Pending.RemoveAll(o => o == null);
            if (Pending.Count == 0) return;
            var old = new HashSet<Object>(Pending);
            var like = new Dictionary<string, Object>();
            foreach (Object o in mine) { string k = KeyOf(o); if (k != null && !old.Contains(o) && !like.ContainsKey(k)) like[k] = o; }
            var used = new HashSet<Object>();
            T Swap<T>(T o) where T : Object
            {
                if (o == null || !old.Contains(o)) return o;
                string k = KeyOf(o);
                if (k != null && like.TryGetValue(k, out Object n) && n is T t) return t;
                used.Add(o); return o;
            }
            foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (Under(r, old)) continue; // (an old prefab's own: going with it)
                Material[] mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++) { Material m = Swap(mats[i]); if (m != mats[i]) { mats[i] = m; changed = true; } }
                if (changed) r.sharedMaterials = mats;
            }
            foreach (MeshFilter f in Resources.FindObjectsOfTypeAll<MeshFilter>()) { if (Under(f, old)) continue; Mesh m = Swap(f.sharedMesh); if (m != f.sharedMesh) f.sharedMesh = m; }
            foreach (MeshCollider c in Resources.FindObjectsOfTypeAll<MeshCollider>()) { if (Under(c, old)) continue; Mesh m = Swap(c.sharedMesh); if (m != c.sharedMesh) c.sharedMesh = m; }
            foreach (Piece p in Resources.FindObjectsOfTypeAll<Piece>()) { if (Under(p, old)) continue; p.m_icon = Swap(p.m_icon); }
            foreach (Object o in used.ToList()) if (o is Sprite s && s.texture != null && old.Contains(s.texture)) used.Add(s.texture);
            int freed = 0, kept = 0;
            foreach (Object o in Pending) { if (used.Contains(o)) kept++; else { Object.Destroy(o); freed++; } }
            Pending.RemoveAll(o => !used.Contains(o));
            Plugin.Log($"An older copy's prefabs and looks: {freed} freed, {kept} still in use and kept");
        }
        // (part of an old prefab, itself going: what it holds doesn't count as in use)
        private static bool Under(Component c, HashSet<Object> old)
        {
            for (Transform t = c.transform; t != null; t = t.parent) if (old.Contains(t.gameObject)) return true;
            return false;
        }
        // What makes two copies' alike: made the same way, under the same name.
        private static string KeyOf(Object o)
        {
            switch (o)
            {
                case Material m:
                    string color = m.HasProperty("_Color") ? m.color.ToString() : "";
                    return $"M|{m.name}|{m.shader?.name}|{color}|{m.mainTexture?.name}|{m.mainTextureScale}";
                case Mesh me: return $"Me|{me.name}|{me.vertexCount}|{me.subMeshCount}";
                case Sprite s: return $"S|{s.name}";
                default: return null; // (a prefab: replaced in the scene and menu already; a texture: goes with its sprite)
            }
        }
    }
}
