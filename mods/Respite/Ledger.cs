using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace Respite
{
    // What the host tells every game: the raids it knows, and its rules.
    internal sealed class Book { public List<Raid> Raids=new List<Raid>(); public float CooldownDays,AreaRadius; public int MaxPerWeek; }

    // The raid clock. The host keeps it (saved per world in config/respite); every other game keeps the host's copy, so a mod deciding
    // on a player's game (Jarldom's colony) asks the same clock. Raids told on a player's game go to the host.
    internal static class Ledger
    {
        private const string Raided_="bob_respite_raided_v1",All="bob_respite_all_v1",Hello="bob_respite_hello_v1",Show="bob_respite_show_v1";
        private static readonly Dictionary<string,object> Handlers=new Dictionary<string,object>();
        private static ZRoutedRpc _rpc;
        private static ZNet _znet;
        private static List<Raid> _raids=new List<Raid>();
        private static Book _host;        // the host's rules, once it has told them
        private static string _path;
        private static bool _hello;

        internal static double Now=>ZNet.instance.GetTimeSeconds();
        internal static double Day=>Math.Max(60,EnvMan.instance!=null?EnvMan.instance.m_dayLengthSec:1800);
        private static bool Host=>ZNet.instance!=null&&ZNet.instance.IsServer();
        private static float CooldownDays=>_host!=null&&!Host?_host.CooldownDays:Plugin.Instance.CooldownDays.Value;
        private static float AreaRadius=>_host!=null&&!Host?_host.AreaRadius:Plugin.Instance.AreaRadius.Value;
        private static int MaxPerWeek=>_host!=null&&!Host?_host.MaxPerWeek:Plugin.Instance.MaxPerWeek.Value;
        internal static IReadOnlyList<Raid> Raids=>_raids;

        internal static void Tick()
        {
            if(ZNet.instance!=_znet){_znet=ZNet.instance;_raids=new List<Raid>();_host=null;_path=null;_hello=false;}
            if(_rpc!=ZRoutedRpc.instance){Unregister();Register();}
            if(_znet==null||_rpc==null||ZoneSystem.instance==null||EnvMan.instance==null)return;
            if(Host){if(_path==null)Load();}
            else if(!_hello&&Player.m_localPlayer!=null){_hello=true;_rpc.InvokeRoutedRPC(Hello);}
        }

        // Why no raid may come near a place now ("" if one may).
        internal static string Blocked(Vector3 at)=>ZNet.instance==null?"":
            Policy.Blocked(_raids,Now,at.x,at.z,AreaRadius,CooldownDays*Day,MaxPerWeek,7*Day,Day);

        // A raid came (or is coming) near a place.
        internal static void Note(Vector3 at,string source)
        {
            if(ZNet.instance==null)return;
            var raid=new Raid{T=Now,X=at.x,Z=at.z,Src=source??""};
            bool fresh=Policy.Add(_raids,raid,8*Day);
            if(Host){if(fresh){Plugin.Log($"Raid noted near {at.x:F0},{at.z:F0}: {raid.Src}");Save();Broadcast();}}
            else _rpc?.InvokeRoutedRPC(Raided_,at,raid.Src);
        }

        private static void Register()
        {
            _rpc=ZRoutedRpc.instance;if(_rpc==null)return;
            _rpc.Register<Vector3,string>(Raided_,(s,at,src)=>{if(Host)Note(at,src);});
            _rpc.Register<string>(All,(s,json)=>
            {
                if(Host)return;
                try{_host=JsonConvert.DeserializeObject<Book>(json);_raids=_host?.Raids??new List<Raid>();}
                catch(Exception e){Plugin.Log("Respite: the host's raid clock was unreadable: "+e.Message);}
            });
            _rpc.Register(Hello,s=>{if(Host)Broadcast();});
            _rpc.Register<Vector3,Vector3,float,string>(Show,(s,spot,home,seconds,text)=>Muster.Show(spot,home,seconds,text));
            var table=AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) as IDictionary;
            foreach(string name in new[]{Raided_,All,Hello,Show})Handlers[name]=table?[name.GetStableHashCode()];
        }
        internal static void Unregister()
        {
            if(_rpc!=null&&AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) is IDictionary table)
                foreach(var entry in Handlers)
                    if(ReferenceEquals(table[entry.Key.GetStableHashCode()],entry.Value))table.Remove(entry.Key.GetStableHashCode()); // only our own handlers
            Handlers.Clear();_rpc=null;
        }
        // Tells every game where raiders gather (each shows it if its player is near).
        internal static void Announce(Vector3 spot,Vector3 home,float seconds,string text)
        {
            if(_rpc!=null)_rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody,Show,spot,home,seconds,text??"");
            else Muster.Show(spot,home,seconds,text);
        }

        private static void Broadcast()
        {
            if(_rpc==null)return;
            var book=new Book{Raids=_raids,CooldownDays=Plugin.Instance.CooldownDays.Value,AreaRadius=Plugin.Instance.AreaRadius.Value,MaxPerWeek=Plugin.Instance.MaxPerWeek.Value};
            _rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody,All,JsonConvert.SerializeObject(book));
        }
        private static void Load()
        {
            string world=ZNet.instance.GetWorldName()+"-"+ZNet.instance.GetWorldUID();
            string safe=new string(world.Select(c=>char.IsLetterOrDigit(c)||c=='-'?c:'_').ToArray());
            _path=Path.Combine(Paths.ConfigPath,"respite",safe+".json");
            try{_raids=File.Exists(_path)?JsonConvert.DeserializeObject<List<Raid>>(File.ReadAllText(_path))??new List<Raid>():new List<Raid>();}
            catch(Exception e){Plugin.Log("Could not read "+_path+": "+e.Message);_raids=new List<Raid>();}
            Broadcast();
        }
        private static void Save()
        {
            if(_path==null)return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path+".tmp",JsonConvert.SerializeObject(_raids,Formatting.Indented));
                if(File.Exists(_path))File.Delete(_path);
                File.Move(_path+".tmp",_path);
            }
            catch(Exception e){Plugin.Log("Could not save "+_path+": "+e.Message);}
        }
    }
}
