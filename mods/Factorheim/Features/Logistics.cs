using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using M=Factorheim.LogisticsModels;

namespace Factorheim
{
    // Belts, a splitter and inserters: the lines that carry items between drills, smelters, machines and chests. Every conveyor is ticked
    // here in one loop (not an Update each); inserters are machines and run themselves. A player standing on a belt rides along.
    internal sealed class Logistics:Feature
    {
        internal override string Name=>"Logistics";

        private static readonly PieceDef[] Defs=
        {
            new PieceDef{Prefab="FH_Belt",Name="Bronze belt",Tier=0,Cost="Bronze:1, Wood:2",Station="piece_workbench",Material=WearNTear.MaterialType.Wood,Health=300f,
                Description="A metre of belt. Items ride it toward the arrow and go on into whatever is at its end: another belt, a machine, a chest, a smelter. Things dropped on it get on. So do you.",
                Model=t=>M.Belt(t,M.Bronze,Look.Hazard,0f),Setup=go=>{go.AddComponent<Belt>();Snap(go,1f);}},
            new PieceDef{Prefab="FH_BeltFast",Name="Iron belt",Tier=2,Cost="Iron:1, FH_IronGear:2",Station="forge",Health=500f,
                Description="Twice as fast as bronze.",Model=t=>M.Belt(t,M.Iron,new Color(0.9f,0.2f,0.15f),0f),Setup=go=>{go.AddComponent<Belt>();Snap(go,1f);}},
            new PieceDef{Prefab="FH_BeltExpress",Name="Eitr belt",Tier=4,Cost="Iron:1, FH_IronGear:2, Eitr:1",Station="forge",Health=700f,
                Description="Three times as fast as bronze, and it glows.",Model=t=>M.Belt(t,M.Eitr,M.EitrGlow,1.5f),Setup=go=>{go.AddComponent<Belt>();Snap(go,1f);}},
            new PieceDef{Prefab="FH_Splitter",Name="Splitter",Tier=2,Cost="Iron:2, Bronze:2, FH_RuneCircuit:2",Station="forge",Health=600f,
                Description="Two belts wide. What comes in on either side goes out left, right, left, right (all to one side when the other is blocked).",
                Model=M.Splitter,Setup=go=>{go.AddComponent<Splitter>();Snap(go,2f);}},
            new PieceDef{Prefab="FH_BurnerInserter",Name="Burner inserter",Tier=0,Cost="Bronze:1, Wood:2",Station="piece_workbench",Material=WearNTear.MaterialType.Wood,Health=250f,BoxCollider=false,
                Description="Picks items up behind it and puts them down in front, taking only what the front can take. Burns fuel as it swings, and feeds itself first when its fire is out. Use fuel on it to fill it.",
                Model=t=>M.Inserter(t,M.Bronze,M.Dark,1f),Setup=go=>{go.AddComponent<Inserter>();Snap(go,1f);}},
            new PieceDef{Prefab="FH_Inserter",Name="Inserter",Tier=1,Cost="Iron:1, FH_IronGear:1, FH_RuneCircuit:1",Station="forge",Health=350f,BoxCollider=false,
                Description="An electric inserter: faster than the burner, 13 kW while it works. Put it within reach of a power pole.",
                Model=t=>M.Inserter(t,Look.Hazard,M.Iron,1f),Setup=go=>{go.AddComponent<Inserter>();Snap(go,1f);}},
            new PieceDef{Prefab="FH_LongInserter",Name="Long inserter",Tier=2,Cost="Iron:2, FH_IronGear:1, FH_RuneCircuit:1",Station="forge",Health=350f,BoxCollider=false,
                Description="Reaches two metres each way: over a belt to the one beyond it.",
                Model=t=>M.Inserter(t,new Color(0.85f,0.15f,0.12f),M.Iron,2f),Setup=go=>{go.AddComponent<Inserter>();Snap(go,1f);}},
            new PieceDef{Prefab="FH_FilterInserter",Name="Filter inserter",Tier=3,Cost="Iron:1, FH_RuneCircuit:2, FH_SilverCircuit:2",Station="forge",Health=350f,BoxCollider=false,
                Description="Fast, and moves only the item you set: use that item on it. Shift+E clears the filter.",
                Model=t=>M.Inserter(t,Look.Violet,M.Iron,1f,0.6f),Setup=go=>{go.AddComponent<Inserter>();Snap(go,1f);}},
        };
        internal override IEnumerable<PieceDef> Pieces=>Defs;

        // Corners of its base, a metre long and `width` wide: belts meet end to end and side by side on a 1 m grid.
        private static void Snap(GameObject go,float width)
        {
            float w=width*0.5f;
            SnapPoints.Add(go,new Vector3(-w,0,-0.5f),new Vector3(w,0,-0.5f),new Vector3(-w,0,0.5f),new Vector3(w,0,0.5f));
        }

