using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // A staff planted upright in the ground near a Warstone. It looses its own magic at foes in reach (a Hearth stave mends friends
    // instead) and never hurts players, companions, tames or buildings. Pull it out to take it back; if the horde knocks it down,
    // it drops where it stood.
    internal sealed class Planted:MonoBehaviour,Hoverable,Interactable,IDestructible
    {
        internal const string PrefabName="BobPlantedStave",ItemKey="bob_sw_item",HealthKey="bob_sw_stavehealth";
        internal static int Hash=>PrefabName.GetStableHashCode();
        internal static readonly List<Planted> Loaded=new List<Planted>();
        // Our shots carry the planted staff's own item; that is how the friendly-fire guard knows them.
        internal static readonly HashSet<ItemDrop.ItemData> Shots=new HashSet<ItemDrop.ItemData>();
        private static GameObject _prefab;
        internal static GameObject Prefab=>Ensure();
        private static int _sight=-1;

        internal ZNetView View;internal StaticTarget Target;
        private ItemDrop.ItemData _item;private Stave _stave;
        private float _length=2,_next;private Light _light;
        internal Vector3 Tip=>transform.position+Vector3.up*(_length-0.45f);

        internal static GameObject Ensure()
        {
            if(_prefab!=null)return _prefab;
            var go=new GameObject(PrefabName);
            go.transform.SetParent(Assets.Holder,false);
            ZNetView view=go.AddComponent<ZNetView>();
            view.m_persistent=true;view.m_type=ZDO.ObjectType.Default;view.m_distant=false;
            go.AddComponent<StaticTarget>().m_randomTarget=false;
            go.AddComponent<Planted>();
            _prefab=go;
            return go;
        }
        internal static void Forget(){_prefab=null;}

        private void Awake()
        {
            View=GetComponent<ZNetView>();Target=GetComponent<StaticTarget>();
            if(View==null||!View.IsValid())return;
            Loaded.Add(this);
            string prefab=View.GetZDO().GetString(ItemKey,"");
            GameObject source=Items.Get(prefab)??Assets.Find(prefab);
            ItemDrop drop=source!=null?source.GetComponent<ItemDrop>():null;
            if(drop==null)return;
            _item=drop.m_itemData.Clone();_item.m_dropPrefab=source;
            ItemDrop.LoadFromZDO(_item,View.GetZDO());
            _stave=Policy.Staves.FirstOrDefault(s=>s.Name==_item.m_shared.m_name);
            Shots.Add(_item);
            Build(source);
            _next=Time.time+Random.Range(0.5f,1.5f);
        }
        private void OnDestroy(){Loaded.Remove(this);if(_item!=null)Shots.Remove(_item);}
        internal static void DetachAll(){foreach(Planted p in Loaded.ToList())if(p!=null){foreach(Transform t in p.transform.Cast<Transform>().ToList())Object.Destroy(t.gameObject);Object.Destroy(p);}Loaded.Clear();Shots.Clear();}
        internal static void AttachAll()
        {
            foreach(ZNetView view in Assets.Instances(Hash))
            {
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())if(stale!=null&&stale.GetType().Name==nameof(Planted)&&stale.GetType()!=typeof(Planted))Object.DestroyImmediate(stale);
                foreach(Transform t in view.transform.Cast<Transform>().ToList())Object.Destroy(t.gameObject);
                if(view.GetComponent<Planted>()==null)view.gameObject.AddComponent<Planted>();
            }
        }

        // The staff stands on its butt with its head up (the head is the end farthest from where a hand holds it), sunk a little into the ground.
        private void Build(GameObject source)
        {
            Transform attach=source.transform.Find("attach");
            GameObject look=Assets.Model(attach!=null?attach.gameObject:source,transform,Vector3.zero,Quaternion.identity,1.4f,r=>r.GetComponent<MeshFilter>()!=null);
            if(look==null)return;
            Renderer[] renderers=look.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)return;
            Bounds local=new Bounds(transform.InverseTransformPoint(renderers[0].bounds.center),Vector3.zero);
            foreach(Renderer r in renderers){local.Encapsulate(transform.InverseTransformPoint(r.bounds.min));local.Encapsulate(transform.InverseTransformPoint(r.bounds.max));}
            Vector3 head=local.center.sqrMagnitude>0.01f?local.center.normalized:Vector3.forward;
            look.transform.localRotation=Quaternion.FromToRotation(head,Vector3.up);
            // Measure again upright and sink the butt 0.3 m.
            float low=float.MaxValue,high=float.MinValue;
            foreach(Renderer r in look.GetComponentsInChildren<Renderer>()){low=Mathf.Min(low,r.bounds.min.y);high=Mathf.Max(high,r.bounds.max.y);}
            look.transform.localPosition=new Vector3(0,transform.position.y-low-0.3f,0);
            _length=Mathf.Clamp(high-low-0.3f,0.8f,3f);
            int layer=LayerMask.NameToLayer("piece_nonsolid");
            var body=new GameObject("Body"){layer=layer};body.transform.SetParent(transform,false);
            var capsule=body.AddComponent<CapsuleCollider>();capsule.radius=0.25f;capsule.height=_length;capsule.center=new Vector3(0,_length/2,0);
            var lit=new GameObject("Glow");lit.transform.SetParent(transform,false);lit.transform.localPosition=new Vector3(0,_length-0.3f,0);
            _light=lit.AddComponent<Light>();_light.type=LightType.Point;_light.range=4;_light.intensity=1.1f;_light.shadows=LightShadows.None;
            _light.color=_stave!=null&&Items.Colours.TryGetValue(_stave.Kind,out var c)?c.glow*1.6f:new Color(0.6f,0.6f,1f);
        }

        // The stone that powers it (looked up once a second, not every frame).
        private Warstone _stone;private float _nextStone;
        internal Warstone Stone()
        {
            if(Time.time<_nextStone&&(_stone==null||_stone.View!=null&&_stone.View.IsValid()))return _stone;
            _nextStone=Time.time+1;
            _stone=Warstone.Loaded.Where(w=>w!=null).OrderBy(w=>Vector3.Distance(w.transform.position,transform.position)).FirstOrDefault(w=>Policy.Powered(Vector3.Distance(w.transform.position,transform.position)));
            return _stone;
        }
        internal static Planted Nearest(Vector3 at,float range)=>Loaded.Where(p=>p!=null&&p._item!=null&&Vector3.Distance(p.transform.position,at)<=range).OrderBy(p=>Vector3.Distance(p.transform.position,at)).FirstOrDefault();
        private int Quality=>_item?.m_quality??1;
        private float Range=>_stave!=null?Policy.RangeAt(_stave,Quality):Policy.BorrowedRange;
        private float Cooldown=>_stave!=null?_stave.Cooldown:Policy.BorrowedCooldown;

        // ---- watching and shooting (on the game that owns it) ----
        private void Update()
        {
            if(View==null||!View.IsValid()||_item==null)return;
            Warstone stone=Stone();
            if(_light!=null)_light.enabled=stone!=null;
            if(!View.IsOwner()||stone==null||Time.time<_next)return;
            _next=Time.time+Cooldown;
            if(_stave!=null&&_stave.Kind==StaveKind.Hearth){Mend(stone);return;}
            Character foe=Choose();
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
        private Character Choose()
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
                // Thunder seeks the strongest; the rest the nearest.
                float score=_stave!=null&&_stave.Kind==StaveKind.Thunder?-c.GetMaxHealth()+d:d;
                if(score>=bestScore)continue;
                if(Physics.Linecast(tip,at,out RaycastHit hit,_sight,QueryTriggerInteraction.Ignore)&&hit.collider.GetComponentInParent<Character>()!=c)continue;
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
            GameObject shot=Object.Instantiate(prefab,tip+dir*0.4f,Quaternion.LookRotation(dir));
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
        // The Hearth stave: friends near it heal; during a siege the stone itself is mended a little too.
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
            if(stone.Phase==Phase.Battle&&Vector3.Distance(stone.transform.position,transform.position)<=Range+5){Net.Damage(stone,-amount*3,stone.transform.position);any=true;}
            if(any)Assets.Effect("vfx_HealthUpgrade",transform.position);
        }

        // ---- the horde's blows ----
        public DestructibleType GetDestructibleType()=>DestructibleType.Default;
        public void Damage(HitData hit)
        {
            if(hit==null||!View.IsValid()||_item==null)return;
            Character attacker=hit.GetAttacker();
            if(attacker==null||attacker.IsPlayer()||attacker.IsTamed())return;
            Net.StaveHit(this,hit.GetTotalDamage());
        }
        internal void Struck(float damage)
        {
            if(!View.IsOwner()||_item==null)return;
            ZDO z=View.GetZDO();
            float health=z.GetFloat(HealthKey,Policy.StaveHealth)-damage;
            z.Set(HealthKey,health);
            if(health>0)return;
            // Knocked down: it falls where it stood, as an item, for anyone to pick up and plant again.
            ItemDrop.DropItem(_item,1,transform.position+Vector3.up*0.6f,Quaternion.Euler(0,Random.Range(0,360f),90));
            Net.Say($"A {_item.m_shared.m_name.ToLowerInvariant()} was knocked down!",transform.position,60);
            _item=null;
            ZNetScene.instance.Destroy(gameObject);
        }

        // ---- hover and pulling it out ----
        public string GetHoverName()=>_item?.m_shared.m_name??"Staff";
        public float GetHoverOffset()=>0;
        public string GetHoverText()
        {
            if(_item==null)return "";
            string name=_item.m_shared.m_name+(_item.m_quality>1?$" ★{_item.m_quality}":"")+(_stave==null?" (borrowed: weak)":"");
            string state=Stone()==null?"<color=#A0A0A0>Asleep: no Warstone near</color>":_stave!=null&&_stave.Kind==StaveKind.Hearth?$"Mending friends within {Range:0} m":$"Watching {Range:0} m";
            float health=View.IsValid()?View.GetZDO().GetFloat(HealthKey,Policy.StaveHealth):Policy.StaveHealth;
            if(health<Policy.StaveHealth)state+=$" · {Mathf.RoundToInt(100*health/Policy.StaveHealth)}%";
            return Localization.instance.Localize($"{name}\n{state}\n[<color=yellow><b>$KEY_Use</b></color>] Pull it out");
        }
        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||_item==null||!(user is Player player)||player!=Player.m_localPlayer)return false;
            if(!PrivateArea.CheckAccess(transform.position))return false;
            View.ClaimOwnership();
            ItemDrop.ItemData item=_item.Clone();
            if(!player.GetInventory().AddItem(item))ItemDrop.DropItem(item,1,player.transform.position+Vector3.up,Quaternion.identity);
            else player.ShowPickupMessage(item,1);
            _item=null;
            ZNetScene.instance.Destroy(gameObject);
            return true;
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;

        internal static void Make(ItemDrop.ItemData item,Vector3 at,Quaternion rotation)
        {
            GameObject go=Object.Instantiate(Ensure(),at,rotation);
            ZNetView view=go.GetComponent<ZNetView>();
            // The item goes onto the planted staff after it woke up empty, so the empty one comes off and a fresh one builds itself.
            view.GetZDO().Set(ItemKey,item.m_dropPrefab!=null?item.m_dropPrefab.name:"");
            ItemDrop.SaveToZDO(item,view.GetZDO());
            Planted planted=go.GetComponent<Planted>();
            if(planted!=null){Loaded.Remove(planted);Object.DestroyImmediate(planted);}
            go.AddComponent<Planted>();
        }
        // ---- planting: Use with a staff in hand and nothing in front of you ----
        internal static bool TryPlant(Player player)
        {
            ItemDrop.ItemData item=player.RightItem;
            if(item==null||!Items.Plantable(item))return false;
            // An ordinary staff far from any stone: Use on nothing does nothing, as it always did.
            if(!Items.IsStave(item)&&!Warstone.Loaded.Any(w=>w!=null&&Vector3.Distance(w.transform.position,player.transform.position)<100))return false;
            if(!Physics.Raycast(GameCamera.instance.transform.position,GameCamera.instance.transform.forward,out RaycastHit hit,8,LayerMask.GetMask("terrain","static_solid","Default","piece"),QueryTriggerInteraction.Ignore)||
                Vector3.Distance(hit.point,player.transform.position)>5||hit.normal.y<0.6f)
            {player.Message(MessageHud.MessageType.Center,"Look at open ground close by to plant it.");return true;}
            Vector3 at=hit.point;
            Warstone stone=Warstone.Loaded.Where(w=>w!=null).OrderBy(w=>Vector3.Distance(w.transform.position,at)).FirstOrDefault();
            if(stone==null||!Policy.Powered(Vector3.Distance(stone.transform.position,at)))
            {player.Message(MessageHud.MessageType.Center,$"A staff draws its power from a Warstone: plant it within {Policy.WardRadius+10:0} m of one.");return true;}
            if(Loaded.Any(p=>p!=null&&Vector3.Distance(p.transform.position,at)<1.5f)){player.Message(MessageHud.MessageType.Center,"Too close to another staff.");return true;}
            if(!PrivateArea.CheckAccess(at))return true;
            Make(item,at,Quaternion.Euler(0,player.transform.eulerAngles.y,0));
            player.UnequipItem(item,false);
            player.GetInventory().RemoveItem(item);
            Assets.Effect("vfx_HealthUpgrade",at);
            player.Message(MessageHud.MessageType.TopLeft,$"Planted the {item.m_shared.m_name.ToLowerInvariant()}.");
            return true;
        }
    }

    // Use with nothing hovered and a staff in hand plants it.
    [HarmonyPatch(typeof(Player),"Update")]
    internal static class PlantKey
    {
        private static readonly System.Reflection.MethodInfo TakeInput=AccessTools.Method(typeof(Player),"TakeInput");
        private static void Postfix(Player __instance)
        {
            if(__instance!=Player.m_localPlayer||Plugin.Instance==null)return;
            if(!ZInput.GetButtonDown("Use")&&!ZInput.GetButtonDown("JoyUse"))return;
            if(!(bool)TakeInput.Invoke(__instance,null)||__instance.InPlaceMode()||__instance.IsDead())return;
            if(__instance.GetHoverObject()!=null)return; // Use on something: the game handles it
            Planted.TryPlant(__instance);
        }
    }

    // Shots from planted staves never hurt players, companions, tames or buildings: only foes.
    [HarmonyPatch(typeof(Projectile),"IsValidTarget")]
    internal static class StaveShotTargets
    {
        private static readonly AccessTools.FieldRef<Projectile,Character> Owner=AccessTools.FieldRefAccess<Projectile,Character>("m_owner");
        private static readonly AccessTools.FieldRef<Projectile,ItemDrop.ItemData> Weapon=AccessTools.FieldRefAccess<Projectile,ItemDrop.ItemData>("m_weapon");
        private static bool Prefix(Projectile __instance,IDestructible destr,ref bool __result)
        {
            if(Owner(__instance)!=null||!Planted.Shots.Contains(Weapon(__instance)))return true;
            __result=destr is Character c&&Planted.IsFoe(c);
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
            if(Owner(__instance)!=null||!Planted.Shots.Contains(Weapon(__instance)))return true;
            __result=collider!=null&&collider.GetComponentInParent<Character>() is Character c&&Planted.IsFoe(c);
            return __result; // a foe: let the game's own checks run; anything else: no hit
        }
    }
}
