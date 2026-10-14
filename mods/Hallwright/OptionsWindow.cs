using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Hallwright
{
    public sealed partial class Plugin
    {
        private int _menuOpenedFrame;
        private GUIStyle _menuTitle, _menuText, _menuButton, _menuNumber;
        private readonly Dictionary<string,string> _numberEdits=new Dictionary<string,string>();
        private bool _editingNumber;
        private string _numberError;
        private const string NumberControl="Hallwright number ";

        private static string Number(float value) => value.ToString("0.######",CultureInfo.InvariantCulture);
        private bool ReadNumber(string key,float min,float max,ref float value)
        {
            if(!_numberEdits.TryGetValue(key,out string text))return true;
            if((!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float next) &&
                !float.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out next)) ||
                float.IsNaN(next) || float.IsInfinity(next) || next<min || next>max)
            {_numberError=$"{key}: enter a number from {Number(min)} to {Number(max)}.";return false;}
            value=next;return true;
        }
        private bool NumberControlRow(string key,string unit,float min,float max,float step,ref float value)
        {
            bool fine=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
            if(fine)step/=key=="Spacing"||key=="Height"?5:10;
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
        private void DrawOptionsWindow(ref Rect rect, ref bool placed, float height, int id, GUI.WindowFunction contents, bool centered=false)
        {
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
            Matrix4x4 saved=GUI.matrix;
            bool enabled=GUI.enabled;
            GUISkin skin=GUI.skin;
            try
            {
                GUI.skin=Theme();
                float scale=Mathf.Min(Mathf.Max(0.6f,Screen.height/1080f),Screen.height/(height+40));
                GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
                float sw=Screen.width/scale,sh=Screen.height/scale;
                const float width=440;
                if(!placed){rect=new Rect(centered?(sw-width)/2:sw-width-25,(sh-height)/2,width,height);placed=true;}
                rect.width=width;rect.height=height;
                rect.x=Mathf.Clamp(rect.x,0,Mathf.Max(0,sw-width));
                rect.y=Mathf.Clamp(rect.y,0,Mathf.Max(0,sh-height));
                // Ignore the marker click that caused this window to appear.
                GUI.enabled=enabled && Time.frameCount>_menuOpenedFrame+1;
                rect=GUI.Window(id,rect,contents,GUIContent.none);
            }
            finally
            {
                _editingNumber=OptionsMenuOpen && (GUI.GetNameOfFocusedControl()??"").StartsWith(NumberControl);
                GUI.matrix=saved;GUI.enabled=enabled;GUI.skin=skin;
            }
        }
    }

    [HarmonyPatch(typeof(Player),"TakeInput")]
    internal static class OptionsPlayerInput
    {
        private static void Postfix(Player __instance,ref bool __result)
        {if(__instance==Player.m_localPlayer && Plugin.OptionsMenuOpen && !Plugin.ProbingInput)__result=false;}
    }
    [HarmonyPatch(typeof(PlayerController),"TakeInput")]
    internal static class OptionsControllerInput
    {
        private static void Postfix(ref bool __result) {if(Plugin.OptionsMenuOpen)__result=false;}
    }
    [HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture")]
    internal static class OptionsMouseCapture
    {
        private static bool Prefix()
        {if(!Plugin.OptionsMenuOpen)return true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return false;}
    }
    [HarmonyPatch(typeof(ZInput),"GetMouseScrollWheel")]
    internal static class OptionsScroll
    {
        private static void Postfix(ref float __result) {if(Plugin.OptionsMenuOpen)__result=0;}
    }
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]
    internal static class OptionsAttack
    {
        private static bool Prefix(Humanoid __instance,ref bool __result)
        {if(__instance!=Player.m_localPlayer || !Plugin.OptionsMenuOpen)return true;__result=false;return false;}
    }
}
