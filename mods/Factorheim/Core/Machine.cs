using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Factorheim
{
    // A count of items by prefab, saved in one ZDO string "Prefab:n,Prefab:n". Cap: how many of each kind it holds (Total: of all kinds).
    internal sealed class Store
    {
        internal readonly string Key;
        internal int Cap,Total=int.MaxValue;
        internal Func<string,bool> Accepts=_=>true;
        internal readonly Dictionary<string,int> Items=new Dictionary<string,int>();
        internal Store(string key,int cap){Key=key;Cap=cap;}
        internal int Count(string item)=>Items.TryGetValue(item,out int n)?n:0;
        internal int Sum=>Items.Values.Sum();
        internal bool Empty=>Items.Count==0;
        internal bool CanAdd(string item,int n=1)=>Accepts(item)&&Count(item)+n<=Cap&&Sum+n<=Total;
        internal void Add(string item,int n=1){if(n<=0)return;Items[item]=Count(item)+n;}
        internal bool Remove(string item,int n=1)
        {
            int have=Count(item);
            if(have<n)return false;
            if(have==n)Items.Remove(item);else Items[item]=have-n;
            return true;
        }
        internal string First(Func<string,bool> wants=null)=>Items.Keys.FirstOrDefault(k=>wants==null||wants(k));
        internal string Save()=>string.Join(",",Items.Where(kv=>kv.Value>0).Select(kv=>kv.Key+":"+kv.Value));
        internal void Load(string s)
        {
            Items.Clear();
            if(string.IsNullOrEmpty(s))return;
            foreach(string part in s.Split(','))
            {
                int c=part.LastIndexOf(':');
                if(c>0&&int.TryParse(part.Substring(c+1),out int n)&&n>0)Items[part.Substring(0,c)]=n;
            }
        }
    }

    // What burns and for how long (seconds of a burner machine's work). Settings could come later; these are Factorio-ish ratios.
    internal static class Fuel
    {
        internal static readonly Dictionary<string,float> Seconds=new Dictionary<string,float>
        {
            {"Wood",15f},{"FineWood",20f},{"RoundLog",25f},{"ElderBark",25f},{"YggdrasilWood",30f},{"Blackwood",30f},
            {"Resin",8f},{"Coal",40f},{"FH_SolidFuel",160f},
        };
        internal static bool Is(string item)=>item!=null&&Seconds.ContainsKey(item);
    }

    // The base of every machine of ours: a piece with input, output and fuel stores kept in its ZDO, working on its owner's game a few
    // times a second (Work), saving at most once a second when something changed. Everyone else reads the ZDO for the hover text.
    //
    // Players: E takes everything out of the output (Shift+E: Interact with alt, for the machine's own menu: recipes); using an item from
    // the hotbar while looking at it puts in as many of that stack as fit (fuel first). Automation goes through Port().
    //
    // Power: a machine that runs on electricity says PowerUse (watts while working) and Wants (it would work if it could). The Power
    // feature's solver sets Powered (0..1) about once a second; Powered falls back to 0 when nothing has set it for 3 s.
    internal abstract class Machine:MonoBehaviour,IPortHost,IPort,Hoverable,Interactable
    {
        internal const float Step=0.25f;
        protected ZNetView View;
        protected Store Input,Output,FuelStore;
        protected float Progress;          // 0..1 of the current job (saved)
        protected float Burn;              // seconds of fuel already burning (saved)
        private bool _dirty;private float _saveAt,_acc,_poweredAt;
        private float _powered;

        internal virtual string Title=>GetComponent<Piece>()?.m_name??name;
        internal virtual float PowerUse=>0f;
        internal virtual bool Wants=>false;
        internal float Powered{get=>Time.time-_poweredAt<3f?_powered:0f;set{_powered=Mathf.Clamp01(value);_poweredAt=Time.time;}}
        internal bool Owner=>View!=null&&View.IsValid()&&View.IsOwner();

        // Stores: made in Setup (called from Awake, before Load). Leave any null that the machine doesn't have.
        protected abstract void Setup();
        // The machine's job, on its owner only, dt = Step (or several steps' worth when catching up after loading).
        protected abstract void Work(float dt);
        // One line under the title: "Smelting copper 42%", "No fuel", "Needs a deposit under it".
        protected virtual string Status()=>"";
        // Its own Shift+E menu (an assembler's recipe picker). Return true when handled.
        protected virtual bool Menu(Humanoid user)=>false;
        // Extra ZDO fields of its own (a recipe id), after the stores.
        protected virtual void SaveMore(ZDO z){}
        protected virtual void LoadMore(ZDO z){}

        protected virtual void Awake()
        {
            View=GetComponent<ZNetView>();
            Setup();
            Live<Machine>.Add(this);
            if(View!=null&&View.IsValid())Load();
            WearNTear wear=GetComponent<WearNTear>();
            if(wear!=null)wear.m_onDestroyed+=Spill;
        }
        // Torn down or broken: what it held falls on the ground (on the owner's game, who has the latest stores).
        private static readonly System.Reflection.MethodInfo SaveDrop=HarmonyLib.AccessTools.Method(typeof(ItemDrop),"Save");
        // (never throws: an exception here would stop the game removing the piece)
        private void Spill(){try{SpillStores();}catch(Exception e){Plugin.Warn($"{Title}: could not drop what it held: {e.Message}");}}
        protected virtual void SpillStores()
        {
            if(!Owner)return;
            foreach(Store s in new[]{Input,Output,FuelStore})
            {
                if(s==null)continue;
                foreach(var kv in s.Items.ToList())
                {
                    GameObject prefab=Ports.Prefab(kv.Key);
                    ItemDrop drop=prefab!=null?prefab.GetComponent<ItemDrop>():null;
                    if(drop==null)continue;
                    for(int left=kv.Value;left>0;)
                    {
                        int n=Mathf.Min(left,Mathf.Max(1,drop.m_itemData.m_shared.m_maxStackSize));
                        // (made from the prefab itself: ItemDrop.DropItem wants the item's m_dropPrefab, which a prefab's own data lacks)
                        GameObject go=Instantiate(prefab,transform.position+Vector3.up*0.6f+UnityEngine.Random.insideUnitSphere*0.3f,Quaternion.identity);
                        ItemDrop d=go.GetComponent<ItemDrop>();
                        if(d!=null){d.m_itemData.m_stack=n;SaveDrop?.Invoke(d,null);}
                        left-=n;
                    }
                }
                s.Items.Clear();
            }
        }
        protected virtual void OnDestroy()
        {
            if(_dirty&&Owner)Save();
            Live<Machine>.Remove(this);
        }
        protected void Dirty()=>_dirty=true;

        protected virtual void Update()
        {
            if(View==null||!View.IsValid())return;
            if(!View.IsOwner())
            {
                if(Time.time>=_saveAt){_saveAt=Time.time+1f;Load();}   // (for the hover text)
                return;
            }
            _acc+=Time.deltaTime;
            int steps=0;
            while(_acc>=Step&&steps<8){_acc-=Step;steps++;Work(Step);}
            if(_acc>Step*8)_acc=0;   // (a long hitch: don't spiral)
            if(_dirty&&Time.time>=_saveAt){_saveAt=Time.time+1f;Save();}
        }

        protected void Save()
        {
            ZDO z=View.GetZDO();if(z==null)return;
            if(Input!=null)Set(z,Input.Key,Input.Save());
            if(Output!=null)Set(z,Output.Key,Output.Save());
            if(FuelStore!=null)Set(z,FuelStore.Key,FuelStore.Save());
            if(Math.Abs(z.GetFloat("fh_progress",0f)-Progress)>0.001f)z.Set("fh_progress",Progress);
            if(Math.Abs(z.GetFloat("fh_burn",0f)-Burn)>0.01f)z.Set("fh_burn",Burn);
            SaveMore(z);
            _dirty=false;
        }
        // (only when it changed: every Set bumps the ZDO's revision and sends it to everyone)
        protected static void Set(ZDO z,string key,string value){if(z.GetString(key,"")!=value)z.Set(key,value);}
        protected void Load()
        {
            ZDO z=View.GetZDO();if(z==null)return;
            Input?.Load(z.GetString(Input.Key,""));
            Output?.Load(z.GetString(Output.Key,""));
            FuelStore?.Load(z.GetString(FuelStore.Key,""));
            Progress=z.GetFloat("fh_progress",0f);
            Burn=z.GetFloat("fh_burn",0f);
            LoadMore(z);
        }

        // Burner machines: keeps a fire going while there's fuel; true while it burns (call once per Work with dt when it wants to work).
        protected bool Burning(float dt)
        {
            if(Burn<=0f&&FuelStore!=null)
            {
                string f=FuelStore.First(Fuel.Is);
                if(f!=null&&FuelStore.Remove(f)){Burn+=Fuel.Seconds[f];Dirty();}
            }
            if(Burn<=0f)return false;
            Burn=Mathf.Max(0f,Burn-dt);Dirty();
            return true;
        }

        // ---- IPortHost / IPort: fuel and input in, output out ----
        public virtual IPort Port(Vector3 at)=>this;
        public bool Ready=>Owner;
        public virtual bool CanPut(string item)=>FuelStore!=null&&Fuel.Is(item)&&FuelStore.CanAdd(item)||Input!=null&&Input.CanAdd(item);
        public virtual bool Put(string item)
        {
            if(FuelStore!=null&&Fuel.Is(item)&&FuelStore.CanAdd(item)){FuelStore.Add(item);Dirty();return true;}
            if(Input!=null&&Input.CanAdd(item)){Input.Add(item);Dirty();return true;}
            return false;
        }
        public virtual string Peek(Func<string,bool> wants=null)=>Output?.First(wants);
        public virtual bool Take(string item){if(Output==null||!Output.Remove(item))return false;Dirty();return true;}

        // ---- players ----
        public virtual string GetHoverName()=>Title;
        public virtual float GetHoverOffset()=>0f;
        public virtual string GetHoverText()
        {
            var sb=new StringBuilder();
            sb.Append(Title);
            string s=Status();
            if(!string.IsNullOrEmpty(s))sb.Append("\n<color=#ffd27a>").Append(s).Append("</color>");
            if(PowerUse>0f)sb.Append($"\nPower: {Mathf.RoundToInt(Powered*100)}% of {Watts(PowerUse)}");
            if(FuelStore!=null)sb.Append("\nFuel: ").Append(List(FuelStore)).Append(Burn>0?$" (burning {Burn:0}s)":"");
            if(Input!=null)sb.Append("\nIn: ").Append(List(Input));
            if(Output!=null)sb.Append("\nOut: ").Append(List(Output));
            if(Output!=null)sb.Append("\n[<color=yellow><b>$KEY_Use</b></color>] Take out");
            sb.Append(MenuHint!=null?$"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] {MenuHint}":"");
            sb.Append("\n[<color=yellow><b>1-8</b></color>] Put in");
            return Localization.instance.Localize(sb.ToString());
        }
        protected virtual string MenuHint=>null;
        internal static string List(Store s)=>s.Empty?"empty":string.Join(", ",s.Items.Select(kv=>kv.Value+" "+Ports.Title(kv.Key)));
        internal static string Watts(float w)=>w>=1e6f?$"{w/1e6f:0.#} MW":w>=1e3f?$"{w/1e3f:0.#} kW":$"{w:0} W";

        public virtual bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||!(user is Player p))return false;
            if(alt&&Menu(user))return true;
            if(Output==null||Output.Empty)return false;
            if(!Owner){View.ClaimOwnership();Load();}
            int took=0;
            foreach(var kv in Output.Items.ToList())
            {
                GameObject prefab=Ports.Prefab(kv.Key);
                if(prefab==null)continue;
                int n=kv.Value;
                while(n>0&&p.GetInventory().CanAddItem(prefab,1)){p.GetInventory().AddItem(prefab,1);n--;took++;Output.Remove(kv.Key);}
            }
            if(took>0){Save();p.Message(MessageHud.MessageType.TopLeft,$"Took {took}");}
            return took>0;
        }
        public virtual bool UseItem(Humanoid user,ItemDrop.ItemData item)
        {
            string n=Ports.NameOf(item);
            if(n==null||!(user is Player p))return false;
            if(!Owner){View.ClaimOwnership();Load();}
            int put=0;
            while(item.m_stack>0&&Put(n)){p.GetInventory().RemoveItem(item,1);put++;}
            if(put==0){p.Message(MessageHud.MessageType.Center,"It doesn't take that");return true;}
            Save();
            p.Message(MessageHud.MessageType.TopLeft,$"Put in {put} {Ports.Title(n)}");
            return true;
        }
    }
}
