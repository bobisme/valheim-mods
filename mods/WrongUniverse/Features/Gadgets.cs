using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // Things that have no business in the tenth world: the Gravity Gun (GadgetGravity.cs), the Displacer (blink to where you look), the
    // Stim Pack (full health and stamina, then 30 s of absurd regeneration and speed) and the Recall Beacon (home to your bed, ore and all).
    internal sealed class Gadgets:Feature
    {
        internal override string Name=>"Gadgets";
        internal static ConfigEntry<bool> MapJump;
        internal static ConfigEntry<float> GrabRange,PuntRange,FlingSpeed,ImpactDamage,MaxMass,BlinkRange,BlinkCooldown,StimSeconds,StimRegen,StimSpeed;

        // The two guns are copies of the Arbalest (two hands, the aiming pose); the barrel lies along +Z in the model, turned into the hand.
        private static readonly Vector3 GunTurn=new Vector3(0,90,0);
        internal static readonly ItemDef GravityGun=new ItemDef{Prefab="WU_GravityGun",Name="Gravity Gun",Base="CrossbowArbalest",Weight=6f,
            Cost="Iron:15, SurtlingCore:3, Crystal:2",Station="forge",Level=2,AttachEuler=GunTurn,Model=GravityModel,Stats=Gun,
            Description="A zero-point field manipulator, says the stencil. Hold attack on anything loose to lift it, a log, a cart, a troll who had it coming, and carry it in front of you. Let go to drop it. Block or the secondary attack flings it; with nothing held, the secondary attack punts whatever is in front of you. Whatever it hits, it hurts.",
            Primary=Gravity.Primary,Secondary=Gravity.Secondary};
        internal static readonly ItemDef Displacer=new ItemDef{Prefab="WU_Displacer",Name="Displacer",Base="CrossbowArbalest",Weight=3f,
            Cost="Iron:10, SurtlingCore:2, Crystal:3, GreydwarfEye:6",Station="forge",Level=2,AttachEuler=GunTurn,Model=DisplacerModel,Stats=Gun,
            Description="Point it, pull the trigger, and you are over there. Not quite instant, not quite safe, and not something a portal would approve of. Needs a few seconds to cool between jumps.",
            Primary=Blink,Secondary=OpenMapJump};
        internal static readonly ItemDef StimPack=new ItemDef{Prefab="WU_StimPack",Name="Stim Pack",Base="MeadHealthMinor",Weight=0.2f,Stack=10,
            Cost="Honey:1, Resin:1, Bloodbag:1",Station="piece_workbench",Level=2,Model=StimModel,Stats=Consumable,
            Description="One jab in the thigh: every wound closes, every breath comes back, and for half a minute you heal like a troll and run like a deer. The label lists side effects in a language nobody here reads.",
            Use=UseStim};
        internal static readonly ItemDef Beacon=new ItemDef{Prefab="WU_RecallBeacon",Name="Recall Beacon",Base="MeadHealthMinor",Weight=0.5f,Stack=5,
            Cost="SurtlingCore:1, GreydwarfEye:3, Copper:2",Station="forge",Level=1,Model=BeaconModel,Stats=Consumable,
            Description="Crack it open and something far above pulls you home to your bed, with everything you carry. Ore, too. The gods of portals are not consulted. Used up in the jump.",
            Use=UseBeacon};
        internal override IEnumerable<ItemDef> Items=>new[]{GravityGun,Displacer,StimPack,Beacon};

        internal override void Bind(ConfigFile c)
        {
            const string S="8 - Gadgets";
            GrabRange=c.Bind(S,"GravityGrabRange",15f,new ConfigDescription("How far the Gravity Gun reaches to lift something (metres).",new AcceptableValueRange<float>(3,40)));
            PuntRange=c.Bind(S,"GravityPuntRange",8f,new ConfigDescription("How far its punt reaches with nothing held (metres).",new AcceptableValueRange<float>(2,30)));
            FlingSpeed=c.Bind(S,"GravityFlingSpeed",32f,new ConfigDescription("How fast it flings (metres per second).",new AcceptableValueRange<float>(5,80)));
            ImpactDamage=c.Bind(S,"GravityImpactDamage",60f,new ConfigDescription("Blunt damage a flung thing does to what it slams into at full speed (a flung creature takes it too).",new AcceptableValueRange<float>(0,500)));
            MaxMass=c.Bind(S,"GravityMaxMass",1500f,new ConfigDescription("The heaviest loose thing it lifts (rigidbody mass). Creatures it lifts whatever they weigh, except bosses.",new AcceptableValueRange<float>(10,100000)));
            BlinkRange=c.Bind(S,"DisplacerRange",60f,new ConfigDescription("How far the Displacer jumps (metres).",new AcceptableValueRange<float>(5,200)));
            BlinkCooldown=c.Bind(S,"DisplacerCooldown",3f,new ConfigDescription("Seconds between jumps.",new AcceptableValueRange<float>(0,60)));
            MapJump=c.Bind(S,"DisplacerMapJump",false,"The Displacer's secondary attack opens the map: click anywhere on it to jump there (the world loads around you as a portal's does). The cooldown still applies.");
            StimSeconds=c.Bind(S,"StimSeconds",30f,new ConfigDescription("How long a Stim Pack's rush lasts (seconds). Needs a restart.",new AcceptableValueRange<float>(5,300)));
            StimRegen=c.Bind(S,"StimRegen",6f,new ConfigDescription("Health and stamina regeneration during the rush (times normal). Needs a restart.",new AcceptableValueRange<float>(1,50)));
            StimSpeed=c.Bind(S,"StimSpeed",0.4f,new ConfigDescription("Extra movement speed during the rush (0.4 = 40% faster). Needs a restart.",new AcceptableValueRange<float>(0,3)));
        }

        // ---- items --------------------------------------------------------------------------------------------------------

        // A gun the game never fires: no bolts, no draw, no reload, nothing to block with (Things sends the buttons here).
        private static void Gun(ItemDrop.ItemData.SharedData s)
        {
            s.m_ammoType="";s.m_damages=new HitData.DamageTypes();s.m_useDurability=false;
            s.m_blockPower=0f;s.m_deflectionForce=0f;s.m_attackForce=0f;
            foreach(Attack a in new[]{s.m_attack,s.m_secondaryAttack})
            {
                if(a==null)continue;
                a.m_bowDraw=false;a.m_requiresReload=false;a.m_attackStamina=0f;a.m_attackType=Attack.AttackType.None;
            }
        }
        // A consumable whose use is ours (the mead's own healing taken out).
        private static void Consumable(ItemDrop.ItemData.SharedData s)
        {
            s.m_consumeStatusEffect=null;s.m_food=0f;s.m_foodStamina=0f;s.m_foodEitr=0f;s.m_foodBurnTime=0f;s.m_foodRegen=0f;
        }

        // ---- the Displacer ------------------------------------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Character,Rigidbody> BodyOf=AccessTools.FieldRefAccess<Character,Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character,float> MaxAir=AccessTools.FieldRefAccess<Character,float>("m_maxAirAltitude");
        private static float _blinkReady;
        private static readonly Color Violet=new Color(0.75f,0.35f,1f);

        private static bool Blink(Player p,ItemDrop.ItemData item)
        {
            if(Time.time<_blinkReady)return true;
            if(p.IsDead()||p.IsTeleporting()||p.IsAttached()||p.InDodge()||p.IsStaggering())return true;
            _blinkReady=Time.time+0.3f;   // (the button stays down: don't try again every physics step)
            if(!Spot(p,BlinkRange.Value,out Vector3 to)){p.Message(MessageHud.MessageType.Center,"No room there");return true;}
            Jump(p,to);
            _blinkReady=Time.time+BlinkCooldown.Value;
            return true;
        }
        internal static void Jump(Player p,Vector3 to)
        {
            Vector3 from=p.transform.position;
            Rigidbody body=BodyOf(p);
            p.transform.position=to;
            if(body!=null){body.position=to;body.linearVelocity=Vector3.zero;}
            MaxAir(p)=to.y;   // (no fall damage for the jump itself)
            Beam.Show(from+Vector3.up*1f,to+Vector3.up*1f,Violet,0.5f,0.45f);
            Beam.Show(from,from+Vector3.up*2.6f,Violet,0.9f,0.6f);
            Beam.Show(to,to+Vector3.up*2.6f,Color.white,0.9f,0.6f);
            Sfx(SfxBlink,from);Sfx(SfxBlink,to);
        }

        // The map jump: the secondary attack opens the map, and the next click on it (instead of ticking a pin) jumps there.
        private static bool _mapJumping;
        private static float _lastSecondary;
        private static bool _mapLanding;   // (on the ground once the jump lands: then up onto whatever is built there)
        private static bool _mapTest;   // (`wu gadget map on`: as if the setting were on, without changing it)
        private static readonly Func<Minimap,Vector3,Vector3> MapToWorld=AccessTools.MethodDelegate<Func<Minimap,Vector3,Vector3>>(AccessTools.Method(typeof(Minimap),"ScreenToWorldPoint"));
        private static bool OpenMapJump(Player p,ItemDrop.ItemData item)
        {
            bool pressed=Time.time-_lastSecondary>0.2f;   // (called every physics step while held: only a new press counts)
            _lastSecondary=Time.time;
            if(!pressed||p!=Player.m_localPlayer||Minimap.instance==null)return true;
            if(!MapJump.Value&&!_mapTest){p.Message(MessageHud.MessageType.Center,"Map jumps are off (setting DisplacerMapJump)");return true;}
            if(Time.time<_blinkReady||p.IsTeleporting())return true;
            _mapJumping=true;
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            p.Message(MessageHud.MessageType.Center,"Click the map to jump there");
            return true;
        }
        internal static bool MapClick(Minimap map)
        {
            Player p=Player.m_localPlayer;
            if(!_mapJumping||p==null||Things.Held(p,out _)!=Displacer)return true;
            _mapJumping=false;
            Vector3 at=MapToWorld(map,ZInput.pointerPosition);
            map.SetMapMode(Minimap.MapMode.Small);
            MapLand(p,at);
            return false;
        }
        private static bool MapLand(Player p,Vector3 at)
        {
            if(at==Vector3.zero||p.IsDead()||p.IsTeleporting()||p.IsAttached()||Time.time<_blinkReady)return false;
            if(new Vector2(at.x,at.z).magnitude>WorldGenerator.worldSize+400f){p.Message(MessageHud.MessageType.Center,"Nothing out there to stand on");return false;}
            // (the ground there: the real heightmap if it is loaded, else the world's generated height; never under the sea)
            if(!Heightmap.GetHeight(at,out float ground))ground=WorldGenerator.instance!=null?WorldGenerator.instance.GetHeight(at.x,at.z):30f;
            float water=ZoneSystem.instance!=null?ZoneSystem.instance.m_waterLevel:30f;
            at.y=Mathf.Max(ground,water)+0.5f;
            Vector3 from=p.transform.position;
            if(!p.TeleportTo(at,p.transform.rotation,true))return false;
            _mapLanding=true;
            Beam.Show(from,from+Vector3.up*2.6f,Violet,0.9f,0.6f);
            Sfx(SfxBlink,from);
            _blinkReady=Time.time+BlinkCooldown.Value;
            return true;
        }

        // Where to land: on what the crosshair finds (backed off a wall, stood on a floor), or in the air at full range; always with room for a
        // body and never below the ground. Steps back towards the player until it fits.
        internal static bool Spot(Player p,float range,out Vector3 spot)
        {
            spot=Vector3.zero;
            Transform cam=GameCamera.instance!=null?GameCamera.instance.transform:null;
            if(cam==null)return false;
            int mask=Hits.Solid;
            Vector3 eye=p.GetEyePoint();
            float behind=Vector3.Distance(cam.position,eye);
            Vector3 dir=cam.forward;
            Vector3 target;
            if(Physics.Raycast(cam.position+dir*behind,dir,out RaycastHit hit,range,mask,QueryTriggerInteraction.Ignore))
                target=hit.normal.y>0.6f?hit.point+Vector3.up*0.05f:hit.point+hit.normal*0.5f;
            else target=cam.position+dir*(behind+range);
            if(Vector3.Distance(eye,target)>range)target=eye+(target-eye).normalized*range;
            Vector3 back=(eye-target).normalized;
            for(int i=0;i<16;i++)
            {
                Vector3 t=target;
                // (a floor within a couple of metres below: stand on it)
                if(Physics.Raycast(t+Vector3.up*1f,Vector3.down,out RaycastHit g,3.2f,mask,QueryTriggerInteraction.Ignore))t=g.point+Vector3.up*0.05f;
                if(ZoneSystem.instance!=null&&t.y<5000f)   // (not in a dungeon, which floats high above the world)
                {
                    float ground=ZoneSystem.instance.GetGroundHeight(t);
                    if(t.y<ground)t.y=ground+0.05f;
                }
                if(!Physics.CheckCapsule(t+Vector3.up*0.45f,t+Vector3.up*1.55f,0.38f,mask,QueryTriggerInteraction.Ignore)&&Vector3.Distance(t,p.transform.position)>1.5f)
                {spot=t;return true;}
                target+=back*0.8f;
                if(Vector3.Distance(target,eye)<2f)break;
            }
            return false;
        }

        // ---- the Stim Pack ------------------------------------------------------------------------------------------------

        internal const string StimName="WU_SE_Stim";
        internal static readonly int StimHash=StimName.GetStableHashCode();
        private static SE_Stats _stim;
        private static Light _stimGlow;

        private static bool UseStim(Player p,ItemDrop.ItemData item)
        {
            if(p.IsDead())return true;
            p.GetInventory().RemoveOneItem(item);
            Stim(p);
            return true;
        }
        internal static void Stim(Player p)
        {
            p.Heal(Mathf.Max(0f,p.GetMaxHealth()-p.GetHealth()),true);
            p.AddStamina(p.GetMaxStamina());
            if(_stim!=null)GadgetEffects.Apply(p,StimHash);
            Sfx(SfxStim,p.transform.position+Vector3.up);
        }
        private void MakeStim()
        {
            if(_stim!=null)return;
            Sprite icon=StimPack.Shared?.m_icons?.FirstOrDefault();
            _stim=GadgetEffects.Create<SE_Stats>(StimName,"Stimmed",icon);
            _stim.m_tooltip="Healing and breathing like a troll, running like a deer.";
            _stim.m_startMessage="Your heart is going very fast";_stim.m_startMessageType=MessageHud.MessageType.Center;
            _stim.m_ttl=StimSeconds.Value;
            _stim.m_healthRegenMultiplier=StimRegen.Value;_stim.m_staminaRegenMultiplier=StimRegen.Value;
            _stim.m_speedModifier=StimSpeed.Value;
            _stim.m_runStaminaDrainModifier=-0.5f;
            _stim.m_mods=new List<HitData.DamageModPair>();   // (its own list: never one shared with a game effect)
        }

        // ---- the Recall Beacon --------------------------------------------------------------------------------------------

        private static bool UseBeacon(Player p,ItemDrop.ItemData item)
        {
            PlayerProfile profile=Game.instance!=null?Game.instance.GetPlayerProfile():null;
            if(profile==null||p.IsDead())return true;
            if(!profile.HaveCustomSpawnPoint()){p.Message(MessageHud.MessageType.Center,"The beacon finds no bed of yours to pull you to");return true;}
            if(p.IsTeleporting()){return true;}
            Vector3 home=profile.GetCustomSpawnPoint()+Vector3.up*0.5f;
            Vector3 from=p.transform.position;
            if(!p.TeleportTo(home,p.transform.rotation,true)){p.Message(MessageHud.MessageType.Center,"The beacon is still gathering itself");return true;}
            p.GetInventory().RemoveOneItem(item);
            Beam.Show(from,from+Vector3.up*120f,new Color(0.4f,0.8f,1f),1.6f,1.2f);
            Sfx(SfxRecall,from);
            return true;
        }

        // ---- sounds (heard by everyone near) ------------------------------------------------------------------------------

        internal const int SfxGrab=1,SfxFling=2,SfxThud=3,SfxBlink=4,SfxStim=5,SfxRecall=6;
        private const string SfxRpc="WU_GadgetSfx";
        internal static void Sfx(int id,Vector3 at)
        {
            PlaySfx(id,at);
            var pkg=new ZPackage();pkg.Write(id);pkg.Write(at);
            Net.Others(SfxRpc,pkg);
        }
        private static void PlaySfx(int id,Vector3 at)
        {
            Player me=Player.m_localPlayer;
            if(me!=null&&Vector3.Distance(me.transform.position,at)>80f)return;
            switch(id)
            {
                case SfxGrab:Sound.Play(Clip("grab",0.3f,t=>Sound.Sine(Sound.SweepPhase(t,180,900,0.3f))*0.5f*Sound.Env(t,0.01f,0.3f)+Sound.Noise()*0.08f*Sound.Env(t,0.005f,0.1f)),at,0.7f,1f,30f);break;
                case SfxFling:Sound.Play(Clip("fling",0.45f,t=>(Sound.Saw(Sound.SweepPhase(t,700,60,0.45f))*0.5f+Sound.Noise()*0.5f*Sound.Env(t,0.002f,0.12f))*Sound.Env(t,0.003f,0.45f)),at,0.9f,UnityEngine.Random.Range(0.92f,1.08f),45f);break;
                case SfxThud:Sound.Play(Clip("thud",0.35f,t=>(Sound.Sine(Sound.SweepPhase(t,120,40,0.35f))*0.8f+Sound.Noise()*0.4f*Sound.Env(t,0.001f,0.06f))*Sound.Env(t,0.002f,0.35f)),at,1f,UnityEngine.Random.Range(0.85f,1.15f),40f);break;
                case SfxBlink:Sound.Play(Clip("blink",0.55f,t=>(Sound.Noise()*0.5f+Sound.Sine(Sound.SweepPhase(t,1400,200,0.55f))*0.35f)*Mathf.Sin(Mathf.Clamp01(t/0.55f)*Mathf.PI)),at,0.8f,1f,40f);break;
                case SfxStim:Sound.Play(Clip("stim",0.8f,t=>Sound.Noise()*0.4f*Sound.Env(t,0.01f,0.25f)+Sound.Sine(Sound.SweepPhase(t,300,1200,0.8f))*0.3f*Sound.Env(t,0.2f,0.8f)),at,0.7f,1f,20f);break;
                case SfxRecall:Sound.Play(Clip("recall",1.6f,t=>(Sound.Sine(Sound.SweepPhase(t,200,2400,1.6f))*0.35f+Sound.Sine(Sound.SweepPhase(t,300,3600,1.6f))*0.2f+Sound.Noise()*0.15f)*Sound.Env(t,0.3f,1.6f)),at,0.9f,1f,60f);break;
            }
        }
        private static AudioClip Clip(string name,float seconds,Func<float,float> wave)=>Sound.Make("gadget "+name,seconds,wave);
        // A one-second loop (whole cycles only, so it joins without a click): a low electric drone with a slow throb.
        internal static AudioClip Hum()=>Clip("hum",1f,t=>(Sound.Saw(55f*t)*0.35f+Sound.Sine(110f*t)*0.3f+Sound.Sine(165f*t)*0.15f)*(0.75f+0.25f*Sound.Sine(4f*t)));

        // ---- the feature ----------------------------------------------------------------------------------------------------

        internal override void Register(ZNetScene scene)
        {
            MakeStim();
            GadgetEffects.Register();
        }
        internal override void Start()
        {
            Net.Register(SfxRpc,(sender,pkg)=>PlaySfx(pkg.ReadInt(),pkg.ReadVector3()));
            GadgetEffects.Register();
        }
        internal override void Tick()
        {
            Gravity.Tick();
            Player p=Player.m_localPlayer;
            if(_mapJumping&&(Minimap.instance==null||Minimap.instance.m_mode!=Minimap.MapMode.Large))_mapJumping=false;   // (closed without a click)
            if(_mapLanding&&p!=null&&!p.IsTeleporting())
            {
                // The map knows the ground, not what stands on it: a jump onto a house lands on its floor or roof, not inside it under the floor.
                // (Only pieces and ground count: not the top of a tree.)
                _mapLanding=false;
                Vector3 at=p.transform.position;
                if(at.y<5000f&&Physics.Raycast(at+Vector3.up*500f,Vector3.down,out RaycastHit top,520f,LayerMask.GetMask("piece","terrain"),QueryTriggerInteraction.Ignore)&&top.point.y>at.y+0.3f)
                {
                    Vector3 to=top.point+Vector3.up*0.05f;
                    p.transform.position=to;
                    Rigidbody body=BodyOf(p);
                    if(body!=null){body.position=to;body.linearVelocity=Vector3.zero;}
                    MaxAir(p)=to.y;
                }
            }
            bool stimmed=p!=null&&GadgetEffects.Has(p,StimHash);
            if(stimmed&&_stimGlow==null)_stimGlow=Look.AddLight(p.transform,new Color(0.4f,1f,0.5f),4f,0.9f,Vector3.up*1.2f);
            if(_stimGlow!=null)
            {
                if(!stimmed||_stimGlow.transform.parent!=(p!=null?p.transform:null)){Object.Destroy(_stimGlow.gameObject);_stimGlow=null;}
                else _stimGlow.intensity=0.7f+0.3f*Mathf.Sin(Time.time*7f);
            }
        }
        internal override void FixedTick()=>Gravity.FixedTick();
        [HarmonyPatch(typeof(Minimap),nameof(Minimap.OnMapLeftClick))]
        private static class MapJumpClick{private static bool Prefix(Minimap __instance)=>MapClick(__instance);}
        internal override void Gui()
        {
            Player p=Player.m_localPlayer;
            if(p==null)return;
            ItemDef held=Things.Held(p,out _);
            if(held==Displacer&&Time.time<_blinkReady&&BlinkCooldown.Value>0f)Hud.Bar("DISPLACER",1f-(_blinkReady-Time.time)/BlinkCooldown.Value,Violet);
            float left=GadgetEffects.Remaining(p,StimHash);
            if(left>0f&&!float.IsInfinity(left))Hud.Bar("STIM",left/StimSeconds.Value,new Color(0.4f,1f,0.5f));
        }
        internal override void Stop()
        {
            Gravity.Stop();
            if(_stimGlow!=null)Object.Destroy(_stimGlow.gameObject);
            _stimGlow=null;
            GadgetEffects.Unload();
            _stim=null;
        }

        // ---- `wu gadget ...` ----------------------------------------------------------------------------------------------

        internal override string Command=>"gadget";
        internal override string Help=>"info | equip <prefab> | grab [seconds] | fling | drop | spot | blink | map [click | x z | on] | stim | recall [go]";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player p=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"info";
            switch(sub)
            {
                case "info":
                    output(new JObject{["held"]=Things.Held(p,out _)?.Prefab,["holding"]=Gravity.HeldName,["blinkReadyIn"]=Math.Round(Mathf.Max(0f,_blinkReady-Time.time),2),
                        ["stimLeft"]=Math.Round(GadgetEffects.Remaining(p,StimHash),1),["stimRegistered"]=ObjectDB.instance!=null&&ObjectDB.instance.GetStatusEffect(StimHash)!=null,
                        ["bed"]=Game.instance.GetPlayerProfile().HaveCustomSpawnPoint()?Tools.V(Game.instance.GetPlayerProfile().GetCustomSpawnPoint()):null,["player"]=Tools.V(p.transform.position)});
                    return null;
                case "equip":
                {
                    ItemDef d=Things.Find(args.Length>3?args[3]:"");
                    ItemDrop.ItemData item=d!=null?p.GetInventory().GetAllItems().FirstOrDefault(d.Is):null;
                    if(item==null){error("wu gadget equip: not in the inventory (wu give <prefab>)");return null;}
                    p.EquipItem(item);
                    output(new JObject{["equipped"]=Things.Held(p,out _)?.Prefab});
                    return null;
                }
                case "grab":return Hold(p,args.Length>3&&float.TryParse(args[3],out float s)?s:3f,output);
                case "fling":
                    if(!Gravity.Holding){error("wu gadget fling: nothing held (wu gadget grab first, it holds for its seconds)");return null;}
                    Gravity.Secondary(p,null);output(new JObject{["flung"]=true});return null;
                case "drop":Gravity.Drop();output(new JObject{["holding"]=Gravity.HeldName});return null;
                case "spot":
                    output(Spot(p,BlinkRange.Value,out Vector3 at)?new JObject{["spot"]=Tools.V(at),["distance"]=Math.Round(Vector3.Distance(at,p.transform.position),1)}:new JObject{["spot"]=null});
                    return null;
                case "map":   // the secondary attack (opens the map in jump mode), then a click where the mouse is, or a jump to x z
                {
                    _lastSecondary=0f;
                    _mapTest=args.Length>3&&args[3]!="off";
                    OpenMapJump(p,null);
                    _mapTest=false;
                    var o=new JObject{["mapOpen"]=Minimap.instance.m_mode==Minimap.MapMode.Large,["jumpMode"]=_mapJumping,["from"]=Tools.V(p.transform.position)};
                    if(args.Length>3&&args[3]=="click"){Minimap.instance.OnMapLeftClick();o["teleporting"]=p.IsTeleporting();}
                    else if(args.Length>4&&float.TryParse(args[3],out float x)&&float.TryParse(args[4],out float z))
                    {Minimap.instance.SetMapMode(Minimap.MapMode.Small);_mapJumping=false;o["jumped"]=MapLand(p,new Vector3(x,0f,z));o["teleporting"]=p.IsTeleporting();}
                    output(o);
                    return null;
                }
                case "blink":
                {
                    if(!Spot(p,BlinkRange.Value,out Vector3 to)){error("wu gadget blink: no room there");return null;}
                    Vector3 from=p.transform.position;Jump(p,to);
                    output(new JObject{["from"]=Tools.V(from),["to"]=Tools.V(to)});
                    return null;
                }
                case "stim":Stim(p);output(new JObject{["stimLeft"]=Math.Round(GadgetEffects.Remaining(p,StimHash),1),["health"]=Math.Round(p.GetHealth()),["stamina"]=Math.Round(p.GetStamina())});return null;
                case "recall":
                {
                    PlayerProfile profile=Game.instance.GetPlayerProfile();
                    if(!profile.HaveCustomSpawnPoint()){error("wu gadget recall: no bed");return null;}
                    bool go=args.Length>3&&args[3]=="go";
                    bool ok=go&&p.TeleportTo(profile.GetCustomSpawnPoint()+Vector3.up*0.5f,p.transform.rotation,true);
                    output(new JObject{["bed"]=Tools.V(profile.GetCustomSpawnPoint()),["teleporting"]=ok});
                    return null;
                }
            }
            error("wu gadget: "+Help);
            return null;
        }
        // Holds the attack button for the gravity gun (it must be in hand) for some seconds, as a player would.
        private static IEnumerator Hold(Player p,float seconds,Action<JObject> output)
        {
            float end=Time.time+seconds;
            string got=null;
            while(Time.time<end)
            {
                if(Things.Held(p,out ItemDrop.ItemData item)!=GravityGun)break;
                Gravity.Primary(p,item);
                got=got??Gravity.HeldName;
                yield return new WaitForFixedUpdate();
            }
            output(new JObject{["grabbed"]=got,["stillHolding"]=Gravity.HeldName,["needs"]=Things.Held(p,out _)!=GravityGun?"the Gravity Gun in hand (wu gadget equip WU_GravityGun)":null});
        }

        // ---- models ---------------------------------------------------------------------------------------------------------

        // The models are Blender's (Models/gravitygun.py, displacer.py, stimpack.py, beacon.py → .json.gz); the plain shapes below are
        // only a fallback for a build missing its model files.
        private static void GravityModel(Transform root)
        {
            if(Meshes.AddAll(root,"gravitygun").Count>0){Look.AddLight(root,new Color(1f,0.6f,0.15f),1.2f,0.6f,new Vector3(0,0.06f,0.45f));return;}
            var b=new Look.Builder(root);
            Material orange=Look.Paint(Look.Hazard,0.3f,0.5f),dark=Look.Paint(Look.Gunmetal,0.6f,0.5f),black=Look.Paint(Look.Black,0.2f,0.3f);
            b.Box("body",new Vector3(0,0.06f,0.05f),new Vector3(0.16f,0.14f,0.42f),orange);
            b.Box("grip",new Vector3(0,-0.08f,-0.02f),new Vector3(0.05f,0.16f,0.07f),black,new Vector3(-12,0,0));
            b.Box("stock",new Vector3(0,0.05f,-0.26f),new Vector3(0.09f,0.1f,0.2f),dark);
            b.Tube("barrel",new Vector3(0,0.06f,0.32f),0.13f,0.14f,dark);
            b.Ball("core",new Vector3(0,0.06f,0.41f),new Vector3(0.09f,0.09f,0.09f),Look.Glow(new Color(1f,0.6f,0.15f)));
            for(int i=0;i<3;i++)
            {
                float a=i*120f;
                var claw=b.Group("claw"+i,new Vector3(0,0.06f,0.38f),new Vector3(0,0,a));
                claw.Box("base",new Vector3(0,0.09f,0.02f),new Vector3(0.025f,0.025f,0.12f),dark,new Vector3(-18,0,0));
                claw.Box("tip",new Vector3(0,0.1f,0.11f),new Vector3(0.022f,0.022f,0.09f),black,new Vector3(22,0,0));
            }
            b.Box("stripe",new Vector3(0,0.135f,0.05f),new Vector3(0.12f,0.012f,0.3f),black);
            Look.AddLight(root,new Color(1f,0.6f,0.15f),1.2f,0.6f,new Vector3(0,0.06f,0.45f));
        }
        private static void DisplacerModel(Transform root)
        {
            if(Meshes.AddAll(root,"displacer").Count>0)return;
            var b=new Look.Builder(root);
            Material chrome=Look.Paint(Look.Chrome,0.9f,0.8f),dark=Look.Paint(Look.Black,0.3f,0.4f);
            Material coil=Look.Glow(new Color(0.75f,0.35f,1f));
            b.Box("body",new Vector3(0,0.05f,0.0f),new Vector3(0.07f,0.09f,0.3f),dark);
            b.Box("grip",new Vector3(0,-0.07f,-0.04f),new Vector3(0.045f,0.15f,0.06f),dark,new Vector3(-12,0,0));
            b.Tube("rod",new Vector3(0,0.05f,0.27f),0.035f,0.36f,chrome);
            for(int i=0;i<4;i++)b.Tube("coil"+i,new Vector3(0,0.05f,0.17f+i*0.07f),0.075f,0.022f,coil);
            b.Ball("tip",new Vector3(0,0.05f,0.47f),new Vector3(0.06f,0.06f,0.06f),Look.Glow(Color.white));
            b.Box("fin",new Vector3(0,0.11f,-0.05f),new Vector3(0.012f,0.05f,0.14f),chrome);
        }
        private static void StimModel(Transform root)
        {
            if(Meshes.AddAll(root,"stimpack").Count>0)return;
            var b=new Look.Builder(root);
            b.Tube("barrel",new Vector3(0,0,0),0.045f,0.16f,Look.Paint(Look.White,0.1f,0.6f));
            b.Tube("dose",new Vector3(0,0,0.01f),0.048f,0.07f,Look.Glow(new Color(1f,0.15f,0.2f)));
            b.Tube("plunger",new Vector3(0,0,-0.1f),0.03f,0.05f,Look.Paint(Look.Hazard,0.2f,0.5f));
            b.Tube("cap",new Vector3(0,0,0.1f),0.03f,0.04f,Look.Paint(Look.Gunmetal,0.6f,0.5f));
        }
        private static void BeaconModel(Transform root)
        {
            if(Meshes.AddAll(root,"beacon").Count>0)return;
            var b=new Look.Builder(root);
            b.Tube("puck",Vector3.zero,0.12f,0.05f,Look.Paint(Look.Gunmetal,0.7f,0.6f),'y');
            b.Tube("ring",new Vector3(0,0.026f,0),0.1f,0.008f,Look.Paint(Look.Hazard,0.3f,0.5f),'y');
            b.Ball("eye",new Vector3(0,0.03f,0),new Vector3(0.05f,0.025f,0.05f),Look.Glow(new Color(0.4f,0.8f,1f)));
        }
    }
}
