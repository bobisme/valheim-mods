using System;
using System.Collections.Generic;
using System.Linq;

namespace TrophyHall
{
    // A themed perk a kind of trophy gives the hall it hangs in.
    internal enum Perk { None, Carry, Poison, Stamina, Swim, Woodcutting, Health, Speed }

    internal sealed class Theme
    {
        internal Perk Perk;internal string Plural,Description;internal string[] Trophies;
    }

    // Pure rules shared with the standalone tests: no Unity or game types.
    internal static class Policy
    {
        internal const int MaxThemes=6;
        // Rarest first: when more than MaxThemes kinds hang in one hall, the commonest drop out.
        internal static readonly Theme[] Themes=
        {
            new Theme{Perk=Perk.Carry,Plural="trolls",Description="+30 carry weight",Trophies=new[]{"TrophyForestTroll","TrophyFrostTroll"}},
            new Theme{Perk=Perk.Poison,Plural="draugr",Description="slight poison resistance",Trophies=new[]{"TrophyDraugr","TrophyDraugrElite","TrophyDraugrFem"}},
            new Theme{Perk=Perk.Stamina,Plural="wolves",Description="+10% stamina regeneration",Trophies=new[]{"TrophyWolf","TrophyUlv","TrophyFenring"}},
            new Theme{Perk=Perk.Swim,Plural="sea beasts",Description="+10% swim speed",Trophies=new[]{"TrophyNeck","TrophySerpent"}},
            new Theme{Perk=Perk.Woodcutting,Plural="greydwarfs",Description="+10% woodcutting skill gain",Trophies=new[]{"TrophyGreydwarf","TrophyGreydwarfBrute","TrophyGreydwarfShaman"}},
            new Theme{Perk=Perk.Health,Plural="boars",Description="+10% health regeneration",Trophies=new[]{"TrophyBoar"}},
            new Theme{Perk=Perk.Speed,Plural="deer",Description="+5% movement speed",Trophies=new[]{"TrophyDeer","TrophyDeerWhite"}},
        };
        // Boss trophies belong on their altars, where the game uses them for Forsaken powers.
        internal static readonly HashSet<string> Bosses=new HashSet<string>{"TrophyEikthyr","TrophyTheElder","TrophyBonemass","TrophyDragonQueen",
            "TrophyGoblinKing","TrophySeekerQueen","TrophyFader"};

        internal static Theme ThemeOf(string trophy)=>Themes.FirstOrDefault(t=>t.Trophies.Contains(trophy));
        internal static bool Counts(string prefab,bool isTrophy)=>isTrophy&&!string.IsNullOrEmpty(prefab)&&!Bosses.Contains(prefab);

        // Each kind counts once; at most MaxThemes perks, rarest first.
        internal static List<Theme> Perks(IEnumerable<string> trophies)
        {
            var have=new HashSet<string>(trophies??Enumerable.Empty<string>());
            return Themes.Where(t=>t.Trophies.Any(have.Contains)).Take(MaxThemes).ToList();
        }
        // Comfort from the hall as a whole: +1 from four distinct trophies, +2 from eight.
        internal static int Comfort(int distinctTrophies)=>distinctTrophies>=8?2:distinctTrophies>=4?1:0;

        // "Trolls, wolves and draugr have fallen here."
        internal static string Fallen(IList<Theme> perks)
        {
            var names=perks.Select(p=>p.Plural).ToList();
            if(names.Count==0)return "";
            string list=names.Count==1?names[0]:string.Join(", ",names.Take(names.Count-1))+" and "+names[names.Count-1];
            return char.ToUpperInvariant(list[0])+list.Substring(1)+" have fallen here.";
        }
    }
}
