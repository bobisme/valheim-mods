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

        // The ship you are on: the game's (its deck trigger), or the one whose masthead you sit at, above that trigger.
        internal static Ship Aboard()=>Ship.GetLocalShip()??Masthead.Ship;
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
            Ship ship=Ships.Aboard();
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
        private static string FleetKey()=>Plugin.World() is string w?FleetPrefix+w:null;
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

        // Ships taken out of the fleet (test boats removed): their pins off the map. How many were in it.
        internal static int Forget(Player me,System.Func<ZDOID,bool> which)
        {
            int n=0;
            for(int i=Fleet_.Count-1;i>=0;i--)if(which(Fleet_[i].Id)){RemovePin(Fleet_[i]);Fleet_.RemoveAt(i);n++;}
            if(n>0&&FleetKey() is string key&&me!=null)SaveFleet(me,key);
            return n;
        }

        internal static string FleetText(Player me)=>FleetKey() is string key&&me.m_customData.TryGetValue(key,out string text)?text:"";
        internal static void SetFleet(Player me,string text)
        {
            if(!(FleetKey() is string key))return;
            me.m_customData[key]=text;ClearPins();Fleet_.Clear();_fleetFor=null; // (read again on the next update)
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
            MastLine(ship,out Vector3 top,out Vector3 foot);
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
            Seat(root,_looks,top,foot);
            Fits.Add(fit);
        }
        // The mast's own line in the ship's space: its top and foot, on the pivot it turns on with the yard (the middle of its
        // bounds moves sideways as the sail swings round, which took the fittings off the mast). Measured in the ship's own
        // frame: a ship rolling at sea tips the long yard, and a world box round it reaches higher than the mast.
        internal static void MastLine(Ship ship,out Vector3 top,out Vector3 foot)
        {
            Transform mast=ship.m_mastObject.transform;
            Vector3 axis=ship.transform.InverseTransformPoint(mast.position);
            float low=float.MaxValue,high=float.MinValue;
            // only the mast's solid parts, shown, plain meshes (the longship and the drakkar also carry hidden sails and cloth
            // colliders under the mast, one of them reaching far below the keel)
            foreach(MeshRenderer r in mast.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter f=r.GetComponent<MeshFilter>();
                if(!r.enabled||f==null||f.sharedMesh==null)continue;
                Bounds m=f.sharedMesh.bounds;
                for(int i=0;i<8;i++)
                {
                    Vector3 c=m.center+Vector3.Scale(m.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    float y=ship.transform.InverseTransformPoint(r.transform.TransformPoint(c)).y;
                    low=Mathf.Min(low,y);high=Mathf.Max(high,y);
                }
            }
            if(low>high)low=high=axis.y;
            top=new Vector3(axis.x,high,axis.z);
            foot=new Vector3(axis.x,low,axis.z);
        }
        // Where the lookout sits, from the masthead (the top of the mast's bounds), in the ship's space.
        internal static Vector3 SeatAt(Vector3 top)=>top+new Vector3(0,-1.35f,0.32f);
        // The lookout's seat: a board lashed to the fore side of the mast under the masthead, slung from it by two ropes, and
        // pegs up the mast to climb by (so whoever is aloft sits on something, for everyone aboard to see).
        private static void Seat(Transform root,Looks l,Vector3 top,Vector3 foot)
        {
            Vector3 seat=SeatAt(top);
            Vector3 mast=new Vector3(top.x,seat.y,top.z);
            Models.Part(root,"SeatBoard",PrimitiveType.Cube,l.Get("DarkWood"),new Vector3(seat.x,seat.y-0.03f,top.z+0.3f),Vector3.zero,new Vector3(0.5f,0.05f,0.42f));
            Models.Part(root,"SeatCleat",PrimitiveType.Cube,l.Get("DarkWood"),new Vector3(seat.x,seat.y-0.1f,top.z+0.13f),Vector3.zero,new Vector3(0.3f,0.1f,0.08f));
            foreach(float side in new[]{-0.22f,0.22f})
                Models.Rod(root,"SeatRope",l.Get("Leather"),new Vector3(seat.x+side,seat.y-0.01f,top.z+0.48f),mast+new Vector3(side*0.25f,0.75f,0.1f),0.012f);
            Models.Part(root,"SeatLashing",PrimitiveType.Cylinder,l.Get("Leather"),mast+new Vector3(0,0.75f,0),Vector3.zero,new Vector3(0.24f,0.03f,0.24f));
            int i=0;
            for(float y=foot.y+0.7f;y<seat.y-0.35f;y+=0.5f,i++) // pegs, port and starboard by turns
            {
                float side=i%2==0?0.1f:-0.1f;
                Models.Rod(root,"Peg",l.Get("DarkWood"),new Vector3(top.x+side*0.6f,y,top.z+0.04f),new Vector3(top.x+side*2.2f,y+0.03f,top.z+0.04f),0.02f);
            }
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
        // How high the lookout's seat is above the sea; the tallest (a drakkar's) gets the whole of MastheadSight.
        internal static float Height;
        internal const float TallestSeat=16.4f; // (a drakkar's seat, measured: 16.37 m)
        internal static float Sight=>Aloft?(float)Policy.MastheadSight(Plugin.MastheadSight.Value,Height,TallestSeat):1f;
        private static Transform _perch;
        // Where your hips settle in the sitting pose, from the seat's point: measured the first time you sit up there, so the
        // seat point can be moved to put you on the board (the pose is the same each time).
        private static Vector3? _hipFit;
        private static Vector3 _seatHips,_deck;
        private static float _climbedAt;
        private static bool _fitted;
        // No fall is counted until a moment after you go up or come down (going up or down is a jump of several metres).
        internal static float SafeUntil;
        private static readonly AccessTools.FieldRef<Player,Vector3> DetachOffset=AccessTools.FieldRefAccess<Player,Vector3>("m_detachOffset");
        internal static Ship Ship=>Aloft&&_perch!=null?_perch.GetComponentInParent<Ship>():null;
        internal static bool IsPerch(Transform t)=>t!=null&&t.name=="WayfinderMasthead";
        internal static void Climb(Player me,Ship ship,Chair mastChair)
        {
            if(ship==null||ship.m_mastObject==null)return;
            Fittings.MastLine(ship,out Vector3 top,out Vector3 foot);
            if(top.y-foot.y<2.5f){me.Message(MessageHud.MessageType.Center,"There is no masthead to climb.");return;}
            if(_perch!=null)Object.Destroy(_perch.gameObject);
            _perch=new GameObject("WayfinderMasthead").transform;
            _perch.SetParent(ship.transform,false);
            Vector3 seat=Fittings.SeatAt(top);
            _seatHips=new Vector3(seat.x,seat.y+0.1f,top.z+0.2f); // (hips just above the board, back to the mast)
            _perch.localPosition=_hipFit.HasValue?_seatHips-_hipFit.Value:seat;
            _fitted=_hipFit.HasValue;
            // back down onto the deck beside the mast: found by looking straight down there for the ship's own floor
            Vector3 deck=mastChair.m_attachPoint.position;
            int mask=LayerMask.GetMask("vehicle","piece","Default","static_solid");
            foreach(float side in new[]{0.9f,-0.9f,1.3f})
            {
                Vector3 from=ship.transform.TransformPoint(new Vector3(top.x+side,Mathf.Lerp(foot.y,top.y,0.3f),top.z)); // (under the yard)
                foreach(RaycastHit hit in Physics.RaycastAll(from,Vector3.down,12,mask,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
                    if(hit.collider.GetComponentInParent<Ship>()==ship){deck=hit.point;goto found;}
            }
            found:
            _deck=ship.transform.InverseTransformPoint(deck+Vector3.up*0.3f);
            SafeUntil=Time.time+2;_climbedAt=Time.time;
            me.AttachStart(_perch,null,false,false,true,"attach_chair",_perch.InverseTransformPoint(deck+Vector3.up*0.3f));
            me.Message(MessageHud.MessageType.Center,"You climb to the lookout's seat.");
        }
        internal static void Tick(Player me)
        {
            Aloft=_perch!=null&&me.IsAttached()&&me.GetAttachPoint()==_perch;
            if(_perch!=null&&!me.IsAttached()){Object.Destroy(_perch.gameObject);_perch=null;}
            if(Aloft){SafeUntil=Mathf.Max(SafeUntil,Time.time+0.5f);Height=_perch.position.y-(ZoneSystem.instance!=null?ZoneSystem.instance.m_waterLevel:30);}
            if(Aloft&&!_fitted&&Time.time-_climbedAt>1.2f)Fit(me);
        }
        // Once the sitting pose has settled: where are the hips from the seat's point? Move the point so they sit on the board.
        private static void Fit(Player me)
        {
            _fitted=true;
            Transform hips=Hips(me);
            if(hips==null||_perch.parent==null)return;
            Vector3 off=_perch.parent.InverseTransformVector(hips.position-_perch.position);
            if(off.magnitude>2)return; // (not the pose we expected)
            _hipFit=off;
            _perch.localPosition=_seatHips-off;
            DetachOffset(me)=_perch.InverseTransformPoint(_perch.parent.TransformPoint(_deck));
            Plugin.Log($"Lookout's seat: hips sit {off.x:0.00},{off.y:0.00},{off.z:0.00} from the seat point.");
        }
        private static Transform Hips(Player me)
        {
            foreach(Animator a in me.GetComponentsInChildren<Animator>())
            {
                if(a.isHuman){Transform h=a.GetBoneTransform(HumanBodyBones.Hips);if(h!=null)return h;}
            }
            foreach(Transform t in me.GetComponentsInChildren<Transform>())if(t.name=="Hips")return t;
            return null;
        }
        internal static void Stop(){if(_perch!=null){Player.m_localPlayer?.AttachStop();Object.Destroy(_perch.gameObject);}_perch=null;Aloft=false;}
    }
    // Leaving the masthead (a key pressed, or a reload) sets you down on the deck: no fall from the height you sat at. (While
    // seated the game keeps your highest point in the air at the seat, and a low chair never matters; a masthead would.)
    [HarmonyPatch(typeof(Player),nameof(Player.AttachStop))]
    internal static class SafeDescent
    {
        private static readonly AccessTools.FieldRef<Character,float> MaxAir=AccessTools.FieldRefAccess<Character,float>("m_maxAirAltitude");
        internal static void Reset(Character c)=>MaxAir(c)=c.transform.position.y;
        private static void Prefix(Player __instance,out bool __state)=>__state=__instance.IsAttached()&&Masthead.IsPerch(__instance.GetAttachPoint());
        private static void Postfix(Player __instance,bool __state)
        {
            if(!__state)return;
            Reset(__instance);
            Masthead.SafeUntil=Time.time+2;
            Rigidbody body=__instance.GetComponent<Rigidbody>();
            if(body!=null)body.linearVelocity=Vector3.zero;
        }
    }
    // Going up and coming down: no fall counted for a moment either side (the jump up was being taken for a fall).
    [HarmonyPatch(typeof(Character),"UpdateGroundContact")]
    internal static class NoFallAloft
    {
        private static void Prefix(Character __instance)
        {
            if(__instance==Player.m_localPlayer&&Time.time<Masthead.SafeUntil)SafeDescent.Reset(__instance);
        }
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
        private static void Prefix(Minimap __instance,out float __state){__state=__instance.m_exploreRadius;if(Masthead.Aloft)__instance.m_exploreRadius*=Masthead.Sight;}
        private static void Finalizer(Minimap __instance,float __state){__instance.m_exploreRadius=__state;}
    }
}
