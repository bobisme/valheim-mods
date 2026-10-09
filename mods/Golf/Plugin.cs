using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MeadowGolf
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed partial class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.golf";
        public const string Name="Meadow Golf";
        public const string Version="0.1.0";
        internal static Plugin Instance;
        private Harmony _harmony;
        private ConfigEntry<KeyboardShortcut> _cardKey;
        private bool _charging,_card,_sawDone;
        private int _mode;
        private float _started,_nextMessage;
        private GolfBall _chargedBall,_observed;
        private GUIStyle _label,_heading,_small;
        private Texture2D _panel,_bar;
        private LineRenderer _aim,_aimEnd;
        private readonly ShotPreview _preview=new ShotPreview();
        private static int _escapeFrame=-10;
        internal static bool ReservesEscape=>_escapeFrame==Time.frameCount||_escapeFrame==Time.frameCount-1||
            (Instance!=null&&Instance._card&&Input.GetKeyDown(KeyCode.Escape));
        internal static bool ReadingScroll;
        internal static bool Active=>Instance!=null&&CanPlay(Player.m_localPlayer)&&Prefabs.IsClub(Prefabs.Right(Player.m_localPlayer));
        private void Awake()
        {
            Instance=this;
            _cardKey=Config.Bind("Controls","Scorecard",new KeyboardShortcut(KeyCode.G),"Show nearby golfers' scorecards while holding the golf club.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Prefabs.Loaded();GolfWorld.Tick();
            Logger.LogInfo($"{Name} {Version} loaded. Craft a Meadow golf club at the workbench; tees and cups are under Hammer → Furniture.");
        }
        internal static bool CanPlay(Player p)=>p!=null&&!p.IsDead()&&!p.InCutscene()&&!p.InBed()&&!p.InDodge()&&!p.IsStaggering()&&!p.IsSwimming()&&
            !(Chat.instance!=null&&Chat.instance.HasFocus())&&!Console.IsVisible()&&!TextInput.IsVisible()&&!Minimap.InTextInput()&&
            !InventoryGui.IsVisible()&&!Menu.IsVisible()&&!Minimap.IsOpen()&&!StoreGui.IsVisible()&&!Hud.IsPieceSelectionVisible();
        internal static GolfBall MyBall()=>GolfBall.Loaded.Where(b=>b!=null&&b.Mine&&b.Data!=null).OrderByDescending(b=>b.Data.GetLong("bob_golf_sequence",0)).FirstOrDefault();
        private float Power=>Mathf.Clamp01((Time.unscaledTime-_started)/1.4f);
        private static Vector3 Aim(Player player)
        {Vector3 direction=player.GetLookDir();direction.y=0;return direction.sqrMagnitude>.001f?direction.normalized:player.transform.forward;}
        private void Update()
        {
            GolfWorld.Tick();CommandsTick();
            Player player=Player.m_localPlayer;
            if(!Active){_charging=false;_card=false;DrawAim(null,Vector3.forward);return;}
            if(_cardKey.Value.IsDown()){_card=!_card;_charging=false;}
            if(_card&&Input.GetKeyDown(KeyCode.Escape)){_escapeFrame=Time.frameCount;_card=false;return;}
            GolfBall ball=MyBall();
            if(ball!=_observed){_observed=ball;_sawDone=ball!=null&&ball.Done;}
            if(ball!=null&&ball.Done&&!_sawDone)
            {
                _sawDone=true;Rules.Parse(ball.Data.GetString(GolfWorld.LabelKey,""),out var hole);
                GolfWorld.Say($"{Rules.Outcome(ball.Strokes,hole?.Par??3)} — {ball.Strokes} strokes. G shows your scorecard.");_charging=false;
            }
            if(_card){DrawAim(null,Vector3.forward);return;}
            ReadingScroll=true;float wheel;try{wheel=ZInput.GetMouseScrollWheel();}finally{ReadingScroll=false;}
            if(!_charging&&Mathf.Abs(wheel)>.01f)_mode=(_mode+(wheel>0?1:2))%3;
            bool close=ball!=null&&!ball.Done&&Vector3.Distance(player.transform.position,ball.transform.position)<3f;
            if(_charging&&Input.GetMouseButtonDown(1)){_charging=false;_chargedBall=null;}
            if(Input.GetMouseButtonDown(0))
            {
                if(!close){Message("Use a golf tee to start, then stand within 3 m of your ball.");}
                else if(!ball.Still){Message("Let your ball settle first.");}
                else if(ball.Strokes>=Rules.MaxStrokes){Message("Stroke limit reached. Start another hole.");}
                else{_charging=true;_started=Time.unscaledTime;_chargedBall=ball;}
            }
            if(_charging&&(!close||ball!=_chargedBall||!ball.Still)){_charging=false;}
            if(_charging&&Input.GetMouseButtonUp(0))
            {
                Vector3 direction=Aim(player);float power=Power;_charging=false;
                Swing(player,ball,_mode,power,direction);
            }
            DrawAim(close&&ball.Still?ball:null,Aim(player));
        }
        private void Message(string text){if(Time.unscaledTime<_nextMessage)return;_nextMessage=Time.unscaledTime+1.5f;GolfWorld.Say(text);}
        private void DrawAim(GolfBall ball,Vector3 direction)
        {
            if(ball==null){_preview.Pause();if(_aim!=null)_aim.enabled=false;if(_aimEnd!=null)_aimEnd.enabled=false;return;}
            if(_aim==null)
            {
                var go=new GameObject("GolfAim");_aim=go.AddComponent<LineRenderer>();
                Shader shader=Shader.Find("Sprites/Default");if(shader==null)shader=Shader.Find("Unlit/Color");
                if(shader==null){Destroy(go);return;}
                _aim.sharedMaterial=new Material(shader);_aim.useWorldSpace=true;_aim.widthMultiplier=.025f;
                _aim.startColor=new Color(.95f,.78f,.42f,.85f);_aim.endColor=new Color(.95f,.78f,.42f,.12f);
                _aim.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;_aim.receiveShadows=false;
            }
            _aim.enabled=true;_preview.Draw(_aim,ball,_mode,_charging?Power:.5f,direction);
            if(_aimEnd==null)
            {
                _aimEnd=new GameObject("GolfStoppingPoint").AddComponent<LineRenderer>();_aimEnd.sharedMaterial=_aim.sharedMaterial;
                _aimEnd.useWorldSpace=true;_aimEnd.widthMultiplier=.025f;_aimEnd.startColor=_aimEnd.endColor=new Color(.95f,.78f,.42f,.7f);
                _aimEnd.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;_aimEnd.receiveShadows=false;_aimEnd.positionCount=25;
            }
            _aimEnd.enabled=_preview.DisplayComplete;
            if(_aimEnd.enabled)for(int i=0;i<25;i++){float a=i*Mathf.PI/12;_aimEnd.SetPosition(i,_preview.End+new Vector3(Mathf.Cos(a)*.17f,.035f,Mathf.Sin(a)*.17f));}
        }
        internal static void Swing(Player player,GolfBall ball,int mode,float power,Vector3 direction)
        {
            ball.RequestShot(player,mode,power,direction);
            player.FaceLookDirection();var attack=Prefabs.Right(player).m_shared.m_attack;
            player.GetZAnim().SetTrigger(attack.m_attackAnimation+(attack.m_attackChainLevels>1?"0":""));
        }
        private static readonly string[] Modes={"Drive","Chip","Putt"};
        private void OnGUI()
        {
            if(!Active)return;Theme();
            GolfBall ball=MyBall();float scale=Mathf.Max(.75f,Screen.height/900f);Matrix4x4 prior=GUI.matrix;Color priorColor=GUI.color,priorContent=GUI.contentColor;GUI.color=GUI.contentColor=Color.white;
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(scale,scale,1));
            try
            {
                float width=Screen.width/scale,height=Screen.height/scale;
                if(_card){DrawCard(width,height);return;}
                var rect=new Rect(width/2-225,height-175,450,110);GUI.DrawTexture(rect,_panel);
                string info="E at a tee to start a hole";
                if(ball!=null&&Rules.Parse(ball.Data.GetString(GolfWorld.LabelKey,""),out var hole))
                {
                    ZDO cup=ZDOMan.instance?.GetZDO(ball.Data.GetZDOID(GolfWorld.CupKey));
                    info=$"{hole.Course} · {hole.Number} / par {hole.Par} · {ball.Strokes} strokes"+
                        (ball.Done?" · finished":cup!=null?$" · {Vector3.Distance(ball.transform.position,cup.GetPosition()):0.0} m to cup":"");
                }
                GUI.Label(new Rect(rect.x+15,rect.y+7,420,24),Modes[_mode]+(_aim!=null&&_aim.enabled?(_preview.DisplayComplete?$" · ~{_preview.Distance:0.0} m ({_preview.PredictedPower*100:0}%)":" · aim guide"):"")+(_charging?$" — {Power*100:0}%":ball!=null&&!ball.Done&&!ball.Still?" — ball moving":""),_heading);
                GUI.Label(new Rect(rect.x+15,rect.y+35,420,22),info,_label);
                GUI.Label(new Rect(rect.x+15,rect.y+64,420,25),$"Wheel: shot · hold/release left mouse · {_cardKey.Value}: scores",_small);
                if(_charging){GUI.color=new Color(.84f,.63f,.30f);GUI.DrawTexture(new Rect(rect.x+15,rect.y+96,420*Power,5),_bar);GUI.color=Color.white;}
            }
            finally{GUI.matrix=prior;GUI.color=priorColor;GUI.contentColor=priorContent;}
        }
        private void DrawCard(float width,float height)
        {
            var golfers=GolfBall.Loaded.Where(b=>b!=null&&b.Data!=null&&Vector3.Distance(Player.m_localPlayer.transform.position,b.transform.position)<120)
                .OrderBy(b=>b.Mine?0:1).ThenBy(b=>b.Data.GetString(GolfWorld.NameKey,"")).Take(8).ToArray();
            Rect r=new Rect(width/2-390,Mathf.Max(50,height/2-255),780,510);GUI.DrawTexture(r,_panel);
            GUI.Label(new Rect(r.x+22,r.y+15,730,30),"MEADOW GOLF — SCORECARD",_heading);
            GUI.Label(new Rect(r.x+22,r.y+52,730,24),"Each golfer's current course · finished holes · nearby players",_small);
            float y=r.y+91;
            foreach(var golfer in golfers)
            {
                ZDO z=golfer.Data;Rules.Parse(z.GetString(GolfWorld.LabelKey,""),out var hole);
                var rows=Rules.ReadCard(z.GetString(GolfWorld.CardKey,""));int total=rows.Sum(row=>row.Strokes),par=rows.Sum(row=>row.Par);
                string scores=string.Join("  ·  ",rows.Select(row=>$"{row.Hole}: {row.Strokes}"));
                GUI.Label(new Rect(r.x+22,y,730,24),$"{z.GetString(GolfWorld.NameKey,"Golfer")} — {hole?.Course??"Meadow"}   {total} / par {par}"+ (rows.Count>0?$"  ({(total-par>0?"+":"")}{total-par})":""),_label);
                GUI.Label(new Rect(r.x+22,y+25,730,22),scores.Length>0?scores:"No finished holes yet",_small);y+=44;
            }
            if(golfers.Length==0)GUI.Label(new Rect(r.x+22,y,730,30),"Start a hole at a tee to begin your card.",_label);
            GUI.Label(new Rect(r.x+22,r.y+471,730,24),$"{_cardKey.Value} / Escape: close · E on your ball: last lie (+1) · E on tee: return (+1)",_small);
        }
        private void Theme()
        {
            if(_panel!=null)return;
            _panel=new Texture2D(1,1,TextureFormat.RGBA32,false);_panel.SetPixel(0,0,new Color(.006f,.004f,.003f,.94f));_panel.Apply();
            _bar=new Texture2D(1,1,TextureFormat.RGBA32,false);_bar.SetPixel(0,0,Color.white);_bar.Apply();
            Font font=Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f=>f.name=="AveriaSerifLibre-Bold");
            _label=new GUIStyle{font=font,fontSize=16,normal={textColor=new Color(.94f,.89f,.77f)}};
            _heading=new GUIStyle(_label){fontSize=21,fontStyle=FontStyle.Bold};_small=new GUIStyle(_label){fontSize=14,normal={textColor=new Color(.77f,.73f,.64f)}};
        }
        private void OnDestroy()
        {
            _preview.Dispose();CommandsStop();GolfWorld.Stop();_harmony?.UnpatchSelf();Prefabs.Unregister();
            if(_aimEnd!=null)Destroy(_aimEnd.gameObject);if(_aim!=null){Destroy(_aim.sharedMaterial);Destroy(_aim.gameObject);}if(_panel!=null)Destroy(_panel);if(_bar!=null)Destroy(_bar);
            if(Instance==this)Instance=null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class SceneHook{private static void Postfix(ZNetScene __instance)=>Prefabs.Scene(__instance);}
    [HarmonyPatch(typeof(ObjectDB),"UpdateRegisters")]
    internal static class DatabaseHook{private static void Prefix(ObjectDB __instance)=>Prefabs.Database(__instance);}
    [HarmonyPatch(typeof(Player),nameof(Player.SetControls))]
    internal static class ClubControls
    {
        private static void Prefix(Player __instance,ref bool attack,ref bool attackHold,ref bool secondaryAttack,ref bool secondaryAttackHold,ref bool block,ref bool blockHold)
        {
            if(__instance!=Player.m_localPlayer||!Prefabs.IsClub(Prefabs.Right(__instance)))return;
            attack=attackHold=secondaryAttack=secondaryAttackHold=block=blockHold=false;
        }
    }
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]
    internal static class NoGolfDamage{private static bool Prefix(Humanoid __instance,ref bool __result){if(!Prefabs.IsClub(Prefabs.Right(__instance)))return true;__result=false;return false;}}
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.OnAttackTrigger))]
    internal static class GolfContact
    {
        private static bool Prefix(Humanoid __instance)
        {
            if(!Prefabs.IsClub(Prefabs.Right(__instance)))return true;
            foreach(var ball in GolfBall.Loaded)if(ball!=null&&ball.View.IsOwner())ball.AnimationImpact(__instance.GetZDOID());
            return false;
        }
    }
    [HarmonyPatch(typeof(ZInput),nameof(ZInput.GetMouseScrollWheel))]
    internal static class ShotWheel{private static void Postfix(ref float __result){if(Plugin.Active&&!Plugin.ReadingScroll)__result=0;}}
    // Native transform sync caches a body's original kinematic/gravity settings. Golf changes
    // those with ownership: tell the synchronizer before it writes velocities to a replica.
    [HarmonyPatch(typeof(ZSyncTransform),"ClientSync")]
    internal static class ReplicaSync
    {
        private static readonly AccessTools.FieldRef<ZSyncTransform,bool> Kinematic=AccessTools.FieldRefAccess<ZSyncTransform,bool>("m_isKinematicBody");
        private static void Prefix(ZSyncTransform __instance)
        {
            if(!GolfBall.Syncs.TryGetValue(__instance,out var ball)||ball==null||ball.Data==null)return;
            ball.PreparePhysics();Kinematic(__instance)=ball.Body.isKinematic;
        }
    }
    [HarmonyPatch(typeof(ZSyncTransform),"OwnerSync")]
    internal static class OwnerSync
    {
        private static readonly AccessTools.FieldRef<ZSyncTransform,bool> Gravity=AccessTools.FieldRefAccess<ZSyncTransform,bool>("m_useGravity");
        private static bool Prefix(ZSyncTransform __instance)
        {
            if(!GolfBall.Syncs.TryGetValue(__instance,out var ball)||ball==null||ball.Data==null)return true;
            ball.PreparePhysics();Gravity(__instance)=!ball.Done;
            return !ball.Done; // Completed balls already stored their final pose and zero velocities.
        }
    }
    [HarmonyPatch(typeof(Menu),"Update")]
    internal static class CardEscape{private static bool Prefix()=>!Plugin.ReservesEscape;}
    [HarmonyPatch(typeof(Character),"Awake")]
    internal static class WalkThroughBalls
    {private static void Postfix(Character __instance){foreach(GolfBall b in GolfBall.Loaded)if(b!=null)b.Ignore(__instance.GetComponentsInChildren<Collider>());}}
}
