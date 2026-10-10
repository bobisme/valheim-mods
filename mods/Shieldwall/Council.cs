using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Shieldwall
{
    // The war council (Shift+E on an idle Warstone): choose the boon the stone has earned, swear boasts for a harder siege and richer
    // spoils, and sound the horn. A small window in the same dark wood as the game's own panels.
    internal static class Council
    {
        private static Warstone _stone;
        private static int _boasts,_openedFrame,_escapeFrame=-10;
        private static Rect _rect;
        internal static bool IsOpen=>_stone!=null;
        internal static bool ReservesEscape=>Time.frameCount-_escapeFrame<=1;

        internal static void Open(Warstone stone)
        {
            _stone=stone;_boasts=0;_openedFrame=Time.frameCount;_rect=new Rect(0,0,0,0);
            if(TextViewer.instance!=null&&TextViewer.instance.IsVisible())TextViewer.instance.Hide(); // the saga, if it is still up
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        internal static void Close(){_stone=null;}

        // Every frame: close when the stone goes, its siege starts, you walk off or die, or press Escape.
        internal static void Tick()
        {
            if(_stone==null)return;
            Player me=Player.m_localPlayer;
            if(_stone.Z==null||me==null||me.IsDead()||_stone.Phase!=Phase.Idle||Vector3.Distance(me.transform.position,_stone.transform.position)>8){Close();return;}
            if(Input.GetKeyDown(KeyCode.Escape)){_escapeFrame=Time.frameCount;Close();}
        }

        internal static void Draw()
        {
            if(_stone==null||_stone.Z==null)return;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            Matrix4x4 saved=GUI.matrix;GUISkin skin=GUI.skin;bool enabled=GUI.enabled;
            try
            {
                GUI.skin=Theme.Skin();
                float scale=Mathf.Max(0.6f,Screen.height/1080f);
                GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
                const float width=480;
                float sw=Screen.width/scale,sh=Screen.height/scale;
                GUI.enabled=Time.frameCount>_openedFrame+1; // not the key press that opened it
                _rect=GUILayout.Window(0x5741524,new Rect((sw-width)/2,_rect.height>0?(sh-_rect.height)/2:sh*0.2f,width,_rect.height),Contents,GUIContent.none,GUILayout.Width(width));
            }
            finally{GUI.matrix=saved;GUI.skin=skin;GUI.enabled=enabled;}
        }

        private static void Contents(int id)
        {
            Warstone stone=_stone;
            if(stone==null||stone.Z==null)return;
            ZDO z=stone.Z;
            string act=null,boon=null;bool saga=false;

            GUILayout.BeginHorizontal();
            GUILayout.Label("War council",Theme.Title,GUILayout.Height(34));
            GUILayout.FlexibleSpace();
            if(GUILayout.Button("×",Theme.CloseButton,GUILayout.Width(38),GUILayout.Height(34)))act="close";
            GUILayout.EndHorizontal();
            GUILayout.Label($"The Warstone · {Policy.Title(stone.Marks)}{(stone.Marks>0?" ("+Policy.Numeral(stone.Marks)+")":"")}{(stone.Cracked?" · cracked":"")}",Theme.Hint);

            // The boon it has earned.
            string[] offer=stone.BoonOffer;
            if(offer.Length>0)
            {
                GUILayout.Space(14);
                GUILayout.Label("The stone held. Choose a boon for it to keep:",Theme.Heading);
                foreach(string id2 in offer)
                {
                    Boon b=Policy.BoonOf(id2);
                    if(GUILayout.Button(b.Name,Theme.Button,GUILayout.Height(34)))boon=id2;
                    GUILayout.Label(b.Text,Theme.Hint);
                    GUILayout.Space(4);
                }
            }

            // The horn.
            GUILayout.Space(14);
            GUILayout.Label("The war horn",Theme.Heading);
            double cooldown=stone.CooldownLeft;
            if(cooldown>0)GUILayout.Label($"The stone still rings from the last siege: {Mathf.CeilToInt((float)cooldown/60)} more minutes.",Theme.Text);
            else
            {
                int strength=stone.Strength,stage=Policy.SiegeStage(Director.StageNow(),strength);
                int players=Player.GetAllPlayers().Count(p=>p!=null&&Vector3.Distance(p.transform.position,stone.transform.position)<80);
                GUILayout.Label($"A {Policy.Rosters[stage].Name} will gather and march on the stone: {Policy.Waves(strength)} waves, about {Policy.Total(strength,System.Math.Max(1,players))} strong.",Theme.Text);
                GUILayout.Space(8);
                GUILayout.Label("Swear boasts for a harder siege and more warshards:",Theme.Hint);
                foreach(Boast b in stone.BoastOffer)
                {
                    BoastInfo info=Policy.Boasts.First(x=>x.Id==b);
                    bool on=Policy.Has(_boasts,b);
                    if(GUILayout.Button($"{info.Name}   +{Mathf.RoundToInt((float)info.Bonus*100)}% warshards{(on?"   · sworn":"")}",on?Theme.Selected:Theme.Button,GUILayout.Height(34)))_boasts^=1<<(int)b;
                    GUILayout.Label(info.Text,Theme.Hint);
                    GUILayout.Space(4);
                }
                if(_boasts!=0)GUILayout.Label($"Sworn: +{Mathf.RoundToInt((float)Policy.BoastBonus(_boasts)*100)}% warshards if the stone holds.",Theme.Gold);
                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Sound the war horn",Theme.Button,GUILayout.Height(38)))act="horn";
                if(GUILayout.Button("Not yet",Theme.Button,GUILayout.Height(38),GUILayout.Width(120)))act="close";
                GUILayout.EndHorizontal();
            }

            // What it keeps, and its last saga.
            if(stone.Boons.Count>0)
            {
                GUILayout.Space(12);
                GUILayout.Label("Boons: "+string.Join(", ",stone.Boons.Select(b=>Policy.BoonOf(b).Name)),Theme.Hint);
            }
            if(!string.IsNullOrEmpty(z.GetString(Stone.SagaKey,"")))
            {
                GUILayout.Space(8);
                if(GUILayout.Button("Read the saga of the last siege",Theme.Button,GUILayout.Height(30)))saga=true;
            }
            GUILayout.Space(6);
            GUILayout.Label("Esc: close",Theme.Hint);

            // Act once the layout is done.
            Player me=Player.m_localPlayer;
            if(boon!=null&&me!=null)
            {
                if(!PrivateArea.CheckAccess(stone.transform.position))me.Message(MessageHud.MessageType.Center,"This stone is warded by another.");
                else Net.Choose(stone,boon,me.GetPlayerName());
            }
            if(saga){Close();Saga.Show(z.GetString(Stone.SagaTopicKey,"The last siege"),z.GetString(Stone.SagaKey,""));}
            if(act=="horn"){int boasts=_boasts;Close();Net.Horn(stone,boasts);}
            else if(act=="close")Close();
        }
    }

    // The dark wood panel, cream text and Averia font of the game's own windows (as in Bob's other mods).
    internal static class Theme
    {
        private static readonly Color Panel=new Color(0.11f,0.09f,0.06f,0.97f),Edge=new Color(0.36f,0.29f,0.19f,1),Cream=new Color(0.95f,0.9f,0.8f),
            Muted=new Color(0.72f,0.67f,0.58f),GoldColor=new Color(0.93f,0.76f,0.38f),Fill=new Color(0.25f,0.2f,0.14f,1),Hover=new Color(0.33f,0.27f,0.18f,1),Down=new Color(0.18f,0.14f,0.1f,1);
        private static GUISkin _skin;
        private static readonly List<Texture2D> Textures=new List<Texture2D>();
        internal static GUIStyle Title,Heading,Text,Hint,Gold,Button,Selected,CloseButton;

        private static Texture2D Box(Color fill,Color? edge=null)
        {
            var texture=new Texture2D(3,3,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            for(int x=0;x<3;x++)for(int y=0;y<3;y++)texture.SetPixel(x,y,x==1&&y==1||edge==null?fill:edge.Value);
            texture.Apply();Textures.Add(texture);return texture;
        }
        private static void Colour(GUIStyle style,Color color){foreach(GUIStyleState s in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})s.textColor=color;}
        private static Font _font;
        private static Font ValheimFont()
        {
            if(_font!=null)return _font;
            foreach(Font font in Resources.FindObjectsOfTypeAll<Font>()) // once, when the window first opens
            {
                if(font.name=="AveriaSerifLibre-Bold")return _font=font;
                if(font.name.StartsWith("AveriaSerifLibre"))_font=font;
            }
            return _font;
        }
        // GUI.skin can only be read inside OnGUI, so this is built on the first draw.
        internal static GUISkin Skin()
        {
            if(_skin!=null)return _skin;
            _skin=Object.Instantiate(GUI.skin);_skin.hideFlags=HideFlags.HideAndDontSave;
            Font font=ValheimFont();if(font!=null)_skin.font=font;
            var thin=new RectOffset(1,1,1,1);
            _skin.window=new GUIStyle{border=thin,padding=new RectOffset(22,22,16,16)};
            _skin.window.normal.background=_skin.window.onNormal.background=Box(Panel,Edge);
            _skin.label=new GUIStyle(_skin.label){fontSize=16,wordWrap=true};Colour(_skin.label,Cream);
            _skin.button=new GUIStyle(_skin.button){fontSize=17,border=thin,alignment=TextAnchor.MiddleCenter,padding=new RectOffset(8,8,5,5)};
            _skin.button.normal.background=Box(Fill,Edge);
            _skin.button.hover.background=_skin.button.focused.background=Box(Hover,Edge);
            _skin.button.active.background=Box(Down,Edge);
            _skin.button.onNormal.background=_skin.button.onHover.background=_skin.button.onActive.background=_skin.button.onFocused.background=null;
            Colour(_skin.button,Cream);
            Title=new GUIStyle(_skin.label){fontSize=26,wordWrap=false};
            Heading=new GUIStyle(_skin.label){fontSize=19};Colour(Heading,GoldColor);
            Text=new GUIStyle(_skin.label){fontSize=16};
            Hint=new GUIStyle(_skin.label){fontSize=14};Colour(Hint,Muted);
            Gold=new GUIStyle(_skin.label){fontSize=16};Colour(Gold,GoldColor);
            Button=_skin.button;
            Selected=new GUIStyle(_skin.button);Selected.normal.background=Selected.hover.background=Box(Hover,GoldColor);Colour(Selected,GoldColor);
            CloseButton=new GUIStyle(_skin.button){fontSize=20,padding=new RectOffset(0,0,0,2)};
            return _skin;
        }
        internal static void Destroy()
        {
            foreach(Texture2D t in Textures)if(t!=null)Object.Destroy(t);Textures.Clear();
            if(_skin!=null)Object.Destroy(_skin);_skin=null;
        }
    }

    // While the council is open the game takes no input: no walking, looking, swinging or scrolling, and Escape closes the window only.
    [HarmonyPatch(typeof(Player),"TakeInput")]
    internal static class CouncilPlayerInput
    {
        private static void Postfix(Player __instance,ref bool __result){if(Council.IsOpen&&__instance==Player.m_localPlayer)__result=false;}
    }
    [HarmonyPatch(typeof(PlayerController),"TakeInput")]
    internal static class CouncilControllerInput
    {
        private static void Postfix(ref bool __result){if(Council.IsOpen)__result=false;}
    }
    [HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture")]
    internal static class CouncilMouse
    {
        private static bool Prefix(){if(!Council.IsOpen)return true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return false;}
    }
    [HarmonyPatch(typeof(ZInput),nameof(ZInput.GetMouseScrollWheel))]
    internal static class CouncilScroll
    {
        private static void Postfix(ref float __result){if(Council.IsOpen)__result=0;}
    }
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]
    internal static class CouncilAttack
    {
        private static bool Prefix(Humanoid __instance,ref bool __result){if(!Council.IsOpen||__instance!=Player.m_localPlayer)return true;__result=false;return false;}
    }
    [HarmonyPatch(typeof(Menu),"Update")]
    internal static class CouncilEscape
    {
        private static bool Prefix()=>!Council.IsOpen&&!Council.ReservesEscape;
    }
}
