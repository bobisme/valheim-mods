using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Godforged
{
    // Relics that come back when thrown (Mjölnir, Gungnir). The throw is the game's own spear throw (the item's secondary attack) with a
    // projectile of our own that carries the relic's model. As it throws, the game takes the item out of the bag (Attack.m_consumeItem) and
    // hands it to the projectile to drop where it lands (Projectile.m_respawnItemOnHit). On the thrower's game we take it back at the first
    // hit, or whenever the projectile goes some other way (out of time, its area unloaded), and fly it home: back into the bag and, if the
    // hand is still empty, into the hand. Everyone else sees the flight home (GF_Return). If it can't get home (the thrower dead, the bag
    // full) it drops where it is, so it is never lost.
    internal sealed class Throws:Feature
    {
        internal override string Name=>"Throws";
        private const string ReturnRpc="GF_Return";
        internal static readonly AccessTools.FieldRef<Projectile,Vector3> Vel=AccessTools.FieldRefAccess<Projectile,Vector3>("m_vel");
        private static readonly Dictionary<string,GameObject> Projectiles=new Dictionary<string,GameObject>();
        internal static readonly List<Flight> Flying=new List<Flight>();

        internal override void Start()=>Net.Register(ReturnRpc,OnReturn);
        internal override void Stop()
        {
            // Unloading: every relic still in flight goes home now and its projectile goes. (A relic left in the air through a reload
            // came back twice: the old copy's Thrown brought it home, and the new copy, not knowing that Thrown, let the game drop it too.)
            foreach(Thrown th in Object.FindObjectsByType<Thrown>(FindObjectsSortMode.None))
            {
                if(th==null||th.Taken||th.Item==null)continue;
                th.Taken=true;
                if(th.Projectile!=null)th.Projectile.m_spawnItem=null;
                Give(th.Owner,th.Item,th.transform.position);th.Item=null;
                ZNetView nv=th.GetComponent<ZNetView>();
                if(nv!=null&&nv.IsValid()&&nv.IsOwner()&&ZNetScene.instance!=null)ZNetScene.instance.Destroy(th.gameObject);
            }
            foreach(Flight f in Flying.ToArray())if(f!=null){f.Arrive();Object.Destroy(f.gameObject);}   // (unloading: home now)
            Flying.Clear();
            Projectiles.Clear();   // (the prefabs stay registered inside a world, as the items do: the next copy replaces them by name)
        }

        // The relic's projectile: a copy of a spear's (its flight, sounds and trail) with the relic's model in place of the spear's, put in
        // ZNetScene so everyone sees it fly, and set as the relic's throw. Called from the feature's Register: safe to call again.
        internal static GameObject Projectile(ZNetScene scene,ItemDef d,string spear,Action<Transform> model,bool spin=false,Func<AudioClip> sound=null)
        {
            if(d.Built==null)return null;
            string name=d.Prefab+"_projectile";
            if(!Projectiles.TryGetValue(name,out GameObject go)||go==null)
            {
                GameObject source=scene.GetPrefab(spear)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_secondaryAttack?.m_attackProjectile;
                if(source==null){Plugin.Warn($"{d.Name}: no {spear} throw to copy");return null;}
                go=Object.Instantiate(source,Things.Holder);
                go.name=name;
                foreach(Renderer r in go.GetComponentsInChildren<Renderer>(true))if(!(r is ParticleSystemRenderer)&&!(r is TrailRenderer))r.enabled=false;
                var holder=new GameObject("gf_model"){layer=go.layer};
                holder.transform.SetParent(go.transform,false);
                if(spin)holder.AddComponent<Spin>();
                if(sound!=null)holder.AddComponent<FlightSound>().Clip=sound;   // (on the way out and, in the flight home's copy, back)
                var inner=new GameObject("model"){layer=go.layer};
                inner.transform.SetParent(holder.transform,false);
                // The model's business end (-y) forward along the flight (+z); a spinning one is also rolled a quarter turn about the flight so
                // its head (along x) stands upright, and tumbles end over end about the sideways axis (Spin).
                inner.transform.localRotation=(spin?Quaternion.Euler(0,0,90):Quaternion.identity)*Quaternion.Euler(-90,0,0);
                model(inner.transform);
                foreach(Transform t in holder.GetComponentsInChildren<Transform>(true))t.gameObject.layer=go.layer;
                Projectile p=go.GetComponent<Projectile>();
                p.m_respawnItemOnHit=true;p.m_spawnOnTtl=false;   // (we bring it home: the game must never drop it as well)
                p.m_stayAfterHitStatic=false;p.m_stayAfterHitDynamic=false;p.m_attachToRigidBody=false;p.m_attachToClosestBone=false;
                Projectiles[name]=go;
            }
            Things.AddPrefab(scene,go);
            if(d.Shared?.m_secondaryAttack!=null)d.Shared.m_secondaryAttack.m_attackProjectile=go;
            return go;
        }
        // The relic in flight, pointing forward along +z (from its projectile), for the flight home.
        internal static Transform FlyingModel(ItemDef d)=>Projectiles.TryGetValue(d.Prefab+"_projectile",out GameObject go)&&go!=null?go.transform.Find("gf_model"):null;

        // The foe nearest the crosshair within cone degrees and range metres that can be seen from the camera, or none.
        internal static Character Seek(Player p,float cone,float range)
        {
            Transform cam=GameCamera.instance!=null?GameCamera.instance.transform:null;
            if(cam==null)return null;
            Character best=null;float bestAngle=cone;
            foreach(Character c in Character.GetAllCharacters())
            {
                if(c==null||c==p||c.IsDead()||Hits.Friendly(p,c))continue;
                Vector3 at=c.GetCenterPoint();
                if(Vector3.Distance(at,p.transform.position)>range)continue;
                float angle=Vector3.Angle(cam.forward,at-cam.position);
                if(angle>bestAngle)continue;
                if(Physics.Linecast(cam.position,at,Hits.Solid,QueryTriggerInteraction.Ignore))continue;
                best=c;bestAngle=angle;
            }
            return best;
        }

        // The relic is ours again at 'from': fly it home (or give it now, if there's no model to fly).
        internal static void Home(Player owner,ItemDef d,ItemDrop.ItemData item,Vector3 from)
        {
            if(item==null)return;
            Flight f=Flight.Make(d,owner,from);
            if(f==null){Give(owner,item,from);return;}
            f.Item=item;f.Def=d;
            if(owner==null)return;
            var pkg=new ZPackage();pkg.Write(d.Prefab);pkg.Write(from);pkg.Write(owner.GetZDOID());
            Net.Others(ReturnRpc,pkg);
        }
        private static void OnReturn(long sender,ZPackage pkg)
        {
            ItemDef d=Things.Find(pkg.ReadString());Vector3 from=pkg.ReadVector3();ZDOID who=pkg.ReadZDOID();
            GameObject go=ZNetScene.instance!=null?ZNetScene.instance.FindInstance(who):null;
            if(d!=null)Flight.Make(d,go!=null?go.GetComponent<Character>():null,from);   // (only to look at: no item)
        }
        // Back in the bag, and in the hand if it's empty; or on the ground where it is, if it can't be.
        internal static void Give(Player owner,ItemDrop.ItemData item,Vector3 at)
        {
            if(item==null)return;
            if(owner!=null&&!owner.IsDead()&&owner.GetInventory()!=null&&owner.GetInventory().AddItem(item))
            {
                bool handFull=owner.GetInventory().GetEquippedItems().Exists(i=>i!=item&&(i.IsWeapon()||i.m_shared.m_itemType==ItemDrop.ItemData.ItemType.Tool||
                    i.m_shared.m_itemType==ItemDrop.ItemData.ItemType.Torch));
                if(!handFull)owner.EquipItem(item);
                return;
            }
            if(ZNetScene.instance!=null)ItemDrop.DropItem(item,1,at+Vector3.up*0.3f,Quaternion.identity);
            else Plugin.Warn($"{item.m_shared.m_name} was in flight as the world closed and is lost");
        }
    }

    // On the thrower's game: the relic's projectile. Gungnir steers it to its target; the first hit (or the projectile going) sends it home.
    internal sealed class Thrown:MonoBehaviour
    {
        internal ItemDef Def;internal ItemDrop.ItemData Item;internal Player Owner;internal Projectile Projectile;internal Character Target;
        internal bool Taken;

        private void FixedUpdate()
        {
            if(Projectile==null||Taken)return;
            if(Target==null||Target.IsDead())return;
            // Turn towards the target, harder the closer it is, so it never circles past (it never misses).
            Vector3 to=Target.GetCenterPoint()-transform.position;
            Vector3 v=Throws.Vel(Projectile);
            float speed=Mathf.Max(v.magnitude,25f);
            float turn=(to.magnitude<5f?40f:10f)*Time.fixedDeltaTime;
            Throws.Vel(Projectile)=Vector3.RotateTowards(v,to.normalized*speed,turn,speed);
        }
        private void OnDestroy()
        {
            if(Taken||Item==null)return;
            Taken=true;
            Throws.Home(Owner,Def,Item,transform.position);
            Item=null;
        }
    }

    // The flight home: the relic's model from where it hit to the thrower's right hand, faster as it goes. On the thrower's game it carries
    // the item and gives it back on arrival (or wherever it is if it can't arrive); elsewhere it's only to look at.
    internal sealed class Flight:MonoBehaviour
    {
        internal ItemDrop.ItemData Item;internal ItemDef Def;
        private Character _to;private Transform _hand;private float _t,_speed=12f;private bool _arrived;
        private readonly HashSet<IDestructible> _hit=new HashSet<IDestructible>();

        internal static Flight Make(ItemDef d,Character to,Vector3 from)
        {
            Transform model=Throws.FlyingModel(d);
            if(model==null)return null;
            var go=new GameObject("GF_Flight_"+d.Prefab);
            go.transform.position=from;
            GameObject look=Object.Instantiate(model.gameObject,go.transform,false);
            look.SetActive(true);
            Flight f=go.AddComponent<Flight>();
            f._to=to;f._hand=to!=null?Utils.FindChild(to.transform,"RightHand"):null;
            Throws.Flying.Add(f);
            return f;
        }
        private Vector3 Target=>_hand!=null?_hand.position:_to!=null?_to.GetCenterPoint():transform.position;

        private void Update()
        {
            _t+=Time.deltaTime;
            Vector3 to=Target-transform.position;
            _speed=Mathf.Min(_speed+50f*Time.deltaTime,45f);
            float step=_speed*Time.deltaTime;
            if(to.magnitude<=Mathf.Max(step,0.4f)||_t>4f||_to==null||_to.IsDead()){Arrive();Destroy(gameObject);return;}
            if(Item!=null&&Def?.ReturnHit!=null&&_to is Player p)Sweep(p,to.normalized,step);
            transform.position+=to.normalized*step;
            transform.rotation=Quaternion.LookRotation(-to.normalized);   // (handle or butt first: pulled home)
        }
        // What lies along this step of the way home (a little wider than the relic), each hit once.
        private void Sweep(Player p,Vector3 dir,float step)
        {
            foreach(RaycastHit h in Physics.SphereCastAll(transform.position,0.35f,dir,step,Hits.Mask,QueryTriggerInteraction.Ignore))
            {
                if(h.collider==null)continue;
                GameObject go=Projectile.FindHitObject(h.collider);
                IDestructible target=go!=null?go.GetComponent<IDestructible>():null;
                if(target==null||(target is Character c&&Hits.Friendly(p,c))||!_hit.Add(target))continue;
                Vector3 at=h.point==Vector3.zero?h.collider.ClosestPoint(transform.position):h.point;
                Plugin.Safe(Def.Name+" return hit",()=>Def.ReturnHit(p,Item,h.collider,at,dir));
            }
        }
        internal void Arrive()
        {
            if(_arrived)return;
            _arrived=true;
            if(Item!=null){Throws.Give(_to as Player,Item,transform.position);Item=null;}
        }
        private void OnDestroy()
        {
            Throws.Flying.Remove(this);
            if(!_arrived&&Item!=null)Arrive();
        }
    }

    // A relic's own sound while it flies (a loop on it, heard by everyone: the projectile is everyone's, and so is the flight home).
    internal sealed class FlightSound:MonoBehaviour
    {
        internal Func<AudioClip> Clip;
        private void Start()
        {
            AudioSource s=Clip!=null?Sound.Loop(Clip(),transform,45f):null;
            if(s==null)return;
            s.volume=0.85f*Plugin.Volume.Value;s.dopplerLevel=1.2f;
        }
    }

    // End over end, as a thrown hammer tumbles: about the sideways axis, across both the handle and the head (which stands upright in
    // flight), so a head comes round to the front and strikes.
    internal sealed class Spin:MonoBehaviour
    {
        private float _a;
        private void Update(){_a=(_a+1100f*Time.deltaTime)%360f;transform.localRotation=Quaternion.Euler(_a,0,0);}
    }

    [HarmonyPatch(typeof(Projectile),nameof(Projectile.Setup))]
    internal static class ThrowStart
    {
        private static void Postfix(Projectile __instance,Character owner,ItemDrop.ItemData item)
        {
            if(!(owner is Player p)||p!=Player.m_localPlayer)return;
            ItemDef d=Things.Of(item);
            if(d==null||!d.Returns)return;
            Thrown t=__instance.gameObject.AddComponent<Thrown>();
            t.Def=d;t.Item=item;t.Owner=p;t.Projectile=__instance;
            if(d.SeekCone>0f)t.Target=Throws.Seek(p,d.SeekCone,d.SeekRange);
        }
    }
    [HarmonyPatch(typeof(Projectile),nameof(Projectile.OnHit))]
    internal static class ThrowHit
    {
        private static void Prefix(Projectile __instance,out Thrown __state)
        {
            __state=null;
            Thrown t=__instance.GetComponent<Thrown>();
            if(t==null||t.Taken)return;
            t.Taken=true;__instance.m_spawnItem=null;   // (ours now: the game would drop it here)
            __state=t;
        }
        private static void Postfix(Collider collider,Vector3 hitPoint,Thrown __state)
        {
            if(__state==null)return;
            Thrown t=__state;
            Plugin.Safe(t.Def.Name+" impact",()=>t.Def.Impact?.Invoke(t.Owner,t.Item,hitPoint,collider));
            Throws.Home(t.Owner,t.Def,t.Item,hitPoint);
            t.Item=null;
        }
    }
    // A relic's projectile never leaves the relic on the ground, however the game ends its flight.
    [HarmonyPatch(typeof(Projectile),"SpawnOnHit")]
    internal static class ThrowNoDrop
    {
        private static void Prefix(Projectile __instance){if(__instance.GetComponent<Thrown>()!=null)__instance.m_spawnItem=null;}
    }
}
