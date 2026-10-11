using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace LocalPortals
{
    // What the mirrors sound like. A linked mirror's glass hums low (the game's own portal hum, slowed); stepping through is
    // a rush of air; now and then its crystals chime. And the far side is heard through the glass: a sound made near the
    // other mirror comes out behind this one, as far behind it as it was in front of the other; and when the other side is
    // another kind of land, its air (birds, wind, surf) murmurs at the glass.
    internal static class Sound
    {
        private const string WhooshName="sfx_OpenPortal",ChimeName="sfx_build_hammer_crystal";
        private static bool _looked;
        private static AudioClip _hum;
        private static AudioMixerGroup _sfx;
        private static GameObject _whoosh,_chime;
        internal static int Relayed;            // far sounds heard through a mirror, this session
        internal static string Found=>$"hum {(_hum!=null?_hum.name+" "+_hum.length.ToString("0.0")+" s":"none")}, whoosh {(_whoosh!=null?"yes":"none")}, chime {(_chime!=null?"yes":"none")}";

        private static void Look()
        {
            if(_looked||ZNetScene.instance==null)return;
            _looked=true;
            AudioSource hum=ZNetScene.instance.GetPrefab("portal_wood")?.transform.Find("_target_found_red/SFX")?.GetComponent<AudioSource>();
            if(hum!=null){_hum=hum.clip;_sfx=hum.outputAudioMixerGroup;}
            _whoosh=ZNetScene.instance.GetPrefab(WhooshName);
            _chime=ZNetScene.instance.GetPrefab(ChimeName);
        }
        private static Vector3 Listener()
        {
            AudioListener l=AudioMan.instance!=null?AudioMan.instance.GetActiveAudioListener():null;
            if(l!=null)return l.transform.position;
            return GameCamera.instance!=null?GameCamera.instance.transform.position:Vector3.zero;
        }

        // A one-off sound from the game's effects, softer or higher than it is.
        private static void Play(GameObject prefab,Vector3 at,float volume,float pitch)
        {
            if(prefab==null)return;
            GameObject go=Object.Instantiate(prefab,at,Quaternion.identity);
            ZSFX sfx=go.GetComponent<ZSFX>();
            if(sfx!=null){sfx.SetVolumeModifier(volume);sfx.SetPitchModifier(pitch);}
        }
        // Someone stepped out of a mirror here.
        internal static void Crossed(Vector3 at,bool player)
        {
            if(!Plugin.Sounds.Value)return;
            Look();
            Play(_whoosh,at+Vector3.up,player?0.55f:0.35f,Random.Range(1.05f,1.2f));
        }
        // A sleeping mirror waking.
        internal static void Woke(Vector3 at)
        {
            if(!Plugin.Sounds.Value)return;
            Look();
            Play(_whoosh,at+Vector3.up*1.2f,0.8f,0.7f);
            Play(_chime,at+Vector3.up*2.2f,0.6f,1.5f);
        }

        // A sound starting near the far mirror of a mirror the listener stands at is moved through the glass to them.
        private static readonly AccessTools.FieldRef<ZSFX,AudioSource> Source=AccessTools.FieldRefAccess<ZSFX,AudioSource>("m_audioSource");
        internal static void Relay(ZSFX sfx)
        {
            if(LocalPortal.Live.Count<2||sfx==null||sfx.transform.parent!=null||!Plugin.Sounds.Value)return; // (one carried by a creature stays with it)
            AudioSource source=Source(sfx);
            if(source==null||source.loop)return;
            Vector3 ear=Listener(),at=sfx.transform.position;
            if((at-ear).sqrMagnitude<35*35)return; // heard as it is
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||p.Partner==null||p.Asleep||(p.transform.position-ear).sqrMagnitude>16*16)continue;
                if(p.transform.InverseTransformPoint(ear).z<0)continue;
                Vector3 l=p.Partner.transform.InverseTransformPoint(at);
                if(l.z<0.05f||l.sqrMagnitude>20*20)continue;
                sfx.transform.position=Crossing.Point(p.Partner.transform,p.transform,at);
                sfx.SetVolumeModifier(sfx.GetVolumeModifier()*0.8f);
                Relayed++;
                return;
            }
        }

        // Each mirror's own voices: its hum, the far side's air, and its crystals.
        internal sealed class Voice
        {
            private readonly LocalPortal _p;
            private readonly GameObject _holder;
            private readonly AudioSource _hum,_air;
            private float _nextLook,_nextChime,_nextCall,_airVolume;
            private BiomeAmbient _far;
            internal string FarLand=>_far?.Name??"";

            internal Voice(LocalPortal p)
            {
                _p=p;
                Look();
                _holder=new GameObject("Voices");
                _holder.transform.SetParent(p.transform,false);
                _hum=Source(_holder,"Hum",new Vector3(0,1.3f,0.05f),_sfx,1.5f,9f);
                _hum.clip=Sound._hum;
                _hum.loop=true;
                _air=Source(_holder,"Air",new Vector3(0,1.4f,-0.4f),AudioMan.instance!=null?AudioMan.instance.m_ambientMixer:_sfx,1.5f,10f);
                _air.loop=true;
                _nextChime=Time.time+Random.Range(8f,25f);
                _nextCall=Time.time+Random.Range(3f,9f);
            }
            private static AudioSource Source(GameObject holder,string name,Vector3 at,AudioMixerGroup mixer,float near,float far)
            {
                var go=new GameObject(name);
                go.transform.SetParent(holder.transform,false);
                go.transform.localPosition=at;
                var s=go.AddComponent<AudioSource>();
                s.playOnAwake=false;s.spatialBlend=1;s.rolloffMode=AudioRolloffMode.Linear;s.minDistance=near;s.maxDistance=far;
                s.dopplerLevel=0;s.volume=0;s.outputAudioMixerGroup=mixer;
                return s;
            }
            internal void Destroy(){if(_holder!=null)Object.Destroy(_holder);}

            internal void Tick(bool linked)
            {
                if(_holder==null)return;
                bool on=Plugin.Sounds.Value;
                Vector3 ear=Listener();
                float d=Vector3.Distance(ear,_p.transform.position);
                if(!on||d>14){Quiet(_hum);Quiet(_air);return;}
                // the hum: only a linked, waking mirror
                float hum=linked?0.32f:0;
                _hum.pitch=_p.Colour==1?0.83f:0.76f;
                Fade(_hum,hum,1.2f);
                // the far side's land, when it is not this land
                if(Time.time>=_nextLook){_nextLook=Time.time+1.5f;_far=linked?FarAir():null;}
                if(_far!=null&&_far.Loop!=null)
                {
                    if(_air.clip!=_far.Loop){_air.clip=_far.Loop;_air.time=Random.value*Mathf.Max(0,_far.Loop.length-1);}
                    Fade(_air,_far.Volume,1.5f);
                }
                else Fade(_air,0,1.5f);
                // now and then, a call from out there (a bird, a howl) and a chime from the crystals
                if(_far!=null&&Time.time>=_nextCall&&d<12)
                {
                    _nextCall=Time.time+Random.Range(5f,13f);
                    AudioClip call=_far.Call();
                    if(call!=null)_air.PlayOneShot(call,0.45f);
                }
                if(linked&&Time.time>=_nextChime&&d<10)
                {
                    _nextChime=Time.time+Random.Range(18f,45f);
                    float[] notes={1f,1.125f,1.25f,1.5f,1.667f,2f}; // a pentatonic scale: never out of tune with itself
                    Play(_chime,_p.transform.TransformPoint(new Vector3(0.55f,2.6f,0.1f)),0.12f,1.3f*notes[Random.Range(0,notes.Length)]);
                }
            }
            private static void Quiet(AudioSource s){if(s!=null&&s.isPlaying){s.volume=0;s.Stop();}}
            private static void Fade(AudioSource s,float target,float time)
            {
                if(s==null||s.clip==null)return;
                s.volume=Mathf.MoveTowards(s.volume,target,Time.deltaTime/time);
                if(s.volume>0.001f&&!s.isPlaying){s.time=Random.value*Mathf.Max(0,s.clip.length-0.5f);s.Play();}
                else if(s.volume<=0.001f&&s.isPlaying)s.Stop();
            }
            // The land round the far mirror, if it is another kind than round this one (out of doors, both).
            private BiomeAmbient FarAir()
            {
                if(WorldGenerator.instance==null)return null;
                Vector3 here=_p.transform.position,there;
                if(_p.Partner!=null)there=_p.Partner.transform.position;
                else
                {
                    ZDOID id=_p.PartnerId();
                    ZDO zdo=id.IsNone()?null:ZDOMan.instance.GetZDO(id);
                    if(zdo==null)return null;
                    there=zdo.GetPosition();
                }
                if(here.y>3000||there.y>3000)return null; // (dungeons are up in the sky: their air is their own)
                Heightmap.Biome a=WorldGenerator.instance.GetBiome(here),b=WorldGenerator.instance.GetBiome(there);
                if(a==b)return null;
                return BiomeAmbient.Of(b);
            }
        }

        // A kind of land's air: its weather's loop and its random calls, from the game's own lists.
        internal sealed class BiomeAmbient
        {
            internal string Name;
            internal AudioClip Loop;
            internal float Volume;
            private List<AudioClip> _any,_day,_night;
            private static readonly Dictionary<Heightmap.Biome,BiomeAmbient> Known=new Dictionary<Heightmap.Biome,BiomeAmbient>();
            internal static BiomeAmbient Of(Heightmap.Biome biome)
            {
                if(Known.TryGetValue(biome,out BiomeAmbient a))return a;
                a=new BiomeAmbient{Name=biome.ToString()};
                EnvSetup env=null;
                if(EnvMan.instance!=null)
                    foreach(BiomeEnvSetup b in EnvMan.instance.m_biomes)
                        if(b.m_biome==biome&&b.m_environments.Count>0){env=b.m_environments[0].m_env;break;}
                if(env!=null){a.Loop=env.m_ambientLoop;a.Volume=Mathf.Clamp(env.m_ambientVol,0.05f,0.5f)*0.8f;}
                if(AudioMan.instance!=null)
                    foreach(AudioMan.BiomeAmbients amb in AudioMan.instance.m_randomAmbients)
                    {
                        bool match=env!=null&&env.m_ambientList.Length>0?amb.m_name==env.m_ambientList:(amb.m_biome&biome)!=0;
                        if(!match)continue;
                        a._any=amb.m_randomAmbientClips;a._day=amb.m_randomAmbientClipsDay;a._night=amb.m_randomAmbientClipsNight;
                        break;
                    }
                Known[biome]=a;
                return a;
            }
            internal AudioClip Call()
            {
                var pool=new List<AudioClip>();
                if(_any!=null)pool.AddRange(_any);
                bool day=EnvMan.instance==null||EnvMan.IsDay();
                List<AudioClip> time=day?_day:_night;
                if(time!=null)pool.AddRange(time);
                pool.RemoveAll(c=>c==null);
                return pool.Count>0?pool[Random.Range(0,pool.Count)]:null;
            }
            internal static void Forget()=>Known.Clear();
        }

        internal static void Stop(){_looked=false;_hum=null;_whoosh=_chime=null;BiomeAmbient.Forget();}
    }

    [HarmonyPatch(typeof(ZSFX),"Start")]
    internal static class HearThroughGlass
    {
        private static void Prefix(ZSFX __instance)=>Sound.Relay(__instance);
    }
}
