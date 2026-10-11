using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Wayfinder
{
    // The ships themselves: a name each (a kenning until someone names her at the helm, Shift+E), a pin on your map for each
    // ship you have sailed in, a lantern on the mast by night, a gilded vane at the masthead, a lookout's perch up there, and
    // the helmsman's course shown to everyone aboard.
    internal static class Ships
    {
        internal const string NameKey="bob_wayfinder_ship_name",HelmKey="bob_wayfinder_helm_course",HelmByKey="bob_wayfinder_helm_by";
        internal const string RenameRpc="bob_wayfinder_rename",HelmRpc="bob_wayfinder_helm";
        private const string FleetPrefix="bob_wayfinder_ships:";

        internal static string NameOf(Ship ship)
        {
            ZNetView view=ship!=null?ship.GetComponent<ZNetView>():null;
            if(view==null||!view.IsValid())return "";
            string name=view.GetZDO().GetString(NameKey);
            return string.IsNullOrEmpty(name)?Policy.ShipName(view.GetZDO().m_uid.ID+view.GetZDO().m_uid.UserID):name;
        }
        internal static string NameOf(ZDO zdo)
        {
            string name=zdo.GetString(NameKey);
            return string.IsNullOrEmpty(name)?Policy.ShipName(zdo.m_uid.ID+zdo.m_uid.UserID):name;
        }

        // ---- the helmsman's course
        private static string _helmText,_pushed;
        private static readonly List<(double x,double z,string name)> Helm=new List<(double,double,string)>();
        private static int _helmNext;
        internal static string HelmBy="";
        internal static List<(double x,double z,string name)> HelmCourse=>Helm.Count>0?Helm:null;
        internal static Vector3? HelmNext=>Helm.Count>0&&_helmNext<Helm.Count?new Vector3((float)Helm[_helmNext].x,0,(float)Helm[_helmNext].z):(Vector3?)null;
        internal static string HelmLabel=>Helm.Count>0&&_helmNext<Helm.Count?(string.IsNullOrEmpty(Helm[_helmNext].name)?"waypoint "+(_helmNext+1):Helm[_helmNext].name)+(HelmBy!=""?" <size=80%>("+HelmBy+")</size>":""):"";

        private static float _next,_resend;private static Ship _steered;
        internal static void Tick()
        {
            Player me=Player.m_localPlayer;
            if(me==null){Helm.Clear();_helmText=null;return;}
            Ship ship=Ship.GetLocalShip();
            ZNetView view=ship!=null?ship.GetComponent<ZNetView>():null;
            bool valid=view!=null&&view.IsValid();
            if(valid&&me.GetControlledShip()==ship)
            {
                // steering: our course goes on the ship for the others. Only the legs ahead: a moving ship is resent to everyone
                // with each step, so the whole course would ride along kilobytes at a time. Asked again every 2 s until it lands.
                string text=Policy.Ahead(Course.Points,Course.Next);
                if(text!=_pushed||view.GetZDO().GetString(HelmKey)!=text&&Time.time>=_resend)
                {_pushed=text;_resend=Time.time+2;view.InvokeRPC(HelmRpc,text,me.GetPlayerName());}
                _steered=ship;
                Helm.Clear();_helmText=null;
            }
            else
            {
                _pushed=null;
                // Off the helm: the course comes off the ship (if nobody else took the helm), not left riding on it for good.
                if(_steered!=null)
                {
                    ZNetView left=_steered.GetComponent<ZNetView>();
                    if(left!=null&&left.IsValid()&&(_steered.m_shipControlls==null||!_steered.m_shipControlls.HaveValidUser())&&left.GetZDO().GetString(HelmKey)!="")
                        left.InvokeRPC(HelmRpc,"","");
                    _steered=null;
                }
                string text=valid&&ship.m_shipControlls!=null&&ship.m_shipControlls.HaveValidUser()&&ship.m_shipControlls.GetUser()!=me.GetPlayerID()?view.GetZDO().GetString(HelmKey):"";
                if(text!=_helmText)
                {
                    _helmText=text;
                    Policy.Load(text,Helm,out _helmNext);
                    HelmBy=valid?view.GetZDO().GetString(HelmByKey):"";
                }
            }
            if(Time.time>=_next){_next=Time.time+1f;Fleet(me,ship);}
            Masthead.Tick(me);
        }

        // ---- the fleet: ships you have sailed in, pinned on your map
        private sealed class Known{internal ZDOID Id;internal Vector3 At;internal string Name;internal Minimap.PinData Pin;}
        private static readonly List<Known> Fleet_=new List<Known>();
        private static string _fleetFor;
        internal static int FleetCount=>Fleet_.Count;
        private static string FleetKey()=>ZNet.instance!=null?FleetPrefix+ZNet.instance.GetWorldUID():null;
        private static void Fleet(Player me,Ship aboard)
        {
            string key=FleetKey();
            if(key==null)return;
            if(_fleetFor!=key+me.GetPlayerID()){ClearPins();Fleet_.Clear();_fleetFor=key+me.GetPlayerID();LoadFleet(me,key);}
            bool changed=false;
            ZNetView view=aboard!=null?aboard.GetComponent<ZNetView>():null;
            if(view!=null&&view.IsValid()&&!Fleet_.Any(k=>k.Id==view.GetZDO().m_uid))
            {
                Fleet_.Insert(0,new Known{Id=view.GetZDO().m_uid,At=aboard.transform.position,Name=NameOf(aboard)});
                while(Fleet_.Count>Plugin.FleetSize.Value){RemovePin(Fleet_[Fleet_.Count-1]);Fleet_.RemoveAt(Fleet_.Count-1);}
                changed=true;
            }
            Minimap map=Minimap.instance;
            Sprite icon=map!=null&&map.m_smallShipMarker!=null?map.m_smallShipMarker.GetComponent<UnityEngine.UI.Image>()?.sprite:null;
            for(int i=Fleet_.Count-1;i>=0;i--)
            {
                Known k=Fleet_[i];
                ZDO zdo=ZDOMan.instance.GetZDO(k.Id);
                if(zdo!=null)
                {
                    Vector3 at=zdo.GetPosition();string name=NameOf(zdo);
                    if((at-k.At).sqrMagnitude>4||name!=k.Name){k.At=at;k.Name=name;changed=true;}
                }
                else if(ZoneSystem.instance.IsZoneLoaded(k.At)&&(me.transform.position-k.At).sqrMagnitude<40*40)
                {
                    // where she lay, she is not: wrecked, or broken up
                    RemovePin(k);Fleet_.RemoveAt(i);changed=true;
                    me.Message(MessageHud.MessageType.TopLeft,$"{k.Name} is gone from where you left her.");
                    continue;
                }
                if(map==null||!Plugin.ShipPins.Value){RemovePin(k);continue;}
                bool aboardHer=view!=null&&view.IsValid()&&view.GetZDO().m_uid==k.Id;
                if(k.Pin!=null&&!MapPinList(map).Contains(k.Pin))
                {
                    // the pin was taken off the map by hand: forget her
                    k.Pin=null;Fleet_.RemoveAt(i);changed=true;
                    me.Message(MessageHud.MessageType.TopLeft,$"You put {k.Name} out of mind.");
                    continue;
                }
                if(aboardHer){RemovePin(k);continue;}
                if(k.Pin==null||(k.Pin.m_pos-k.At).sqrMagnitude>1||k.Pin.m_name!=k.Name)
                {
                    RemovePin(k);
                    k.Pin=map.AddPin(k.At,Minimap.PinType.Icon3,k.Name,false,false);
                    if(icon!=null)k.Pin.m_icon=icon;
                }
            }
            if(changed)SaveFleet(me,key);
        }
        private static readonly AccessTools.FieldRef<Minimap,List<Minimap.PinData>> MapPinList=AccessTools.FieldRefAccess<Minimap,List<Minimap.PinData>>("m_pins");
        private static void RemovePin(Known k){if(k.Pin!=null&&Minimap.instance!=null)Minimap.instance.RemovePin(k.Pin);k.Pin=null;}
        private static void ClearPins(){foreach(Known k in Fleet_)RemovePin(k);}
        private static void LoadFleet(Player me,string key)
        {
            if(!me.m_customData.TryGetValue(key,out string text)||string.IsNullOrEmpty(text))return;
            foreach(string s in text.Split(';'))
            {
                string[] f=s.Split('|');
                if(f.Length<5||!long.TryParse(f[0],out long user)||!uint.TryParse(f[1],out uint id))continue;
                if(!float.TryParse(f[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float x)||!float.TryParse(f[3],NumberStyles.Float,CultureInfo.InvariantCulture,out float z))continue;
                Fleet_.Add(new Known{Id=new ZDOID(user,id),At=new Vector3(x,ZoneSystem.instance!=null?ZoneSystem.instance.m_waterLevel:30,z),Name=f[4]});
            }
        }
        private static void SaveFleet(Player me,string key)
        {
            me.m_customData[key]=string.Join(";",Fleet_.Select(k=>$"{k.Id.UserID}|{k.Id.ID}|{k.At.x.ToString("0.0",CultureInfo.InvariantCulture)}|{k.At.z.ToString("0.0",CultureInfo.InvariantCulture)}|{Policy.Clean(k.Name)}"));
        }

        internal static void Stop(){ClearPins();Fleet_.Clear();_fleetFor=null;Helm.Clear();_helmText=null;Fittings.Stop();Masthead.Stop();}
    }

    // Each ship, when it wakes: its rename and helm-course calls, and its fittings.
    [HarmonyPatch(typeof(Ship),"Awake")]
    internal static class ShipAwake
    {
        private static void Postfix(Ship __instance)=>Setup(__instance);
        internal static void Setup(Ship ship)
        {
            Fittings.Add(ship);
            ZNetView view=ship.GetComponent<ZNetView>();
            if(view==null||view.GetZDO()==null)return;
            view.Unregister(Ships.RenameRpc);view.Unregister(Ships.HelmRpc); // (a reloaded copy registers again)
            view.Register<string>(Ships.RenameRpc,(sender,name)=>{if(view.IsOwner())view.GetZDO().Set(Ships.NameKey,Policy.Clean(name));});
            view.Register<string,string>(Ships.HelmRpc,(sender,text,by)=>{if(view.IsOwner()){view.GetZDO().Set(Ships.HelmKey,text??"");view.GetZDO().Set(Ships.HelmByKey,Policy.Clean(by));}});
        }
    }

    // At the helm: her name over it, and Shift+E to name her.
    [HarmonyPatch(typeof(ShipControlls),nameof(ShipControlls.GetHoverText))]
    internal static class HelmHover
    {
        private static void Postfix(ShipControlls __instance,ref string __result)
        {
            if(__instance.m_ship==null||!Plugin.ShipNames.Value)return;
            __result=$"<color=#ffd27a>{Ships.NameOf(__instance.m_ship)}</color>\n"+__result+"\n[<color=yellow><b>Shift + E</b></color>] Name her";
        }
    }
    [HarmonyPatch(typeof(ShipControlls),nameof(ShipControlls.Interact))]
    internal static class HelmRename
    {
        private static bool Prefix(ShipControlls __instance,Humanoid character,bool repeat,bool alt,ref bool __result)
        {
            if(!alt||repeat||!Plugin.ShipNames.Value||__instance.m_ship==null||character!=Player.m_localPlayer)return true;
            if(TextInput.instance!=null)TextInput.instance.RequestText(new ShipNamer(__instance.m_ship),"Name your ship",24);
            __result=true;
            return false;
        }
    }
    internal sealed class ShipNamer:TextReceiver
    {
        private readonly Ship _ship;
        internal ShipNamer(Ship ship){_ship=ship;}
        public string GetText()=>_ship!=null?Ships.NameOf(_ship):"";
        public void SetText(string text)
        {
            ZNetView view=_ship!=null?_ship.GetComponent<ZNetView>():null;
            if(view==null||!view.IsValid())return;
            view.InvokeRPC(Ships.RenameRpc,text??"");
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center,string.IsNullOrWhiteSpace(text)?"She takes back her old name.":$"She is {Policy.Clean(text)} now.");
        }
    }

    // A lantern hung on the mast, lit from dusk till dawn; and a gilded vane at the masthead that swings to the wind.
    internal static class Fittings
    {
        private sealed class Fit{internal Ship Ship;internal Transform Lantern,Vane,Root;internal Light Light;internal Renderer Glow;internal float Seed;}
        private static readonly List<Fit> Fits=new List<Fit>();
        internal const string RootName="WayfinderFittings";
        private static Looks _looks;
        private static readonly List<Object> Owned=new List<Object>();
        private static Mesh _quadrant;

        internal static void Add(Ship ship)
        {
            if(ship==null||ship.transform.Find(RootName)!=null)return;
            Transform mast=ship.m_mastObject!=null?ship.m_mastObject.transform:null;
            if(mast==null||ObjectDB.instance==null)return;
            if(_looks==null)_looks=new Looks(ObjectDB.instance,Owned);
            if(!_looks.Ready)return;
            Bounds b=Items.Bounds(mast);
            Vector3 top=ship.transform.InverseTransformPoint(new Vector3(b.center.x,b.max.y,b.center.z));
            Vector3 foot=ship.transform.InverseTransformPoint(new Vector3(b.center.x,b.min.y,b.center.z));
            if(top.y-foot.y<2.5f||mast.GetComponentInChildren<Renderer>()==null)return; // (other mods' boats with no real mast)
            var root=new GameObject(RootName).transform;
            root.SetParent(ship.transform,false);
            var fit=new Fit{Ship=ship,Root=root,Seed=Random.value*10};
            // the lantern: a third of the way up the mast, on its fore side
            float h=Mathf.Lerp(foot.y,top.y,0.3f);
            var lantern=new GameObject("Lantern").transform;
            lantern.SetParent(root,false);
            lantern.localPosition=new Vector3(top.x,h,top.z+0.28f);
            lantern.localScale=Vector3.one*1.6f;
            Lantern(lantern,_looks,out fit.Glow);
            var light=new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(lantern,false);light.transform.localPosition=new Vector3(0,-0.12f,0);
            light.type=LightType.Point;light.range=11;light.intensity=0;light.color=new Color(1f,0.72f,0.42f);light.shadows=LightShadows.Soft;light.shadowStrength=0.6f;
            fit.Light=light;fit.Lantern=lantern;
            // the vane at the masthead
            var vane=new GameObject("Vane").transform;
            vane.SetParent(root,false);
            vane.localPosition=top+new Vector3(0,0.05f,0);
            Vane(vane,_looks,0.5f);
            fit.Vane=vane.Find("Turn");
            Fits.Add(fit);
        }
        // A bronze lantern with a horn window glowing warm.
        internal static void Lantern(Transform at,Looks l,out Renderer glow)
        {
            Models.Rod(at,"Hook",l.Get("Bronze"),new Vector3(0,0.1f,-0.18f),new Vector3(0,0.1f,0),0.012f);
            Models.Rod(at,"Bail",l.Get("Bronze"),new Vector3(0,0.1f,0),new Vector3(0,0.03f,0),0.008f);
            Models.Part(at,"Cap",PrimitiveType.Cylinder,l.Get("Bronze"),new Vector3(0,0.02f,0),Vector3.zero,new Vector3(0.12f,0.02f,0.12f));
            Models.Part(at,"Roof",PrimitiveType.Sphere,l.Get("Bronze"),new Vector3(0,0.04f,0),Vector3.zero,new Vector3(0.1f,0.05f,0.1f));
            glow=Models.Part(at,"Horn",PrimitiveType.Cylinder,l.Get("Glow"),new Vector3(0,-0.07f,0),Vector3.zero,new Vector3(0.1f,0.08f,0.1f),false).GetComponent<Renderer>();
            for(int i=0;i<4;i++){float a=i*90+45;Vector3 p=Quaternion.Euler(0,a,0)*new Vector3(0,0,0.055f);Models.Rod(at,"Rib",l.Get("Bronze"),p+new Vector3(0,0.0f,0),p+new Vector3(0,-0.15f,0),0.007f);}
            Models.Part(at,"Base",PrimitiveType.Cylinder,l.Get("Bronze"),new Vector3(0,-0.16f,0),Vector3.zero,new Vector3(0.12f,0.015f,0.12f));
        }
        // A gilded quadrant vane on a spindle, with a little beast on top and tassel-holes along its curve; the plate trails downwind.
        internal static void Vane(Transform at,Looks l,float s)
        {
            if(_quadrant==null){_quadrant=Models.Quadrant(0.5f,0.012f);Owned.Add(_quadrant);}
            Models.Rod(at,"Spindle",l.Get("Bronze"),Vector3.zero,new Vector3(0,0.62f*s/0.5f*0.5f+0.1f,0),0.01f*s/0.5f);
            var turn=new GameObject("Turn").transform;
            turn.SetParent(at,false);
            turn.localPosition=new Vector3(0,0.55f*s,0);
            Models.Rod(turn,"Sleeve",l.Get("Gold"),new Vector3(0,-0.04f,0)*s/0.5f,new Vector3(0,0.06f,0)*s/0.5f,0.016f*s/0.5f);
            // the plate in the turn's YZ plane, reaching back along -z (downwind)
            Models.Mesh(turn,"Plate",_quadrant,l.Get("Gold"),new Vector3(0,0.05f,0)*s/0.5f,new Vector3(0,90,0),Vector3.one*s/0.5f);
            for(int i=1;i<6;i++)
            {
                float a=Mathf.PI/2*i/6;
                Vector3 p=new Vector3(0,0.05f-Mathf.Sin(a)*0.47f,-Mathf.Cos(a)*0.47f)*s/0.5f;
                Models.Rod(turn,"Tassel",l.Get("Gold"),p,p+new Vector3(0,-0.07f,0)*s/0.5f,0.005f*s/0.5f);
            }
            // the beast: a curled dragon-ish shape of a few gilded parts, looking upwind
            Models.Part(turn,"Beast",PrimitiveType.Capsule,l.Get("Gold"),new Vector3(0,0.12f,-0.15f)*s/0.5f,new Vector3(90,0,0),new Vector3(0.05f,0.12f,0.05f)*s/0.5f);
            Models.Part(turn,"BeastHead",PrimitiveType.Sphere,l.Get("Gold"),new Vector3(0,0.17f,-0.02f)*s/0.5f,Vector3.zero,new Vector3(0.05f,0.05f,0.08f)*s/0.5f);
            Models.Rod(turn,"BeastNeck",l.Get("Gold"),new Vector3(0,0.12f,-0.06f)*s/0.5f,new Vector3(0,0.17f,-0.03f)*s/0.5f,0.018f*s/0.5f);
            Models.Rod(turn,"BeastTail",l.Get("Gold"),new Vector3(0,0.12f,-0.26f)*s/0.5f,new Vector3(0,0.21f,-0.33f)*s/0.5f,0.012f*s/0.5f);
            Models.Rod(turn,"BeastLeg",l.Get("Gold"),new Vector3(0,0.1f,-0.1f)*s/0.5f,new Vector3(0,0.05f,-0.1f)*s/0.5f,0.008f*s/0.5f);
            Models.Rod(turn,"BeastLeg",l.Get("Gold"),new Vector3(0,0.1f,-0.2f)*s/0.5f,new Vector3(0,0.05f,-0.2f)*s/0.5f,0.008f*s/0.5f);
        }

        internal static void Tick()
        {
            if(EnvMan.instance==null)return;
            Vector3 wind=EnvMan.instance.GetWindDir();
            float strength=EnvMan.instance.GetWindIntensity();
            bool night=!EnvMan.IsDay()||EnvMan.instance.GetCurrentEnvironment()?.m_alwaysDark==true;
            Vector3 cam=GameCamera.instance!=null?GameCamera.instance.transform.position:Vector3.zero;
            for(int i=Fits.Count-1;i>=0;i--)
            {
                Fit f=Fits[i];
                if(f.Ship==null||f.Root==null){Fits.RemoveAt(i);continue;}
                bool near=(f.Ship.transform.position-cam).sqrMagnitude<200*200;
                bool lit=night&&Plugin.ShipLanterns.Value;
                if(f.Lantern.gameObject.activeSelf!=Plugin.ShipLanterns.Value)f.Lantern.gameObject.SetActive(Plugin.ShipLanterns.Value);
                if(f.Vane!=null&&f.Vane.parent.gameObject.activeSelf!=Plugin.ShipVanes.Value)f.Vane.parent.gameObject.SetActive(Plugin.ShipVanes.Value);
                if(!near)continue;
                float flicker=0.85f+0.15f*Mathf.PerlinNoise(Time.time*3+f.Seed,f.Seed);
                f.Light.intensity=Mathf.MoveTowards(f.Light.intensity,lit?1.6f*flicker:0,Time.deltaTime*2);
                f.Light.enabled=f.Light.intensity>0.01f;
                if(f.Glow!=null)f.Glow.material.SetColor("_EmissionColor",new Color(1f,0.6f,0.25f)*(lit?2.2f*flicker:0.05f));
                // the lantern swings a little with the ship's roll
                Vector3 up=f.Ship.transform.InverseTransformDirection(Vector3.up);
                f.Lantern.localRotation=Quaternion.Slerp(f.Lantern.localRotation,Quaternion.FromToRotation(Vector3.up,up),Time.deltaTime*3);
                if(f.Vane!=null)
                {
                    // the plate streams downwind: its -z along the wind
                    Vector3 local=f.Vane.parent.InverseTransformDirection(wind);local.y=0;
                    if(local.sqrMagnitude>1e-4)
                    {
                        Quaternion want=Quaternion.LookRotation(-local.normalized,Vector3.up)*Quaternion.Euler(0,Mathf.Sin(Time.time*(1.5f+strength*4)+f.Seed)*6*strength,0);
                        f.Vane.localRotation=Quaternion.Slerp(f.Vane.localRotation,want,Time.deltaTime*(1+strength*3));
                    }
                }
            }
        }
        internal static void Refresh()
        {
            foreach(Ship ship in Object.FindObjectsByType<Ship>(FindObjectsSortMode.None))
            {
                Transform old=ship.transform.Find(RootName);
                if(old!=null)Object.DestroyImmediate(old.gameObject);
                ShipAwake.Setup(ship);
            }
        }
        internal static void Stop()
        {
            foreach(Fit f in Fits)if(f.Root!=null)Object.Destroy(f.Root.gameObject);
            Fits.Clear();
            _looks=null;_quadrant=null;
        }
    }

    // Shift+E at the mast climbs to the masthead: from up there the lookout sees farther, and the map fills in round you
    // three times as wide while you are aloft.
    internal static class Masthead
    {
        internal static bool Aloft;
        private static Transform _perch;
        internal static void Climb(Player me,Ship ship,Chair mastChair)
        {
            if(ship==null||ship.m_mastObject==null)return;
            if(Items.Bounds(ship.m_mastObject.transform).size.y<2.5f){me.Message(MessageHud.MessageType.Center,"There is no masthead to climb.");return;}
            Bounds b=Items.Bounds(ship.m_mastObject.transform);
            Vector3 top=ship.transform.InverseTransformPoint(new Vector3(b.center.x,b.max.y,b.center.z));
            if(_perch!=null)Object.Destroy(_perch.gameObject);
            _perch=new GameObject("WayfinderMasthead").transform;
            _perch.SetParent(ship.transform,false);
            _perch.localPosition=top+new Vector3(0,-1.35f,0.18f);
            Vector3 deck=ship.transform.InverseTransformPoint(mastChair.m_attachPoint.position);
            me.AttachStart(_perch,null,false,false,true,"attach_chair",deck-_perch.localPosition+new Vector3(0,0.4f,0));
            me.Message(MessageHud.MessageType.Center,"You climb to the masthead.");
        }
        internal static void Tick(Player me)
        {
            Aloft=_perch!=null&&me.IsAttached()&&me.GetAttachPoint()==_perch;
            if(_perch!=null&&!me.IsAttached()){Object.Destroy(_perch.gameObject);_perch=null;}
        }
        internal static void Stop(){if(_perch!=null){Player.m_localPlayer?.AttachStop();Object.Destroy(_perch.gameObject);}_perch=null;Aloft=false;}
    }
    [HarmonyPatch(typeof(Chair),nameof(Chair.GetHoverText))]
    internal static class MastHover
    {
        private static void Postfix(Chair __instance,ref string __result)
        {
            if(!Plugin.MastheadClimb.Value||!__instance.m_inShip||__instance.name!="mast")return;
            __result+="\n[<color=yellow><b>Shift + E</b></color>] Climb to the masthead";
        }
    }
    [HarmonyPatch(typeof(Chair),nameof(Chair.Interact))]
    internal static class MastClimb
    {
        private static bool Prefix(Chair __instance,Humanoid human,bool hold,bool alt,ref bool __result)
        {
            if(!alt||hold||!Plugin.MastheadClimb.Value||!__instance.m_inShip||__instance.name!="mast")return true;
            Player me=human as Player;
            if(me==null||me!=Player.m_localPlayer||me.IsAttached()||me.IsEncumbered())return true;
            Masthead.Climb(me,__instance.GetComponentInParent<Ship>(),__instance);
            __result=false;
            return false;
        }
    }
    [HarmonyPatch(typeof(Minimap),"UpdateExplore")]
    internal static class LookoutsEye
    {
        private static void Prefix(Minimap __instance,out float __state){__state=__instance.m_exploreRadius;if(Masthead.Aloft)__instance.m_exploreRadius*=Plugin.MastheadSight.Value;}
        private static void Finalizer(Minimap __instance,float __state){__instance.m_exploreRadius=__state;}
    }
}
