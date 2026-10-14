using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // The laser pistol and rifle. Both are copies of the Arbalest (its aiming carry, the Crossbows skill), as Quad's Guns does it, and
    // the game's attack never fires them: StartAttack comes here (Things.AttackInput) and shoots a hitscan Beam from the muzzle to
    // whatever is under the crosshair. No ammo; shots heat the gun, and an overheated gun locks out until it has cooled.
    // Pistol: one shot per click. Rifle: hold to charge (a full charge pierces several bodies), release to fire; hold block to zoom.
    internal sealed class Lasers:Feature
    {
        internal override string Name=>"Lasers";
        private const string ShotRpc="WU_LaserShot";

        private static ConfigEntry<float> PDamage,PRange,PCooldown,PHeat,RDamage,RRange,RCooldown,RHeat,RCharge,RChargedX,RZoom,Cool;
        private static ConfigEntry<int> RPierce;
        private static ConfigEntry<bool> Buildings;

        internal static readonly ItemDef Pistol=new ItemDef{Prefab="WU_LaserPistol",Name="Laser Pistol",Base="CrossbowArbalest",
            Description="A hand blaster from a universe with better ideas. Click to shoot a bolt of red light at whatever you point at. It runs hot: shoot too fast and it locks up until it cools. Leaves the off hand free for a shield.",
            Cost="Iron:8, SurtlingCore:2, Copper:4",Station="forge",Level=1,Weight=1.2f,AttachEuler=new Vector3(0,90,0),Model=PistolModel,Stats=PistolStats};
        internal static readonly ItemDef Rifle=new ItemDef{Prefab="WU_LaserRifle",Name="Laser Rifle",Base="CrossbowArbalest",
            Description="A long gun that shoots light. Hold attack to charge and let go to fire; a full charge burns straight through a line of enemies. Hold block to look down the scope. Gets hot.",
            Cost="Iron:16, SurtlingCore:4, Crystal:3",Station="forge",Level=2,Weight=4f,AttachEuler=new Vector3(0,90,0),Model=RifleModel,Stats=RifleStats};
        internal override IEnumerable<ItemDef> Items=>new[]{Pistol,Rifle};

        internal override void Bind(ConfigFile c)
        {
            const string S="1 - Lasers";
            PDamage=c.Bind(S,"PistolDamage",48f,new ConfigDescription("Damage of one pistol bolt (60% pierce, 40% lightning).",new AcceptableValueRange<float>(1,1000)));
            PRange=c.Bind(S,"PistolRange",120f,new ConfigDescription("How far a pistol bolt reaches, in metres.",new AcceptableValueRange<float>(10,500)));
            PCooldown=c.Bind(S,"PistolCooldown",0.16f,new ConfigDescription("Seconds between pistol shots, at the fastest clicking.",new AcceptableValueRange<float>(0.03f,2)));
            PHeat=c.Bind(S,"PistolHeatPerShot",0.11f,new ConfigDescription("Heat each pistol shot adds (1 = overheated).",new AcceptableValueRange<float>(0,1)));
            RDamage=c.Bind(S,"RifleDamage",130f,new ConfigDescription("Damage of an uncharged rifle shot (60% pierce, 40% lightning).",new AcceptableValueRange<float>(1,2000)));
            RRange=c.Bind(S,"RifleRange",260f,new ConfigDescription("How far a rifle shot reaches, in metres.",new AcceptableValueRange<float>(20,800)));
            RCooldown=c.Bind(S,"RifleCooldown",0.65f,new ConfigDescription("Seconds between rifle shots.",new AcceptableValueRange<float>(0.05f,5)));
            RHeat=c.Bind(S,"RifleHeatPerShot",0.2f,new ConfigDescription("Heat an uncharged rifle shot adds; a charged one adds twice that.",new AcceptableValueRange<float>(0,1)));
            RCharge=c.Bind(S,"RifleChargeSeconds",1.2f,new ConfigDescription("How long holding attack takes to fully charge the rifle.",new AcceptableValueRange<float>(0.2f,5)));
            RChargedX=c.Bind(S,"RifleChargedMultiplier",2.5f,new ConfigDescription("Damage of a fully charged shot, times an uncharged one.",new AcceptableValueRange<float>(1,10)));
            RPierce=c.Bind(S,"RifleChargedPierce",4,new ConfigDescription("How many bodies a fully charged shot burns through.",new AcceptableValueRange<int>(1,20)));
            RZoom=c.Bind(S,"RifleZoom",3f,new ConfigDescription("How far the rifle's scope zooms in (hold block).",new AcceptableValueRange<float>(1,8)));
            Cool=c.Bind(S,"CoolingPerSecond",0.45f,new ConfigDescription("How much heat both guns lose each second.",new AcceptableValueRange<float>(0.05f,5)));
            Buildings=c.Bind(S,"HitBuildings",false,"Laser shots also damage build pieces (walls, floors...). Off, they pass harmlessly over your own base.");
            Pistol.Primary=PistolTrigger;
            Rifle.Primary=RifleTrigger;
        }

        // ---- the items ----------------------------------------------------------------------------------------------

        private static void PistolStats(ItemDrop.ItemData.SharedData s)
        {
            s.m_itemType=ItemDrop.ItemData.ItemType.OneHandedWeapon;   // (the left hand stays free for a shield)
            s.m_damages=Damage(PDamage.Value);
            GunStats(s);
        }
        private static void RifleStats(ItemDrop.ItemData.SharedData s)
        {
            s.m_damages=Damage(RDamage.Value);
            GunStats(s);
        }
        private static void GunStats(ItemDrop.ItemData.SharedData s)
        {
            s.m_ammoType="";s.m_useDurability=false;s.m_skillType=Skills.SkillType.Crossbows;
            s.m_blockPower=0f;s.m_deflectionForce=0f;s.m_attackForce=10f;
            s.m_attack.m_requiresReload=false;s.m_attack.m_bowDraw=false;s.m_attack.m_attackStamina=0f;
            s.m_attack.m_attackType=Attack.AttackType.None;   // (if the game's attack ever runs, it fires nothing)
            if(s.m_secondaryAttack!=null){s.m_secondaryAttack.m_attackType=Attack.AttackType.None;s.m_secondaryAttack.m_attackStamina=0f;s.m_secondaryAttack.m_requiresReload=false;}
            Silence(s);Silence(s.m_attack);Silence(s.m_secondaryAttack);   // (the Arbalest's bolt sounds and shockwave)
        }
        private static void Silence(object owner)
        {
            if(owner==null)return;
            foreach(FieldInfo f in owner.GetType().GetFields())if(f.FieldType==typeof(EffectList))f.SetValue(owner,new EffectList());
        }
        private static HitData.DamageTypes Damage(float total)=>new HitData.DamageTypes{m_pierce=total*0.6f,m_lightning=total*0.4f};

        // Models: barrel along +Z, up +Y, the grip hand at the origin (Guns' convention, turned into the hand by AttachEuler).
        private static void PistolModel(Transform root)
        {
            var b=new Look.Builder(root);
            Material white=Look.Paint(Look.White,0.2f,0.7f),black=Look.Paint(Look.Black,0.3f,0.4f),metal=Look.Paint(Look.Gunmetal,0.8f,0.6f),red=Look.Glow(Look.Red);
            b.Box("grip",new Vector3(0,-0.055f,-0.03f),new Vector3(0.034f,0.11f,0.048f),black,new Vector3(-14,0,0));
            b.Box("body",new Vector3(0,0.025f,0.05f),new Vector3(0.046f,0.058f,0.2f),white);
            b.Box("top rail",new Vector3(0,0.06f,0.04f),new Vector3(0.022f,0.014f,0.16f),metal);
            b.Box("guard",new Vector3(0,-0.022f,0.03f),new Vector3(0.012f,0.012f,0.07f),black);
            b.Tube("barrel",new Vector3(0,0.03f,0.19f),0.032f,0.08f,metal);
            for(int i=0;i<3;i++)b.Tube("fin",new Vector3(0,0.03f,0.165f+i*0.022f),0.044f,0.006f,metal);
            b.Tube("emitter",new Vector3(0,0.03f,0.232f),0.026f,0.008f,red);
            b.Box("charge strip L",new Vector3(0.0235f,0.03f,0.05f),new Vector3(0.003f,0.01f,0.14f),red);
            b.Box("charge strip R",new Vector3(-0.0235f,0.03f,0.05f),new Vector3(0.003f,0.01f,0.14f),red);
            b.Point("wu_muzzle",new Vector3(0,0.03f,0.245f));
        }
        private static void RifleModel(Transform root)
        {
            var b=new Look.Builder(root);
            Material white=Look.Paint(Look.White,0.2f,0.7f),black=Look.Paint(Look.Black,0.3f,0.4f),metal=Look.Paint(Look.Gunmetal,0.8f,0.6f),
                blue=Look.Glow(Look.Blue),lens=Look.Paint(Look.Lens,0.9f,0.95f,1.2f);
            b.Box("grip",new Vector3(0,-0.06f,-0.02f),new Vector3(0.036f,0.12f,0.05f),black,new Vector3(-14,0,0));
            b.Box("stock",new Vector3(0,0.0f,-0.3f),new Vector3(0.05f,0.1f,0.28f),white);
            b.Box("butt",new Vector3(0,-0.02f,-0.45f),new Vector3(0.055f,0.15f,0.04f),black);
            b.Box("receiver",new Vector3(0,0.03f,0.08f),new Vector3(0.06f,0.09f,0.36f),white);
            b.Box("power cell",new Vector3(0,-0.04f,0.16f),new Vector3(0.04f,0.09f,0.07f),metal);
            b.Box("cell glow",new Vector3(0,-0.04f,0.196f),new Vector3(0.03f,0.07f,0.004f),blue);
            b.Tube("barrel",new Vector3(0,0.04f,0.48f),0.034f,0.48f,metal);
            for(int i=0;i<6;i++)b.Tube("coil",new Vector3(0,0.04f,0.3f+i*0.065f),0.052f,0.014f,blue);
            b.Tube("shroud",new Vector3(0,0.04f,0.73f),0.05f,0.06f,black);
            b.Tube("emitter",new Vector3(0,0.04f,0.762f),0.03f,0.006f,blue);
            b.Tube("scope",new Vector3(0,0.12f,0.08f),0.04f,0.22f,black);
            b.Tube("scope lens",new Vector3(0,0.12f,0.192f),0.034f,0.004f,lens);
            b.Box("scope mount",new Vector3(0,0.085f,0.08f),new Vector3(0.02f,0.03f,0.08f),metal);
            b.Point("wu_muzzle",new Vector3(0,0.04f,0.775f));
        }

        // ---- firing -------------------------------------------------------------------------------------------------

        private static float _heat,_next,_chargeStart=-1f,_flashUntil,_lastShot;
        private static bool _overheated,_released=true;
        private static ItemDef _held;
        private static Light _flash;
        private static AudioClip _pew,_pshew,_bigShot,_hiss,_click;
        private static readonly MethodInfo TakeInput=AccessTools.Method(typeof(Player),"TakeInput");
        private static readonly AccessTools.FieldRef<VisEquipment,GameObject> RightInstance=AccessTools.FieldRefAccess<VisEquipment,GameObject>("m_rightItemInstance");

        internal static bool InputAllowed(Player p)
        {
            if(p==null||p!=Player.m_localPlayer)return false;
            try{return (bool)TakeInput.Invoke(p,null);}catch{return true;}
        }
        private static bool TriggerHeld(Player p)=>InputAllowed(p)&&(ZInput.GetButton("Attack")||ZInput.GetButton("JoyAttack"));
        private static bool CanShoot(Player p)=>!(p.IsDead()||p.InDodge()||p.IsStaggering()||p.IsTeleporting()||p.InMinorAction()||p.IsAttached()&&!p.IsAttachedToShip()||p.IsSwimming()||p.InPlaceMode());

        private static bool PistolTrigger(Player p,ItemDrop.ItemData item)
        {
            if(!CanShoot(p)||_overheated||!_released||Time.time<_next)return true;   // (true: "attacking", so nothing stays queued)
            _released=false;
            Shoot(p,Pistol,1f);
            return true;
        }
        private static bool RifleTrigger(Player p,ItemDrop.ItemData item)
        {
            if(!CanShoot(p)||_overheated||Time.time<_next)return true;
            if(_chargeStart<0f&&_released){_chargeStart=Time.time;_released=false;}
            return true;
        }
        internal static float Charge=>_chargeStart<0f?0f:Mathf.Clamp01((Time.time-_chargeStart)/RCharge.Value);

        // One shot from the muzzle to whatever the crosshair is on. charge (rifle): 0..1.
        internal static void Shoot(Player p,ItemDef d,float charge)
        {
            bool rifle=d==Rifle;
            Transform muzzle=Muzzle(p);
            Vector3 from=muzzle!=null?muzzle.position:p.transform.position+Vector3.up*1.4f+p.transform.forward*0.6f;
            float range=rifle?RRange.Value:PRange.Value;
            Vector3 dir=AimAt(p,from,range);
            Color color=rifle?Look.Blue:Look.Red;
            bool full=rifle&&charge>=0.99f;
            if(rifle)
            {
                float dmg=RDamage.Value*Mathf.Lerp(1f,RChargedX.Value,charge);
                if(full)Pierce(p,from,dir,range,Damage(dmg),color,RPierce.Value);
                else Beam.Fire(p,from,dir,range,Damage(dmg),color,0.06f+0.08f*charge,25f,Skills.SkillType.Crossbows,Buildings.Value,0.22f);
            }
            else Beam.Fire(p,from,dir,range,Damage(PDamage.Value),color,0.045f,8f,Skills.SkillType.Crossbows,Buildings.Value,0.12f);
            p.RaiseSkill(Skills.SkillType.Crossbows,rifle?0.6f:0.25f);
            _heat+=rifle?RHeat.Value*(full?2f:1f):PHeat.Value;
            if(_heat>=1f){_heat=1f;_overheated=true;Sound.Play(Hiss(),from,0.7f);}
            _next=Time.time+(rifle?RCooldown.Value:PCooldown.Value);
            _lastShot=Time.time;
            Flash(muzzle,color);
            int kind=rifle?(full?2:1):0;
            Sound.Play(Clip(kind),from,kind==2?1f:0.75f,UnityEngine.Random.Range(0.95f,1.05f),kind==0?50f:90f);
            var pkg=new ZPackage();pkg.Write(from);pkg.Write(kind);
            Net.Others(ShotRpc,pkg);
        }

        // A full charge: through up to n bodies, stopping at the first thing that isn't one (ground, rock, wall).
        private static void Pierce(Player p,Vector3 from,Vector3 dir,float range,HitData.DamageTypes dmg,Color color,int n)
        {
            RaycastHit[] hits=Physics.SphereCastAll(from,0.12f,dir,range,Hits.Mask,QueryTriggerInteraction.Ignore);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            Vector3 end=from+dir*range;
            var done=new HashSet<Character>();
            foreach(RaycastHit h in hits)
            {
                if(h.collider==null)continue;
                Character c=h.collider.GetComponentInParent<Character>();
                if(c==p)continue;
                if(c!=null)
                {
                    if(!done.Add(c))continue;
                    Hits.Deal(p,h.collider,h.point,dir,dmg,60f,Skills.SkillType.Crossbows,Buildings.Value);
                    if(done.Count>=n){end=h.point;break;}
                    continue;
                }
                if(h.distance<=0f&&h.point==Vector3.zero)continue;
                Hits.Deal(p,h.collider,h.point,dir,dmg,60f,Skills.SkillType.Crossbows,Buildings.Value);
                end=h.point;break;
            }
            Beam.Show(from,end,color,0.2f,0.45f);
        }

        // From the muzzle towards the spot under the crosshair (the camera sits behind and above, so its own line would miss up close).
        private static Vector3 AimAt(Player p,Vector3 from,float range)
        {
            Transform cam=GameCamera.instance!=null?GameCamera.instance.transform:null;
            if(cam==null)return p.GetLookDir();
            Vector3 target=cam.position+cam.forward*range;
            float nearest=range;
            foreach(RaycastHit h in Physics.RaycastAll(cam.position,cam.forward,range,Hits.Mask,QueryTriggerInteraction.Ignore))
            {
                if(h.distance>=nearest||h.collider.GetComponentInParent<Character>()==p)continue;
                nearest=h.distance;target=h.point;
            }
            Vector3 to=target-from;
            if(to.magnitude<1.5f||Vector3.Dot(to,cam.forward)<=0f)return cam.forward;
            return to.normalized;
        }

        internal static Transform Muzzle(Player p)
        {
            VisEquipment vis=p!=null?p.GetComponent<VisEquipment>():null;
            GameObject inst=vis!=null?RightInstance(vis):null;
            return inst!=null?Find(inst.transform,"wu_muzzle"):null;
        }
        internal static Transform Find(Transform t,string name)
        {
            if(t.name==name)return t;
            foreach(Transform c in t){Transform r=Find(c,name);if(r!=null)return r;}
            return null;
        }

        private static void Flash(Transform muzzle,Color color)
        {
            if(muzzle==null)return;
            if(_flash==null){_flash=Look.AddLight(muzzle,color,5f,3f);_flash.gameObject.name="WU_MuzzleFlash";}
            _flash.transform.SetParent(muzzle,false);_flash.transform.localPosition=Vector3.zero;
            _flash.color=color;_flash.enabled=true;
            _flashUntil=Time.time+0.06f;
        }

        // ---- every frame ---------------------------------------------------------------------------------------------

        internal override void Start()
        {
            Net.Register(ShotRpc,(sender,pkg)=>
            {
                Vector3 at=pkg.ReadVector3();int kind=pkg.ReadInt();
                Player me=Player.m_localPlayer;
                if(me==null||Vector3.Distance(me.transform.position,at)>(kind==0?60f:100f))return;
                Sound.Play(Clip(kind),at,kind==2?1f:0.75f,UnityEngine.Random.Range(0.95f,1.05f),kind==0?50f:90f);
            });
        }
        internal override void Tick()
        {
            Player p=Player.m_localPlayer;
            if(_flash!=null&&_flash.enabled&&Time.time>_flashUntil)_flash.enabled=false;
            _heat=Mathf.Max(0f,_heat-Cool.Value*Time.deltaTime*(_overheated?0.8f:1f));
            if(_overheated&&_heat<=0.3f){_overheated=false;if(p!=null)Sound.Play(Click(),p.transform.position+Vector3.up,0.5f);}
            ItemDef d=p!=null?Things.Held(p,out _):null;
            if(d!=Pistol&&d!=Rifle)d=null;
            if(d!=_held){_held=d;_chargeStart=-1f;Zoom(false);}
            if(d==null)return;
            bool held=TriggerHeld(p);
            if(d==Rifle)
            {
                if(_chargeStart>=0f&&(!held||_overheated))
                {
                    float charge=Charge;_chargeStart=-1f;
                    if(!_overheated&&CanShoot(p))Shoot(p,Rifle,charge);
                }
                Zoom(InputAllowed(p)&&(ZInput.GetButton("Block")||ZInput.GetButton("JoyBlock")));
            }
            if(!held)_released=true;
        }

        private static bool _zoomed;
        private static void Zoom(bool want)
        {
            if(want==_zoomed)return;
            _zoomed=want;
            GameCamera cam=GameCamera.instance;
            if(cam==null)return;
            if(want)cam.SetTempFOV(cam.m_fov/Mathf.Max(1f,RZoom.Value),0.2f);else cam.ResetTempFOV();
        }

        internal override void Gui()
        {
            if(_held==null)return;
            if(_heat>0.01f||_overheated)Hud.Bar(_overheated?"OVERHEAT":"Heat",_heat,Color.Lerp(new Color(1f,0.75f,0.2f),new Color(1f,0.15f,0.1f),_heat),_overheated);
            if(_held==Rifle&&_chargeStart>=0f)Hud.Bar(Charge>=0.99f?"PIERCE":"Charge",Charge,Look.Cyan,Charge>=0.99f);
        }

        internal override void Stop()
        {
            Zoom(false);
            if(_flash!=null)Object.Destroy(_flash.gameObject);
            _flash=null;_held=null;_chargeStart=-1f;
        }

        // ---- sounds -------------------------------------------------------------------------------------------------

        private static AudioClip Clip(int kind)=>kind==0?Pew():kind==1?Pshew():BigShot();
        // The pistol: a quick falling square-ish chirp.
        private static AudioClip Pew()=>_pew!=null?_pew:_pew=Sound.Make("pew",0.2f,t=>
        {
            float ph=Sound.SweepPhase(t,2400f,260f,0.2f);
            return (0.55f*Sound.Sine(ph)+0.25f*Sound.Square(ph*0.5f))*Sound.Env(t,0.004f,0.2f);
        });
        // The rifle: lower, longer, with a hiss of air.
        private static AudioClip Pshew()=>_pshew!=null?_pshew:_pshew=Sound.Make("pshew",0.38f,t=>
        {
            float ph=Sound.SweepPhase(t,1300f,110f,0.38f);
            return (0.5f*Sound.Sine(ph)+0.25f*Sound.Saw(ph*0.5f)+0.2f*Sound.Noise()*Mathf.Exp(-t*12f))*Sound.Env(t,0.006f,0.38f);
        });
        // A charged shot: a crack and a deep falling roar.
        private static AudioClip BigShot()=>_bigShot!=null?_bigShot:_bigShot=Sound.Make("bigshot",0.8f,t=>
        {
            float ph=Sound.SweepPhase(t,700f,45f,0.8f);
            float crack=Sound.Noise()*Mathf.Exp(-t*40f);
            return (0.45f*Sound.Saw(ph)+0.35f*Sound.Sine(ph*2f)+0.6f*crack+0.15f*Sound.Noise()*Mathf.Exp(-t*4f))*Sound.Env(t,0.003f,0.8f);
        });
        // Overheated: steam venting.
        private static AudioClip Hiss()=>_hiss!=null?_hiss:_hiss=Sound.Make("hiss",0.9f,t=>0.45f*Sound.Noise()*Sound.Env(t,0.02f,0.9f));
        private static AudioClip Click()=>_click!=null?_click:_click=Sound.Make("click",0.06f,t=>0.6f*Sound.Square(t*900f)*Sound.Env(t,0.001f,0.06f));

        // ---- the test command ----------------------------------------------------------------------------------------

        internal override string Command=>"laser";
        internal override string Help=>"info | fire [charge 0-1] | cool";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player p=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"info";
            ItemDef d=Things.Held(p,out _);
            switch(sub)
            {
                case "info":
                {
                    Transform m=Muzzle(p);
                    output(new JObject{["held"]=d?.Prefab,["heat"]=Math.Round(_heat,3),["overheated"]=_overheated,["cooldown"]=Math.Round(Mathf.Max(0,_next-Time.time),2),
                        ["charge"]=Math.Round(Charge,2),["zoomed"]=_zoomed,["muzzle"]=m!=null?Tools.V(m.position):null,["sinceShot"]=Math.Round(Time.time-_lastShot,1)});
                    return null;
                }
                case "fire":
                {
                    if(d!=Pistol&&d!=Rifle){error("wu laser fire: hold the laser pistol or rifle");return null;}
                    float charge=args.Length>3&&float.TryParse(args[3],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float c)?Mathf.Clamp01(c):0f;
                    Shoot(p,d,charge);
                    output(new JObject{["fired"]=d.Prefab,["charge"]=charge,["heat"]=Math.Round(_heat,3),["overheated"]=_overheated});
                    return null;
                }
                case "cool":_heat=0f;_overheated=false;output(new JObject{["heat"]=0});return null;
            }
            error("wu laser: info | fire [charge] | cool");
            return null;
        }
    }
}
