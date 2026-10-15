using UnityEngine;

namespace Factorheim
{
    // A metre of belt: one lane down its middle, items flowing toward the piece's front.
    internal sealed class Belt:Conveyor
    {
        // metres a second: 1.875, 3.75 and 5.625 items a second at a quarter-metre apart (Factorio's yellow, red and blue)
        internal static float SpeedOf(string prefab)=>prefab=="FH_BeltExpress"?1.40625f:prefab=="FH_BeltFast"?0.9375f:0.46875f;
        protected override void Configure(string prefab)
        {
            Lanes=new[]{new Lane()};LaneX=new[]{0f};OutX=new[]{0f};Width=1f;
            Speed=SpeedOf(prefab);
        }
        protected override bool Deliver(int lane)
        {
            Lane l=Lanes[lane];
            if(!Out(0,l.Item[0]))return false;
            l.RemoveAt(0);return true;
        }
    }

    // Two metres wide: two lanes in, and what reaches the end goes out left, right, left, right (the other side if one is blocked).
    internal sealed class Splitter:Conveyor
    {
        private int _next;
        protected override void Configure(string prefab)
        {
            Lanes=new[]{new Lane(),new Lane()};LaneX=new[]{-0.5f,0.5f};OutX=new[]{-0.5f,0.5f};Width=2f;
            Speed=Belt.SpeedOf("FH_BeltFast");
        }
        protected override bool Deliver(int lane)
        {
            Lane l=Lanes[lane];
            string item=l.Item[0];
            int used=_next;
            if(!Out(used,item)){used=1-used;if(!Out(used,item))return false;}
            _next=1-used;
            l.RemoveAt(0);return true;
        }
    }
}
