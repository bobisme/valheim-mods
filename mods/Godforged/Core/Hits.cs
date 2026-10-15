using System.Collections.Generic;
using UnityEngine;

namespace Godforged
{
    // Hurting things the game's way: a HitData given to whatever IDestructible owns the collider (a creature, a tree, a rock, a piece).
    // The game sends it to that object's owner, so this works in multiplayer from anyone's game. Friends are left alone: tamed animals,
    // other players unless both have PvP on (BaseAI.IsEnemy), and the attacker.
    internal static class Hits
    {
        private static int _mask,_solid;
        // What a shot can hit: the melee attack's layers, the ground and trees.
        internal static int Mask=>_mask!=0?_mask:_mask=LayerMask.GetMask("Default","static_solid","Default_small","piece","piece_nonsolid","terrain","character","character_net","character_ghost","hitbox","character_noenv","vehicle");
        // What stops a beam's look but isn't hurt by it (for aiming points): the same.
        internal static int Solid=>_solid!=0?_solid:_solid=LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","vehicle");

        internal static bool Friendly(Character attacker,Character target)
        {
            if(target==null||attacker==null)return false;
            if(target==attacker)return true;
            return !BaseAI.IsEnemy(attacker,target);
        }

        // The first thing along a ray that isn't the attacker (or its own colliders), or nothing within range.
        internal static bool Ray(Character attacker,Vector3 from,Vector3 dir,float range,out RaycastHit hit,float radius=0f)
        {
            RaycastHit[] hits=radius>0f?Physics.SphereCastAll(from,radius,dir,range,Mask,QueryTriggerInteraction.Ignore):Physics.RaycastAll(from,dir,range,Mask,QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(RaycastHit h in hits)
            {
                if(h.collider==null)continue;
                if(attacker!=null&&h.collider.GetComponentInParent<Character>()==attacker)continue;
                if(h.distance<=0f&&h.point==Vector3.zero)continue;   // (a sphere cast starting inside something)
                hit=h;return true;
            }
            hit=default;return false;
        }

        // One hit on what owns the collider. Returns true when something took it. buildings: also pieces (walls, floors...).
        internal static bool Deal(Character attacker,Collider collider,Vector3 point,Vector3 dir,HitData.DamageTypes damage,float push=0f,
            Skills.SkillType skill=Skills.SkillType.None,bool buildings=true,int toolTier=4,bool ranged=true)
        {
            if(collider==null)return false;
            GameObject go=Projectile.FindHitObject(collider);
            IDestructible target=go!=null?go.GetComponent<IDestructible>():null;
            if(target==null)return false;
            if(target is Character c&&Friendly(attacker,c))return false;
            if(!buildings&&target is WearNTear)return false;
            var hit=new HitData{m_damage=damage,m_point=point,m_dir=dir.normalized,m_pushForce=push,m_skill=skill,m_toolTier=(short)toolTier,
                m_hitCollider=collider,m_blockable=true,m_dodgeable=true,m_ranged=ranged};
            if(attacker!=null)hit.SetAttacker(attacker);
            target.Damage(hit);
            return true;
        }

        // Everything in a sphere, once each, the damage falling off to 'edge' of itself at the rim. Returns how many were hit.
        internal static int Blast(Character attacker,Vector3 centre,float radius,HitData.DamageTypes damage,float push,bool buildings,
            Skills.SkillType skill=Skills.SkillType.None,float edge=0.35f,int toolTier=4)
        {
            var done=new HashSet<IDestructible>();
            int n=0;
            foreach(Collider col in Physics.OverlapSphere(centre,radius,Mask,QueryTriggerInteraction.Ignore))
            {
                GameObject go=Projectile.FindHitObject(col);
                IDestructible target=go!=null?go.GetComponent<IDestructible>():null;
                if(target==null||!done.Add(target))continue;
                if(target is Character c&&Friendly(attacker,c))continue;
                if(!buildings&&target is WearNTear)continue;
                Vector3 at=col.ClosestPoint(centre);
                float t=Mathf.Clamp01(Vector3.Distance(at,centre)/radius);
                HitData.DamageTypes d=damage.Clone();d.Modify(Mathf.Lerp(1f,edge,t));
                Vector3 dir=go.transform.position-centre;dir.y=Mathf.Max(dir.y,0.5f);
                var hit=new HitData{m_damage=d,m_point=at,m_dir=dir.normalized,m_pushForce=push*Mathf.Lerp(1f,edge,t),m_skill=skill,m_toolTier=(short)toolTier,
                    m_hitCollider=col,m_blockable=false,m_dodgeable=false,m_ranged=true};
                if(attacker!=null)hit.SetAttacker(attacker);
                target.Damage(hit);
                n++;
            }
            return n;
        }
    }
}
