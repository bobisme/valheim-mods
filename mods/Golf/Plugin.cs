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
        public const string Version="0.2.2";
        internal static Plugin Instance;
        private Harmony _harmony;
        private ConfigEntry<KeyboardShortcut> _cardKey;
        private bool _charging,_card,_sawDone,_roundTest,_roundCancel;private int _roundHole;
        private int _mode;
        private float _started,_nextMessage;
        private GolfBall _chargedBall,_observed;
        private GUIStyle _label,_heading,_small;
        private LineRenderer _aim,_aimEnd;
        private readonly ShotPreview _preview=new ShotPreview();
        private static int _escapeFrame=-10;
        internal static bool ReservesEscape=>_escapeFrame==Time.frameCount||_escapeFrame==Time.frameCount-1||
            (Instance!=null&&Instance._card&&Input.GetKeyDown(KeyCode.Escape));
        internal static bool ReadingScroll;
        internal static bool CardOpen=>Instance!=null&&Instance._card&&Active;
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
            if(_roundTest){if(Input.GetKeyDown(KeyCode.Escape)){_roundCancel=true;_escapeFrame=Time.frameCount;}DrawAim(null,Vector3.forward);return;}
            if(!Active){if(_nativeUI!=null)_nativeUI.SetActive(false);_charging=false;_card=false;DrawAim(null,Vector3.forward);return;}
            if(_cardKey.Value.IsDown()){_card=!_card;_charging=false;if(_card)GolfMatches.RefreshBoard();}
            if(_card&&Input.GetKeyDown(KeyCode.Escape)){_escapeFrame=Time.frameCount;_card=false;return;}
            GolfBall ball=MyBall();
            if(ball!=_observed){_observed=ball;_sawDone=ball!=null&&ball.Done;}
            if(ball!=null&&ball.Done&&!_sawDone)
            {
                _sawDone=true;Rules.Parse(ball.Data.GetString(GolfWorld.LabelKey,""),out var hole);
                int length=ball.Data.GetInt(GolfMatches.Length,0),next=Rules.NextHole(ball.Data.GetString(GolfWorld.CardKey,""),length);
                GolfWorld.Say($"{Rules.Outcome(ball.Strokes,hole?.Par??3)} — {ball.Strokes} strokes. "+(length>0?(next==0?"Match complete! G shows the results.":$"Next: hole {next}. E at its tee continues your match."):"G shows your scorecard."));_charging=false;
            }
            if(_card)
            {
                if(Input.GetKeyDown(KeyCode.Alpha1))GolfMatches.RequestMatch(9);
                if(Input.GetKeyDown(KeyCode.Alpha2))GolfMatches.RequestMatch(18);
                if(Input.GetKeyDown(KeyCode.J))GolfMatches.RequestMatch(0,true);
                if(Input.GetKeyDown(KeyCode.X))GolfMatches.StopRound();
                if(Input.GetKeyDown(KeyCode.M))GolfMatches.EndMatch();
                if(Input.GetKeyDown(KeyCode.R))GolfMatches.RefreshBoard();
                DrawAim(null,Vector3.forward);return;
            }
            ReadingScroll=true;float wheel;try{wheel=ZInput.GetMouseScrollWheel();}finally{ReadingScroll=false;}
            if(!_charging&&Mathf.Abs(wheel)>.01f)_mode=(_mode+(wheel>0?1:2))%3;
            bool close=ball!=null&&!ball.Done&&!ball.Closed&&Vector3.Distance(player.transform.position,ball.transform.position)<3f;
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
            Color aimColor=_preview.Hazard&&_preview.DisplayComplete?new Color(.35f,.7f,1f,.85f):new Color(.95f,.78f,.42f,.85f);
            _aim.startColor=aimColor;_aim.endColor=new Color(aimColor.r,aimColor.g,aimColor.b,.25f);_aimEnd.startColor=_aimEnd.endColor=aimColor;
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
            if(!Active){if(_nativeUI!=null)_nativeUI.SetActive(false);return;}Theme();
            GolfBall ball=MyBall();float scale=Mathf.Max(.75f,Screen.height/900f);
            if(!BeginNative(scale))return;
            try
            {
                float width=Screen.width/scale,height=Screen.height/scale;
                if(_card){DrawCard(width,height);return;}
                var rect=new Rect(width/2-225,height-175,450,110);Panel(rect);
                string info="E at a tee to start a hole";
                if(ball!=null&&Rules.Parse(ball.Data.GetString(GolfWorld.LabelKey,""),out var hole))
                {
                    ZDO cup=ZDOMan.instance?.GetZDO(ball.Data.GetZDOID(GolfWorld.CupKey));
                    info=$"{hole.Course} · {hole.Number}"+(ball.Data.GetInt(GolfMatches.Length,0)>0?$"/{ball.Data.GetInt(GolfMatches.Length,0)}":"")+$" / par {hole.Par} · {ball.Strokes} strokes"+
                        (ball.Closed?" · stopped":ball.Done?" · finished":cup!=null?$" · {Vector3.Distance(ball.transform.position,cup.GetPosition()):0.0} m to cup":"");
                }
                NativeLabel(new Rect(rect.x+15,rect.y+4,420,36),Modes[_mode]+(_aim!=null&&_aim.enabled?(_preview.DisplayComplete?$" · ~{_preview.Distance:0.0} m ({_preview.PredictedPower*100:0}%)":" · aim guide"):"")+(_charging?$" — {Power*100:0}%":ball!=null&&!ball.Done&&!ball.Still?" — ball moving":""),_heading);
                NativeLabel(new Rect(rect.x+15,rect.y+35,420,22),info,_label);
                NativeLabel(new Rect(rect.x+15,rect.y+64,420,25),(_preview.DisplayComplete&&_preview.Hazard?"Water hazard — last lie +1":ball!=null&&!ball.Done&&!ball.Closed?$"{ShotPhysics.Lie(ball.Body.position)} · Wheel: shot · {_cardKey.Value}: match / scores":$"Wheel: shot · hold/release left mouse · {_cardKey.Value}: match / scores"),_small);
                if(_charging){UIBox(new Rect(rect.x+15,rect.y+96,420*Power,5),null,new Color(.84f,.63f,.30f));}
            }
            finally{EndNative();}
        }
        private void DrawCard(float width,float height)
        {
            ZDO mine=GolfMatches.MyData;string match=GolfMatches.MyMatch;
            var scores=GolfMatches.BoardId==match&&match.Length>0?GolfMatches.Board.Select(row=>new GolfMatches.Score{Name=row.Name,Player=row.Player,Card=row.Card,Holes=row.Holes,State=row.State}).ToList():new System.Collections.Generic.List<GolfMatches.Score>();
            foreach(GolfBall b in GolfBall.Loaded.Where(b=>b!=null&&b.Data!=null&&(match.Length>0?b.Data.GetString(GolfMatches.Id,"")==match:Vector3.Distance(Player.m_localPlayer.transform.position,b.transform.position)<120)))
            {
                long player=b.Data.GetLong(GolfWorld.PlayerKey,0);scores.RemoveAll(row=>row.Player==player);
                scores.Add(new GolfMatches.Score{Name=b.Data.GetString(GolfWorld.NameKey,"Golfer"),Player=player,Card=b.Data.GetString(GolfWorld.CardKey,""),Holes=b.Data.GetInt(GolfMatches.Length,18),State=GolfMatches.State(b.Data)});
            }
            var golfers=scores.OrderByDescending(row=>Rules.ReadCard(row.Card).Count).ThenBy(row=>Rules.ReadCard(row.Card).Sum(h=>h.Strokes-h.Par)).ThenBy(row=>row.Name).Take(8).ToArray();
            Rect r=new Rect(width/2-390,Mathf.Max(50,height/2-255),780,575);Panel(r);
            NativeLabel(new Rect(r.x+22,r.y+12,730,40),(_roundTest?$"GOLF TEST · HOLE {_roundHole} · ESCAPE CANCELS":"MEADOW GOLF — MATCH & SCORECARD"),_heading);
            NativeLabel(new Rect(r.x+22,r.y+52,730,24),"At hole 1: [1] Start 9 holes   [2] Start 18 holes   [J] Join match",_small);
            NativeLabel(new Rect(r.x+22,r.y+78,730,24),"[X] Stop your round · [M] End match (starter / builder / host) · [R] Refresh scores",_small);
            GolfMarker first=GolfMatches.FirstTee();
            NativeLabel(new Rect(r.x+22,r.y+104,730,24),first==null?(mine!=null&&Rules.Parse(mine.GetString(GolfWorld.LabelKey,""),out var cardHole)?$"Course: {cardHole.Course} · E at the next tee continues":"Stand near the first tee to start / join; E at each next tee continues."):$"Course: {first.Hole.Course} · "+(first.View.GetZDO().GetBool(GolfMatches.Open,false)?$"Open {first.View.GetZDO().GetInt(GolfMatches.Length,0)}-hole match":"No open match"),_small);
            Panel(new Rect(r.x+16,r.y+137,748,387),true);
            float y=r.y+143;
            foreach(var golfer in golfers)
            {
                var rows=Rules.ReadCard(golfer.Card);int total=rows.Sum(row=>row.Strokes),par=rows.Sum(row=>row.Par);
                NativeLabel(new Rect(r.x+22,y,730,24),$"{golfer.Name} · {golfer.State} · {rows.Count}/{golfer.Holes} holes   {total} / par {par}"+(rows.Count>0?$"  ({(total-par>0?"+":"")}{total-par})":""),_label);
                for(int h=1;h<=golfer.Holes;h++)
                {var result=rows.FirstOrDefault(row=>row.Hole==h);NativeLabel(new Rect(r.x+22+(h-1)*40,y+25,40,22),$"{h}:{(result==null?"—":result.Strokes.ToString())}",_small);}
                y+=46;
            }
            if(golfers.Length==0)NativeLabel(new Rect(r.x+22,y,730,30),"Start a hole at a tee to begin your card.",_label);
            NativeLabel(new Rect(r.x+22,r.y+535,730,24),$"{_cardKey.Value} / Escape: close · E on your ball: last lie (+1) · E on tee: return (+1)",_small);
        }
        private void Theme()
        {
            if(_label!=null)return;
            _label=new GUIStyle{fontSize=16,normal={textColor=new Color(.94f,.89f,.77f)}};
            _heading=new GUIStyle(_label){fontSize=26,fontStyle=FontStyle.Bold,normal={textColor=new Color(1f,.77f,.2f)}};_small=new GUIStyle(_label){fontSize=14,normal={textColor=new Color(.77f,.73f,.64f)}};
        }
        private void OnDestroy()
        {
            if(_nativeUI!=null)Destroy(_nativeUI);
            _preview.Dispose();CommandsStop();GolfWorld.Stop();_harmony?.UnpatchSelf();Prefabs.Unregister();
            if(_aimEnd!=null)Destroy(_aimEnd.gameObject);if(_aim!=null){Destroy(_aim.sharedMaterial);Destroy(_aim.gameObject);}
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
            ball.PreparePhysics();Gravity(__instance)=!ball.Done&&!ball.Closed;
            return !ball.Done&&!ball.Closed; // Completed balls already stored their final pose and zero velocities.
        }
    }
    [HarmonyPatch(typeof(Menu),"Update")]
    internal static class CardEscape{private static bool Prefix()=>!Plugin.ReservesEscape;}
    [HarmonyPatch(typeof(Player),"TakeInput")]
    internal static class CardPlayerInput
    {private static void Postfix(Player __instance,ref bool __result){if(__instance==Player.m_localPlayer&&Plugin.CardOpen)__result=false;}}
    [HarmonyPatch(typeof(PlayerController),"TakeInput")]
    internal static class CardControllerInput
    {private static void Postfix(ref bool __result){if(Plugin.CardOpen)__result=false;}}
    [HarmonyPatch(typeof(Character),"Awake")]
    internal static class WalkThroughBalls
    {private static void Postfix(Character __instance){foreach(GolfBall b in GolfBall.Loaded)if(b!=null)b.Ignore(__instance.GetComponentsInChildren<Collider>());}}
}
