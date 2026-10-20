using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Homespun
{
    // The dye vat (the rules are Core/Dye.cs): a chest of the game's (E opens it; its contents are saved and shared as a chest's are)
    // with a tub's look. Put in cloth or clothes and one dyestuff; a minute after there's a batch to dye, what the dye runs to comes out
    // that colour and the dyestuff it took is gone. The dye in the tub shows the colour of what's steeping. Worked on the vat's owner's
    // game, never while someone has it open.
    internal sealed class DyeVat : MonoBehaviour
    {
        internal const string Since = "homespun_dye_since"; // (ZDO: the game time the batch now in it started steeping; 0: none)
        internal const string DyeChild = "dye";
        private static readonly Color Water = new Color(0.2f, 0.22f, 0.18f);
        private ZNetView _view; private Container _box; private Renderer _dye; private Color _shown = Color.clear; private float _next;

        // (read here, not from the prefab's fields: a reattached copy starts at its defaults)
        private void Awake()
        {
            _view = GetComponent<ZNetView>(); _box = GetComponent<Container>();
            _dye = transform.Find(DyeChild)?.GetComponent<Renderer>();
        }

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 1;
            if (_view == null || !_view.IsValid() || _box == null || ZNet.instance == null) return;
            Inventory inv = _box.GetInventory();
            var contents = Contents(inv);
            Show(contents);
            if (!_view.IsOwner() || _box.IsInUse()) return;
            ZDO z = _view.GetZDO();
            long since = z.GetLong(Since), now = ZNet.instance.GetTime().Ticks;
            if (Dye.Wants(contents) != null) { if (since != 0) z.Set(Since, 0L); return; }
            if (since == 0) { z.Set(Since, now); return; }
            if (TimeSpan.FromTicks(now - since).TotalSeconds < Dye.SteepSeconds) return;
            Steep(inv);
            z.Set(Since, 0L);
        }

        internal static List<(string prefab, int stack)> Contents(Inventory inv) => inv.GetAllItems().Select(i => (Prefab(i), i.m_stack)).ToList();
        private static string Prefab(ItemDrop.ItemData i) => i?.m_dropPrefab != null ? i.m_dropPrefab.name : "";

        // The batch done: each thing the dye runs to becomes its dyed self (the same item, its wear and mending kept: only what it is
        // changes), a part of a stack split off as its own; and the dyestuff it took is gone. How many things were dyed.
        internal static int Steep(Inventory inv)
        {
            List<ItemDrop.ItemData> items = inv.GetAllItems().ToList();
            var batch = Dye.Batch(items.Select(i => (Prefab(i), i.m_stack)).ToList());
            if (batch.dye == null || batch.dyed.Count == 0) return 0;
            int used = 0, done = 0, each;
            foreach (var (index, n, into) in batch.dyed)
            {
                ItemDrop.ItemData item = items[index]; GameObject to = Things.Get(into);
                if (to == null) continue;
                each = Dye.Measures(Prefab(item)) * batch.dye.PerMeasure;
                if (n >= item.m_stack) Become(item, to);
                else
                {
                    ItemDrop.ItemData part = item.Clone(); part.m_stack = n; Become(part, to);
                    if (!inv.CanAddItem(part)) continue; // (no room for the dyed part: it waits)
                    item.m_stack -= n; inv.AddItem(part);
                }
                used += n * each; done += n;
            }
            string stuff = ObjectDB.instance?.GetItemPrefab(batch.dye.Stuff)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            if (stuff != null && used > 0) inv.RemoveItem(stuff, used);
            inv.m_onChanged?.Invoke();
            return done;
        }
        private static void Become(ItemDrop.ItemData item, GameObject to)
        {
            item.m_shared = to.GetComponent<ItemDrop>().m_itemData.m_shared;
            item.m_dropPrefab = to;
        }

        // The dye in the tub: the colour of the dyestuff in it, else water.
        private void Show(List<(string prefab, int stack)> contents)
        {
            if (_dye == null) return;
            Dyestuff d = contents.Select(c => Dye.ByStuff(c.prefab)).FirstOrDefault(x => x != null);
            Color want = d != null ? Things.Paint(d) * 0.7f : Water;
            if (want == _shown) return;
            _shown = want;
            _dye.material.color = want; // (its own copy, made on first use: Unity's per-renderer material)
        }

        // What the vat says when looked at.
        internal string Status()
        {
            if (_box == null || _view == null || !_view.IsValid()) return "";
            var contents = Contents(_box.GetInventory());
            string wants = Dye.Wants(contents);
            if (wants != null) return wants;
            long since = _view.GetZDO().GetLong(Since);
            var b = Dye.Batch(contents);
            string what = $"Dyes {b.dyed.Sum(x => x.n)} {b.dye.Title}";
            if (since == 0) return what;
            double left = Dye.SteepSeconds - TimeSpan.FromTicks(ZNet.instance.GetTime().Ticks - since).TotalSeconds;
            // (done but open: it comes out dyed when the lid's shut, not while someone's looking in)
            if (left <= 0 && _box.IsInUse()) return $"{what}: ready, close the vat";
            return $"{what}: steeping, {Chain.Span((float)Math.Max(0, left))}";
        }

        private void OnDestroy() { if (_dye != null && _shown != Color.clear) Destroy(_dye.material); }
    }

    // The vat looked at: a chest's words, and what it's doing.
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class VatHover
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (__instance == null || !(__instance.GetComponent<DyeVat>() is DyeVat vat)) return;
            __result += "\n<color=#c8b48c>" + vat.Status() + "</color>";
        }
    }

    internal static class VatLook
    {
        // A disc of dye just under the rim (Models/README: the vat's numbers), drawn in a plain copy of the piece material.
        internal static void AddDye(GameObject vat, ZNetScene scene, float radius, float y)
        {
            const int segs = 20;
            var v = new Vector3[segs + 1]; var t = new int[segs * 3]; var n = new Vector3[segs + 1]; var uv = new Vector2[segs + 1];
            v[0] = new Vector3(0, y, 0); n[0] = Vector3.up; uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2 / segs;
                v[i + 1] = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius); n[i + 1] = Vector3.up; uv[i + 1] = new Vector2(0.5f + Mathf.Cos(a) / 2, 0.5f + Mathf.Sin(a) / 2);
                t[3 * i] = 0; t[3 * i + 1] = 1 + (i + 1) % segs; t[3 * i + 2] = 1 + i;
            }
            var mesh = new Mesh { name = "Homespun dye", vertices = v, normals = n, uv = uv, triangles = t, colors = Enumerable.Repeat(Color.white, segs + 1).ToArray() };
            mesh.RecalculateBounds(); Looks.WorldOwned.Add(mesh);
            Material basis = Looks.PieceMat(scene, "wood_pole");
            if (basis == null) return;
            var m = new Material(basis) { name = "Homespun dye", color = new Color(0.2f, 0.22f, 0.18f) };
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            foreach (string k in new[] { "_Glossiness", "_Smoothness" }) if (m.HasProperty(k)) m.SetFloat(k, 0.8f);
            Looks.WorldOwned.Add(m);
            var o = new GameObject(DyeVat.DyeChild) { layer = vat.layer }; o.transform.SetParent(vat.transform, false);
            o.AddComponent<MeshFilter>().sharedMesh = mesh;
            o.AddComponent<MeshRenderer>().sharedMaterial = m;
        }
    }
}
