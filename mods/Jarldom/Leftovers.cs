using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Hot reloads: what an older copy of the mod made for itself (materials, meshes, icons, sounds) is handed to the next copy, which puts
    // its own in their place wherever they're still in use (standing pieces, items in bags and chests, sounds playing) and destroys the
    // rest. The game never frees these by itself (they're kept out of its clean-up), so every reload used to leave a full set behind
    // (about 35 MB) until the game restarted. One left in use with nothing of this copy's to take its place is kept, and offered again.
    internal static class Leftovers
    {
        private const string Key = "Jarldom.Leftovers"; // (the process-wide slot one copy hands over through: copies share no statics)
        private static readonly List<Object> Pending = new List<Object>();
        internal static int Freed, Kept; // (the last settling's counts, for jarldom mem)

        // The old copy, unloading: hand over what it made, and whatever it was still holding from before.
        internal static void HandOver(IEnumerable<Object> owned)
        {
            var all = AppDomain.CurrentDomain.GetData(Key) as List<Object> ?? new List<Object>();
            all.AddRange(owned.Concat(Pending).Where(o => o != null).Distinct());
            Pending.Clear();
            AppDomain.CurrentDomain.SetData(Key, all);
        }
        // This copy: take what the last one handed over.
        internal static void TakeOver()
        {
            if (AppDomain.CurrentDomain.GetData(Key) is List<Object> handed) Pending.AddRange(handed.Where(o => o != null));
            AppDomain.CurrentDomain.SetData(Key, null);
            Settle();
        }
        // One of this copy's own, replaced (an icon drawn again in the world's light): settled with the rest.
        internal static void Retire(Object o) { if (o == null) return; Assets.Owned.Remove(o); Pending.Add(o); }

        // Swap each pending one still in use for this copy's like it, and destroy every one no longer in use.
        internal static void Settle()
        {
            Pending.RemoveAll(o => o == null);
            if (Pending.Count == 0) return;
            var old = new HashSet<Object>(Pending);
            var mine = new Dictionary<string, Object>();
            foreach (Object o in Assets.Owned) { string k = KeyOf(o); if (k != null && !old.Contains(o) && !mine.ContainsKey(k)) mine[k] = o; }
            var used = new HashSet<Object>();
            T Swap<T>(T o) where T : Object
            {
                if (o == null || !old.Contains(o)) return o;
                string k = KeyOf(o);
                if (k != null && mine.TryGetValue(k, out Object n) && n is T t) return t;
                used.Add(o); return o;
            }
            // (only what could hold one: the first icons alone, retired on entering a world, want no sweep of every renderer)
            bool looks = Pending.Any(o => o is Material || o is Mesh), sounds = Pending.Any(o => o is AudioClip), icons = Pending.Any(o => o is Sprite);
            if (looks) foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                Material[] mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++) { Material m = Swap(mats[i]); if (m != mats[i]) { mats[i] = m; changed = true; } }
                if (changed) r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer s) { Mesh m = Swap(s.sharedMesh); if (m != s.sharedMesh) s.sharedMesh = m; }
            }
            if (looks) foreach (MeshFilter f in Resources.FindObjectsOfTypeAll<MeshFilter>()) { Mesh m = Swap(f.sharedMesh); if (m != f.sharedMesh) f.sharedMesh = m; }
            if (looks) foreach (MeshCollider c in Resources.FindObjectsOfTypeAll<MeshCollider>()) { Mesh m = Swap(c.sharedMesh); if (m != c.sharedMesh) c.sharedMesh = m; }
            if (sounds) foreach (AudioSource a in Resources.FindObjectsOfTypeAll<AudioSource>()) { AudioClip c = Swap(a.clip); if (c != a.clip) a.clip = c; }
            if (icons) foreach (Piece p in Resources.FindObjectsOfTypeAll<Piece>()) p.m_icon = Swap(p.m_icon);
            // (an item's icons: a dropped one's own, and every bag's and chest's loaded; items made from one prefab share the array)
            var seen = new HashSet<ItemDrop.ItemData.SharedData>();
            void Icons(ItemDrop.ItemData item)
            {
                Sprite[] icons = item?.m_shared?.m_icons;
                if (icons == null || !seen.Add(item.m_shared)) return;
                for (int i = 0; i < icons.Length; i++) icons[i] = Swap(icons[i]);
            }
            if (icons) foreach (ItemDrop d in Resources.FindObjectsOfTypeAll<ItemDrop>()) Icons(d.m_itemData);
            if (icons) foreach (Humanoid h in Object.FindObjectsOfType<Humanoid>()) foreach (var item in h.GetInventory()?.GetAllItems() ?? new List<ItemDrop.ItemData>()) Icons(item);
            if (icons) foreach (Container c in Object.FindObjectsOfType<Container>()) foreach (var item in c.GetInventory()?.GetAllItems() ?? new List<ItemDrop.ItemData>()) Icons(item);
            // (an icon's picture goes with its icon)
            foreach (Object o in used.ToList()) if (o is Sprite s && s.texture != null && old.Contains(s.texture)) used.Add(s.texture);
            Freed = 0; Kept = 0;
            foreach (Object o in Pending) { if (used.Contains(o)) Kept++; else { Object.Destroy(o); Freed++; } }
            Pending.RemoveAll(o => !used.Contains(o));
            Plugin.Log($"Jarldom: an older copy's assets: {Freed} freed, {Kept} still in use and kept");
        }
        // What makes two copies' assets alike: made the same way, under the same name.
        private static string KeyOf(Object o)
        {
            switch (o)
            {
                case Material m:
                    string color = m.HasProperty("_Color") ? m.color.ToString() : "";
                    string glow = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor").ToString() : "";
                    return $"M|{m.name}|{m.shader?.name}|{color}|{glow}|{m.mainTexture?.name}|{m.mainTextureScale}";
                case Mesh me: return $"Me|{me.name}|{me.vertexCount}|{me.subMeshCount}";
                case Sprite s: return $"S|{s.name}";
                case AudioClip a: return $"A|{a.name}|{a.samples}";
                default: return null; // (a texture: kept or freed with its sprite)
            }
        }
    }
}
