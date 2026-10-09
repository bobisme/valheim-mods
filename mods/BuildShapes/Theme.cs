using System.Collections.Generic;
using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        // The same dark wood panel, cream text and Averia font as ChestSearch's uGUI window.
        private static readonly Color Panel=new Color(0.11f,0.09f,0.06f,0.98f),Edge=new Color(0.36f,0.29f,0.19f,1),
            Cream=new Color(0.95f,0.9f,0.8f),Muted=new Color(0.72f,0.67f,0.58f),Gold=new Color(0.93f,0.76f,0.38f),Warning=new Color(1f,0.62f,0.42f),
            ButtonFill=new Color(0.25f,0.2f,0.14f,1),ButtonHover=new Color(0.33f,0.27f,0.18f,1),ButtonDown=new Color(0.18f,0.14f,0.1f,1),
            InputFill=new Color(0.22f,0.19f,0.14f,1),Track=new Color(0.06f,0.05f,0.03f,1);
        private GUISkin _skin;
        private GUIStyle _menuHint,_menuWarning,_menuSelected,_menuClose,_hud,_hallNotice;
        private readonly List<Texture2D> _themeTextures=new List<Texture2D>();

        private Texture2D Fill(Color fill,Color? edge=null)
        {
            // A 3×3 texture with a one-pixel border, sliced so only the outline stays thin.
            var texture=new Texture2D(3,3,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            for(int x=0;x<3;x++)for(int y=0;y<3;y++)texture.SetPixel(x,y,x==1&&y==1||edge==null?fill:edge.Value);
            texture.Apply();_themeTextures.Add(texture);return texture;
        }
        private static void Text(GUIStyle style,Color color)
        {foreach(GUIStyleState state in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})state.textColor=color;}
        private static Font ValheimFont()
        {
            Font regular=null;
            foreach(Font font in Resources.FindObjectsOfTypeAll<Font>())
            {
                if(font.name=="AveriaSerifLibre-Bold")return font;
                if(font.name.StartsWith("AveriaSerifLibre"))regular=font;
            }
            return regular;
        }
        // GUI.skin can only be read inside OnGUI, so the theme is built on first draw.
        private GUISkin Theme()
        {
            if(_skin!=null)return _skin;
            _skin=Instantiate(GUI.skin);_skin.hideFlags=HideFlags.HideAndDontSave;
            Font font=ValheimFont();if(font!=null)_skin.font=font;
            _skin.settings.cursorColor=Cream;_skin.settings.selectionColor=new Color(0.93f,0.76f,0.38f,0.35f);
            var thin=new RectOffset(1,1,1,1);

            _skin.window=new GUIStyle{border=thin,padding=new RectOffset(20,20,14,14)};
            _skin.window.normal.background=_skin.window.onNormal.background=Fill(Panel,Edge);
            _skin.box=new GUIStyle(_skin.window){padding=new RectOffset(14,14,10,10),wordWrap=true,fontSize=15};Text(_skin.box,Cream);

            _skin.label=new GUIStyle(_skin.label){fontSize=16,wordWrap=true};Text(_skin.label,Cream);
            _skin.button=new GUIStyle(_skin.button){fontSize=16,border=thin,alignment=TextAnchor.MiddleCenter,padding=new RectOffset(8,8,5,5)};
            _skin.button.normal.background=Fill(ButtonFill,Edge);
            _skin.button.hover.background=_skin.button.focused.background=Fill(ButtonHover,Edge);
            _skin.button.active.background=Fill(ButtonDown,Edge);
            _skin.button.onNormal.background=_skin.button.onHover.background=_skin.button.onActive.background=_skin.button.onFocused.background=null;
            Text(_skin.button,Cream);
            _skin.textField=new GUIStyle(_skin.textField){fontSize=16,border=thin,padding=new RectOffset(8,8,5,5)};
            _skin.textField.normal.background=Fill(InputFill,Edge);
            _skin.textField.hover.background=Fill(InputFill,Muted);
            _skin.textField.focused.background=_skin.textField.onFocused.background=Fill(InputFill,Gold);
            Text(_skin.textField,Cream);
            _skin.toggle=new GUIStyle(_skin.toggle){fontSize=16};Text(_skin.toggle,Cream);

            _skin.horizontalSlider=new GUIStyle{border=thin,fixedHeight=8,margin=new RectOffset(6,6,12,12)};
            _skin.horizontalSlider.normal.background=Fill(Track,Edge);
            _skin.horizontalSliderThumb=new GUIStyle{fixedWidth=14,overflow=new RectOffset(0,0,6,6)};
            _skin.horizontalSliderThumb.normal.background=Fill(Gold);
            _skin.horizontalSliderThumb.hover.background=_skin.horizontalSliderThumb.active.background=Fill(Cream);

            _skin.scrollView=new GUIStyle();
            _skin.verticalScrollbar=new GUIStyle(_skin.verticalScrollbar){border=new RectOffset(0,0,0,0),fixedWidth=10,margin=new RectOffset(6,0,0,0)};
            _skin.verticalScrollbar.normal.background=Fill(Track);
            _skin.verticalScrollbarThumb=new GUIStyle(_skin.verticalScrollbarThumb){border=new RectOffset(0,0,0,0),fixedWidth=10};
            _skin.verticalScrollbarThumb.normal.background=Fill(ButtonHover);
            _skin.horizontalScrollbar=new GUIStyle(_skin.horizontalScrollbar){border=new RectOffset(0,0,0,0),fixedHeight=10};
            _skin.horizontalScrollbar.normal.background=Fill(Track);
            _skin.horizontalScrollbarThumb=new GUIStyle(_skin.horizontalScrollbarThumb){border=new RectOffset(0,0,0,0),fixedHeight=10};
            _skin.horizontalScrollbarThumb.normal.background=Fill(ButtonHover);

            _menuTitle=new GUIStyle(_skin.label){fontSize=24,wordWrap=false};
            _menuText=new GUIStyle(_skin.label){fontSize=15};
            _menuHint=new GUIStyle(_menuText){fontSize=14};Text(_menuHint,Muted);
            _menuWarning=new GUIStyle(_menuText);Text(_menuWarning,Warning);
            _menuButton=_skin.button;
            _menuSelected=new GUIStyle(_skin.button);
            _menuSelected.normal.background=_menuSelected.hover.background=Fill(ButtonHover,Gold);Text(_menuSelected,Gold);
            _menuClose=new GUIStyle(_skin.button){fontSize=20,padding=new RectOffset(0,0,0,2)};
            _menuNumber=_skin.textField;
            _hud=_skin.box;
            _hallNotice=new GUIStyle(_skin.box){fontSize=22,alignment=TextAnchor.MiddleCenter};Text(_hallNotice,Warning);
            return _skin;
        }
        private void DestroyTheme()
        {
            foreach(Texture2D texture in _themeTextures)if(texture!=null)Destroy(texture);_themeTextures.Clear();
            if(_skin!=null)Destroy(_skin);_skin=null;
        }
    }
}
