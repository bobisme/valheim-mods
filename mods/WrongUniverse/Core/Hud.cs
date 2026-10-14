using UnityEngine;

namespace WrongUniverse
{
    // Small gauges stacked under the crosshair (a gun's heat, the jetpack's fuel, a drone's battery, the uplink's cooldown), drawn from
    // each feature's Gui() with Bar(). Hidden with the game's HUD and while a menu is open.
    internal static class Hud
    {
        private static Texture2D _white;
        private static int _row;
        private static GUIStyle _label;
        internal static void Begin(){_row=0;}
        internal static bool Showing=>Player.m_localPlayer!=null&&!Player.m_localPlayer.IsDead()&&Hud_Visible()&&!InventoryGui.IsVisible()&&!Menu.IsVisible()&&!Minimap.IsOpen();
        private static bool Hud_Visible()=>global::Hud.instance!=null&&global::Hud.instance.m_rootObject!=null&&global::Hud.instance.m_rootObject.activeSelf;

        // A bar: fill 0..1 in its colour, the label to its left. flash: blinks (overheated, empty).
        internal static void Bar(string label,float fill,Color color,bool flash=false)
        {
            if(!Showing)return;
            if(_white==null){_white=new Texture2D(1,1){hideFlags=HideFlags.HideAndDontSave};_white.SetPixel(0,0,Color.white);_white.Apply();}
            if(_label==null)_label=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(13*Scale),alignment=TextAnchor.MiddleRight,fontStyle=FontStyle.Bold};
            float s=Scale,w=150*s,h=7*s;
            float x=Screen.width*0.5f-w*0.5f,y=Screen.height*0.5f+70*s+_row*16*s;
            _row++;
            if(flash&&Mathf.Repeat(Time.time*4f,1f)<0.5f)color=Color.white;
            Color was=GUI.color;
            GUI.color=new Color(0,0,0,0.55f);GUI.DrawTexture(new Rect(x-1,y-1,w+2,h+2),_white);
            GUI.color=color;GUI.DrawTexture(new Rect(x,y,w*Mathf.Clamp01(fill),h),_white);
            GUI.color=new Color(1,1,1,0.9f);
            GUI.Label(new Rect(x-160*s-6*s,y-6*s,160*s,h+12*s),label,_label);
            GUI.color=was;
        }
        private static float Scale=>Mathf.Max(0.75f,Screen.height/1080f);
    }
}