        private static readonly AccessTools.FieldRef<Character,Rigidbody> Body=AccessTools.FieldRefAccess<Character,Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character,Collider> Ground=AccessTools.FieldRefAccess<Character,Collider>("m_lastGroundCollider");
        private int _writesBefore;private float _rateAt;private float _rate;

        internal override void Tick()
        {
            float dt=Time.deltaTime;
            if(dt<=0f)return;
            Vector3 camera=GameCamera.instance!=null?GameCamera.instance.transform.position:Vector3.zero;
            List<Conveyor> all=Live<Conveyor>.All;
            for(int i=0;i<all.Count;i++)all[i].Tick(dt,camera);
            if(Time.time>=_rateAt)
            {
                _rate=(Conveyor.Writes-_writesBefore)/5f;_writesBefore=Conveyor.Writes;_rateAt=Time.time+5f;
            }
        }

        // The local player standing on a belt moves with it.
        internal override void FixedTick()
        {
            Player p=Player.m_localPlayer;
            if(p==null||p.IsDead()||!p.IsOnGround())return;
            Collider c=Ground(p);
            Conveyor belt=c!=null?c.GetComponentInParent<Conveyor>():null;
            if(belt==null||!belt.Valid)return;
            Rigidbody body=Body(p);
            if(body!=null)body.position+=belt.transform.forward*belt.Speed*Time.fixedDeltaTime;
        }

        internal override void Stop()
        {
            foreach(Conveyor c in Live<Conveyor>.All.ToList())if(c!=null)c.Hide();
            foreach(Machine m in Live<Machine>.All.ToList())if(m is Inserter i)i.Hide();
            Live<Conveyor>.Clear();
        }

        internal override string Command=>"belt";
        internal override string Help=>"[line <n> [belt prefab]] [feed <item> [n]]";
        internal override IEnumerator Test(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            string sub=args.Length>2?args[2]:"stats";
            switch(sub)
            {
                case "stats":
                {
                    var all=Live<Conveyor>.All;
                    output(new JObject{["conveyors"]=all.Count,["owned"]=all.Count(c=>c.Owner),["items"]=all.Sum(c=>c.ItemCount),
                        ["zdoWritesPerSecond"]=Math.Round(_rate,2),["writesTotal"]=Conveyor.Writes,
                        ["inserters"]=Live<Machine>.All.Count(m=>m is Inserter)});
                    return null;
                }
                case "line":
                {
                    // fh belt line <n> [prefab]: n belts in a line ahead of the player (flowing away), a chest at the end
                    int n=args.Length>3&&int.TryParse(args[3],out int k)?Mathf.Clamp(k,1,64):8;
                    string prefab=args.Length>4?args[4]:"FH_Belt";
                    GameObject belt=Factorheim.Pieces.Find(prefab)?.Built,chest=ZNetScene.instance.GetPrefab("piece_chest_wood");
                    if(belt==null){error("fh belt line: no belt "+prefab);return null;}
                    float yaw=Mathf.Round(me.transform.eulerAngles.y/90f)*90f;
                    Quaternion rot=Quaternion.Euler(0,yaw,0);Vector3 fwd=rot*Vector3.forward;
                    Vector3 start=me.transform.position+fwd*2f;
                    for(int i=0;i<n;i++)UnityEngine.Object.Instantiate(belt,Grounded(start+fwd*i),rot);
                    if(chest!=null)UnityEngine.Object.Instantiate(chest,Grounded(start+fwd*(n-0.5f+0.5f)),rot);
                    output(new JObject{["belts"]=n,["prefab"]=prefab,["from"]=Tools.V(start),["yaw"]=yaw,["chestAt"]=Tools.V(start+fwd*n)});
                    return null;
                }
                case "feed":
                {
                    // fh belt feed <item> [n]: puts items on the nearest belt (5 m) of ours, as many as fit
                    string item=args.Length>3?args[3]:"CopperOre";
                    int n=args.Length>4&&int.TryParse(args[4],out int k)?k:4;
                    if(Ports.Prefab(item)==null){error("fh belt feed: no item "+item);return null;}
                    Conveyor c=Live<Conveyor>.All.Where(x=>x.Owner).OrderBy(x=>Vector3.Distance(x.transform.position,me.transform.position)).FirstOrDefault();
                    if(c==null||Vector3.Distance(c.transform.position,me.transform.position)>5f){error("fh belt feed: no belt of ours within 5 m");return null;}
                    int put=0;
                    for(float p=0f;p<=1f&&put<n;p+=Lane.Gap)if(c.Port(c.transform.TransformPoint(0,0.12f,p-0.5f)).Put(item))put++;
                    output(new JObject{["put"]=put,["on"]=c.name,["at"]=Tools.V(c.transform.position)});
                    return null;
                }
            }
            error("fh belt [line <n> [prefab]] [feed <item> [n]]");
            return null;
        }
        private static Vector3 Grounded(Vector3 at){if(ZoneSystem.instance.GetGroundHeight(at,out float h))at.y=h;return at;}
    }
}
