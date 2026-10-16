using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Village;

namespace Villagewright
{
    // The plan drawn on the ground before anything is built: the site's edge, the square, streets, each lot's outline with its door,
    // fields and the palisade line, with a name over each building.
    public sealed partial class Plugin
    {
        private readonly List<GameObject> _lines = new List<GameObject>();
        private Material _lineMaterial;
        private readonly List<(Vector3 at, string text)> _labels = new List<(Vector3, string)>();
        internal bool ShowPreview = true;

        internal void ClearPreview() { foreach (var go in _lines) if (go != null) Destroy(go); _lines.Clear(); _labels.Clear(); }

        private float GroundAt(double x, double z) => ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(new Vector3((float)x, 0, (float)z)) : 0;

        private void Line(IEnumerable<P2> points, Color color, float width, float lift = 0.35f, bool closed = false, System.Func<P2, float> height = null)
        {
            var pts = points.ToList(); if (closed && pts.Count > 2) pts.Add(pts[0]);
            if (pts.Count < 2) return;
            // Follow the ground: points every metre or so.
            var dense = new List<Vector3>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                P2 a = pts[i], b = pts[i + 1]; int steps = Mathf.Max(1, Mathf.CeilToInt((float)(b - a).Length / 1.5f));
                for (int s = 0; s < steps; s++) { P2 p = a + (b - a) * (s / (double)steps); dense.Add(new Vector3((float)p.X, (height?.Invoke(p) ?? GroundAt(p.X, p.Z)) + lift, (float)p.Z)); }
            }
            P2 last = pts[pts.Count - 1]; dense.Add(new Vector3((float)last.X, (height?.Invoke(last) ?? GroundAt(last.X, last.Z)) + lift, (float)last.Z));
            if (_lineMaterial == null) { Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"); _lineMaterial = new Material(sh); }
            var go = new GameObject("Villagewright preview") { layer = LayerMask.NameToLayer("Ignore Raycast") };
            var lr = go.AddComponent<LineRenderer>(); lr.useWorldSpace = true; lr.sharedMaterial = _lineMaterial;
            lr.startColor = lr.endColor = color; lr.startWidth = lr.endWidth = width;
            lr.positionCount = dense.Count; lr.SetPositions(dense.ToArray());
            _lines.Add(go);
        }

        internal void DrawPreview()
        {
            ClearPreview();
            if (!ShowPreview) return;
            if (Site != null)
            {
                var edge = Site.Plot ?? Enumerable.Range(0, 72).Select(i => Site.Centre + P2.Forward(i * 5) * Site.Radius).ToList();
                Line(edge, new Color(1f, 0.95f, 0.8f, 0.8f), 0.25f, 0.5f, closed: true);
            }
            if (Plan == null) return;
            var ink = new Color(0.25f, 0.18f, 0.12f);
            Line(Enumerable.Range(0, 48).Select(i => Plan.Square + P2.Forward(i * 7.5) * Plan.SquareRadius), new Color(0.5f, 0.38f, 0.2f), 0.3f, closed: true);
            foreach (var s in Plan.Streets)
            {
                Line(s.Points, s.Main ? new Color(0.45f, 0.28f, 0.12f) : new Color(0.55f, 0.4f, 0.22f), s.Main ? 0.3f : 0.22f);
                foreach (int side in new[] { -1, 1 })
                    Line(Geometry.Walk(s.Points, 2).Select(w => w.at + w.dir.Left * (side * s.Width / 2)).ToList(), new Color(0.4f, 0.3f, 0.18f, 0.8f), 0.08f);
            }
            foreach (var l in Plan.Lots)
            {
                // Dark pencil colours: anything bright and unlit glows through the game's bloom.
                Color c = l.Use == Use.Hall ? new Color(0.55f, 0.15f, 0.1f) : l.Use == Use.Temple ? new Color(0.25f, 0.2f, 0.5f) : l.Use == Use.House ? new Color(0.2f, 0.33f, 0.58f) : new Color(0.3f, 0.3f, 0.32f);
                Line(l.Corners(0), c, 0.22f, closed: true, height: _ => (float)l.Pad + 0.4f);
                P2 door = l.Door, outward = P2.Forward(l.Yaw) * -1.5;
                Line(new[] { door, door + outward }, new Color(0.6f, 0.45f, 0.1f), 0.3f, height: _ => (float)l.Pad + 0.5f);
                _labels.Add((new Vector3((float)l.Centre.X, (float)l.Pad + 3, (float)l.Centre.Z), l.Name + (l.Style == Style.Longhouse ? " (longhouse)" : l.Style == Style.Stave ? " (stave)" : "")));
            }
            foreach (var f in Plan.Fields)
                Line(new[] { f.World(0, 0), f.World(f.Width, 0), f.World(f.Width, f.Depth), f.World(0, f.Depth) }, new Color(0.25f, 0.45f, 0.15f), 0.2f, closed: true);
            if (Plan.Palisade.Count > 2) Line(Plan.Palisade, ink, 0.3f, 0.6f, closed: true);
            foreach (var g in Plan.Gates) _labels.Add((new Vector3((float)g.X, GroundAt(g.X, g.Z) + 3, (float)g.Z), "gate"));
            _labels.Add((new Vector3((float)Plan.Square.X, (float)Plan.SquareHeight + 4, (float)Plan.Square.Z), $"{Plan.Brief.Kind} village (seed {Plan.Brief.Seed})"));
        }

        private GUIStyle _labelStyle;
        private void OnGUI()
        {
            if (_labels.Count == 0 || Camera.main == null || Player.m_localPlayer == null) return;
            if (_labelStyle == null) _labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            Vector3 eye = Camera.main.transform.position;
            foreach (var (at, text) in _labels)
            {
                float d = Vector3.Distance(eye, at); if (d > 140) continue;
                Vector3 s = Camera.main.WorldToScreenPoint(at); if (s.z < 0) continue;
                var r = new Rect(s.x - 150, Screen.height - s.y - 12, 300, 24);
                GUI.color = new Color(0, 0, 0, 0.7f); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, _labelStyle);
                GUI.color = new Color(1f, 0.95f, 0.8f); GUI.Label(r, text, _labelStyle);
            }
            GUI.color = Color.white;
        }
    }
}
