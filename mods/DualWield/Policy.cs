using System;

namespace DualWield
{
    // Weapon kinds the game has a dual move set for: two axes fight like the Berserkir axes, two knives like Skoll and Hati.
    internal enum Family { None, Axes, Knives }
    internal enum Verdict { Single, Dual, NeedsSkill }

    // Pure rules shared with the standalone tests: no Unity or game types.
    internal static class Policy
    {
        internal static Family FamilyOf(bool oneHanded,bool axe,bool knife)=>!oneHanded?Family.None:axe?Family.Axes:knife?Family.Knives:Family.None;

        // Equipping a second weapon while one is in the main hand: a matching pair goes to the off hand once the skill allows it.
        internal static Verdict Equip(Family main,Family incoming,bool sameItem,double skill,double minSkill,bool swapHeld)
        {
            if(main==Family.None||incoming!=main||sameItem||swapHeld)return Verdict.Single;
            return skill>=minSkill?Verdict.Dual:Verdict.NeedsSkill;
        }

        // Average damage multiplier across one attack chain: every hit at the base multiplier, the last one multiplied again.
        internal static double ChainAverage(double multiplier,int levels,double lastMultiplier)
        {
            if(!(multiplier>0))return 0;
            if(levels<=1)return multiplier;
            if(!(lastMultiplier>0))lastMultiplier=1;
            return multiplier*((levels-1)+lastMultiplier)/levels;
        }

        // The factor applied to each dual hit, which uses the main weapon's damage: a share of both weapons' damage per hit,
        // normalized so the dual chain's own multipliers do not change the total.
        internal static double DamageFactor(double mainDamage,double offDamage,double share,double singleAverage,double dualAverage)
        {
            if(!(mainDamage>0)||!(singleAverage>0)||!(dualAverage>0))return 1;
            if(!(offDamage>=0))offDamage=0;
            if(!(share>0))share=0.62;
            return share*(mainDamage+offDamage)/mainDamage*singleAverage/dualAverage;
        }
        internal static double Stamina(double mainStamina,double factor)=>Math.Max(0,(double.IsNaN(mainStamina)?0:mainStamina)*(factor>0?factor:1));
    }
}
