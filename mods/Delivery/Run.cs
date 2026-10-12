using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Delivery
{
    // One errand: the order is called, the courier answers from far off, sets out after a while (sooner for those who tip), runs in from the
    // distance (by the navmesh where it reaches, round what's in his way, swimming where he must, straight through when he's stuck), hands over
    // the food for the coins, waits on a tip, and runs off again.
    internal static class Run
    {
        internal enum Stage{None,Waiting,Coming,Handing,Leaving}
        internal const string DataKey="bob_delivery";
        internal static Stage Now{get;private set;}
        internal static List<KeyValuePair<Meal,int>> Order=new List<KeyValuePair<Meal,int>>();
        internal static int Food,Fee;
        internal static string FeeWhy="";
        internal static int Total=>Food+Fee;
        internal static float SetsOutIn=>Now==Stage.Waiting?Mathf.Max(0,_until-Time.time):0;
        internal static bool Dyed=>_dyed;
        internal static bool Pinned=>_pin!=null;
        internal static Vector3? Where=>_body!=null?_body.transform.position:(Vector3?)null;
        internal static int TipOffered;
        internal static bool Free; // (tests: Claude Tools' "delivery free", nothing is paid)

        private static GameObject _body;
        private static Animator _anim;
        private static float _nearSince,_until,_stageAt,_speed,_y,_nextPath,_ghostUntil,_stuckAt,_handedAt;
        private static Vector3 _dir,_stuckFrom,_goal;
        private static readonly List<Vector3> Path=new List<Vector3>();
        private static int _pathAt;
        private static bool _hornBlown,_swimming,_dyed;
        private static Minimap.PinData _pin;
        private static int _lineSeed;
        private static int _groundMask,_blockMask;
        private static readonly int Forward=Animator.StringToHash("forward_speed"),InWater=Animator.StringToHash("inWater"),OnGround=Animator.StringToHash("onGround");

        internal static Courier Info(Player p)
        {
            p.m_customData.TryGetValue(DataKey,out string text);
            Courier c=Policy.Load(text,Random.Range(1,int.MaxValue));
            if(text==null)Save(p,c);
            return c;
        }
        internal static void Save(Player p,Courier c)=>p.m_customData[DataKey]=Policy.Save(c);

        internal static (int fee,string why) FeeAt(Vector3 at)
        {
            string biome=WorldGenerator.instance!=null?WorldGenerator.instance.GetBiome(at).ToString():"Meadows";
            bool night=EnvMan.IsNight();
            return (Policy.Fee(biome,night),Policy.BiomeName(biome)+(night?", by night":""));
        }

        // The order goes out: the calls are blown already; the end call says that's all.
        internal static bool Send(Player me,List<KeyValuePair<Meal,int>> order)
        {
            if(Now!=Stage.None||order.Count==0)return false;
            Courier c=Info(me);
            Order=order.ToList();
            Food=Order.Sum(kv=>kv.Key.Price*kv.Value);
            (Fee,FeeWhy)=FeeAt(me.transform.position);
            Calls.Blow(Policy.EndCall);
            Now=Stage.Waiting;_stageAt=Time.time;
            _until=Time.time+Policy.Wait(Plugin.WaitSeconds.Value,c.Regard);
            _dir=Direction(me.transform.position,Random.Range(0f,360f));
            _lineSeed=Random.Range(0,int.MaxValue);
            Vector3 answerAt=me.transform.position+_dir*Plugin.CourierDistance.Value*1.5f+Vector3.up*10;
            Calls.From(answerAt,Policy.Answer,true);
            me.Message(MessageHud.MessageType.Center,$"A horn answers from the {Policy.Compass(_dir.x,_dir.z)}: {c.Name} has heard you.");
            Plugin.Log($"Order: {Policy.Order(Order.Select(kv=>new KeyValuePair<string,int>(kv.Key.Prefab,kv.Value)))}; {Food}+{Fee} coins; sets out in {_until-Time.time:0} s.");
            return true;
        }
        internal static void Hurry(){if(Now==Stage.Waiting)_until=Time.time;}

        internal static void Tick()
        {
            if(Now==Stage.None)return;
            if(_body!=null&&!_dyed)_dyed=Body.Dye(_body);
            Pin();
            Player me=Player.m_localPlayer;
            if(me==null||me.IsDead()){Cancel(me!=null?$"{Name(me)} turns back.":null);return;}
            switch(Now)
            {
                case Stage.Waiting:
                    if(Time.time>=_until&&!me.InInterior()&&!me.IsTeleporting())SetOut(me);
                    break;
                case Stage.Coming:Come(me);break;
                case Stage.Handing:
                    Face(me.transform.position);
                    Animate(0,false);
                    if(TipOffered>=0||Time.time>_handedAt+Plugin.TipWait.Value)Leave(me);
                    break;
                case Stage.Leaving:Go(me);break;
            }
        }

        // Where he comes from: a way in that keeps out of the water as much as it can, starting with the way he answered from.
        private static Vector3 Direction(Vector3 from,float firstAngle)
        {
            float d=Plugin.CourierDistance.Value;
            Vector3 best=Quaternion.Euler(0,firstAngle,0)*Vector3.forward;
            int bestWet=int.MaxValue;
            for(int k=0;k<12;k++)
            {
                Vector3 dir=Quaternion.Euler(0,firstAngle+k*30f,0)*Vector3.forward;
                int wet=0;
                for(int i=1;i<=10;i++)
                {
                    Vector3 p=from+dir*(d*i/10f);
                    float ground=ZoneSystem.instance!=null?ZoneSystem.instance.GetGroundHeight(p):0;
                    if(ground<ZoneSystem.instance?.m_waterLevel-0.5f)wet+=i==10?5:1;
                }
                if(wet<bestWet){bestWet=wet;best=dir;}
                if(wet==0)break;
            }
            return best;
        }

        private static void SetOut(Player me)
        {
            _dir=Direction(me.transform.position,Mathf.Atan2(_dir.x,_dir.z)*Mathf.Rad2Deg);
            Vector3 start=me.transform.position+_dir*Plugin.CourierDistance.Value;
            start.y=Ground(start,true);
            _body=Body.Make(Info(me),start,Quaternion.LookRotation(-_dir));
            if(_body==null){Cancel($"{Name(me)} couldn't come.");return;}
            _anim=_body.GetComponentInChildren<Animator>();
            _y=start.y;_speed=0;_hornBlown=false;_ghostUntil=0;_dyed=false;
            _stuckAt=Time.time;_stuckFrom=start;_nextPath=0;Path.Clear();_nearSince=Time.time;
            Now=Stage.Coming;_stageAt=Time.time;
            me.Message(MessageHud.MessageType.TopLeft,$"{Name(me)} is running your order in from the {Policy.Compass(_dir.x,_dir.z)}.");
        }

        private static void Come(Player me)
        {
            Vector3 mine=me.transform.position;
            if(me.InInterior()){Animate(0,_swimming);return;} // (he waits at the door of a dungeon)
            Vector3 at=_body.transform.position;
            Vector3 flat=mine-at;flat.y=0;
            if(flat.magnitude>Plugin.CourierDistance.Value*3){Teleported(me);return;}
            if(!_hornBlown&&flat.magnitude<35){_hornBlown=true;Calls.From(at+Vector3.up*1.7f,Policy.Arrived,false);}
            float speed=Policy.RunSpeed(Info(me).Regard);
            bool ghost=Time.time<_ghostUntil||Time.time-_stageAt>Plugin.GiveUpSeconds.Value;
            Vector3 goal=Spot(mine,-flat.normalized);
            // there: at his spot, or close by you and not getting closer
            if(flat.magnitude>3.5f)_nearSince=Time.time;
            if(Mathf.Abs(mine.y-at.y)<2.5f&&(Flat(goal-at)<0.6f||flat.magnitude<1.2f||Time.time-_nearSince>3f)){Arrive(me);return;}
            Vector3 step=Steer(at,goal,ghost);
            Move(step,speed,ghost);
            // stuck: barely any headway in three seconds, so through whatever it is
            if(Time.time-_stuckAt>3f)
            {
                Vector3 moved=_body.transform.position-_stuckFrom;moved.y=0;
                if(moved.magnitude<2f&&!ghost)_ghostUntil=Time.time+3f;
                _stuckAt=Time.time;_stuckFrom=_body.transform.position;
            }
        }
        private static void Teleported(Player me)
        {
            // You've gone far (a portal, a fast ship): he follows you there, arriving from the distance again.
            Object.Destroy(_body);_body=null;Unpin();
            Now=Stage.Waiting;_until=Time.time+5f;
        }

        // Where to stop: in front of you on his side, or the nearest clear spot round you (not in a tree).
        private static Vector3 Spot(Vector3 mine,Vector3 back)
        {
            if(_blockMask==0)_blockMask=LayerMask.GetMask("Default","static_solid","Default_small","piece","vehicle");
            if(back.sqrMagnitude<0.01f)back=Vector3.forward;
            foreach(float turn in new[]{0f,30f,-30f,60f,-60f,90f,-90f,120f,-120f,150f,-150f,180f})
            {
                Vector3 at=mine+Quaternion.Euler(0,turn,0)*back*1.7f;
                if(!Physics.CheckCapsule(at+Vector3.up*0.5f,at+Vector3.up*1.6f,0.35f,_blockMask))return at;
            }
            return mine+back*1.6f;
        }

        // Which way to go: along the navmesh path where there is one, else straight; round anything in the way.
        private static Vector3 Steer(Vector3 at,Vector3 goal,bool ghost)
        {
            Vector3 target=goal;
            if(!ghost&&Pathfinding.instance!=null)
            {
                if(Time.time>=_nextPath||(goal-_goal).sqrMagnitude>9)
                {
                    _nextPath=Time.time+1.2f;_goal=goal;
                    if(!Pathfinding.instance.GetPath(at,goal,Path,Pathfinding.AgentType.Humanoid))Path.Clear();
                    _pathAt=Path.Count>1?1:0;
                }
                while(_pathAt<Path.Count-1&&Flat(Path[_pathAt]-at)<1.0f)_pathAt++;
                if(_pathAt<Path.Count)target=Path[_pathAt];
            }
            Vector3 dir=target-at;dir.y=0;
            if(dir.sqrMagnitude<1e-4f)return Vector3.zero;
            dir.Normalize();
            if(ghost||Path.Count>1)return dir;
            if(_blockMask==0)_blockMask=LayerMask.GetMask("Default","static_solid","Default_small","piece","vehicle");
            Vector3 eye=at+Vector3.up*1.1f;
            if(!Physics.SphereCast(eye,0.35f,dir,out _,1.6f,_blockMask))return dir;
            foreach(float turn in new[]{35f,-35f,70f,-70f,105f,-105f})
            {
                Vector3 d=Quaternion.Euler(0,turn,0)*dir;
                if(!Physics.SphereCast(eye,0.35f,d,out _,1.6f,_blockMask))return d;
            }
            return dir;
        }
        private static float Flat(Vector3 v){v.y=0;return v.magnitude;}

        private static void Move(Vector3 dir,float speed,bool ghost)
        {
            float dt=Time.deltaTime;
            Vector3 at=_body.transform.position;
            float want=dir==Vector3.zero?0:(_swimming?speed*0.45f:speed);
            _speed=Mathf.MoveTowards(_speed,want,dt*10f);
            Vector3 next=at+dir*_speed*dt;
            float ground=Ground(next,ghost);
            float water=Floating.GetLiquidLevel(next+Vector3.up*0.5f);
            _swimming=water>ground+1.3f;
            float y=_swimming?water-1.45f:ground;
            _y=Mathf.Abs(y-_y)>3?y:Mathf.Lerp(_y,y,Mathf.Min(1,dt*14f));
            next.y=_y;
            _body.transform.position=next;
            if(dir!=Vector3.zero)_body.transform.rotation=Quaternion.Slerp(_body.transform.rotation,Quaternion.LookRotation(dir),Mathf.Min(1,dt*8f));
            Animate(_speed,_swimming);
        }
        private static float Ground(Vector3 p,bool terrainOnly)
        {
            if(_groundMask==0)_groundMask=LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","vehicle");
            int mask=terrainOnly?LayerMask.GetMask("terrain"):_groundMask;
            float from=(_body!=null?_body.transform.position.y:p.y)+1.6f;
            if(Physics.Raycast(new Vector3(p.x,from,p.z),Vector3.down,out RaycastHit hit,60f,mask))return hit.point.y;
            if(Physics.Raycast(new Vector3(p.x,p.y+400f,p.z),Vector3.down,out hit,800f,LayerMask.GetMask("terrain")))return hit.point.y;
            return ZoneSystem.instance!=null?ZoneSystem.instance.GetGroundHeight(p):p.y;
        }
        private static void Animate(float speed,bool swimming)
        {
            if(_anim==null)return;
            _anim.SetFloat(Forward,speed);
            _anim.SetBool(InWater,swimming);
            _anim.SetBool(OnGround,!swimming);
        }
        private static void Face(Vector3 at)
        {
            if(_body==null)return;
            Vector3 d=at-_body.transform.position;d.y=0;
            if(d.sqrMagnitude>0.01f)_body.transform.rotation=Quaternion.Slerp(_body.transform.rotation,Quaternion.LookRotation(d),Mathf.Min(1,Time.deltaTime*6f));
        }

        // The handing over: coins for the food (all of it, or none if you can't pay), then the tip.
        private static void Arrive(Player me)
        {
            Courier c=Info(me);
            _speed=0;Animate(0,false);
            Inventory inv=me.GetInventory();
            int coins=inv.CountItems("$item_coins");
            if(coins<Total&&!Free)
            {
                c.Regard=Policy.AfterStiffed(c.Regard);Save(me,c);
                Say(Policy.Pick(Policy.Stiffed,_lineSeed),me);
                me.Message(MessageHud.MessageType.Center,$"You can't pay {c.Name}: {Total} coins, and you have {coins}.");
                TipOffered=0;Now=Stage.Handing;_handedAt=Time.time-Plugin.TipWait.Value;
                return;
            }
            if(!Free)inv.RemoveItem("$item_coins",Total);
            var given=new List<KeyValuePair<Meal,int>>(Order);
            if(Policy.Extra(c.Regard,Random.value)&&Order.Count>0)
            {
                Meal extra=Order.Select(kv=>kv.Key).OrderBy(m=>m.Price).First();
                given.Add(new KeyValuePair<Meal,int>(extra,1));
                me.Message(MessageHud.MessageType.TopLeft,$"{c.Name} throws in an extra {extra.Name}, on the house.");
            }
            foreach(var kv in given)for(int i=0;i<kv.Value;i++)Give(me,kv.Key);
            c.Runs++;Save(me,c);
            if(_anim!=null)_anim.SetTrigger("emote_wave");
            Say(string.Format(Policy.Greeting(c.Regard,_lineSeed),me.GetPlayerName()),me);
            me.Message(MessageHud.MessageType.Center,$"{c.Name} hands you {Policy.Order(Order.Select(kv=>new KeyValuePair<string,int>(kv.Key.Name,kv.Value)))} for {Total} coins.");
            Sound("sfx_coins_placed",me.transform.position);
            TipOffered=-1;Now=Stage.Handing;_handedAt=Time.time;
            Tip.Open(c.Name,Total);
        }
        private static void Give(Player me,Meal meal)
        {
            if(me.GetInventory().AddItem(meal.Item,1))return;
            ItemDrop.ItemData d=meal.Data.Clone();d.m_dropPrefab=meal.Item;d.m_stack=1;
            ItemDrop.DropItem(d,1,me.transform.position+me.transform.forward*0.8f+Vector3.up*0.6f,Quaternion.identity);
        }
        // A tip chosen in the tip window (0 for none): taken here, outside the window's drawing.
        internal static void Tipped(int tip)
        {
            Player me=Player.m_localPlayer;
            if(Now!=Stage.Handing||TipOffered>=0||me==null)return;
            Courier c=Info(me);
            if(tip>0&&me.GetInventory().CountItems("$item_coins")>=tip)
            {
                me.GetInventory().RemoveItem("$item_coins",tip);
                c.Tipped+=tip;
                Sound("sfx_coins_placed",me.transform.position);
            }
            else tip=0;
            float before=c.Regard;
            c.Regard=Policy.AfterTip(c.Regard,tip,Total);Save(me,c);
            Say(string.Format(Policy.Thanks(tip,Total,_lineSeed),me.GetPlayerName()),me);
            if(tip>0&&_anim!=null)_anim.SetTrigger("emote_cheer");
            if(Policy.RegardName(before)!=Policy.RegardName(c.Regard))
                me.Message(MessageHud.MessageType.TopLeft,$"{c.Name} is {Policy.RegardName(c.Regard).ToLowerInvariant()} now{(c.Regard>before?": he'll come quicker.":".")}");
            TipOffered=tip;
        }

        private static void Leave(Player me)
        {
            if(TipOffered<0)Tipped(0);
            Tip.Close();
            Say(Policy.Pick(Policy.Leaving,_lineSeed+1),me);
            Now=Stage.Leaving;_stageAt=Time.time;
            Vector3 away=_body.transform.position-me.transform.position;away.y=0;
            _dir=away.sqrMagnitude>0.01f?away.normalized:_dir;
        }
        private static void Go(Player me)
        {
            if(Time.time-_stageAt<2.2f){Face(me.transform.position);Animate(0,false);return;} // (a word first)
            Vector3 at=_body.transform.position;
            Move(_dir,Policy.RunSpeed(Info(me).Regard),Time.time-_stageAt>8f);
            if(Flat(at-me.transform.position)>Plugin.CourierDistance.Value*0.8f||Time.time-_stageAt>40f)End();
        }

        // On the map while he's out: the game's player marker, with his name.
        private static void Pin()
        {
            Minimap map=Minimap.instance;
            if(map==null)return;
            if(_body==null||!Plugin.ShowOnMap.Value){Unpin();return;}
            if(_pin==null)_pin=map.AddPin(_body.transform.position,Minimap.PinType.Player,Player.m_localPlayer!=null?Name(Player.m_localPlayer):"Courier",false,false);
            _pin.m_pos=_body.transform.position;
        }
        private static void Unpin()
        {
            if(_pin!=null&&Minimap.instance!=null)Minimap.instance.RemovePin(_pin);
            _pin=null;
        }

        private static void Say(string text,Player me)
        {
            if(_body==null||Chat.instance==null)return;
            Chat.instance.SetNpcText(_body,Vector3.up*2.1f,20f,5f,Name(me),text,false);
        }
        private static string Name(Player me)=>Info(me).Name;
        private static void Sound(string prefab,Vector3 at)
        {
            GameObject p=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(prefab):null;
            if(p!=null)Object.Instantiate(p,at,Quaternion.identity);
        }

        internal static void Cancel(string why)
        {
            if(why!=null)Player.m_localPlayer?.Message(MessageHud.MessageType.Center,why);
            End();
        }
        internal static void End()
        {
            Tip.Close();
            if(_body!=null)Object.Destroy(_body);
            _body=null;_anim=null;
            Unpin();
            Now=Stage.None;Order=new List<KeyValuePair<Meal,int>>();Food=Fee=0;Path.Clear();
        }
    }
}
