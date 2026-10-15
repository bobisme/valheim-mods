using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Godforged
{
    // Gjallarhorn, Heimdall's horn, heard in every world. Used from the bag or hotbar it's blown (never used up): every player in the
    // world hears it (louder the nearer they are), told who blew it, and a ping marks the spot on their maps; friends within 30 m take
    // heart, Heimdall's Watch, a minute of quicker stamina. Then it rests five minutes (kept on the horn, in world time). Fimbulwinter
    // hears it through Api.OnHorn (its call at Ragnarök).
    internal sealed class Gjallarhorn:Feature
    {
        internal override string Name=>"Gjallarhorn";
        internal static readonly ItemDef Horn=new ItemDef{Prefab="GF_Gjallarhorn",Name="Gjallarhorn",Base="MeadHealthMinor",
            Description="Heimdall's horn, heard in every world. Blow it (use it), and everyone hears it and sees where you stand, and those beside you take heart.",
            Cost="HardAntler:2, Bronze:6, Silver:4",Station="forge",Level=2,Weight=2f,Stack=1,Model=t=>Meshes.AddAll(t,"gjallarhorn"),
            Stats=s=>{s.m_consumeStatusEffect=null;s.m_food=0f;s.m_foodStamina=0f;s.m_foodRegen=0f;s.m_foodEitr=0f;},Use=Blow};
        internal override IEnumerable<ItemDef> Items=>new[]{Horn};

        private const string Rpc="GF_Horn",Key="gf_horn_ready",WatchName="Godforged_SE_HeimdallsWatch";
        private static readonly int WatchHash=WatchName.GetStableHashCode();
        private static ConfigEntry<float> _cooldown,_radius,_watchSeconds,_regen;
        private static SE_Stats _watch;
        internal static readonly List<System.Action<long,Vector3>> OnHorn=new List<System.Action<long,Vector3>>();

        internal override void Bind(ConfigFile c)
        {
            const string s="6 - Gjallarhorn";
            _cooldown=c.Bind(s,"CooldownSeconds",300f,"How long it rests between blasts.");
            _radius=c.Bind(s,"WatchRadius",30f,"Friends this near take Heimdall's Watch.");
            _watchSeconds=c.Bind(s,"WatchSeconds",60f,"How long Heimdall's Watch lasts.");
            _regen=c.Bind(s,"WatchStaminaRegen",1.5f,"Heimdall's Watch: stamina comes back this many times as fast.");
        }
        internal override void Start()=>Net.Register(Rpc,(sender,pkg)=>Heard(pkg.ReadVector3(),pkg.ReadString()));
        internal override void Register(ZNetScene scene)
        {
            if(_watch==null)
            {
                _watch=GadgetEffects.Create<SE_Stats>(WatchName,"Heimdall's Watch",Horn.Shared?.m_icons?.FirstOrDefault());
                _watch.m_tooltip="Gjallarhorn sounded: stamina comes back quicker.";
                _watch.m_ttl=_watchSeconds.Value;
                _watch.m_staminaRegenMultiplier=_regen.Value;
                _watch.m_mods=new List<HitData.DamageModPair>();   // (its own list, never a game effect's)
            }
            if(_watch.m_icon==null)_watch.m_icon=Horn.Shared?.m_icons?.FirstOrDefault();
            GadgetEffects.Register();
        }
        internal override void Stop(){GadgetEffects.Unload();_watch=null;}

        private static bool Blow(Player p,ItemDrop.ItemData item)
        {
            double now=ZNet.instance!=null?ZNet.instance.GetTimeSeconds():0;
            if(item.m_customData.TryGetValue(Key,out string s)&&double.TryParse(s,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double ready)&&now<ready)
            {
                int left=Mathf.CeilToInt((float)(ready-now));
                p.Message(MessageHud.MessageType.Center,$"Gjallarhorn is still ringing ({left/60}:{left%60:00})");
                return true;
            }
            item.m_customData[Key]=(now+_cooldown.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Vector3 at=p.transform.position;
            Heard(at,p.GetPlayerName());
            var pkg=new ZPackage();pkg.Write(at);pkg.Write(p.GetPlayerName());
            Net.Others(Rpc,pkg);
            Chat.instance?.SendPing(at);
            foreach(Character c in Character.GetAllCharacters())
                if(c!=null&&!c.IsDead()&&(c==p||c is Player||c.IsTamed())&&Hits.Friendly(p,c)&&Vector3.Distance(c.transform.position,at)<=_radius.Value)
                    GadgetEffects.Apply(c,WatchHash);
            foreach(var f in OnHorn)Plugin.Safe("Api.OnHorn",()=>f(p.GetPlayerID(),at));
            return true;
        }

        // On every game: the horn's call from where it was blown, heard across the world, and who blew it.
        private static void Heard(Vector3 at,string who)
        {
            Sound.Play(Call(),at+Vector3.up*1.5f,1f,1f,1500f);
            Player me=Player.m_localPlayer;
            if(me!=null)me.Message(MessageHud.MessageType.TopLeft,$"Gjallarhorn sounds: {who} calls");
        }

        // A great horn's long call: a low note that bends up to pitch as the breath fills it, rich in overtones (fewer, the higher), a
        // little vibrato as it's held, and breath in its attack; four and a half seconds.
        internal static AudioClip Call()
        {
            float phase=0f,lo=0f;
            return Sound.Make("gjallarhorn call",4.5f,t=>
            {
                float bend=t<0.35f?0.93f+0.07f*Mathf.SmoothStep(0f,1f,t/0.35f):1f;
                float vib=t>0.9f?1f+0.006f*Mathf.Sin(2f*Mathf.PI*5f*t)*Mathf.Min(1f,(t-0.9f)/0.6f):1f;
                phase+=110f*bend*vib/Sound.Rate;
                float tone=0f;
                for(int n=1;n<=12;n++)tone+=Mathf.Sin(2f*Mathf.PI*n*phase)*Mathf.Exp(-(n-1)/3.2f)*(n%2==1?1f:0.7f);
                float env=t<0.3f?t/0.3f:t<3.3f?1f:Mathf.Exp(-(t-3.3f)*3.2f);
                float nz=Sound.Noise();lo+=(nz-lo)*0.08f;
                float breath=lo*Mathf.Max(0f,1f-t/0.6f)*0.6f;
                return (tone*0.28f+breath)*env;
            });
        }
    }
}
