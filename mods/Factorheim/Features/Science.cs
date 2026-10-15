using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Factorheim
{
    // Science: the five rune packs, the rune lab that reads them, the world's research (Research: progress kept by the server in global
    // keys) and the research window.
    internal sealed class Science:Feature
    {
        internal override string Name=>"Science";
        private ItemDef[] _items;
        private PieceDef[] _pieces;
        private Craft[] _crafts;

        internal override IEnumerable<ItemDef> Items=>_items??(_items=Packs.Items().ToArray());
        internal override IEnumerable<Craft> Crafts=>_crafts??(_crafts=Packs.Crafts().ToArray());
        internal override IEnumerable<PieceDef> Pieces=>_pieces??(_pieces=new[]
        {
            new PieceDef
            {
                Prefab="FH_RuneLab",Name="Rune lab",Tier=0,Cost="Stone:12, Wood:8, Bronze:2",Station="piece_workbench",
                Description="Reads rune packs to research the next tier for the whole world. Put packs in (or feed it with inserters); Shift+E shows the research. Works on runes alone at half speed; power brings it to full.",
                Material=WearNTear.MaterialType.Stone,Health=800f,Model=RuneLab.Model,
                Setup=go=>{go.AddComponent<RuneLab>();SnapPoints.Box(go,new Vector3(-0.85f,0f,-0.85f),new Vector3(0.85f,0f,0.85f));},
            },
        });

        internal override void Bind(ConfigFile config)=>Research.Bind(config);
        internal override void Start()=>Research.Start();
        internal override void Tick(){Research.Tick();ResearchWindow.Tick();}
        internal override void Stop()=>Research.Stop();
        internal override bool WindowOpen=>ResearchWindow.IsOpen;
        internal override void CloseWindow()=>ResearchWindow.Close();

        internal override string Command=>"research";
        internal override string Help=>"[add <units> | lab | open | unlocks <tier>]";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            string sub=args.Length>2?args[2]:"";
            int current=Research.Current;
            switch(sub)
            {
                case "":
                    output(new JObject{["level"]=Tech.Level,["current"]=current,["name"]=current>0?Tech.Names[current]:"done",
                        ["progress"]=current>0?Research.Progress(current):0,["cost"]=Research.Cost(current),
                        ["packs"]=new JArray(Research.PacksFor(current)),["server"]=ZNet.instance!=null&&ZNet.instance.IsServer(),
                        ["key"]=ZoneSystem.instance!=null&&ZoneSystem.instance.GetGlobalKey(Research.Key(current),out string v)?v:null});
                    return null;
                case "add":
                {
                    int n=args.Length>3&&int.TryParse(args[3],out int a)?a:1;
                    if(current==0){error("fh research add: everything is researched");return null;}
                    Research.Add(current,n);
                    output(new JObject{["added"]=n,["tier"]=current,["note"]="the server writes it within 3 s"});
                    return null;
                }
                case "lab":
                case "open":
                {
                    Player me=Player.m_localPlayer;
                    RuneLab lab=Live<Machine>.All.OfType<RuneLab>().Where(m=>m!=null).OrderBy(m=>Vector3.Distance(m.transform.position,me.transform.position)).FirstOrDefault();
                    if(lab==null){error("fh research "+sub+": no rune lab loaded");return null;}
                    if(sub=="open"){ResearchWindow.Open(lab);output(new JObject{["open"]=ResearchWindow.IsOpen});return null;}
                    output(new JObject{["at"]=Tools.V(lab.transform.position),["distance"]=Math.Round(Vector3.Distance(lab.transform.position,me.transform.position),1),
                        ["owner"]=lab.Owner,["powered"]=lab.Powered,["hover"]=System.Text.RegularExpressions.Regex.Replace(lab.GetHoverText(),"<.*?>","")});
                    return null;
                }
                case "unlocks":
                {
                    int t=args.Length>3&&int.TryParse(args[3],out int tt)?tt:current;
                    output(new JObject{["tier"]=t,["unlocks"]=System.Text.RegularExpressions.Regex.Replace(ResearchWindow.Unlocks(t),"<.*?>","")});
                    return null;
                }
            }
            error("fh research "+Help);
            return null;
        }
    }
}
