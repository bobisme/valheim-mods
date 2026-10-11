using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Wayfinder
{
    // The game draws each stretch of weather from the clock: every period (EnvMan.m_environmentDuration seconds) it seeds the
    // random numbers with the period's number and picks from the land's weathers by weight; the wind is drawn the same way.
    // So what the primstav foretells is what comes, unless a raid, a boss or a god's power brings its own weather.
    internal static class Weather
    {
        private static readonly System.Reflection.MethodInfo Pick=AccessTools.Method(typeof(EnvMan),"SelectWeightedEnvironment");

        internal static long Period(out double secondsLeft)
        {
            EnvMan env=EnvMan.instance;
            double now=ZNet.instance!=null?ZNet.instance.GetTimeSeconds():0;
            long length=env!=null?System.Math.Max(1,env.m_environmentDuration):1;
            long period=(long)now/length;
            secondsLeft=(period+1)*length-now;
            return period;
        }

        // The weather in a period to come, over the land at a point (null if it cannot be told).
        internal static EnvSetup Foretell(Vector3 at,long period)
        {
            EnvMan env=EnvMan.instance;
            if(env==null||WorldGenerator.instance==null||Pick==null)return null;
            Random.State state=Random.state;
            try
            {
                Random.InitState((int)period);
                List<EnvEntry> available=env.GetAvailableEnvironments(WorldGenerator.instance.GetBiomeSector(at));
                if(available==null||available.Count==0)return null;
                var copy=new List<EnvEntry>(available); // (the game's own list is reused)
                EnvSetup chosen=(EnvSetup)Pick.Invoke(env,new object[]{copy});
                bool ash=WorldGenerator.IsAshlands(at.x,at.z),north=WorldGenerator.IsDeepnorth(at.x,at.y);
                foreach(EnvEntry e in copy){if(e.m_ashlandsOverride&&ash)chosen=e.m_env;if(e.m_deepnorthOverride&&north)chosen=e.m_env;}
                return chosen;
            }
            catch{return null;}
            finally{Random.state=state;}
        }

        // The wind at a time to come, as the game would blow it (not counting Moder's power): direction it blows to, and strength.
        internal static void Wind(long timeSec,EnvSetup weather,out Vector3 toward,out float intensity)
        {
            EnvMan env=EnvMan.instance;
            long period=env!=null?env.m_windPeriodDuration:10;
            Random.State state=Random.state;
            float angle=0;intensity=0.5f;
            try
            {
                foreach(int octave in new[]{1,2,4,8})
                {
                    Random.InitState((int)(timeSec/(period/octave)));
                    angle+=Random.value*(Mathf.PI*2f/octave);
                    intensity+=-0.5f/octave+Random.value/octave;
                }
            }
            finally{Random.state=state;}
            toward=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
            intensity=Mathf.Clamp(Mathf.Lerp(weather!=null?weather.m_windMin:0,weather!=null?weather.m_windMax:1,intensity),0.05f,1);
        }

        internal static bool IsOvercast(EnvSetup e)=>e==null||Policy.Overcast(e.m_name,e.m_isWet,e.m_alwaysDark,e.m_fogDensityDay);

        // What the sky gives a navigator now, where they stand.
        internal static Policy.Sky SkyNow(Player me,bool hasSunstone)
        {
            EnvMan env=EnvMan.instance;
            if(env==null||me==null)return Policy.Sky.Dark;
            bool indoors=me.InInterior()||me.transform.position.y>3000;
            bool day=EnvMan.IsDay();
            return Policy.Read(day,IsOvercast(env.GetCurrentEnvironment()),hasSunstone,indoors);
        }

        // A line for the primstav: this weather and the next two.
        internal static string Forecast(Vector3 at)
        {
            if(EnvMan.instance==null||ZNet.instance==null)return "";
            long period=Period(out double left);
            EnvSetup now=EnvMan.instance.GetCurrentEnvironment(),next=Foretell(at,period+1),after=Foretell(at,period+2);
            if(now==null||next==null)return "";
            long length=EnvMan.instance.m_environmentDuration;
            string text=Policy.Weather(now.m_name);
            string n=Policy.Weather(next.m_name);
            if(n==text&&after!=null&&Policy.Weather(after.m_name)!=text)
                return $"{Cap(text)}; {Policy.Weather(after.m_name)} in {Policy.Duration(left+length)}";
            if(n==text)return $"{Cap(text)}, and so it stays a while";
            string tail=after!=null&&Policy.Weather(after.m_name)!=n?$", then {Policy.Weather(after.m_name)}":"";
            return $"{Cap(text)}; {n} in {Policy.Duration(left)}{tail}";
        }
        internal static string WindNow()
        {
            EnvMan env=EnvMan.instance;
            if(env==null)return "";
            Vector3 w=env.GetWindDir();
            return $"{Cap(Policy.WindStrength(env.GetWindIntensity()))} from the {Policy.WindFrom(Policy.Bearing(w.x,w.z))}";
        }
        internal static string Cap(string s)=>string.IsNullOrEmpty(s)?s:char.ToUpperInvariant(s[0])+s.Substring(1);
    }
}
