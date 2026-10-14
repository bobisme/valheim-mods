using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // Sounds made in code (no sound files): a clip from a wave function, played at a place or looped on a part. Through the game's
    // sound-effect channel, so its volume slider and pause apply. The clip is filled through AudioClip.Create's reader callback, as
    // Delivery's horn is: SetData's span overload can't be called from net48.
    internal static class Sound
    {
        internal const int Rate=44100;
        private static readonly Dictionary<string,AudioClip> Made=new Dictionary<string,AudioClip>();
        private static AudioMixerGroup _mixer;
        private static readonly System.Random Rng=new System.Random(1234);

        // A clip of that many seconds, sample(t) giving -1..1 at t seconds. Made once per name.
        internal static AudioClip Make(string name,float seconds,Func<float,float> sample)
        {
            if(Made.TryGetValue(name,out AudioClip c)&&c!=null)return c;
            int n=Mathf.Max(1,(int)(seconds*Rate));
            var data=new float[n];
            for(int i=0;i<n;i++)data[i]=Mathf.Clamp(sample(i/(float)Rate),-1f,1f);
            int read=0;
            c=AudioClip.Create("WrongUniverse "+name,n,1,Rate,false,buffer=>{int k=Math.Min(buffer.Length,data.Length-read);Array.Copy(data,read,buffer,0,k);if(k<buffer.Length)Array.Clear(buffer,k,buffer.Length-k);read+=k;});
            c.hideFlags=HideFlags.HideAndDontSave;
            Made[name]=c;
            return c;
        }
        internal static void Clear()
        {
            foreach(AudioClip c in Made.Values)if(c!=null)Object.Destroy(c);
            Made.Clear();_mixer=null;
        }

        // Once, at a place (or following a part). range: heard up to there.
        internal static AudioSource Play(AudioClip clip,Vector3 at,float volume=1f,float pitch=1f,float range=40f,Transform follow=null)
        {
            if(clip==null)return null;
            var go=new GameObject("WrongUniverseSound");
            if(follow!=null)go.transform.SetParent(follow,false);else go.transform.position=at;
            AudioSource s=Source(go,range);
            s.clip=clip;s.volume=volume*Plugin.Volume.Value;s.pitch=pitch;
            s.Play();
            Object.Destroy(go,clip.length/Mathf.Max(0.05f,Mathf.Abs(pitch))+0.3f);
            return s;
        }
        // Looping on a part, silent until you set its volume (and it follows the part). Destroyed with the part.
        internal static AudioSource Loop(AudioClip clip,Transform on,float range=30f)
        {
            if(clip==null||on==null)return null;
            var go=new GameObject("WrongUniverseLoop");
            go.transform.SetParent(on,false);
            AudioSource s=Source(go,range);
            s.clip=clip;s.loop=true;s.volume=0f;
            s.time=UnityEngine.Random.Range(0f,clip.length);
            s.Play();
            return s;
        }
        internal static AudioSource Source(GameObject go,float range)
        {
            AudioSource s=go.AddComponent<AudioSource>();
            s.outputAudioMixerGroup=Mixer();
            s.spatialBlend=1f;s.dopplerLevel=0f;
            s.rolloffMode=AudioRolloffMode.Linear;s.minDistance=Mathf.Min(4f,range*0.2f);s.maxDistance=range;
            return s;
        }
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

        // Pieces to make waves from.
        internal static float Noise()=>(float)(Rng.NextDouble()*2.0-1.0);
        internal static float Sine(float phase)=>Mathf.Sin(phase*2f*Mathf.PI);
        internal static float Saw(float phase)=>2f*(phase-Mathf.Floor(phase+0.5f));
        internal static float Square(float phase)=>phase-Mathf.Floor(phase)<0.5f?1f:-1f;
        // Up over attack seconds, then down to nothing by the end (length).
        internal static float Env(float t,float attack,float length)=>t<attack?t/attack:Mathf.Clamp01(1f-(t-attack)/Mathf.Max(0.001f,length-attack));
        // A sweep from f0 to f1 Hz over seconds: the phase at t (exponential, as a falling "pew" sounds right).
        internal static float SweepPhase(float t,float f0,float f1,float seconds)
        {
            float k=Mathf.Log(f1/f0)/seconds;
            return Mathf.Abs(k)<1e-4f?f0*t:f0*(Mathf.Exp(k*t)-1f)/k;
        }
    }
}
