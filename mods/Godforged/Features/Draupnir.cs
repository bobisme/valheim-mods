using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Godforged
{
    // Draupnir, Odin's gold ring, made by Brokkr and Eitri: every ninth night eight rings of equal weight drip from it. Carried (the game
    // has no ring slot), every ninth dawn after it's first carried it pays out coins, eight rings' worth. One Draupnir per player: a second
    // in the bag pays nothing. The day it last paid is kept on the ring itself, so it survives logouts and changes hands with it.
    // Fimbulwinter hears of every drip through Api.OnDrip (its Glory).
    internal sealed class Draupnir:Feature
    {
        internal override string Name=>"Draupnir";
        internal static readonly ItemDef Ring=new ItemDef{Prefab="GF_Draupnir",Name="Draupnir",Base="Ruby",
            Description="Odin's ring, forged by Brokkr and Eitri. Every ninth night eight rings of equal weight drip from it. Carry it, and every ninth dawn it pays.",
            Cost="Coins:300, Ruby:3, Silver:6",Station="forge",Level=3,Weight=0.3f,Model=t=>Meshes.AddAll(t,"draupnir")};
        internal override IEnumerable<ItemDef> Items=>new[]{Ring};

        private const string Key="gf_draupnir_day";
        private static ConfigEntry<int> _coins,_every;
        private static float _next;
        internal static readonly List<System.Action<long,int>> OnDrip=new List<System.Action<long,int>>();

        internal override void Bind(ConfigFile c)
        {
            const string s="4 - Draupnir";
            _coins=c.Bind(s,"Coins",80,"Coins it drips (eight rings' worth).");
            _every=c.Bind(s,"EveryDays",9,"Every how many days it drips, at dawn.");
        }

        internal override void Tick()
        {
            if(Time.time<_next)return;
            _next=Time.time+2f;
            Player p=Player.m_localPlayer;
            if(p==null||p.IsDead()||EnvMan.instance==null||ObjectDB.instance==null)return;
            ItemDrop.ItemData ring=p.GetInventory().GetAllItems().Find(Ring.Is);
            if(ring==null)return;
            int day=EnvMan.instance.GetDay();
            if(!ring.m_customData.TryGetValue(Key,out string s)||!int.TryParse(s,out int last)){ring.m_customData[Key]=day.ToString();return;}
            if(day-last<Mathf.Max(1,_every.Value)||!EnvMan.IsDay())return;
            ring.m_customData[Key]=day.ToString();
            Drip(p,_coins.Value);
        }

        internal override string Command=>"drip";
        internal override string Help=>": Draupnir drips now, as if it were the ninth dawn (cheats only)";
        internal override System.Collections.IEnumerator Test(string[] args,System.Action<Newtonsoft.Json.Linq.JObject> output,System.Action<string> error)
        {
            Player p=Player.m_localPlayer;
            if(p==null||!(ZNet.instance!=null&&(ZNet.instance.IsServer()||global::Console.instance!=null&&global::Console.instance.IsCheatsEnabled()))){error("gf drip: only where cheats are allowed");return null;}
            ItemDrop.ItemData ring=p.GetInventory().GetAllItems().Find(Ring.Is);
            if(ring==null){error("gf drip: no Draupnir in the bag");return null;}
            int before=p.GetInventory().CountItems("$item_coins");
            ring.m_customData[Key]=EnvMan.instance.GetDay().ToString();
            Drip(p,_coins.Value);
            output(new Newtonsoft.Json.Linq.JObject{["coinsBefore"]=before,["coinsAfter"]=p.GetInventory().CountItems("$item_coins")});
            return null;
        }

        private static void Drip(Player p,int n)
        {
            GameObject coins=ObjectDB.instance.GetItemPrefab("Coins");
            if(coins!=null&&n>0&&!p.GetInventory().AddItem(coins,n))
            {
                GameObject go=Object.Instantiate(coins,p.transform.position+p.transform.forward*0.6f+Vector3.up,Quaternion.identity);
                go.GetComponent<ItemDrop>()?.SetStack(n);
            }
            p.Message(MessageHud.MessageType.Center,$"Draupnir drips eight rings: {n} coins");
            Sound.Play(Clinks(),p.transform.position+Vector3.up,0.8f,1f,20f,p.transform);
            foreach(var f in OnDrip)Plugin.Safe("Api.OnDrip",()=>f(p.GetPlayerID(),n));
        }

        // Eight small gold rings falling one after another: bright inharmonic pings, each fading fast.
        internal static AudioClip Clinks()
        {
            var at=new float[8];var hz=new float[8];var rng=new System.Random(8);
            for(int i=0;i<8;i++){at[i]=0.05f+i*0.16f+(float)rng.NextDouble()*0.05f;hz[i]=2300f+(float)rng.NextDouble()*1800f;}
            return Sound.Make("draupnir clinks",1.8f,t=>
            {
                float v=0f;
                for(int i=0;i<8;i++)
                {
                    float d=t-at[i];if(d<0f||d>0.5f)continue;
                    float e=Mathf.Exp(-d*14f);
                    v+=(Sound.Sine(hz[i]*d)*0.6f+Sound.Sine(hz[i]*2.76f*d)*0.25f+Sound.Sine(hz[i]*5.4f*d)*0.1f)*e*0.35f;
                }
                return v;
            });
        }
    }
}
