using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Godforged
{
    // Skíðblaðnir, Freyr's ship, built by the sons of Ivaldi: big enough for all the gods, yet folded like a cloth into a pouch, and a fair
    // wind behind her whenever her sail is up. Used by the water, the relic unfolds into a karve afloat a few metres out; used beside her
    // again, she folds back into it, but only with her hold empty and nobody aboard (what she carries is never folded away). One ship per
    // relic: which one is kept on the relic, so a ship left moored stays where she is until her owner comes back to fold her. Her sail
    // always draws as if the wind were dead astern (a setting). She's the game's own karve, so she stays a karve if Godforged is removed;
    // taken apart with a hammer or sunk she gives back no wood (she was never built of any), and the relic can then unfold her anew.
    internal sealed class Skidbladnir:Feature
    {
        internal override string Name=>"Skidbladnir";
        internal static readonly ItemDef Relic=new ItemDef{Prefab="GF_Skidbladnir",Name="Skíðblaðnir",Base="MeadHealthMinor",
            Description="Freyr's ship, made by the sons of Ivaldi, folded small enough to carry. Use it by the water and she unfolds, a fair wind always in her sail; use it beside her, her hold empty, and she folds away again.",
            Cost="FineWood:30, BronzeNails:20, DeerHide:10, Silver:6",Station="forge",Level=2,Weight=4f,Stack=1,Model=t=>Meshes.AddAll(t,"skidbladnir"),
            Stats=s=>{s.m_consumeStatusEffect=null;s.m_food=0f;s.m_foodStamina=0f;s.m_foodRegen=0f;s.m_foodEitr=0f;},Use=Unfurl};
        internal override IEnumerable<ItemDef> Items=>new[]{Relic};

        // On the relic: her token, and her health when folded. On her ZDO: that she's Skíðblaðnir, and the same token (a ZDO's own id is
        // numbered afresh each time the world loads, so it can't be kept).
        private const string ShipKey="gf_skid_ship",HealthKey="gf_skid_health",Hull="Karve",AskRpc="GF_SkidAsk",TellRpc="GF_SkidTell";
        internal static readonly int Marked="gf_skidbladnir".GetStableHashCode(),TokenKey="gf_skidbladnir_token".GetStableHashCode();
        private static ConfigEntry<bool> _fairWind;
        private static ConfigEntry<float> _reach;
        // A client's question to the server about a ship it can't see: who asked about which relic, and when.
        private static ItemDrop.ItemData _asking;
        private static float _askedAt;

        internal override void Bind(ConfigFile c)
        {
            const string s="7 - Skidbladnir";
            _fairWind=Plugin.Synced.Bind(s,"FairWind",true,"Her sail always draws as if the wind were behind her.");
            _reach=Plugin.Synced.Bind(s,"FoldReach",25f,"How near her you must stand to fold her up.");
        }
        internal override void Start()
        {
            // The server: does this ship still exist, and where? (Only the server knows every ZDO.)
            Net.Register(AskRpc,(sender,pkg)=>
            {
                long token=pkg.ReadLong();
                ZDO zdo=Find(token);
                var reply=new ZPackage();reply.Write(token);reply.Write(zdo!=null);reply.Write(zdo!=null?zdo.GetPosition():Vector3.zero);
                ZRoutedRpc.instance.InvokeRoutedRPC(sender,TellRpc,reply);
            },skipOwn:false);
            Net.Register(TellRpc,(sender,pkg)=>
            {
                long token=pkg.ReadLong();bool there=pkg.ReadBool();Vector3 at=pkg.ReadVector3();
                if(_asking==null||Token(_asking)!=token)return;
                Heard(_asking,there,at);_asking=null;
            },skipOwn:false);
        }
        internal override void Tick()
        {
            // No word from the server (Godforged isn't on it): she's taken as lost, so the relic still works.
            if(_asking!=null&&Time.time-_askedAt>5f){Heard(_asking,false,Vector3.zero);_asking=null;}
        }
        internal override void Stop(){_asking=null;_test=null;}

        // (testing) a relic of its own, never in a bag, so the test leaves the player's bag as it was.
        private static ItemDrop.ItemData _test;
        internal override string Command=>"skid";
        internal override string Help=>"[use|status|sail <0-3>|upwind|face <yaw>|ships]: Skíðblaðnir with a test relic (use: unfold or fold, as from the bag; status: her and the wind; sail: set her speed, 0 stop .. 3 full; upwind: turn her bow into the wind; face: to a yaw; ships: every one in the world) (cheats only)";
        internal override System.Collections.IEnumerator Test(string[] args,System.Action<Newtonsoft.Json.Linq.JObject> output,System.Action<string> error)
        {
            Player p=Player.m_localPlayer;
            if(p==null||!(ZNet.instance!=null&&(ZNet.instance.IsServer()||global::Console.instance!=null&&global::Console.instance.IsCheatsEnabled()))){error("gf skid: only where cheats are allowed");return null;}
            if(_test==null&&Relic.Built!=null){_test=Relic.Built.GetComponent<ItemDrop>().m_itemData.Clone();_test.m_dropPrefab=Relic.Built;}
            if(_test==null){error("gf skid: the relic isn't built");return null;}
            string verb=args.Length>2?args[2]:"status";
            if(verb=="ships")
            {
                // every Skíðblaðnir in the world the server knows of (on the host; a client sees only what's near)
                var all=new List<ZDO>();int index=0;
                while(!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Hull,all,ref index)){}
                var list=new Newtonsoft.Json.Linq.JArray();
                int removed=0;
                foreach(ZDO z in all)
                {
                    if(!z.GetBool(Marked))continue;
                    GameObject at=ZNetScene.instance.FindInstance(z.m_uid);
                    list.Add($"{z.GetLong(TokenKey):x} at {z.GetPosition()} loaded {at!=null}");
                    // (ships remove: the loaded ones taken away, as folding would, only empty and with nobody aboard)
                    if(args.Length>3&&args[3]=="remove"&&at!=null&&at.GetComponent<global::Ship>().CanBeRemoved()&&!(at.GetComponentInChildren<Container>() is Container c&&c.GetInventory().NrOfItems()>0))
                    {at.GetComponent<ZNetView>().ClaimOwnership();ZNetScene.instance.Destroy(at);removed++;}
                }
                output(new Newtonsoft.Json.Linq.JObject{["karves"]=all.Count,["skidbladnir"]=list,["removed"]=removed});
                return null;
            }
            if(verb=="use")Unfurl(p,_test);
            GameObject go=Loaded(Token(_test));
            global::Ship ship=go!=null?go.GetComponent<global::Ship>():null;
            if(verb=="sail"&&ship!=null&&args.Length>3&&int.TryParse(args[3],out int sp))
                AccessTools.Field(typeof(global::Ship),"m_speed").SetValue(ship,new[]{global::Ship.Speed.Stop,global::Ship.Speed.Slow,global::Ship.Speed.Half,global::Ship.Speed.Full}[Mathf.Clamp(sp,0,3)]);
            if((verb=="upwind"||verb=="face"&&args.Length>3)&&ship!=null&&EnvMan.instance!=null)
            {
                // (her bow into the wind, where the game's sail draws least; or to a yaw)
                Vector3 into=-EnvMan.instance.GetWindDir();into.y=0;
                if(verb=="face"&&float.TryParse(args[3],NumberStyles.Float,CultureInfo.InvariantCulture,out float yaw))into=Quaternion.Euler(0,yaw,0)*Vector3.forward;
                Rigidbody body=go.GetComponent<Rigidbody>();
                Quaternion turn=Quaternion.LookRotation(into.normalized);
                if(body!=null){body.rotation=turn;body.angularVelocity=Vector3.zero;body.velocity=Vector3.zero;}
                go.transform.rotation=turn;
            }
            var o=new Newtonsoft.Json.Linq.JObject{["relic"]=_test.m_customData.TryGetValue(ShipKey,out string sid)?sid:"folded",["health"]=_test.m_customData.TryGetValue(HealthKey,out string h)?h:null,["fairWind"]=FairWind};
            if(EnvMan.instance!=null){Vector3 w=EnvMan.instance.GetWindDir();o["wind"]=Tools.V(w);o["windIntensity"]=System.Math.Round(EnvMan.instance.GetWindIntensity(),2);}
            if(go!=null)
            {
                Rigidbody body=go.GetComponent<Rigidbody>();
                o["ship"]=Tools.V(go.transform.position);o["heading"]=Tools.V(go.transform.forward);o["marked"]=Is(ship);
                o["distance"]=System.Math.Round(Vector3.Distance(go.transform.position,p.transform.position),1);
                o["speed"]=ship!=null?ship.GetSpeedSetting().ToString():null;o["velocity"]=body!=null?System.Math.Round(Vector3.Dot(body.velocity,go.transform.forward),2):0;
                o["windAngleFactor"]=ship!=null?System.Math.Round(ship.GetWindAngleFactor(),2):0;
                if(ship!=null&&EnvMan.instance!=null)
                {
                    // her pull with all sail set, as patched, and what the game's own sail would give here (before its smoothing)
                    var force=(Vector3)AccessTools.Method(typeof(global::Ship),"GetSailForce").Invoke(ship,new object[]{1f,Time.fixedDeltaTime});
                    o["sailPull"]=System.Math.Round(Vector3.Dot(force,go.transform.forward),3);
                    o["gamePull"]=System.Math.Round(ship.GetWindAngleFactor()*Mathf.Lerp(0.25f,1f,EnvMan.instance.GetWindIntensity())*ship.m_sailForceFactor,3);
                }
                o["health"]=System.Math.Round(go.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_health,-1f),1);
                Container hold=go.GetComponentInChildren<Container>();o["hold"]=hold!=null?hold.GetInventory().NrOfItems():-1;
            }
            else if(Token(_test)!=0&&ZNet.instance.IsServer())
            {
                ZDO zdo=Find(Token(_test));
                o["zdo"]=zdo==null?"none":$"at {zdo.GetPosition()} prefab {(ZNetScene.instance.GetPrefab(zdo.GetPrefab()) is GameObject pf?pf.name:zdo.GetPrefab().ToString())} owner {zdo.GetOwner()} marked {zdo.GetBool(Marked)}";
            }
            output(o);
            return null;
        }

        private static long Token(ItemDrop.ItemData relic)=>relic.m_customData.TryGetValue(ShipKey,out string s)&&long.TryParse(s,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out long t)?t:0;
        // Her, if she's loaded near here.
        private static GameObject Loaded(long token)
        {
            if(token==0)return null;
            foreach(var u in global::Ship.Instances)
                if(u is global::Ship ship&&ship!=null&&ship.GetComponent<ZNetView>() is ZNetView v&&v.IsValid()&&v.GetZDO().GetLong(TokenKey)==token)return ship.gameObject;
            return null;
        }
        // Her ZDO wherever she is (the server knows them all; only asked when the relic is used and she isn't near).
        private static ZDO Find(long token)
        {
            if(token==0||ZDOMan.instance==null)return null;
            var all=new List<ZDO>();int index=0;
            while(!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Hull,all,ref index)){}
            return all.Find(z=>z.GetLong(TokenKey)==token);
        }

        private static void Say(Player p,string text){p.Message(MessageHud.MessageType.Center,text);Plugin.Log("Skidbladnir: "+text);}

        private static bool Unfurl(Player p,ItemDrop.ItemData relic)
        {
            long token=Token(relic);
            if(token==0){Unfold(p,relic);return true;}
            GameObject go=Loaded(token);
            if(go!=null)
            {
                float far=Vector3.Distance(go.transform.position,p.transform.position);
                if(far>_reach.Value)Say(p,$"Skíðblaðnir is moored {far:0} m away: fold her up there");
                else Fold(p,relic,go);
                return true;
            }
            if(ZNet.instance!=null&&ZNet.instance.IsServer()){ZDO zdo=Find(token);Heard(relic,zdo!=null,zdo!=null?zdo.GetPosition():Vector3.zero);return true;}
            if(_asking!=null)return true;
            _asking=relic;_askedAt=Time.time;
            var pkg=new ZPackage();pkg.Write(token);
            ZRoutedRpc.instance.InvokeRoutedRPC(AskRpc,pkg);
            return true;
        }

        // Where her ship is, out of sight: still afloat somewhere (say where), or gone (sunk, or taken apart), so the relic is free again.
        private static void Heard(ItemDrop.ItemData relic,bool there,Vector3 at)
        {
            Player p=Player.m_localPlayer;
            if(p==null)return;
            if(there){Say(p,$"Skíðblaðnir is moored {Vector3.Distance(at,p.transform.position):0} m away: fold her up there");return;}
            relic.m_customData.Remove(ShipKey);relic.m_customData.Remove(HealthKey);
            Say(p,"Skíðblaðnir was lost; she can be unfolded anew");
        }

        // Afloat a few metres out, her bow away from the shore: the nearest open water in front of the player deep enough for her keel,
        // fore and aft too.
        internal static bool Berth(Vector3 from,Vector3 facing,out Vector3 at,out Quaternion turn)
        {
            at=Vector3.zero;turn=Quaternion.identity;
            facing.y=0;if(facing.sqrMagnitude<0.01f)facing=Vector3.forward;facing.Normalize();
            float[] angles={0,-25,25,-50,50,-75,75,-110,110,180};
            for(float d=5;d<=20;d+=2.5f)
                foreach(float a in angles)
                {
                    Vector3 dir=Quaternion.Euler(0,a,0)*facing,p=from+dir*d;
                    if(!Deep(p,1.2f)||!Deep(p+dir*5f,0.8f)||!Deep(p-dir*5f,0.6f))continue;
                    if(Physics.OverlapSphere(p+Vector3.up,4f,LayerMask.GetMask("piece","vehicle","Default_small","static_solid")).Length>0)continue;
                    at=new Vector3(p.x,Floating.GetLiquidLevel(p,0f),p.z);turn=Quaternion.LookRotation(dir);
                    return true;
                }
            return false;
        }
        private static bool Deep(Vector3 p,float depth)
        {
            float water=Floating.GetLiquidLevel(p,0f,LiquidType.Water);
            return water>-1000f&&water-ZoneSystem.instance.GetGroundHeight(p)>=depth;
        }

        private static void Unfold(Player p,ItemDrop.ItemData relic)
        {
            GameObject prefab=ZNetScene.instance?.GetPrefab(Hull);
            if(prefab==null){Say(p,"No karve in this game");return;}
            if(!Berth(p.transform.position,p.transform.forward,out Vector3 at,out Quaternion turn)){Say(p,"Skíðblaðnir needs open water close by");return;}
            GameObject go=Object.Instantiate(prefab,at,turn);
            ZNetView view=go.GetComponent<ZNetView>();
            ZDO zdo=view.GetZDO();
            long mint=0;while(mint==0)mint=((long)Random.Range(int.MinValue,int.MaxValue)<<32)^(uint)Random.Range(int.MinValue,int.MaxValue);
            zdo.Set(Marked,true);zdo.Set(TokenKey,mint);
            if(relic.m_customData.TryGetValue(HealthKey,out string h)&&float.TryParse(h,NumberStyles.Float,CultureInfo.InvariantCulture,out float health))zdo.Set(ZDOVars.s_health,health);
            Piece piece=go.GetComponent<Piece>();
            piece?.SetCreator(p.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            piece?.m_placeEffect.Create(at,turn,go.transform);
            relic.m_customData[ShipKey]=mint.ToString("x",CultureInfo.InvariantCulture);
            relic.m_customData.Remove(HealthKey);
            Say(p,"Skíðblaðnir unfolds");
        }

        private static void Fold(Player p,ItemDrop.ItemData relic,GameObject go)
        {
            global::Ship ship=go.GetComponent<global::Ship>();
            Container hold=go.GetComponentInChildren<Container>();
            ZNetView view=go.GetComponent<ZNetView>();
            if(view==null||!view.IsValid())return;
            if(ship!=null&&!ship.CanBeRemoved()){Say(p,"Someone is aboard Skíðblaðnir");return;}
            if(hold!=null&&(hold.IsInUse()||hold.GetInventory().NrOfItems()>0)){Say(p,"Empty Skíðblaðnir's hold before she's folded");return;}
            view.ClaimOwnership();
            if(view.GetZDO().GetFloat(ZDOVars.s_health,-1f) is float health&&health>=0)relic.m_customData[HealthKey]=health.ToString(CultureInfo.InvariantCulture);
            else relic.m_customData.Remove(HealthKey);
            relic.m_customData.Remove(ShipKey);
            go.GetComponent<Piece>()?.m_placeEffect.Create(go.transform.position,go.transform.rotation);
            ZNetScene.instance.Destroy(go);
            Say(p,"Skíðblaðnir folds away");
        }

        internal static bool Is(global::Ship ship)
        {
            ZNetView view=ship!=null?ship.GetComponent<ZNetView>():null;
            return view!=null&&view.IsValid()&&view.GetZDO().GetBool(Marked);
        }
        internal static bool FairWind=>_fairWind!=null&&_fairWind.Value;
    }

    // Her fair wind: the sail draws as the game's does with the wind dead astern (its full pull, at the wind's strength), whatever way it blows.
    [HarmonyPatch(typeof(Ship),"GetSailForce")]
    internal static class SkidbladnirWind
    {
        private static void Postfix(Ship __instance,float sailSize,ref Vector3 __result)
        {
            if(!Skidbladnir.FairWind||EnvMan.instance==null||!Skidbladnir.Is(__instance))return;
            float strength=Mathf.Lerp(0.25f,1f,EnvMan.instance.GetWindIntensity());
            __result=__instance.transform.forward*(strength*__instance.m_sailForceFactor*sailSize);
        }
    }

    // And her sail turns to it: square to her keel, as if the wind were behind.
    [HarmonyPatch(typeof(Ship),"UpdateSail")]
    internal static class SkidbladnirSail
    {
        private static void Postfix(Ship __instance,float dt)
        {
            if(!Skidbladnir.FairWind||__instance.m_mastObject==null||!Skidbladnir.Is(__instance))return;
            Ship.Speed s=__instance.GetSpeedSetting();
            if(s!=Ship.Speed.Full&&s!=Ship.Speed.Half)return;
            Quaternion to=Quaternion.LookRotation(-__instance.transform.forward,__instance.transform.up);
            __instance.m_mastObject.transform.rotation=Quaternion.RotateTowards(__instance.m_mastObject.transform.rotation,to,60f*dt);
        }
    }

    // She was never built of wood, so she gives none back: taken apart with a hammer, or wrecked.
    [HarmonyPatch(typeof(Piece),nameof(Piece.DropResources))]
    internal static class SkidbladnirNoWood
    {
        private static bool Prefix(Piece __instance)=>!(__instance.GetComponent<Ship>() is Ship ship&&Skidbladnir.Is(ship));
    }
}
