using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // One of the horde. It marches on the Warstone by the game's own pathfinding (so walls turn it aside), fights only what stands
    // close, breaks through whatever blocks the way, and strikes the stone when it reaches it. Sappers go for planted staves first.
    internal sealed class Raider:MonoBehaviour
    {
        internal const string RoleKey="bob_sw_role",SiegeKey="bob_sw_siege",StoneKey="bob_sw_stone",TrickKey="bob_sw_trick",ChiefKey="bob_sw_chief";
        internal static readonly List<Raider> Loaded=new List<Raider>();
        private static readonly AccessTools.FieldRef<MonsterAI,Character> TargetCreature=AccessTools.FieldRefAccess<MonsterAI,Character>("m_targetCreature");
        private static readonly AccessTools.FieldRef<MonsterAI,StaticTarget> TargetStatic=AccessTools.FieldRefAccess<MonsterAI,StaticTarget>("m_targetStatic");
        private static readonly AccessTools.FieldRef<MonsterAI,float> UpdateTargetTimer=AccessTools.FieldRefAccess<MonsterAI,float>("m_updateTargetTimer");
        private static readonly AccessTools.FieldRef<MonsterAI,float> SinceAttacking=AccessTools.FieldRefAccess<MonsterAI,float>("m_timeSinceAttacking");
        private static readonly AccessTools.FieldRef<MonsterAI,float> SinceSensed=AccessTools.FieldRefAccess<MonsterAI,float>("m_timeSinceSensedTargetCreature");
        private static readonly System.Reflection.MethodInfo WakeUp=AccessTools.Method(typeof(MonsterAI),"Wakeup");
        private static readonly AccessTools.FieldRef<MonsterAI,bool> DespawnInDay=AccessTools.FieldRefAccess<MonsterAI,bool>("m_despawnInDay");
        private static readonly System.Reflection.MethodInfo SetAlerted=AccessTools.Method(typeof(BaseAI),"SetAlerted");
        private static readonly System.Reflection.MethodInfo SeesStatic=AccessTools.Method(typeof(BaseAI),"CanSeeTarget",new[]{typeof(StaticTarget)});
        private static readonly Collider[] Near=new Collider[64];
        private static int _pieces=-1;

        internal Role Role;internal long Siege;internal Vector3 StoneAt;internal Trick Trick=(Trick)(-1);internal string ChiefName;
        internal Character Body;private MonsterAI _ai;private ZNetView _view;
        private float _think,_stuckSince,_blockerUntil,_disbandAt=-1;private Vector3 _lastPos;
        private StaticTarget _blocker;private Warstone _stone;

        private void Awake()
        {
            _view=GetComponent<ZNetView>();Body=GetComponent<Character>();_ai=GetComponent<MonsterAI>();
            if(_view==null||!_view.IsValid()||Body==null||_ai==null){Destroy(this);return;}
            Read();
            Loaded.Add(this);
        }
        private void OnDestroy()=>Loaded.Remove(this);
        internal void Read()
        {
            ZDO z=_view.GetZDO();
            Role=(Role)z.GetInt(RoleKey,0);Siege=z.GetLong(SiegeKey,0L);StoneAt=z.GetVec3(StoneKey,transform.position);
            Trick=(Trick)z.GetInt(TrickKey,-1);ChiefName=z.GetString(ChiefKey,"");if(ChiefName=="")ChiefName=null;
            // Emboldened: no fear of fire, no fleeing, no going home at dawn.
            _ai.m_afraidOfFire=false;_ai.m_avoidFire=false;_ai.m_fleeIfLowHealth=0;_ai.m_fleeIfNotAlerted=false;_ai.m_fleeIfHurtWhenTargetCantBeReached=false;
            DespawnInDay(_ai)=false;
            Body.m_group="shieldwall_horde"; // one horde: greydwarfs and skeletons side by side, not at each other's throats
            if(Role==Role.Champion&&!_grown)
            {
                _grown=true;
                if(!Body.m_name.StartsWith("Warchief ")&&Body.m_name!=ChiefName){transform.localScale*=1.25f;Body.m_name=ChiefName??"Warchief "+Localization.instance.Localize(Body.m_name);} // not again after a reload
                // A red glow about him, so the warchief stands out in the crowd.
                if(transform.Find("WarchiefGlow")==null)
                {
                    var glow=new GameObject("WarchiefGlow");glow.transform.SetParent(transform,false);glow.transform.localPosition=Vector3.up*1.6f;
                    Light light=glow.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1f,0.2f,0.1f);light.range=5;light.intensity=2;light.shadows=LightShadows.None;
                }
            }
            // The chosen chief's guard: a cold blue glow, the same as the shield they hold over him.
            if(Role==Role.Guard&&transform.Find("GuardGlow")==null)
            {
                var glow=new GameObject("GuardGlow");glow.transform.SetParent(transform,false);glow.transform.localPosition=Vector3.up*1.4f;
                Light light=glow.AddComponent<Light>();light.type=LightType.Point;light.color=Shield;light.range=4;light.intensity=1.6f;light.shadows=LightShadows.None;
            }
        }
        internal static readonly Color Shield=new Color(0.35f,0.6f,1f);
        internal const string TrophyKey="bob_sw_trophy";
        // A warchief sworn as the chosen chief takes no blow while one of his guard lives.
        internal bool Shielded()
        {
            if(Role!=Role.Champion)return false;
            Warstone stone=Stone();
            if(stone==null||!Policy.Has(stone.Boasts,Boast.Chosen))return false;
            return Loaded.Any(r=>r!=null&&r!=this&&r.Role==Role.Guard&&r.Siege==Siege&&r.Body!=null&&!r.Body.IsDead());
        }
        // Everyone sees the chief's glow turn blue while he is shielded.
        private float _nextShieldLook;private Light _chiefLight;
        private void Update()
        {
            if(Role!=Role.Champion||Time.time<_nextShieldLook)return;
            _nextShieldLook=Time.time+0.5f;
            if(_chiefLight==null)_chiefLight=transform.Find("WarchiefGlow")?.GetComponent<Light>();
            if(_chiefLight!=null)_chiefLight.color=Shielded()?Shield:new Color(1f,0.2f,0.1f);
        }
        private bool _grown;

        internal static void Attach(GameObject go){if(go!=null&&go.GetComponent<Raider>()==null)go.AddComponent<Raider>();}
        internal static void DetachAll(){foreach(Raider r in Loaded.ToList())if(r!=null)Object.Destroy(r);Loaded.Clear();}
        internal static void AttachAll()
        {
            foreach(Character c in Character.GetAllCharacters())
            {
                ZNetView v=c!=null?c.GetComponent<ZNetView>():null;
                if(v==null||!v.IsValid()||v.GetZDO().GetInt(RoleKey,0)==0)continue;
                foreach(MonoBehaviour stale in c.GetComponents<MonoBehaviour>())if(stale!=null&&stale.GetType().Name==nameof(Raider)&&stale.GetType()!=typeof(Raider))Object.DestroyImmediate(stale);
                Attach(c.gameObject);
            }
        }

        internal string Doing()
        {
            Character c=TargetCreature(_ai);StaticTarget t=TargetStatic(_ai);
            if(c!=null)return "fighting "+c.m_name;
            if(t==null)return _disbandAt>0?"disbanding":"idle";
            if(t==_blocker)return "breaking "+Utils.GetPrefabName(t.gameObject);
            if(t.GetComponent<Warstone>()!=null)return _seesStone?"striking the stone":Vector3.Distance(t.FindClosestPoint(transform.position),transform.position)<Policy.Reach+1?"at the stone, blocked":"marching";
            if(t.GetComponent<Planted>()!=null)return "sapping a stave";
            return "targeting "+t.name;
        }
        internal Warstone Stone()
        {
            if(_stone!=null&&_stone.View!=null&&_stone.View.IsValid())return _stone;
            _stone=Warstone.Loaded.FirstOrDefault(w=>w!=null&&Vector3.Distance(w.transform.position,StoneAt)<3);
            return _stone;
        }

        // Runs on the game that owns this raider, before the game's own AI: it picks the targets, the game does the walking and fighting.
        // What its wits are doing, for finding out why a raider stands still (Claude Tools: siege why).
        internal Newtonsoft.Json.Linq.JObject Why(Warstone stone)
        {
            var o=new Newtonsoft.Json.Linq.JObject();
            o["name"]=Body.GetHoverName();o["role"]=Role.ToString();o["owned"]=Owned;
            o["sleeping"]=_ai.IsSleeping();o["alerted"]=_ai.IsAlerted();o["agent"]=_ai.m_pathAgentType.ToString();
            o["targetStatic"]=TargetStatic(_ai)!=null?TargetStatic(_ai).name:"";o["targetCreature"]=TargetCreature(_ai)!=null?TargetCreature(_ai).GetHoverName():"";
            o["speed"]=System.Math.Round(Body.GetVelocity().magnitude,2);o["onGround"]=Body.IsOnGround();o["swimming"]=Body.IsSwimming();
            o["inAttack"]=Body.InAttack();o["staggering"]=Body.IsStaggering();o["afraidOfFire"]=_ai.m_afraidOfFire;o["avoidFire"]=_ai.m_avoidFire;
            o["fireNear"]=EffectArea.IsPointInsideArea(transform.position,EffectArea.Type.Fire,3f)!=null;
            o["despawnInDay"]=DespawnInDay(_ai);o["eventCreature"]=_ai.IsEventCreature();o["huntPlayer"]=_ai.HuntPlayer();
            if(stone!=null&&Pathfinding.instance!=null)
            {Vector3 door=stone.Target.FindClosestPoint(transform.position);o["pathToStone"]=Pathfinding.instance.HavePath(transform.position,door,_ai.m_pathAgentType);}
            o["sinceThought"]=System.Math.Round(SinceThought,2);o["stuckFor"]=System.Math.Round(Time.time-_stuckSince,1);
            return o;
        }
        internal bool Owned=>_view!=null&&_view.IsValid()&&_view.IsOwner();
        internal bool AiOn=>_ai!=null&&_ai.isActiveAndEnabled;
        internal float SinceThought=>Time.time-_thought;private float _thought;
        internal void Think(float dt)
        {
            _thought=Time.time;
            if(!_view.IsOwner()||Body.IsDead())return;
            UpdateTargetTimer(_ai)=5; // the game never picks targets for a raider: Think does
            SinceSensed(_ai)=0;SinceAttacking(_ai)=0; // nor gives up on them (it drops every target after 30 s without a foe in sight)
            Warstone stone=Stone();
            if(stone==null||stone.Phase!=Phase.Battle||stone.Siege!=Siege){Disband(stone);return;}
            _disbandAt=-1;
            if(Time.time<_think)return;
            _think=Time.time+0.5f;
            if(!_ai.IsAlerted())SetAlerted.Invoke(_ai,new object[]{true});
            if(_ai.IsSleeping())WakeUp.Invoke(_ai,null); // some kinds (ulvs, crypt dead) come out of the rift asleep, as they lie in the wild
            if(Role==Role.Champion)Tricks(stone);

            // A foe close by: fight it (the game chases and strikes), but never far from the road.
            Character current=TargetCreature(_ai);
            float engage=Role==Role.Sapper?4:Role==Role.Flyer?14:Role==Role.Champion?12:10;
            if(current!=null&&(current.IsDead()||current.GetComponent<Raider>()!=null||Vector3.Distance(current.transform.position,transform.position)>engage+8))current=null;
            Character foe=current??Foe(engage);
            if(foe!=null){TargetCreature(_ai)=foe;TargetStatic(_ai)=null;_stuckSince=Time.time;_lastPos=transform.position;return;}
            TargetCreature(_ai)=null;

            // Towers: sappers make for the nearest powered stave socket, and a raider a stave just shot sometimes turns on its tower.
            // A socket up on a tower is reached through the tower: the piece under it is torn down first.
            Planted tower=null;
            if(Role==Role.Sapper&&Time.time>_sapUntil)tower=Planted.Nearest(transform.position,40);
            if(tower==null&&_vengeance!=null&&Time.time<_vengeanceUntil&&_vengeance.Holding)tower=_vengeance;
            if(tower!=null)
            {
                StaticTarget aim=Footing(tower);
                if(_blocker!=null&&_blocker.gameObject.activeInHierarchy&&Time.time<_blockerUntil)aim=_blocker;
                TargetStatic(_ai)=aim;
                if(Vector3.Distance(transform.position,_lastPos)>1){_lastPos=transform.position;_stuckSince=Time.time;}
                else if(Time.time-_stuckSince>4&&!Body.InAttack())
                {
                    // Walled off from it: break toward it; after long enough, give up and march with the rest.
                    _blocker=Blocker(tower.transform.position);
                    if(_blocker!=null){_blockerUntil=Time.time+20;_stuckSince=Time.time;}
                    else if(Time.time-_stuckSince>10){_sapUntil=Time.time+30;_vengeance=null;_stuckSince=Time.time;}
                }
                return;
            }
            // Breaking through: a piece in the way, until it falls or a while passes.
            if(_blocker!=null&&_blocker.gameObject.activeInHierarchy&&Time.time<_blockerUntil){TargetStatic(_ai)=_blocker;return;}
            _blocker=null;
            TargetStatic(_ai)=stone.Target;

            // Stuck: short of the stone, or beside it with something in between (a wall, a porch rail). Break the player-built piece
            // in the way, but only when the road is really shut (a raider merely snagged on a corner walks on), or after a long while.
            float toStone=Vector3.Distance(stone.Target.FindClosestPoint(transform.position),transform.position);
            bool close=toStone<=Policy.Reach+1;
            _seesStone=close&&(bool)SeesStatic.Invoke(_ai,new object[]{stone.Target});
            if(Vector3.Distance(transform.position,_lastPos)>1||_seesStone){_lastPos=transform.position;_stuckSince=Time.time;}
            else if(Time.time-_stuckSince>3&&!Body.IsFlying()&&!Body.InAttack())
            {
                bool shut=close||Time.time-_stuckSince>10||Pathfinding.instance==null||
                    !Pathfinding.instance.HavePath(transform.position,stone.Target.FindClosestPoint(transform.position),_ai.m_pathAgentType);
                _blocker=shut?Blocker(stone.transform.position):null;
                if(_blocker!=null){_blockerUntil=Time.time+25;TargetStatic(_ai)=_blocker;_stuckSince=Time.time;}
                // Shut by the land itself (a moat, a cliff): nothing to break, so they build a ramp. Diggers start at once, the rest in a while.
                else if(shut&&!close&&(Role==Role.Digger||Time.time-_stuckSince>8))Earthwork(stone.transform.position);
                // A warchief does not sit out his own siege: stuck long enough, he leaps for the stone whatever his trick.
                if(Role==Role.Champion&&!close&&Time.time-_stuckSince>12&&Leap(stone))_stuckSince=Time.time;
            }
            if(Role==Role.Champion&&Plugin.Instance.DebugHits.Value&&Time.time>=_nextLog)
            {_nextLog=Time.time+5;Plugin.Log($"[chief] {ChiefName} {Doing()} at {transform.position:F0}, {toStone:0} m from the stone, still for {Time.time-_stuckSince:0} s");}
            // Diggers that reach the stone dig at its foot: let them, and it topples.
            if(Role==Role.Digger&&_seesStone&&Time.time>=_nextDig&&TerrainTurn()){_nextDig=Time.time+4;Undermine(stone);}
        }
        private bool _seesStone;private float _nextDig,_nextRamp;

        // ---- the warchief's trick (on the game that owns him) ----
        private float _nextTrick=-1;private int _raised;
        private static readonly AccessTools.FieldRef<Character,Rigidbody> BodyOf=AccessTools.FieldRefAccess<Character,Rigidbody>("m_body");
        private void Tricks(Warstone stone)
        {
            if(_nextTrick<0){_nextTrick=Time.time+Random.Range(4f,8f);return;}
            if(Time.time<_nextTrick||Body.InAttack())return;
            Vector3 at=transform.position;
            switch(Trick)
            {
                case Trick.Wallbreaker:
                {
                    // A ground slam: everything built within 4 m shakes.
                    _nextTrick=Time.time+12;
                    if(_pieces<0)_pieces=LayerMask.GetMask("piece","piece_nonsolid");
                    int count=Physics.OverlapSphereNonAlloc(at,4,Near,_pieces,QueryTriggerInteraction.Ignore);
                    var struck=new HashSet<WearNTear>();
                    for(int i=0;i<count;i++){WearNTear piece=Built(Near[i]);if(piece!=null)struck.Add(piece);}
                    if(struck.Count==0){_nextTrick=Time.time+2;return;}
                    Assets.Effect("vfx_troll_groundslam",at);Assets.Effect("sfx_trollfire_attack_slam_impact",at);
                    foreach(WearNTear piece in struck)
                    {
                        var hit=new HitData{m_point=piece.transform.position,m_dir=(piece.transform.position-at).normalized,m_toolTier=4,m_hitType=HitData.HitType.EnemyHit};
                        hit.m_damage.m_blunt=120;hit.SetAttacker(Body);
                        piece.Damage(hit);
                    }
                    break;
                }
                case Trick.Stonethrower:
                {
                    // A boulder at the nearest tower in sight, or the wall in his way.
                    _nextTrick=Time.time+10;
                    Planted tower=Planted.Nearest(at,35);
                    Vector3? aim=tower!=null?tower.transform.position+Vector3.up*1.2f:_blocker!=null?_blocker.transform.position+Vector3.up:(Vector3?)null;
                    if(aim==null){_nextTrick=Time.time+3;return;}
                    Throw(aim.Value);
                    break;
                }
                case Trick.Gravecaller:
                {
                    _nextTrick=Time.time+18;
                    if(_raised>=Policy.MaxRaised)return;
                    Assets.Effect("vfx_prespawn",at);
                    for(int i=0;i<2&&_raised<Policy.MaxRaised;i++,_raised++)
                    {
                        Vector2 off=Random.insideUnitCircle.normalized*Random.Range(2f,4f);
                        Director.Raise(stone,"Skeleton",at+new Vector3(off.x,0,off.y),Siege);
                    }
                    break;
                }
                case Trick.Leaper:
                {
                    // Over the wall: when he is stuck or a wall stands in his way, a great leap toward the stone.
                    _nextTrick=Time.time+3;
                    Vector3 to=stone.transform.position-at;to.y=0;
                    if(to.magnitude<7)return;
                    bool walled=_blocker!=null||Time.time-_stuckSince>2;
                    if(!walled||!Leap(stone))return;
                    _blocker=null;_stuckSince=Time.time;_nextTrick=Time.time+9;
                    break;
                }
                case Trick.Warcaller:
                {
                    _nextTrick=Time.time+15;
                    var hurt=Loaded.Where(r=>r!=null&&r.Siege==Siege&&r.Body!=null&&!r.Body.IsDead()&&Vector3.Distance(r.transform.position,at)<12&&r.Body.GetHealth()<r.Body.GetMaxHealth()*0.9f).ToList();
                    if(hurt.Count==0){_nextTrick=Time.time+3;return;}
                    Assets.Effect("sfx_goblinbrute_alerted",at);Assets.Effect("fx_DvergerMage_Support_start",at+Vector3.up);
                    foreach(Raider r in hurt)r.Body.Heal(r.Body.GetMaxHealth()*Policy.WarcryHeal,true);
                    break;
                }
                default:_nextTrick=Time.time+30;return; // Iron-hide's trick is always on
            }
            if(Plugin.Instance.DebugHits.Value)Plugin.Log($"[chief] {ChiefName}: {Trick}");
        }
        // A great leap toward the stone.
        private bool Leap(Warstone stone)
        {
            Vector3 to=stone.transform.position-transform.position;to.y=0;
            if(to.magnitude<7||Body.IsFlying()||!Body.IsOnGround())return false;
            Rigidbody rb=BodyOf(Body);
            if(rb==null)return false;
            rb.linearVelocity=to.normalized*Mathf.Min(12,to.magnitude)+Vector3.up*12;
            Assets.Effect("vfx_spawn_small",transform.position);
            return true;
        }
        private float _nextLog;
        private void Throw(Vector3 aim)
        {
            GameObject prefab=Assets.Find("troll_throw_projectile");
            if(prefab==null)return;
            Vector3 from=transform.position+Vector3.up*2.6f;
            float speed=24,gravity=prefab.GetComponent<Projectile>()?.m_gravity??0;
            Vector3 flat=aim-from;flat.y=0;
            Vector3 dir=(aim-from).normalized;
            if(flat.sqrMagnitude>0.01f&&Policy.Arc(flat.magnitude,aim.y-from.y,speed,gravity,out double angle))
                dir=Quaternion.AngleAxis(-(float)angle*Mathf.Rad2Deg,Vector3.Cross(flat.normalized,Vector3.up)*-1)*flat.normalized;
            GameObject rock=Object.Instantiate(prefab,from,Quaternion.LookRotation(dir));
            var hit=new HitData{m_toolTier=4,m_pushForce=20,m_hitType=HitData.HitType.EnemyHit};
            hit.m_damage.m_blunt=90;hit.SetAttacker(Body);
            rock.GetComponent<IProjectile>()?.Setup(Body,dir*speed,-1,hit,null,null);
            Assets.Effect("sfx_trollfire_attack_throw",from);
        }
        // A ramp of earth: the ground just ahead (toward the stone) raised to where the raider stands, a little at a time.
        // Every change to the ground resends its whole zone's terrain record (kilobytes): the horde shares one earthwork a second.
        private static float _nextTerrain;
        private static bool TerrainTurn(){if(Time.time<_nextTerrain)return false;_nextTerrain=Time.time+1;return true;}
        private void Earthwork(Vector3 stone)
        {
            if(Time.time<_nextRamp||!TerrainTurn())return;
            _nextRamp=Time.time+(Role==Role.Digger?1.5f:3f);
            Vector3 toward=stone-transform.position;toward.y=0;toward.Normalize();
            // A step a metre above where it stands, up to the land around the stone (or level with it, to fill a ditch): a ramp, one step at a time.
            float top=ZoneSystem.instance.GetGroundHeight(stone,out float g)?g:stone.y;
            Vector3 at=transform.position+toward*1.8f;
            at.y=Mathf.Min(transform.position.y+1f,top)-0.3f;
            Assets.Raise(at);
            Assets.Effect("vfx_Place_stone_wall_2x1",at);
        }
        private void Undermine(Warstone stone)
        {
            Vector3 foot=stone.Target.FindClosestPoint(transform.position);
            if(ZoneSystem.instance.GetGroundHeight(foot,out float ground))foot.y=ground;
            Vector3 under=Vector3.Lerp(foot,stone.transform.position,0.5f);under.y=foot.y;
            Assets.Dig(under);
        }private float _sapUntil,_vengeanceUntil;private Planted _vengeance;
        // The piece to strike to bring a socket down: the socket itself if it is within reach of the ground, else what holds it up.
        private static readonly RaycastHit[] Under=new RaycastHit[8];
        private StaticTarget Footing(Planted socket)
        {
            if(socket.transform.position.y-transform.position.y<2.2f)return socket.Target;
            int count=Physics.RaycastNonAlloc(socket.transform.position+Vector3.down*0.05f,Vector3.down,Under,30,LayerMask.GetMask("piece"),QueryTriggerInteraction.Ignore);
            WearNTear lowest=null;float y=float.MaxValue;
            for(int i=0;i<count;i++)
            {
                WearNTear piece=Built(Under[i].collider);
                if(piece==null||piece.gameObject==socket.gameObject)continue;
                float py=Under[i].point.y;
                if(py<y){lowest=piece;y=py;} // the tower's foot: the lowest piece under the socket
            }
            return lowest!=null?lowest.GetComponent<StaticTarget>():socket.Target;
        }
        // Struck by a stave: now and then a raider turns on the tower that shot it (for a while).
        internal static readonly Dictionary<Character,(Planted socket,float at)> LastShot=new Dictionary<Character,(Planted,float)>();
        internal static void ShotBy(Character c,Planted socket)
        {
            LastShot[c]=(socket,Time.time);
            Raider r=c.GetComponent<Raider>();
            if(r==null||r._vengeance!=null&&Time.time<r._vengeanceUntil)return;
            if(Vector3.Distance(c.transform.position,socket.transform.position)<15&&Random.value<(r.Role==Role.Champion?0.25f:0.12f)){r._vengeance=socket;r._vengeanceUntil=Time.time+20;}
        }
        private Character Foe(float range)
        {
            Character best=null;float bestDistance=range;
            bool passive=ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs); // a passive world: they only fight back
            foreach(Character c in Character.GetAllCharacters())
            {
                if(c==null||c==Body||c.IsDead()||c.GetComponent<Raider>()!=null)continue;
                if(!(c.IsPlayer()||c.IsTamed()||c.GetFaction()==Character.Faction.Players))continue; // players, their tames and companions
                if(passive)continue;
                if(!BaseAI.IsEnemy(Body,c))continue;
                float d=Vector3.Distance(c.transform.position,transform.position);
                if(d<bestDistance&&_ai.CanSeeTarget(c)){best=c;bestDistance=d;}
            }
            return best;
        }
        // What stands between this raider and the stone: the first player-built piece on the straight line to it (at knee, chest and
        // head height), else a piece right in front of it. Never something off to the side, like a house the road runs past.
        private static readonly RaycastHit[] Hits=new RaycastHit[16];
        private StaticTarget Blocker(Vector3 stone)
        {
            if(_pieces<0)_pieces=LayerMask.GetMask("piece","piece_nonsolid");
            Vector3 toward=stone-transform.position;toward.y=0;
            float far=toward.magnitude;toward.Normalize();
            WearNTear best=null;float bestDistance=float.MaxValue;
            foreach(float height in new[]{0.5f,1.2f,2f})
            {
                Vector3 from=transform.position+Vector3.up*height;
                int count=Physics.RaycastNonAlloc(from,toward,Hits,Mathf.Min(far,6),_pieces,QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++)
                {
                    WearNTear piece=Built(Hits[i].collider);
                    if(piece!=null&&Hits[i].distance<bestDistance){best=piece;bestDistance=Hits[i].distance;}
                }
            }
            if(best==null)
            {
                int count=Physics.OverlapSphereNonAlloc(transform.position+toward*1.2f+Vector3.up,1.6f,Near,_pieces,QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++)
                {
                    WearNTear piece=Built(Near[i]);
                    if(piece==null)continue;
                    Vector3 to=Near[i].ClosestPoint(transform.position)-transform.position;to.y=0;
                    if(to.sqrMagnitude>0.01f&&Vector3.Dot(to.normalized,toward)<0.4f)continue; // not ahead
                    if(to.magnitude<bestDistance){best=piece;bestDistance=to.magnitude;}
                }
            }
            if(best==null)return null;
            StaticTarget target=best.GetComponent<StaticTarget>();
            if(target==null){target=best.gameObject.AddComponent<StaticTarget>();target.m_primaryTarget=false;target.m_randomTarget=false;}
            return target;
        }
        private static WearNTear Built(Collider c)
        {
            WearNTear piece=c!=null?c.GetComponentInParent<WearNTear>():null;
            return piece!=null&&piece.GetComponent<Piece>() is Piece p&&p.GetCreator()!=0?piece:null;
        }
        // The siege is over (or this one belongs to none): melt away in a puff of smoke after a moment.
        // Gone from the field at a siege's end: here if this game owns it, else its owner's game disbands it as it thinks.
        internal void Melt()
        {
            if(_view==null||!_view.IsValid())return;
            if(!_view.IsOwner()&&!_view.GetZDO().HasOwner())_view.ClaimOwnership(); // frozen out where no game runs it
            if(!_view.IsOwner())return;
            Assets.Effect("vfx_spawn_small",transform.position+Vector3.up*0.5f);
            ZNetScene.instance.Destroy(gameObject);
        }
        private void Disband(Warstone stone)
        {
            TargetCreature(_ai)=null;TargetStatic(_ai)=null;
            if(_disbandAt<0){_disbandAt=Time.time+Random.Range(1.5f,7f);return;}
            if(Time.time<_disbandAt)return;
            Assets.Effect("vfx_spawn_small",transform.position+Vector3.up*0.5f);
            ZNetScene.instance.Destroy(gameObject);
        }
    }

    [HarmonyPatch(typeof(MonsterAI),nameof(MonsterAI.UpdateAI))]
    internal static class MarchOnTheStone
    {
        private static void Prefix(MonsterAI __instance,float dt)
        {
            Raider raider=__instance.GetComponent<Raider>();
            if(raider!=null)raider.Think(dt);
        }
    }
    // Raiders made on another game: recognise them when they appear here.
    [HarmonyPatch(typeof(Character),"Awake")]
    internal static class KnowRaiders
    {
        private static void Postfix(Character __instance)
        {
            ZNetView view=__instance.GetComponent<ZNetView>();
            if(view!=null&&view.IsValid()&&view.GetZDO().GetInt(Raider.RoleKey,0)!=0)Raider.Attach(__instance.gameObject);
        }
    }
    // What they carry: sometimes a warshard (a warchief always several), and their heads more often than wild ones.
    [HarmonyPatch(typeof(CharacterDrop),nameof(CharacterDrop.GenerateDropList))]
    internal static class RaiderSpoils
    {
        private static void Postfix(CharacterDrop __instance,List<KeyValuePair<GameObject,int>> __result)
        {
            Raider raider=__instance.GetComponent<Raider>();
            if(raider==null||__result==null)return;
            int shards=Policy.Carried(raider.Role,Random.value,raider.Stone()?.Boons.Contains("luck")==true);
            GameObject shard=Items.Get(Policy.ShardPrefab);
            if(shards>0&&shard!=null)__result.Add(new KeyValuePair<GameObject,int>(shard,shards));
            // A warchief always leaves his trophy, and it carries his name.
            if(raider.Role==Role.Champion)
            {
                GameObject chiefTrophy=__instance.m_drops.Select(d=>d.m_prefab).FirstOrDefault(p=>p!=null&&p.name.StartsWith("Trophy"));
                __result.RemoveAll(d=>d.Key!=null&&d.Key==chiefTrophy);
                if(chiefTrophy!=null&&chiefTrophy.GetComponent<ItemDrop>() is ItemDrop drop)
                {
                    ItemDrop.ItemData data=drop.m_itemData.Clone();data.m_dropPrefab=chiefTrophy;
                    data.m_customData[Raider.TrophyKey]=raider.ChiefName??raider.Body.m_name;
                    ItemDrop.DropItem(data,1,__instance.transform.position+Vector3.up,Quaternion.identity);
                }
                return;
            }
            if(__result.All(d=>d.Key==null||!d.Key.name.StartsWith("Trophy"))&&Random.value<0.15f)
            {
                GameObject trophy=__instance.m_drops.Select(d=>d.m_prefab).FirstOrDefault(p=>p!=null&&p.name.StartsWith("Trophy"));
                if(trophy!=null)__result.Add(new KeyValuePair<GameObject,int>(trophy,1));
            }
        }
    }
    // The chosen chief: blows on him do nothing while a guard lives (on the game that owns him, which applies the damage).
    [HarmonyPatch(typeof(Character),nameof(Character.ApplyDamage))]
    internal static class ChosenChief
    {
        private static bool Prefix(Character __instance,HitData hit)
        {
            if(!(__instance.GetComponent<Raider>() is Raider r))return true;
            if(r.Trick==Trick.Ironhide&&r.Role==Role.Champion&&hit.m_hitType==HitData.HitType.Turret)hit.m_damage.Modify(Policy.IronhideStaves); // Iron-hide: staves bite half as deep
            if(!r.Shielded())return true;
            DamageText.instance?.ShowText(HitData.DamageModifier.Immune,hit.m_point,0);
            return false;
        }
    }
    // Warchiefs are siege breakers: their blows on walls and towers land half again as hard.
    [HarmonyPatch(typeof(WearNTear),nameof(WearNTear.Damage))]
    internal static class Breakers
    {
        private static void Prefix(WearNTear __instance,HitData hit)
        {
            if(hit==null)return;
            Character attacker=hit.GetAttacker();
            if(attacker==null||!(attacker.GetComponent<Raider>() is Raider r))return;
            if(r.Role==Role.Champion)hit.m_damage.Modify(r.Trick==Trick.Wallbreaker?Policy.WallbreakerHit:Policy.ChiefHit);
            if(Plugin.Instance.DebugHits.Value&&(__instance.GetComponent<Planted>()!=null||__instance.GetComponent<StationExtension>()!=null))
                Plugin.Log($"[hit] {r.Body.m_name} ({r.Role}, {r.Doing()}) struck {Utils.GetPrefabName(__instance.gameObject)} for {hit.GetTotalDamage():0}");
            // The stone's upgrades are warded: the horde barely scratches them.
            if(__instance.GetComponent<StationExtension>() is StationExtension ext&&ext.m_craftingStation!=null&&ext.m_craftingStation.m_name==Stone.StationName)hit.m_damage.Modify(0.25f);
        }
    }
}
