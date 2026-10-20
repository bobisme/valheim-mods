using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildKit
{
    // On each turf roof piece standing in the world: which of its sides are open (torn edge, grass hanging over) and which meet another
    // roof piece (left clean, so the two sods meet without a lump or a gap), looked at when it loads, now and then after, and when a
    // neighbour comes or goes. Its look is chosen to match: one mesh per shape, open sides and variant, shared (Kit.TurfMesh).
    internal sealed class TurfLook : MonoBehaviour
    {
        internal const string LookName = "BuildKit turf";
        private Kit.Def _def; private ZNetView _view; private int _mask = -1; private float _next;
        private static readonly Collider[] Hits = new Collider[16];
        private static int _layers;
        internal static readonly HashSet<TurfLook> All = new HashSet<TurfLook>();
        private void OnEnable() { All.Add(this); SmokeThrough.Add(gameObject); }
        private void OnDisable() { All.Remove(this); SmokeThrough.Remove(gameObject); }

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            string name = Utils.GetPrefabName(gameObject);
            _def = Kit.Defs.FirstOrDefault(d => d.Name == name);
        }
        private void Start()
        {
            if (_def == null || _view == null || !_view.IsValid()) return; // (a placement ghost keeps the prefab's look)
            Refresh();
            Neighbours(transform.position, this);
            _next = Time.time + 2f + Random.value; // (again soon: pieces loading around it come in over a few frames)
        }
        private void Update()
        {
            if (_def == null || Time.time < _next) return;
            _next = Time.time + 8f + 4f * Random.value;
            if (_view == null || !_view.IsValid()) return;
            Refresh();
        }
        private void OnDestroy()
        {
            if (_def != null && _view != null && _view.IsValid() && ZNetScene.instance != null) Neighbours(transform.position, this);
        }
        // The turf pieces around a place look at their sides again (one came or went).
        internal static void Neighbours(Vector3 at, TurfLook except)
        {
            foreach (TurfLook t in All.Where(x => x != null && Vector3.Distance(x.transform.position, at) < 3.2f).ToList()) if (t != except && t != null) t._next = Mathf.Min(t._next, Time.time + 0.2f);
        }

        internal void Refresh()
        {
            int mask = 0;
            for (int side = 0; side < 4; side++) if (Open(side)) mask |= 1 << side;
            if (mask == _mask) return;
            _mask = mask;
            int variant = Variant(transform.position);
            Mesh mesh = Kit.TurfMesh(_def, mask, variant);
            if (mesh == null) return;
            // (and this copy's materials: a piece built before the mod was reloaded still has the last copy's)
            Material[] mats = Kit.Made.TryGetValue(_def.Name, out GameObject prefab) && prefab != null ? prefab.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == LookName)?.sharedMaterials : null;
            foreach (MeshFilter f in GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.name != LookName) continue;
                f.sharedMesh = mesh;
                if (mats != null && f.GetComponent<MeshRenderer>() is MeshRenderer r) r.sharedMaterials = mats;
            }
        }
        internal int Mask => _mask;
        // (the same piece in the same place always gets the same one)
        internal static int Variant(Vector3 p) => ((Mathf.RoundToInt(p.x * 2) * 73856093) ^ (Mathf.RoundToInt(p.z * 2) * 19349663) ^ (Mathf.RoundToInt(p.y * 2) * 83492791)) & 3;

        // Nothing built just past this side: two spots along it, each a little out from the edge (far enough out not to find a wall
        // under the eave; this piece's own collider doesn't count).
        private bool Open(int side)
        {
            if (_layers == 0) _layers = LayerMask.GetMask("piece", "piece_nonsolid");
            Piece me = GetComponent<Piece>();
            foreach (float t in new[] { 0.5f, 1.5f })
            {
                var (x, z) = Edge(side, t);
                float ox = side == 1 ? 1 : side == 3 ? -1 : 0, oz = side == 0 ? -1 : side == 2 ? 1 : 0;
                float h = Roofs.Height(_def.Shape, _def.Steep, x, z);
                // (a short upright capsule: a neighbour's roof may go on up, level or down from here, as steep as 45°)
                Vector3 lo = transform.TransformPoint(new Vector3(x + ox * 0.32f, h - 0.4f, z + oz * 0.32f)), hi = transform.TransformPoint(new Vector3(x + ox * 0.32f, h + 0.45f, z + oz * 0.32f));
                int n = Physics.OverlapCapsuleNonAlloc(lo, hi, 0.14f, Hits, _layers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    Piece other = Hits[i] != null ? Hits[i].GetComponentInParent<Piece>() : null;
                    if (other != null && other != me) return false;
                }
            }
            return true;
        }
        private static (float x, float z) Edge(int side, float t) => side == 0 ? (-1 + t, -1) : side == 1 ? (1, -1 + t) : side == 2 ? (1 - t, 1) : (-1, 1 - t);
    }
}
