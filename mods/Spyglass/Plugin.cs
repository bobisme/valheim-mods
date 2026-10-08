using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Spyglass
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.spyglass";
        public const string Name="Spyglass";
        public const string Version="0.1.2";
        internal static Plugin Instance;
        internal ConfigEntry<KeyboardShortcut> Toggle;
        internal ConfigEntry<float> StartMagnification,MaxMagnification,RevealRadius,RevealRange;
        internal ConfigEntry<bool> Vignette;
        private Harmony _harmony;
        private Texture2D _vignette,_black;
        private GUIStyle _label;
        private static int _escapeFrame=-10;

        internal static bool Zooming=>Instance!=null&&Zoom.Active;
        // Menu.Update may run before this mod's Update in the frame Escape lowers the glass.
        internal static bool ReservesEscape=>_escapeFrame==Time.frameCount||_escapeFrame==Time.frameCount-1||(Zooming&&Input.GetKeyDown(KeyCode.Escape));

        private void Awake()
        {
            Instance=this;
            Toggle=Config.Bind("Controls","Toggle",new KeyboardShortcut(KeyCode.Z,KeyCode.LeftShift),
                "Raise or lower the spyglass while it is in your inventory. Escape also lowers it. A plain Z bound by another mod (GearSlots' quick slot 1) does not fire while this is pressed.");
            StartMagnification=Config.Bind("Zoom","Magnification",4f,new ConfigDescription("Magnification when you raise the spyglass. The mouse wheel changes it while looking.",new AcceptableValueRange<float>(2,12)));
            MaxMagnification=Config.Bind("Zoom","MaxMagnification",8f,new ConfigDescription("Strongest magnification the mouse wheel reaches (the weakest is 2×).",new AcceptableValueRange<float>(2,12)));
            Vignette=Config.Bind("Zoom","Vignette",true,"Darken the edges of the screen to a round eyepiece while looking.");
            RevealRadius=Config.Bind("Map","RevealRadius",30f,new ConfigDescription("Metres of map uncovered around the point you gaze at. 0 turns map reveal off.",new AcceptableValueRange<float>(0,80)));
            RevealRange=Config.Bind("Map","RevealRange",600f,new ConfigDescription("Farthest gaze point that uncovers the map, in metres.",new AcceptableValueRange<float>(100,1500)));
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            ShortcutGuard.Start(_harmony,Logger);
            Item.RegisterLoaded(); // hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded. {Toggle.Value} raises the spyglass.");
        }
        private void Update()
        {
            ShortcutGuard.Tick();
            Player player=Player.m_localPlayer;
            if(Zoom.Active)
            {
                if(Input.GetKeyDown(KeyCode.Escape)){_escapeFrame=Time.frameCount;Zoom.Lower();return;}
                if(!CanLook(player)||!Has(player)||Toggle.Value.IsDown()){Zoom.Lower();return;}
                Zoom.Tick(MaxMagnification.Value);
                Zoom.Reveal(RevealRadius.Value,RevealRange.Value);
                return;
            }
            if(!Toggle.Value.IsDown()||!CanLook(player))return;
            if(!Has(player)){player.Message(MessageHud.MessageType.TopLeft,"You need a Spyglass. Craft one at the forge from bronze.");return;}
            Zoom.Raise(Mathf.Clamp(StartMagnification.Value,2,Mathf.Max(2,MaxMagnification.Value)));
        }
        private static bool Has(Player player)=>player.GetInventory().GetAllItems().Any(Item.Is);
        private static bool CanLook(Player player)=>
            player!=null&&!player.IsDead()&&!player.InCutscene()&&!player.InBed()&&
            !(Chat.instance!=null&&Chat.instance.HasFocus())&&!Console.IsVisible()&&!TextInput.IsVisible()&&!Minimap.InTextInput()&&
            !InventoryGui.IsVisible()&&!Menu.IsVisible()&&!Minimap.IsOpen()&&!StoreGui.IsVisible()&&!Hud.IsPieceSelectionVisible();
        private void OnGUI()
        {
            if(!Zoom.Active||Event.current.type!=EventType.Repaint)return;
            if(_vignette==null){_vignette=Eyepiece();_black=new Texture2D(1,1){hideFlags=HideFlags.HideAndDontSave};_black.SetPixel(0,0,new Color(0,0,0,0.94f));_black.Apply();}
            if(Vignette.Value)
            {
                float size=Mathf.Min(Screen.width,Screen.height*1.15f),x=(Screen.width-size)/2,y=(Screen.height-size)/2;
                GUI.DrawTexture(new Rect(x,y,size,size),_vignette);
                if(x>0){GUI.DrawTexture(new Rect(0,0,x+1,Screen.height),_black);GUI.DrawTexture(new Rect(x+size-1,0,x+1,Screen.height),_black);}
                if(y>0){GUI.DrawTexture(new Rect(0,0,Screen.width,y+1),_black);GUI.DrawTexture(new Rect(0,y+size-1,Screen.width,y+1),_black);}
            }
            if(_label==null)_label=new GUIStyle(GUI.skin.label){fontSize=Mathf.Max(14,Screen.height/60),alignment=TextAnchor.MiddleCenter,normal={textColor=new Color(0.95f,0.9f,0.8f)}};
            GUI.Label(new Rect(0,Screen.height-Screen.height/12f,Screen.width,30),$"{Zoom.Magnification:0.0}×   wheel: zoom · {Toggle.Value} / Esc: lower",_label);
        }
        // Clear in the middle, softly darkening to the eyepiece rim.
        private static Texture2D Eyepiece()
        {
            const int size=256;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float r=new Vector2(x-size/2f+0.5f,y-size/2f+0.5f).magnitude/(size/2f);
                texture.SetPixel(x,y,new Color(0,0,0,Mathf.SmoothStep(0,0.94f,Mathf.InverseLerp(0.82f,0.98f,r))));
            }
            texture.Apply();return texture;
        }
        private void OnDestroy()
        {
            Zoom.Lower();
            ShortcutGuard.Stop();
            _harmony?.UnpatchSelf();
            Item.Unregister();
            if(_vignette!=null)Destroy(_vignette);if(_black!=null)Destroy(_black);
            if(Instance==this)Instance=null;
        }
    }

    internal static class Zoom
    {
        internal static bool Active;
        internal static float Magnification=4;
        internal static bool ReadingScroll;
        private static float _shown=1,_nextReveal;
        private static readonly MethodInfo Explore=AccessTools.Method(typeof(Minimap),"Explore",new[]{typeof(Vector3),typeof(float)});
        private static int _mask=-1;

        internal static void Raise(float magnification){Active=true;Magnification=magnification;_shown=1;_nextReveal=Time.time+0.4f;}
        internal static void Lower(){Active=false;_shown=1;}
        internal static void Tick(float max)
        {
            ReadingScroll=true;
            float scroll;try{scroll=ZInput.GetMouseScrollWheel();}finally{ReadingScroll=false;}
            Magnification=(float)Policy.Step(Magnification,scroll,2,Mathf.Max(2,max));
        }
        // Called from the camera after the game has placed it: look from the eye, through a narrower view.
        internal static void Apply(GameCamera camera,Camera view,float dt)
        {
            Player player=Player.m_localPlayer;
            if(!Active||player==null||view==null)return;
            _shown=Mathf.Lerp(_shown,Magnification,1-Mathf.Exp(-12*dt));
            float fov=(float)Policy.Fov(camera.m_fov,_shown);
            view.fieldOfView=fov;
            if(camera.m_skyCamera!=null)camera.m_skyCamera.fieldOfView=fov;
            Vector3 forward=player.m_eye.forward;
            camera.transform.position=player.m_eye.position+forward*0.3f; // just in front of the face, so the head stays behind
            camera.transform.rotation=Quaternion.LookRotation(forward,Vector3.up);
        }
        internal static float LookScale()=>Active&&GameCamera.instance!=null?
            (float)Policy.LookScale(GameCamera.instance.m_fov,Policy.Fov(GameCamera.instance.m_fov,_shown)):1;

        // Where the gaze lands: loaded colliders first, then the generated terrain shape and sea level beyond them.
        internal static void Reveal(float radius,float range)
        {
            if(radius<=0||Time.time<_nextReveal||GameCamera.instance==null||Minimap.instance==null||Explore==null)return;
            if(_shown<Magnification*0.8f)return; // still raising the glass
            _nextReveal=Time.time+0.25f;
            if(_mask<0)_mask=LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain");
            Transform eye=GameCamera.instance.transform;
            Vector3 origin=eye.position,direction=eye.forward;
            float water=ZoneSystem.instance!=null?ZoneSystem.instance.m_waterLevel:30;
            float distance=float.NaN;
            if(Physics.Raycast(origin,direction,out RaycastHit hit,range,_mask,QueryTriggerInteraction.Ignore))distance=hit.distance;
            else if(WorldGenerator.instance!=null)
                distance=(float)Policy.Gaze(origin.x,origin.y,origin.z,direction.x,direction.y,direction.z,
                    (x,z)=>WorldGenerator.instance.GetHeight((float)x,(float)z),water,8,range);
            if(direction.y<-1e-4f&&origin.y>water)distance=Mathf.Min(float.IsNaN(distance)?float.MaxValue:distance,(water-origin.y)/direction.y);
            if(float.IsNaN(distance)||distance>range)return;
            Explore.Invoke(Minimap.instance,new object[]{origin+direction*distance,radius});
        }
    }
}
