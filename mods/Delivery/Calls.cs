using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Delivery
{
    // The horn's voice, made here: a goat horn's (see Synth), and the slap of an echo. Calls are queued so they never blur together (the courier
    // has to tell them apart), and everyone near hears yours.
    internal static class Calls
    {
        private const int Rate=44100;
        private const string Rpc="BobDelivery_Call";
        private static readonly Dictionary<string,AudioClip> Made=new Dictionary<string,AudioClip>();
        private static readonly Queue<(Note[] tune,Vector3? at,bool far)> Waiting=new Queue<(Note[],Vector3?,bool)>();
        private static float _busyUntil;
        private static AudioMixerGroup _mixer;
        private static ZRoutedRpc _registered;


        // Your own call: you hear it, and so does everyone within earshot.
        internal static void Blow(Note[] tune)
        {
            Waiting.Enqueue((tune,null,false));
            Player me=Player.m_localPlayer;
            if(me!=null&&ZRoutedRpc.instance!=null)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody,Rpc,me.transform.position,Policy.Key(tune),me.GetPlayerID());
        }
        // A horn somewhere else (the courier's answer, far off).
        internal static void From(Vector3 at,Note[] tune,bool far)=>Waiting.Enqueue((tune,at,far));

        internal static void Tick()
        {
            Register();
            if(Waiting.Count==0||Time.time<_busyUntil)return;
            var (tune,at,far)=Waiting.Dequeue();
            Player me=Player.m_localPlayer;
            if(me==null&&at==null)return;
            Vector3 where=at??me.transform.position+Vector3.up*1.6f;
            Play(where,tune,far,at==null);
            if(at==null&&me!=null)me.GetComponent<ZSyncAnimation>()?.SetTrigger("eat"); // (the horn to your lips: the drinking motion, seen by everyone)
            _busyUntil=Time.time+(float)Policy.Length(tune)+0.35f;
        }
        private static void Register()
        {
            if(ZRoutedRpc.instance==null||_registered==ZRoutedRpc.instance)return;
            // (an earlier copy of the mod, before a reload, registered it already: its handler goes, ours comes)
            var functions=HarmonyLib.AccessTools.Field(typeof(ZRoutedRpc),"m_functions")?.GetValue(ZRoutedRpc.instance) as System.Collections.IDictionary;
            functions?.Remove(Rpc.GetStableHashCode());
            ZRoutedRpc.instance.Register<Vector3,string,long>(Rpc,Heard);
            _registered=ZRoutedRpc.instance;
        }
        internal static void Reset(){_registered=null;Waiting.Clear();_busyUntil=0;}
        private static void Heard(long sender,Vector3 at,string key,long playerId)
        {
            Player me=Player.m_localPlayer;
            if(me==null||playerId==me.GetPlayerID())return; // (your own you already heard)
            if(Vector3.Distance(me.transform.position,at)>Plugin.Earshot.Value)return;
            Note[] tune=Policy.Parse(key);
            if(tune.Length>0)Play(at+Vector3.up*1.6f,tune,false,false);
        }

        private static void Play(Vector3 at,Note[] tune,bool far,bool mine)
        {
            AudioClip clip=Clip(tune,far);
            var go=new GameObject("DeliveryHorn");
            go.transform.position=at;
            var src=go.AddComponent<AudioSource>();
            src.clip=clip;
            src.outputAudioMixerGroup=Mixer();
            src.spatialBlend=mine?0.6f:1f;
            src.rolloffMode=AudioRolloffMode.Linear;
            src.minDistance=far?40f:8f;src.maxDistance=far?Plugin.CourierDistance.Value*2.2f:Plugin.Earshot.Value;
            src.volume=Plugin.Volume.Value*(far?0.75f:1f);
            src.dopplerLevel=0;
            src.Play();
            Object.Destroy(go,clip.length+0.5f);
        }
        // The game's own sound-effect channel (so the game's volume slider and pause apply), from one of its sounds.
        private static readonly string[] SoundsLike={"sfx_coins_placed","sfx_build_hammer_wood","sfx_chest_open"};
        private static AudioMixerGroup Mixer()
        {
            if(_mixer!=null)return _mixer;
            foreach(string name in SoundsLike)
            {
                AudioSource s=ZNetScene.instance?.GetPrefab(name)?.GetComponentInChildren<AudioSource>(true);
                if(s!=null&&s.outputAudioMixerGroup!=null)return _mixer=s.outputAudioMixerGroup;
            }
            return AudioMan.instance!=null?AudioMan.instance.m_ambientMixer:null;
        }

        internal static AudioClip Clip(Note[] tune,bool far)
        {
            string key=Policy.Key(tune)+(far?"/far":"");
            if(Made.TryGetValue(key,out AudioClip c)&&c!=null)return c;
            float[] data=Synth(tune,far);
            int read=0;
            // (filled through the reader: SetData's span overload can't be called from this framework)
            c=AudioClip.Create("DeliveryHorn "+key,data.Length,1,Rate,false,buffer=>{int n=System.Math.Min(buffer.Length,data.Length-read);System.Array.Copy(data,read,buffer,0,n);if(n<buffer.Length)System.Array.Clear(buffer,n,buffer.Length-n);read+=n;});
            c.hideFlags=HideFlags.HideAndDontSave;
            Made[key]=c;
            return c;
        }
        internal static void Stop()
        {
            if(ZRoutedRpc.instance!=null&&_registered==ZRoutedRpc.instance)
                (HarmonyLib.AccessTools.Field(typeof(ZRoutedRpc),"m_functions")?.GetValue(ZRoutedRpc.instance) as System.Collections.IDictionary)?.Remove(Rpc.GetStableHashCode());
            _registered=null;
            foreach(AudioClip c in Made.Values)if(c!=null)Object.Destroy(c);
            Made.Clear();
            Waiting.Clear();
        }

        // The sound itself: a small horn's, with a few finger-holes (five notes a little apart, not a war horn's leaps). Each note swells
        // quickly, scoops up into pitch from a little flat, wavers slowly when held, brightens as the breath builds, and has a soft
        // overblown edge and a breath of noise.
        private static readonly float[] Holes={1f,1.125f,1.26f,1.5f,1.682f}; // (the root, a second, a third, a fifth and a sixth)
        private const float Root=262f; // (middle C)
        internal static float[] Synth(Note[] tune,bool far)
        {
            float tail=far?0.9f:0.45f;
            int total=(int)((Policy.Length(tune)+tail)*Rate);
            var dry=new float[total];
            var rng=new System.Random(12345);
            int at=0;
            foreach(Note n in tune)
            {
                float len=(float)(n.Long?Policy.LongBeat:Policy.ShortBeat);
                int count=(int)(len*Rate);
                float f=Root*Holes[Mathf.Clamp(n.Partial-Policy.LowPartial,0,Holes.Length-1)];
                double phase=0;
                float noise=0;
                for(int i=0;i<count+(int)(0.07f*Rate)&&at+i<total;i++)
                {
                    float t=i/(float)Rate;
                    float env=t<0.03f?t/0.03f:t<len?1f:Mathf.Max(0,1-(t-len)/0.07f);
                    env*=env;
                    float held=Mathf.Clamp01((t-0.12f)/0.2f);
                    float bend=-0.035f*Mathf.Exp(-t/0.035f)+0.005f*held*Mathf.Sin(2*Mathf.PI*5.2f*t)-0.008f*held;
                    phase+=2*System.Math.PI*f*(1+bend)/Rate;
                    float bright=0.45f+0.55f*env;
                    float s=0;
                    for(int k=1;k<=10;k++)
                    {
                        if(f*k>9000)break;
                        float amp=Mathf.Pow(k,-(2.1f-1.1f*bright));
                        s+=amp*(float)System.Math.Sin(phase*k);
                    }
                    noise=noise*0.85f+0.15f*(float)(rng.NextDouble()*2-1);
                    s=(float)System.Math.Tanh(1.7f*s)*0.8f+noise*0.26f*env;
                    dry[at+i]+=s*env*(0.85f+0.15f*(n.Partial-2)/4f);
                }
                at+=(int)((len+Policy.Gap)*Rate);
            }
            // echoes (and from afar, the high end lost on the way)
            var wet=new float[total];
            (float delay,float gain)[] echoes=far?new[]{(0f,1f),(0.19f,0.45f),(0.41f,0.3f),(0.66f,0.18f)}:new[]{(0f,1f),(0.12f,0.22f),(0.27f,0.12f)};
            foreach(var (delay,gain) in echoes)
            {
                int d=(int)(delay*Rate);
                for(int i=0;i+d<total;i++)wet[i+d]+=dry[i]*gain;
            }
            if(far){float y=0;for(int i=0;i<total;i++){y+=0.18f*(wet[i]-y);wet[i]=y;}}
            float peak=0.0001f;foreach(float v in wet)peak=Mathf.Max(peak,Mathf.Abs(v));
            for(int i=0;i<total;i++)wet[i]*=0.85f/peak;
            return wet;
        }
    }
}
