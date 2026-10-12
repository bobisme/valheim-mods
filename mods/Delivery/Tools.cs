using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Delivery
{
    // A "delivery" command for Claude Tools, found while the game runs (no reference), to check Delivery in a live game.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.quad.claudetools";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if(Time.unscaledTime<_nextCheck)return;
            _nextCheck=Time.unscaledTime+5;
            BaseUnityPlugin found=BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid,out PluginInfo info)?info.Instance:null;
            if(found==_tools)return;
            _tools=found;
            if(found==null)return;
            try
            {
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"delivery",
                    "delivery info | meals [all] | menu | close | blow <prefab> | send | order <prefab> [count] [<prefab> [count]...] | now | tip <coins> | regard <0-1> | cancel | horn | free [off]: check Delivery. menu opens the horn's menu and blow/send press its buttons; order sends an order straight away (its calls blown); now makes the courier set out at once; tip answers the tip window; regard sets how he thinks of the player; cancel sends him away (no charge); horn puts an Errand Horn in the inventory; free makes orders cost nothing (for a full inventory).",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: delivery command added");
            }
            catch(Exception e){Plugin.Log("Could not add the delivery command: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }
        private static JArray V(Vector3 v)=>new JArray(Math.Round(v.x,1),Math.Round(v.y,1),Math.Round(v.z,1));

        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null){error("delivery: no player");return null;}
            string sub=args.Length>1?args[1]:"info";
            switch(sub)
            {
                case "info":
                {
                    Courier c=Delivery.Run.Info(me);
                    var (fee,why)=Delivery.Run.FeeAt(me.transform.position);
                    var o=new JObject{["courier"]=c.Name,["regard"]=Math.Round(c.Regard,3),["regardName"]=Policy.RegardName(c.Regard),["runs"]=c.Runs,["tipped"]=c.Tipped,
                        ["stage"]=Delivery.Run.Now.ToString(),["fee"]=fee,["feeWhy"]=why,["coins"]=me.GetInventory().CountItems("$item_coins"),
                        ["menuOpen"]=OrderMenu.IsOpen,["tipOpen"]=Tip.IsOpen,["calling"]=Policy.Order(OrderMenu.Called.Select(kv=>new KeyValuePair<string,int>(kv.Key.Prefab,kv.Value))),
                        ["player"]=V(me.transform.position)};
                    if(Delivery.Run.Now!=Delivery.Run.Stage.None)
                    {
                        o["order"]=Policy.Order(Delivery.Run.Order.Select(kv=>new KeyValuePair<string,int>(kv.Key.Prefab,kv.Value)));
                        o["total"]=Delivery.Run.Total;o["setsOutIn"]=Math.Round(Delivery.Run.SetsOutIn,1);
                        if(Delivery.Run.Where.HasValue){o["courierAt"]=V(Delivery.Run.Where.Value);o["dyed"]=Delivery.Run.Dyed;o["pinned"]=Delivery.Run.Pinned;o["distance"]=Math.Round(Vector3.Distance(Delivery.Run.Where.Value,me.transform.position),1);}
                    }
                    output(o);
                    break;
                }
                case "meals":
                {
                    List<Meal> list=args.Length>2&&args[2]=="all"?Meals.All:Meals.For(me);
                    output(new JObject{["count"]=list.Count,["all"]=Meals.All.Count,["meals"]=new JArray(list.Select(m=>new JObject{["prefab"]=m.Prefab,["name"]=m.Name,["price"]=m.Price,["stats"]=m.Stats,["call"]=Policy.Key(m.Tune)}))});
                    break;
                }
                case "menu":OrderMenu.Open();output(new JObject{["open"]=OrderMenu.IsOpen});break;
                case "close":OrderMenu.Close();Tip.Close();output(new JObject{["open"]=OrderMenu.IsOpen});break;
                case "blow":
                {
                    Meal m=args.Length>2?Meals.Find(args[2]):null;
                    if(m==null){error("delivery blow: no such meal");break;}
                    if(!OrderMenu.IsOpen){error("delivery blow: the menu isn't open");break;}
                    OrderMenu.Press(m);
                    output(new JObject{["blown"]=m.Prefab,["call"]=Policy.Key(m.Tune)});
                    break;
                }
                case "send":OrderMenu.PressSend();output(new JObject{["sent"]=true});break;
                case "order":
                {
                    var order=new List<KeyValuePair<Meal,int>>();
                    for(int i=2;i<args.Length;i++)
                    {
                        Meal m=Meals.Find(args[i]);
                        if(m==null){error("delivery order: no such meal "+args[i]);return null;}
                        int n=i+1<args.Length&&int.TryParse(args[i+1],out int k)?k:1;
                        if(i+1<args.Length&&int.TryParse(args[i+1],out _))i++;
                        order.Add(new KeyValuePair<Meal,int>(m,Math.Max(1,n)));
                    }
                    if(order.Count==0){error("delivery order: which meals?");break;}
                    foreach(var kv in order)for(int i=0;i<kv.Value;i++)Calls.Blow(kv.Key.Tune);
                    if(!Delivery.Run.Send(me,order)){error("delivery order: an order is already out");break;}
                    output(new JObject{["sent"]=Policy.Order(order.Select(kv=>new KeyValuePair<string,int>(kv.Key.Prefab,kv.Value))),["total"]=Delivery.Run.Total,["setsOutIn"]=Math.Round(Delivery.Run.SetsOutIn,1)});
                    break;
                }
                case "free":Delivery.Run.Free=!(args.Length>2&&args[2]=="off");output(new JObject{["free"]=Delivery.Run.Free});break;
                case "now":Delivery.Run.Hurry();output(new JObject{["stage"]=Delivery.Run.Now.ToString()});break;
                case "tip":
                {
                    if(!Tip.IsOpen){error("delivery tip: no tip window");break;}
                    Tip.Choose(args.Length>2&&int.TryParse(args[2],out int t)?Math.Max(0,t):0);
                    output(new JObject{["tip"]=args.Length>2?args[2]:"0"});
                    break;
                }
                case "regard":
                {
                    Courier c=Delivery.Run.Info(me);
                    if(args.Length>2&&float.TryParse(args[2],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float r))c.Regard=Mathf.Clamp01(r);
                    Delivery.Run.Save(me,c);
                    output(new JObject{["regard"]=c.Regard,["name"]=Policy.RegardName(c.Regard)});
                    break;
                }
                case "cancel":Delivery.Run.Cancel(null);output(new JObject{["stage"]=Delivery.Run.Now.ToString()});break;
                case "horn":
                {
                    if(Items.Horn.Built==null){error("delivery horn: not made");break;}
                    bool ok=me.GetInventory().AddItem(Items.Horn.Built,1);
                    if(!ok)ItemDrop.DropItem(Items.Horn.Built.GetComponent<ItemDrop>().m_itemData.Clone(),1,me.transform.position+me.transform.forward,Quaternion.identity);
                    output(new JObject{["inInventory"]=ok});
                    break;
                }
                default:error("delivery: unknown "+sub);break;
            }
            return null;
        }
    }
}
