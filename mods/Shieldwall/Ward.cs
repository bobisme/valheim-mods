using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // The Warstone's ward on you: a status effect while you are within 40 m of a stone, stronger with its marks and at full strength
    // when the stone stands at a hearth (under a roof, a fire burning within 10 m). It lingers a minute after you step out.
    internal static class Ward
    {
        internal const string EffectName="BobWarstoneWard";
        private static string _signature="";
        private static SE_Stats _template;
        private static float _next,_nextHearth;
        private static Warstone _hearthStone;private static bool _hearth;
        private static readonly Collider[] Near=new Collider[256];

        internal static void Tick()
        {
            Player me=Player.m_localPlayer;
            if(me==null||Time.time<_next)return;
            _next=Time.time+1;
            Warstone stone=Warstone.Loaded.Where(w=>w!=null&&w.Z!=null&&Vector3.Distance(w.transform.position,me.transform.position)<=Policy.WardRadius)
                .OrderByDescending(w=>w.Strength).FirstOrDefault();
            if(stone==null){_signature="";return;} // the effect runs down on its own
            bool hearth=AtHearth(stone);
            WardStats ward=Policy.WardOf(stone.Strength+Policy.BoonWard(stone.Boons),hearth);
            string signature=$"{stone.Strength}|{hearth}|{stone.Cracked}|{Policy.BoonWard(stone.Boons)}";
            int hash=EffectName.GetStableHashCode();
            if(signature==_signature&&me.GetSEMan().GetStatusEffect(hash) is StatusEffect active){active.ResetTime();return;}
            me.GetSEMan().RemoveStatusEffect(hash,true);
            if(_template!=null)Object.Destroy(_template);
            _signature=signature;
            var se=ScriptableObject.CreateInstance<SE_Stats>();
            se.name=EffectName;
            se.m_name=stone.Marks>0?$"Warstone's ward ({Policy.Numeral(stone.Strength)})":"Warstone's ward";
            se.m_icon=Stone.Station!=null?Stone.Station.m_icon:null;
            se.m_tooltip=string.Join("\n",Policy.Describe(ward))+
                (hearth?"\nThe stone stands at a hearth: full strength.":"\nHalf strength: set the stone under a roof with a fire near for the full ward.")+
                (stone.Cracked?"\nThe stone is cracked: hold a siege to mend it.":"");
            se.m_ttl=60;
            se.m_healthRegenMultiplier=ward.HealthRegen;se.m_staminaRegenMultiplier=ward.StaminaRegen;se.m_eitrRegenMultiplier=ward.EitrRegen;
            se.m_addMaxCarryWeight=ward.Carry;se.m_addArmor=ward.Armor;
            _template=se;
            me.GetSEMan().AddStatusEffect(se,true);
        }
        // Under a roof, with a burning fire near: checked every few seconds (a physics query near the stone, not a scene search).
        internal static bool AtHearth(Warstone stone)
        {
            if(stone==_hearthStone&&Time.time<_nextHearth)return _hearth;
            _hearthStone=stone;_nextHearth=Time.time+5;_hearth=false;
            Vector3 at=stone.transform.position+Vector3.up*1.5f;
            Cover.GetCoverForPoint(at,out float cover,out bool roof);
            if(!roof||cover<0.5f)return false;
            int count=Physics.OverlapSphereNonAlloc(stone.transform.position,Policy.HearthRadius,Near,LayerMask.GetMask("piece","piece_nonsolid"),QueryTriggerInteraction.Collide);
            for(int i=0;i<count;i++)
                if(Near[i].GetComponentInParent<Fireplace>() is Fireplace fire&&fire.IsBurning()){_hearth=true;break;}
            return _hearth;
        }
        internal static int ComfortAt(Vector3 position)
        {
            Warstone stone=Warstone.Loaded.FirstOrDefault(w=>w!=null&&w.Z!=null&&Vector3.Distance(w.transform.position,position)<=Policy.HearthRadius);
            return stone!=null&&AtHearth(stone)?Policy.WardOf(stone.Strength+Policy.BoonWard(stone.Boons),true).Comfort:0;
        }
        internal static void Clear()
        {
            Player me=Player.m_localPlayer;
            if(me!=null)me.GetSEMan().RemoveStatusEffect(EffectName.GetStableHashCode(),true);
            if(_template!=null)Object.Destroy(_template);_template=null;_signature="";_hearthStone=null;
        }
    }

    [HarmonyPatch(typeof(SE_Rested),nameof(SE_Rested.CalculateComfortLevel),new[]{typeof(bool),typeof(Vector3)})]
    internal static class WardComfort
    {
        private static void Postfix(bool inShelter,Vector3 position,ref int __result){if(inShelter&&Plugin.Instance!=null)__result+=Ward.ComfortAt(position);}
    }
}
