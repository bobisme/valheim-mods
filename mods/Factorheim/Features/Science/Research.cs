using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Factorheim
{
    // The world's research: the next tier (Tech.Level + 1) is researched in units; a unit uses one of each pack that tier needs.
    //
    // Where progress lives: a world global key "fh_progress<tier> <units>" (the server keeps global keys in the world save and sends every
    // change to everyone). Only the server writes it: labs (on whichever game owns them) send each finished unit to the server
    // (FH_ResearchAdd), and the server adds them up and writes the key at most every 3 s. So two labs owned by two players never
    // overwrite each other's units. When the count reaches the tier's cost the server unlocks the tier (Tech.Unlock: key "fh_tier<n>")
    // and tells everyone (FH_Unlocked: a message and a chime).
    internal static class Research
    {
        internal static readonly string[] Packs={"FH_SciRed","FH_SciGreen","FH_SciBlue","FH_SciPurple","FH_SciYellow"};
        private static readonly int[] BaseUnits={0,30,75,150,250,500};
        internal static ConfigEntry<float> CostScale,Seconds;

        private static readonly Dictionary<int,int> Pending=new Dictionary<int,int>();   // (server: units come in, not yet written)
        private static float _flushAt;

        internal static void Bind(ConfigFile config)
        {
            CostScale=config.Bind("4 - Science","Research cost",1f,new ConfigDescription("Multiplies how many units each tier's research takes (the server's value counts).",new AcceptableValueRange<float>(0.01f,20f)));
            Seconds=config.Bind("4 - Science","Research seconds",10f,new ConfigDescription("Seconds a powered lab takes per unit (unpowered: twice as long).",new AcceptableValueRange<float>(0.5f,600f)));
        }

        internal static bool IsPack(string item)=>System.Array.IndexOf(Packs,item)>=0;
        // The tier being researched now, or 0 when everything is.
        internal static int Current=>Tech.Level<Tech.Top?Tech.Level+1:0;
        internal static IEnumerable<string> PacksFor(int tier)=>Packs.Take(Mathf.Clamp(tier,0,Packs.Length));
        internal static int Cost(int tier)=>tier<=0||tier>Tech.Top?0:Mathf.Max(1,Mathf.RoundToInt(BaseUnits[tier]*(CostScale?.Value??1f)));
        internal static string Key(int tier)=>"fh_progress"+tier;

        // Units done for a tier (what the server has written; the server also counts what it hasn't written yet).
        internal static int Progress(int tier)
        {
            int n=0;
            if(ZoneSystem.instance!=null&&ZoneSystem.instance.GetGlobalKey(Key(tier),out string v))int.TryParse(v,out n);
            if(IsServer&&Pending.TryGetValue(tier,out int p))n+=p;
            if(Tech.Unlocked(tier))n=Cost(tier);
            return n;
        }
        internal static float Fraction(int tier){int c=Cost(tier);return c<=0?1f:Mathf.Clamp01(Progress(tier)/(float)c);}

        private static bool IsServer=>ZNet.instance!=null&&ZNet.instance.IsServer();

        internal static void Start()
        {
            Net.Register("FH_ResearchAdd",(sender,pkg)=>{if(IsServer)Collect(pkg.ReadInt(),pkg.ReadInt());},skipOwn:false);
            Net.Register("FH_Unlocked",(sender,pkg)=>Announce(pkg.ReadInt()),skipOwn:false);
        }
        internal static void Stop(){Pending.Clear();}

        // A lab finished units of a tier.
        internal static void Add(int tier,int units)
        {
            if(units<=0||ZNet.instance==null)return;
            if(IsServer){Collect(tier,units);return;}
            ZNetPeer server=ZNet.instance.GetServerPeer();
            if(server==null||ZRoutedRpc.instance==null)return;
            var pkg=new ZPackage();pkg.Write(tier);pkg.Write(units);
            ZRoutedRpc.instance.InvokeRoutedRPC(server.m_uid,"FH_ResearchAdd",pkg);
        }
        private static void Collect(int tier,int units)
        {
            if(tier!=Current||units<=0)return;   // (units for a tier finished meanwhile are dropped: their packs were that tier's)
            Pending[tier]=(Pending.TryGetValue(tier,out int p)?p:0)+units;
        }

        // Server, every frame: writes what came in, at most every 3 s; unlocks a finished tier at once.
        internal static void Tick()
        {
            if(!IsServer||Pending.Count==0||ZoneSystem.instance==null)return;
            int tier=Current;
            if(tier==0||!Pending.ContainsKey(tier)){Pending.Clear();return;}
            int total=Progress(tier);   // (written + pending)
            if(total>=Cost(tier))
            {
                Pending.Clear();
                ZoneSystem.instance.RemoveGlobalKey(Key(tier));
                Tech.Unlock(tier);
                var pkg=new ZPackage();pkg.Write(tier);
                Net.Everyone("FH_Unlocked",pkg);
                return;
            }
            if(Time.unscaledTime<_flushAt)return;
            _flushAt=Time.unscaledTime+3f;
            Pending.Clear();
            // (remove first: a global key with a value is replaced by name, but an old copy left beside the new one would read wrong)
            ZoneSystem.instance.RemoveGlobalKey(Key(tier));
            ZoneSystem.instance.SetGlobalKey(Key(tier)+" "+total);
        }

        // Everyone: the news and a chime.
        private static void Announce(int tier)
        {
            if(tier<1||tier>Tech.Top)return;
            Player p=Player.m_localPlayer;
            if(p==null)return;
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,$"Research complete: {Tech.Names[tier]}");
            Sound.Play(Chime(),p.transform.position,(Plugin.Volume?.Value??0.7f)*0.8f,1f,60f,p.transform);
        }
        internal static AudioClip Chime()=>Sound.Make("research chime",2.2f,t=>
        {
            // three rising bell notes, each a fundamental and a bright overtone ringing down
            float s=0f;float[] at={0f,0.18f,0.36f};float[] f={523.25f,659.25f,783.99f};
            for(int i=0;i<3;i++){float u=t-at[i];if(u<0)continue;float e=Mathf.Exp(-u*2.6f);s+=e*(0.5f*Sound.Sine(f[i]*u)+0.18f*Sound.Sine(f[i]*2.76f*u)*Mathf.Exp(-u*5f));}
            return s*0.45f;
        });
    }
}
