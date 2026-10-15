using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuildShapes
{
    // A shape tool's keys, in the game's own key-hint row along the bottom of the screen (cloned from its build hints: the same
    // labels, key caps and mouse-wheel icon), standing in for the game's row while the tool is out. Above it, one line: the shape,
    // its markers and its settings.
    public sealed partial class Plugin
    {
        private struct Hint { public string Label; public string[] Keys; } // (a key named "wheel" is the mouse-wheel icon)
        private GameObject _hintRow, _hintGame; // (ours, and the game's keyboard row it stands in for while it's hidden)
        private TMP_Text _hintStatus;
        private string _hintShown;
        private Tool? _hintPin; // (Claude Tools: shapes hints <tool> shows a tool's row for pictures, changing nothing else)
        private Tool HintTool => _hintPin ?? _tool;
        private bool _hintFull; // (with it: every hint the tool can show, as if its markers were down and a shape planned)

        private void UpdateShapeHints() // (from LateUpdate: after the game has set its own hints up for the frame)
        {
            bool show = _hintPin != null && Player.m_localPlayer != null || _tool != Tool.None && !_radial && Ready(Player.m_localPlayer) && _planner.Available(Player.m_localPlayer);
            if (!show) { HideShapeHints(); return; }
            Transform build = KeyHints.instance != null && KeyHints.instance.m_buildHints != null ? KeyHints.instance.m_buildHints.transform : null;
            Transform game = build != null ? build.Find("Keyboard") : null;
            if (game == null) { HideShapeHints(); return; }
            List<Hint> hints = ShapeHints();
            string signature = string.Join("|", hints.Select(h => h.Label + ":" + string.Join("+", h.Keys)));
            if (_hintRow == null || signature != _hintShown) BuildHintRow(game, hints, signature);
            if (_hintRow == null) return;
            if (game.gameObject.activeSelf && !ZInput.IsGamepadActive()) { game.gameObject.SetActive(false); _hintGame = game.gameObject; }
            _hintRow.SetActive(true);
            string status = HintStatus();
            if (_hintStatus != null && _hintStatus.text != status) _hintStatus.text = status;
        }
        private List<Hint> ShapeHints()
        {
            var hints = new List<Hint>();
            void Add(string label, params string[] keys) => hints.Add(new Hint { Label = label, Keys = keys });
            string ctrl = "Ctrl", plan = KeyName(_plan.Value);
            Add("Marker", "Mouse-1");
            if (HintTool == Tool.Mirror) { Add("Select", ctrl, "Mouse-1"); Add("Unselect last", ctrl, KeyName(_back.Value)); }
            bool full = _hintPin != null && _hintFull;
            if (ShapeReady || full) Add("Plan", plan);
            if (ShapeScrolls || full)
            {
                if (HintTool != Tool.Mirror) { Add("Roll", "wheel"); Add("Turn", "Shift", "wheel"); }
                if (HintTool == Tool.Arch) Add("Height", ctrl, "wheel");
                if (HintTool == Tool.Curve && (_markers.Count > 2 || full)) Add("Curve", ctrl, "wheel");
                if (HintTool != Tool.Mirror) Add("Gap", ctrl, "Shift", "wheel");
            }
            if (_markers.Count > 0 || full) { Add("Back", KeyName(_back.Value)); Add("Clear", "Delete"); }
            if (_lastPlan != null || full) Add("Undo", KeyName(_undo.Value));
            Add("Shapes", "Hold " + KeyName(_radialKey.Value));
            return hints;
        }
        private string HintStatus()
        {
            string Warm(string text) => "<color=#FFDB8C>" + text + "</color>";
            var parts = new List<string> { _output.Count > 0 ? $"{_output.Count} pieces" : HintTool == Tool.Curve ? $"{_markers.Count} markers" : $"{_markers.Count}/{RequiredMarkers} markers" };
            if (HintTool == Tool.Circle && _markers.Count == 2)
                parts.Add($"radius {Vector3.ProjectOnPlane(_markers[1] - _markers[0], Vector3.up).magnitude:0.##} m");
            if (HintTool == Tool.Circle) parts.Add((!_upright ? "along the circle" : UprightPossible() ? "upright" : "side by side") + " (Home)");
            if (HintTool == Tool.Curve)
            {
                if (_closed) parts.Add("a closed loop");
                if (_markers.Count > 2) parts.Add(_curve > 0 ? $"curve {_curve * 100:0}%" : "straight sides, sharp corners");
                parts.Add((!_upright ? "along the curve" : UprightPossible() ? "upright" : "side by side") + " (Home)");
            }
            if (HintTool == Tool.Arch) parts.Add($"height {_archRise:0.##} m");
            if (HintTool != Tool.Mirror) parts.Add(_gap > 0 ? $"gaps up to {_gap:0.##} m" : "no gaps");
            if (HintTool == Tool.Mirror) parts.Add($"{_sources.Count} selected");
            else { parts.Add($"rolled {_shapeRoll:0.#}°"); parts.Add($"turned {_pieceTurn:0.#}°"); }
            string line = Warm(HintTool.ToString()) + "   " + string.Join("  ·  ", parts);
            return _previewError != null ? line + "\n<color=#FF9E6B>" + _previewError + "</color>" : line;
        }
        private static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                case KeyCode.Return: return "Enter";
                default: return key.ToString();
            }
        }
        // Our row: a copy of the game's keyboard row, emptied, then one copy of its Place hint per key (a key cap per key, the Copy
        // hint's "+" between them, the Rotate hint's wheel icon for the wheel), and the status line, in the hints' own font, above it.
        private void BuildHintRow(Transform game, List<Hint> hints, string signature)
        {
            DestroyHintRow();
            Transform place = game.Find("Place"), copy = game.Find("Copy"), rotate = game.Find("rotate");
            Transform cap = place != null ? place.Find("key_bkg") : null, label = place != null ? place.Find("Text") : null;
            Transform plus = copy != null ? copy.Find("Text (1)") : null, wheel = rotate != null ? rotate.Find("mousew_icon") : null;
            if (cap == null || label == null) return;
            bool wasActive = game.gameObject.activeSelf;
            if (!wasActive) game.gameObject.SetActive(true); // (cloned while active, so the copies start active)
            _hintRow = Instantiate(game.gameObject, game.parent, false);
            if (!wasActive) game.gameObject.SetActive(false);
            _hintRow.name = "BuildShapes hints";
            foreach (Transform child in _hintRow.transform.Cast<Transform>().ToList()) { child.SetParent(null); Destroy(child.gameObject); }
            var row = _hintRow.GetComponent<HorizontalLayoutGroup>();
            if (row != null) row.childAlignment = TextAnchor.MiddleCenter; // (centred: the game's own row is wide enough to fill the screen)
            foreach (Hint hint in hints)
            {
                var item = new GameObject(hint.Label, typeof(RectTransform));
                item.transform.SetParent(_hintRow.transform, false);
                var layout = item.AddComponent<HorizontalLayoutGroup>(); var like = place.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = like.spacing; layout.childAlignment = like.childAlignment; layout.padding = like.padding;
                layout.childControlWidth = like.childControlWidth; layout.childControlHeight = like.childControlHeight;
                layout.childForceExpandWidth = like.childForceExpandWidth; layout.childForceExpandHeight = like.childForceExpandHeight;
                Instantiate(label.gameObject, item.transform, false).GetComponent<TMP_Text>().text = hint.Label;
                for (int i = 0; i < hint.Keys.Length; i++)
                {
                    if (i > 0 && plus != null) Instantiate(plus.gameObject, item.transform, false).GetComponent<TMP_Text>().text = "+";
                    if (hint.Keys[i] == "wheel" && wheel != null)
                    {
                        // (the size the game gives it in its Favorite hint: its Rotate hint leaves a 100-wide gap)
                        var icon = Instantiate(wheel.gameObject, item.transform, false); icon.SetActive(true);
                        if (icon.TryGetComponent(out Image image)) image.preserveAspect = true;
                        var size = icon.GetComponent<LayoutElement>() ?? icon.AddComponent<LayoutElement>();
                        size.preferredWidth = size.minWidth = 45; size.preferredHeight = size.minHeight = 50; ((RectTransform)icon.transform).sizeDelta = new Vector2(45, 50);
                    }
                    else Instantiate(cap.gameObject, item.transform, false).GetComponentInChildren<TMP_Text>().text = hint.Keys[i];
                }
            }
            var status = new GameObject("BuildShapes status", typeof(RectTransform));
            status.transform.SetParent(_hintRow.transform.parent, false);
            var rect = (RectTransform)status.transform; var rowRect = (RectTransform)_hintRow.transform;
            rect.anchorMin = rowRect.anchorMin; rect.anchorMax = rowRect.anchorMax; rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = rowRect.anchoredPosition + new Vector2(0, 48); rect.sizeDelta = new Vector2(1400, 60);
            var like2 = label.GetComponent<TMP_Text>();
            _hintStatus = status.AddComponent<TextMeshProUGUI>();
            _hintStatus.font = like2.font; _hintStatus.fontSharedMaterial = like2.fontSharedMaterial; _hintStatus.fontSize = like2.fontSize * 1.1f;
            _hintStatus.color = like2.color; _hintStatus.alignment = TextAlignmentOptions.Bottom; _hintStatus.richText = true; _hintStatus.raycastTarget = false;
            status.transform.SetParent(_hintRow.transform, true); // (shown and hidden with the row, but left out of its layout)
            status.AddComponent<LayoutElement>().ignoreLayout = true;
            _hintShown = signature;
        }
        private void HideShapeHints()
        {
            if (_hintRow != null && _hintRow.activeSelf) _hintRow.SetActive(false);
            if (_hintGame != null) { if (!ZInput.IsGamepadActive()) _hintGame.SetActive(true); _hintGame = null; }
        }
        private void DestroyHintRow()
        {
            if (_hintRow != null) Destroy(_hintRow);
            _hintRow = null; _hintStatus = null; _hintShown = null;
        }
    }
}
