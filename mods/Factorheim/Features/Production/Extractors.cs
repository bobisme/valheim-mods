using System.Collections.Generic;
using UnityEngine;

namespace Factorheim
{
    // A machine that makes something out of the place it stands (ore from a deposit, wood from trees, tar from a pit) and pushes it out
    // of its front: into whatever port is there (a belt, a chest, a machine), or into its own little output store if nothing takes it.
    // Burner ones burn fuel (put in by hand or inserter); electric ones run at the power they get.
    internal abstract class Extractor:Machine
    {
        protected Running Shown;
        protected abstract float Half{get;}                  // how far its front is from its middle
        protected virtual bool Burner=>true;
        protected virtual int Buffer=>10;
        internal Vector3 Front=>transform.position+transform.forward*(Half+0.45f)+Vector3.up*0.35f;
        private float _pushAt;

        protected override void Setup()
        {
            Output=new Store("fh_out",Buffer){Total=Buffer};
            if(Burner)FuelStore=new Store("fh_fuel",20){Accepts=Fuel.Is,Total=20};
            Shown=GetComponent<Running>();
        }

        // What it would make here now, and how long one takes at full speed; null when there's nothing to work.
        protected abstract string Product(out float seconds);

        private string _making;private float _seconds;private float _lookAt;
        internal string Making=>_making;
        protected override void Work(float dt)
        {
            if(Time.time>=_lookAt){_lookAt=Time.time+5f;_making=Product(out _seconds);}   // (the place doesn't change: look again now and then)
            Push();
            bool worked=false;
            if(_making!=null&&Output.CanAdd(_making))
            {
                float speed=Burner?(Burning(dt)?1f:0f):Powered;
                if(speed>0f)
                {
                    Progress+=dt*speed/Mathf.Max(0.1f,_seconds);worked=true;
                    if(Progress>=1f){Progress-=1f;Output.Add(_making);Push();}
                    Dirty();
                }
            }
            if(Shown!=null)Shown.Set(worked);
        }
        internal override bool Wants=>_making!=null&&Output!=null&&Output.CanAdd(_making);

        // Out of the front, at most twice a second (a belt takes an item when it has room).
        private void Push()
        {
            if(Output.Empty||Time.time<_pushAt)return;
            _pushAt=Time.time+0.5f;
            IPort to=Ports.At(Front,0.5f,this);
            if(to==null||!to.Ready)return;
            string item=Output.First(to.CanPut);
            if(item!=null&&to.Put(item)){Output.Remove(item);Dirty();}
        }
        // Inserters may take from the output too, but nothing is put into an extractor but fuel.
        public override bool CanPut(string item)=>FuelStore!=null&&Fuel.Is(item)&&FuelStore.CanAdd(item);

        protected override string Status()
        {
            if(!Owner&&Time.time>=_lookAt){_lookAt=Time.time+5f;_making=Product(out _seconds);}   // (someone else runs it: look for the hover text)
            if(_making==null)return Missing;
            string s="Making "+Ports.Title(_making)+$" {Mathf.RoundToInt(Progress*100)}%";
            if(Burner&&Burn<=0f&&(FuelStore==null||FuelStore.Empty))s+=" — no fuel";
            if(!Burner&&Powered<=0f)s+=" — no power";
            if(!Output.CanAdd(_making))s+=" — full";
            return s;
        }
        protected abstract string Missing{get;}
    }

    // Burner and electric drills: on or right beside a deposit; plain ground gives stone.
    internal sealed class Drill:Extractor
    {
        private bool Electric=>gameObject.name.StartsWith("FH_ElectricDrill");
        protected override bool Burner=>!Electric;
        protected override float Half=>Electric?1.5f:1f;
        internal override float PowerUse=>Electric?90000f:0f;
        internal string Deposit;
        protected override string Product(out float seconds)
        {
            float reach=Electric?2.6f:1.8f;
            string mineral=Deposits.Mineral(transform.position+Vector3.up*0.3f,reach,out Deposit);
            float baseSeconds=Electric?Production.ElectricDrillSeconds.Value:Production.BurnerDrillSeconds.Value;
            if(mineral==null&&Deposits.Ground(transform.position)){mineral="Stone";Deposit="plain ground";}
            seconds=baseSeconds*(mineral!=null&&Slow.TryGetValue(mineral,out float f)?f:1f);
            return mineral;
        }
        // Rarer ores come up slower.
        internal static readonly Dictionary<string,float> Slow=new Dictionary<string,float>
        {
            {"SilverOre",1.5f},{"Obsidian",1.5f},{"BlackMarble",1.5f},{"Softtissue",2f},{"FlametalOreNew",3f},{"FlametalOre",3f},
            {"GemstoneRed",6f},{"GemstoneBlue",6f},{"GemstoneGreen",6f},{"Crystal",2f},
        };
        protected override string Missing=>"Nothing to mine: put it on an ore deposit";
        protected override string Status()=>base.Status()+(Deposit!=null&&Making!=null?"\nOn: "+Deposit:"");
    }

    // Gathers fallen branches and bark from the trees round it, without felling them: the more trees (up to eight), the faster.
    internal sealed class Woodcutter:Extractor
    {
        protected override float Half=>1f;
        private int _next;internal int TreeCount;
        protected override string Product(out float seconds)
        {
            List<string> trees=Deposits.Trees(transform.position,Production.WoodcutterReach.Value);
            TreeCount=trees.Count;
            seconds=Production.WoodcutterSeconds.Value*4f/Mathf.Clamp(trees.Count,1,8);
            if(trees.Count==0)return null;
            _next=(_next+1)%trees.Count;   // (each look takes the next tree's wood: a mixed wood gives a mix)
            return trees[_next];
        }
        protected override string Missing=>"No trees within reach";
        protected override string Status()=>base.Status()+(TreeCount>0?$"\n{TreeCount} trees in reach":"");
    }

    // On a Plains tar pit: pumps tar.
    internal sealed class Pumpjack:Extractor
    {
        protected override bool Burner=>false;
        protected override float Half=>1.5f;
        internal override float PowerUse=>90000f;
        protected override string Product(out float seconds)
        {
            seconds=Production.PumpjackSeconds.Value;
            return Deposits.Tar(transform.position,3.5f)?"Tar":null;
        }
        protected override string Missing=>"Not on a tar pit";
    }
}
