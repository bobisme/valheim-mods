using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Factorheim
{
    // Electricity: poles and wires, steam engines, Surtling generators, eitr reactors and accumulators, and the solver that shares
    // what they make among the machines that use it (Power/Grid.cs).
    internal sealed class Power:Feature
    {
        internal override string Name=>"Power";

        private static readonly PieceDef[] Defs=
        {
            new PieceDef{Prefab="FH_WoodPole",Name="Small power pole",Tier=1,Category=Piece.PieceCategory.Misc,Material=WearNTear.MaterialType.Wood,Health=200f,
                Description="A wooden pole with copper on its crossarm. Wires itself to other poles within 7.5 m and powers machines within 2.5 m of it.",
                Cost="Wood:2, Copper:1",Station="piece_workbench",Model=PowerModels.WoodPole,
                Setup=go=>{go.AddComponent<Pole>();SnapPoints.Add(go,"bottom",Vector3.zero);}},
            new PieceDef{Prefab="FH_IronPole",Name="Big power pole",Tier=2,Category=Piece.PieceCategory.Misc,Health=800f,
                Description="An iron lattice tower. Carries wires 30 m to the next big pole; powers only what stands within 1 m of it.",
                Cost="Iron:4, Copper:2, Stone:4",Station="forge",Model=PowerModels.IronPole,
                Setup=go=>{go.AddComponent<Pole>();SnapPoints.Add(go,"bottom",Vector3.zero);}},
            new PieceDef{Prefab="FH_SteamEngine",Name="Steam engine",Tier=1,Health=1200f,
                Description="A boiler and a steam engine on one plinth: burns wood, coal or solid fuel for up to 900 kW. Its boiler drinks: build it within 12 m of a lake or the sea. Burns fuel only as fast as the network draws power.",
                Cost="Iron:10, Copper:5, Stone:10",Station="forge",Model=PowerModels.SteamEngine,
                Setup=go=>{go.AddComponent<SteamEngine>();SnapPoints.Box(go,new Vector3(-1.7f,0,-1f),new Vector3(1.7f,0,1f));}},
            new PieceDef{Prefab="FH_Accumulator",Name="Accumulator",Tier=2,Health=600f,
                Description="Stores 5 MJ of what the generators make and nobody uses, and gives it back (up to 300 kW) when they can't keep up.",
                Cost="Iron:2, Copper:4, Resin:6",Station="forge",Model=PowerModels.Accumulator,
                Setup=go=>{go.AddComponent<Accumulator>();SnapPoints.Box(go,new Vector3(-0.8f,0,-0.6f),new Vector3(0.8f,0,0.6f));}},
            new PieceDef{Prefab="FH_SurtlingGenerator",Name="Surtling generator",Tier=3,Health=1200f,
                Description="A Surtling core burns in a glass drum: 3 MW. A core lasts two minutes at full load.",
                Cost="Iron:10, Silver:4, SurtlingCore:4",Station="forge",Model=PowerModels.Surtling,
                Setup=go=>{go.AddComponent<SurtlingGenerator>();SnapPoints.Box(go,new Vector3(-1,0,-1),new Vector3(1,0,1));}},
            new PieceDef{Prefab="FH_EitrReactor",Name="Eitr reactor",Tier=4,Health=2000f,
                Description="Eitr, refined and burned: 10 MW. One eitr lasts five minutes at full load.",
                Cost="BlackMetal:20, Silver:10, Eitr:10, Copper:10",Station="blackforge",Model=PowerModels.Reactor,
                Setup=go=>{go.AddComponent<EitrReactor>();SnapPoints.Box(go,new Vector3(-1.5f,0,-1.5f),new Vector3(1.5f,0,1.5f));}},
        };
        internal override IEnumerable<PieceDef> Pieces=>Defs;

        internal override void Tick()=>PowerGrid.Tick();
        internal override void Stop()=>PowerGrid.Stop();

        internal override string Command=>"power";
        internal override string Help=>"[near [radius]] | rebuild";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            string sub=args.Length>2?args[2]:"";
            if(sub=="rebuild"){PowerGrid.Rebuild();output(new JObject{["rebuild"]="next tick"});return null;}
            Player me=Player.m_localPlayer;
            float radius=args.Length>3&&float.TryParse(args[3],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float r)?r:40f;
            var nets=new JArray();
            foreach(PowerNet n in PowerGrid.Nets)
            {
                if(sub=="near"&&!n.Poles.Any(p=>p!=null&&Vector3.Distance(p.transform.position,me.transform.position)<=radius))continue;
                var o=new JObject{["poles"]=n.Poles.Count,["capacity"]=Machine.Watts(n.Capacity),["demand"]=Machine.Watts(n.Demand),
                    ["satisfaction"]=Math.Round(n.Satisfaction,3),["stored"]=Pole.Joules(n.Stored)+" / "+Pole.Joules(n.StoredMax),["flow"]=Machine.Watts(n.Flow),
                    ["sources"]=new JArray(n.Sources.Where(s=>s!=null).Select(s=>new JObject{["piece"]=s.Title,["at"]=Tools.V(s.transform.position),
                        ["capacity"]=Machine.Watts(s.Capacity),["load"]=Math.Round(s.Draw,3),["owner"]=s.Owner}))};
                if(sub=="near")o["machines"]=new JArray(n.Users.Where(u=>u!=null).Select(u=>new JObject{["piece"]=u.Title,["at"]=Tools.V(u.transform.position),
                    ["use"]=Machine.Watts(u.PowerUse),["wants"]=u.Wants,["powered"]=Math.Round(u.Powered,3)}));
                else o["machines"]=n.Users.Count;
                nets.Add(o);
            }
            output(new JObject{["networks"]=nets,["allNetworks"]=PowerGrid.Nets.Count,["poles"]=Live<Pole>.All.Count,["wires"]=PowerGrid.Wires,
                ["lastRebuildMs"]=Math.Round(PowerGrid.RebuildMs,2),["rebuilds"]=PowerGrid.Rebuilds});
            return null;
        }
    }
}
