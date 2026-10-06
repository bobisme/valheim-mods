using System.Collections.Generic;
using System.Globalization;
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
        private GUIStyle _menuTitle, _menuText, _menuButton, _menuNumber;
        private readonly Dictionary<string,string> _numberEdits=new Dictionary<string,string>();
        private Vector2 _repeatOptionsScroll;
        private bool _editingNumber;
        private string _numberError;
        private const string NumberControl="BuildShapes Repeat number ";

        private void OpenRepeatMenu()
        {
            _repeatMenu=true; _menuOpenedFrame=Time.frameCount;
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
        }
        private void CloseRepeatMenu()
        {_repeatMenu=false;_editingNumber=false;_numberEdits.Clear();_numberError=null;}

        private static string Number(float value) => value.ToString("0.######",CultureInfo.InvariantCulture);
        // DeltaAngle wraps via 360, which adds avoidable float noise to small negative edits.
        private static float MenuAngle(float value) => value>=-180 && value<=180?value:Mathf.DeltaAngle(0,value);
        private bool ReadNumber(string key,float min,float max,ref float value)
        {
            if(!_numberEdits.TryGetValue(key,out string text))return true;
            if((!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float next) &&
                !float.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out next)) ||
                float.IsNaN(next) || float.IsInfinity(next) || next<min || next>max)
            {_numberError=$"{key}: enter a number from {Number(min)} to {Number(max)}.";return false;}
            value=next;return true;
        }
        private void ConfirmRepeat(Player player)
        {
            float spacing=SafeSpacing(),yaw=MenuAngle(_yaw),pitch=MenuAngle(_pitch),roll=MenuAngle(_roll);
            if(!ReadNumber("Spacing",0.25f,16,ref spacing) || !ReadNumber("Yaw",-180,180,ref yaw) ||
                !ReadNumber("Pitch",-180,180,ref pitch) || !ReadNumber("Roll",-180,180,ref roll))return;
            _spacing.Value=spacing;_yaw=yaw;_pitch=pitch;_roll=roll;
            _numberEdits.Clear();_numberError=null;
            Preview();Submit(player);
        }
        private bool NumberControlRow(string key,string unit,float min,float max,float step,ref float value)
        {
            bool fine=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
            if(fine)step/=key=="Spacing"?5:10;
            string control=NumberControl+key;
            // Capture Return before the text field consumes it. It applies this field, never the plan.
            bool enter=GUI.GetNameOfFocusedControl()==control && Event.current.type==EventType.KeyDown &&
                (Event.current.keyCode==KeyCode.Return || Event.current.keyCode==KeyCode.KeypadEnter);
            float next=value;
            GUILayout.BeginHorizontal();
            bool minus=GUILayout.Button("− "+Number(step),_menuButton,GUILayout.Width(68));
            GUI.SetNextControlName(control);
            string text=_numberEdits.TryGetValue(key,out string edit)?edit:Number(value);
            string typed=GUILayout.TextField(text,24,_menuNumber,GUILayout.MinWidth(64));
            if(typed!=text){_numberEdits[key]=typed;_numberError=null;}
            GUILayout.Label(unit,_menuText,GUILayout.Width(18));
            bool apply=GUILayout.Button("Apply",_menuButton,GUILayout.Width(60))||enter;
            bool plus=GUILayout.Button("+ "+Number(step),_menuButton,GUILayout.Width(68));
            GUILayout.EndHorizontal();
            if(enter)Event.current.Use();
            if(apply && ReadNumber(key,min,max,ref next))
            {_numberEdits.Remove(key);_numberError=null;GUI.FocusControl(null);}
            if(minus||plus)
            {
                // A typed pending value is the starting point for a nudge when valid.
                if(!ReadNumber(key,min,max,ref next))return false;
                next=Mathf.Clamp((float)System.Math.Round(next+(minus?-step:step),4),min,max);
                _numberEdits.Remove(key);_numberError=null;GUI.FocusControl(null);
            }
            if(next==value)return false;
            value=next;return true;
        }
        private void DrawRepeatMenu()
        {
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
            if(_menuTitle==null)
            {
                _menuTitle=new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold};
                _menuText=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true};
                _menuButton=new GUIStyle(GUI.skin.button){fontSize=14};
                _menuNumber=new GUIStyle(GUI.skin.textField){fontSize=14};
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
            finally
            {
                _editingNumber=_repeatMenu && (GUI.GetNameOfFocusedControl()??"").StartsWith(NumberControl);
                GUI.matrix=saved;GUI.enabled=enabled;
            }
        }
        private void RepeatContents(int id)
        {
            GUILayout.BeginArea(new Rect(18,30,_repeatRect.width-36,_repeatRect.height-44));
            GUILayout.Label("Repeat",_menuTitle);
            GUILayout.Label($"{_seed.Prefab} · {_output.Count} pieces",_menuText);
            _repeatOptionsScroll=GUILayout.BeginScrollView(_repeatOptionsScroll);
            GUILayout.Space(8);
            bool changed=DrawRepeatAnchors();
            GUILayout.Space(8);
            GUILayout.Label($"Spacing: {SafeSpacing():0.##} m",_menuText);
            float spacing=SafeSpacing();
            float slide=Mathf.Round(GUILayout.HorizontalSlider(spacing,0.25f,16f)*20)/20;
            if(slide!=Mathf.Round(spacing*20)/20){spacing=slide;_numberEdits.Remove("Spacing");_numberError=null;}
            changed|=NumberControlRow("Spacing","m",0.25f,16f,0.05f,ref spacing);
            GUILayout.BeginHorizontal();
            foreach(float preset in new[]{1f,2f,4f})if(GUILayout.Button($"{preset:0} m",_menuButton))
            {spacing=preset;_numberEdits.Remove("Spacing");_numberError=null;}
            GUILayout.EndHorizontal();
            if(spacing!=SafeSpacing()){_spacing.Value=spacing;changed=true;}
            GUILayout.Label("Spacing fits evenly to both ends of the path.",_menuText);
            GUILayout.Space(8);
            bool follow=GUILayout.Toggle(_follow.Value,"Turn with the curve (keep initial heading)");
            if(follow!=_follow.Value){_follow.Value=follow;changed=true;}
            bool Rotation(string label,ref float value)
            {
                string key=label.Split(' ')[0];
                GUILayout.Label($"{label}: {Number(MenuAngle(value))}°",_menuText);
                float next=MenuAngle(value);
                float slider=Mathf.Round(GUILayout.HorizontalSlider(next,-180,180));
                if(slider!=Mathf.Round(next)){next=slider;_numberEdits.Remove(key);_numberError=null;}
                NumberControlRow(key,"°",-180,180,1,ref next);
                if(next==MenuAngle(value))return false;
                value=next;return true;
            }
            changed|=Rotation("Yaw — turn",ref _yaw);
            changed|=Rotation("Pitch — lean forward",ref _pitch);
            changed|=Rotation("Roll — lean sideways",ref _roll);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Reset rotation",_menuButton))
            {_yaw=_pitch=_roll=0;foreach(string key in new[]{"Yaw","Pitch","Roll"})_numberEdits.Remove(key);_numberError=null;changed=true;}
            if(GUILayout.Button("Turn 90°",_menuButton))
            {_yaw=Mathf.Repeat(_yaw+90,360);_numberEdits.Remove("Yaw");_numberError=null;changed=true;}
            GUILayout.EndHorizontal();
            if(changed)Preview();
            GUILayout.Label("Type a value, then Enter or Apply. Hold Shift for finer ± steps.",_menuText);
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            GUILayout.Label(_numberError??_previewError??$"{_output.Count} ghosts ready. Normal materials and support apply.",_menuText);
            bool enabled=GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled=enabled && _output.Count>0;
            if(GUILayout.Button("Confirm",_menuButton,GUILayout.Height(34)))ConfirmRepeat(Player.m_localPlayer);
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
