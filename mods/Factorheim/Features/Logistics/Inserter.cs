using System.Text;
using UnityEngine;

namespace Factorheim
{
    // An arm on a post: it picks an item up from what is behind it (a belt, a chest, a machine's output, the ground) and swings it over
    // to what is in front (a machine, a chest, a belt), taking only what the front can take. The swing is the work: the arm turns at its
    // speed, grabs when it is back, lets go when it is over. Others see it from the ZDO's "fh_hand" (what it holds) and swing their copy
    // the same way, so a running inserter needs a write only when it grabs or lets go (at most once a second).
    //
    // Burner inserters burn fuel while they swing; holding fuel with an empty fire, they feed themselves first. Electric ones run on
    // Power (Machine.Powered). Filter inserters move only the item they are set to (use that item on it; Shift+E clears the filter).
    internal sealed class Inserter:Machine
    {
        private float _reach=1f,_halfSwing=0.6f;   // metres to each side; seconds for one half of the round trip
        private bool _burner,_electric,_filter;
        private string _hand,_filterItem;
        private float _angle;                        // 0: over the pickup side, 180: over the drop side
        private float _pace=1f;                      // how fast it may move now (power, fuel); remote copies swing at full pace
        private bool _busy;
        private IPort _from,_to;private float _lookAt;
        private Transform _arm,_claw;private GameObject _held;private string _heldShown;

        internal override float PowerUse=>_electric?13000f:0f;
        internal override bool Wants=>_busy;

        protected override void Setup()
        {
            string prefab=Utils.GetPrefabName(gameObject.name);
            switch(prefab)
            {
                case "FH_BurnerInserter":_burner=true;_halfSwing=0.8f;break;
                case "FH_LongInserter":_electric=true;_reach=2f;_halfSwing=0.45f;break;
                case "FH_FilterInserter":_electric=true;_filter=true;_halfSwing=0.3f;break;
                default:_electric=true;_halfSwing=0.6f;break;
            }
            if(_burner)FuelStore=new Store("fh_fuel",5){Accepts=Fuel.Is};
            _arm=transform.Find("Model/arm");
            _claw=_arm!=null?_arm.Find("claw"):null;
        }
        protected override void SaveMore(ZDO z){Set(z,"fh_hand",_hand??"");if(_filter)Set(z,"fh_filter",_filterItem??"");}
        protected override void LoadMore(ZDO z)
        {
            string h=z.GetString("fh_hand","");_hand=h==""?null:h;
            string f=z.GetString("fh_filter","");_filterItem=f==""?null:f;
        }

        // Nothing reaches into an inserter as if it were a box.
        public override IPort Port(Vector3 at)=>null;

        private Vector3 Pick=>transform.TransformPoint(0,0.2f,-_reach);
        private Vector3 Drop=>transform.TransformPoint(0,0.2f,_reach);
        private bool Wanted(string item)=>!_filter||_filterItem==null||item==_filterItem;

        protected override void Work(float dt)
        {
            if(Time.time>=_lookAt||_from==null||_to==null)
            {
                _lookAt=Time.time+1f;
                _from=Ports.At(Pick,0.35f,this);_to=Ports.At(Drop,0.35f,this);
            }
            bool moving=_hand!=null?_angle<180f:_angle>0f;
            bool grab=_hand==null&&_angle<=0.5f,release=_hand!=null&&_angle>=179.5f;
            // a burner holding fuel with nothing in its own fire puts it there
            if(_burner&&_hand!=null&&Fuel.Is(_hand)&&Burn<=0f&&FuelStore.Empty){FuelStore.Add(_hand);_hand=null;Dirty();return;}
            if(grab)
            {
                _busy=false;
                if(_from!=null&&_to!=null&&_from.Ready&&_to.Ready)
                {
                    string item=_from.Peek(x=>Wanted(x)&&_to.CanPut(x));
                    if(item==null&&_burner&&Burn<=0f&&FuelStore.Empty)item=_from.Peek(Fuel.Is);   // (its own fire first)
                    _busy=item!=null;   // (wanting power even before it has any: the solver only feeds what wants to work)
                    if(item!=null&&(!_electric||Powered>0f)&&_from.Take(item)){_hand=item;Dirty();}
                }
            }
            else if(release)
            {
                if(_to!=null&&_to.Ready&&_to.Put(_hand)){_hand=null;Dirty();}
            }
            if(moving)_busy=true;
            // how fast the arm may move this step
            if(_electric)_pace=Powered;
            else if(_burner)_pace=moving||_hand!=null?(Burning(dt)?1f:0f):1f;
            else _pace=1f;
        }

        protected override void Update()
        {
            base.Update();
            if(View==null||!View.IsValid()||_arm==null)return;
            float pace=Owner?_pace:1f;
            float target=_hand!=null?180f:0f;
            _angle=Mathf.MoveTowards(_angle,target,180f/_halfSwing*pace*Time.deltaTime);
            _arm.localRotation=Quaternion.Euler(0f,_angle,0f);
            if(_heldShown!=_hand)
            {
                ItemView.Give(_held);_held=null;_heldShown=_hand;
                if(_hand!=null&&_claw!=null){_held=ItemView.Take(_hand,_claw,0.2f);if(_held!=null)_held.transform.localPosition=new Vector3(0,-0.12f,0);}
            }
        }
        protected override void OnDestroy()
        {
            _held=null;   // (a child: goes with it)
            base.OnDestroy();
        }
        internal void Hide(){ItemView.Give(_held);_held=null;_heldShown=null;}

        // ---- players ----
        protected override string MenuHint=>_filter?"Clear the filter":null;
        protected override bool Menu(Humanoid user)
        {
            if(!_filter)return false;
            if(!Owner)View.ClaimOwnership();
            _filterItem=null;Dirty();Save();
            (user as Player)?.Message(MessageHud.MessageType.Center,"Filter cleared: moves anything");
            return true;
        }
        public override bool UseItem(Humanoid user,ItemDrop.ItemData item)
        {
            if(!_filter)return base.UseItem(user,item);
            string n=Ports.NameOf(item);
            if(n==null)return false;
            if(!Owner)View.ClaimOwnership();
            _filterItem=n;Dirty();Save();
            (user as Player)?.Message(MessageHud.MessageType.Center,"Filter: "+Ports.Title(n));
            return true;
        }
        public override bool Interact(Humanoid user,bool hold,bool alt)=>!hold&&alt&&Menu(user);
        public override string GetHoverText()
        {
            var sb=new StringBuilder(Title);
            if(_hand!=null)sb.Append("\nHolding ").Append(Ports.Title(_hand));
            if(_electric)sb.Append($"\nPower: {Mathf.RoundToInt(Powered*100)}% of {Watts(PowerUse)}");
            if(_burner)sb.Append("\nFuel: ").Append(List(FuelStore)).Append(Burn>0?$" (burning {Burn:0}s)":"");
            if(_filter)sb.Append("\nFilter: ").Append(_filterItem!=null?Ports.Title(_filterItem):"anything");
            if(_burner)sb.Append("\n[<color=yellow><b>1-8</b></color>] Add fuel");
            if(_filter)sb.Append("\n[<color=yellow><b>1-8</b></color>] Set the filter to that item\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Clear the filter");
            return Localization.instance.Localize(sb.ToString());
        }
    }
}
