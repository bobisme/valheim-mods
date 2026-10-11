using UnityEngine;

namespace Wayfinder
{
    // The lead cast ahead of the bow every few breaths while you are aboard with a sounding line: the depth under where she
    // will be in a few seconds (the shallowest of a few casts along her way), and what the tallow brings up.
    internal static class Sounding
    {
        internal static string Line="";
        internal static bool Alert;
        internal static float LastDepth=float.NaN;
        private static float _next;
        private static bool _warned;

        internal static void Tick()
        {
            Player me=Player.m_localPlayer;
            Ship ship=Ships.Aboard();
            if(me==null||ship==null||!Items.Has(me,Items.SoundingLine)||WorldGenerator.instance==null||ZoneSystem.instance==null){Line="";Alert=false;_warned=false;return;}
            if(Time.time<_next)return;
            float speed=ship.GetSpeed();
            _next=Time.time+(Mathf.Abs(speed)>3?2.5f:4f);
            Vector3 bow=ship.transform.position+ship.transform.forward*(speed>=0?4:-4);
            Vector3 way=ship.transform.forward*Mathf.Sign(speed==0?1:speed);
            float water=ZoneSystem.instance.m_waterLevel;
            float shallowest=float.MaxValue;Vector3 cast=bow;
            for(float t=0;t<=4;t+=1) // where the bow will be over the next four seconds
            {
                Vector3 p=bow+way*Mathf.Abs(speed)*t;
                float bottom=Ground(p);
                if(water-bottom<shallowest){shallowest=water-bottom;cast=p;}
            }
            LastDepth=shallowest;
            Heightmap.Biome biome=WorldGenerator.instance.GetBiome(cast);
            string depth=Policy.Depth(water,water-shallowest);
            bool deep=biome==Heightmap.Biome.Ocean;
            bool shoal=Policy.Shoal(shallowest);
            Alert=shoal;
            if(shoal)Line=$"SHOAL WATER AHEAD: {depth}, {Policy.Bottom(biome.ToString())}";
            else if(deep)Line=$"{depth}: deep sea, serpent water";
            else Line=$"{depth}, {Policy.Bottom(biome.ToString())}";
            Sounds.Play("fx_float_hitwater",new Vector3(cast.x,water,cast.z),0.5f,1f);
            if(shoal&&!_warned){me.Message(MessageHud.MessageType.Center,"The leadsman calls: shoal water ahead!");_warned=true;}
            else if(!shoal)_warned=false;
        }
        internal static float Ground(Vector3 p)
        {
            if(ZoneSystem.instance.IsZoneLoaded(p)&&ZoneSystem.instance.GetGroundHeight(p,out float h))return h;
            return WorldGenerator.instance.GetHeight(p.x,p.z);
        }
    }

    internal static class Sounds
    {
        internal static void Play(string prefab,Vector3 at,float volume,float pitch)
        {
            GameObject p=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(prefab):null;
            if(p==null)return;
            GameObject go=Object.Instantiate(p,at,Quaternion.identity);
            foreach(ZSFX sfx in go.GetComponentsInChildren<ZSFX>()){sfx.SetVolumeModifier(volume);sfx.SetPitchModifier(pitch);}
        }
        internal static void Tick(){}
        internal static void Arrive(bool end)
        {
            Player me=Player.m_localPlayer;
            if(me==null)return;
            Vector3 at=me.transform.position+Vector3.up*1.6f;
            if(end){Play("sfx_offering",at,0.7f,0.8f);return;}
            Play("sfx_build_hammer_crystal",at,0.5f,0.75f);
            Play("sfx_build_hammer_crystal",at,0.35f,1.125f);
        }
    }
}
