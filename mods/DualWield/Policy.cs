using System;

namespace DualWield
{
    // Weapon kinds that pair up. The game has dual move sets for two of them: axes fight like the Berserkir axes, knives like Skoll and
    // Hati. Maces and swords swing like the Berserkir axes too: the closest move set to a one-handed chop or smash.
    internal enum Family { None, Axes, Knives, Maces, Swords }
    internal enum Verdict { Single, Dual, NeedsSkill }

    // Pure rules shared with the standalone tests: no Unity or game types.
    internal static class Policy
    {
        // Skill toward wielding two: the weapon skill, plus a share of the gathering skill that uses the same tool
        // (woodcutting teaches the axe, not fighting with two).
        internal static double Effective(double weaponSkill,double gatheringSkill,double credit)=>
            Math.Max(0,double.IsNaN(weaponSkill)?0:weaponSkill)+Math.Max(0,double.IsNaN(gatheringSkill)?0:gatheringSkill)*Math.Max(0,Math.Min(1,double.IsNaN(credit)?0:credit));
        // By the weapon's skill (the game's Skills.SkillType names): Clubs are maces.
        internal static Family FamilyOf(bool oneHanded,string skill)
        {
            if(!oneHanded)return Family.None;
            switch(skill)
            {
                case "Axes":return Family.Axes;
                case "Knives":return Family.Knives;
                case "Clubs":return Family.Maces;
                case "Swords":return Family.Swords;
                default:return Family.None;
            }
        }
        // The game's own dual weapon whose move set and stance a pair borrows.
        internal static string Template(Family family)=>family==Family.Knives?"KnifeSkollAndHati":family==Family.None?null:"AxeBerzerkr";

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
