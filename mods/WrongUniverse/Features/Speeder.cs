using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // The Hover Speeder: a levitating bike built with the hammer (at a forge). It flies a hand's breadth under a metre over ground and
    // water alike. The flying is SpeederCraft; here are its settings, its body, its place in the build menu and its test command.
    // The piece is a copy of the Raft with everything but its networking, body, piece and wear taken off (Quad's VehicleKit.CloneBase,
    // FourWheelers): so it is saved, synced, damaged, repaired and taken down with the hammer like any piece.
    internal sealed class Speeder:Feature
    {
        internal override string Name=>"Speeder";
        internal const string PrefabName="WU_HoverSpeeder";
        internal static ConfigEntry<string> CCost,CStation,CPose;
        internal static ConfigEntry<float> CCruise,CBoost,CReverse,CAccel,CTurn,CGrip,CHeight,CHop,CRam;
        internal static bool Helped;
        private static GameObject _prefab;
        private static string _costApplied;
        private static readonly HashSet<PieceTable> Tables=new HashSet<PieceTable>();

        internal override void Bind(ConfigFile config)
        {
            const string s="7 - Hover Speeder";
            CCost=config.Bind(s,"Cost","Iron:20, SurtlingCore:4, FineWood:10, LeatherScraps:6","What a speeder takes to build: \"Item:amount, Item:amount\" (game prefab names). Needs a restart.");
            CStation=config.Bind(s,"Station","forge","The crafting station it must be built near (its prefab name). Needs a restart.");
            CCruise=config.Bind(s,"CruiseSpeed",20f,new ConfigDescription("Top speed holding W (metres per second; a running player does about 7).",new AcceptableValueRange<float>(5f,60f)));
            CBoost=config.Bind(s,"BoostSpeed",35f,new ConfigDescription("Top speed holding Shift+W (metres per second).",new AcceptableValueRange<float>(5f,80f)));
            CReverse=config.Bind(s,"ReverseSpeed",6f,new ConfigDescription("How fast it backs up holding S.",new AcceptableValueRange<float>(1f,20f)));
            CAccel=config.Bind(s,"Acceleration",14f,new ConfigDescription("How quickly it picks up speed (m/s each second). Braking is stronger.",new AcceptableValueRange<float>(2f,60f)));
            CTurn=config.Bind(s,"TurnRate",110f,new ConfigDescription("How fast it turns (degrees per second) when slow; about half that at full boost.",new AcceptableValueRange<float>(20f,300f)));
            CGrip=config.Bind(s,"Grip",5f,new ConfigDescription("How quickly a sideways slide dies away in a turn (lower drifts more).",new AcceptableValueRange<float>(0.5f,20f)));
            CHeight=config.Bind(s,"HoverHeight",0.9f,new ConfigDescription("How high the hull floats over ground or water (metres).",new AcceptableValueRange<float>(0.4f,3f)));
            CHop=config.Bind(s,"HopSpeed",9f,new ConfigDescription("How hard Jump throws it up while moving (metres per second upward; 9 clears a wall).",new AcceptableValueRange<float>(0f,25f)));
            CRam=config.Bind(s,"RamDamage",8f,new ConfigDescription("Blunt damage to an enemy you run into, for each m/s over 6 (so 20 m/s does 112, full boost 232). Players and tamed animals are never hurt; 0 turns it off.",new AcceptableValueRange<float>(0f,50f)));
            CPose=config.Bind(s,"RiderAnimation","","The game animation the rider sits in. Empty: astride, as on a lox (attach_chair if that is missing).");
        }

        // ---- the piece ---------------------------------------------------------------------------------------------------------

        private static PieceTable Hammer(ZNetScene scene)=>scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        internal override void Register(ZNetScene scene)
        {
            if(_prefab==null)
            {
                _prefab=Build(scene);
                if(_prefab==null)return;
            }
            ApplyCost(scene);
            Things.AddPrefab(scene,_prefab);   // (by name, over an older copy's: saved speeders load with this one)
            PieceTable table=Hammer(scene);
            if(table!=null)
            {
                table.m_pieces.RemoveAll(p=>p==null||p!=_prefab&&p.name==_prefab.name);
                if(!table.m_pieces.Contains(_prefab))table.m_pieces.Add(_prefab);
                Tables.Add(table);
            }
            int swapped=Reattach(scene);
            if(swapped>0)Plugin.Log($"{swapped} hover speeder(s) in the world now run this copy");
        }

        private static GameObject Build(ZNetScene scene)
        {
            GameObject raft=scene.GetPrefab("Raft");
            if(raft==null){Plugin.Warn("The game's Raft was not found, so there is no Hover Speeder");return null;}
            int layer=raft.transform.Find("ship/colliders/Cube")?.gameObject.layer??raft.layer;
            GameObject go=Object.Instantiate(raft,Things.Holder);
            go.name=PrefabName;
            foreach(Transform c in go.transform.Cast<Transform>().ToList())Object.DestroyImmediate(c.gameObject);
            foreach(Component comp in go.GetComponents<Component>().ToList())
                if(!(comp is Transform||comp is ZNetView||comp is ZSyncTransform||comp is Rigidbody||comp is Piece||comp is WearNTear))Object.DestroyImmediate(comp);
            WearNTear wear=go.GetComponent<WearNTear>();
            if(wear!=null)
            {
                wear.m_new=null;wear.m_worn=null;wear.m_broken=null;
                wear.m_supports=false;wear.m_noRoofWear=false;wear.m_noSupportWear=false;   // (no rain wear, no support needed: it flies)
                wear.m_health=800f;wear.m_ashDamageImmune=true;
            }
            ZSyncTransform sync=go.GetComponent<ZSyncTransform>();
            if(sync!=null){sync.m_syncPosition=true;sync.m_syncRotation=true;sync.m_syncBodyVelocity=true;}
            Rigidbody body=go.GetComponent<Rigidbody>();
            body.isKinematic=false;body.useGravity=true;body.mass=300f;
            Piece piece=go.GetComponent<Piece>();
            piece.m_name="Hover Speeder";
            piece.m_description="A speeder bike from a universe with roads. It floats a little under a metre over ground and water, goes 20 m/s on the throttle and 35 with Shift, hops on Jump, and runs down whatever is in the way. E to ride, E again to get off. A second rider fits behind.";
            piece.m_waterPiece=false;piece.m_groundOnly=false;piece.m_noInWater=false;piece.m_canBeRemoved=true;
            piece.m_category=Piece.PieceCategory.Misc;
            piece.m_craftingStation=scene.GetPrefab(CStation.Value.Trim())?.GetComponent<CraftingStation>();
            Transform root=go.transform;
            int handles=LayerMask.NameToLayer("piece_nonsolid");
            if(handles<0)handles=layer;

            // what you bump into and look at to ride; the pillion seat behind
            Box(root,"hull",new Vector3(0f,0.5f,0f),new Vector3(0.85f,0.6f,2.7f),layer);
            Box(root,"pillionseat",new Vector3(0f,0.92f,-1.05f),new Vector3(0.6f,0.3f,0.5f),handles).gameObject.AddComponent<SpeederPillion>();
            Point(root,"seat",new Vector3(0f,0.8f,-0.4f));
            Point(root,"passenger",new Vector3(0f,0.85f,-1.05f));
            try{Model(root);}
            catch(Exception e){Plugin.Warn("Could not build the speeder's model: "+e);}
            Sprite icon=Icon.Make(go,new Vector3(0.9f,0.45f,0.7f),null,null);
            if(icon!=null)piece.m_icon=icon;
            go.AddComponent<SpeederCraft>();
            return go;
        }
        private static BoxCollider Box(Transform parent,string name,Vector3 centre,Vector3 size,int layer)
        {
            var go=new GameObject(name){layer=layer};
            go.transform.SetParent(parent,false);
            var b=go.AddComponent<BoxCollider>();b.center=centre;b.size=size;
            return b;
        }
        private static void Point(Transform parent,string name,Vector3 at)
        {
            var go=new GameObject(name){layer=parent.gameObject.layer};
            go.transform.SetParent(parent,false);go.transform.localPosition=at;
        }

        // The body: a long white hull with a nose and a windscreen, twin engine pods with glowing exhausts, four glowing repulsor pads
        // underneath (their light on the ground), handlebars, a seat for two and a headlight. +z forward, the hull's underside at 0.
        private static void Model(Transform root)
        {
            var model=new GameObject("Model"){layer=root.gameObject.layer};
            model.transform.SetParent(root,false);
            var b=new Look.Builder(model.transform);
            Material white=Look.Paint(Look.White,0.3f,0.7f),metal=Look.Paint(Look.Gunmetal,0.6f,0.5f),black=Look.Paint(Look.Black,0.1f,0.3f),
                chrome=Look.Paint(Look.Chrome,0.9f,0.85f),hazard=Look.Paint(Look.Hazard,0.2f,0.5f),lens=Look.Paint(Look.Lens,0.1f,0.95f),glow=Look.Glow(Look.Cyan);
            b.Capsule("hull",new Vector3(0f,0.45f,-0.05f),0.55f,2.5f,white);
            b.Ball("nose",new Vector3(0f,0.43f,1.2f),new Vector3(0.42f,0.32f,0.9f),white);
            b.Box("keel",new Vector3(0f,0.24f,0f),new Vector3(0.36f,0.1f,2.1f),metal);
            b.Box("stripe",new Vector3(0f,0.72f,0.35f),new Vector3(0.3f,0.03f,0.55f),hazard);
            b.Box("windscreen",new Vector3(0f,0.9f,0.62f),new Vector3(0.42f,0.28f,0.02f),lens,new Vector3(-38f,0f,0f));
            b.Box("saddle",new Vector3(0f,0.75f,-0.65f),new Vector3(0.36f,0.1f,1.0f),black);
            b.Tube("bars",new Vector3(0f,0.95f,0.38f),0.045f,0.72f,chrome,'x');
            b.Tube("grip_l",new Vector3(-0.33f,0.95f,0.38f),0.06f,0.14f,black,'x');
            b.Tube("grip_r",new Vector3(0.33f,0.95f,0.38f),0.06f,0.14f,black,'x');
            b.Box("stem",new Vector3(0f,0.83f,0.45f),new Vector3(0.06f,0.2f,0.06f),chrome,new Vector3(-20f,0f,0f));
            foreach(float x in new[]{-0.42f,0.42f})
            {
                b.Tube("pod",new Vector3(x,0.38f,-0.35f),0.26f,1.3f,metal);
                b.Tube("podring",new Vector3(x,0.38f,0.25f),0.3f,0.08f,hazard);
                b.Ball("exhaust",new Vector3(x,0.38f,-1.02f),new Vector3(0.2f,0.2f,0.08f),glow);
                b.Box("vane",new Vector3(x*0.75f,0.35f,0.95f),new Vector3(0.04f,0.3f,0.6f),hazard,new Vector3(0f,0f,x>0?-12f:12f));
            }
            Look.Builder thrust=b.Group("thrust",new Vector3(0f,0.45f,-1.3f));
            thrust.Ball("flame",new Vector3(0f,0f,-0.15f),new Vector3(0.24f,0.24f,0.4f),glow);
            int i=0;
            foreach(float x in new[]{-0.32f,0.32f})foreach(float z in new[]{-0.9f,0.9f})
            {
                Look.Builder pad=b.Group("pad"+i++,new Vector3(x,0.08f,z));
                pad.Tube("rim",new Vector3(0f,0.03f,0f),0.36f,0.05f,metal,'y');
                pad.Tube("glow",Vector3.zero,0.3f,0.04f,glow,'y');
            }
            foreach(float z in new[]{-0.6f,0.6f})Look.AddLight(model.transform,Look.Cyan,4f,1.5f,new Vector3(0f,-0.1f,z)).name="padlight";
            Light head=Look.AddLight(model.transform,new Color(1f,0.97f,0.9f),30f,1.2f,new Vector3(0f,0.5f,1.62f));
            head.name="headlight";head.type=LightType.Spot;head.spotAngle=55f;
            b.Ball("headlamp",new Vector3(0f,0.5f,1.6f),new Vector3(0.12f,0.08f,0.04f),Look.Glow(new Color(1f,0.95f,0.8f)));
        }

        // The cost setting on the piece (again whenever it changed or the item list wasn't ready before). An unreadable one keeps the raft's.
        private static void ApplyCost(ZNetScene scene)
        {
            Piece piece=_prefab.GetComponent<Piece>();
            if(piece==null||_costApplied==CCost.Value&&piece.m_resources!=null&&piece.m_resources.Length>0)return;
            var needs=new List<Piece.Requirement>();
            foreach(string part in CCost.Value.Split(','))
            {
                string[] kv=part.Split(':');
                if(kv[0].Trim()=="")continue;
                ItemDrop res=scene.GetPrefab(kv[0].Trim())?.GetComponent<ItemDrop>();
                if(res==null){Plugin.Warn($"Hover Speeder: the game has no {kv[0].Trim()}; left out of its cost");continue;}
                needs.Add(new Piece.Requirement{m_resItem=res,m_amount=kv.Length>1&&int.TryParse(kv[1].Trim(),out int a)?a:1,m_recover=true});
            }
            if(needs.Count==0)return;
            piece.m_resources=needs.ToArray();
            _costApplied=CCost.Value;
        }

        // Speeders standing in the world get this copy's components in place of an older copy's (found by type name: each copy of the mod
        // is its own assembly). Quad's PieceReload.Reattach.
        private static readonly FieldInfo InstancesField=AccessTools.Field(typeof(ZNetScene),"m_instances");
        private static int Reattach(ZNetScene scene)
        {
            int hash=PrefabName.GetStableHashCode(),changed=0;
            Assembly mine=typeof(Speeder).Assembly;
            var instances=(Dictionary<ZDO,ZNetView>)InstancesField.GetValue(scene);
            foreach(ZNetView view in instances.Values.ToList())
            {
                if(view==null||view.GetZDO()==null||view.GetZDO().GetPrefab()!=hash)continue;
                bool any=false;
                foreach(MonoBehaviour old in view.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if(old==null||old.GetType().Assembly==mine)continue;
                    Type current=mine.GetType(old.GetType().FullName,false);
                    if(current==null||!typeof(MonoBehaviour).IsAssignableFrom(current))continue;
                    GameObject on=old.gameObject;
                    Object.DestroyImmediate(old);
                    on.AddComponent(current);
                    any=true;
                }
                if(any)changed++;
            }
            return changed;
        }

        internal override void Stop()
        {
            Player me=Player.m_localPlayer;
            if(me!=null&&me.GetDoodadController() is SpeederCraft)me.StopDoodadControl();
            // Out of the build menu, but still registered: an area loading before the next copy is up keeps its speeders.
            if(_prefab!=null)foreach(PieceTable t in Tables)if(t!=null)t.m_pieces.Remove(_prefab);
            Tables.Clear();
            _prefab=null;_costApplied=null;
        }

        // ---- sounds and the gauge ----------------------------------------------------------------------------------------------

        // A low two-second hum that loops cleanly (whole cycles of every tone), pitched up with speed by SpeederCraft.
        internal static AudioClip HumClip()=>Sound.Make("speeder hum",2f,t=>
            0.34f*Sound.Sine(55f*t)+0.12f*Sound.Saw(110f*t)+0.1f*Sound.Sine(165f*t)+0.07f*Sound.Sine(220f*t+0.3f*Sound.Sine(3f*t))+0.03f*Sound.Noise());
        internal static AudioClip HopClip()=>Sound.Make("speeder hop",0.5f,t=>
            Sound.Env(t,0.02f,0.5f)*(0.45f*Sound.Sine(Sound.SweepPhase(t,180f,520f,0.5f))+0.25f*Sound.Noise()));

        internal override void Gui()
        {
            if(Player.m_localPlayer?.GetDoodadController() is SpeederCraft s&&s!=null)
                Hud.Bar($"{s.Speed:0} m/s",s.Speed/Mathf.Max(1f,CBoost.Value),Look.Cyan);
        }

        // ---- the test command ------------------------------------------------------------------------------------------------

        internal override string Command=>"speeder";
        internal override string Help=>"place | mount | off | info | drive <throttle -1..1> <steer -1..1> <seconds> [run] | hop | clear";
        private static SpeederCraft Nearest(Player me,float range)=>SpeederCraft.All.Where(s=>s!=null&&Vector3.Distance(s.transform.position,me.transform.position)<range)
            .OrderBy(s=>Vector3.Distance(s.transform.position,me.transform.position)).FirstOrDefault();

        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"info";
            switch(sub)
            {
                case "place":
                {
                    if(_prefab==null){error("speeder: no prefab");break;}
                    Vector3 fwd=Vector3.ProjectOnPlane(me.transform.forward,Vector3.up).normalized;
                    Vector3 at=me.transform.position+fwd*4f+Vector3.up*1.5f;
                    Object.Instantiate(_prefab,at,Quaternion.LookRotation(fwd));   // (no creator: a test speeder anyone may take down)
                    output(new JObject{["placed"]=Tools.V(at)});
                    break;
                }
                case "mount":
                {
                    SpeederCraft s=Nearest(me,15f);
                    if(s==null){error("speeder: none within 15 m");break;}
                    s.Mount(me,true);
                    output(new JObject{["asked"]=Tools.V(s.transform.position)});
                    break;
                }
                case "off":
                    me.StopDoodadControl();
                    output(new JObject{["riding"]=me.GetDoodadController() is SpeederCraft});
                    break;
                case "hop":
                {
                    SpeederCraft s=Nearest(me,15f);
                    if(s==null){error("speeder: none within 15 m");break;}
                    s.Hop();
                    output(new JObject{["hop"]=true});
                    break;
                }
                case "drive":
                {
                    SpeederCraft s=Nearest(me,15f);
                    if(s==null){error("speeder: none within 15 m");break;}
                    float Arg(int i,float d)=>args.Length>i&&float.TryParse(args[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)?v:d;
                    if(!s.View.IsOwner())s.View.ClaimOwnership();
                    s.TestThrottle=Mathf.Clamp(Arg(3,1f),-1f,1f);s.TestSteer=Mathf.Clamp(Arg(4,0f),-1f,1f);
                    s.TestUntil=Time.time+Mathf.Clamp(Arg(5,3f),0f,30f);s.TestRun=args.Length>6&&args[6]=="run";
                    output(new JObject{["driving"]=true,["until"]=Math.Round(s.TestUntil-Time.time,1)});
                    break;
                }
                case "clear":
                {
                    int n=0;
                    foreach(SpeederCraft s in SpeederCraft.All.ToList())
                    {
                        if(s==null||s.GetUser()!=0L||Vector3.Distance(s.transform.position,me.transform.position)>30f)continue;
                        s.View.ClaimOwnership();
                        ZNetScene.instance.Destroy(s.gameObject);
                        n++;
                    }
                    output(new JObject{["removed"]=n});
                    break;
                }
                case "info":
                {
                    var list=new JArray();
                    foreach(SpeederCraft s in SpeederCraft.All)
                    {
                        if(s==null||Vector3.Distance(s.transform.position,me.transform.position)>100f)continue;
                        Player rider=s.Rider();
                        list.Add(new JObject{["at"]=Tools.V(s.transform.position),["yaw"]=Math.Round(s.transform.eulerAngles.y),["speed"]=Math.Round(s.Speed,1),
                            ["height"]=Math.Round(s.Height,2),["contact"]=s.Contact,["mine"]=s.View.IsOwner(),["rider"]=rider!=null?rider.GetPlayerName():null,
                            ["kinematic"]=s.Kinematic,["areaReady"]=s.AreaReady,["health"]=Math.Round(s.GetComponent<WearNTear>()?.GetHealthPercentage()??-1f,2)});
                    }
                    output(new JObject{["speeders"]=list,["riding"]=me.GetDoodadController() is SpeederCraft,["registered"]=_prefab!=null&&ZNetScene.instance?.GetPrefab(PrefabName)==_prefab,
                        ["inHammer"]=_prefab!=null&&Tables.Any(t=>t!=null&&t.m_pieces.Contains(_prefab))});
                    break;
                }
                default:error("speeder: "+Help);break;
            }
            return null;
        }
    }

    // Jump while riding a speeder that's moving (or with the throttle held) hops it, instead of the game's "get off". Stopped, Jump gets off.
    [HarmonyPatch(typeof(Player),nameof(Player.SetControls))]
    internal static class SpeederJump
    {
        private static void Prefix(Player __instance,Vector3 movedir,ref bool jump)
        {
            if(!jump||__instance!=Player.m_localPlayer||!(__instance.GetDoodadController() is SpeederCraft s)||s==null)return;
            if(s.Speed<1.5f&&movedir.z<0.1f)return;
            s.Hop();
            jump=false;
        }
    }
}
