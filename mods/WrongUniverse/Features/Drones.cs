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
    // Combat drones: use the item to send one up, use it again to call it back. It hovers by your shoulder and zaps the nearest enemy it
    // can see. A battery runs down while it's out (faster when it shoots) and charges back up in your bag. One drone per item; carry
    // three for a swarm. The drone is a networked object (non-persistent: it goes when its owner does); its brain runs in the owner's game.
    internal sealed class Drones:Feature
    {
        internal override string Name=>"Drones";
        internal const string BotPrefab="WU_DroneBot";
        internal static readonly ItemDef Drone=new ItemDef{Prefab="WU_Drone",Name="Combat Drone",Base="SurtlingCore",
            Cost="Iron:8, Bronze:4, SurtlingCore:3, Resin:5",Station="forge",Level=1,Weight=3f,
            Description="A palm-sized quadcopter with one red eye and no sense of proportion. Use it to send it up: it keeps by your shoulder and lasers anything that means you harm. Use it again to call it home. Its battery runs down while it flies and charges in your bag.",
            Model=t=>DroneBot.BuildModel(t,0.8f,false),IconLook=Quaternion.Euler(60,0,0)};
        internal override IEnumerable<ItemDef> Items=>new[]{Drone};

        internal static ConfigEntry<float> Range,Damage,FireInterval,FlightMinutes,RechargeMinutes,ShotCost;
        private static GameObject _prefab;
        // The local player's drones out now, by the item that sent them.
        internal static readonly Dictionary<ItemDrop.ItemData,DroneBot> Out=new Dictionary<ItemDrop.ItemData,DroneBot>();

        internal override void Bind(ConfigFile c)
        {
            const string S="5 - Drones";
            Range=c.Bind(S,"Range",30f,new ConfigDescription("How far a drone looks for enemies, in metres.",new AcceptableValueRange<float>(5,80)));
            Damage=c.Bind(S,"Damage",22f,new ConfigDescription("Damage of one zap (half piercing, half lightning).",new AcceptableValueRange<float>(1,500)));
            FireInterval=c.Bind(S,"SecondsBetweenShots",0.7f,new ConfigDescription("How often a drone zaps.",new AcceptableValueRange<float>(0.1f,10)));
            FlightMinutes=c.Bind(S,"FlightMinutes",5f,new ConfigDescription("How long a full battery keeps a drone up when it isn't shooting.",new AcceptableValueRange<float>(0.5f,120)));
            ShotCost=c.Bind(S,"BatteryPerShot",0.006f,new ConfigDescription("Battery each zap uses (1 = a full battery).",new AcceptableValueRange<float>(0,0.2f)));
            RechargeMinutes=c.Bind(S,"RechargeMinutes",3f,new ConfigDescription("How long a flat battery takes to charge fully in your bag.",new AcceptableValueRange<float>(0.1f,120)));
            Drone.Use=Use;
        }

        internal override void Register(ZNetScene scene)
        {
            if(_prefab==null)
            {
                _prefab=new GameObject(BotPrefab);
                _prefab.transform.SetParent(Things.Holder,false);
                ZNetView nv=_prefab.AddComponent<ZNetView>();
                nv.m_persistent=false;nv.m_type=ZDO.ObjectType.Default;nv.m_distant=false;
                ZSyncTransform sync=_prefab.AddComponent<ZSyncTransform>();
                sync.m_syncPosition=true;sync.m_syncRotation=true;
                var model=new GameObject("model");model.transform.SetParent(_prefab.transform,false);
                DroneBot.BuildModel(model.transform,1f,true);
                _prefab.AddComponent<DroneBot>();
            }
            Things.AddPrefab(scene,_prefab);   // (always registered: an unknown prefab with a ZNetView spams "Missing prefab hash")
        }

        internal override void Start()
        {
            // After a hot reload: other players' drones already in view get this copy's brain-less look (spinning rotors, buzz).
            if(ZNetScene.instance==null)return;
            var views=(Dictionary<ZDO,ZNetView>)AccessTools.Field(typeof(ZNetScene),"m_instances").GetValue(ZNetScene.instance);
            int hash=BotPrefab.GetStableHashCode();
            foreach(var kv in views.ToList())
                if(kv.Value!=null&&kv.Key.GetPrefab()==hash&&kv.Value.GetComponent<DroneBot>()==null)kv.Value.gameObject.AddComponent<DroneBot>();
        }

        // Battery, kept on the item (saved with it): the level when last put away and when, so it charges while it sits in the bag.
        internal static float Battery(ItemDrop.ItemData item)
        {
            if(Out.TryGetValue(item,out DroneBot bot)&&bot!=null)return bot.Battery;
            float level=item.m_customData.TryGetValue("wu_battery",out string b)&&float.TryParse(b,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)?v:1f;
            double at=item.m_customData.TryGetValue("wu_batteryAt",out string a)&&double.TryParse(a,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double d)?d:Now;
            return Mathf.Clamp01(level+(float)(Now-at)/(RechargeMinutes.Value*60f));
        }
        private static void StoreBattery(ItemDrop.ItemData item,float level)
        {
            item.m_customData["wu_battery"]=Mathf.Clamp01(level).ToString("0.####",System.Globalization.CultureInfo.InvariantCulture);
            item.m_customData["wu_batteryAt"]=Now.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
        }
        private static double Now=>ZNet.instance!=null?ZNet.instance.GetTimeSeconds():Time.time;

        private static bool Use(Player p,ItemDrop.ItemData item)
        {
            if(Out.TryGetValue(item,out DroneBot bot)&&bot!=null){Recall(item,"Drone recalled");return true;}
            Deploy(p,item);
            return true;
        }
        internal static DroneBot Deploy(Player p,ItemDrop.ItemData item)
        {
            if(_prefab==null||ZNetScene.instance==null)return null;
            float battery=Battery(item);
            if(battery<0.1f){p.Message(MessageHud.MessageType.Center,$"Drone battery flat ({battery*100:0}%)");return null;}
            int index=0;while(Out.Values.Any(b=>b!=null&&b.Index==index))index++;
            Vector3 at=p.transform.position+Vector3.up*1.2f+p.transform.right*0.6f;
            GameObject go=Object.Instantiate(_prefab,at,p.transform.rotation);
            var bot=go.GetComponent<DroneBot>();
            bot.Launch(p,item,index,battery);
            Out[item]=bot;
            p.Message(MessageHud.MessageType.TopLeft,"Drone up");
            return bot;
        }
        internal static void Recall(ItemDrop.ItemData item,string why)
        {
            if(!Out.TryGetValue(item,out DroneBot bot))return;
            Out.Remove(item);
            StoreBattery(item,bot!=null?bot.Battery:Battery(item));
            if(bot!=null)bot.Despawn();
            if(why!=null)Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft,why);
        }
        private static void RecallAll(string why){foreach(var item in Out.Keys.ToList())Recall(item,why);}

        internal override void Tick()
        {
            if(Out.Count==0)return;
            Player p=Player.m_localPlayer;
            if(p==null||p.IsDead()){RecallAll(null);return;}
            foreach(var kv in Out.ToList())
            {
                if(kv.Value==null){Out.Remove(kv.Key);continue;}
                if(!p.GetInventory().ContainsItem(kv.Key)){Recall(kv.Key,"Drone lost its controller");continue;}
                if(kv.Value.Battery<=0f)Recall(kv.Key,"Drone battery flat: it flew home");
            }
        }
        internal override void Gui()
        {
            int n=0;
            foreach(var kv in Out.OrderBy(k=>k.Value!=null?k.Value.Index:99))
                if(kv.Value!=null)Hud.Bar($"Drone {++n}",kv.Value.Battery,kv.Value.Battery<0.2f?Look.Red:Look.Cyan,kv.Value.Battery<0.1f);
        }
        internal override void Stop()
        {
            RecallAll(null);
            foreach(DroneBot b in DroneBot.All.ToList())if(b!=null)b.Unload();
            DroneBot.All.Clear();
            _prefab=null;   // (it lives under Things.Holder, which Things looks after)
        }

        internal override string Command=>"drone";
        internal override string Help=>"[info|up|home|zap <x y z>]";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"info";
            switch(sub)
            {
                case "up":
                {
                    ItemDrop.ItemData item=me.GetInventory().GetAllItems().FirstOrDefault(i=>Drone.Is(i)&&!Out.ContainsKey(i));
                    if(item==null){error("drone up: no idle Combat Drone in the inventory (wu give WU_Drone)");return null;}
                    DroneBot bot=Deploy(me,item);
                    output(new JObject{["deployed"]=bot!=null,["battery"]=Math.Round(Battery(item),3)});
                    return null;
                }
                case "home":RecallAll("Drones recalled");output(new JObject{["out"]=Out.Count});return null;
                case "zap":
                {
                    if(args.Length<6){error("drone zap x y z");return null;}
                    var to=new Vector3(float.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture),float.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture),float.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture));
                    DroneBot bot=Out.Values.FirstOrDefault(b=>b!=null);
                    if(bot==null){error("drone zap: no drone out");return null;}
                    bot.Zap(to);
                    output(new JObject{["from"]=Tools.V(bot.Eye),["to"]=Tools.V(to)});
                    return null;
                }
                default:
                    output(new JObject{["prefab"]=_prefab!=null,["out"]=new JArray(Out.Select(kv=>new JObject{["index"]=kv.Value!=null?kv.Value.Index:-1,
                        ["battery"]=Math.Round(Battery(kv.Key),3),["at"]=kv.Value!=null?Tools.V(kv.Value.transform.position):null,["target"]=kv.Value!=null?kv.Value.TargetName:null})),
                        ["inBag"]=new JArray(me.GetInventory().GetAllItems().Where(Drone.Is).Select(i=>Math.Round(Battery(i),3))),
                        ["inWorld"]=DroneBot.All.Count});
                    return null;
            }
        }
    }
}
