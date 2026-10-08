using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private bool _modeMenu;
        private Rect _modeRect;
        private bool _modeRectPlaced;
        private Vector2 _modeScroll;

        private void OpenModeMenu()
        {
            CloseRepeatMenu(); CloseArchMenu();
            _modeMenu = true; _menuOpenedFrame = Time.frameCount;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        private void CloseModeMenu() { _modeMenu = false; _editingNumber = false; }
        private void DrawModeMenu() => DrawOptionsWindow(ref _modeRect, ref _modeRectPlaced, 560, 194740, ModeContents, true);

        private void ChooseMode(Tool requested)
        {
            Player player = Player.m_localPlayer;
            Piece piece = player.GetSelectedPiece();
            string selected = piece != null ? Utils.GetPrefabName(piece.gameObject) : "";
            // Choosing the current mode resumes its path; switching discards only the local preview.
            if (requested == _tool && selected == _selected)
            {
                CloseModeMenu();
                if (_tool == Tool.Arch && _markers.Count == 2) OpenArchMenu();
                else if (_tool == Tool.Repeat && _markers.Count == 3) OpenRepeatMenu();
            }
            else Begin(player, requested);
        }
        private void ModeContents(int id)
        {
            GUILayout.BeginArea(new Rect(20, 14, _modeRect.width-40, _modeRect.height-28));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Build Shapes", _menuTitle, GUILayout.Height(32));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", _menuClose, GUILayout.Width(38), GUILayout.Height(34))) CloseModeMenu();
            GUILayout.EndHorizontal();
            GUILayout.Label("Choose a building tool", _menuText);
            GUILayout.Space(12);
            Piece piece = Player.m_localPlayer.GetSelectedPiece();
            string selected = piece != null ? Utils.GetPrefabName(piece.gameObject) : "";
            bool beam = selected.Contains("beam") || selected.Contains("pole");
            bool extended = _planner.Extended;
            bool enabled = GUI.enabled;
            Tool chosen = Tool.None;
            _modeScroll = GUILayout.BeginScrollView(_modeScroll);
            void Mode(Tool tool, string detail, bool available)
            {
                GUI.enabled = enabled && available && piece != null;
                string label = tool.ToString() + (_tool == tool ? "  ·  active" : "");
                if (GUILayout.Button(label, _tool == tool ? _menuSelected : _menuButton, GUILayout.Height(36))) chosen = tool;
                GUI.enabled = enabled;
                GUILayout.Label(detail, _menuHint);
                GUILayout.Space(10);
            }
            Mode(Tool.Curve, "Three points: start, bend, and end. Uses beams or poles.", beam);
            Mode(Tool.Arch, "Two endpoints, then adjust the center height. Uses beams or poles.", beam);
            Mode(Tool.Mirror, "Reflect a group of pieces across a marked line.", extended);
            Mode(Tool.Repeat, "Repeat a piece along a curve, with spacing and rotation controls.", extended);
            if (!beam) GUILayout.Label("Select a beam or pole in the hammer to use Curve or Arch.", _menuHint);
            if (!extended) GUILayout.Label("Update BuildOrders to enable Mirror and Repeat.", _menuWarning);
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            if (_tool != Tool.None)
            {
                GUILayout.Label("Switching tools clears the local preview. Submitted ghosts stay.", _menuHint);
                if (GUILayout.Button("Exit shape mode", _menuButton, GUILayout.Height(34))) Stop();
            }
            else if (GUILayout.Button("Cancel", _menuButton, GUILayout.Height(34))) CloseModeMenu();
            GUILayout.Label("F4 / Esc: close picker", _menuHint);
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, _modeRect.width-60, 50));
            // Defer switching until the current IMGUI layout is balanced.
            if (chosen != Tool.None) ChooseMode(chosen);
        }
    }
}
