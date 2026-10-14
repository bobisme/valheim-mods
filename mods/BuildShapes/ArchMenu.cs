using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private bool _archMenu, _archRiseSet;
        private float _archRise;
        private Rect _archRect;
        private bool _archRectPlaced;

        private void OpenArchMenu()
        {
            _archMenu = true; _menuOpenedFrame = Time.frameCount;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        private void CloseArchMenu()
        { _archMenu = false; _editingNumber = false; _numberEdits.Clear(); _numberError = null; }

        private void ConfirmArch(Player player)
        {
            float rise = _archRise;
            if (!ReadNumber("Height", 0, Arch.MaximumRise, ref rise)) return;
            _archRise = rise; _numberEdits.Clear(); _numberError = null;
            Preview(); Submit(player);
        }
        private void DrawArchMenu() => DrawOptionsWindow(ref _archRect, ref _archRectPlaced, 470, 194739, ArchContents);

        private void ArchContents(int id)
        {
            GUILayout.BeginArea(new Rect(20, 14, _archRect.width-40, _archRect.height-28));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Build an arch", _menuTitle, GUILayout.Height(32));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", _menuClose, GUILayout.Width(38), GUILayout.Height(34))) CloseArchMenu();
            GUILayout.EndHorizontal();
            GUILayout.Label($"{_selected} · {_output.Count} pieces", _menuText);
            if (_markers.Count == 2)
                GUILayout.Label($"Span: {Arch.HorizontalSpan(V(_markers[0]), V(_markers[1])):0.##} m", _menuHint);
            GUILayout.Space(16);
            GUILayout.Label($"Center rise: {Number(_archRise)} m", _menuText);
            float rise = _archRise;
            float slider = Mathf.Round(GUILayout.HorizontalSlider(rise, 0, Arch.MaximumRise)*20)/20;
            // A rounded slider display must not overwrite an exact typed value unless dragged.
            if (slider != Mathf.Round(rise*20)/20)
            { rise = slider; _numberEdits.Remove("Height"); _numberError = null; }
            NumberControlRow("Height", "m", 0, Arch.MaximumRise, 0.05f, ref rise);
            if (rise != _archRise) { _archRise = rise; Preview(); }
            GUILayout.Space(10);
            GUILayout.Label("Height is measured above the line between your endpoints. The gold guide marks the center.", _menuHint);
            GUILayout.Space(8);
            GUILayout.Label("Type an exact height, then Enter or Apply. Hold Shift for 0.01 m steps.", _menuHint);
            GUILayout.FlexibleSpace();
            string problem = _numberError ?? _previewError;
            GUILayout.Label(problem ?? $"{_output.Count} ghosts ready. Normal materials and support apply.", problem != null ? _menuWarning : _menuText);
            GUILayout.Space(8);
            bool enabled = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = enabled && _output.Count > 0;
            if (GUILayout.Button("Confirm", _menuButton, GUILayout.Height(34))) ConfirmArch(Player.m_localPlayer);
            GUI.enabled = enabled;
            if (GUILayout.Button("Edit end", _menuButton, GUILayout.Height(34)))
            { CloseArchMenu(); if (_markers.Count > 0) _markers.RemoveAt(_markers.Count-1); Preview(); }
            if (GUILayout.Button("Cancel", _menuButton, GUILayout.Height(34))) Stop();
            GUILayout.EndHorizontal();
            GUILayout.Label("Esc: close options · L: reopen · F4: modes", _menuHint);
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, _archRect.width-60, 50));
        }
    }
}
