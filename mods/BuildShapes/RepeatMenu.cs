using HarmonyLib;
using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private bool _repeatMenu;
        private int _menuOpenedFrame;
        private Rect _repeatRect;
        private bool _repeatRectPlaced;
        private GUIStyle _menuTitle, _menuText, _menuButton;

        private void OpenRepeatMenu()
        {
            _repeatMenu=true; _menuOpenedFrame=Time.frameCount;
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
        }
        private void CloseRepeatMenu() { _repeatMenu=false; }
        private void DrawRepeatMenu()
        {
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
            if(_menuTitle==null)
            {
                _menuTitle=new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold};
                _menuText=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true};
                _menuButton=new GUIStyle(GUI.skin.button){fontSize=14};
            }
            Matrix4x4 saved=GUI.matrix;
            bool enabled=GUI.enabled;
            try
            {
                float scale=Mathf.Min(Mathf.Max(0.6f,Screen.height/1080f),Screen.height/820f);
                GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
                float sw=Screen.width/scale,sh=Screen.height/scale;
                const float width=440,height=780;
                if(!_repeatRectPlaced){_repeatRect=new Rect(sw-width-25,(sh-height)/2,width,height);_repeatRectPlaced=true;}
                _repeatRect.width=width;_repeatRect.height=height;
                _repeatRect.x=Mathf.Clamp(_repeatRect.x,0,Mathf.Max(0,sw-width));
                _repeatRect.y=Mathf.Clamp(_repeatRect.y,0,Mathf.Max(0,sh-height));
                // Ignore the marker click that caused this window to appear.
                GUI.enabled=enabled && Time.frameCount>_menuOpenedFrame+1;
                _repeatRect=GUI.Window(194738,_repeatRect,RepeatContents,"Repeat along curve");
            }
            finally { GUI.matrix=saved;GUI.enabled=enabled; }
        }
        private void RepeatContents(int id)
        {
            GUILayout.BeginArea(new Rect(18,30,_repeatRect.width-36,_repeatRect.height-44));
            GUILayout.Label("Repeat",_menuTitle);
            GUILayout.Label($"{_seed.Prefab} · {_output.Count} pieces",_menuText);
            GUILayout.Space(8);
            bool changed=DrawRepeatAnchors();
            GUILayout.Space(8);
            GUILayout.Label($"Spacing: {SafeSpacing():0.##} m",_menuText);
            float spacing=Mathf.Round(GUILayout.HorizontalSlider(SafeSpacing(),0.25f,16f)*20)/20;
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("− 0.25",_menuButton))spacing=Mathf.Max(0.25f,spacing-0.25f);
            foreach(float preset in new[]{1f,2f,4f})if(GUILayout.Button($"{preset:0} m",_menuButton))spacing=preset;
            if(GUILayout.Button("+ 0.25",_menuButton))spacing=Mathf.Min(16,spacing+0.25f);
            GUILayout.EndHorizontal();
            if(spacing!=SafeSpacing()){_spacing.Value=spacing;changed=true;}
            GUILayout.Label("Spacing fits evenly to both ends of the path.",_menuText);
            GUILayout.Space(8);
            bool follow=GUILayout.Toggle(_follow.Value,"Turn with the curve (keep initial heading)");
            if(follow!=_follow.Value){_follow.Value=follow;changed=true;}
            bool Rotation(string label,ref float value)
            {
                GUILayout.Label($"{label}: {Mathf.DeltaAngle(0,value):0}°",_menuText);
                float next=Mathf.Round(GUILayout.HorizontalSlider(Mathf.DeltaAngle(0,value),-180,180));
                if(Mathf.Abs(Mathf.DeltaAngle(value,next))<0.01f)return false;
                value=next;return true;
            }
            changed|=Rotation("Yaw — turn",ref _yaw);
            changed|=Rotation("Pitch — lean forward",ref _pitch);
            changed|=Rotation("Roll — lean sideways",ref _roll);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Reset rotation",_menuButton)){_yaw=_pitch=_roll=0;changed=true;}
            if(GUILayout.Button("Turn 90°",_menuButton)){_yaw=Mathf.Repeat(_yaw+90,360);changed=true;}
            GUILayout.EndHorizontal();
            if(changed)Preview();
            GUILayout.Space(8);
            GUILayout.Label(_previewError??$"{_output.Count} ghosts ready. Normal materials and support apply.",_menuText);
            GUILayout.FlexibleSpace();
            bool enabled=GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled=enabled && _output.Count>0;
            if(GUILayout.Button("Confirm",_menuButton,GUILayout.Height(34)))Submit(Player.m_localPlayer);
            GUI.enabled=enabled;
            if(GUILayout.Button("Edit path",_menuButton,GUILayout.Height(34)))
            {CloseRepeatMenu();if(_markers.Count>0)_markers.RemoveAt(_markers.Count-1);Preview();}
            if(GUILayout.Button("Cancel",_menuButton,GUILayout.Height(34)))Stop();
            GUILayout.EndHorizontal();
            GUILayout.Label("Esc: close options · L: reopen · F4: exit",_menuText);
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0,0,_repeatRect.width,26));
        }
    }

    [HarmonyPatch(typeof(Player),"TakeInput")]
    internal static class RepeatPlayerInput
    {
        private static void Postfix(Player __instance,ref bool __result)
        {if(__instance==Player.m_localPlayer && Plugin.RepeatMenuOpen && !Plugin.ProbingInput)__result=false;}
    }
    [HarmonyPatch(typeof(PlayerController),"TakeInput")]
    internal static class RepeatControllerInput
    {
        private static void Postfix(ref bool __result) {if(Plugin.RepeatMenuOpen)__result=false;}
    }
    [HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture")]
    internal static class RepeatMouseCapture
    {
        private static bool Prefix()
        {if(!Plugin.RepeatMenuOpen)return true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return false;}
    }
    [HarmonyPatch(typeof(ZInput),"GetMouseScrollWheel")]
    internal static class RepeatScroll
    {
        private static void Postfix(ref float __result) {if(Plugin.RepeatMenuOpen)__result=0;}
    }
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]
    internal static class RepeatAttack
    {
        private static bool Prefix(Humanoid __instance,ref bool __result)
        {if(__instance!=Player.m_localPlayer || !Plugin.RepeatMenuOpen)return true;__result=false;return false;}
    }
}
