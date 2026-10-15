using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Factorheim
{
    // Production: what makes things. Drills on ore deposits, a woodcutter among trees, a tar pumpjack, assemblers and a chemical plant,
    // and the intermediate items they make (gears, wire, circuits, engines, plastic, solid fuel, silver circuits, eitr units).
    internal sealed class Production:Feature
    {
        internal override string Name=>"Production";
        internal static ConfigEntry<float> BurnerDrillSeconds,ElectricDrillSeconds,WoodcutterSeconds,WoodcutterReach,PumpjackSeconds;

        internal override void Bind(ConfigFile c)
        {
            const string s="4 - Production";
            BurnerDrillSeconds=c.Bind(s,"Burner drill seconds per ore",8f,"How long a burner drill takes to bring up one ore (rarer ores take longer).");
            ElectricDrillSeconds=c.Bind(s,"Electric drill seconds per ore",4f,"How long an electric drill takes to bring up one ore at full power.");
            WoodcutterSeconds=c.Bind(s,"Woodcutter seconds per wood",10f,"How long a woodcutter takes to gather one wood with four trees in reach (eight trees: twice as fast).");
            WoodcutterReach=c.Bind(s,"Woodcutter reach",8f,"How far round it a woodcutter finds trees (metres).");
            PumpjackSeconds=c.Bind(s,"Pumpjack seconds per tar",3f,"How long a pumpjack takes to pump one tar at full power.");
        }

        // ---- items ----
        private static readonly ItemDef[] ItemList=
        {
            new ItemDef{Prefab="FH_IronGear",Name="Iron gear",Description="A cogwheel filed from iron. Belts, inserters and engines turn on them.",
                Base="Coal",Cost="Iron:1",Station="forge",Amount=2,Stack=100,Weight=0.3f,Tier=0,Model=ProdModels.Gear},
            new ItemDef{Prefab="FH_CopperWire",Name="Copper wire",Description="Copper drawn thin and wound on a spool.",
                Base="Coal",Cost="Copper:1",Station="piece_workbench",Amount=3,Stack=100,Weight=0.1f,Tier=0,Model=ProdModels.Wire},
            new ItemDef{Prefab="FH_RuneCircuit",Name="Rune circuit",Description="Copper traces on a birch board around one carved rune. Machines think with it.",
                Base="Coal",Cost="FH_CopperWire:3, FineWood:1",Station="forge",Stack=100,Weight=0.2f,Tier=1,
                Model=t=>ProdModels.Circuit(t,new Color(0.55f,0.42f,0.25f),new Color(0.8f,0.45f,0.2f),new Color(0.3f,1f,0.4f))},
            new ItemDef{Prefab="FH_Engine",Name="Engine",Description="Iron pistons on a geared shaft.",
                Base="Coal",Cost="",Stack=50,Weight=2f,Tier=2,Model=ProdModels.Engine},
            new ItemDef{Prefab="FH_Plastic",Name="Black resin plastic",Description="Tar and coal cooked into a hard, shiny black.",
                Base="Coal",Cost="",Stack=100,Weight=0.2f,Tier=3,Model=t=>ProdModels.Bar(t,ProdModels.Plastic)},
            new ItemDef{Prefab="FH_SolidFuel",Name="Solid fuel",Description="Tar pressed into bricks. Burns four times as long as coal.",
                Base="Coal",Cost="",Stack=50,Weight=0.5f,Tier=3,Model=t=>ProdModels.Bar(t,ProdModels.FuelBrick,ProdModels.Ember)},
            // (by hand at tier 2 with resin in place of plastic, so blue runes can be made before tar; machines use plastic from tier 3)
            new ItemDef{Prefab="FH_SilverCircuit",Name="Silver circuit",Description="A layered circuit with silver contacts and a sealed core.",
                Base="Coal",Cost="FH_RuneCircuit:2, Resin:4, FH_CopperWire:4, Silver:1",Station="forge",Stack=100,Weight=0.3f,Tier=2,
                Model=t=>ProdModels.Circuit(t,new Color(0.12f,0.14f,0.2f),new Color(0.85f,0.87f,0.9f),new Color(0.3f,0.6f,1f))},
            new ItemDef{Prefab="FH_EitrUnit",Name="Eitr processing unit",Description="Eitr held in a lattice of silver circuits: it thinks fast.",
                Base="Coal",Cost="",Stack=100,Weight=0.3f,Tier=4,Model=ProdModels.EitrUnit},
        };
        internal override IEnumerable<ItemDef> Items=>ItemList;

        // ---- machine recipes ----
        private static readonly Craft[] CraftList=
        {
            Craft.Of("gear","assembler",1f,0,new[]{("Iron",1)},("FH_IronGear",2)),
            Craft.Of("wire","assembler",0.5f,0,new[]{("Copper",1)},("FH_CopperWire",3)),
            Craft.Of("rune_circuit","assembler",1f,1,new[]{("FH_CopperWire",3),("FineWood",1)},("FH_RuneCircuit",1)),
            Craft.Of("engine","assembler",10f,2,new[]{("FH_IronGear",2),("Iron",2)},("FH_Engine",1)),
            Craft.Of("plastic","chemical",2f,3,new[]{("Tar",2),("Coal",1)},("FH_Plastic",2)),
            Craft.Of("solid_fuel","chemical",2f,3,new[]{("Tar",3)},("FH_SolidFuel",1)),
            Craft.Of("silver_circuit","assembler",6f,3,new[]{("FH_RuneCircuit",2),("FH_Plastic",2),("FH_CopperWire",4),("Silver",1)},("FH_SilverCircuit",1)),
            Craft.Of("eitr_unit","assembler",10f,4,new[]{("FH_SilverCircuit",2),("FH_RuneCircuit",10),("Eitr",1)},("FH_EitrUnit",1)),
        };
        internal override IEnumerable<Craft> Crafts=>CraftList;

        // ---- pieces ----
        private static PieceDef[] _pieces;
        internal override IEnumerable<PieceDef> Pieces=>_pieces??(_pieces=new[]
        {
            Machine<Drill>("FH_BurnerDrill","Burner drill","Put it on an ore deposit (copper, tin, a muddy scrap pile, a silver vein...) and feed it wood or coal: it brings up the ore forever, slowly, and pushes it out of its front onto a belt or into a chest. On plain ground it digs stone.",
                "Stone:10, Bronze:3, Wood:4","piece_workbench",0,1f,ProdModels.BurnerDrill,WearNTear.MaterialType.Stone),
            Machine<Drill>("FH_ElectricDrill","Electric drill","A bigger drill on power: twice as fast, reaches a little further, needs no fuel. 90 kW.",
                "Iron:10, FH_IronGear:5, FH_RuneCircuit:3","forge",2,1.5f,ProdModels.ElectricDrill),
            Machine<Woodcutter>("FH_Woodcutter","Woodcutter","Set it among trees and feed it a little fuel: it gathers fallen branches and bark from every tree within reach without felling them (birch and oak give fine wood, pines core wood).",
                "Wood:10, Bronze:2, Stone:4","piece_workbench",0,1f,ProdModels.Woodcutter,WearNTear.MaterialType.Wood),
            Machine<Assembler>("FH_Assembler","Assembler","Makes one recipe over and over (Shift+E to choose it) from what's put in. 75 kW, half speed.",
                "Iron:6, FH_IronGear:5, FH_RuneCircuit:3","forge",1,1.5f,t=>ProdModels.Assembler(t,new Color(0.45f,0.4f,0.32f),new Color(1f,0.7f,0.3f))),
            Machine<Assembler>("FH_Assembler2","Iron assembler","A faster assembler: 150 kW, three-quarter speed.",
                "Iron:10, FH_IronGear:8, FH_RuneCircuit:6, Silver:2","forge",2,1.5f,t=>ProdModels.Assembler(t,new Color(0.32f,0.4f,0.48f),new Color(0.3f,0.7f,1f))),
            Machine<Assembler>("FH_Assembler3","Eitr assembler","The fastest assembler: 375 kW, one and a quarter speed.",
                "BlackMetal:10, FH_EitrUnit:2, FH_SilverCircuit:4","forge",4,1.5f,t=>ProdModels.Assembler(t,new Color(0.3f,0.22f,0.38f),new Color(0.75f,0.35f,1f))),
            Machine<Pumpjack>("FH_Pumpjack","Tar pumpjack","Build it on a Plains tar pit: it pumps up tar. 90 kW.",
                "Iron:10, FH_IronGear:10, FH_RuneCircuit:5","forge",3,1.5f,ProdModels.Pumpjack),
            Machine<Assembler>("FH_ChemPlant","Chemical plant","Cooks tar into plastic and solid fuel (Shift+E to choose). 210 kW.",
                "Iron:10, Tar:10, FH_IronGear:5, FH_RuneCircuit:5","forge",3,1.5f,ProdModels.ChemPlant),
        });

        private static PieceDef Machine<T>(string prefab,string name,string text,string cost,string station,int tier,float half,Action<Transform> model,
            WearNTear.MaterialType material=WearNTear.MaterialType.Iron)where T:Machine=>new PieceDef
        {
            Prefab=prefab,Name=name,Description=text,Cost=cost,Station=station,Tier=tier,Material=material,Model=model,
            Health=material==WearNTear.MaterialType.Wood?500f:1200f,
            Setup=go=>
            {
                go.AddComponent<Running>();
                go.AddComponent<T>();
                SnapPoints.Box(go,new Vector3(-half,0f,-half),new Vector3(half,0f,half));
            },
        };

        internal override bool WindowOpen=>RecipeWindow.IsOpen;
        internal override void CloseWindow()=>RecipeWindow.Close();
        internal override void Tick()=>RecipeWindow.Tick();
        internal override void Stop()=>RecipeWindow.Close();

        // ---- tests: fh prod deposits [r] | drill | setrecipe <id> | status ----
        internal override string Command=>"prod";
        internal override string Help=>"deposits [r] | drill | setrecipe <id|none> | status [r]";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"status";
            var inv=System.Globalization.CultureInfo.InvariantCulture;
            switch(sub)
            {
                case "deposits":
                {
                    float r=args.Length>3?float.Parse(args[3],inv):10f;
                    output(new JObject{["deposits"]=new JArray(Deposits.Near(me.transform.position,r).Select(d=>new JObject{["what"]=d.what,["drops"]=d.drops,["distance"]=Math.Round(d.distance,1)})),
                        ["trees"]=new JArray(Deposits.Trees(me.transform.position,r)),["tar"]=Deposits.Tar(me.transform.position,r)});
                    return null;
                }
                case "drill":
                {
                    Drill d=Nearest<Drill>(me,30f);
                    if(d==null){error("fh prod drill: no drill within 30 m");return null;}
                    string mineral=Deposits.Mineral(d.transform.position+Vector3.up*0.3f,d.gameObject.name.StartsWith("FH_Electric")?2.6f:1.8f,out string what);
                    output(new JObject{["drill"]=d.gameObject.name,["at"]=Tools.V(d.transform.position),["mineral"]=mineral,["deposit"]=what,
                        ["ground"]=Deposits.Ground(d.transform.position),["front"]=Tools.V(d.Front),["frontPort"]=Ports.At(d.Front,0.5f,d)?.GetType().Name,
                        ["hover"]=System.Text.RegularExpressions.Regex.Replace(d.GetHoverText(),"<.*?>","")});
                    return null;
                }
                case "setrecipe":
                {
                    Assembler a=Nearest<Assembler>(me,30f);
                    if(a==null){error("fh prod setrecipe: no assembler within 30 m");return null;}
                    string id=args.Length>3?args[3]:"none";
                    Craft c=id=="none"?null:Factorheim.Crafts.Find(id);
                    if(id!="none"&&(c==null||c.Kind!=a.Kind)){error($"fh prod setrecipe: no {a.Kind} recipe {id} ({string.Join(", ",Factorheim.Crafts.For(a.Kind).Select(x=>x.Id))})");return null;}
                    a.Choose(c,me);
                    output(new JObject{["machine"]=a.gameObject.name,["recipe"]=a.Recipe?.Id});
                    return null;
                }
                case "status":
                {
                    float r=args.Length>3?float.Parse(args[3],inv):20f;
                    var list=new JArray();
                    foreach(Machine m in Live<Machine>.All)
                        if(m!=null&&(m is Extractor||m is Assembler)&&Vector3.Distance(m.transform.position,me.transform.position)<=r)
                            list.Add(new JObject{["piece"]=m.gameObject.name.Replace("(Clone)",""),["owner"]=m.Owner,["powered"]=m.Powered,["wants"]=m.Wants,
                                ["hover"]=System.Text.RegularExpressions.Regex.Replace(m.GetHoverText(),"<.*?>","")});
                    output(new JObject{["machines"]=list});
                    return null;
                }
            }
            error("fh prod "+Help);
            return null;
        }
        private static T Nearest<T>(Player me,float r)where T:Machine=>
            Live<Machine>.All.OfType<T>().Where(m=>m!=null&&Vector3.Distance(m.transform.position,me.transform.position)<=r)
                .OrderBy(m=>Vector3.Distance(m.transform.position,me.transform.position)).FirstOrDefault();
    }
}
