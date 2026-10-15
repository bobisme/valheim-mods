using System.Linq;
using UnityEngine;

namespace Factorheim
{
    // Assemblers and the chemical plant: set to one recipe (Shift+E), they take in only that recipe's ingredients (two crafts' worth),
    // start a craft when all are there (taking them then, as Factorio does), and stop when the output holds five crafts' worth.
    internal sealed class Assembler:Machine
    {
        private struct Spec{internal string Kind;internal float Speed,Power;}
        private Spec _spec;private Running _shown;
        internal Craft Recipe;

        private static Spec SpecOf(string name)
        {
            if(name.StartsWith("FH_ChemPlant"))return new Spec{Kind="chemical",Speed=1f,Power=210000f};
            if(name.StartsWith("FH_Assembler3"))return new Spec{Kind="assembler",Speed=1.25f,Power=375000f};
            if(name.StartsWith("FH_Assembler2"))return new Spec{Kind="assembler",Speed=0.75f,Power=150000f};
            return new Spec{Kind="assembler",Speed=0.5f,Power=75000f};
        }
        internal string Kind=>_spec.Kind;
        internal float Speed=>_spec.Speed;
        internal override float PowerUse=>_spec.Power;
        internal override bool Wants=>Recipe!=null&&(Progress>0f||HasInputs())&&!OutputFull();

        protected override void Setup()
        {
            _spec=SpecOf(gameObject.name);
            _shown=GetComponent<Running>();
            Input=new Store("fh_in",999){Accepts=item=>Recipe!=null&&Recipe.Uses(item)&&Input.Count(item)<Recipe.Needs(item)*2};
            Output=new Store("fh_out",999);
        }
        protected override void SaveMore(ZDO z)=>Set(z,"fh_recipe",Recipe?.Id??"");
        protected override void LoadMore(ZDO z)
        {
            string id=z.GetString("fh_recipe","");
            Recipe=id==""?null:Crafts.Find(id);
            if(Recipe!=null&&Recipe.Kind!=Kind)Recipe=null;
        }

        private bool HasInputs()=>Recipe.In.All(i=>Input.Count(i.item)>=i.n);
        private bool OutputFull()=>Recipe.Out.Any(o=>Output.Count(o.item)>=o.n*5);

        protected override void Work(float dt)
        {
            bool worked=false;
            if(Recipe!=null)
            {
                if(Progress<=0f&&Tech.Unlocked(Recipe.Tier)&&HasInputs()&&!OutputFull())
                {
                    foreach(var i in Recipe.In)Input.Remove(i.item,i.n);
                    Progress=0.0001f;Dirty();
                }
                if(Progress>0f&&Powered>0f)
                {
                    Progress+=dt*Speed*Powered/Mathf.Max(0.05f,Recipe.Seconds);worked=true;
                    if(Progress>=1f)
                    {
                        foreach(var o in Recipe.Out)Output.Add(o.item,o.n);
                        Progress=0f;
                    }
                    Dirty();
                }
            }
            _shown?.Set(worked);
        }

        // Another recipe: what was in it (and the craft under way) goes back to the player who changed it.
        internal void Choose(Craft c,Player by)
        {
            if(c!=null&&c.Kind!=Kind)return;
            if(!Owner){View.ClaimOwnership();Load();}
            if(c==Recipe)return;
            if(Recipe!=null&&Progress>0f)foreach(var i in Recipe.In)Input.Add(i.item,i.n);
            foreach(var kv in Input.Items.ToList())Give(by,kv.Key,kv.Value);
            Input.Items.Clear();
            Progress=0f;Recipe=c;
            Save();
        }
        private static void Give(Player p,string item,int n)
        {
            GameObject prefab=Ports.Prefab(item);
            if(prefab==null)return;
            while(n>0&&p!=null&&p.GetInventory().CanAddItem(prefab,1)){p.GetInventory().AddItem(prefab,1);n--;}
            if(n>0)   // (no room: on the ground at the player's feet)
            {
                Vector3 at=p!=null?p.transform.position+Vector3.up:Vector3.zero;
                ItemDrop drop=Object.Instantiate(prefab,at,Quaternion.identity).GetComponent<ItemDrop>();
                if(drop!=null)drop.SetStack(n);
            }
        }

        protected override bool Menu(Humanoid user){RecipeWindow.Open(this);return true;}
        // (E on one with no recipe yet opens the picker too: nothing to take out anyway)
        public override bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(!hold&&!alt&&Recipe==null&&(Output==null||Output.Empty)&&user==Player.m_localPlayer)return Menu(user);
            return base.Interact(user,hold,alt);
        }
        protected override string MenuHint=>"Choose recipe";
        protected override string Status()
        {
            if(Recipe==null)return "No recipe: Shift+E to choose one";
            string s=$"Making {Recipe.Title} {Mathf.RoundToInt(Mathf.Clamp01(Progress)*100)}%";
            if(!Tech.Unlocked(Recipe.Tier))s+=" — not researched";
            else if(Powered<=0f)s+=" — no power";
            else if(Progress<=0f&&!HasInputs())s+="\nNeeds: "+string.Join(", ",Recipe.In.Where(i=>Input.Count(i.item)<i.n).Select(i=>$"{i.n-Input.Count(i.item)} {Ports.Title(i.item)}"));
            else if(OutputFull())s+=" — output full";
            return s;
        }
    }
}
