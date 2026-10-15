using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // The rider's ride to Valhalla, played on their own game only. The player's body never moves: it stays standing by the silo, hidden,
    // unhurtable and without input (GameUi's input patches, via Rocket.WindowOpen), while the camera does the flying: the countdown
    // seen from round the pad, liftoff from the ground, then chasing the rocket up through the clouds with a Valkyrie alongside (the
    // game's intro, the other way round), the sky going gold, VALHALLA, the credits, and a fade back down to the silo with the
    // Einherjar's horn in the bag.
    //
    // Safety: everything taken away is given back in Restore, which runs on finishing, on Escape (Skip goes straight to the end), when the
    // player dies or leaves, and when the mod unloads (Stop). The horn is promised in the character's custom data the moment they board
    // ("fh_valhalla_pending"), so a crash or reload mid-flight still hands it over the next time the mod sees them.
    internal static class Flight
    {
        // The timeline, seconds after boarding (the silo's ZDO time, the same on every game).
        internal const float Ignite=6f,Liftoff=7f,Gone=32f,Gold=25f,Title=30f,Credits=36f,Fade=60f,End=63f;
        internal const string Pending="fh_valhalla_pending",First="fh_first",Launches="fh_launches";
        // How high the rocket is: held by the clamps until liftoff, then climbing harder and harder.
        internal static float Height(double t)=>t<Liftoff?0f:(float)(0.5*4.0*(t-Liftoff)*(t-Liftoff)+(t-Liftoff)*2.0);

        internal static bool Active{get;private set;}
        private static double _start;private static Vector3 _pad;private static Quaternion _turn;
        private static bool _skip;private static Silo _silo;
        private static readonly List<Renderer> Hidden=new List<Renderer>();
        private static bool _hudWas;
        private static GameObject _valkyrie;private static AudioSource _roar;private static bool _horned;
        private static Vector3 _camPos;private static Quaternion _camRot;
        private static string[] _credits=new string[0];
        private static Font _norse,_serif;

        internal static void Start(){}

        // ---- in and out ----
        internal static void Begin(Silo silo,double start)
        {
            Player p=Player.m_localPlayer;
            if(p==null||Active)return;
            _silo=silo;_start=start;_pad=silo.transform.position;_turn=silo.transform.rotation;_skip=false;_horned=false;
            p.m_customData[Pending]="1";
            Active=true;
            // hide the body (only what's showing now, so we give back exactly that)
            Hidden.Clear();
            foreach(Renderer r in p.GetComponentsInChildren<Renderer>())if(r.enabled){r.enabled=false;Hidden.Add(r);}
            if(global::Hud.instance!=null){_hudWas=global::Hud.instance.m_userHidden;global::Hud.instance.m_userHidden=true;}
            if(InventoryGui.IsVisible())InventoryGui.instance.Hide();
            _credits=CreditLines(p,silo);
            FindFonts();
            Camera cam=Camera.main;
            if(cam!=null){_camPos=cam.transform.position;_camRot=cam.transform.rotation;}
            _roar=Sound.Loop(Fx.Rumble(),cam!=null?cam.transform:p.transform,50f);
            if(_roar!=null)_roar.spatialBlend=0f;
            Plugin.Log($"{p.GetPlayerName()} boarded the rocket");
        }

        // Escape (and `fh silo launch skip`): straight to the end, horn and all.
        internal static void Skip(){if(Active)_skip=true;}

        // The mod is unloading: give everything back, keep the horn promised.
        internal static void Stop(){if(Active)Restore();}

        private static void Restore()
        {
            Active=false;
            foreach(Renderer r in Hidden)if(r!=null)r.enabled=true;
            Hidden.Clear();
            if(global::Hud.instance!=null)global::Hud.instance.m_userHidden=_hudWas;
            if(_valkyrie!=null)Object.Destroy(_valkyrie);
            _valkyrie=null;
            if(_roar!=null)Object.Destroy(_roar.gameObject);
            _roar=null;_silo=null;
            // (the game's camera takes over again next frame: GameCamera.LateUpdate is only skipped while Active)
        }

        private static void Finish()
        {
            Player p=Player.m_localPlayer;
            Restore();
            if(p!=null&&!p.IsDead())Reward(p);
        }

        // The horn, the world's memory of it, and the news for everyone.
        private static void Reward(Player p)
        {
            if(!p.m_customData.ContainsKey(Pending))return;
            p.m_customData.Remove(Pending);
            p.m_customData[Launches]=((p.m_customData.TryGetValue(Launches,out string s)&&int.TryParse(s,out int n)?n:0)+1).ToString();
            GameObject horn=Ports.Prefab(Rocket.Trophy);
            if(horn!=null&&!p.GetInventory().AddItem(horn,1))
            {
                // (a full bag: at their feet, not lost)
                Object.Instantiate(horn,p.transform.position+p.transform.forward*0.8f+Vector3.up,Quaternion.identity);
            }
            ZoneSystem.instance?.SetGlobalKey("fh_valhalla");
            var pkg=new ZPackage();pkg.Write(p.GetPlayerName());
            Net.Everyone("FH_Valhalla",pkg);
            Sound.Play(Fx.Horn(),p.transform.position,1f,1f,80f);
            Plugin.Log($"{p.GetPlayerName()} rode to Valhalla");
        }

        // ---- every frame ----
        internal static void Tick()
        {
            Player p=Player.m_localPlayer;
            if(!Active)
            {
                // a horn promised before a crash or reload: hand it over now
                if(p!=null&&!p.IsDead()&&p.m_customData.ContainsKey(Pending)&&ZNet.instance!=null&&Time.timeSinceLevelLoad>5f)Reward(p);
                return;
            }
            if(p==null||p.IsDead()||ZNet.instance==null){Restore();return;}
            double t=ZNet.instance.GetTimeSeconds()-_start;
            if(_skip||t>=End||t<-5){Finish();return;}
            // the horn as the title comes up; the engine's roar fades out in the gold
            if(!_horned&&t>=Title){_horned=true;Sound.Play(Fx.Horn(),Camera.main!=null?Camera.main.transform.position:p.transform.position,1f,1f,80f);}
            if(_roar!=null)_roar.volume=Plugin.Volume.Value*(t<Ignite?0f:Mathf.Clamp01((float)(t-Ignite)*2f)*Mathf.Clamp01((float)(Title-t)/5f));
            // keep them out of harm's way: no menus open under the ride
            if(InventoryGui.IsVisible())InventoryGui.instance.Hide();
        }

        internal static void LateTick()
        {
            if(!Active)return;
            Camera cam=Camera.main;
            if(cam==null||ZNet.instance==null)return;
            double t=ZNet.instance.GetTimeSeconds()-_start;
            Vector3 up=Vector3.up,fwd=_turn*Vector3.forward,right=_turn*Vector3.right;
            Vector3 rocket=_pad+up*Height(t);
            Vector3 pos;Vector3 look;
            if(t<Ignite)
            {
                // round the pad while the clock runs down
                float a=(float)(200+t*7)*Mathf.Deg2Rad;
                pos=_pad+(fwd*Mathf.Cos(a)+right*Mathf.Sin(a))*34f+up*(9f+(float)t*0.6f);
                look=_pad+up*11f;
            }
            else if(t<13)
            {
                // from the ground in front, looking up as it goes
                pos=_pad+fwd*30f+right*11f+up*2.2f;
                look=rocket+up*11f;
            }
            else
            {
                // chasing it up through the clouds
                float k=Mathf.Clamp01((float)(t-13)/2f);
                Vector3 ground=_pad+fwd*30f+right*11f+up*2.2f;
                Vector3 chase=rocket+right*20f+fwd*12f-up*7f;
                pos=Vector3.Lerp(ground,chase,k*k*(3-2*k));
                look=rocket+up*13f;
            }
            // shake while the engines are loud
            if(t>Ignite&&t<Gold)
            {
                float s=(t<Liftoff+4?0.45f:0.2f)*Mathf.Clamp01((float)(t-Ignite)*2);
                pos+=new Vector3(Mathf.PerlinNoise(Time.time*25f,1f)-0.5f,Mathf.PerlinNoise(2f,Time.time*25f)-0.5f,Mathf.PerlinNoise(Time.time*23f,3f)-0.5f)*s;
            }
            if(t>=Gold+5)return;   // (behind the gold: hold the last view)
            cam.transform.position=pos;
            cam.transform.rotation=Quaternion.LookRotation(look-pos,up);
            cam.fieldOfView=62f;
            ZCursor.LockState=CursorLockMode.Locked;ZCursor.Hide();
            Valkyrie(t,rocket,fwd,right);
        }

        // She meets the rocket in the clouds and flies alongside, between it and the camera's side.
        private static void Valkyrie(double t,Vector3 rocket,Vector3 fwd,Vector3 right)
        {
            if(t<15||t>Gold+4){if(_valkyrie!=null)_valkyrie.SetActive(false);return;}
            if(_valkyrie==null){_valkyrie=Fx.Valkyrie();if(_valkyrie==null)return;}
            _valkyrie.SetActive(true);
            float k=Mathf.Clamp01((float)(t-15)/4f);
            Vector3 from=rocket+right*70f+fwd*40f+Vector3.up*10f,beside=rocket+right*9f+fwd*5f+Vector3.up*12f;
            Vector3 at=Vector3.Lerp(from,beside,k*k*(3-2*k));
            at+=Vector3.up*Mathf.Sin((float)t*1.7f)*0.8f;
            _valkyrie.transform.position=at;
            _valkyrie.transform.rotation=Quaternion.LookRotation(Vector3.Lerp(-right,Vector3.up,0.25f+0.6f*k),fwd);
        }

        // ---- the screen ----
        private static Texture2D _white;
        internal static void Gui()
        {
            if(!Active||ZNet.instance==null)return;
            double t=ZNet.instance.GetTimeSeconds()-_start;
            if(_white==null){_white=new Texture2D(1,1){hideFlags=HideFlags.HideAndDontSave};_white.SetPixel(0,0,Color.white);_white.Apply();}
            float w=Screen.width,h=Screen.height,s=Mathf.Max(0.6f,h/1080f);
            // letterbox bars
            float bar=h*0.09f;
            GUI.color=Color.black;GUI.DrawTexture(new Rect(0,0,w,bar),_white);GUI.DrawTexture(new Rect(0,h-bar,w,bar),_white);
            // the countdown
            if(t>=1&&t<Liftoff+2)
            {
                string n=t<Liftoff?Mathf.CeilToInt((float)(Liftoff-t)).ToString():"LIFTOFF";
                float pulse=1f-(float)(t-Math.Floor(t));
                Text(n,new Rect(0,h*0.3f,w,h*0.2f),Mathf.RoundToInt((t<Liftoff?110:90)*s),new Color(1f,0.85f,0.45f,Mathf.Clamp01(pulse*1.5f)),_norse);
            }
            // gold
            float gold=Mathf.Clamp01((float)(t-Gold)/5f);
            if(gold>0){GUI.color=new Color(0.98f,0.82f,0.42f,gold);GUI.DrawTexture(new Rect(0,0,w,h),_white);}
            if(t>=Gold+2&&t<Fade+1)
            {
                // light rays: a few soft white beams from the top
                for(int i=0;i<7;i++)
                {
                    float x=w*(0.08f+0.14f*i)+Mathf.Sin((float)t*0.3f+i)*w*0.02f;
                    GUI.color=new Color(1,1,1,0.07f*gold*(0.6f+0.4f*Mathf.Sin((float)t*0.8f+i*1.3f)));
                    GUI.DrawTexture(new Rect(x,0,w*0.05f,h),_white);
                }
            }
            // VALHALLA
            if(t>=Title&&t<Credits+2)
            {
                float a=Mathf.Clamp01((float)(t-Title)/1.5f)*Mathf.Clamp01((float)(Credits+2-t)/1.5f);
                Text("VALHALLA",new Rect(0,h*0.32f,w,h*0.2f),Mathf.RoundToInt(150*s),new Color(0.35f,0.2f,0.05f,a),_norse);
                Text("The hall of the slain opens its doors",new Rect(0,h*0.52f,w,h*0.08f),Mathf.RoundToInt(34*s),new Color(0.35f,0.2f,0.05f,a*0.9f),_serif);
            }
            // the credits, rolling up
            if(t>=Credits&&t<Fade+1)
            {
                float line=46*s,scroll=(float)(t-Credits)*62f*s;
                float y0=h-bar-scroll+20*s;
                for(int i=0;i<_credits.Length;i++)
                {
                    float y=y0+i*line;
                    if(y<bar-line||y>h-bar)continue;
                    float edge=Mathf.Clamp01((y-bar)/(h*0.12f))*Mathf.Clamp01((h-bar-y)/(h*0.12f));
                    string c=_credits[i];
                    bool head=c.StartsWith("#");
                    Text(head?c.Substring(1):c,new Rect(0,y,w,line),Mathf.RoundToInt((head?40:30)*s),new Color(0.3f,0.17f,0.04f,edge),head?_norse:_serif);
                }
            }
            // and back down: through black
            float black=Mathf.Clamp01((float)(t-Fade)/2f)*Mathf.Clamp01((float)(End+0.5f-t)/1.5f);
            if(black>0){GUI.color=new Color(0,0,0,black);GUI.DrawTexture(new Rect(0,0,w,h),_white);}
            if(t<Fade)Text("[Esc] skip",new Rect(0,h-bar*0.8f,w-20*s,bar*0.6f),Mathf.RoundToInt(18*s),new Color(1,1,1,0.4f),_serif,TextAnchor.MiddleRight);
            GUI.color=Color.white;
        }
        private static GUIStyle _style;
        private static void Text(string text,Rect r,int size,Color c,Font font,TextAnchor align=TextAnchor.MiddleCenter)
        {
            if(_style==null)_style=new GUIStyle(GUI.skin.label){wordWrap=false,richText=false};
            _style.font=font;_style.fontSize=size;_style.alignment=align;
            GUI.color=Color.white;_style.normal.textColor=c;
            GUI.Label(r,text,_style);
        }
        // The game's fonts: Norse for titles, Averia for the rest (looked up once, when boarding).
        private static void FindFonts()
        {
            if(_norse!=null&&_serif!=null)return;
            Font[] all=Resources.FindObjectsOfTypeAll<Font>();
            _norse=all.FirstOrDefault(f=>f.name=="Norsebold")??all.FirstOrDefault(f=>f.name.StartsWith("Norse"));
            _serif=all.FirstOrDefault(f=>f.name.StartsWith("AveriaSerifLibre-Bold"))??all.FirstOrDefault(f=>f.name.StartsWith("AveriaSerif"));
        }

        // What the credits say about the run.
        private static string[] CreditLines(Player p,Silo silo)
        {
            var lines=new List<string>();
            string name=p.GetPlayerName();
            double now=ZNet.instance.GetTimeSeconds();
            long dayLength=EnvMan.instance!=null?Math.Max(1L,EnvMan.instance.m_dayLengthSec):1200L;
            int day=EnvMan.instance!=null?EnvMan.instance.GetDay():0;
            lines.Add("#Factorheim");
            lines.Add($"{name}, chief engineer");
            lines.Add("");
            lines.Add($"Rode fire to Valhalla on day {day}");
            if(p.m_customData.TryGetValue(First,out string f)&&double.TryParse(f,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double first))
            {
                double days=(now-first)/dayLength;
                lines.Add(days<1?"The factory was built in less than a day":$"The factory was {days:0} days in the making");
            }
            lines.Add($"Rocket parts: {Silo.Needed}");
            int tier=Tech.Level;
            lines.Add($"Research: {Tech.Names[Mathf.Clamp(tier,0,Tech.Top)]} (tier {tier} of {Tech.Top})");
            int launches=(p.m_customData.TryGetValue(Launches,out string l)&&int.TryParse(l,out int n)?n:0)+1;
            lines.Add(launches==1?"Your first ride":$"Ride number {launches}");
            int machines=Live<Machine>.All.Count(m=>m!=null);
            if(machines>0)lines.Add($"Machines humming below: {machines}");
            lines.Add("");
            lines.Add("#The einherjar");
            lines.Add("raise their horns to you");
            lines.Add("Odin nods, once");
            lines.Add("Thor asks how the engines work");
            lines.Add("Loki asks how the engines break");
            lines.Add("");
            lines.Add("#Thanks to");
            lines.Add("every copper vein that never ran dry");
            lines.Add("the belts, the inserters, the smelters");
            lines.Add("the greydwarves who watched it all, confused");
            lines.Add("");
            lines.Add("#Factorheim");
            lines.Add("a mod by Bob");
            lines.Add("");
            lines.Add("The factory must grow");
            return lines.ToArray();
        }
    }

    // While riding, the game's camera rests (Flight drives it) and nothing hurts the rider.
    [HarmonyPatch(typeof(GameCamera),"LateUpdate")]
    internal static class FlightCamera{private static bool Prefix()=>!Flight.Active;}
    [HarmonyPatch(typeof(Character),nameof(Character.Damage))]
    internal static class FlightUnhurt{private static bool Prefix(Character __instance)=>!(Flight.Active&&__instance==Player.m_localPlayer);}

    // When the local player first builds anything of ours: the factory's founding day, for the credits.
    [HarmonyPatch(typeof(Piece),nameof(Piece.SetCreator))]
    internal static class FactoryFounded
    {
        private static void Postfix(Piece __instance,long uid)
        {
            Player p=Player.m_localPlayer;
            if(p==null||uid!=p.GetPlayerID()||__instance==null||!__instance.gameObject.name.StartsWith("FH_")||ZNet.instance==null)return;
            if(!p.m_customData.ContainsKey(Flight.First))
                p.m_customData[Flight.First]=ZNet.instance.GetTimeSeconds().ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
