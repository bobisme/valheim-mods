using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // A stave set in a stave socket. The socket is a building piece (on the ground, a wall or a tower); the stave in it looses its magic at
    // foes in reach (a Hearth stave mends friends instead) and never hurts players, companions, tames or buildings. A Warstone feeds only
    // so many staves, nearest first, and only so far, both growing with its level. Use takes a stave out or sets one in; Shift+Use spends
    // warshards to strengthen it. If the horde brings the socket down, its stave falls where it stood.
    internal sealed class Planted:MonoBehaviour,Hoverable,Interactable
    {
        internal const string LegacyName="BobPlantedStave",ItemKey="bob_sw_item",KillsKey="bob_sw_kills";
        internal static readonly List<Planted> Loaded=new List<Planted>();
        // Our shots carry the socket's own item; that is how the friendly-fire guard (and the kill count) knows them.
        internal static readonly Dictionary<ItemDrop.ItemData,Planted> Shots=new Dictionary<ItemDrop.ItemData,Planted>();
        private static int _sight=-1;

        internal ZNetView View;internal StaticTarget Target;private WearNTear _wear;
        private ItemDrop.ItemData _item;private Stave _stave;
        private GameObject _look;private Light _light;
        private float _length=3,_next;
        internal bool Holding=>_item!=null;
        internal Vector3 Tip=>transform.position+Vector3.up*(Pieces.SocketTop+_length-0.5f);

        private void Awake()
        {
            View=GetComponent<ZNetView>();Target=GetComponent<StaticTarget>();_wear=GetComponent<WearNTear>();
            if(View==null||!View.IsValid())return;
            Loaded.Add(this);
            if(_wear!=null)_wear.m_onDestroyed+=Fallen;
            Load();
            _next=Time.time+Random.Range(0.5f,1.5f);
        }
        private void OnDestroy()
        {
            Loaded.Remove(this);
            if(_item!=null)Shots.Remove(_item);
            if(_wear!=null)_wear.m_onDestroyed-=Fallen;
        }
        // The stave in the socket, from the socket's saved data (it changes when someone sets, takes or strengthens one).
        private string _loaded="";
        internal void Load()
        {
            ZDO z=View.GetZDO();
            string prefab=z.GetString(ItemKey,"");
            string signature=prefab+"|"+z.GetInt(ZDOVars.s_quality,1);
            if(signature==_loaded)return;
            _loaded=signature;
            if(_item!=null)Shots.Remove(_item);
            _item=null;_stave=null;
            if(_look!=null)Object.Destroy(_look);
            if(_light!=null)Object.Destroy(_light.gameObject);
            GameObject source=string.IsNullOrEmpty(prefab)?null:Items.Get(prefab)??Assets.Find(prefab);
            ItemDrop drop=source!=null?source.GetComponent<ItemDrop>():null;
            if(drop==null)return;
            _item=drop.m_itemData.Clone();_item.m_dropPrefab=source;
            ItemDrop.LoadFromZDO(_item,z);
            _stave=Policy.Staves.FirstOrDefault(s=>s.Name==_item.m_shared.m_name);
            Shots[_item]=this;
            Build(source);
        }
        internal static void DetachAll(){foreach(Planted p in Loaded.ToList())if(p!=null){if(p._look!=null)Object.Destroy(p._look);if(p._light!=null)Object.Destroy(p._light.gameObject);Object.Destroy(p);}Loaded.Clear();Shots.Clear();}
        internal static void AttachAll()
        {
            foreach(ZNetView view in Assets.Instances(Pieces.SocketName.GetStableHashCode()))
            {
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())if(stale!=null&&stale.GetType().Name==nameof(Planted)&&stale.GetType()!=typeof(Planted))Object.DestroyImmediate(stale);
                foreach(Transform t in view.transform.Cast<Transform>().Where(t=>t.name=="StaveLook"||t.name=="StaveGlow").ToList())Object.Destroy(t.gameObject);
                if(view.GetComponent<Planted>()==null)view.gameObject.AddComponent<Planted>();
            }
        }

        // The stave stands in the pedestal head up (the head is the end farthest from where a hand holds it): big, thick and heavy.
        private void Build(GameObject source)
        {
            Transform attach=source.transform.Find("attach");
            _look=new GameObject("StaveLook");
            _look.transform.SetParent(transform,false);
            GameObject model=Assets.Model(attach!=null?attach.gameObject:source,_look.transform,Vector3.zero,Quaternion.identity,1,r=>r.GetComponent<MeshFilter>()!=null);
            if(model==null)return;
            Bounds local=Local(model,_look.transform);
            Vector3 head=local.center.sqrMagnitude>0.01f?local.center.normalized:Vector3.forward;
            model.transform.localRotation=Quaternion.FromToRotation(head,Vector3.up);
            Bounds upright=Local(model,_look.transform);
            model.transform.localPosition=new Vector3(-upright.center.x,-upright.min.y-0.35f,-upright.center.z);
            _look.transform.localScale=new Vector3(4.2f,2f,4.2f); // four times as thick, twice as long: no hand could swing it
            _look.transform.localPosition=new Vector3(0,Pieces.SocketTop,0);
            _length=Mathf.Clamp(upright.size.y*2f-0.7f,1.5f,5.5f);
            var lit=new GameObject("StaveGlow");lit.transform.SetParent(transform,false);lit.transform.localPosition=new Vector3(0,Pieces.SocketTop+_length-0.4f,0);
            _light=lit.AddComponent<Light>();_light.type=LightType.Point;_light.range=5;_light.intensity=1.2f;_light.shadows=LightShadows.None;
            _light.color=_stave!=null&&Items.Colours.TryGetValue(_stave.Kind,out var c)?c.glow*1.6f:new Color(0.6f,0.6f,1f);
        }
        private static Bounds Local(GameObject model,Transform frame)
        {
            bool any=false;Bounds b=new Bounds();
            foreach(MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if(f.sharedMesh==null)continue;
                Bounds m=f.sharedMesh.bounds;Matrix4x4 to=frame.worldToLocalMatrix*f.transform.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    Vector3 p=to.MultiplyPoint3x4(m.center+Vector3.Scale(m.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    if(!any){b=new Bounds(p,Vector3.zero);any=true;}else b.Encapsulate(p);
                }
            }
            return b;
        }

        // ---- power: the nearest stone in reach feeds it, if the stone has room (nearest staves first) ----
        private Warstone _stone;private bool _powered;private float _nextPower;private string _why="";
        internal Warstone Stone(){Power();return _powered?_stone:null;}
        private void Power()
        {
            if(Time.time<_nextPower)return;
            _nextPower=Time.time+1;
            _stone=null;_powered=false;
            if(_item==null){_why="Empty";return;}
            Warstone near=Warstone.Loaded.Where(w=>w!=null&&w.Z!=null).OrderBy(w=>Vector3.Distance(w.transform.position,transform.position)).FirstOrDefault();
            if(near==null){_why="Asleep: no Warstone near";return;}
            int level=near.Level;float reach=Policy.PowerRadius(level),d=Vector3.Distance(near.transform.position,transform.position);
            if(d>reach){_why=$"Asleep: {d:0} m from the Warstone, which reaches {reach:0} m (raise its level)";return;}
            int capacity=Policy.Capacity(level);
            int rank=Loaded.Count(p=>p!=null&&p!=this&&p._item!=null&&Vector3.Distance(p.transform.position,near.transform.position)<d);
            _stone=near;
            if(rank>=capacity){_why=$"Asleep: the Warstone feeds only {capacity} staves (raise its level)";return;}
            _powered=true;_why="";
        }
        internal static Planted Nearest(Vector3 at,float range)=>Loaded.Where(p=>p!=null&&p._item!=null&&p.Stone()!=null&&Vector3.Distance(p.transform.position,at)<=range)
            .OrderBy(p=>Vector3.Distance(p.transform.position,at)).FirstOrDefault();
        private int Quality=>_item?.m_quality??1;
        private float Range=>_stave!=null?Policy.RangeAt(_stave,Quality):Policy.BorrowedRange;
        private float Cooldown=>_stave!=null?_stave.Cooldown:Policy.BorrowedCooldown;

        // ---- watching and shooting (on the game that owns the socket) ----
        private float _nextLoad;
        private void Update()
        {
            if(View==null||!View.IsValid())return;
            if(Time.time>=_nextLoad){_nextLoad=Time.time+0.5f;Load();}
            if(_item==null)return;
            Warstone stone=Stone();
            if(_light!=null)_light.enabled=stone!=null;
            if(!View.IsOwner()||stone==null||Time.time<_next)return;
            _next=Time.time+Cooldown;
            if(_stave!=null&&_stave.Kind==StaveKind.Hearth){Mend(stone);return;}
            Character foe=Choose(stone);
            if(foe==null){_next=Time.time+0.5f;return;}
            Shoot(foe);
        }
        internal static bool IsFoe(Character c)
        {
            if(c==null||c.IsDead()||c.IsPlayer()||c.IsTamed()||c.GetFaction()==Character.Faction.Players)return false;
            if(c.GetComponent<Raider>()!=null)return true;
            Character.Faction f=c.GetFaction();
            if(f==Character.Faction.AnimalsVeg||f==Character.Faction.Dverger||f==Character.Faction.PlayerSpawned||f==Character.Faction.TrainingDummy)return false;
            BaseAI ai=c.GetBaseAI();
            return ai==null||!ai.IsAggravatable()||ai.IsAggravated(); // a calm passive thing (a Dverger, a hare) is left alone
        }
        // Who to shoot: Thunder seeks the strongest; the rest whoever is closest to the stone (the one about to strike it).
        private Character Choose(Warstone stone)
        {
            if(_sight<0)_sight=LayerMask.GetMask("Default","static_solid","terrain","piece");
            Vector3 tip=Tip;float range=Range;
            Character best=null;float bestScore=float.MaxValue;
            foreach(Character c in Character.GetAllCharacters())
            {
                if(!IsFoe(c))continue;
                Vector3 at=c.GetCenterPoint();
                float d=Vector3.Distance(at,tip);
                if(d>range)continue;
                float score=_stave!=null&&_stave.Kind==StaveKind.Thunder?-c.GetMaxHealth()+d*0.1f:Vector3.Distance(c.transform.position,stone.transform.position);
                if(score>=bestScore)continue;
                if(Physics.Linecast(tip,at,out RaycastHit hit,_sight,QueryTriggerInteraction.Ignore)&&hit.collider.GetComponentInParent<Character>()!=c&&hit.collider.GetComponentInParent<Planted>()!=this)continue;
                best=c;bestScore=score;
            }
            return best;
        }
        private void Shoot(Character foe)
        {
            Attack attack=_item.m_shared.m_attack;
            GameObject prefab=_stave!=null?Assets.Find(_stave.Projectile):attack?.m_attackProjectile;
            if(prefab==null)return;
            Projectile template=prefab.GetComponent<Projectile>();
            float speed=Mathf.Max(10,attack!=null&&attack.m_projectileVel>0?attack.m_projectileVel:30);
            float gravity=template!=null?template.m_gravity:0;
            Vector3 tip=Tip,at=foe.GetCenterPoint(),v=foe.GetVelocity();
            var lead=Policy.Lead((tip.x,tip.y,tip.z),(at.x,at.y,at.z),(v.x,v.y,v.z),speed);
            Vector3 aim=new Vector3((float)lead.x,(float)lead.y,(float)lead.z);
            Vector3 flat=aim-tip;flat.y=0;
            Vector3 dir=(aim-tip).normalized;
            if(Policy.Arc(flat.magnitude,aim.y-tip.y,speed,gravity,out double angle))
                dir=Quaternion.AngleAxis(-(float)angle*Mathf.Rad2Deg,Vector3.Cross(flat.normalized,Vector3.up)*-1)*flat.normalized;
            GameObject shot=Object.Instantiate(prefab,tip+dir*0.6f,Quaternion.LookRotation(dir));
            CinderSpawner cinders=shot.GetComponent<CinderSpawner>();
            if(cinders!=null)
            {
                Object.DestroyImmediate(cinders); // fire that cannot catch on your roof
                if(shot.GetComponent<Projectile>() is Projectile p)p.m_onHit=null; // the spawner's own hook on the shot
            }
            var hit=new HitData{m_pushForce=10,m_staggerMultiplier=1,m_hitType=HitData.HitType.Turret,m_itemWorldLevel=(byte)Game.m_worldLevel,m_blockable=true,m_dodgeable=true};
            if(_stave!=null)
            {
                float power=Policy.Power(Quality);
                switch(_stave.Type)
                {
                    case "fire":hit.m_damage.m_fire=_stave.Damage*power;break;
                    case "frost":hit.m_damage.m_frost=_stave.Damage*power;break;
                    case "lightning":hit.m_damage.m_lightning=_stave.Damage*power;break;
                    default:hit.m_damage.m_blunt=_stave.Damage*power;break;
                }
                hit.m_damage.m_blunt+=_stave.Splash*power;
            }
            else{hit.m_damage=_item.GetDamage();hit.m_damage.Modify(Policy.BorrowedPower);}
            StatusEffect status=_item.m_shared.m_attackStatusEffect;
            if(status!=null)hit.m_statusEffectHash=status.NameHash();
            shot.GetComponent<IProjectile>()?.Setup(null,dir*speed,-1,hit,_item,null);
            if(attack!=null)attack.m_startEffect.Create(tip,Quaternion.LookRotation(dir));
        }
        internal void Killed(){if(View!=null&&View.IsValid())View.GetZDO().Set(KillsKey,View.GetZDO().GetInt(KillsKey,0)+1);}
        // The Hearth stave: friends near it heal; during a siege the stone itself is mended too.
        private void Mend(Warstone stone)
        {
            float amount=_stave.Damage*Policy.Power(Quality);
            bool any=false;
            foreach(Character c in Character.GetAllCharacters())
            {
                if(c==null||c.IsDead()||!(c.IsPlayer()||c.IsTamed()||c.GetFaction()==Character.Faction.Players))continue;
                if(Vector3.Distance(c.transform.position,transform.position)>Range||c.GetHealth()>=c.GetMaxHealth()-0.5f)continue;
                c.Heal(amount,true);any=true;
            }
            // The stone: half a percent of its strength each pulse (more with upgrades), during a siege.
            ZDO z=stone.Z;
            if(stone.Phase==Phase.Battle&&Vector3.Distance(stone.transform.position,transform.position)<=Range+5&&z.GetFloat(Shieldwall.Stone.HealthKey,0)<z.GetFloat(Shieldwall.Stone.MaxHealthKey,0))
            {Net.Damage(stone,-z.GetFloat(Shieldwall.Stone.MaxHealthKey,0)*0.005f*Policy.Power(Quality),stone.transform.position);any=true;}
            if(any)Assets.Effect("vfx_HealthUpgrade",transform.position+Vector3.up*Pieces.SocketTop);
        }

        // ---- the socket brought down (by the horde, or taken down with the hammer): the stave falls where it stood ----
        private void Fallen()
        {
            if(_item==null||View==null||!View.IsValid()||!View.IsOwner())return;
            ItemDrop.DropItem(_item,1,transform.position+Vector3.up*1.2f,Quaternion.Euler(0,Random.Range(0,360f),90));
            Net.Say($"A tower has fallen! Its {_item.m_shared.m_name.ToLowerInvariant()} lies on the ground.",transform.position,60);
            View.GetZDO().Set(ItemKey,"");_item=null;
        }

        // ---- hover, setting, taking out and strengthening ----
        public string GetHoverName()=>_item!=null?_item.m_shared.m_name:"Stave socket";
        public float GetHoverOffset()=>0;
        public string GetHoverText()
        {
            if(View==null||!View.IsValid())return "";
            Power();
            if(_item==null)return Localization.instance.Localize("Stave socket\n[<color=yellow><b>$KEY_Use</b></color>] Set a stave from your bag");
            string stars=_item.m_quality>1?" "+new string('★',_item.m_quality-1):"";
            string name=_item.m_shared.m_name+stars+(_stave==null?" (borrowed: weak)":"");
            string state=!_powered?$"<color=#A0A0A0>{_why}</color>":_stave!=null&&_stave.Kind==StaveKind.Hearth?$"Mending friends within {Range:0} m":
                $"Watching {Range:0} m{(_stave!=null&&_stave.Kind==StaveKind.Thunder?", the strongest first":", the nearest to the stone first")}";
            int kills=View.GetZDO().GetInt(KillsKey,0);
            if(kills>0)state+=$" · {kills} slain";
            if(_wear!=null&&_wear.GetHealthPercentage()<0.99f)state+=$" · socket {Mathf.RoundToInt(_wear.GetHealthPercentage()*100)}%";
            string line=$"{name}\n{state}\n[<color=yellow><b>$KEY_Use</b></color>] Take it out";
            if(_stave!=null&&_item.m_quality<Policy.MaxQuality)
                line+=$"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Strengthen: {Policy.UpgradeShards(_item.m_quality+1)} warshards, Warstone level {Policy.UpgradeLevel(_item.m_quality+1)}";
            return Localization.instance.Localize(line);
        }
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||!(user is Player player)||player!=Player.m_localPlayer||View==null||!View.IsValid())return false;
            if(!PrivateArea.CheckAccess(transform.position))return false;
            if(alt)return Strengthen(player);
            if(_item!=null)return TakeOut(player);
            // Empty: the best stave in the bag (Shieldwall's own first, strongest first).
            ItemDrop.ItemData best=player.GetInventory().GetAllItems().Where(Items.Plantable).OrderByDescending(i=>Items.IsStave(i)).ThenByDescending(i=>i.m_quality).FirstOrDefault();
            if(best==null){player.Message(MessageHud.MessageType.Center,"You carry no stave to set in it.");return true;}
            return Set(player,best);
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)
        {
            if(!(user is Player player)||player!=Player.m_localPlayer||View==null||!View.IsValid()||!Items.Plantable(item))return false;
            if(_item!=null){player.Message(MessageHud.MessageType.Center,"A stave already stands here.");return true;}
            return Set(player,item);
        }
        private bool Set(Player player,ItemDrop.ItemData item)
        {
            View.ClaimOwnership();
            ZDO z=View.GetZDO();
            z.Set(ItemKey,item.m_dropPrefab!=null?item.m_dropPrefab.name:"");
            ItemDrop.SaveToZDO(item,z);
            z.Set(ZDOVars.s_quality,item.m_quality);
            player.UnequipItem(item,false);
            player.GetInventory().RemoveItem(item);
            Load();
            Assets.Effect("vfx_HealthUpgrade",transform.position+Vector3.up*Pieces.SocketTop);
            player.Message(MessageHud.MessageType.TopLeft,$"Set the {item.m_shared.m_name.ToLowerInvariant()} in its socket.");
            return true;
        }
        private bool TakeOut(Player player)
        {
            View.ClaimOwnership();
            ItemDrop.ItemData item=_item.Clone();
            if(!player.GetInventory().AddItem(item))ItemDrop.DropItem(item,1,transform.position+Vector3.up*1.2f,Quaternion.identity);
            else player.ShowPickupMessage(item,1);
            View.GetZDO().Set(ItemKey,"");
            Load();
            return true;
        }
        private bool Strengthen(Player player)
        {
            if(_item==null||_stave==null){player.Message(MessageHud.MessageType.Center,"Only a war stave can be strengthened here.");return true;}
            int to=_item.m_quality+1;
            if(to>Policy.MaxQuality){player.Message(MessageHud.MessageType.Center,"It is as strong as a stave can be.");return true;}
            _nextPower=0;Power();
            int level=_stone!=null?_stone.Level:0,need=Policy.UpgradeLevel(to),shards=Policy.UpgradeShards(to);
            if(level<need){player.Message(MessageHud.MessageType.Center,$"The Warstone must be level {need} to strengthen it further. Build its upgrades.");return true;}
            Inventory bag=player.GetInventory();
            if(bag.CountItems("Warshard")<shards){player.Message(MessageHud.MessageType.Center,$"It takes {shards} warshards.");return true;}
            bag.RemoveItem("Warshard",shards);
            View.ClaimOwnership();
            ItemDrop.ItemData item=_item.Clone();item.m_quality=to;
            ItemDrop.SaveToZDO(item,View.GetZDO());View.GetZDO().Set(ZDOVars.s_quality,to);
            Load();
            Assets.Effect("fx_DvergerMage_Support_start",transform.position+Vector3.up*Pieces.SocketTop);
            player.Message(MessageHud.MessageType.Center,$"The {item.m_shared.m_name.ToLowerInvariant()} grows stronger: {new string('★',to-1)}");
            return true;
        }

        // ---- for tests ----
        internal static GameObject Make(ItemDrop.ItemData item,Vector3 at,Quaternion rotation)
        {
            GameObject go=Object.Instantiate(Pieces.Socket,at,rotation);
            Piece piece=go.GetComponent<Piece>();
            if(Player.m_localPlayer!=null)piece?.SetCreator(Player.m_localPlayer.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            if(item==null)return go;
            ZDO z=go.GetComponent<ZNetView>().GetZDO();
            z.Set(ItemKey,item.m_dropPrefab!=null?item.m_dropPrefab.name:"");
            ItemDrop.SaveToZDO(item,z);z.Set(ZDOVars.s_quality,item.m_quality);
            go.GetComponent<Planted>()?.Load();
            return go;
        }
    }

    // Staves planted in the ground by version 0.1: they fall over as items (to be set in a socket), and the old marker goes.
    internal sealed class LegacyPlanted:MonoBehaviour
    {
        private static GameObject _prefab;
        internal static GameObject Prefab
        {
            get
            {
                if(_prefab!=null)return _prefab;
                var go=new GameObject(Planted.LegacyName);go.transform.SetParent(Assets.Holder,false);
                ZNetView view=go.AddComponent<ZNetView>();view.m_persistent=true;view.m_type=ZDO.ObjectType.Default;
                go.AddComponent<LegacyPlanted>();
                return _prefab=go;
            }
        }
        internal static void Forget(){_prefab=null;}
        private void Start()
        {
            ZNetView view=GetComponent<ZNetView>();
            if(view==null||!view.IsValid()||!view.IsOwner())return;
            string prefab=view.GetZDO().GetString(Planted.ItemKey,"");
            GameObject source=string.IsNullOrEmpty(prefab)?null:Items.Get(prefab)??Assets.Find(prefab);
            if(source!=null&&source.GetComponent<ItemDrop>() is ItemDrop drop)
            {
                ItemDrop.ItemData item=drop.m_itemData.Clone();item.m_dropPrefab=source;ItemDrop.LoadFromZDO(item,view.GetZDO());
                ItemDrop.DropItem(item,1,transform.position+Vector3.up,Quaternion.identity);
            }
            ZNetScene.instance.Destroy(gameObject);
        }
    }

    // A socket placed too close to another: refused, with the reason.
    [HarmonyPatch(typeof(Player),nameof(Player.TryPlacePiece))]
    internal static class SocketSpacing
    {
        private static readonly AccessTools.FieldRef<Player,GameObject> Ghost=AccessTools.FieldRefAccess<Player,GameObject>("m_placementGhost");
        private static bool Prefix(Player __instance,Piece piece,ref bool __result)
        {
            if(piece==null||Utils.GetPrefabName(piece.gameObject)!=Pieces.SocketName)return true;
            GameObject ghost=Ghost(__instance);
            Vector3 at=ghost!=null?ghost.transform.position:__instance.transform.position;
            if(Planted.Loaded.Any(p=>p!=null&&Vector3.Distance(p.transform.position,at)<Policy.SocketSpacing))
            {
                __instance.Message(MessageHud.MessageType.Center,$"Too close to another stave socket (keep {Policy.SocketSpacing:0} m apart).");
                __result=false;return false;
            }
            return true;
        }
    }

    // Shots from staves never hurt players, companions, tames or buildings: only foes. The raider struck remembers who shot it.
    [HarmonyPatch(typeof(Projectile),"IsValidTarget")]
    internal static class StaveShotTargets
    {
        private static readonly AccessTools.FieldRef<Projectile,Character> Owner=AccessTools.FieldRefAccess<Projectile,Character>("m_owner");
        private static readonly AccessTools.FieldRef<Projectile,ItemDrop.ItemData> Weapon=AccessTools.FieldRefAccess<Projectile,ItemDrop.ItemData>("m_weapon");
        private static bool Prefix(Projectile __instance,IDestructible destr,ref bool __result)
        {
            ItemDrop.ItemData weapon=Weapon(__instance);
            if(Owner(__instance)!=null||weapon==null||!Planted.Shots.TryGetValue(weapon,out Planted socket))return true;
            __result=destr is Character c&&Planted.IsFoe(c);
            if(__result&&destr is Character foe&&socket!=null)Raider.ShotBy(foe,socket);
            return false;
        }
    }
    [HarmonyPatch(typeof(Aoe),"ShouldHit")]
    internal static class StaveBlastTargets
    {
        private static readonly AccessTools.FieldRef<Aoe,Character> Owner=AccessTools.FieldRefAccess<Aoe,Character>("m_owner");
        private static readonly AccessTools.FieldRef<Aoe,ItemDrop.ItemData> Weapon=AccessTools.FieldRefAccess<Aoe,ItemDrop.ItemData>("m_itemData");
        private static bool Prefix(Aoe __instance,Collider collider,ref bool __result)
        {
            ItemDrop.ItemData weapon=Weapon(__instance);
            if(Owner(__instance)!=null||weapon==null||!Planted.Shots.TryGetValue(weapon,out Planted socket))return true;
            __result=collider!=null&&collider.GetComponentInParent<Character>() is Character c&&Planted.IsFoe(c);
            if(__result&&collider.GetComponentInParent<Character>() is Character foe&&socket!=null)Raider.ShotBy(foe,socket);
            return __result; // a foe: let the game's own checks run; anything else: no hit
        }
    }
    // The killing blow from a stave counts on its socket.
    [HarmonyPatch(typeof(Character),"OnDeath")]
    internal static class StaveKills
    {
        private static void Prefix(Character __instance)
        {
            if(Raider.LastShot.TryGetValue(__instance,out var shot)&&shot.socket!=null&&Time.time-shot.at<4)shot.socket.Killed();
            Raider.LastShot.Remove(__instance);
        }
    }
}
