using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GraveGoods
{
    // Grave goods: the weapons and war-gear the dead were buried with. Pure rules shared with the standalone tests: no Unity
    // or game types. Values for percentages are fractions (0.15 is 15%).
    internal enum Rarity{Plain,Fine,Runed,Heirloom,Saga}

    [Flags]
    internal enum Slot{Weapon=1,Armor=2,Shield=4,Any=7}

    internal sealed class Affix
    {
        internal string Id;
        internal double Value;
        internal string Arg="";      // the skill for a skill affix
        internal Affix(string id,double value,string arg=""){Id=id;Value=value;Arg=arg??"";}
    }

    // What makes one buried thing more than its kind: how rare it is, from what depth of the world, its own name, its powers.
    internal sealed class Goods
    {
        internal Rarity Rarity;
        internal int Tier;
        internal string Name="";     // a heirloom's or saga-weapon's own name ("" for plain, fine and runed: named from the powers)
        internal string Saga="";     // a saga weapon's line of lore
        internal readonly List<Affix> Affixes=new List<Affix>();
        internal double Sum(string id)=>Affixes.Where(a=>a.Id==id).Sum(a=>a.Value);
    }

    internal sealed class AffixDef
    {
        internal string Id,Prefix,Suffix,Text;
        internal Slot Slots;
        internal double Min,Max;
        internal bool PerTier;       // flat amounts that grow with the tier (health, stamina, eitr)
        internal bool Whole;         // shown and kept as a whole number
        internal int MinTier=1;
        internal double Weight=1;
        internal string Group="";    // only one affix of a group on an item (one element, say)
    }

    internal sealed class SagaDef
    {
        internal string Name,Lore,Class; // Class: a weapon skill, or Helmet/Chest/Legs/Shoulder/Shield
        internal Affix[] Fixed;
    }

    internal static class Policy
    {
        internal const string Key="bob_gravegoods";
        internal const int MaxTier=7;

        internal static readonly string[] RarityNames={"Plain","Fine","Runed","Heirloom","Saga"};
        internal static readonly string[] RarityColors={"#d8d2c4","#5fd35f","#4f9dff","#c070ff","#ff9a2e"};
        internal static readonly int[] AffixCount={0,1,2,3,2}; // (a saga weapon's own powers come on top)
        // How strong the powers roll, low and high end of the range, by rarity.
        private static readonly double[] RollLow={0,0,0.2,0.45,0.6},RollHigh={0,0.7,0.85,1,1};

        internal static readonly string[] Elements={"fire","frost","lightning","poison","spirit"};
        internal static readonly string[] ArmorSkills={"Run","Jump","Sneak","Swim","Blocking","Swords","Axes","Clubs","Spears","Polearms","Knives","Bows","Unarmed","Crossbows","ElementalMagic","BloodMagic"};

        internal static readonly AffixDef[] Defs=
        {
            new AffixDef{Id="dmg",Slots=Slot.Weapon,Min=0.08,Max=0.22,Prefix="Keen",Suffix="of Slaughter",Text="+{0} damage",Weight=1.4},
            new AffixDef{Id="fire",Slots=Slot.Weapon,Min=0.10,Max=0.30,Prefix="Burning",Suffix="of Surtr",Text="+{0} of its damage as fire",Group="element"},
            new AffixDef{Id="frost",Slots=Slot.Weapon,Min=0.10,Max=0.30,Prefix="Rimed",Suffix="of Niflheim",Text="+{0} of its damage as frost",Group="element"},
            new AffixDef{Id="lightning",Slots=Slot.Weapon,Min=0.10,Max=0.30,Prefix="Thundering",Suffix="of Thor",Text="+{0} of its damage as lightning",Group="element"},
            new AffixDef{Id="poison",Slots=Slot.Weapon,Min=0.10,Max=0.30,Prefix="Venomous",Suffix="of Jörmungandr",Text="+{0} of its damage as poison",Group="element"},
            new AffixDef{Id="spirit",Slots=Slot.Weapon,Min=0.10,Max=0.30,Prefix="Hallowed",Suffix="of the Ancestors",Text="+{0} of its damage as spirit",Group="element",Weight=0.7},
            new AffixDef{Id="stam",Slots=Slot.Weapon,Min=0.10,Max=0.30,Prefix="Balanced",Suffix="of Ease",Text="-{0} stamina to swing"},
            new AffixDef{Id="leech",Slots=Slot.Weapon,Min=0.02,Max=0.06,Prefix="Blood-drinking",Suffix="of the Raven",Text="{0} of the damage it deals heals you",Weight=0.6},
            new AffixDef{Id="stagger",Slots=Slot.Weapon,Min=0.25,Max=0.60,Prefix="Crushing",Suffix="of the Troll",Text="+{0} stagger"},
            new AffixDef{Id="backstab",Slots=Slot.Weapon,Min=0.30,Max=0.90,Prefix="Sly",Suffix="of Loki",Text="+{0} sneak-attack damage",Weight=0.8},
            new AffixDef{Id="wskill",Slots=Slot.Weapon,Min=5,Max=12,Whole=true,Prefix="Practised",Suffix="of the Master",Text="+{0} {skill}",Weight=0.8},
            new AffixDef{Id="dura",Slots=Slot.Any,Min=0.40,Max=1.00,Prefix="Dverg-forged",Suffix="of the Dvergr",Text="+{0} durability",Weight=0.7},
            new AffixDef{Id="weight",Slots=Slot.Any,Min=0.30,Max=0.60,Prefix="Feather-light",Suffix="of the Wind",Text="-{0} weight",Weight=0.6},
            new AffixDef{Id="armor",Slots=Slot.Armor,Min=0.10,Max=0.35,Prefix="Stout",Suffix="of the Bear",Text="+{0} armour",Weight=1.4},
            new AffixDef{Id="block",Slots=Slot.Shield,Min=0.10,Max=0.35,Prefix="Stalwart",Suffix="of the Shieldwall",Text="+{0} block power",Weight=1.4},
            new AffixDef{Id="hp",Slots=Slot.Armor|Slot.Shield,Min=4,Max=9,PerTier=true,Whole=true,Prefix="Hale",Suffix="of Life",Text="+{0} health",Weight=1.2},
            new AffixDef{Id="stamina",Slots=Slot.Armor,Min=4,Max=9,PerTier=true,Whole=true,Prefix="Tireless",Suffix="of the Stag",Text="+{0} stamina"},
            new AffixDef{Id="eitr",Slots=Slot.Armor,Min=5,Max=12,PerTier=true,Whole=true,MinTier=5,Prefix="Witch-woven",Suffix="of Seiðr",Text="+{0} eitr",Weight=0.7},
            new AffixDef{Id="hregen",Slots=Slot.Armor,Min=0.10,Max=0.35,Prefix="Healing",Suffix="of Eir",Text="+{0} health regeneration"},
            new AffixDef{Id="sregen",Slots=Slot.Armor|Slot.Shield,Min=0.08,Max=0.25,Prefix="Restful",Suffix="of the Ox",Text="+{0} stamina regeneration"},
            new AffixDef{Id="speed",Slots=Slot.Armor,Min=0.03,Max=0.08,Prefix="Fleet",Suffix="of Sleipnir",Text="+{0} movement speed",Weight=0.7},
            new AffixDef{Id="carry",Slots=Slot.Armor,Min=15,Max=60,Whole=true,Prefix="Broad-backed",Suffix="of the Lox",Text="+{0} carry weight",Weight=0.7},
            new AffixDef{Id="rfire",Slots=Slot.Armor|Slot.Shield,Min=0.10,Max=0.35,Prefix="Ember-warded",Suffix="of Embers",Text="-{0} fire damage taken",Weight=0.5},
            new AffixDef{Id="rfrost",Slots=Slot.Armor|Slot.Shield,Min=0.10,Max=0.35,Prefix="Frost-warded",Suffix="of the Hearth",Text="-{0} frost damage taken",Weight=0.5},
            new AffixDef{Id="rpoison",Slots=Slot.Armor|Slot.Shield,Min=0.10,Max=0.35,Prefix="Venom-warded",Suffix="of the Healer",Text="-{0} poison damage taken",Weight=0.5},
            new AffixDef{Id="rlightning",Slots=Slot.Armor|Slot.Shield,Min=0.10,Max=0.35,Prefix="Storm-warded",Suffix="of the Oak",Text="-{0} lightning damage taken",Weight=0.5},
            new AffixDef{Id="rspirit",Slots=Slot.Armor|Slot.Shield,Min=0.10,Max=0.35,Prefix="Spirit-warded",Suffix="of the Völva",Text="-{0} spirit damage taken",Weight=0.4},
            new AffixDef{Id="askill",Slots=Slot.Armor,Min=5,Max=12,Whole=true,Prefix="Skilled",Suffix="of the Wolf",Text="+{0} {skill}",Weight=0.9},
        };
        internal static AffixDef Def(string id)=>Defs.FirstOrDefault(d=>d.Id==id);

        // The weapons and war-gear of the sagas, for the rarest finds: their own powers, and two more of chance.
        internal static readonly SagaDef[] Sagas=
        {
            new SagaDef{Name="Tyrfing",Class="Swords",Lore="Forged by dwarves under duress and cursed: it must kill each time it is drawn.",Fixed=new[]{new Affix("dmg",0.40),new Affix("leech",0.05),new Affix("hp",-15)}},
            new SagaDef{Name="Gram",Class="Swords",Lore="Sigurd's sword, that clove the anvil and slew Fáfnir.",Fixed=new[]{new Affix("dmg",0.30),new Affix("stagger",0.60)}},
            new SagaDef{Name="Skofnung",Class="Swords",Lore="Hrólf Kraki's sword: its wounds heal only with its own stone.",Fixed=new[]{new Affix("leech",0.05),new Affix("wskill",12,"Swords")}},
            new SagaDef{Name="Hrunting",Class="Swords",Lore="Unferth's sword, tempered in venom-twigs, that never failed in battle.",Fixed=new[]{new Affix("poison",0.30),new Affix("dmg",0.20)}},
            new SagaDef{Name="Kvernbítr",Class="Swords",Lore="Quern-biter: Haakon the Good's sword, that clove a millstone to the eye.",Fixed=new[]{new Affix("dmg",0.35),new Affix("dura",0.8)}},
            new SagaDef{Name="Rimmugýgr",Class="Axes",Lore="Battle-troll-woman: Skarphéðinn's axe, in Njál's saga.",Fixed=new[]{new Affix("backstab",0.9),new Affix("dmg",0.25)}},
            new SagaDef{Name="Hel",Class="Axes",Lore="King Olaf's axe, that he named for the queen of the dead.",Fixed=new[]{new Affix("spirit",0.30),new Affix("dmg",0.20)}},
            new SagaDef{Name="Gunnar's Atgeir",Class="Polearms",Lore="It sang out before a killing, so loud it was heard far off.",Fixed=new[]{new Affix("dmg",0.30),new Affix("wskill",12,"Polearms")}},
            new SagaDef{Name="Grásíða",Class="Spears",Lore="Grey-side: a sword broken and reforged as a spear, in Gísli's saga.",Fixed=new[]{new Affix("dmg",0.30),new Affix("dura",0.8)}},
            new SagaDef{Name="Fótbítr",Class="Knives",Lore="Leg-biter: a short, wicked blade, at its best from behind.",Fixed=new[]{new Affix("backstab",0.9),new Affix("dmg",0.25)}},
            new SagaDef{Name="Hausakljúfr",Class="Clubs",Lore="Skull-splitter: its last owner's name, and his habit.",Fixed=new[]{new Affix("stagger",0.8),new Affix("dmg",0.30)}},
            new SagaDef{Name="Gusisnautar",Class="Bows",Lore="Gusir's gifts: Örvar-Odd's arrows, won from the king of the Finns. They always flew home.",Fixed=new[]{new Affix("dmg",0.35),new Affix("wskill",12,"Bows")}},
            new SagaDef{Name="Sindri's Work",Class="Crossbows",Lore="Work of Sindri, the dwarf who forged Draupnir and the golden boar.",Fixed=new[]{new Affix("dmg",0.30),new Affix("dura",1.0)}},
            new SagaDef{Name="Gambanteinn",Class="ElementalMagic",Lore="The wand of power Skírnir carried into Jötunheim.",Fixed=new[]{new Affix("dmg",0.35),new Affix("lightning",0.25)}},
            new SagaDef{Name="Svalinn",Class="Shield",Lore="The shield that stands before the sun; without it, the mountains and seas would burn.",Fixed=new[]{new Affix("rfire",0.35),new Affix("block",0.30)}},
            new SagaDef{Name="Ægishjálmur",Class="Helmet",Lore="The Helm of Awe, from Fáfnir's lair: all who saw it feared its wearer.",Fixed=new[]{new Affix("armor",0.30),new Affix("hp",12)}},
            new SagaDef{Name="Hildigöltr",Class="Helmet",Lore="Battle-boar: King Aðils' helm, won from the Swedes.",Fixed=new[]{new Affix("armor",0.30),new Affix("rfrost",0.30)}},
            new SagaDef{Name="The Golden Byrnie",Class="Chest",Lore="Fáfnir's own mail, from the hoard on Gnitaheath.",Fixed=new[]{new Affix("armor",0.35),new Affix("rpoison",0.35)}},
            new SagaDef{Name="Hermóðr's Leggings",Class="Legs",Lore="Worn on the nine nights' ride to Hel and back.",Fixed=new[]{new Affix("speed",0.08),new Affix("sregen",0.25)}},
            new SagaDef{Name="Valshamr",Class="Shoulder",Lore="Freyja's falcon cloak: whoever wears it goes as the birds go.",Fixed=new[]{new Affix("speed",0.08),new Affix("stamina",12)}},
        };

        // ---- rolling ----

        // How deep in the world a chest is, from its biome: what its dead were buried with.
        internal static int BiomeTier(string biome)
        {
            switch(biome)
            {
                case "Meadows":return 1;
                case "BlackForest":return 2;
                case "Swamp":case "Ocean":return 3;
                case "Mountain":return 4;
                case "Plains":return 5;
                case "Mistlands":return 6;
                case "AshLands":case "DeepNorth":return 7;
                default:return 1;
            }
        }
        // Mostly the chest's own tier; now and then one below, rarely one above.
        internal static int ChestTier(int biomeTier,Random rng)
        {
            double r=rng.NextDouble();
            int t=r<0.7?biomeTier:r<0.9?biomeTier-1:biomeTier+1;
            return Math.Max(1,Math.Min(MaxTier,t));
        }
        internal static Rarity RollRarity(double[] weights,Random rng,int bump=0)
        {
            double total=weights.Sum(),r=rng.NextDouble()*total;
            int i=0;
            for(;i<weights.Length-1;i++){if(r<weights[i])break;r-=weights[i];}
            return (Rarity)Math.Min((int)Rarity.Saga,i+bump);
        }
        internal static int RollQuality(Rarity rarity,int maxQuality,Random rng)
        {
            int lo,hi;
            switch(rarity)
            {
                case Rarity.Plain:lo=1;hi=2;break;
                case Rarity.Fine:lo=1;hi=3;break;
                case Rarity.Runed:lo=2;hi=3;break;
                case Rarity.Heirloom:lo=3;hi=4;break;
                default:lo=4;hi=4;break;
            }
            hi=Math.Max(1,Math.Min(hi,maxQuality));lo=Math.Min(lo,hi);
            return rng.Next(lo,hi+1);
        }

        // The powers of one find. weaponSkill: the item's own skill (for a weapon's skill power and to pick a saga weapon);
        // armorClass: Helmet/Chest/Legs/Shoulder/Shield for gear.
        // taken: sagas already found in this world (each comes once, until every one of its kind has).
        internal static Goods Roll(Rarity rarity,int tier,Slot slot,string weaponSkill,string armorClass,Random rng,Func<string,bool> taken=null)
        {
            var g=new Goods{Rarity=rarity,Tier=Math.Max(1,Math.Min(MaxTier,tier))};
            if(rarity==Rarity.Saga)
            {
                string cls=slot==Slot.Weapon?weaponSkill:armorClass;
                SagaDef[] fits=Sagas.Where(s=>s.Class==cls).ToArray();
                if(taken!=null&&fits.Any(s=>!taken(s.Name)))fits=fits.Where(s=>!taken(s.Name)).ToArray();
                if(fits.Length>0)
                {
                    SagaDef s=fits[rng.Next(fits.Length)];
                    g.Name=s.Name;g.Saga=s.Lore;
                    foreach(Affix a in s.Fixed)g.Affixes.Add(new Affix(a.Id,Scale(Def(a.Id),a.Value,g.Tier),a.Arg));
                }
                else{g.Rarity=Rarity.Heirloom;g.Name=Kenning(slot,rng);} // (nothing in the sagas of its kind: the best heirloom)
            }
            else if(rarity==Rarity.Heirloom)g.Name=Kenning(slot,rng);
            int want=AffixCount[(int)rarity]+(g.Rarity==Rarity.Heirloom&&rarity==Rarity.Saga?1:0);
            for(int n=0;n<want;n++)
            {
                AffixDef d=Pick(g,slot,rng);
                if(d==null)break;
                double u=RollLow[(int)rarity]+(RollHigh[(int)rarity]-RollLow[(int)rarity])*rng.NextDouble();
                double v=Scale(d,d.Min+(d.Max-d.Min)*u,g.Tier);
                string arg=d.Id=="wskill"?weaponSkill:d.Id=="askill"?ArmorSkills[rng.Next(ArmorSkills.Length)]:"";
                g.Affixes.Add(new Affix(d.Id,v,arg));
            }
            return g;
        }
        private static double Scale(AffixDef d,double v,int tier)
        {
            if(d==null)return v;
            if(d.PerTier)v*=1+0.5*(tier-1);
            return d.Whole?Math.Round(v):Math.Round(v,3);
        }
        // A power the item doesn't have yet, by weight, fitting its kind and depth, one of a group at most.
        private static AffixDef Pick(Goods g,Slot slot,Random rng)
        {
            var have=new HashSet<string>(g.Affixes.Select(a=>a.Id));
            var groups=new HashSet<string>(g.Affixes.Select(a=>Def(a.Id)?.Group??"").Where(s=>s!=""));
            AffixDef[] fit=Defs.Where(d=>(d.Slots&slot)!=0&&g.Tier>=d.MinTier&&!have.Contains(d.Id)&&(d.Group==""||!groups.Contains(d.Group))).ToArray();
            if(fit.Length==0)return null;
            double total=fit.Sum(d=>d.Weight),r=rng.NextDouble()*total;
            foreach(AffixDef d in fit){if(r<d.Weight)return d;r-=d.Weight;}
            return fit[fit.Length-1];
        }

        // How a saga is remembered in a world's keys (plain letters: a world key can't hold spaces).
        internal static string SagaKey(string name)=>"bob_gravegoods_saga_"+new string(name.Normalize(NormalizationForm.FormD).Where(char.IsLetterOrDigit).Where(c=>c<128).ToArray()).ToLowerInvariant();

        // An heirloom's name, a kenning as the skalds made them.
        private static readonly string[] WeaponHead={"Wolf's","Raven's","Wound","Battle","Blood","Skull","Shield","Leg","Mail","Helm","Corpse","Grave"};
        private static readonly string[] WeaponTail={"Tooth","Biter","Feeder","Fire","Icicle","Wand","Snake","Breaker","Gnawer","Sorrow","Thorn","Glede"};
        private static readonly string[] ArmorHead={"Storm","Iron","Bear","Elk","Ash","Hearth","Night","Frost","Grave","Sea","Wolf","Barrow"};
        private static readonly string[] ArmorTail={"Shirt","Coat","Hide","Ward","Cowl","Weave","Skin","Byrnie"};
        private static readonly string[] ShieldHead={"Battle","Sun","Linden","Spear","Arrow","Hail","Sword","War"};
        private static readonly string[] ShieldTail={"Moon","Wall","Board","Roof","Wheel","Cloud"};
        internal static string Kenning(Slot slot,Random rng)
        {
            string[] head=slot==Slot.Weapon?WeaponHead:slot==Slot.Shield?ShieldHead:ArmorHead;
            string[] tail=slot==Slot.Weapon?WeaponTail:slot==Slot.Shield?ShieldTail:ArmorTail;
            string h=head[rng.Next(head.Length)],t=tail[rng.Next(tail.Length)];
            return h.EndsWith("'s")?h+" "+t:h+"-"+t.ToLowerInvariant();
        }

        // ---- names and text ----

        // What it's called: a heirloom or saga weapon by its own name; others by their kind with their powers around it.
        internal static string Title(Goods g,string baseName)
        {
            if(g.Name!="")return g.Name;
            AffixDef first=g.Affixes.Count>0?Def(g.Affixes[0].Id):null,second=g.Affixes.Count>1?Def(g.Affixes[1].Id):null;
            if(first==null)return baseName;
            if(second==null)return first.Prefix+" "+baseName;
            return first.Prefix+" "+baseName+" "+second.Suffix;
        }
        internal static string Color(Rarity r)=>RarityColors[(int)r];
        internal static string Line(Affix a)
        {
            AffixDef d=Def(a.Id);
            if(d==null)return "";
            string amount=d.Whole?((int)Math.Round(Math.Abs(a.Value))).ToString(CultureInfo.InvariantCulture):Math.Round(Math.Abs(a.Value)*100).ToString(CultureInfo.InvariantCulture)+"%";
            string text=d.Text.Replace("{skill}",SkillName(a.Arg)).Replace("{0}",amount);
            if(a.Value<0)text=text.StartsWith("+")?"-"+text.Substring(1):text.StartsWith("-")?"+"+text.Substring(1):text; // (a curse)
            return text;
        }
        internal static string SkillName(string skill)
        {
            switch(skill)
            {
                case "ElementalMagic":return "Elemental Magic";
                case "BloodMagic":return "Blood Magic";
                case "Polearms":return "Polearms";
                default:return skill??"";
            }
        }

        // ---- keeping it on the item ----

        // 1|rarity|tier|name|saga|id:value:arg,... (texts escaped as in a URL)
        internal static string Save(Goods g)
        {
            var sb=new StringBuilder("1|");
            sb.Append((int)g.Rarity).Append('|').Append(g.Tier).Append('|').Append(Clean(g.Name)).Append('|').Append(Clean(g.Saga)).Append('|');
            sb.Append(string.Join(",",g.Affixes.Select(a=>a.Id+":"+a.Value.ToString("R",CultureInfo.InvariantCulture)+":"+Clean(a.Arg))));
            return sb.ToString();
        }
        internal static Goods Load(string s)
        {
            if(string.IsNullOrEmpty(s))return null;
            string[] f=s.Split('|');
            if(f.Length<6||f[0]!="1")return null;
            if(!int.TryParse(f[1],out int r)||!int.TryParse(f[2],out int t))return null;
            var g=new Goods{Rarity=(Rarity)Math.Max(0,Math.Min(4,r)),Tier=t,Name=Unclean(f[3]),Saga=Unclean(f[4])};
            foreach(string part in f[5].Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p=part.Split(':');
                if(p.Length<2||Def(p[0])==null||!double.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double v))continue;
                g.Affixes.Add(new Affix(p[0],v,p.Length>2?Unclean(p[2]):""));
            }
            return g;
        }
        private static string Clean(string s)=>Uri.EscapeDataString(s??"");
        private static string Unclean(string s){try{return Uri.UnescapeDataString(s??"");}catch(Exception){return "";}}

        // ---- materials: how deep in the world a recipe's ingredients come from ----

        private static readonly Dictionary<string,int> Materials=new Dictionary<string,int>
        {
            ["Wood"]=1,["Stone"]=1,["Flint"]=1,["LeatherScraps"]=1,["DeerHide"]=1,["Resin"]=1,["BoneFragments"]=1,["Feathers"]=1,["Raspberry"]=1,
            ["Copper"]=2,["Tin"]=2,["Bronze"]=2,["TrollHide"]=2,["RoundLog"]=2,["FineWood"]=2,["GreydwarfEye"]=2,["AncientSeed"]=2,["BronzeNails"]=2,
            ["Iron"]=3,["ElderBark"]=3,["Chain"]=3,["Root"]=3,["Guck"]=3,["Bloodbag"]=3,["Ooze"]=3,["IronNails"]=3,["SerpentScale"]=3,["Chitin"]=3,["WitheredBone"]=3,
            ["Silver"]=4,["WolfPelt"]=4,["WolfFang"]=4,["WolfClaw"]=4,["DragonTear"]=4,["FreezeGland"]=4,["Obsidian"]=4,["Crystal"]=4,["JuteRed"]=4,["TrophyCultist"]=4,
            ["BlackMetal"]=5,["LinenThread"]=5,["Needle"]=5,["LoxPelt"]=5,["Tar"]=5,["YagluthDrop"]=5,["TrophyGoblinShaman"]=5,
            ["Carapace"]=6,["BlackCore"]=6,["Eitr"]=6,["ScaleHide"]=6,["Sap"]=6,["Mandible"]=6,["JuteBlue"]=6,["YggdrasilWood"]=6,["BlackMarble"]=6,["Wisp"]=6,["DvergrNeedle"]=6,["QueenDrop"]=6,["Bilebag"]=6,["RoyalJelly"]=6,
            ["Flametal"]=7,["FlametalNew"]=7,["AskHide"]=7,["MorgenSinew"]=7,["CharcoalResin"]=7,["Grausten"]=7,["CelestialFeather"]=7,["GemstoneRed"]=7,["GemstoneBlue"]=7,["GemstoneGreen"]=7,["MoltenCore"]=7,["CharredBone"]=7,["Blackwood"]=7,["BellFragment"]=7,["MorgenHeart"]=7,["FlametalOre"]=7,
        };
        // A recipe's tier: its deepest known ingredient (0: none known, so not offered).
        internal static int RecipeTier(IEnumerable<string> ingredients)
        {
            int t=0;
            foreach(string i in ingredients)if(i!=null&&Materials.TryGetValue(i,out int m))t=Math.Max(t,m);
            return t;
        }
    }
}
